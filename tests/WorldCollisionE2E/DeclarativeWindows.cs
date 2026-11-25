using Microsoft.Playwright;
using static BrowserTest;

internal static class DeclarativeWindows
{
    internal static async Task RunAsync(IBrowser browser)
    {
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1200, Height = 720 } });
        var errors = TrackErrors(page);
        string? reported = null;
        await page.RouteAsync("**/v1/diagnostics/reports", async route =>
        {
            reported = route.Request.PostData;
            await route.FulfillAsync(new() { Status = 201, ContentType = "application/json",
                Body = "{\"id\":\"0123456789abcdef0123456789abcdef\"}" });
        });
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ULTIMA_DIAGNOSTIC")))
            page.Console += (_, message) => Console.WriteLine($"{message.Type} {message.Text[..Math.Min(900, message.Text.Length)]}");
        await page.AddInitScriptAsync("""
            document.addEventListener('DOMContentLoaded', () => {
                const init = game.aiCpu.init;
                game.aiCpu.init = bridge => {
                    const status = game.aiCpu.status;
                    let ready = false;
                    game.aiCpu.load = () => { ready = true; };
                    game.aiCpu.status = () => ready
                        ? { state: 'ready', progress: 1, detail: 'Modelo de teste pronto' }
                        : status();
                    game.live.token = () => null;
                    game.aiCpu.garden = (_, text, tokens, history) => {
                        window.guardianProbe = { text, tokens, history };
                        return 'Olá, caminhante.';
                    };
                    if (sessionStorage.getItem('guardian-test-engine') === 'gpu') {
                        game.ai.init = () => {};
                        game.ai.status = () => ({ state: 'ready', progress: 1, detail: 'GPU de teste pronta' });
                        game.ai.garden = (_, text, tokens, history) => {
                            window.guardianProbe = { text, tokens, history };
                            return 'Você pediu novas árvores.';
                        };
                    }
                    game.voice.startDictation = () => bridge.invokeMethodAsync('OnSpeechTranscript', 'Texto ditado');
                    game.voice.speak = text => { window.guardianSpoken = text; };
                    return init(bridge);
                };
            }, { once: true });
            """);
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
        await WaitAsync(() => page.EvaluateAsync<bool>("() => game.teleportToGuardian()"), "chega à Guardiã");
        await page.Locator("#game-canvas").FocusAsync();
        await page.Locator(".ultima-hint").GetByText("para conversar").WaitForAsync();
        await page.Keyboard.PressAsync("e");

        var conversation = page.GetByRole(AriaRole.Dialog, new() { Name = "Conversa com a Guardiã", Exact = true });
        await conversation.WaitForAsync(new() { Timeout = 10_000 });
        Check((await conversation.GetAttributeAsync("class"))?.Contains("ultima-window--contrast", StringComparison.Ordinal) == true,
            "a conversa usa o contraste escolhido");
        var draft = conversation.GetByRole(AriaRole.Textbox, new() { Name = "Escreva para a Guardiã" });
        var testAi = Button(conversation, "Testar IA");
        Check(!await testAi.IsEnabledAsync(), "teste indisponível antes de ativar um modelo");
        await Button(conversation, "Ativar IA na CPU").ClickAsync();
        await conversation.GetByText("IA local pronta na CPU.").WaitForAsync();
        await testAi.ClickAsync();
        await conversation.GetByText("A IA local respondeu ao teste.").WaitForAsync();
        Check(await page.EvaluateAsync<bool>("() => guardianProbe.text === 'Olá!' && guardianProbe.tokens === 32"),
            "teste usa a inferência curta");
        await Button(conversation, "Sugerir trilha").ClickAsync();
        Check((await draft.InputValueAsync()).StartsWith("Crie uma trilha de pedra", StringComparison.Ordinal), "sugestão preenche a caixa de texto");
        await Button(conversation, "Ditar").ClickAsync();
        await WaitAsync(async () => await draft.InputValueAsync() == "Texto ditado", "ditado alimenta a caixa de texto");
        await Button(conversation, "Ouvir fala").ClickAsync();
        Check(await page.EvaluateAsync<bool>("() => !!window.guardianSpoken"), "reproduz a fala");
        if (Environment.GetEnvironmentVariable("ULTIMA_CAPTURE") is { Length: > 0 } capture)
        {
            await page.SetViewportSizeAsync(390, 844);
            await page.ScreenshotAsync(new() { Path = capture });
            await page.SetViewportSizeAsync(1200, 720);
        }
        var line = conversation.Locator(".ultima-dialog__line");
        var first = await line.InnerTextAsync();
        await Button(conversation, "Continuar · E/A").ClickAsync();
        await WaitAsync(async () => await line.InnerTextAsync() != first, "avança para a fala seguinte");
        await Button(conversation, "Iluminar o jardim").ClickAsync();
        await conversation.GetByText("Conecte-se ao servidor do jogo para aplicar alterações no jardim.", new() { Exact = true }).WaitForAsync();
        await conversation.GetByRole(AriaRole.Textbox, new() { Name = "Escreva para a Guardiã" }).FillAsync("Quero novas árvores");
        await Button(conversation, "Enviar para a Guardiã").ClickAsync();
        await conversation.GetByText("Olá, caminhante. Conecte-se ao servidor do jogo para aplicar alterações no jardim.").WaitForAsync();
        await conversation.GetByText("✦ origem da resposta: Genius local · CPU").WaitForAsync();
        Check(await page.EvaluateAsync<bool>("() => guardianProbe.text === 'Quero novas árvores' && guardianProbe.tokens === 120"),
            "conversa sem sessão usa a IA local com contexto curto");
        var reply = await line.InnerTextAsync();
        var history = conversation.GetByRole(AriaRole.Region, new() { Name = "Histórico da conversa" });
        await history.GetByText("Quero novas árvores", new() { Exact = true }).WaitForAsync();
        await Button(conversation, "Reportar erro").ClickAsync();
        await conversation.GetByRole(AriaRole.Textbox, new() { Name = "Descrição opcional do problema" }).FillAsync("GPU indisponível");
        await Button(conversation, "Enviar diagnóstico").ClickAsync();
        await conversation.GetByRole(AriaRole.Status).GetByText("Diagnóstico enviado.", new() { Exact = false }).WaitForAsync();
        using var report = System.Text.Json.JsonDocument.Parse(reported ?? "{}");
        Check(report.RootElement.GetProperty("cpu").GetProperty("state").GetString() == "ready" &&
            report.RootElement.GetProperty("description").GetString() == "GPU indisponível" &&
            !reported!.Contains("Quero novas árvores", StringComparison.Ordinal), "diagnóstico envia estados sem conversa");
        await CloseAsync(conversation);
        await OpenJournalAsync();
        await journal.GetByText(reply).WaitForAsync();
        Check(!await page.EvaluateAsync<bool>("() => (localStorage.getItem('ultima-dialog-queue') || '').includes('Quero novas árvores')"),
            "a conversa do Genius fica fora da fila de telemetria");

        await CloseAsync(journal);
        await page.EvaluateAsync("() => sessionStorage.setItem('guardian-test-engine', 'gpu')");
        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await WaitAsync(() => page.EvaluateAsync<bool>("() => game.teleportToGuardian()"), "reencontra a Guardiã após recarregar");
        await page.Locator("#game-canvas").FocusAsync();
        await page.Locator(".ultima-hint").GetByText("para conversar").WaitForAsync();
        await page.Keyboard.PressAsync("e");
        await conversation.WaitForAsync();
        await history.GetByText("Quero novas árvores", new() { Exact = true }).WaitForAsync();
        await conversation.GetByText("IA local pronta na GPU.").WaitForAsync();
        await draft.FillAsync("Qual foi meu pedido anterior?");
        await Button(conversation, "Enviar para a Guardiã").ClickAsync();
        await conversation.GetByText("✦ origem da resposta: Genius local · GPU").WaitForAsync();
        Check(await page.EvaluateAsync<bool>("() => guardianProbe.history.some(turn => turn.user === 'Quero novas árvores' && turn.assistant === 'Olá, caminhante.')"),
            "o segundo turno recebe o histórico do Genius depois de recarregar");
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
