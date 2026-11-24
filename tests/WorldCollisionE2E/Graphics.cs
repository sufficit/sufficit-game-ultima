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
        var expected = JsonSerializer.SerializeToElement(new { style = "cinema", env = true, csm = true, ssao = true, depthOfField = true });
        Check(JsonElement.DeepEquals(status, expected), "perfil cinema completo", status);
        Check(await page.EvaluateAsync<string>("() => game.mode") == Mode, "modo do renderizador", Mode);

        var detail = await RenderAsync<GraphicsDetail>(page, """
            () => {
                const scene = BABYLON.EngineStore.LastCreatedScene;
                const shadow = scene.getLightByName('sun').getShadowGenerator(scene.activeCamera);
                return { cascades: shadow?.numCascades, fps: scene.getEngine().getFps(), gpu: scene.getEngine().getGlInfo().renderer };
            }
            """);
        Check(detail.Cascades == 3, "sombras com três cascatas", detail);
        Task<string[]> CastersAsync() => RenderAsync<string[]>(page, """
            () => {
                const scene = BABYLON.EngineStore.LastCreatedScene;
                const map = scene.getLightByName('sun').getShadowGenerator(scene.activeCamera).getShadowMap();
                return map.renderList.map(mesh => mesh.name);
            }
            """);
        async Task<bool> AvatarShadowAsync() =>
            (await CastersAsync()).Any(name => name.Contains("remote-shadow-cascade", StringComparison.Ordinal));

        await page.EvaluateAsync("""
            () => {
                game.addRemote('shadow-cascade', 'Teste CSM');
                game.updateRemote('shadow-cascade', 1, 3, 0);
            }
            """);
        await WaitAsync(AvatarShadowAsync, "avatar próximo deve projetar sombra em cascata", 3000, 100);
        await page.EvaluateAsync("() => game.updateRemote('shadow-cascade', 40, 3, 0)");
        await WaitAsync(async () => !await AvatarShadowAsync(), "avatar distante deve sair do mapa de sombras em cascata", 3000, 100);
        await page.EvaluateAsync("() => game.removeRemote('shadow-cascade')");
        Check(errors.IsEmpty, "erros de página, shader ou framebuffer", errors);
        if (Environment.GetEnvironmentVariable("ULTIMA_CAPTURE") is { Length: > 0 } path)
            await page.ScreenshotAsync(new() { Path = path });
        Log(new { build, mode = Mode, status, detail });
    }

    private sealed record GraphicsDetail(int Cascades, double Fps, string Gpu);
}
