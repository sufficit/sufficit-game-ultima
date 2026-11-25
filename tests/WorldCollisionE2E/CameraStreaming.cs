using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static BrowserTest;

internal static class CameraStreaming
{
    private static readonly Regex GroundName = new(@"^ground-[-\d]+,[-\d]+$", RegexOptions.CultureInvariant);

    internal static async Task RunAsync(IBrowser browser)
    {
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1200, Height = 720 } });
        var errors = TrackErrors(page);
        await page.GotoAsync(GameUrl());
        var build = await CheckBuildAsync(page);
        var timeout = LoadTimeoutMs(100);
        await WaitAsync(async () =>
        {
            var assets = await ReadAsync<SceneAssets>(page, """
                async () => ({
                    glb: await window.game?.glb?.status?.(),
                    models: await window.game?.models?.status?.()
                })
                """);
            return assets.Models is { Queue: 0 } &&
                assets.Glb?.Loaded.Any(asset => asset.StartsWith("flora.pine", StringComparison.Ordinal)) == true;
        }, "o cenário não carregou", timeout);
        Check(await page.EvaluateAsync<string>("() => game.mode") == Mode, "modo do renderizador", Mode);

        Task<Orbit> CameraAsync() => RenderAsync<Orbit>(page, """
            () => {
                const camera = BABYLON.EngineStore.LastCreatedScene.activeCamera;
                return { radius: camera.radius, alpha: camera.alpha };
            }
            """);
        var walk = await ObserveWalkAsync(page);
        await page.EvaluateAsync("() => game.resetPlayer()");
        await WaitAsync(() => RenderAsync<bool>(page, """
            () => BABYLON.EngineStore.LastCreatedScene.getTransformNodeByName('player').position.lengthSquared() < 1
            """), "jogador voltou à clareira", timeout);
        Orbit clear = null!, blocked = null!;
        await WaitAsync(async () => (clear = await CameraAsync()).Radius > 6, "a órbita inicial está bloqueada", timeout);
        var trees = new[] { -.6, 0, .6 }.Select((x, i) => new
        {
            kind = "place", uid = "camera-pinheiro-" + i, asset = "flora.pine", x, z = -2, rot = 0, scale = 2
        }).ToArray();
        foreach (var tree in trees) await page.EvaluateAsync("op => game.applyWorldOp(op)", tree);
        await WaitAsync(async () => (blocked = await CameraAsync()).Radius < 2.4, "a câmera atravessou o pinheiro", timeout);
        Check(blocked.Radius > .12, "o raio mínimo mantém a câmera fora do personagem", blocked);
        foreach (var tree in trees)
            await page.EvaluateAsync("uid => game.applyWorldOp({ kind: 'remove', uid })", tree.uid);
        await WaitAsync(async () => (await CameraAsync()).Radius >= clear.Radius - .05, "o zoom não voltou ao remover as árvores", timeout);

        var canvas = await page.Locator("#game-canvas").BoundingBoxAsync()
            ?? throw new InvalidOperationException("canvas do jogo visível");
        var (x, y) = (canvas.X + canvas.Width / 2, canvas.Y + canvas.Height / 2);
        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(x + 120, y, new() { Steps = 4 });
        await page.Mouse.UpAsync();
        await WaitAsync(async () => Math.Abs((await CameraAsync()).Alpha - clear.Alpha) > .2, "o arrasto não girou a câmera", timeout);
        await page.Mouse.WheelAsync(0, -360);
        await WaitAsync(async () => (await CameraAsync()).Radius < clear.Radius - .5, "a roda não aproximou a câmera", timeout);

        await RenderAsync<JsonElement>(page, """
            x => { BABYLON.EngineStore.LastCreatedScene.getTransformNodeByName('player').position.x = x; }
            """, 144);
        string[] names = [];
        ChunkWindow window = null!;
        await WaitAsync(async () =>
        {
            window = await RenderAsync<ChunkWindow>(page, """
                () => {
                    const scene = BABYLON.EngineStore.LastCreatedScene;
                    return { names: scene.meshes.map(mesh => mesh.name).filter(name => name.startsWith('ground-')),
                        fog: scene.fogDensity };
                }
                """);
            names = window.Names.Where(name => GroundName.IsMatch(name)).ToArray();
            return names.Length >= 25 && names.Contains("ground-5,0") && !names.Contains("ground-0,0");
        }, "a janela não carregou os vizinhos ou manteve o chunk de origem", timeout);
        var chunks = new { count = names.Length, unique = names.Distinct().Count(), window.Fog };
        Check(chunks.count == chunks.unique && chunks.Fog > 0, "chunks sem duplicatas e com neblina", chunks);
        Check(errors.IsEmpty, "erros no navegador", errors);
        Log(new { build, mode = Mode, walk, clear, blocked, chunks });
    }

    private static async Task<object> ObserveWalkAsync(IPage page)
    {
        await page.Locator("#game-canvas").FocusAsync();
        await RenderAsync<object?>(page, """
            () => {
                const scene = BABYLON.EngineStore.LastCreatedScene;
                const root = scene.getTransformNodeByName('player');
                const frames = [];
                globalThis.ultimaCameraWalk = frames;
                const subscription = scene.onAfterRenderObservable.add(() => {
                    const target = scene.activeCamera.target;
                    frames.push({ z: root.position.z, body: root.position.y, camera: target.y });
                    if (frames.length >= 240) scene.onAfterRenderObservable.remove(subscription);
                });
            }
            """);
        await page.Keyboard.DownAsync("w");
        try { await page.WaitForTimeoutAsync(1700); }
        finally { await page.Keyboard.UpAsync("w"); }

        var frames = await RenderAsync<WalkFrame[]>(page, "() => globalThis.ultimaCameraWalk");
        var moving = frames.Where(frame => frame.Z > frames[0].Z + .2).ToArray();
        Check(moving.Length >= 6 && moving[^1].Z - moving[0].Z > .7, "amostra de caminhada insuficiente", new { frames = frames.Length });
        static double Variation(WalkFrame[] samples, Func<WalkFrame, double> height) =>
            samples.Zip(samples.Skip(1), (a, b) => Math.Abs(height(b) - height(a))).Sum();
        var body = Variation(moving, frame => frame.Body);
        var camera = Variation(moving, frame => frame.Camera);
        Check(camera < body * .8, "a câmera deve oscilar menos que o corpo durante a caminhada", new { body, camera });
        return new { frames = moving.Length, body, camera };
    }

    private sealed record WalkFrame(double Z, double Body, double Camera);
    private sealed record SceneAssets(AssetsState? Glb, ModelsState? Models);
    private sealed record Orbit(double Radius, double Alpha);
    private sealed record ChunkWindow(string[] Names, double Fog);
}
