using Microsoft.Playwright;
using static BrowserTest;

internal static class GuardianCpuTools
{
    internal static async Task RunAsync(IBrowser browser)
    {
        var api = Environment.GetEnvironmentVariable("ULTIMA_GARDEN_API");
        Check(Uri.TryCreate(api, UriKind.Absolute, out var endpoint) && endpoint.IsLoopback,
            "Tool writes require a disposable loopback garden.");
        var url = new UriBuilder(GameUrl(forceWebGl: true))
        {
            Query = $"rede=0&gpu=0&iacpu=qwen05tools&api={Uri.EscapeDataString(endpoint!.GetLeftPart(UriPartial.Authority))}" +
                (MainThread ? "&render=main" : "")
        };
        var page = await browser.NewPageAsync();
        var errors = TrackErrors(page);
        if (Environment.GetEnvironmentVariable("ULTIMA_TOOL_CACHE") is { Length: > 0 } cache)
        {
            // Substitute download URLs only. Inference, tool parsing and execution stay real.
            Check(Uri.TryCreate(cache, UriKind.Absolute, out var storage) && storage.IsLoopback, "Expected a loopback model cache.");
            await page.RouteAsync("**/js/ai-cpu.js*", async route =>
            {
                var original = await route.FetchAsync();
                var script = (await original.TextAsync())
                    .Replace("https://cdn.jsdelivr.net/npm/@wllama/wllama@3.13.0/esm/index.min.js", cache + "/wllama.mjs", StringComparison.Ordinal)
                    .Replace("https://cdn.jsdelivr.net/npm/@wllama/wllama@3.13.0/esm/wasm/wllama.wasm", cache + "/wllama.wasm", StringComparison.Ordinal)
                    .Replace("https://huggingface.co/Qwen/Qwen2.5-0.5B-Instruct-GGUF/resolve/main/qwen2.5-0.5b-instruct-q4_k_m.gguf", cache + "/qwen.gguf", StringComparison.Ordinal);
                await route.FulfillAsync(new() { Status = 200, ContentType = "text/javascript", Body = script });
            });
        }
        await page.AddInitScriptAsync("""
            document.addEventListener('DOMContentLoaded', () => {
                const garden = game.aiCpu.garden;
                game.aiCpu.garden = async (...args) => {
                    const output = await garden(...args);
                    if (args[5]) window.lastGardenToolOutput = output;
                    return output;
                };
            }, { once: true });
            """);
        await page.GotoAsync(url.Uri.AbsoluteUri, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var build = await CheckBuildAsync(page);
        await WaitAsync(() => page.EvaluateAsync<bool>("() => !!window.game?.live?.token()"), "Garden session started.");
        await WaitAsync(() => page.EvaluateAsync<bool>("() => game.teleportToGuardian()"), "Guardian is reachable.");
        await page.Locator("#game-canvas").FocusAsync();
        await page.Locator(".ultima-hint").GetByText("para conversar").WaitForAsync();
        await page.Keyboard.PressAsync("e");
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Conversa com a Guardiã" });
        await dialog.WaitForAsync();
        await page.EvaluateAsync("() => { void game.aiCpu.load(); }");
        await WaitAsync(async () => await page.EvaluateAsync<string>("() => game.aiCpu.status().state") == "ready",
            "Qwen tool model is ready.", 240_000, 750);
        await Button("Iluminar o jardim").ClickAsync();
        await dialog.GetByText("As duas luminárias estão na entrada do jardim.", new() { Exact = false }).WaitForAsync();
        var baseline = await ReadMapAsync();
        const string request = "aumente a altura dessas luminarias 1m cada";
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await SubmitAsync(request);
        await WaitAsync(async () =>
        {
            var reply = await dialog.Locator(".ultima-dialog__line").InnerTextAsync();
            return reply.EndsWith("Quer aplicar?", StringComparison.Ordinal) ||
                reply.Contains("Nenhuma alteração foi aplicada", StringComparison.Ordinal);
        }, "The real tool turn finished.", 125_000);
        var proposal = await dialog.Locator(".ultima-dialog__line").InnerTextAsync();
        Check(proposal.Contains("Somar 1 m em Altura da haste", StringComparison.Ordinal),
            "The model picks the catalogued lamp height and adds one metre.",
            new { proposal, output = await page.EvaluateAsync<string?>("() => window.lastGardenToolOutput ?? null") });
        var latency = clock.ElapsedMilliseconds;
        Check((await ReadMapAsync()).Revision == baseline.Revision, "A tool proposal leaves the world unchanged.");
        await Button("Aplicar alteração").ClickAsync();
        await dialog.GetByText("Apliquei as mudanças no jardim. Atualizei a propriedade: altura da haste", new() { Exact = false }).WaitForAsync();
        var applied = await ReadMapAsync();
        Check(applied.Placements.Count(p => p.Asset == "prop.lantern" && p.Parameters?.PoleHeight == 2.4 &&
            p.Parameters.LightIntensity is null) == 2 && applied.Revision == baseline.Revision + 2,
            "The real model tool increased both poles by one metre and kept default intensity.", applied);

        await SubmitAsync("Não aumente a altura dessas luminárias.");
        await WaitAsync(async () =>
        {
            var reply = await dialog.Locator(".ultima-dialog__line").InnerTextAsync();
            return reply.EndsWith("Quer aplicar?", StringComparison.Ordinal) ||
                reply.Contains("Nenhuma alteração foi aplicada", StringComparison.Ordinal);
        }, "The negated turn finished.", 125_000);
        if (await Button("Descartar proposta").IsVisibleAsync())
            await Button("Descartar proposta").ClickAsync();
        Check((await ReadMapAsync()).Revision == applied.Revision, "An unconfirmed reply cannot edit the world.");
        Check(errors.IsEmpty, "No errors during native CPU tool integration.", errors);
        Log(new { build, mode = Mode, model = "Qwen2.5 0.5B", latency, applied.Revision });

        ILocator Button(string name) => dialog.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });

        async Task SubmitAsync(string text)
        {
            await dialog.Locator("#dialog-input").FillAsync(text);
            await Button("Enviar para a Guardiã").ClickAsync();
        }

        Task<Map> ReadMapAsync() => ReadAsync<Map>(page,
            "async () => (await fetch(game.live.api() + '/v1/game/map', {cache:'no-store'})).json()");
    }

    private sealed record Map(int Revision, Placement[] Placements);
    private sealed record Placement(string Asset, Parameters? Parameters);
    private sealed record Parameters(double PoleHeight, double? LightIntensity);
}
