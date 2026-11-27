using Microsoft.Playwright;
using static BrowserTest;

internal static partial class DeclarativeWindows
{
    private static async Task VerifyGardenReceiptAsync(IPage page, ILocator conversation, ILocator draft)
    {
        var writes = 0;
        await page.RouteAsync("**/v1/game/garden/tools", route =>
            route.FulfillAsync(new() { Json = new { models = new { } } }));
        await page.RouteAsync("**/v1/game/garden/context", route =>
            route.FulfillAsync(new() { Json = new { prompt = "Propose a garden edit." } }));
        string? lastWrite = null;
        await page.RouteAsync("**/v1/game/garden/edits", route =>
        {
            lastWrite = route.Request.PostData;
            return route.FulfillAsync(new() { Json = new { applied = writes++ } });
        });
        await page.EvaluateAsync("""
            () => {
                window.testGardenEdits = true;
                window.guardianReplyOverride = JSON.stringify({calls:[{type:'function',function:{
                    name:'submit_garden_plan', arguments:JSON.stringify({
                        reply: 'Plantei uma flor.',
                        actions: [{ action: 'place', asset: 'plant.flower', x: 2, z: 3 }]
                    })
                }}]});
            }
            """);
        const string request = "Plante uma flor perto do banco";
        var send = Button(conversation, "Enviar para a Guardiã");
        await page.EvaluateAsync("() => { window.holdGuardianReply = true; delete window.finishGuardianReply; }");
        await draft.FillAsync(request);
        await send.ClickAsync();
        await WaitAsync(() => page.EvaluateAsync<bool>("() => typeof window.finishGuardianReply === 'function'"),
            "The garden turn started.");
        Check(await page.EvaluateAsync<bool>("() => guardianProbe.tokens === -1"), "Garden generation has no output cap.");
        await CloseAsync(conversation);
        await conversation.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await page.Locator("#game-canvas").FocusAsync();
        await page.Keyboard.PressAsync("e");
        await conversation.WaitForAsync();
        Check(!await send.IsEnabledAsync(), "Reopening preserves the outstanding turn.");
        await CloseAsync(conversation);
        await conversation.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await page.EvaluateAsync("""
            () => {
                window.holdGuardianReply = false;
                window.finishGuardianReply(window.guardianReplyOverride);
            }
            """);
        await WaitAsync(() => page.EvaluateAsync<bool>("""
            request => Object.values(JSON.parse(localStorage.getItem('ultima-dialog-history') || '{}'))
                .some(turns => turns.some(turn => turn.player === request && turn.reply.includes('Quer aplicar?')))
            """, request), "The completed background turn is saved before reopening the dialogue.");
        Check(writes == 0, "A background proposal must wait for confirmation.");
        await page.Locator("#game-canvas").FocusAsync();
        await page.Keyboard.PressAsync("e");
        await conversation.WaitForAsync();
        await conversation.GetByRole(AriaRole.Region, new() { Name = "Histórico da conversa" })
            .GetByText(request, new() { Exact = true }).WaitForAsync();
        var confirm = Button(conversation, "Aplicar alteração");
        await confirm.WaitForAsync();
        await Button(conversation, "Descartar proposta").ClickAsync();
        await page.EvaluateAsync("""
            () => {
                window.guardianReplyOverrideBackup = window.guardianReplyOverride;
                window.guardianReplyOverride = JSON.stringify({calls:[{type:'function',function:{name:'unknown',arguments:'{}'}}]});
            }
            """);
        await send.ClickAsync();
        await conversation.GetByText("A IA local não conseguiu propor uma edição válida.", new() { Exact = false }).WaitForAsync();
        await page.EvaluateAsync("() => { window.guardianReplyOverride = window.guardianReplyOverrideBackup; }");
        await send.ClickAsync();
        await confirm.WaitForAsync();
        await confirm.ClickAsync();
        await conversation.GetByText("O jardim não confirmou nenhuma alteração.", new() { Exact = false }).WaitForAsync();
        Check(await draft.InputValueAsync() == request, "An unconfirmed change preserves the request.");
        await send.ClickAsync();
        await confirm.WaitForAsync();
        await page.EvaluateAsync("""
            () => { window.guardianReplyOverride = JSON.stringify({calls:[],content:'Perto do banco.'}); }
            """);
        await draft.FillAsync("Onde vai ficar?");
        await send.ClickAsync();
        await conversation.GetByText("Perto do banco.", new() { Exact = true }).WaitForAsync();
        Check(writes == 1 && await confirm.IsVisibleAsync(), "A question preserves the proposal without applying it.");
        Check(await page.EvaluateAsync<bool>("""
            request => guardianProbe.prompt.includes('Pending proposal:') &&
                guardianProbe.history.some(turn => turn.user === request &&
                    turn.assistant.includes('não conseguiu propor uma edição válida')) &&
                guardianProbe.history.some(turn => turn.user === request && turn.assistant.includes('Quer aplicar?'))
            """, request), "The model receives both the pending proposal and earlier dialogue.");
        await page.EvaluateAsync("""
            () => { window.guardianReplyOverride = JSON.stringify({calls:[],content:'Preciso saber qual é o outro banco.'}); }
            """);
        await draft.FillAsync("faça o mesmo perto do outro banco");
        await send.ClickAsync();
        await conversation.GetByText("Preciso saber qual é o outro banco.", new() { Exact = true }).WaitForAsync();
        Check(writes == 1 && await confirm.IsVisibleAsync(),
            "A longer instruction starting with 'faça' goes to the model without approving the preview.");
        await page.EvaluateAsync("() => { window.beforeConfirmation = window.guardianProbe; }");
        await draft.FillAsync("faça");
        await send.ClickAsync();
        await conversation.GetByText("Apliquei as mudanças no jardim. Plantei uma flor.").WaitForAsync();
        Check(writes == 2 && await draft.InputValueAsync() == "", "Typed confirmation applies exactly once and clears its draft.");
        Check(await page.EvaluateAsync<bool>("() => window.guardianProbe === window.beforeConfirmation"),
            "Typed confirmation does not ask the model to regenerate the proposal.");
        using var body = System.Text.Json.JsonDocument.Parse(lastWrite!);
        var action = body.RootElement.GetProperty("actions")[0];
        Check(action.GetProperty("asset").GetString() == "plant.flower" && action.GetProperty("x").GetInt32() == 2 &&
            action.GetProperty("z").GetInt32() == 3, "Typed confirmation submits the original payload.");
        await draft.FillAsync("faça");
        await send.ClickAsync();
        await conversation.GetByText("Não há uma proposta pendente.", new() { Exact = false }).WaitForAsync();
        Check(writes == 2, "Repeating a consumed confirmation makes no second edit.");
        await draft.FillAsync("O que você fez?");
        await send.ClickAsync();
        await WaitAsync(() => page.EvaluateAsync<bool>("""
            () => guardianProbe.text === 'O que você fez?' && guardianProbe.history.some(turn =>
                turn.user === 'faça' && turn.assistant.includes('Apliquei as mudanças no jardim. Plantei uma flor.'))
            """), "The response context contains the actual application receipt.");
        await WaitAsync(() => send.IsEnabledAsync(), "The follow-up completes.");
        await page.EvaluateAsync("() => { window.testGardenEdits = false; delete window.guardianReplyOverride; }");
    }

}
