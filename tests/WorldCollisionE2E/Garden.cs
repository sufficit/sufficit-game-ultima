using System.Text.Json;
using Microsoft.Playwright;
using static BrowserTest;

internal static class Garden
{
    internal static async Task RunAsync(IBrowser browser)
    {
        // The scenario writes to a disposable loopback API. Use a provider fixture
        // that returns 503 for "provedor fora" and a path, lamp, and flowers for the garden request.
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
                game.aiCpu.garden = (prompt, text) => {
                    window.lastGardenCpuPrompt = text;
                    if (text === 'Deixe a samambaia duas vezes mais alta e gire um pouco') {
                        const prefix = 'Objetos colocados no jardim: ';
                        const line = prompt.split('\n').find(item => item.startsWith(prefix));
                        const fern = JSON.parse(line.slice(prefix.length)).find(p => p.asset === 'flora.fern' && p.x === 3);
                        return JSON.stringify({ reply: 'Ajustei a samambaia.',
                            actions: [{action: 'customize', uid: fern.uid, parameters: {scaleY: 2, rotationY: 1.2}}] });
                    }
                    if (text === 'Plante outra samambaia mantendo o tamanho padrão')
                        return JSON.stringify({ reply: 'Plantei a outra samambaia.',
                            actions: [{action: 'place', asset: 'flora.fern', x: 6, z: -12}] });
                    return prompt.includes('prop.lantern') && text === 'Plante uma samambaia'
                        ? JSON.stringify({ reply: 'Uma samambaia para o jardim.',
                            actions: [{action: 'place', asset: 'flora.fern', x: 3, z: -12}] })
                        : 'Resposta de teste sem JSON';
                };
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
        var illuminate = page.GetByRole(AriaRole.Button, new() { Name = "Iluminar o jardim", Exact = true });
        var draft = page.Locator("#dialog-input");
        await draft.FillAsync("Rascunho de outro pedido");
        var lightsResponse = page.WaitForResponseAsync(r => r.Url.EndsWith("/v1/game/garden/decor") && r.Status == 200);
        await illuminate.ClickAsync();
        await page.GetByText("As duas luminárias estão na entrada do jardim.", new() { Exact = false }).WaitForAsync();
        await lightsResponse;
        Check(await draft.InputValueAsync() == "Rascunho de outro pedido", "a ação preserva o rascunho");
        var lit = await MapAsync(page);
        var lampIds = lit.Placements.Where(p => p.Asset == "prop.lantern").Select(p => p.Uid).ToArray();
        Check(lampIds.Length == 2, "um clique grava duas luminárias no mapa");
        await WaitAsync(() => RenderAsync<bool>(page, """
            ids => {
                const scene = BABYLON.EngineStore.LastCreatedScene;
                const lights = scene.lights.filter(light => light.name.startsWith('garden-light-') && light.isEnabled());
                return ids.every(id => {
                    const mesh = scene.getMeshByName('place-' + id);
                    return mesh?.isVisible && lights.some(light => light.position.x === mesh.position.x &&
                        light.position.z === mesh.position.z && light.intensity > 0);
                });
            }
            """, lampIds), "as duas luminárias visíveis e acesas");
        await illuminate.ClickAsync();
        await page.GetByText("A entrada do jardim já está iluminada.", new() { Exact = true }).WaitForAsync();
        Check((await MapAsync(page)).Revision == lit.Revision, "repetir a ação não duplica luminárias");

        await page.EvaluateAsync("() => { window.gardenCpuReady = true; }");
        await AskAsync("essas luminarias possuem o meu tamanho, quero luminarias mais altas e um pouco mais fortes");
        await page.GetByText("Ajustei a altura das hastes e a intensidade da luz", new() { Exact = false }).WaitForAsync();
        Check(!await page.EvaluateAsync<bool>("() => !!window.lastGardenCpuPrompt"),
            "ajuste relativo funciona mesmo com um modelo local ruim selecionado");
        var customized = await MapAsync(page);
        Check(customized.Placements.Where(p => lampIds.Contains(p.Uid)).Count(p =>
            p.Parameters is { PoleHeight: 2, LightIntensity: 1.31 }) == 2, "mesmos UIDs com parâmetros personalizados");
        await WaitAsync(() => RenderAsync<bool>(page, """
            ids => {
                const scene = BABYLON.EngineStore.LastCreatedScene;
                const lights = scene.lights.filter(light => light.name.startsWith('garden-light-') && light.isEnabled());
                return ids.every(id => {
                    const mesh = scene.getMeshByName('place-' + id);
                    return mesh?.isVisible && mesh.getBoundingInfo().boundingBox.extendSize.y > 1.2 &&
                        lights.some(light => light.position.x === mesh.position.x && light.position.z === mesh.position.z &&
                            Math.abs(light.position.y - mesh.position.y - 2.17) < 0.001 && light.intensity === 1.31);
                });
            }
            """, lampIds), "hastes mais altas e intensidade aplicada na cena");

        await page.EvaluateAsync("() => { window.gardenCpuReady = false; }");
        var response = page.WaitForResponseAsync(r => r.Url.EndsWith("/v1/game/garden/dialog") && r.Request.Method == "POST");
        await AskAsync("Luminária, flores e trilha");
        var result = await response;
        var body = await result.TextAsync();
        Check(result.Ok, "The local test backend must use a provider fixture for the garden plan", new { result.Status, body });
        using var reply = JsonDocument.Parse(body);
        Check(reply.RootElement.GetProperty("applied").GetInt32() == 7, "diálogo gravou cinco pincéis e dois objetos");
        var answer = reply.RootElement.GetProperty("reply").GetString();
        await page.GetByText($"Apliquei as mudanças no jardim. {answer}", new() { Exact = true }).WaitForAsync();
        var edited = await MapAsync(page);
        var previous = lit.Placements.Select(p => p.Uid).ToHashSet(StringComparer.Ordinal);
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
        var serverCalls = 0;
        page.Request += (_, req) =>
        {
            if (req.Url.EndsWith("/v1/game/garden/dialog")) Interlocked.Increment(ref serverCalls);
        };
        await AskAsync("provedor fora");
        await page.GetByText("A IA local não conseguiu concluir o pedido.", new() { Exact = false }).WaitForAsync();
        Check(serverCalls == 0, "falha local não transmite a fala ao provedor remoto");
        Check((await MapAsync(page)).Revision == edited.Revision, "resposta inválida preserva o jardim");
        await AskAsync("Plante uma samambaia");
        await page.GetByText("✦ origem da resposta: Genius local · CPU").WaitForAsync();
        var fern = (await MapAsync(page)).Placements.Single(p => p.Asset == "flora.fern");
        await AskAsync("Plante outra samambaia mantendo o tamanho padrão");
        await page.GetByText("Plantei a outra samambaia.", new() { Exact = false }).WaitForAsync();
        var original = (await MapAsync(page)).Placements.Single(p => p.Asset == "flora.fern" && p.Uid != fern.Uid);
        await AskAsync("Deixe a samambaia duas vezes mais alta e gire um pouco");
        await page.GetByText("Ajustei a samambaia.", new() { Exact = false }).WaitForAsync();
        var changed = (await MapAsync(page)).Placements.Single(p => p.Uid == fern.Uid);
        Check(changed.Parameters is { ScaleY: 2, RotationY: 1.2 }, "o plano alterou os parâmetros da mesma samambaia");
        await WaitAsync(() => RenderAsync<bool>(page, """
            ([uid, defaultUid]) => {
                const scene = BABYLON.EngineStore.LastCreatedScene;
                const mesh = scene.getMeshByName('place-' + uid);
                const other = scene.getMeshByName('place-' + defaultUid);
                return mesh?.isVisible && mesh.scaling.x === 1 && mesh.scaling.y === 2 &&
                    mesh.scaling.z === 1 && mesh.rotation.y === 1.2 &&
                    other?.isVisible && other.scaling.y === 1 && other.rotation.y === 0;
            }
            """, new[] { fern.Uid, original.Uid }), "a samambaia alterada e a variante padrão coexistem");
        Check(errors.IsEmpty, "erros no navegador", errors);
        Log(new { mode = Mode, edited.Revision, lantern = lantern.Uid, flowers = flowers.Uid });
    }

    private static Task<Map> MapAsync(IPage page) =>
        ReadAsync<Map>(page, "async () => (await fetch(game.live.api() + '/v1/game/map')).json()");

    private sealed record Map(long Revision, Placement[] Placements);
    private sealed record Placement(string Uid, string Asset, Parameters? Parameters);
    private sealed record Parameters(double PoleHeight, double LightIntensity, double ScaleY, double RotationY);
}
