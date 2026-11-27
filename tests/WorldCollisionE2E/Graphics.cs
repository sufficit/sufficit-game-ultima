using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static BrowserTest;

internal static class Graphics
{
    internal static async Task RunAsync(IBrowser browser)
    {
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 960, Height = 640 } });
        var errors = TrackErrors(page);
        page.Console += (_, message) =>
        {
            if (message.Type == "error" && Regex.IsMatch(message.Text, "shader|compil|framebuffer", RegexOptions.IgnoreCase))
                errors.Enqueue(message.Text);
        };
        await page.GotoAsync(GameUrl(forceWebGl: true) + "&gfx=cinema");
        var build = await CheckBuildAsync(page);
        await WaitAsync(async () => (await ReadAsync<MapState?>(page, "() => window.game?.build?.state?.()"))?.Chunks >= 25,
            "cena indisponível", 25_000);
        var status = await ReadAsync<JsonElement>(page, "() => game.graphicsStatus()");
        Check(await page.EvaluateAsync<string>("() => game.mode") == Mode, "modo do renderizador", Mode);

        var detail = await RenderAsync<GraphicsDetail>(page, """
            () => {
                const scene = BABYLON.EngineStore.LastCreatedScene;
                const sun = scene.getLightByName('sun');
                const shadow = sun.getShadowGenerator(scene.activeCamera) ?? sun.getShadowGenerator();
                const engine = scene.getEngine();
                const info = engine.getGlInfo();
                return { cascades: shadow?.numCascades, fps: engine.getFps(),
                    gpu: info.renderer || info.vendor, webGlVersion: engine.webGLVersion, shadow: !!shadow,
                    supportsCsm: !!BABYLON.CascadedShadowGenerator?.IsSupported,
                    supportsSsao: !!BABYLON.SSAO2RenderingPipeline?.IsSupported };
            }
            """);
        // Software renderers skip shadow allocation and advanced effects.
        var software = Regex.IsMatch(detail.Gpu, "swiftshader|llvmpipe|softpipe|software|lavapipe", RegexOptions.IgnoreCase);
        var advanced = !software && detail.WebGlVersion >= 2;
        var csm = advanced && detail.SupportsCsm;
        Check(status.GetProperty("style").GetString() == "cinema" && status.GetProperty("env").GetBoolean() &&
            status.GetProperty("csm").GetBoolean() == csm &&
            status.GetProperty("ssao").GetBoolean() == (advanced && detail.SupportsSsao) &&
            status.GetProperty("depthOfField").GetBoolean() == advanced,
            "cinema settings respect renderer capabilities", new { status, detail });
        Check(detail.Shadow == !software && (csm ? detail.Cascades == 3 : detail.Cascades is null),
            "shadow generator matches the selected profile", detail);
        Task<string[]> CastersAsync() => RenderAsync<string[]>(page, """
            () => {
                const scene = BABYLON.EngineStore.LastCreatedScene;
                const sun = scene.getLightByName('sun');
                const map = (sun.getShadowGenerator(scene.activeCamera) ?? sun.getShadowGenerator()).getShadowMap();
                return map.renderList.map(mesh => mesh.name);
            }
            """);
        async Task<bool> AvatarShadowAsync() =>
            (await CastersAsync()).Any(name => name.Contains("remote-shadow-cascade", StringComparison.Ordinal));

        if (!software)
        {
            await page.EvaluateAsync("""
                () => {
                    game.addRemote('shadow-cascade', 'Teste CSM');
                    game.updateRemote('shadow-cascade', 1, 3, 0);
                }
                """);
            await WaitAsync(AvatarShadowAsync, "nearby avatar casts a shadow", 3000, 100);
            await page.EvaluateAsync("() => game.updateRemote('shadow-cascade', 40, 3, 0)");
            await WaitAsync(async () => !await AvatarShadowAsync(), "distant avatar leaves the shadow map", 3000, 100);
            await page.EvaluateAsync("() => game.removeRemote('shadow-cascade')");
        }
        Check(errors.IsEmpty, "erros de página, shader ou framebuffer", errors);
        if (Environment.GetEnvironmentVariable("ULTIMA_CAPTURE") is { Length: > 0 } path)
            await page.ScreenshotAsync(new() { Path = path });
        Log(new { build, mode = Mode, status, detail });
    }

    private sealed record GraphicsDetail(int? Cascades, double Fps, string Gpu, double WebGlVersion, bool Shadow, bool SupportsCsm, bool SupportsSsao);
}
