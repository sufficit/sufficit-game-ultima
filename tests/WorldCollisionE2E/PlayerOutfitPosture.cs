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
            const joint = name => rig.bones.find(bone => bone.name === name).getTransformNode();
            const head = joint('head').computeWorldMatrix(true).getTranslation();
            const pelvis = joint('pelvis').computeWorldMatrix(true).getTranslation();
            return { x: player.position.x, y: player.position.y, z: player.position.z, pitch: skin.rotation.x,
                headY: head.y, pelvisY: pelvis.y,
                pelvisOffset: Math.hypot(pelvis.x - player.position.x, pelvis.z - player.position.z) };
        }
        """;

    private const string RestingSurfaceGaps = """
        () => {
            const scene = BABYLON.EngineStore.LastCreatedScene;
            const actor = scene.getTransformNodeByName('player');
            const rig = scene.skeletons.find(skeleton => skeleton.bones.length === 53);
            const inverse = actor.computeWorldMatrix(true).clone().invert();
            const point = new BABYLON.Vector3();
            const gaps = Object.fromEntries(['head', 'back', 'hips', 'leftHand', 'rightHand', 'leftHeel', 'rightHeel']
                .map(name => [name, Infinity]));
            const consider = (name, y) => gaps[name] = Math.min(gaps[name], y + .03);
            rig.prepare(true);
            for (const mesh of scene.meshes.filter(mesh => mesh.skeleton === rig)) {
                const positions = mesh.getPositionData(true);
                const toActor = mesh.computeWorldMatrix(true).multiply(inverse);
                for (const i of new Set(mesh.getIndices())) {
                    BABYLON.Vector3.TransformCoordinatesFromFloatsToRef(
                        positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2], toActor, point);
                    const { x, y, z } = point;
                    if (z < -.57) consider('head', y);
                    if (Math.abs(x) < .17 && z > -.48 && z < -.28) consider('back', y);
                    if (Math.abs(x) < .17 && Math.abs(z) < .14) consider('hips', y);
                    if (z > -.09 && z < .19 && x < -.25) consider('leftHand', y);
                    if (z > -.09 && z < .19 && x > .25) consider('rightHand', y);
                    if (z > .77) consider(x < 0 ? 'leftHeel' : 'rightHeel', y);
                }
            }
            return gaps;
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
        await WaitAsync(async () => (await SampleAsync(page)).Pitch < -1.55, "The player did not roll onto their back.");
        var lying = await SampleAsync(page);
        await MoveAsync(page);
        Check(Distance(lying, await SampleAsync(page)) < .02, "The player lies in place.");
        Check(lying.PelvisOffset < .15 && lying.HeadY < idle.HeadY - 1 &&
            Math.Abs(lying.HeadY - lying.PelvisY) < .25,
            "The supine pose stays centered and low.", new { idle, lying });

        var gaps = await RenderAsync<Dictionary<string, double?>>(page, RestingSurfaceGaps);
        Check(gaps.Count == 7 && gaps.Values.All(gap => gap is > -.025 and < .045),
            "The skull, back, pelvis, hands and heels stay close to the floor.", gaps);

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
        Log(new { build, mode = Mode, outfit, jumpHeight = apex - idle.Y, crawlDistance = Distance(crawl, crawled), lying, gaps });
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

    private sealed record Pose(double X, double Y, double Z, double Pitch, double HeadY, double PelvisY, double PelvisOffset);
}
