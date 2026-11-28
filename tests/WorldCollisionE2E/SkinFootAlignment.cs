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
            const inverse = player.computeWorldMatrix(true).clone().invert();
            const hands = ['hand_l', 'hand_r'].map(name =>
                BABYLON.Vector3.TransformCoordinates(position(joint(name)), inverse).z);
            const joints = ['pelvis', 'spine_03', 'clavicle_l', 'clavicle_r', 'lowerarm_l', 'lowerarm_r']
                .map(name => joint(name).rotationQuaternion.asArray());
            const headY = position(joint('head')).y;
            const arms = ['l', 'r'].map(side => {
                const elbow = position(joint('lowerarm_' + side));
                const hand = joint('hand_' + side);
                const upper = elbow.subtract(position(joint('upperarm_' + side))).normalize();
                const forearm = position(hand).subtract(elbow).normalize();
                const bend = Math.acos(Math.max(-1, Math.min(1, BABYLON.Vector3.Dot(upper, forearm))));
                const span = segment => BABYLON.Vector3.Distance(position(joint('index_' + segment + '_' + side)),
                    position(joint('pinky_' + segment + '_' + side)));
                return { bend, wrist: hand.rotationQuaternion.asArray(),
                    finger: joint('index_02_' + side).rotationQuaternion.asArray(), spread: span('03') / span('01') };
            });
            return { x: player.position.x, z: player.position.z, yaw: player.rotation.y, feet, scale, hands, joints, headY, arms };
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
        await WaitAsync(async () => await ReadAsync<bool>(page, """
            async () => (await game.models.status()).loaded.some(name => name.startsWith('character.villager v'))
            """), "The procedural player model did not load.");
        Check(await page.EvaluateAsync<string>("() => game.mode") == Mode, "Expected renderer mode", Mode);
        Check(await RenderAsync<bool>(page, """
            () => {
                const scene = BABYLON.EngineStore.LastCreatedScene;
                return ['player-brow--1', 'player-brow-1', 'player-mouth'].every(name =>
                    scene.getMeshByName(name)?.isEnabled() === false);
            }
            """), "The imported head hides the procedural eyebrows and mouth.");
        await page.WaitForTimeoutAsync(250);
        var rest = await RenderAsync<Pose>(page, SamplePose);
        var walk = await TravelAsync(page, "w");
        await page.WaitForTimeoutAsync(700);
        var stop = await RenderAsync<Pose>(page, SamplePose);
        await page.WaitForTimeoutAsync(250);
        var settled = await RenderAsync<Pose>(page, SamplePose);
        var reverse = await TravelAsync(page, "s");
        Check(Distance(rest, walk[^1]) > .2 && Math.Abs(reverse[^1].Yaw - rest.Yaw) > 2.8,
            "The player walked and reversed direction.");
        Check(Distance(stop, settled) < .005, "The player stopped after releasing movement.", new { stop, settled });

        Pose[] poses = [rest, .. walk, stop, settled, .. reverse];
        var peak = poses.Max(pose => pose.Feet.Max(foot => foot.Error));
        var heightError = poses.Max(pose => pose.Feet.Select((foot, i) => Math.Abs(foot.HeightDelta - rest.Feet[i].HeightDelta)).Max());
        var lengthError = poses.Max(pose => pose.Feet.Select((foot, i) =>
            Math.Max(Math.Abs(foot.Femur - rest.Feet[i].Femur), Math.Abs(foot.Tibia - rest.Feet[i].Tibia))).Max());
        var handTravel = Enumerable.Range(0, 2).Select(i => walk.Max(p => p.Hands[i]) - walk.Min(p => p.Hands[i])).ToArray();
        var jointMotion = rest.Joints.Select((joint, i) => walk.Max(pose => AngleBetween(joint, pose.Joints[i]))).ToArray();
        var handSeparation = walk.Select(pose => pose.Hands[0] - pose.Hands[1]).ToArray();
        var levelTrail = walk.Where(pose => pose.Z is > .55 and < 1.80).ToArray();
        var headRange = levelTrail.Length > 0 ? levelTrail.Max(pose => pose.HeadY) - levelTrail.Min(pose => pose.HeadY) : 0;
        var elbowRest = rest.Arms.Max(arm => arm.Bend);
        var elbowWalk = walk.Max(pose => pose.Arms.Max(arm => arm.Bend));
        var wristMotion = rest.Arms.Select((arm, i) => walk.Max(pose => AngleBetween(arm.Wrist, pose.Arms[i].Wrist))).ToArray();
        var fingerMotion = rest.Arms.Select((arm, i) => walk.Max(pose => AngleBetween(arm.Finger, pose.Arms[i].Finger))).ToArray();
        var fingerSpread = poses.Max(pose => pose.Arms.Max(arm => arm.Spread));
        var evidence = new { build, mode = Mode, custom, rest.Scale, peak, heightError, lengthError, handTravel,
            jointMotion, headRange, elbowRest, elbowWalk, wristMotion, fingerMotion, fingerSpread };
        Check(levelTrail.Length >= 3 && headRange < .025, "The head stays steady across the level trail.", evidence);
        Check(handTravel.All(distance => distance > .10) && handSeparation.Min() < -.06 && handSeparation.Max() > .06,
            "Both hands swing in opposite phases of the stride.", evidence);
        Check(jointMotion.All(angle => angle > .025), "Pelvis, thorax, clavicles and elbows participate in the gait.", evidence);
        Check(elbowRest < .35 && elbowWalk < .63 && wristMotion.All(angle => angle > .10) &&
            fingerMotion.All(angle => angle is > .004 and < .04), "Relaxed arms show wrist lag and subtle finger motion.", evidence);
        Check(fingerSpread < 1.15, "The relaxed fingers stay close together at rest and in motion.", evidence);
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

    private static double AngleBetween(double[] a, double[] b)
        => 2 * Math.Acos(Math.Clamp(Math.Abs(a.Zip(b, (x, y) => x * y).Sum()), 0, 1));

    private sealed record Pose(double X, double Z, double Yaw, Foot[] Feet, double Scale, double[] Hands, double[][] Joints,
        double HeadY, Arm[] Arms);
    private sealed record Foot(double Error, double HeightDelta, double Femur, double Tibia);
    private sealed record Arm(double Bend, double[] Wrist, double[] Finger, double Spread);
}
