using Microsoft.Playwright;
using static BrowserTest;

internal static class BrowserResources
{
    internal static async Task RunAsync(IBrowser browser)
    {
        var page = await browser.NewPageAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 720 },
            DeviceScaleFactor = 2
        });
        var errors = TrackErrors(page);
        var modelRequests = 0;
        await page.AddInitScriptAsync("""
            localStorage.setItem('ultima-ia-local', '1');
            localStorage.setItem('ultima-ia-cpu', '1');
            """);
        await page.RouteAsync("**/@wllama/wllama@*/esm/index.min.js", route =>
        {
            Interlocked.Increment(ref modelRequests);
            return route.FulfillAsync(new()
            {
                ContentType = "text/javascript",
                Headers = new Dictionary<string, string> { ["Access-Control-Allow-Origin"] = "*" },
                Body = "export class Wllama { async loadModelFromUrl() {} async exit() {} }"
            });
        });
        await page.GotoAsync(GameUrl(forceWebGl: true), new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await CheckBuildAsync(page);
        await WaitAsync(() => page.EvaluateAsync<bool>("""
            async () => {
                if (!game?.graphicsStatus || !game.aiCpu?.status().modelKey) return false;
                const graphics = await game.graphicsStatus();
                return graphics?.softwareRenderer && !!(await game.playerState());
            }
            """), "The software renderer and AI controls are ready.");
        Check(modelRequests == 0 && await page.EvaluateAsync<string>("() => game.aiCpu.status().state") == "off",
            "Saved AI preferences do not allocate a model on startup.");
        Check(await page.EvaluateAsync<bool>("() => game.aiCpu.resume()") &&
            modelRequests == 1 && await page.EvaluateAsync<string>("() => game.aiCpu.status().state") == "ready",
            "Opening an AI turn on demand restores the saved CPU model.");
        await page.EvaluateAsync("() => game.aiCpu.disable()");

        var dimensions = await RenderAsync<int[]>(page, """
            () => {
                const scene = BABYLON.EngineStore.LastCreatedScene;
                const engine = scene.getEngine();
                globalThis.resourceDrawCount = 0;
                scene.onAfterRenderObservable.add(() => resourceDrawCount++);
                return [engine.getRenderWidth(), engine.getRenderHeight(), Number(scene.shadowsEnabled)];
            }
            """);
        Check(dimensions is [> 0 and <= 1280, > 0 and <= 720, 0],
            "High-DPR software rendering caps the buffer and skips shadows.", dimensions);
        await WaitAsync(async () => await Draws() > 0, "The visible scene renders.");
        var before = await Draws();
        await Task.Delay(1000);
        var draws = await Draws() - before;
        Check(draws is > 0 and <= 10, "Idle draws are capped in software mode.", draws);

        await VisibilityAsync(hidden: true);
        await WaitAsync(() => VisibleAsync(expected: false), "The visibility event reaches the renderer.");
        var hiddenDraws = await Draws();
        await Task.Delay(550);
        Check(await Draws() == hiddenDraws, "The hidden scene stops drawing.");
        await VisibilityAsync(hidden: false);
        await WaitAsync(() => VisibleAsync(expected: true), "The renderer resumes.");
        await WaitAsync(async () => await Draws() > hiddenDraws, "Rendering resumes after visibility changes.");

        var materialsBefore = await TileMaterials();
        await page.EvaluateAsync("""
            async () => {
                for (let i = 0; i < 40; i++) game.setMapTiles({'1,1': {type: i % 2 ? 'path' : 'pit'}});
                game.setMapTiles({'1,1': {type: 'invalid'}});
                await game.graphicsStatus();
            }
            """);
        Check(await TileMaterials() == materialsBefore, "Tile replacement and removal dispose owned materials.");
        Check(errors.IsEmpty, "No browser errors during resource tests.", errors);
        Log(new { mode = Mode, dimensions, idleDraws = draws, modelRequests });

        Task<int> Draws() => RenderAsync<int>(page, "() => resourceDrawCount");
        Task<int> TileMaterials() => RenderAsync<int>(page, """
            () => BABYLON.EngineStore.LastCreatedScene.materials.filter(m => /^tile-.*-mat-1,1$/.test(m.name)).length
            """);
        Task<bool> VisibleAsync(bool expected) =>
            page.EvaluateAsync<bool>("async expected => (await game.graphicsStatus()).pageVisible === expected", expected);
        Task VisibilityAsync(bool hidden) => page.EvaluateAsync("""
            value => {
                Object.defineProperty(document, 'hidden', { configurable: true, value });
                document.dispatchEvent(new Event('visibilitychange'));
            }
            """, hidden);
    }
}
