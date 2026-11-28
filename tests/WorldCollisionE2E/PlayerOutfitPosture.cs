using Microsoft.Playwright;
using static BrowserTest;

internal static class PlayerOutfitPosture
{
    private const string Sample = """
        () => {
            const scene = BABYLON.EngineStore.LastCreatedScene;
            const player = scene.getTransformNodeByName('player');
            const skin = scene.getTransformNodeByName('innerscene-player-preview');
            const rig = scene.skeletons.find(skeleton => skeleton.bones.length === 53);
            const head = rig.bones.find(bone => bone.name === 'head').getTransformNode();
            return { x: player.position.x, y: player.position.y, z: player.position.z, pitch: skin.rotation.x,
                headY: head.computeWorldMatrix(true).getTranslation().y };
        }
        """;

    internal static async Task RunAsync(IBrowser browser)
    {
        await using var context = await browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 800, Height = 600 }
        });
        await context.AddInitScriptAsync("""
            if (location.origin !== 'null' && !localStorage.getItem('ultima-player-uuid')) {
                localStorage.setItem('ultima-player-uuid', '00000000-d764-49ea-839c-5416eac532ec');
            }
            """);
        var page = await context.NewPageAsync();
        var errors = TrackErrors(page);
        await page.GotoAsync(GameUrl(forceWebGl: true));
        var build = await CheckBuildAsync(page);
        var outfit = await WaitForOutfitAsync(page);
        Check(outfit.Length == 4 && outfit.Take(3).All(color => color is > 0 and <= 0xffffff), "The default avatar has three outfit pieces.", outfit);
        var materialNames = await RenderAsync<string[]>(page, """
            () => BABYLON.EngineStore.LastCreatedScene.meshes.filter(mesh=>mesh.skeleton?.bones.length===53)
                .map(mesh=>mesh.material?.name)
            """);
        Check(new[] { "outfit-shirt", "outfit-trousers", "outfit-boots" }.All(materialNames.Contains),
            "The outfit pieces are skinned.", materialNames);
        Check(outfit[3] == 1 && await RenderAsync<bool>(page, """
            () => !!BABYLON.EngineStore.LastCreatedScene.getMeshByName('outfit-scarf-tail')
            """), "The seeded scarf was attached.");
        var idle = await SampleAsync(page);

        await page.Keyboard.PressAsync("Space");
        double apex = idle.Y;
        for (var i = 0; i < 14; i++)
        {
            await page.WaitForTimeoutAsync(52);
            apex = Math.Max(apex, (await SampleAsync(page)).Y);
        }
        await WaitAsync(async () => Math.Abs((await SampleAsync(page)).Y - idle.Y) < .04, "The jump did not land.");
        Check(apex - idle.Y > .34 && apex - idle.Y < .8, "Space starts a bounded jump.", new { idle, apex });

        await page.Keyboard.PressAsync("c");
        await WaitAsync(async () => (await SampleAsync(page)).HeadY < idle.HeadY - .16, "The player did not crouch.");
        await page.Keyboard.PressAsync("x");
        await WaitAsync(async () => (await SampleAsync(page)).Pitch > 1.45, "The player did not lower into a crawl.");
        var crawl = await SampleAsync(page);
        await MoveAsync(page);
        var crawled = await SampleAsync(page);
        Check(Distance(crawl, crawled) > .12, "The player can move while crawling.", new { crawl, crawled });

        await page.Keyboard.PressAsync("z");
        await page.WaitForTimeoutAsync(350);
        var lying = await SampleAsync(page);
        await MoveAsync(page);
        Check(lying.Pitch > 1.45 && Distance(lying, await SampleAsync(page)) < .02, "The player lies in place.");

        await page.Keyboard.PressAsync("f");
        await WaitAsync(async () => (await SampleAsync(page)) is { Pitch: < .1 } sitting && sitting.HeadY < idle.HeadY - .40,
            "The player did not sit.");
        var seated = await SampleAsync(page);
        await MoveAsync(page);
        Check(Distance(seated, await SampleAsync(page)) < .02, "The seated player stays in place.");
        await page.Keyboard.PressAsync("f");
        await WaitAsync(async () => (await SampleAsync(page)).HeadY > idle.HeadY - .08, "The player did not stand up.");

        await page.ReloadAsync();
        var restoredOutfit = await WaitForOutfitAsync(page);
        Check(outfit.SequenceEqual(restoredOutfit), "The same identity keeps its outfit after reload.");
        Check(errors.IsEmpty, "Browser errors during outfit and posture checks.", errors);
        Log(new { build, mode = Mode, outfit, jumpHeight = apex - idle.Y, crawlDistance = Distance(crawl, crawled) });
    }

    private static async Task<int[]> WaitForOutfitAsync(IPage page)
    {
        await WaitAsync(async () => await ReadAsync<string?>(page,
            "async () => (await globalThis.game?.models?.status())?.skinPreview") == "ready", "The default humanoid did not load.");
        return await ReadAsync<int[]>(page, "async () => (await game.models.status()).outfit");
    }

    private static Task<Pose> SampleAsync(IPage page) => RenderAsync<Pose>(page, Sample);
    private static double Distance(Pose left, Pose right) => double.Hypot(left.X - right.X, left.Z - right.Z);

    private static async Task MoveAsync(IPage page)
    {
        await page.Keyboard.DownAsync("w");
        await page.WaitForTimeoutAsync(550);
        await page.Keyboard.UpAsync("w");
        await page.WaitForTimeoutAsync(300);
    }

    private sealed record Pose(double X, double Y, double Z, double Pitch, double HeadY);
}
