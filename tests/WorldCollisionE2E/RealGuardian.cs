using Microsoft.Playwright;
using static BrowserTest;

internal static class RealGuardian
{
    internal static async Task RunAsync(IBrowser browser)
    {
        var device = Environment.GetEnvironmentVariable("ULTIMA_AI_DEVICE") ?? "cpu";
        Check(device == "cpu", "Only the CPU model has qualified tool support.");
        const string api = "aiCpu";
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1200, Height = 800 } });
        var errors = TrackErrors(page);
        await page.AddInitScriptAsync($$"""
            document.addEventListener('DOMContentLoaded', () => {
                const engine = game.{{api}};
                const generate = engine.garden;
                engine.garden = function (prompt, text, maxTokens, history, signal) {
                    window.lastRealGuardianHistory = history;
                    return generate.call(this, prompt, text, maxTokens, history, signal);
                };
            }, { once: true });
            """);
        var url = GameUrl(forceWebGl: true);
        await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var build = await CheckBuildAsync(page);
        var first = "Responda em uma frase curta: gosto de ipês.";
        var second = "Em uma frase curta, do que eu disse que gosto?";
        await OpenAsync();
        await LoadAsync();
        await SendAsync(first);
        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await OpenAsync();
        await LoadAsync();
        await SendAsync(second);
        await page.GetByRole(AriaRole.Region, new() { Name = "Histórico da conversa" })
            .GetByText(first, new() { Exact = true }).WaitForAsync();
        Check(await page.EvaluateAsync<bool>("text => window.lastRealGuardianHistory?.some(turn => turn.user === text && turn.assistant)", first),
            "The real model receives the restored first turn.");
        Check(errors.IsEmpty, "Errors during the real Guardian conversation", errors);
        Log(new { build, device, historyRestored = true });

        async Task OpenAsync()
        {
            await WaitAsync(() => page.EvaluateAsync<bool>("async () => !!(await window.game?.teleportToGuardian?.())"), "Guardian is reachable");
            await page.Locator("#game-canvas").FocusAsync();
            await page.Locator(".ultima-hint").GetByText("para conversar").WaitForAsync();
            await page.Keyboard.PressAsync("e");
            await page.GetByRole(AriaRole.Dialog, new() { Name = "Conversa com a Guardiã" }).WaitForAsync();
        }

        async Task LoadAsync()
        {
            _ = await page.EvaluateAsync<bool>("api => { void game[api].load(); return true; }", api);
            await WaitAsync(async () =>
            {
                var status = await ReadAsync<ModelStatus>(page, "api => game[api].status()", api);
                Check(status.State is not ("unsupported" or "error"), "Real local model could not start", status);
                return status.State == "ready";
            }, "Real local model did not become ready", 360_000, 750);
        }

        async Task SendAsync(string text)
        {
            var window = page.GetByRole(AriaRole.Dialog, new() { Name = "Conversa com a Guardiã" });
            var replies = window.GetByText($"✦ origem da resposta: Genius local · {device.ToUpperInvariant()}", new() { Exact = true });
            var previous = await replies.CountAsync();
            await window.GetByRole(AriaRole.Textbox, new() { Name = "Escreva para a Guardiã" }).FillAsync(text);
            await window.GetByRole(AriaRole.Button, new() { Name = "Enviar para a Guardiã" }).ClickAsync();
            await WaitAsync(async () => await replies.CountAsync() > previous, "The local runtime did not return a reply", 180_000);
            var reply = await window.Locator(".ultima-dialog__line").InnerTextAsync();
            Check(!string.IsNullOrWhiteSpace(reply) && !reply.Contains("A IA local não conseguiu", StringComparison.Ordinal),
                "The model produced an actual reply", reply);
            Log(new { device, reply });
        }
    }

    private sealed record ModelStatus(string State, string? Detail);
}
