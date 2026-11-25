using System.Text.Json;
using Microsoft.Playwright;
using static BrowserTest;

internal static class Garden
{
    internal static async Task RunAsync(IBrowser browser)
    {
        // O cenário grava no journal. Exige uma API de teste na própria máquina.
        var api = Environment.GetEnvironmentVariable("ULTIMA_GARDEN_API");
        if (!Uri.TryCreate(api, UriKind.Absolute, out var endpoint) || !endpoint.IsLoopback)
            throw new InvalidOperationException("ULTIMA_GARDEN_API deve apontar para a API de teste em loopback.");
        var url = new UriBuilder(GameUrl(forceWebGl: true))
        {
            Query = $"rede=0&gpu=0&api={Uri.EscapeDataString(endpoint.GetLeftPart(UriPartial.Authority))}" +
                (MainThread ? "&render=main" : "")
        };
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 960, Height = 540 } });
        var errors = TrackErrors(page);
        await page.AddInitScriptAsync("""
            document.addEventListener('DOMContentLoaded', () => {
                const status = game.aiCpu.status;
                game.aiCpu.status = () => window.gardenCpuReady
                    ? { state: 'ready', progress: 1, detail: 'Motor de teste' } : status();
                game.aiCpu.garden = (prompt, text) => prompt.includes('prop.lantern') && text === 'Plante uma samambaia'
                    ? JSON.stringify({ reply: 'Uma samambaia para o jardim.',
                        actions: [{action: 'place', asset: 'flora.fern', x: 3, z: -12}] })
                    : 'Resposta de teste sem JSON';
            }, { once: true });
            """);
        await page.GotoAsync(url.Uri.AbsoluteUri, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await WaitAsync(() => page.EvaluateAsync<bool>("() => !!window.game?.live?.token()"), "sessão do jardim ativa");
        await WaitAsync(() => page.EvaluateAsync<bool>("async () => (await game.build.state())?.chunks >= 25"), "jardim carregado");
        Check(await page.EvaluateAsync<string>("() => game.mode") == Mode, "modo do renderizador", Mode);
        Check(await page.EvaluateAsync<bool>("() => game.teleportToGuardian()"), "chega à Guardiã");
        await WaitAsync(async () => await ReadAsync<Position?>(page, """
            async () => (await (await fetch(game.live.api() + '/v1/game/state',
                { headers: { 'X-Game-Token': game.live.token() } })).json()).me
            """) is { Z: < -10 }, "pose chegou ao servidor");
        await page.Locator("#game-canvas").FocusAsync();
        await page.Keyboard.PressAsync("e");
        await page.Locator(".ultima-dialog").WaitForAsync();

        async Task AskAsync(string message)
        {
            await page.Locator("#dialog-input").FillAsync(message);
            await page.GetByRole(AriaRole.Button, new() { Name = "Enviar para a Guardiã", Exact = true }).ClickAsync();
        }

        var initial = await MapAsync(page);
        await AskAsync("provedor fora");
        await page.GetByText("Minha IA está indisponível.", new() { Exact = false }).WaitForAsync();
        Check(!(await MapAsync(page)).Placements.Any(p => p.Asset == "prop.lantern" &&
            initial.Placements.All(old => old.Uid != p.Uid)), "IA indisponível não cria luminárias");
        var response = page.WaitForResponseAsync(r => r.Url.EndsWith("/v1/game/garden/dialog") && r.Status == 200);
        await AskAsync("Luminária, flores e trilha");
        await page.GetByText("Apliquei as mudanças no jardim.", new() { Exact = false }).WaitForAsync();
        var edited = await MapAsync(page);
        using var reply = JsonDocument.Parse(await (await response).TextAsync());
        Check(reply.RootElement.GetProperty("applied").GetInt32() == 7, "diálogo gravou cinco pincéis e dois objetos");
        var previous = initial.Placements.Select(p => p.Uid).ToHashSet(StringComparer.Ordinal);
        var lantern = edited.Placements.Single(p => p.Asset == "prop.lantern" && !previous.Contains(p.Uid));
        var flowers = edited.Placements.Single(p => p.Asset == "flora.flowers" && !previous.Contains(p.Uid));
        await WaitAsync(async () => await RenderAsync<bool>(page, """
            ([lamp, flowers]) => {
                const scene = BABYLON.EngineStore.LastCreatedScene;
                return scene.getMeshByName('place-' + lamp)?.isVisible === true &&
                    scene.getMeshByName('place-' + flowers)?.isEnabled() === true &&
                    scene.lights.some(light => light.name.startsWith('garden-light-') && light.isEnabled());
            }
            """, new[] { lantern.Uid, flowers.Uid }), "luminária ligada e flores aplicadas à cena");
        await page.EvaluateAsync("() => { window.gardenCpuReady = true; }");
        await AskAsync("provedor fora");
        await page.GetByText("A IA local está ativa, mas não gerou um plano válido", new() { Exact = false }).WaitForAsync();
        Check((await MapAsync(page)).Revision == edited.Revision, "resposta inválida preserva o jardim");
        await AskAsync("Plante uma samambaia");
        await page.GetByText("✦ origem da resposta: CPU do jogador").WaitForAsync();
        Check((await MapAsync(page)).Placements.Any(p => p.Asset == "flora.fern"), "plano da CPU aplicado pela API");
        Check(errors.IsEmpty, "erros no navegador", errors);
        Log(new { mode = Mode, edited.Revision, lantern = lantern.Uid, flowers = flowers.Uid });
    }

    private static Task<Map> MapAsync(IPage page) =>
        ReadAsync<Map>(page, "async () => (await fetch(game.live.api() + '/v1/game/map')).json()");

    private sealed record Map(long Revision, Placement[] Placements);
    private sealed record Placement(string Uid, string Asset);
}
