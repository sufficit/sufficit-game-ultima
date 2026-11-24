using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static BrowserTest;

internal static class WorldUndo
{
    internal static async Task RunAsync(IBrowser browser)
    {
        var site = Environment.GetEnvironmentVariable("ULTIMA_SITE");
        var api = Environment.GetEnvironmentVariable("ULTIMA_API");
        Check(Uri.TryCreate(site, UriKind.Absolute, out var pageUrl) && api is not null &&
            Regex.IsMatch(api, @"^http://127\.0\.0\.1:[0-9]+$"), "o teste precisa de um backend descartável local");
        var proxy = pageUrl!.Scheme == "https" ? new Uri(pageUrl, "__world-test-api__/") : null;
        var url = new UriBuilder(pageUrl) { Query = "rede=0&api=" + Uri.EscapeDataString(proxy?.AbsoluteUri ?? api!) };

        var owner = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1200, Height = 720 } });
        var witness = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1200, Height = 720 } });
        var ownerErrors = TrackErrors(owner);
        var witnessErrors = TrackErrors(witness);
        try
        {
            async Task OpenAsync(IPage page)
            {
                if (proxy is not null)
                {
                    // Só no ensaio: HTTPS usa a origem do site para encaminhar ao servidor local.
                    await page.RouteAsync(proxy.AbsoluteUri + "**", async route =>
                    {
                        var request = new Uri(route.Request.Url);
                        var path = request.AbsolutePath[proxy.AbsolutePath.Length..];
                        var response = await route.FetchAsync(new() { Url = $"{api}/{path}{request.Query}" });
                        await route.FulfillAsync(new() { Response = response });
                    });
                }
                await page.GotoAsync(url.Uri.AbsoluteUri);
                await CheckBuildAsync(page);
                await WaitAsync(() => page.EvaluateAsync<bool>("() => game?.mode === 'worker' && !!game?.live?.token()"),
                    "sessão conectada no worker", 30_000, 150);
                await WaitUntilAsync(async () =>
                    (await ReadAsync<MapState?>(page, "() => game.build.state()"))?.Chunks >= 25, "chunks não chegaram");
            }
            await Task.WhenAll(OpenAsync(owner), OpenAsync(witness));

            var before = await ProjectAsync(owner, 0, 1, .05);
            Check(Math.Abs((await ProjectAsync(witness, 0, 1, .05)).Height - before.Height) < .02,
                "os dois clientes iniciam com a mesma altura");
            await Button(owner, "Abrir painel do jogo").ClickAsync();
            await owner.GetByText("Oficina do mundo", new() { Exact = true }).ClickAsync();
            var sculpt = owner.GetByText("Esculpir o terreno", new() { Exact = true });
            await sculpt.ClickAsync();
            var undo = Button(owner, "Desfazer edição");
            Check(!await undo.IsEnabledAsync(), "histórico inicialmente vazio");
            await Button(owner, "Levantar").ClickAsync();
            var canvas = await owner.Locator("#game-canvas").BoundingBoxAsync()
                ?? throw new InvalidOperationException("canvas disponível");
            await owner.Mouse.ClickAsync(canvas.X + before.X, canvas.Y + before.Y);
            await WaitUntilAsync(() => undo.IsEnabledAsync(), "o servidor não confirmou a edição");
            await WaitUntilAsync(async () => (await ProjectAsync(owner, 0, 1, .05)).Height > before.Height + .5 &&
                (await ProjectAsync(witness, 0, 1, .05)).Height > before.Height + .5, "a edição não apareceu nos dois clientes");

            await undo.ClickAsync();
            await WaitUntilAsync(async () => !await undo.IsEnabledAsync(), "o servidor não confirmou o undo");
            await WaitUntilAsync(async () => Math.Abs((await ProjectAsync(owner, 0, 1, .05)).Height - before.Height) < .02 &&
                Math.Abs((await ProjectAsync(witness, 0, 1, .05)).Height - before.Height) < .02, "o replay não restaurou os dois clientes");

            await Button(owner, "Corredor curvo").ClickAsync();
            await WaitUntilAsync(async () => (await ReadAsync<BuildMode>(owner, "() => game.build.state()")).Mode == "tunnel",
                "o modo corredor não foi selecionado");
            var command = owner.WaitForResponseAsync(response => response.Url.EndsWith("/v1/game/command", StringComparison.Ordinal) &&
                response.Request.PostDataJSON()?.TryGetProperty("action", out var action) == true && action.GetString() == "tunnel");
            foreach (var (x, z) in new[] { (0, 5), (3, 12), (10, 19) })
            {
                var screen = await ProjectAsync(owner, x, z);
                Check(screen is { X: > 0 and < 1200, Y: > 0 and < 720 }, "ponto projetado na tela", screen);
                var click = new { x = (double)canvas.X + screen.X, y = (double)canvas.Y + screen.Y };
                await WaitUntilAsync(() => owner.EvaluateAsync<bool>(
                    "({ x, y }) => document.elementFromPoint(x, y)?.id === 'game-canvas'", click),
                    "o ponto do corredor ficou sob um controle");
                await owner.Mouse.ClickAsync((float)click.x, (float)click.y);
            }
            var result = await command;
            Check(result.Status == 200, "o servidor aceitou o corredor", await result.TextAsync());

            await WaitUntilAsync(async () => (await Tunnel.ChunkAsync(owner))?.Volume == true &&
                (await Tunnel.ChunkAsync(witness))?.Volume == true, "o corredor da Oficina não apareceu nos dois clientes");
            await Button(owner, "Abrir painel do jogo").ClickAsync();
            if (!await undo.IsVisibleAsync())
            {
                if (!await sculpt.IsVisibleAsync()) await owner.GetByText("Oficina do mundo", new() { Exact = true }).ClickAsync();
                await sculpt.ClickAsync();
            }
            await undo.ClickAsync();
            await WaitUntilAsync(async () => (await Tunnel.ChunkAsync(owner))?.Volume == false &&
                (await Tunnel.ChunkAsync(witness))?.Volume == false, "o corredor continuou na malha após undo");
            Check(ownerErrors.IsEmpty && witnessErrors.IsEmpty, "erros no navegador", new { ownerErrors, witnessErrors });
            Log(new { mode = "worker", clients = 2, sculpt = true, tunnel = true, undo = true });
        }
        finally
        {
            var options = new PageUnrouteAllOptions { Behavior = UnrouteBehavior.IgnoreErrors };
            await Task.WhenAll(owner.UnrouteAllAsync(options), witness.UnrouteAllAsync(options));
        }
    }

    private static ILocator Button(IPage page, string name) =>
        page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });

    private static Task WaitUntilAsync(Func<Task<bool>> check, string message) => WaitAsync(check, message, 15_000, 150);

    private static Task<Projection> ProjectAsync(IPage page, double x, double z, double? height = null) =>
        ReadAsync<Projection>(page, """
            ({ x, z, height }) => {
                const scene = BABYLON.EngineStore.LastCreatedScene;
                const ray = new BABYLON.Ray(new BABYLON.Vector3(x, 100, z), new BABYLON.Vector3(0, -1, 0));
                const hit = scene.pickWithRay(ray, mesh => height == null ? mesh.name.startsWith('ground-') : mesh.name === 'ground-0,0');
                const target = height == null ? hit.pickedPoint : new BABYLON.Vector3(x, height, z);
                const engine = scene.getEngine();
                const viewport = scene.activeCamera.viewport.toGlobal(engine.getRenderWidth(), engine.getRenderHeight());
                const point = BABYLON.Vector3.Project(target, BABYLON.Matrix.Identity(), scene.getTransformMatrix(), viewport);
                return { x: point.x, y: point.y, height: hit.pickedPoint.y };
            }
            """, new { x, z, height }, page.Workers[0]);

    private sealed record Projection(float X, float Y, double Height);
    private sealed record BuildMode(string Mode);
}
