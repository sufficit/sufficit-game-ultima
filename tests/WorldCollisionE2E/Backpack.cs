using System.Globalization;
using System.Text.Json;
using Microsoft.Playwright;
using static BrowserTest;

internal sealed partial class Backpack(IBrowser browser)
{
    private static readonly bool Touch = Environment.GetEnvironmentVariable("ULTIMA_TOUCH") == "1";
    private static readonly float Dpr = float.Parse(Environment.GetEnvironmentVariable("ULTIMA_DPR") ?? "1", CultureInfo.InvariantCulture);
    private const string FixtureId = "test-backpack-stump";
    private const string Mesh = "place-" + FixtureId;

    internal async Task RunAsync()
    {
        await using var context = await browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1200, Height = 720 }, HasTouch = Touch, DeviceScaleFactor = Dpr
        });
        var page = await context.NewPageAsync();
        var errors = TrackErrors(page);
        await page.AddInitScriptAsync("""
            (() => {
                window.gatherSounds = 0;
                const start = AudioBufferSourceNode.prototype.start;
                AudioBufferSourceNode.prototype.start = function (...args) {
                    if (this.buffer && this.buffer.duration < 0.11) window.gatherSounds++;
                    return start.apply(this, args);
                };
            })();
            """);
        await OpenAsync(page);
        Check(await page.EvaluateAsync<string>("() => game.mode") == Mode, "modo do renderizador", Mode);
        var build = await CheckBuildAsync(page);

        await PlaceAsync(page);
        await UntilAsync(async () => await EnabledAsync(page) == true, "toco disponível para coleta");
        await OpenBagAsync(page);
        await page.GetByText("Mochila vazia.", new() { Exact = true }).WaitForAsync();
        await StartCollectingAsync(page);
        if (!Touch) await CheckObjectPreviewAsync(page);

        await BeginGestureProbeAsync(page);
        await PointAtAsync(page, new(Name: Mesh));
        await UntilAsync(() => page.GetByText("+1 Toco", new() { Exact = true }).IsVisibleAsync(),
            "a coleta confirmada não apareceu", 60_000);
        await UntilAsync(async () => await RenderAsync<double?>(page, "() => globalThis.gatherArmMin") < -.40,
            "a coleta confirmada deve acionar o gesto");
        await RenderAsync<JsonElement>(page, """
            () => BABYLON.EngineStore.LastCreatedScene.onAfterRenderObservable.remove(globalThis.gatherObserver)
            """);
        await UntilAsync(async () => await page.EvaluateAsync<int>("() => window.gatherSounds") == 1,
            "a marca de som da coleta não tocou");
        await UntilAsync(async () => await EnabledAsync(page) == false, "coleta esconde o toco");
        await PointAtAsync(page, new(X: 1, Z: 9));
        await UntilAsync(() => page.GetByText("Aproxime-se para coletar.", new() { Exact = true }).IsVisibleAsync(),
            "a dica de distância não apareceu", 60_000);

        await LookIntoExcavationAsync(page);
        var soil = new Target(X: 1.54, Z: 1.49);
        var before = (await ProjectAsync(page, soil)).Alt;
        if (!Touch) await CheckGroundPreviewAsync(page, soil, before);
        for (var layer = 1; layer <= 3; layer++)
        {
            await PointAtAsync(page, soil);
            await UntilAsync(async () => (await RecordsAsync(page)).Count(r => r.Kind == "soil") == layer,
                $"registra a camada {layer}");
            await UntilAsync(async () => before - (await ProjectAsync(page, soil)).Alt > .35 * layer,
                $"aprofunda a camada {layer}");
        }
        var after = (await ProjectAsync(page, soil)).Alt;
        if (!Touch) await CheckGroundPreviewAsync(page, soil, after);
        var saved = await RecordsAsync(page);
        Check(saved.Count(r => r.Item == "Toco") == 1, "um toco na mochila", saved);
        var cuts = saved.Where(r => r.Kind == "soil").ToArray();
        Check(cuts.Length == 3, "três camadas na mochila", cuts);
        Check(cuts.All(cut => cut.X is double x && cut.Z is double z && double.Hypot(x - soil.X, z - soil.Z) < .10),
            "o corte deve seguir o ponto clicado", cuts);
        await StopCollectingAsync(page);
        Check(await PreviewAsync(page) is { Objects.Length: 0, Ground: null }, "parar a coleta limpa a prévia");
        await OpenBagAsync(page);
        await CheckItemsAsync(page, saved);

        var legacyPoint = new Target(X: -1, Z: 1);
        var legacyBefore = (await ProjectAsync(page, legacyPoint)).Alt;
        await CollectAsync(page, new { id = "soil:-1,0", item = "Terra", kind = "soil", x = legacyPoint.X, z = legacyPoint.Z });
        await OpenAsync(page);
        await PlaceAsync(page);
        await UntilAsync(async () => await EnabledAsync(page) == false, "toco continua coletado após recarga");
        Check(Math.Abs((await ProjectAsync(page, soil)).Alt - after) < .04, "profundidade persiste ao recarregar");
        var legacyAfter = (await ProjectAsync(page, legacyPoint)).Alt;
        Check(Math.Abs(legacyBefore - legacyAfter - .48) < .04, "preserva a profundidade da escavação antiga");

        await OpenBagAsync(page);
        await CheckItemsAsync(page, saved);
        await StartCollectingAsync(page);
        await LookIntoExcavationAsync(page);
        await PointAtAsync(page, legacyPoint);
        await UntilAsync(async () => legacyAfter - (await ProjectAsync(page, legacyPoint)).Alt > .4,
            "a coleta aprofunda uma escavação antiga");
        await StopCollectingAsync(page);
        await OpenBagAsync(page);

        var second = await context.NewPageAsync();
        await WaitForBridgeAsync(second);
        var entry = new { id = "concurrency-check", item = "Validação de duplicidade", kind = "object" };
        await Task.WhenAll(CollectAsync(page, entry), CollectAsync(second, entry));
        var final = await RecordsAsync(page);
        Check(final.Length == 7, "coleta concorrente grava uma única entrada", final);
        await page.GetByText(entry.item, new() { Exact = true }).WaitForAsync();
        Check(errors.IsEmpty, "erros no navegador", errors);
        Log(new { build, mode = Mode, touch = Touch, deviceScaleFactor = Dpr, items = saved.Select(r => r.Item),
            depth = before - after, reload = true, legacyDepth = legacyBefore - legacyAfter,
            atomicAcrossTabs = true, hoverTested = !Touch });
    }

    private static Task NavigateAsync(IPage page) => page.GotoAsync(GameUrl(),
        new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 90_000 });

    private static async Task OpenAsync(IPage page)
    {
        await NavigateAsync(page);
        await UntilAsync(async () =>
        {
            var world = await ReadAsync<WorldState?>(page, """
                async () => {
                    if (!window.game?.glb?.status || !game?.build?.state) return null;
                    return { map: await game.build.state(), glb: await game.glb.status(), models: await game.models.status() };
                }
                """);
            return world is { Map.Chunks: >= 25, Glb: not null, Models.Queue: 0 } &&
                world.Glb.Loaded.Any(id => id.StartsWith("prop.stump", StringComparison.Ordinal));
        }, "o jogo não ficou pronto (chunks, toco e fila de modelos)", 300_000);
    }

    private static async Task OpenBagAsync(IPage page)
    {
        var open = Touch ? page.Locator("#ultima-menu-fab[aria-expanded=false]") : Button(page, "Abrir painel do jogo");
        if (await open.IsVisibleAsync()) await open.ClickAsync();
        var heading = Button(page, "Mochila");
        if (await heading.GetAttributeAsync("aria-expanded") != "true") await heading.ClickAsync();
    }

    private static async Task StartCollectingAsync(IPage page)
    {
        await Button(page, "Coletar").ClickAsync();
        if (Touch) await page.Locator("#ultima-menu-fab[aria-expanded=true]").ClickAsync();
    }

    private static async Task StopCollectingAsync(IPage page)
    {
        if (Touch) await OpenBagAsync(page);
        await Button(page, "Parar coleta").ClickAsync();
    }

    private static ILocator Button(IPage page, string name) =>
        page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });

    private static async Task CheckItemsAsync(IPage page, CollectRecord[] saved)
    {
        foreach (var item in saved.Select(r => r.Item).Distinct())
            await page.GetByText(item, new() { Exact = true }).WaitForAsync();
    }

    private static async Task CheckObjectPreviewAsync(IPage page)
    {
        await PointAtAsync(page, new(Name: Mesh), hover: true);
        await UntilAsync(async () => (await PreviewAsync(page)).Objects.Contains(Mesh), "destaca o toco sob o mouse");
        var preview = await PreviewAsync(page);
        Check(preview.Objects.SequenceEqual([Mesh]) && preview.Ground is null, "destaca apenas o toco", preview);
        await PointAtAsync(page, new(X: 1, Z: 9), hover: true);
        await UntilAsync(async () => (await PreviewAsync(page)).Objects.Length == 0, "remove destaque do toco distante");
        Check((await PreviewAsync(page)).Ground is null, "solo distante fica sem destaque");
        await PointAtAsync(page, new(Name: Mesh), hover: true);
        await UntilAsync(async () => (await PreviewAsync(page)).Objects.Contains(Mesh), "restaura destaque ao voltar ao toco");
        await Button(page, "Parar coleta").HoverAsync();
        await UntilAsync(async () => (await PreviewAsync(page)).Objects.Length == 0, "esconde destaque sobre o painel");
    }

    private static async Task CheckGroundPreviewAsync(IPage page, Target soil, double height)
    {
        await PointAtAsync(page, soil, hover: true);
        await UntilAsync(async () => (await PreviewAsync(page)).Ground is Position center &&
            double.Hypot(center.X - soil.X, center.Z - soil.Z) < .1 && Math.Abs(center.Y - height) < .1,
            "a prévia deve coincidir com o ponto de coleta");
    }

    private static async Task PointAtAsync(IPage page, Target point, bool hover = false)
    {
        var screen = await ProjectAsync(page, point);
        await UntilAsync(() => page.EvaluateAsync<bool>("({x,y}) => document.elementFromPoint(x,y)?.id === 'game-canvas'",
            new { x = screen.X, y = screen.Y }), "o ponto projetado deve estar acessível no canvas");
        var (x, y) = ((float)screen.X, (float)screen.Y);
        if (hover) await page.Mouse.MoveAsync(x, y);
        else if (Touch) await page.Touchscreen.TapAsync(x, y);
        else await page.Mouse.ClickAsync(x, y);
    }

    private static Task UntilAsync(Func<Task<bool>> condition, string description, int timeoutMs = 180_000) =>
        WaitAsync(condition, description, timeoutMs);

    private sealed record Target(string? Name = null, double X = 0, double Z = 0);
    private sealed record ScreenPoint(double X, double Y, double Alt);
    private sealed record Preview(string[] Objects, Position? Ground);
    private sealed record CollectRecord(string Item, string Kind, double? X, double? Z);
}
