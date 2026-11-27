using System.Text.Json;
using Microsoft.Playwright;
using static BrowserTest;

internal static class GuardianCpuTools
{
    internal static async Task RunAsync(IBrowser browser)
    {
        // Published LFM weights are selected through the game menu. Research candidates
        // continue to replace the default GGUF inside the isolated test fixture.
        const string LfmUrl = "https://huggingface.co/LiquidAI/LFM2.5-350M-GGUF/resolve/657e078c94084481950a2d555a941481f715536b/LFM2.5-350M-Q4_K_M.gguf";
        var trial = Environment.GetEnvironmentVariable("ULTIMA_CPU_TOOL_TRIAL");
        Check(trial is null || System.Text.RegularExpressions.Regex.IsMatch(trial, @"\A[a-z0-9][a-z0-9-]{0,63}\z"),
            "A CPU tool trial must be a lowercase model ID.");
        var cache = Environment.GetEnvironmentVariable("ULTIMA_TOOL_CACHE");
        Check(trial is null || !string.IsNullOrEmpty(cache), "CPU tool trials need a loopback model cache.");
        var selectLfm = trial == "lfm350";
        var api = Environment.GetEnvironmentVariable("ULTIMA_GARDEN_API");
        Check(Uri.TryCreate(api, UriKind.Absolute, out var endpoint) && endpoint.IsLoopback,
            "Tool writes require a disposable loopback garden.");
        var url = new UriBuilder(GameUrl(forceWebGl: true))
        {
            Query = $"rede=0&gpu=0&api={Uri.EscapeDataString(endpoint!.GetLeftPart(UriPartial.Authority))}" +
                (MainThread ? "&render=main" : "")
        };
        var page = await browser.NewPageAsync();
        var errors = TrackErrors(page);
        string? missingAsset = null;
        if (cache is { Length: > 0 })
        {
            // Substitute download URLs only. Inference, tool parsing and execution stay real.
            Check(Uri.TryCreate(cache, UriKind.Absolute, out var storage) && storage.IsLoopback, "Expected a loopback model cache.");
            cache = storage!.AbsoluteUri.TrimEnd('/');
            await page.RouteAsync("**/js/ai-cpu.js*", async route =>
            {
                var original = await route.FetchAsync();
                var script = await original.TextAsync();
                (string Url, string File)[] assets =
                [
                    ("https://cdn.jsdelivr.net/npm/@wllama/wllama@3.13.0/esm/index.min.js", "wllama.mjs"),
                    ("https://cdn.jsdelivr.net/npm/@wllama/wllama@3.13.0/esm/wasm/wllama.wasm", "wllama.wasm"),
                    (selectLfm ? LfmUrl : "https://huggingface.co/Qwen/Qwen2.5-0.5B-Instruct-GGUF/resolve/main/qwen2.5-0.5b-instruct-q4_k_m.gguf",
                        (trial ?? "qwen") + ".gguf")
                ];
                foreach (var (url, file) in assets)
                {
                    if (!script.Contains(url, StringComparison.Ordinal))
                    {
                        missingAsset = url;
                        await route.AbortAsync();
                        return;
                    }
                    script = script.Replace(url, cache + "/" + file, StringComparison.Ordinal);
                }
                await route.FulfillAsync(new() { Status = 200, ContentType = "text/javascript", Body = script });
            });
        }
        await page.AddInitScriptAsync("""
            document.addEventListener('DOMContentLoaded', () => {
                const garden = game.aiCpu.garden;
                game.aiCpu.garden = async (...args) => {
                    const output = await garden(...args);
                    if (args[5]) {
                        window.lastGardenToolOutput = output;
                        window.gardenToolTurns = (window.gardenToolTurns ?? 0) + 1;
                    }
                    return output;
                };
            }, { once: true });
            """);
        await page.GotoAsync(url.Uri.AbsoluteUri, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        Check(missingAsset is null, "Update the fixture URLs to match the current CPU adapter.", missingAsset);
        var build = await CheckBuildAsync(page);
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Conversa com a Guardiã" });
        if (selectLfm)
        {
            await page.GetByRole(AriaRole.Button, new() { Name = "Abrir painel do jogo", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Controles e ajuda", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "IA na CPU (sem placa de vídeo)", Exact = true }).ClickAsync();
            var lfm = page.GetByRole(AriaRole.Button, new() { Name = "LFM2.5-350M", Exact = false });
            await lfm.ClickAsync();
            Check(await lfm.GetAttributeAsync("aria-pressed") == "true", "LFM was selected in the CPU menu.");
            Check(await page.EvaluateAsync<string>("() => localStorage.getItem('ultima-ia-cpu-modelo')") == "lfm350",
                "The selected CPU model was persisted.");
            await page.GetByRole(AriaRole.Button, new() { Name = "Ativar IA na minha CPU", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Fechar painel do jogo", Exact = true }).ClickAsync();
        }
        await OpenGuardianAsync();
        var loading = System.Diagnostics.Stopwatch.StartNew();
        var ready = await page.EvaluateAsync<bool>("() => game.aiCpu.load()");
        var loadMs = loading.ElapsedMilliseconds;
        if (trial is not null) Log(new { trial, mode = Mode, loadMs, ready });
        Check(ready, "The CPU model could not load.",
            ready ? null : await page.EvaluateAsync<JsonElement>("() => game.aiCpu.debug().lastError"));
        if (selectLfm)
            Check(await page.EvaluateAsync<string>("() => game.aiCpu.status().modelKey") == "lfm350",
                "The game loaded the selected LFM model.");
        await WaitAsync(() => page.EvaluateAsync<bool>("""
            async () => (await fetch(game.live.api() + '/v1/game/garden/tools',
                { headers: {'X-Game-Token':game.live.token()}, cache:'no-store' })).ok
            """), "The server recognizes the avatar is in reach of the Guardian.");
        await Button("Iluminar o jardim").ClickAsync();
        await dialog.GetByText("As duas luminárias estão na entrada do jardim.", new() { Exact = false }).WaitForAsync();
        await page.EvaluateAsync("""
            async () => {
                const response = await fetch(game.live.api() + '/v1/game/garden/plants', {
                    method: 'POST', headers: {'Content-Type':'application/json','X-Game-Token':game.live.token()},
                    body: JSON.stringify({actions:[
                        {action:'place',asset:'flora.pine',x:3,z:-9},
                        {action:'place',asset:'flora.flowers',x:5,z:-9},
                        {action:'place',asset:'flora.flowers',x:6,z:-9}
                    ]})
                });
                if (!response.ok) throw Error('Could not place alternate model types.');
            }
            """);
        var baseline = await ReadMapAsync();
        const string request = "aumente a altura dessas luminarias 1m cada";
        await using var measurements = trial is null ? null : await CpuTrialMeasurements.StartAsync(browser, page);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await SubmitAsync(request);
        await WaitAsync(async () =>
        {
            var reply = await dialog.Locator(".ultima-dialog__line").InnerTextAsync();
            return trial is not null
                ? await Button("Enviar para a Guardiã").IsEnabledAsync()
                : reply.EndsWith("Quer aplicar?", StringComparison.Ordinal) ||
                  reply.Contains("Nenhuma alteração foi aplicada", StringComparison.Ordinal);
        }, "The real tool turn finished.", 125_000);
        var proposal = await dialog.Locator(".ultima-dialog__line").InnerTextAsync();
        var positiveMatch = proposal.Contains("Somar 1 m em Altura da haste", StringComparison.Ordinal)
            && await Button("Aplicar alteração").IsVisibleAsync();
        var latency = clock.ElapsedMilliseconds;
        if (measurements is not null)
            Log(new { trial, mode = Mode, latency, positiveMatch, performance = await measurements.FinishAsync(),
                proposal, output = await page.EvaluateAsync<string?>("() => window.lastGardenToolOutput ?? null") });
        else
            Check(positiveMatch, "The model picks the catalogued lamp height and adds one metre.", new
            {
                proposal,
                output = await page.EvaluateAsync<string?>("() => window.lastGardenToolOutput ?? null")
            });
        Check((await ReadMapAsync()).Revision == baseline.Revision, "A tool proposal leaves the world unchanged.");
        var applied = baseline;
        if (positiveMatch)
        {
            var modelCalls = await page.EvaluateAsync<int>("() => window.gardenToolTurns ?? 0");
            await SubmitAsync("faça");
            await dialog.GetByText("Apliquei as mudanças no jardim. Atualizei a propriedade: altura da haste", new() { Exact = false }).WaitForAsync();
            Check(await page.EvaluateAsync<int>("() => window.gardenToolTurns ?? 0") == modelCalls,
                "A typed confirmation reuses the proposal without requesting another model turn.");
            applied = await ReadMapAsync();
            Check(applied.Placements.Count(p => p.Asset == "prop.lantern" && p.Parameters?.PoleHeight == 2.4 &&
                p.Parameters.LightIntensity is null) == 2 && applied.Revision == baseline.Revision + 2,
                "The real model tool increased both poles by one metre and kept default intensity.", applied);
            if (trial is not null) Log(new { trial, typedConfirmationApplied = true, applied.Revision });
        }

        if (trial is not null)
        {
            var cases = new[]
            {
                new TrialCase("abaixe a altura das luminárias 30 cm", "prop.lantern/poleHeight", "add", "-0.3"),
                new TrialCase("deixe as flores na cor #AA00FF", "flora.flowers/tintRgb", "set", "#AA00FF"),
                new TrialCase("faça o mesmo nas outras flores", "flora.flowers/tintRgb", "set", "#AA00FF", UseHistory: true),
                new TrialCase("Não aumente a altura dessas luminárias.")
            };
            var passed = positiveMatch;
            foreach (var item in cases)
            {
                if (await Button("Descartar proposta").IsVisibleAsync())
                    await Button("Descartar proposta").ClickAsync();
                if (!item.UseHistory)
                {
                    // An aborted generation may still hold the adapter busy. Reload before an independent case.
                    if (await page.EvaluateAsync<string>("() => game.aiCpu.status().state") != "ready")
                    {
                        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
                        await OpenGuardianAsync();
                        Check(await page.EvaluateAsync<bool>("() => game.aiCpu.load()"),
                            "The CPU model could not reload for an independent case.");
                        Log(new { trial, recoveredBefore = item.Text });
                    }
                    await ClearConversationAsync(page);
                }
                var turns = await page.EvaluateAsync<int>("() => window.gardenToolTurns ?? 0");
                clock.Restart();
                await SubmitAsync(item.Text);
                await WaitAsync(() => Button("Enviar para a Guardiã").IsEnabledAsync(),
                    "The qualification turn finished.", 125_000);
                var newTurn = await page.EvaluateAsync<int>("() => window.gardenToolTurns ?? 0") > turns;
                var output = newTurn ? await page.EvaluateAsync<string?>("() => window.lastGardenToolOutput") : null;
                var canApply = await Button("Aplicar alteração").IsVisibleAsync();
                var match = item.Matches(output) && canApply == (item.Property is not null);
                passed &= match;
                Log(new { trial, item.Text, match, canApply, output, latency = clock.ElapsedMilliseconds });
                Check((await ReadMapAsync()).Revision == applied.Revision, "An unconfirmed turn cannot edit the world.");
            }
            Check(errors.IsEmpty, "No browser errors during CPU qualification.", errors);
            Log(new { trial, qualified = passed });
            Check(passed, "The CPU model failed the Portuguese tool qualification cases.");
            return;
        }

        await SubmitAsync("Não aumente a altura dessas luminárias.");
        await WaitAsync(() => Button("Enviar para a Guardiã").IsEnabledAsync(),
            "The negated turn finished.", 125_000);
        if (await Button("Descartar proposta").IsVisibleAsync())
            await Button("Descartar proposta").ClickAsync();
        Check((await ReadMapAsync()).Revision == applied.Revision, "An unconfirmed reply cannot edit the world.");
        Check(errors.IsEmpty, "No errors during native CPU tool integration.", errors);
        Log(new { build, mode = Mode, model = "Qwen2.5 0.5B", latency, applied.Revision });

        async Task OpenGuardianAsync()
        {
            await WaitAsync(() => page.EvaluateAsync<bool>("() => !!window.game?.live?.token()"), "Garden session started.");
            await WaitAsync(() => page.EvaluateAsync<bool>("() => game.teleportToGuardian()"), "Guardian is reachable.");
            await page.Locator("#game-canvas").FocusAsync();
            await page.Locator(".ultima-hint").GetByText("para conversar").WaitForAsync();
            await page.Keyboard.PressAsync("e");
            await dialog.WaitForAsync();
        }

        ILocator Button(string name) => dialog.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });

        async Task SubmitAsync(string text)
        {
            await dialog.Locator("#dialog-input").FillAsync(text);
            await Button("Enviar para a Guardiã").ClickAsync();
        }

        Task<Map> ReadMapAsync() => ReadAsync<Map>(page,
            "async () => (await fetch(game.live.api() + '/v1/game/map', {cache:'no-store'})).json()");
    }

    // Keep the model warm while giving independent cases empty Genius history.
    private static Task ClearConversationAsync(IPage page) => page.EvaluateAsync("""
        async () => {
            const module = await import('./js/guardian-genius.mjs');
            const id = module.guardianConversationId();
            await new Promise((resolve, reject) => {
                const request = indexedDB.open('ultima-genius', 1);
                request.onsuccess = () => {
                    const db = request.result;
                    const tx = db.transaction('sessions', 'readwrite');
                    tx.objectStore('sessions').delete(id);
                    tx.oncomplete = () => { db.close(); resolve(); };
                    tx.onabort = () => { db.close(); reject(tx.error); };
                };
                request.onerror = () => reject(request.error);
            });
        }
        """);

    private sealed record TrialCase(string Text, string? Property = null, string? Operation = null,
        string? Value = null, bool UseHistory = false)
    {
        internal bool Matches(string? output)
        {
            if (output is null) return false;
            try
            {
                using var envelope = JsonDocument.Parse(output);
                var calls = envelope.RootElement.GetProperty("calls");
                if (Property is null) return calls.GetArrayLength() == 0;
                if (calls.GetArrayLength() != 1) return false;
                var function = calls[0].GetProperty("function");
                if (function.GetProperty("name").GetString() != "customize_model") return false;
                using var arguments = JsonDocument.Parse(function.GetProperty("arguments").GetString() ?? "");
                var args = arguments.RootElement;
                var value = args.GetProperty("value");
                return args.GetProperty("property").GetString() == Property
                    && args.GetProperty("operation").GetString() == Operation
                    && (Value!.StartsWith('#')
                        ? value.ValueKind == JsonValueKind.String && string.Equals(value.GetString(), Value, StringComparison.OrdinalIgnoreCase)
                        : value.ValueKind == JsonValueKind.Number && value.GetDouble() == double.Parse(Value, System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                return false;
            }
        }
    }

    private sealed record Map(int Revision, Placement[] Placements);
    private sealed record Placement(string Asset, Parameters? Parameters);
    private sealed record Parameters(double PoleHeight, double? LightIntensity);
}
