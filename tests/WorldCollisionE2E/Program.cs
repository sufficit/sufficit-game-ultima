using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static BrowserTest;

try
{
    var scenario = args.FirstOrDefault() ?? "collision";
    if (scenario != "remote-performance" && args.Length > 1)
        throw new ArgumentException("Informe apenas um cenário.");
    var browserArgs = (Environment.GetEnvironmentVariable("ULTIMA_CHROMIUM_ARGS") ?? "")
        .Split(' ', StringSplitOptions.RemoveEmptyEntries);
    browserArgs = scenario switch
    {
        "remote-performance" when RemotePerformance.UsesSwiftShader => RemotePerformance.SwiftShaderArgs,
        "hud-status" when browserArgs.Length == 0 => HudStatus.DefaultBrowserArgs,
        _ => browserArgs
    };
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await playwright.Chromium.LaunchAsync(new()
    {
        ExecutablePath = Environment.GetEnvironmentVariable("ULTIMA_CHROMIUM"),
        Args = browserArgs
    });
    switch (scenario)
    {
        case "collision":
            var size = (Environment.GetEnvironmentVariable("ULTIMA_VIEWPORT") ?? "1200x720").Split('x');
            var page = await browser.NewPageAsync(new()
            {
                ViewportSize = new() { Width = int.Parse(size[0]), Height = int.Parse(size[1]) }
            });
            await new WorldCollision(page).RunAsync();
            break;
        case "step":
            await ArticulatedStep.RunAsync(browser);
            break;
        case "clock":
            await new WorldClock(browser).RunAsync();
            break;
        case "backpack":
            await new Backpack(browser).RunAsync();
            break;
        case "camera-streaming":
            await CameraStreaming.RunAsync(browser);
            break;
        case "terrain-cache":
            await TerrainCache.RunAsync(browser);
            break;
        case "terrain-seams":
            await TerrainSeams.RunAsync(browser);
            break;
        case "garden":
            await Garden.RunAsync(browser);
            break;
        case "tunnel":
            await Tunnel.RunAsync(browser);
            break;
        case "world-undo":
            await WorldUndo.RunAsync(browser);
            break;
        case "graphics":
            await Graphics.RunAsync(browser);
            break;
        case "crowd":
            await Crowd.RunAsync(browser);
            break;
        case "remote-performance":
            await RemotePerformance.RunAsync(browser, args[1..]);
            break;
        case "declarative-windows":
            await DeclarativeWindows.RunAsync(browser);
            break;
        case "hud-status":
            await HudStatus.RunAsync(browser);
            break;
        default:
            throw new ArgumentException("Cenários disponíveis: collision, step, clock, backpack, camera-streaming, terrain-cache, terrain-seams, garden, tunnel, world-undo, graphics, crowd, remote-performance, declarative-windows, hud-status.");
    }
    return 0;
}
catch (Exception failure)
{
    Console.Error.WriteLine(failure);
    return 1;
}

internal sealed partial class WorldCollision(IPage page)
{
    private static readonly double WaitScale = double.TryParse(Environment.GetEnvironmentVariable("ULTIMA_WALK_SCALE"),
        CultureInfo.InvariantCulture, out var scale) && scale > 0 ? scale : 1;
    private static readonly int WaitTries = int.TryParse(Environment.GetEnvironmentVariable("ULTIMA_WAIT_TRIES"),
        out var tries) && tries > 0 ? tries : 100;

    private readonly ConcurrentQueue<string> _errors = new();
    private IWorker? _worker;
    private static readonly WorldEdit Tree = new("place", "browser-tree", "flora.pine", 0, 2, 0, 1);

    internal async Task RunAsync()
    {
        page.PageError += (_, error) => _errors.Enqueue(error);
        await page.GotoAsync(GameUrl(), new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var build = await CheckBuildAsync(page);
        await WaitAsync(async () =>
        {
            var world = await ReadAsync<WorldState?>(page, """
                async () => {
                    if (!window.game?.glb?.status || !window.game?.build?.state) return null;
                    return { map: await game.build.state(), glb: await game.glb.status(), models: await game.models.status() };
                }
                """);
            return world is { Map.Chunks: >= 25, Glb: not null, Models.Queue: 0 } &&
                new[] { "flora.pine", "prop.stump", "prop.log", "rock.boulder2" }.All(prefix =>
                    world.Glb.Loaded.Any(id => id.StartsWith(prefix, StringComparison.Ordinal))) &&
                world.Models.Loaded.Contains("character.villager v2");
        }, "mundo carregado");
        Check(await page.EvaluateAsync<string>("() => game.mode") == Mode, "modo do renderizador", Mode);
        if (!MainThread) _worker = page.Workers[0];

        var probes = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "scene-probes.js"));
        await RenderAsync<bool>($"() => {probes}");
        await page.GetByRole(AriaRole.Button, new() { Name = "detalhes", Exact = true }).ClickAsync();
        await page.Locator("#game-canvas").FocusAsync();
        await ContactsAsync();
        await GaitAsync();
        await TerrainAsync(build);
        await RemoteAsync();
        CheckPageErrors();
    }

    private Task<T> RenderAsync<T>(string expression, object? arg = null) => ReadAsync<T>(page, expression, arg, _worker);

    private Task<T> Probe<T>(string name, object? arg = null) =>
        RenderAsync<T>($"arg => globalThis.ultimaCollisionProbes.{name}(arg)", arg);

    private static async Task WaitAsync(Func<Task<bool>> condition, string description)
    {
        for (var attempt = 0; attempt < WaitTries; attempt++)
        {
            if (await condition()) return;
            await Task.Delay(250);
        }
        throw new InvalidOperationException(description);
    }

    private void CheckPageErrors() => Check(_errors.IsEmpty, "erros no navegador", _errors);
    private static Task SettleAsync(int milliseconds = 300) => Task.Delay((int)Math.Round(milliseconds * WaitScale));
    private Task ResetAsync() => page.EvaluateAsync("() => game.resetPlayer()");
    private Task ApplyAsync(object edit) =>
        page.EvaluateAsync("op => game.applyWorldOp(JSON.parse(op))", JsonSerializer.Serialize(edit, Json));
    private Task RemoveAsync(string uid) => ApplyAsync(new { kind = "remove", uid });
    private Task AddRemoteAsync(string id, string name, double x, double z) => page.EvaluateAsync(
        "p => { game.addRemote(p.id, p.name); game.updateRemote(p.id, p.x, p.z, 0); }", new { id, name, x, z });
    private Task MoveRemoteAsync(string id, double x, double z) =>
        page.EvaluateAsync("p => game.updateRemote(p.id, p.x, p.z, 0)", new { id, x, z });
    private Task RemoveRemoteAsync(string id) => page.EvaluateAsync("id => game.removeRemote(id)", id);

    private async Task<Position> PositionAsync()
    {
        if (MainThread) return await Probe<Position>("position");
        var status = await page.Locator(".ultima-status").InnerTextAsync();
        var match = Regex.Match(status, @"pos \((-?[\d.,]+), (-?[\d.,]+)\) · alt (-?[\d.,]+)");
        Check(match.Success, "posição no HUD", status);
        double Read(int group) => double.Parse(match.Groups[group].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        return new(Read(1), Read(3), Read(2));
    }

    private Task AdvanceAsync(double seconds) => Probe<JsonElement>("advance", seconds);

    private async Task WithKeysAsync(IEnumerable<string> keys, bool sprint, Func<Task> action)
    {
        var pressed = new List<string>();
        try
        {
            foreach (var key in sprint ? keys.Prepend("Shift") : keys)
            {
                await page.Keyboard.DownAsync(key);
                pressed.Add(key);
            }
            await action();
        }
        finally
        {
            foreach (var key in pressed.Where(key => key != "Shift"))
                await page.Keyboard.UpAsync(key);
            if (pressed.Contains("Shift"))
                await page.Keyboard.UpAsync("Shift");
        }
    }

    private async Task<Position> WalkAsync(string[] keys, int ms, bool sprint = true)
    {
        await WithKeysAsync(keys, sprint, () => AdvanceAsync(ms / 1000d));
        await AdvanceAsync(.3);
        CheckPageErrors();
        return await PositionAsync();
    }

    private async Task<Position> TraverseAsync(string key, double budget, string axis, double target)
    {
        Position? end = null;
        await WithKeysAsync([key], false, async () =>
            end = await Probe<Position>("traverse", new { budget, axis, target }));
        await SettleAsync();
        return end ?? throw new InvalidOperationException("percurso sem amostra");
    }
}

internal sealed record Position(double X, double Y, double Z);
internal sealed record WorldState(MapState? Map, AssetsState? Glb, ModelsState? Models);
internal sealed record MapState(int Chunks);
internal sealed record AssetsState(string[] Loaded);
internal sealed record ModelsState(int Queue, string[] Loaded);
internal sealed record WorldEdit(string Kind, string Uid, string Asset, double X, double Z, double Rot, double Scale);
