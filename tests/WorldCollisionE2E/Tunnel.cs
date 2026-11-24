using System.Text.Json;
using Microsoft.Playwright;
using static BrowserTest;

internal static class Tunnel
{
    internal static async Task RunAsync(IBrowser browser)
    {
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1200, Height = 720 } });
        var errors = TrackErrors(page);
        await page.GotoAsync(GameUrl());
        var build = await CheckBuildAsync(page);
        await WaitAsync(async () => (await ReadAsync<MapState?>(page, "() => window.game?.build?.state?.()"))?.Chunks >= 25,
            "o terreno não carregou", 20_000, 200);
        Check(await page.EvaluateAsync<string>("() => game.mode") == Mode, "modo do renderizador", Mode);

        var tunnel = new
        {
            kind = "tunnel", x = 5, z = 11, r = 12, radius = 2.4, rev = 1,
            points = new[] { new { x = 0, y = .8, z = 4 }, new { x = 4, y = -3.2, z = 10 }, new { x = 10, y = 1.1, z = 18 } }
        };
        await page.EvaluateAsync("op => game.setWorldEdits([op], 1)", tunnel);
        ChunkVolume? chunk = null;
        await WaitAsync(async () => (chunk = await ChunkAsync(page))?.Volume == true,
            "a malha volumétrica não chegou ao Babylon", 20_000, 200);
        Check(chunk!.TotalVertices > 4225, "refinamento da malha volumétrica", chunk);

        Task<TunnelPose> PoseAsync() => RenderAsync<TunnelPose>(page, """
            () => {
                const scene = BABYLON.EngineStore.LastCreatedScene;
                const { x, y, z } = scene.getTransformNodeByName('player').position;
                return { x, y, z, radius: scene.activeCamera.radius };
            }
            """);
        Task SetPoseAsync(double y) => RenderAsync<JsonElement>(page, """
            y => BABYLON.EngineStore.LastCreatedScene.getTransformNodeByName('player').position.set(4, y, 10)
            """, y);
        async Task<TunnelPose> WalkAsync(string[] keys, Func<TunnelPose, bool> reached)
        {
            await page.Locator("#game-canvas").FocusAsync();
            foreach (var key in keys) await page.Keyboard.DownAsync(key);
            try
            {
                TunnelPose? pose = null;
                await WaitAsync(async () => reached(pose = await PoseAsync()),
                    "o jogador não avançou durante a caminhada", 3000, 200);
                return pose!;
            }
            finally
            {
                foreach (var key in keys) await page.Keyboard.UpAsync(key);
            }
        }

        var above = await page.EvaluateAsync<double>("() => game.heightAt(4, 10)");
        await SetPoseAsync(above + .1);
        await WaitAsync(async () => Math.Abs((await PoseAsync()).Y - above) < .13, "o teto perdeu o piso externo", 20_000, 200);
        var onTop = await WalkAsync(["w"], pose => pose.Z > 10.5);
        Check(onTop.Y > -.2, "caminhada pelo piso externo", onTop);

        await page.EvaluateAsync("() => game.resetPlayer()");
        await SetPoseAsync(-4.7);
        TunnelPose? inside = null;
        await WaitAsync(async () => (inside = await PoseAsync()) is { Y: < -4.4, Radius: < 6 },
            "o jogador ou a câmera atravessou o teto", 20_000, 200);
        var start = inside!;
        var walked = await WalkAsync(["w", "d"], pose => double.Hypot(pose.X - start.X, pose.Z - start.Z) > .5);
        Check(walked.Y < above - 1, "caminhada pelo interior do túnel", walked);
        await page.EvaluateAsync("() => game.setWorldEdits([], 2)");
        await WaitAsync(async () => (await ChunkAsync(page))?.Volume == false, "o replay não restaurou a malha da superfície", 20_000, 200);
        Check(errors.IsEmpty, "erros no navegador", errors);
        Log(new { build, mode = Mode, above, inside, walked, vertices = chunk.TotalVertices });
    }

    internal static Task<ChunkVolume?> ChunkAsync(IPage page) =>
        ReadAsync<ChunkVolume?>(page, "() => game.debugChunk(0, 0)");

    internal sealed record ChunkVolume(bool Volume, int TotalVertices);
    private sealed record TunnelPose(double X, double Y, double Z, double Radius);
}
