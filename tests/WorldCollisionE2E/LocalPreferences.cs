using Microsoft.Playwright;
using static BrowserTest;

internal static class LocalPreferences
{
    internal static async Task RunAsync(IBrowser browser)
    {
        await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1200, Height = 760 } });
        var url = GameUrl();
        var page = await context.NewPageAsync();
        var errors = TrackErrors(page);
        var build = await OpenAsync(page, url);

        await page.Locator("#game-canvas").FocusAsync();
        await page.Keyboard.DownAsync("d");
        try
        {
            await WaitAsync(async () => (await PositionAsync(page)) is { X: > 2 }, "caminha para fora da origem");
        }
        finally { await page.Keyboard.UpAsync("d"); }

        await OpenHelpAsync(page);
        const string devLabel = "Informações de desenvolvimento (FPS, fonte do conteúdo)";
        await page.GetByText(devLabel, new() { Exact = true }).ClickAsync();
        await Button(page, "Oficina do mundo").ClickAsync();
        await Button(page, "Esculpir o terreno").ClickAsync();
        var radius = page.Locator("#build-radius");
        await radius.FillAsync("7");
        await radius.PressAsync("Tab");
        await Button(page, "IA na CPU (sem placa de vídeo)").ClickAsync();
        var cpu = page.GetByRole(AriaRole.Button, new() { Name = "Qwen2.5 0.5B", Exact = false });
        await cpu.ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Diário", Exact = true }).ClickAsync();
        var journal = page.GetByRole(AriaRole.Dialog, new() { Name = "Diário", Exact = true });
        await journal.GetByRole(AriaRole.Button, new() { Name = "Mais contraste", Exact = true }).ClickAsync();

        var position = await PositionAsync(page);
        var poseKey = await page.EvaluateAsync<string>("() => 'ultima.posicao.v1:' + game.live.api()");
        await WaitAsync(async () => (await ReadAsync<Pose?>(page, "key => JSON.parse(localStorage.getItem(key))", poseKey)) is { } saved
            && Near(saved, position), "salvou a última pose mesmo depois de parar");
        Check(errors.IsEmpty, "erros antes de fechar a aba", errors);
        await page.CloseAsync();

        page = await context.NewPageAsync();
        errors = TrackErrors(page);
        await OpenAsync(page, url);
        var restored = await PositionAsync(page);
        Check(Near(restored, position), "nova aba restaurou posição e direção", new { position, restored });
        await OpenHelpAsync(page);
        var dev = page.GetByRole(AriaRole.Checkbox, new() { Name = devLabel });
        Check(await dev.IsCheckedAsync(), "HUD configurado continua ativado");
        await Button(page, "Esculpir o terreno").ClickAsync();
        Check(await page.Locator("#build-radius").InputValueAsync() == "7", "Oficina aberta e raio restaurado");
        Check(await page.EvaluateAsync<double>("async () => (await game.build.state()).r") == 7, "motor recebeu o raio salvo");
        await Button(page, "IA na CPU (sem placa de vídeo)").ClickAsync();
        cpu = page.GetByRole(AriaRole.Button, new() { Name = "Qwen2.5 0.5B", Exact = false });
        Check(await cpu.GetAttributeAsync("aria-pressed") == "true", "seleção do modelo de CPU restaurada");
        await page.GetByRole(AriaRole.Button, new() { Name = "Diário", Exact = true }).ClickAsync();
        journal = page.GetByRole(AriaRole.Dialog, new() { Name = "Diário", Exact = true });
        await journal.GetByRole(AriaRole.Button, new() { Name = "Tema padrão", Exact = true }).WaitForAsync();

        // Um servidor diferente deve começar no spawn, mesmo no mesmo navegador.
        await OpenAsync(page, url.Replace("19999", "19998", StringComparison.Ordinal));
        Check(Near(await PositionAsync(page), new Pose(0, 0, 0)), "outra API não herda a posição");

        await page.EvaluateAsync("key => localStorage.setItem(key, '{\"x\":9000,\"z\":1,\"ry\":0}')", poseKey);
        await OpenAsync(page, url);
        Check(Near(await PositionAsync(page), new Pose(0, 0, 0)), "coordenadas inválidas retornam ao spawn");
        Check(errors.IsEmpty, "erros no navegador", errors);
        Log(new { build, mode = Mode, position, restored, preferences = true, worldIsolation = true, invalidPose = true });
    }

    private static ILocator Button(IPage page, string name) => page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });

    private static async Task OpenHelpAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Abrir painel do jogo", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Controles e ajuda", Exact = true }).ClickAsync();
    }

    private static async Task<string?> OpenAsync(IPage page, string url)
    {
        await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var build = await CheckBuildAsync(page);
        await WaitAsync(async () => await page.EvaluateAsync<string?>("() => window.game?.mode") == Mode
            && await PositionAsync(page) is not null, "personagem disponível");
        return build;
    }

    private static Task<Pose> PositionAsync(IPage page) => RenderAsync<Pose>(page, """
        () => {
            const player = globalThis.BABYLON?.EngineStore.LastCreatedScene?.getTransformNodeByName('player');
            return player && { x: player.position.x, z: player.position.z, ry: player.rotation.y };
        }
        """);

    private static bool Near(Pose a, Pose b) => Math.Abs(a.X - b.X) < 0.08 && Math.Abs(a.Z - b.Z) < 0.08
        && Math.Abs(Math.IEEERemainder(a.Ry - b.Ry, 2 * Math.PI)) < 0.08;

    private sealed record Pose(double X, double Z, double Ry);
}
