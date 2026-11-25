using Microsoft.Playwright;
using static BrowserTest;

internal static class DeclarativeWindows
{
    internal static async Task RunAsync(IBrowser browser)
    {
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1200, Height = 720 } });
        var errors = TrackErrors(page);
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ULTIMA_DIAGNOSTIC")))
            page.Console += (_, message) => Console.WriteLine($"{message.Type} {message.Text[..Math.Min(900, message.Text.Length)]}");
        await page.GotoAsync(GameUrl(), new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var build = await CheckBuildAsync(page);

        var panel = Button(page, "Abrir painel do jogo");
        var journal = page.GetByRole(AriaRole.Dialog, new() { Name = "Diário", Exact = true });
        async Task OpenJournalAsync()
        {
            await panel.ClickAsync();
            await Button(page, "Diário").ClickAsync();
        }
        await OpenJournalAsync();
        await journal.GetByText("Suas conversas com personagens aparecem aqui.").WaitForAsync();
        var card = journal.Locator(".ultima-card--glass");
        Task<string> BackgroundAsync() => card.EvaluateAsync<string>("node => getComputedStyle(node).backgroundColor");
        var glass = await BackgroundAsync();
        await Button(journal, "Mais contraste").ClickAsync();
        await WaitAsync(async () => await BackgroundAsync() != glass, "contraste altera o fundo do Diário");
        await Button(journal, "Tema padrão").WaitForAsync();
        await CloseAsync(journal);

        await WaitAsync(async () => await page.EvaluateAsync<string?>("() => window.game?.mode ?? null") == Mode,
            "modo do renderizador");
        Check(await page.EvaluateAsync<bool>("() => game.teleportToGuardian()"), "chega à Guardiã");
        await page.Locator("#game-canvas").FocusAsync();
        await page.Locator(".ultima-hint").GetByText("para conversar").WaitForAsync();
        await page.Keyboard.PressAsync("e");

        var conversation = page.GetByRole(AriaRole.Dialog, new() { Name = "Conversa com a Guardiã", Exact = true });
        await conversation.WaitForAsync(new() { Timeout = 10_000 });
        Check((await conversation.GetAttributeAsync("class"))?.Contains("ultima-window--contrast", StringComparison.Ordinal) == true,
            "a conversa usa o contraste escolhido");
        var line = conversation.Locator(".ultima-dialog__line");
        var first = await line.InnerTextAsync();
        await Button(conversation, "Continuar · E/A").ClickAsync();
        await WaitAsync(async () => await line.InnerTextAsync() != first, "avança para a fala seguinte");
        await conversation.GetByRole(AriaRole.Textbox, new() { Name = "Escreva para a Guardiã" }).FillAsync("Quero novas árvores");
        await Button(conversation, "Enviar para a Guardiã").ClickAsync();
        await conversation.GetByText("Conecte-se ao servidor do jogo").WaitForAsync();
        var reply = await line.InnerTextAsync();
        await CloseAsync(conversation);
        await OpenJournalAsync();
        await journal.GetByText(reply).WaitForAsync();
        Check(errors.IsEmpty, "erros no navegador", errors);
        Log(new { build, mode = Mode, journal = true, dialogue = true, contrast = true, reply });
    }

    private static async Task CloseAsync(ILocator dialog)
    {
        await Button(dialog, "Fechar · Esc").ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
    }

    private static ILocator Button(IPage page, string name) => page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });
    private static ILocator Button(ILocator dialog, string name) => dialog.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });
}
