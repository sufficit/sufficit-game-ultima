using System.Text.Json;
using Microsoft.Playwright;
using static BrowserServicesFixture;
using static BrowserTest;

internal static class BrowserServices
{
    internal static async Task RunAsync(IBrowser browser)
    {
        var root = new Uri(Environment.GetEnvironmentVariable("ULTIMA_SITE") ??
            "https://sufficit.github.io/sufficit-game-ultima/");
        await using var context = await browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1200, Height = 720 }
        });
        var page = await context.NewPageAsync();
        var errors = TrackErrors(page);
        var model = await context.APIRequest.GetAsync(new Uri(root, "assets/prop.stump.glb").AbsoluteUri);
        var glb = await model.BodyAsync();
        Check(model.Ok && glb.AsSpan().StartsWith("glTF"u8), "arquivo GLB disponível", model.Status);
        var fixture = new BrowserServicesFixture(root, glb);
        await fixture.ConfigureAsync(page);
        try
        {
            var url = new UriBuilder(root)
            {
                Query = "api=" + Uri.EscapeDataString(fixture.Api) + "&rede=0&sala=jardim&migrationTest=1" +
                    (MainThread ? "&render=main&gpu=0" : "")
            };
            await page.GotoAsync(url.Uri.AbsoluteUri, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 90_000 });
            var expected = Environment.GetEnvironmentVariable("ULTIMA_BUILD");
            Check(string.IsNullOrEmpty(expected) || fixture.Build == expected, "build publicado antes da simulação", fixture.Build);
            await CheckUpdateAndQueueAsync(page);
            await CheckSessionAndMapAsync(page, fixture);
            await CheckWorkshopAsync(page, fixture, glb);
            await CheckInviteAndFlushAsync(page, fixture);
            Check(errors.IsEmpty, "erros no navegador", errors);
            Log(new { build = fixture.Build, mode = Mode, update = true, dialogQueue = true, session = true, map = true,
                commands = true, sha256 = true, generation = true, upload = true, invite = true });
        }
        finally { await page.UnrouteAllAsync(new() { Behavior = UnrouteBehavior.IgnoreErrors }); }
    }

    private static async Task CheckUpdateAndQueueAsync(IPage page)
    {
        await UntilAsync(async () => await QueuedAsync(page) == 1, "fila anterior restaurada");
        var notice = page.GetByRole(AriaRole.Alert).GetByText("Nova versão disponível (fixture).");
        await notice.WaitForAsync();
        // Screen stack: with a window open, the notice stays behind it like the rest of the game
        // (out of the focus order and of the accessibility tree) and comes back when it closes.
        var update = page.Locator(".ultima-update");
        var openPanel = Button(page, "Abrir painel do jogo");
        var panelWasOpen = !await openPanel.IsVisibleAsync();
        if (!panelWasOpen) await openPanel.ClickAsync();
        await Button(page, "Diário").ClickAsync();
        var journal = page.GetByRole(AriaRole.Dialog, new() { Name = "Diário", Exact = true });
        await WaitAsync(async () => await update.EvaluateAsync<bool>("el => el.inert"),
            "atrás do Diário: o aviso de nova versão fica inerte", 10_000);
        Check(await update.EvaluateAsync<int>("el => Number(getComputedStyle(el).zIndex)") <
              await journal.EvaluateAsync<int>("el => Number(getComputedStyle(el).zIndex)"),
            "atrás do Diário: o aviso de nova versão fica abaixo da janela");
        await journal.GetByRole(AriaRole.Button, new() { Name = "Fechar · Esc", Exact = true }).ClickAsync();
        await journal.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        Check(!await update.EvaluateAsync<bool>("el => el.inert"),
            "ao fechar o Diário o aviso de nova versão volta a receber entrada");
        // Opening the journal closes the panel, so the panel is closed by now: reopen it if it was open.
        if (panelWasOpen) await openPanel.ClickAsync();
        await Button(page, "Depois").ClickAsync();
        await notice.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await page.EvaluateAsync("""
            async () => {
                await game.dialogLog.turn('Guard<i>ã', 'oi\t <caminhante>', 'Sim', 'servidor · regras', 100);
                await game.dialogLog.turn('Guard<i>ã', 'oi\t <caminhante>', 'Sim', 'servidor · regras', 100);
            }
            """);
        var recorded = await ReadAsync<DialogEntry[]>(page, "key => JSON.parse(localStorage.getItem(key))", QueueKey);
        Check(recorded.Length == 2 && recorded[0].Id == Previous.Id &&
            recorded[1] is { Npc: "Guardiã", Player: "oi caminhante" }, "preserva, sanitiza e deduplica os turnos", recorded);
        Check(!await page.EvaluateAsync<bool>("() => game.dialogLog.flush()") && await QueuedAsync(page) == 2,
            "falha no envio mantém a fila");
        await UntilAsync(async () => (await ReadAsync<AssetsState>(page, "() => game.glb.status()")).Loaded.Contains(AssetId + " (runtime)"),
            "catálogo e GLB verificado");
        Check(!await page.EvaluateAsync<bool>("async id => !!(await game.glb.get(id))", InvalidId), "GLB com hash inválido foi recusado");
    }

    private static async Task CheckSessionAndMapAsync(IPage page, BrowserServicesFixture fixture)
    {
        await UntilAsync(async () => await page.EvaluateAsync<string?>("() => game.live.token()") == Token, "sessão da API iniciada");
        Check(await page.EvaluateAsync<string>("() => game.mode") == Mode, "modo do renderizador", Mode);
        await page.GetByText("Olá do servidor fictício").WaitForAsync();
        await UntilAsync(() => RenderAsync<bool>(page, """
            () => {
                const scene = BABYLON.EngineStore.LastCreatedScene;
                return !!scene.getMeshByName('place-migration-map') && !!scene.getTransformNodeByName('remote-other');
            }
            """), "pose remota e mapa aplicados ao motor");

        await page.EvaluateAsync("() => game.net.sendChat('Olá da aplicação')");
        await UntilAsync(() => Task.FromResult(fixture.Commands.Any(command => command is { Action: "say", Token: Token })),
            "comando de chat autenticado");
        Check(fixture.Commands.Any(command => command is { Action: "pose", Yaw: not null }), "pose inclui a rotação");
        await UntilAsync(() => RenderAsync<bool>(page, """
            () => {
                const scene = BABYLON.EngineStore.LastCreatedScene;
                return !!scene?.pick(650, 380, mesh => mesh.name.startsWith('ground-'))?.hit;
            }
            """), "terreno visível sob o ponto de escultura", 120_000);
        await page.EvaluateAsync("""
            async () => {
                await game.build.mode('raise', { r: 1.5 });
                game.terrainClick(650, 380);
            }
            """);
        await UntilAsync(() => Task.FromResult(fixture.Commands.Any(command => command is { Action: "sculpt", R: 1.5, Token: Token })),
            "edição autenticada enviada pela Oficina");
        await page.EvaluateAsync("() => game.build.mode('off')");
    }

    private static async Task CheckWorkshopAsync(IPage page, BrowserServicesFixture fixture, byte[] glb)
    {
        await Button(page, "Abrir painel do jogo").ClickAsync();
        await page.GetByText("Oficina do mundo", new() { Exact = true }).ClickAsync();
        await page.GetByText("Criar modelo", new() { Exact = true }).ClickAsync();
        await page.Locator("#workshop-prompt").FillAsync(Prompt);
        await Button(page, "Gerar").ClickAsync();
        await page.GetByText($"✓ \"{Prompt}\" criado e plantado no mundo!").WaitForAsync();
        var generation = fixture.Generations;
        var expected = JsonSerializer.SerializeToElement(new { prompt = Prompt });
        Check(generation is [{ Token: Token, Body: { } request }] && JsonElement.DeepEquals(expected, request),
            "geração envia o prompt e o token", generation);

        await page.GetByText("Enviar modelo", new() { Exact = true }).ClickAsync();
        await page.Locator("#workshop-file").SetInputFilesAsync(new FilePayload
        {
            Name = "toco.glb", MimeType = "model/gltf-binary", Buffer = glb
        });
        await page.GetByText("✓ \"toco\" publicado no mundo!").WaitForAsync();
        Check(fixture.Uploads.SequenceEqual(new[] { new Upload("toco", Token, "model/gltf-binary", glb.Length) }),
            "upload binário autenticado", fixture.Uploads);
    }

    private static async Task CheckInviteAndFlushAsync(IPage page, BrowserServicesFixture fixture)
    {
        await page.GetByText("Multiplayer e voz", new() { Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Copiar convite" }).ClickAsync();
        var copied = new Uri(await page.EvaluateAsync<string>("() => window.migrationCopied"));
        var query = System.Web.HttpUtility.ParseQueryString(copied.Query);
        Check(query["sala"] == "jardim" && query["api"] == fixture.Api, "convite preserva sala e API", copied);
        fixture.AcceptBatches();
        await page.EvaluateAsync("() => window.dispatchEvent(new Event('pagehide'))");
        await UntilAsync(async () => await QueuedAsync(page) == 0, "envio e confirmação por pagehide");
        Check(fixture.Batches.LastOrDefault()?.Turns.Length == 2, "reenvio contém os dois turnos", fixture.Batches);
    }

    private static Task<int?> QueuedAsync(IPage page) =>
        ReadAsync<int?>(page, "async () => (await window.game?.dialogLog?.debug())?.queued");

    private static Task UntilAsync(Func<Task<bool>> check, string label, int timeoutMs = 20_000) =>
        WaitAsync(check, label, timeoutMs, 200);

    private static ILocator Button(IPage page, string name) => page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });
}
