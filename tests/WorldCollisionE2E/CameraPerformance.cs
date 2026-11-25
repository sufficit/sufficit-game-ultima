using Microsoft.Playwright;
using static BrowserTest;

internal static class CameraPerformance
{
    internal static async Task RunAsync(IBrowser browser, string[] builds)
    {
        Check(builds.Length == 2, "informe as URLs de referência e candidata");
        Sample? baseline = null;
        foreach (var url in builds)
        {
            var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1200, Height = 720 } });
            var errors = TrackErrors(page);
            try
            {
                await page.RouteAsync("**/js/game-core.js*", InstrumentAsync);
                var target = new UriBuilder(url)
                {
                    Query = "live=0&rede=0&render=main&gpu=0&api=http%3A%2F%2F127.0.0.1%3A19999"
                };
                await page.GotoAsync(target.Uri.AbsoluteUri);
                await WaitAsync(() => page.EvaluateAsync<bool>("""
                    async () => {
                        if (typeof globalThis.sampleCameraPerformance !== 'function') return false;
                        const [models, assets, terrain] = await Promise.all([
                            game.models.status(), game.glb.status(), game.build.state()
                        ]);
                        return models?.queue === 0 && assets?.queued > 0 &&
                            assets.loaded.length === assets.queued && terrain?.chunks >= 25;
                    }
                    """), "cena da câmera pronta");
                var sample = await ReadAsync<Sample>(page, "() => globalThis.sampleCameraPerformance()");
                Check(errors.IsEmpty, "erros no navegador", errors);
                if (baseline is not null)
                    Check(baseline.Obstacles == sample.Obstacles &&
                        baseline.Distances.Zip(sample.Distances).All(pair => Math.Abs(pair.First - pair.Second) < 1e-9) &&
                        baseline.Distances.Length == sample.Distances.Length, "mesmas distâncias de colisão nos dois builds");
                var times = sample.BatchMs.Order().ToArray();
                Log(new { url, sample.Obstacles, sample.Candidates, raysPerBatch = sample.Distances.Length,
                    rounds = times.Length, medianMs = times[times.Length / 2], sample.MeshMs, sample.TerrainMs });
                baseline = sample;
            }
            finally { await page.CloseAsync(); }
        }
    }

    private static async Task InstrumentAsync(IRoute route)
    {
        var response = await route.FetchAsync();
        var source = await response.TextAsync();
        const string anchor = "    function advanceCamera(delta, pad) {";
        Check(source.Contains(anchor), "ponto de medição da câmera disponível");
        var instrumented = source
            .Replace("        cameraRay.length = length;",
                "        cameraRay.length = length;\n        const meshStart = performance.now();")
            .Replace("        let distance = cameraRay.length;",
                "        const terrainStart = performance.now();\n        G.cameraMeshMs += terrainStart - meshStart;\n        let distance = cameraRay.length;")
            .Replace("        cameraTrace.from.copyFrom(from);",
                "        G.cameraTerrainMs += performance.now() - terrainStart;\n        cameraTrace.from.copyFrom(from);")
            .Replace(anchor, Probe + "\n" + anchor);
        await route.FulfillAsync(new() { Response = response, Body = instrumented });
    }

    private const string Probe = """
        G.sampleCameraPerformance = () => {
            const from = BABYLON.Vector3.Zero(), to = BABYLON.Vector3.Zero();
            const distances = [], batchMs = [];
            G.cameraMeshMs = G.cameraTerrainMs = 0;
            for (let run = 0; run < 6; run++) {
                const start = performance.now();
                for (let i = 0; i < 1800; i++) {
                    const angle = i * .087;
                    from.set((i % 11 - 5) * .3, 1.1, (i % 13 - 6) * .4);
                    to.set(from.x + 7 * Math.cos(angle), from.y + 1.6, from.z + 7 * Math.sin(angle));
                    const distance = traceWorld(from, to);
                    if (run === 5) distances.push(distance);
                }
                if (run === 0) { G.cameraMeshMs = G.cameraTerrainMs = 0; }
                else { batchMs.push(performance.now() - start); }
            }
            return { distances, batchMs, obstacles: obstacles.size,
                candidates: typeof cameraCandidates === "function" ? cameraCandidates(from, 8).length : obstacles.size,
                meshMs: G.cameraMeshMs, terrainMs: G.cameraTerrainMs };
        };
        """;

    private sealed record Sample(double[] Distances, double[] BatchMs, int Obstacles, int Candidates, double MeshMs, double TerrainMs);
}
