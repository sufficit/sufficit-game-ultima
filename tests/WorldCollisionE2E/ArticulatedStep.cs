using Microsoft.Playwright;
using static BrowserTest;

internal static class ArticulatedStep
{
    private const string Entry = "    function syncCharacterHitboxes() {";
    private const string Accept = "                terrain.OnFootContact(Math.abs(forward - shortened));";
    private const string Fixture = """
        const fixture = globalThis.__stepFixture;
        if (fixture && !fixture.prepared) {
            fixture.prepared = true;
            const rig = characterRigs.get(player);
            const leg = rig.legs[1];
            const obstacle = remotes.get('step-contact-test').rig.legs[0].shoe;
            const { x, y, z } = player.position;
            fixture.start = { x, z };
            leg.stride.set([0.38, 0, 0.065, 0, 0]);
            const pose = terrain.SolveShortenedFoot(y, terrainHeight(x,z), 0.38, 0, 0.065, 0);
            rigRenderer.applyLeg(leg, pose, 0, pose[3], 0);
            leg.shoe.computeWorldMatrix(true);
            const parentInverse = obstacle.parent.computeWorldMatrix(true).clone().invert();
            BABYLON.Vector3.TransformCoordinatesToRef(leg.shoe.getAbsolutePosition(), parentInverse, obstacle.position);
        }
        """;
    private const string Capture = """
        const observed = globalThis.__stepFixture;
        if (observed && !observed.result) observed.result = { forward, shortened };
        """;

    internal static async Task RunAsync(IBrowser browser)
    {
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 960, Height = 540 } });
        await page.RouteAsync("**/js/game-core.js*", async route =>
        {
            var response = await route.FetchAsync();
            var source = await response.TextAsync();
            Check(source.Contains(Entry) && source.Contains(Accept), "pontos de observação da passada disponíveis");
            var body = source.Replace(Entry, Entry + "\n" + Fixture).Replace(Accept, Accept + "\n" + Capture);
            await route.FulfillAsync(new() { Response = response, Body = body });
        });
        await page.GotoAsync(GameUrl(forceWebGl: true));
        var build = await CheckBuildAsync(page);
        await WaitAsync(async () =>
        {
            var models = await ReadAsync<ModelsState?>(page, "() => window.game?.models?.status?.()");
            return models is { Queue: 0 } && models.Loaded.Contains("character.villager v2") &&
                models.Loaded.Any(name => name.StartsWith("character.wanderer", StringComparison.Ordinal));
        }, "modelos humanoides prontos", timeoutMs: 24_000, intervalMs: 200);
        Check(await page.EvaluateAsync<string>("() => game.mode") == Mode, "modo do renderizador", Mode);
        await page.EvaluateAsync("""
            () => {
                game.addRemote('step-contact-test', 'Contato do pé');
                game.updateRemote('step-contact-test', 0.95, 0, 0);
            }
            """);
        await WaitAsync(async () => await RenderAsync<double?>(page, """
            () => BABYLON.EngineStore.LastCreatedScene.getTransformNodeByName('remote-step-contact-test')?.position.x
            """) == .95, "avatar lateral pronto", timeoutMs: 24_000, intervalMs: 200);
        var frame = await RenderAsync<StepFrame>(page, """
            () => new Promise(resolve => {
                const scene = BABYLON.EngineStore.LastCreatedScene;
                const player = scene.getTransformNodeByName('player');
                globalThis.__stepFixture = {};
                scene.onAfterRenderObservable.addOnce(() => {
                    const { start, result } = globalThis.__stepFixture;
                    delete globalThis.__stepFixture;
                    resolve({ start, result, end: { x: player.position.x, z: player.position.z } });
                });
            })
            """).WaitAsync(TimeSpan.FromSeconds(30));
        Check(frame.Result is not null, "o pé não adotou um passo curto", frame);
        var result = frame.Result!;
        var drift = double.Hypot(frame.End.X - frame.Start.X, frame.End.Z - frame.Start.Z);
        var evidence = new { build, mode = Mode, result.Forward, result.Shortened, drift };
        Check(result.Forward - result.Shortened > .12 && drift < .005,
            "encurta a passada sem deslocar o corpo", evidence);
        Log(evidence);
    }

    private sealed record StepFrame(Point Start, StepChange? Result, Point End);
    private sealed record Point(double X, double Z);
    private sealed record StepChange(double Forward, double Shortened);
}
