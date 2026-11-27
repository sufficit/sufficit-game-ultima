using Microsoft.Playwright;
using static BrowserTest;

internal static partial class DeclarativeWindows
{
    internal static async Task RunAsync(IBrowser browser)
    {
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1200, Height = 720 } });
        var errors = TrackErrors(page);
        string? reported = null;
        string? qualityBatch = null;
        var acceptTelemetry = false;
        var failedTelemetry = false;
        await page.RouteAsync("**/v1/analytics/guardian", async route =>
        {
            if (acceptTelemetry) qualityBatch = route.Request.PostData;
            else failedTelemetry = true;
            await route.FulfillAsync(new() { Status = acceptTelemetry ? 200 : 503, ContentType = "application/json",
                Body = acceptTelemetry ? "{\"ok\":true}" : "{}" });
        });
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
                // Simulated local AI. It is installed at page load, before any C# call: Blazor caches
                // the handler it resolves for an identifier at the first call, so a stub installed
                // later (e.g. inside aiCpu.init, which runs at the end of the boot chain) stays
                // invisible to C# once the page has already read the state.
                const init = game.aiCpu.init, cpuStatus = game.aiCpu.status;
                let ready = false;
                game.aiCpu.load = () => { ready = true; };
                game.aiCpu.status = () => ready
                    ? { state: 'ready', progress: 1, detail: 'Modelo de teste pronto', modelKey: 'lfm350' }
                    : cpuStatus();
                const reportCpuStatus = game.aiCpu.status;
                game.aiCpu.status = () => {
                    if (window.failGuardianStatus) throw new Error('CPU status unavailable');
                    return reportCpuStatus();
                };
                const api = game.live.api;
                game.live.api = () => window.testGardenEdits ? location.origin : api();
                game.live.token = () => window.testGardenEdits ? 'browser-test-token' : null;
                game.aiCpu.garden = (prompt, text, tokens, history) => {
                    window.guardianProbe = { prompt, text, tokens, history };
                    if (window.holdGuardianReply)
                        return new Promise(resolve => { window.finishGuardianReply = resolve; });
                    return window.guardianReplyOverride ?? 'Olá, caminhante.';
                };
                if (sessionStorage.getItem('guardian-test-engine') === 'gpu') {
                    game.ai.init = () => {};
                    game.ai.status = () => ({ state: 'ready', progress: 1, detail: 'GPU de teste pronta' });
                    game.ai.garden = (_, text, tokens, history) => {
                        window.guardianProbe = { text, tokens, history };
                        return 'Você pediu novas árvores.';
                    };
                }
                game.voice.speak = text => { window.guardianSpoken = text; };
                game.aiCpu.init = bridge => {
                    game.voice.startDictation = () => bridge.invokeMethodAsync('OnSpeechTranscript', 'Texto ditado');
                    return init(bridge);
                };
            }, { once: true });
            """);
        await page.GotoAsync(GameUrl(), new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var build = await CheckBuildAsync(page);
        var controlsHint = page.GetByText("WASD anda · Setas ou mouse olham", new() { Exact = false });
        await controlsHint.WaitForAsync();
        await controlsHint.WaitForAsync(new() { State = WaitForSelectorState.Hidden });

        var panel = Button(page, "Abrir painel do jogo");
        var journal = page.GetByRole(AriaRole.Dialog, new() { Name = "Diário", Exact = true });
        async Task OpenJournalAsync()
        {
            await panel.ClickAsync();
            await Button(page, "Diário").ClickAsync();
        }
        await OpenJournalAsync();
        await journal.GetByText("Suas conversas com personagens aparecem aqui.").WaitForAsync();
        // Screen stack: the open window is modal — the focus enters it, what stays behind is inert and
        // Tab does not reach the game controls; on close, the focus returns to the canvas.
        Check(await journal.GetAttributeAsync("aria-modal") == "true", "o Diário é uma janela modal");
        await WaitAsync(() => FocusInsideAsync(page, ".ultima-dialog"), "o foco entra no Diário ao abrir");
        Check(await page.EvaluateAsync<bool>("() => document.querySelector('.ultima-menu').inert"), "atrás do Diário: menu e painel inertes");
        var leaked = await TabLeakAsync(page, 8);
        Check(leaked is null, "Tab não escapa do Diário", leaked);
        var card = journal.Locator(".ultima-card--glass");
        Task<string> BackgroundAsync() => card.EvaluateAsync<string>("node => getComputedStyle(node).backgroundColor");
        var glass = await BackgroundAsync();
        await Button(journal, "Mais contraste").ClickAsync();
        await WaitAsync(async () => await BackgroundAsync() != glass, "contraste altera o fundo do Diário");
        await Button(journal, "Tema padrão").WaitForAsync();
        await CloseAsync(journal);
        await WaitAsync(() => page.EvaluateAsync<bool>("() => document.activeElement?.id === 'game-canvas'"),
            "ao fechar o Diário o foco volta ao canvas");
        Check(!await page.EvaluateAsync<bool>("() => document.querySelector('.ultima-menu').inert"), "ao fechar o Diário o jogo volta a receber entrada");

        await WaitAsync(async () => await page.EvaluateAsync<string?>("() => window.game?.mode ?? null") == Mode,
            "modo do renderizador");
        Check(await page.EvaluateAsync<bool>("""
            async () => {
                const genius = await import('./js/guardian-genius.mjs');
                const { api, id } = game.net.info();
                const legacy = JSON.stringify([api, 'old-room', id, 'guardia']);
                const snapshot = { revision: 1, turns: [{user:'Antes da atualização', assistant:'Histórico do Genius preservado.'}] };
                return await genius.save(legacy, 1, JSON.stringify(snapshot));
            }
            """), "A previous Genius session exists in the old key format.");
        await WaitAsync(() => page.EvaluateAsync<bool>("() => game.teleportToGuardian()"), "chega à Guardiã");
        await page.Locator("#game-canvas").FocusAsync();
        await page.Locator(".ultima-hint").GetByText("para conversar").WaitForAsync();
        await page.Keyboard.PressAsync("e");

        var conversation = page.GetByRole(AriaRole.Dialog, new() { Name = "Conversa com a Guardiã", Exact = true });
        await conversation.WaitForAsync(new() { Timeout = 10_000 });
        await conversation.GetByRole(AriaRole.Region, new() { Name = "Histórico da conversa" })
            .GetByText("Histórico do Genius preservado.", new() { Exact = true }).WaitForAsync();
        Check(await conversation.GetAttributeAsync("aria-modal") == "true", "a conversa é uma janela modal");
        Check(await FocusInsideAsync(page, ".ultima-dialog"), "o foco entra na conversa ao abrir (tecla E)", await ActiveAsync(page));
        Check(await page.EvaluateAsync<bool>("() => document.querySelector('.ultima-menu').inert"), "atrás da conversa: menu e painel inertes");
        Check(await page.Locator("#game-canvas").EvaluateAsync<bool>("el => el.tabIndex === -1"), "a conversa aberta: o canvas sai da ordem de Tab");
        var chatLeak = await TabLeakAsync(page, 10);
        Check(chatLeak is null, "Tab não escapa da conversa", chatLeak);
        Check((await conversation.GetAttributeAsync("class"))?.Contains("ultima-window--contrast", StringComparison.Ordinal) == true,
            "a conversa usa o contraste escolhido");
        var draft = conversation.GetByRole(AriaRole.Textbox, new() { Name = "Escreva para a Guardiã" });
        var testAi = Button(conversation, "Testar IA");
        Check(!await testAi.IsEnabledAsync(), "teste indisponível antes de ativar um modelo");
        Check(await conversation.GetByRole(AriaRole.Group, new() { Name = "Assuntos da conversa" }).CountAsync() == 0 &&
            await Button(conversation, "Iluminar o jardim").CountAsync() == 0 &&
            await Button(conversation, "Sugerir trilha").CountAsync() == 0 &&
            await Button(conversation, "Sugerir flores").CountAsync() == 0,
            "The Guardian conversation offers free text without scripted topics or actions.");
        await Button(conversation, "Ativar IA na CPU").ClickAsync();
        await AiTextAsync(conversation, "IA local pronta na CPU.");
        await testAi.ClickAsync();
        await AiTextAsync(conversation, "A IA local respondeu ao teste.");
        Check(await page.EvaluateAsync<bool>("() => guardianProbe.text === 'Olá!' && guardianProbe.tokens === 32"),
            "The diagnostic uses a short inference.");
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
        await VerifyGuardianRetryAsync(page, conversation, draft);
        await VerifyGardenReceiptAsync(page, conversation, draft);
        await draft.FillAsync("Quero novas árvores");
        await Button(conversation, "Enviar para a Guardiã").ClickAsync();
        await conversation.GetByText("Olá, caminhante. Conecte-se ao servidor do jogo para aplicar alterações no jardim.").WaitForAsync();
        await conversation.GetByText("✦ origem da resposta: Genius local · CPU").WaitForAsync();
        Check(await draft.InputValueAsync() == "", "A completed reply clears the submitted draft.");
        Check(await page.EvaluateAsync<bool>("() => guardianProbe.text === 'Quero novas árvores' && guardianProbe.tokens === -1"),
            "conversa sem sessão usa a IA local sem teto de geração");
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
        await WaitAsync(() => page.EvaluateAsync<bool>("() => document.activeElement?.id === 'game-canvas'"),
            "ao fechar a conversa o foco volta ao canvas");
        Check(await page.Locator("#game-canvas").EvaluateAsync<bool>("el => el.tabIndex === 0"), "conversa fechada: o canvas volta à ordem de Tab");
        await OpenJournalAsync();
        await journal.GetByText(reply).WaitForAsync();
        Check(await page.EvaluateAsync<bool>("""
            async () => {
                const genius = await import('./js/guardian-genius.mjs');
                const wanderer = genius.npcConversationId('andarilho');
                return wanderer !== genius.guardianConversationId() &&
                    await genius.load(wanderer) === null && localStorage.getItem('ultima-dialog-history') === null;
            }
            """), "The journal reads the one Genius conversation for each NPC.");

        Check(!await page.EvaluateAsync<bool>("() => (localStorage.getItem('ultima-dialog-queue') || '').includes('Quero novas árvores')"),
            "a conversa do Genius fica fora da fila de telemetria");

        await CloseAsync(journal);
        await page.EvaluateAsync("""
            () => {
                sessionStorage.setItem('guardian-test-engine', 'gpu');
                sessionStorage.setItem('ultima-id-aba', 'aaaaaaaa');
                const url = new URL(location.href);
                url.searchParams.set('sala', 'another-room');
                history.replaceState(null, '', url);
            }
            """);
        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await WaitAsync(() => Task.FromResult(failedTelemetry), "The queued events survive a collector outage.");
        acceptTelemetry = true;
        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await WaitAsync(() => Task.FromResult(qualityBatch is not null), "The persisted queue retries after reload.");
        VerifyQualityBatch(qualityBatch!);
        await WaitAsync(() => page.EvaluateAsync<bool>("() => game.teleportToGuardian()"), "reencontra a Guardiã após recarregar");
        await page.Locator("#game-canvas").FocusAsync();
        await page.Locator(".ultima-hint").GetByText("para conversar").WaitForAsync();
        await page.Keyboard.PressAsync("e");
        await conversation.WaitForAsync();
        await history.GetByText("Quero novas árvores", new() { Exact = true }).WaitForAsync();
        await AiTextAsync(conversation, "IA local pronta na GPU.");
        await draft.FillAsync("Qual foi meu pedido anterior?");
        await Button(conversation, "Enviar para a Guardiã").ClickAsync();
        await conversation.GetByText("✦ origem da resposta: Genius local · GPU").WaitForAsync();
        Check(await page.EvaluateAsync<bool>("() => guardianProbe.history.some(turn => turn.user === 'Quero novas árvores' && turn.assistant.startsWith('Olá, caminhante.'))"),
            "o segundo turno recebe o histórico do Genius depois de recarregar");
        Check(errors.IsEmpty, "erros no navegador", errors);
        Log(new { build, mode = Mode, journal = true, dialogue = true, contrast = true, reply });
    }

    private static void VerifyQualityBatch(string batch)
    {
        using var json = System.Text.Json.JsonDocument.Parse(batch);
        var events = json.RootElement.GetProperty("events").EnumerateArray();
        var outcomes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in events)
        {
            Check(entry.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal)
                .SetEquals(["id", "build", "engine", "model", "outcome", "elapsedMs"]),
                "Only structured telemetry fields are sent.", entry);
            if (entry.GetProperty("model").GetString() == "lfm350")
                outcomes.Add(entry.GetProperty("outcome").GetString()!);
        }
        Check(outcomes.IsSupersetOf(["generation_failed", "tool_rejected", "proposal", "apply_failed", "applied", "dismissed"]),
            "The local CPU model is attributed to generation, proposal and confirmation outcomes.", outcomes);
    }

    private static async Task VerifyGuardianRetryAsync(IPage page, ILocator conversation, ILocator draft)
    {
        const string request = "Plante flores no caminho";
        var send = Button(conversation, "Enviar para a Guardiã");
        await page.EvaluateAsync("() => { window.holdGuardianReply = true; }");
        await draft.FillAsync(request);
        await send.ClickAsync();
        await WaitAsync(() => page.EvaluateAsync<bool>("() => typeof window.finishGuardianReply === 'function'"),
            "The local CPU turn started.");
        Check(await draft.InputValueAsync() == request, "A pending CPU turn keeps its submitted draft visible.");
        Check(!await send.IsEnabledAsync(), "A pending CPU turn blocks duplicate submissions.");
        await page.EvaluateAsync("() => { window.holdGuardianReply = false; window.finishGuardianReply(''); }");
        await conversation.GetByText("A IA local não conseguiu responder.", new() { Exact = false }).WaitForAsync();
        Check(await draft.InputValueAsync() == request && await send.IsEnabledAsync(),
            "A failed CPU turn leaves the original request ready to retry.");

        await page.EvaluateAsync("() => { window.failGuardianStatus = true; }");
        await send.ClickAsync();
        await conversation.GetByText("Não consegui concluir o pedido. Tente novamente.", new() { Exact = true }).WaitForAsync();
        Check(await draft.InputValueAsync() == request && await send.IsEnabledAsync(),
            "A status error resolves the turn without discarding the request.");

        await page.EvaluateAsync("""
            () => {
                window.failGuardianStatus = false;
                window.guardianReplyOverride = 'Entendi onde você quer as flores.';
            }
            """);
        await send.ClickAsync();
        await conversation.GetByText("Entendi onde você quer as flores.", new() { Exact = false }).WaitForAsync();
        Check(await draft.InputValueAsync() == "", "A successful retry clears the draft.");
        Check(await page.EvaluateAsync<bool>("""
            request => guardianProbe.text === request && guardianProbe.history.some(turn =>
                turn.user === request && turn.assistant.includes('A IA local não conseguiu responder.'))
            """, request), "The retry sees the failed turn and its visible reply in Genius history.");
        await page.EvaluateAsync("() => { delete window.guardianReplyOverride; }");
    }

    // Waits for a text of the local AI in the window. If it does not show up, prints what the two
    // local AI modules report next to what the window shows, so the log tells a module that never
    // reached the state apart from a window that stopped redrawing.
    private static async Task AiTextAsync(ILocator scope, string text)
    {
        try { await scope.GetByText(text).WaitForAsync(); }
        catch (TimeoutException)
        {
            Console.WriteLine("AI_DIAG " + await scope.Page.EvaluateAsync<string>("""
                () => JSON.stringify({
                    cpuState: game.aiCpu.status().state, gpuState: game.ai.status().state,
                    window: (document.querySelector('.ultima-dialog')?.innerText ?? '').slice(0, 400),
                    dialogs: document.querySelectorAll('.ultima-dialog').length,
                    draft: document.querySelector('#dialog-input')?.value ?? null
                })
                """));
            throw;
        }
    }

    // Describes the focused element: "#id", the tag with its first class, or "body".
    private static Task<string> ActiveAsync(IPage page) => page.EvaluateAsync<string>(
        "() => { const el = document.activeElement; if (!el) return 'none'; return el.id ? '#' + el.id : el.tagName.toLowerCase() + (typeof el.className === 'string' && el.className ? '.' + el.className.split(' ')[0] : ''); }");

    private static Task<bool> FocusInsideAsync(IPage page, string selector) => page.EvaluateAsync<bool>(
        "selector => !!document.activeElement && !!document.querySelector(selector)?.contains(document.activeElement)", selector);

    // Tab circula pelos controles da janela; devolve o primeiro controle de fora que receber o foco.
    // Tab cycles through the window controls; returns the first outside control that receives the
    // focus. The document body is the natural stop between the last and the first control.
    private static async Task<string?> TabLeakAsync(IPage page, int times)
    {
        for (var i = 0; i < times; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            var active = await ActiveAsync(page);
            if (active != "body" && !await FocusInsideAsync(page, ".ultima-dialog")) return active;
        }
        return null;
    }

    private static async Task CloseAsync(ILocator dialog)
    {
        await Button(dialog, "Fechar · Esc").ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
    }

    private static ILocator Button(IPage page, string name) => page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });
    private static ILocator Button(ILocator dialog, string name) => dialog.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });
}
