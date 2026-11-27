using System.Text.Json.Nodes;
using Microsoft.Playwright;
using static BrowserTest;

internal static class SkinFootAlignment
{
    private const string SamplePose = """
        () => {
            const scene = BABYLON.EngineStore.LastCreatedScene;
            const player = scene.getTransformNodeByName('player');
            const skin = scene.skeletons.find(skeleton => skeleton.bones.length === 53);
            const joint = name => skin.bones.find(bone => bone.name === name).getTransformNode();
            const position = node => node.computeWorldMatrix(true).getTranslation();
            const feet = ['left', 'right'].map((side, index) => {
                const suffix = index === 0 ? '_l' : '_r';
                const hip = position(joint('thigh' + suffix));
                const knee = position(joint('calf' + suffix));
                const ankle = position(joint('foot' + suffix));
                const target = position(scene.getTransformNodeByName('player-ankle-' + side));
                return {
                    error: Math.hypot(ankle.x - target.x, ankle.z - target.z),
                    heightDelta: ankle.y - target.y,
                    femur: BABYLON.Vector3.Distance(hip, knee),
                    tibia: BABYLON.Vector3.Distance(knee, ankle)
                };
            });
            const scale = scene.getTransformNodeByName('innerscene-player-preview').scaling.y;
            return { x: player.position.x, z: player.position.z, yaw: player.rotation.y, feet, scale };
        }
        """;

    internal static async Task RunAsync(IBrowser browser)
    {
        foreach (var custom in new[] { false, true })
        {
            var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 800, Height = 600 } });
            try
            {
                await RunProfileAsync(page, custom);
            }
            finally
            {
                await page.CloseAsync();
            }
        }
    }

    private static async Task RunProfileAsync(IPage page, bool custom)
    {
        if (custom)
        {
            await page.RouteAsync("**/environments.json", async route =>
            {
                var response = await route.FetchAsync();
                var catalog = JsonNode.Parse(await response.TextAsync())!.AsObject();
                catalog["playerBody"] = new JsonObject { ["height"] = 1.9, ["legRatio"] = .48, ["femurRatio"] = .53 };
                await route.FulfillAsync(new() { Response = response, Body = catalog.ToJsonString() });
            });
        }

        var errors = TrackErrors(page);
        await page.GotoAsync(GameUrl(forceWebGl: true) + "&avatar=innerscene");
        var build = await CheckBuildAsync(page);
        await WaitAsync(async () => await ReadAsync<string?>(page,
            "async () => (await globalThis.game?.models?.status())?.skinPreview") == "ready", "The Innerscene skin did not load.");
        Check(await page.EvaluateAsync<string>("() => game.mode") == Mode, "Expected renderer mode", Mode);
        await page.WaitForTimeoutAsync(250);
        var rest = await RenderAsync<Pose>(page, SamplePose);
        var walk = await TravelAsync(page, "w");
        await page.WaitForTimeoutAsync(700);
        var stop = await RenderAsync<Pose>(page, SamplePose);
        var reverse = await TravelAsync(page, "s");
        Check(Distance(rest, walk[^1]) > .2 && Math.Abs(reverse[^1].Yaw - rest.Yaw) > 2.8,
            "The player walked and reversed direction.");
        Check(Distance(stop, walk[^1]) < .35, "The player stopped after releasing movement.");

        Pose[] poses = [rest, .. walk, stop, .. reverse];
        var peak = poses.Max(pose => pose.Feet.Max(foot => foot.Error));
        var heightError = poses.Max(pose => pose.Feet.Select((foot, i) => Math.Abs(foot.HeightDelta - rest.Feet[i].HeightDelta)).Max());
        var lengthError = poses.Max(pose => pose.Feet.Select((foot, i) =>
            Math.Max(Math.Abs(foot.Femur - rest.Feet[i].Femur), Math.Abs(foot.Tibia - rest.Feet[i].Tibia))).Max());
        var evidence = new { build, mode = Mode, custom, rest.Scale, peak, heightError, lengthError };
        Check(!custom || Math.Abs(rest.Scale * 1.6803 - 1.9) < .001, "The custom profile scaled the skin.", evidence);
        Check(peak < .005 && heightError < .005 && lengthError < .001,
            "The skin tracks both ankle targets and preserves leg lengths.", evidence);
        Check(errors.IsEmpty, "Browser errors during skin IK", errors);
        Log(evidence);
    }

    private static async Task<Pose[]> TravelAsync(IPage page, string key)
    {
        var frames = new Pose[12];
        await page.Keyboard.DownAsync(key);
        try
        {
            for (var frame = 0; frame < frames.Length; frame++)
            {
                await page.WaitForTimeoutAsync(65);
                frames[frame] = await RenderAsync<Pose>(page, SamplePose);
            }
        }
        finally
        {
            await page.Keyboard.UpAsync(key);
        }
        return frames;
    }

    private static double Distance(Pose a, Pose b) => double.Hypot(a.X - b.X, a.Z - b.Z);

    private sealed record Pose(double X, double Z, double Yaw, Foot[] Feet, double Scale);
    private sealed record Foot(double Error, double HeightDelta, double Femur, double Tibia);
}
