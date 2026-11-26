using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static BrowserTest;

internal static partial class RemotePerformance
{
    internal static readonly string[] SwiftShaderArgs =
        ["--use-gl=angle", "--use-angle=swiftshader", "--enable-unsafe-swiftshader"];
    internal static bool UsesSwiftShader => string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ULTIMA_CHROMIUM_ARGS"));

    private const int Samples = 90;
    private const int Warmup = 36;
    private const int RemoteTime = 0, WasmTime = 1, CallCount = 2, RenderTime = 3, FrameInterval = 4;

    internal static async Task RunAsync(IBrowser browser, string[] builds)
    {
        Check(builds.Length == 2, "informe as URLs base de referência e candidata");
        var probe = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "remote-performance-probe.js"));
        foreach (var url in builds)
        {
            var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 640, Height = 360 } });
            try { await CollectAsync(page, url, probe); }
            finally
            {
                await page.UnrouteAllAsync(new() { Behavior = UnrouteBehavior.IgnoreErrors });
                await page.CloseAsync();
            }
        }
    }

    private static async Task CollectAsync(IPage page, string url, string probe)
    {
        var errors = TrackErrors(page);
        await page.RouteAsync("**/js/game-core.js*", InstrumentAsync);
        var root = new UriBuilder(url)
        {
            Query = "live=0&rede=0&gpu=0&api=http%3A%2F%2F127.0.0.1%3A19999"
        };
        await page.GotoAsync(root.Uri.AbsoluteUri, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ULTIMA_BUILD"))) await CheckBuildAsync(page);
        await WaitAsync(async () =>
        {
            Check(errors.IsEmpty, "the instrumented render loop starts without errors", errors);
            var world = await ReadAsync<RemoteWorld?>(page, """
                async () => {
                    if (window.game?.mode !== 'worker' || !game.models?.status) return null;
                    const [models, assets] = await Promise.all([game.models.status(), game.glb.status()]);
                    return { models, assets, map: await game.build.state() };
                }
                """);
            return world is { Models.Queue: 0, Assets.Queued: > 0, Map.Chunks: >= 25 } &&
                world.Assets.Loaded.Length == world.Assets.Queued &&
                world.Models.Loaded.Any(id => id.StartsWith("character.wanderer", StringComparison.Ordinal));
        }, "worker e modelos precisam estar prontos", 45_000);
        var worker = page.Workers[0];
        var renderer = await worker.EvaluateAsync<string>(
            "() => BABYLON.EngineStore.LastCreatedScene.getEngine().getGlInfo().renderer");
        Check(UsesSwiftShader ? Regex.IsMatch(renderer, "SwiftShader", RegexOptions.IgnoreCase) :
            !Regex.IsMatch(renderer, "SwiftShader|llvmpipe|software", RegexOptions.IgnoreCase),
            "renderizador esperado para a bancada", renderer);
        Log(new { url, renderer });

        await worker.EvaluateAsync<bool>("""
            count => new Promise(resolve => {
                const observable = BABYLON.EngineStore.LastCreatedScene.getEngine().onEndFrameObservable;
                let frames = 0;
                const hook = observable.add(() => {
                    if (++frames !== count) return;
                    observable.remove(hook);
                    resolve(true);
                });
            })
            """, Samples).WaitAsync(TimeSpan.FromSeconds(90));
        foreach (var count in new[] { 12, 32, 64 })
        {
            var avatars = Enumerable.Range(0, count).Select(i =>
            {
                var x = (i % 2 == 0 ? -7 : 34) + (i / 2 % 8) * 1.15;
                return new { id = $"perf-{i}", name = $"Avatar {i}", x, nextX = x + .8, z = 4 + i / 16 * 1.4 };
            }).ToArray();
            foreach (var moving in new[] { false, true })
            {
                var sample = await ReadAsync<RemoteSample>(page, probe,
                    new { avatars, moving, warmup = Warmup, samples = Samples }, worker);
                var rows = sample.Rows;
                Check(errors.IsEmpty, "erros no navegador", errors);
                Check(rows.Length == Samples, "90 quadros por cenário", rows.Length);
                Check(rows.All(row => row[CallCount] > 0), "instrumentação WASM registra chamadas");
                Log(new
                {
                    url, count, moving, samples = rows.Length,
                    companionsMs = Summarize(rows.Select(row => row[RemoteTime])),
                    wasmMs = Summarize(rows.Select(row => row[WasmTime])),
                    sceneAdapterMs = Summarize(rows.Select(row => Math.Max(0, row[RemoteTime] - row[WasmTime]))),
                    calls = rows[0][CallCount],
                    sample.RemoteShadowCasters, sample.EnabledAvatars,
                    renderLoopMs = Summarize(rows.Select(row => row[RenderTime])),
                    frameIntervalMs = Summarize(rows.Skip(1).Select(row => row[FrameInterval]))
                });
            }
        }
    }

    private static Timing Summarize(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return new(Math.Round(sorted.Average(), 3, MidpointRounding.AwayFromZero),
            Math.Round(sorted[(int)Math.Ceiling(sorted.Length * .95) - 1], 3, MidpointRounding.AwayFromZero));
    }

    private static async Task InstrumentAsync(IRoute route)
    {
        var response = await route.FetchAsync();
        var source = await response.TextAsync();
        var calls = WasmExportCalls();
        Check(calls.IsMatch(source), "exportações WASM encontradas para instrumentação");
        var body = calls.Replace(source, "measureRemoteExport(terrain.$1, ");
        body = ReplaceAnchor(body, "    function updateOtherCharacters(delta) {", """
                function measureRemoteExport(exported, ...args) {
                    const probe = globalThis.__remotePerf;
                    if (!probe) return exported(...args);
                    const start = performance.now();
                    const result = exported(...args);
                    probe.wasm += performance.now() - start;
                    probe.calls++;
                    return result;
                }
                function updateOtherCharacters(delta) {
            """);
        // Keep sample state at render-loop scope. The remote update may run inside an environment branch.
        body = ReplaceAnchor(body, "                const frameStart = performance.now();", """
                            const frameStart = performance.now();
                            const probe = globalThis.__remotePerf;
                            let remoteMs = 0;
            """);
        body = ReplaceAnchor(body, "                updateOtherCharacters(delta);", """
                            const remoteStart = probe && performance.now();
                            updateOtherCharacters(delta);
                            if (probe) remoteMs = performance.now() - remoteStart;
            """);
        body = ReplaceAnchor(body, "                framePerf(performance.now() - frameStart);", """
                            if (probe) {
                                const end = performance.now();
                                probe.rows.push([remoteMs, probe.wasm, probe.calls, end - frameStart, end - probe.previous]);
                                probe.previous = end;
                                probe.wasm = probe.calls = 0;
                            }
                            framePerf(performance.now() - frameStart);
            """);
        await route.FulfillAsync(new() { Response = response, Body = body });
    }

    private static string ReplaceAnchor(string source, string anchor, string replacement)
    {
        var index = source.IndexOf(anchor, StringComparison.Ordinal);
        Check(index >= 0, "ponto de instrumentação encontrado", anchor.Trim());
        return string.Concat(source.AsSpan(0, index), replacement, source.AsSpan(index + anchor.Length));
    }

    [GeneratedRegex(@"terrain\.(AnimateCompanion|UpdateRemoteAvatars)\(")]
    private static partial Regex WasmExportCalls();

    private sealed record RemoteWorld(ModelsState? Models, AssetQueue? Assets, MapState? Map);
    private sealed record AssetQueue(int Queued, string[] Loaded);
    private sealed record RemoteSample(double[][] Rows, int RemoteShadowCasters, int EnabledAvatars);
    private sealed record Timing(double Mean, double P95);
}
