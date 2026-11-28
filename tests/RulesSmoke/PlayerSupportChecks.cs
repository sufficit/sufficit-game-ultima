using Ultima.Terrain;

internal static class PlayerSupportChecks
{
    internal static void Run(Action<string, bool> check)
    {
        var contacts = new PlayerFootContacts();
        Span<double> gait = stackalloc double[12];
        Span<double> feet = stackalloc double[10];
        Span<double> alignment = stackalloc double[4];

        foreach (var (dx, dz) in new[] { (0.045, 0d), (-0.045, 0d), (0d, -0.045) })
        {
            contacts.Reset();
            var aimedPhase = 0d;
            var maxOffset = 0d;
            var x = 0d;
            var z = 0d;
            var distance = double.Hypot(dx, dz);
            contacts.Begin(x, z, 0, 0, aimed: true);
            contacts.Apply(feet, alignment, 0, .091, .06);
            for (var frame = 0; frame < 120; frame++)
            {
                x += dx;
                z += dz;
                contacts.Begin(x, z, 0, distance, aimed: true);
                PlayerPose.Animate(gait, aimedPhase, 1, 0, distance, .02, 0, false, false, 0, 0, 0,
                    contacts.AimedStride, playerGait: true);
                aimedPhase = gait[0];
                PlayerPose.FootTargets(feet, 0, aimedPhase, 1, distance / .02, .091, .06, contacts.AimedStride);
                contacts.Apply(feet, alignment, aimedPhase, .091, .06, .02);
                maxOffset = Math.Max(maxOffset, Math.Max(Math.Abs(alignment[0]), Math.Abs(alignment[1])));
            }
            check($"Aimed travel keeps the lateral reach bounded ({dx}, {dz}, offset={maxOffset:F3})", maxOffset < .40);

            for (var frame = 0; frame < 24; frame++)
            {
                contacts.Begin(x, z, 0, 0, aimed: true);
                contacts.Apply(feet, alignment, aimedPhase, .091, .06, .02);
            }
            check("Stopping in aimed locomotion replants both feet under the hips",
                Math.Abs(alignment[0]) < 1e-9 && Math.Abs(alignment[1]) < 1e-9 &&
                feet[1] == 0 && feet[5] == 0 && Math.Abs(feet[0]) < 1e-9 && Math.Abs(feet[4]) < 1e-9);
        }

        contacts.Reset();
        contacts.Begin(0, 0, 0, 0, aimed: true);
        contacts.Apply(feet, alignment, 0, .091, .06);
        var turnSupport = true;
        for (var i = 0; i < 65; i++)
        {
            var turn = Math.Min(1, (i + 1) * .05) * Math.PI;
            turnSupport &= contacts.Begin(0, 0, turn, 0, aimed: true) == 0;
            contacts.Apply(feet, alignment, 0, .091, .06, .02);
            turnSupport &= feet[1] == 0 || feet[5] == 0;
            turnSupport &= double.Hypot(feet[6] - feet[2], feet[7] - feet[3]) > .13;
            turnSupport &= double.Hypot((feet[2] + feet[6]) / 2, (feet[3] + feet[7]) / 2) < .16;
        }
        check("Aimed turning in place keeps a compact stance and a grounded foot", turnSupport);
        CheckSkinRetargeting(check);
        CheckGaitPose(check);
        CheckSkinHeight(check);
        CheckHandSwing(check);
        CheckAnkleSwing(check);
        CheckOutfitAndPosture(check);
    }

    private static void CheckOutfitAndPosture(Action<string, bool> check)
    {
        var outfit = PlayerOutfit.Select("first-identity");
        check("Player outfit is stable per identity and varies between identities",
            outfit.SequenceEqual(PlayerOutfit.Select("first-identity")) && !outfit.SequenceEqual(PlayerOutfit.Select("second-identity")));
        var cloth = new PlayerOutfit();
        var deflected = 0d;
        for (var i = 0; i < 60; i++) deflected = cloth.Advance(3, .3, 0, 1d / 60);
        var settled = deflected;
        for (var i = 0; i < 120; i++) settled = cloth.Advance(0, 0, 0, 1d / 60);
        check("Wind drag deflects the cloth and gravity brings it back", deflected > .05 && Math.Abs(settled) < .01);

        var posture = new PlayerPosture();
        Span<double> pose = stackalloc double[9];
        posture.Jump();
        var apex = 0d;
        for (var i = 0; i < 70; i++)
        {
            posture.Advance(pose, 0, .25, 1d / 60, 0, 0);
            apex = Math.Max(apex, pose[1]);
        }
        check("Jump respects overhead clearance and lands", apex is > .20 and <= .25 && pose[1] == 0);
        posture.Toggle(PlayerPosture.Kind.Crawling);
        posture.Advance(pose, 0, 1, .05, 0, 0);
        check("Crawl lowers the pose and limits movement", pose[3] > 0 && posture.SpeedScale is > 0 and < .5);
        posture.Toggle(PlayerPosture.Kind.Sitting);
        posture.Advance(pose, 0, 1, .05, 0, 0);
        check("Sitting changes the pose and holds position", pose[4] > 0 && posture.SpeedScale == 0);
    }

    private static void CheckAnkleSwing(Action<string, bool> check)
    {
        var contacts = new PlayerFootContacts();
        Span<double> feet = stackalloc double[10];
        Span<double> alignment = stackalloc double[4];
        var highestPitch = 0d;
        var lowestPitch = 0d;
        var lagged = false;
        var supportStill = true;
        var wasPlanted = false;
        var supportZ = 0d;
        for (var frame = 0; frame < 140; frame++)
        {
            const double step = 1d / 120, pace = 1.65, spacing = .091, shoe = .06;
            var distance = pace * step;
            var phase = frame * distance * Math.Tau / contacts.Stride % Math.Tau;
            contacts.Begin(0, frame * distance, 0, distance);
            PlayerPose.FootTargets(feet, 0, phase, 1, pace, spacing, shoe, contacts.Stride, softSwing: true);
            var target = feet[8];
            contacts.Apply(feet, alignment, phase, spacing, shoe, step, softSwing: true);
            highestPitch = Math.Max(highestPitch, feet[8]);
            lowestPitch = Math.Min(lowestPitch, feet[8]);
            lagged |= target > .12 && feet[8] < target - .02;
            var planted = feet[1] == 0;
            var worldZ = frame * distance + feet[3];
            if (planted) supportStill &= feet[8] == 0 && (!wasPlanted || Math.Abs(worldZ - supportZ) < 1e-9);
            wasPlanted = planted;
            supportZ = worldZ;
        }
        check("Swing pitch lags the step and reverses before touchdown", lagged && highestPitch > .08 && lowestPitch < -.08);
        check("A planted foot keeps its position and pitch during the ankle swing", supportStill);
    }

    private static void CheckHandSwing(Action<string, bool> check)
    {
        var arm = new PlayerArmSwing();
        const double neutralElbow = -.16, dt = 1d / 60;
        var firstElbow = arm.AdvanceElbow(-.32, neutralElbow, 1, 1, dt);
        var secondElbow = arm.AdvanceElbow(-.32, neutralElbow, 1, 1, dt);
        double settledElbow = 0;
        for (var frame = 0; frame < 90; frame++)
            settledElbow = arm.AdvanceElbow(-.32, neutralElbow, 1, 1, dt);
        var reversingElbow = arm.AdvanceElbow(.32, neutralElbow, 1, 1, dt);
        check("The elbow extends behind a forward swing, flexes on reversal, and settles",
            firstElbow > neutralElbow && secondElbow > firstElbow &&
            Math.Abs(settledElbow - neutralElbow) < .001 && reversingElbow < neutralElbow);
        arm.Reset();
        check("An idle arm has no residual elbow swing", arm.AdvanceElbow(0, 0, 1, 0, dt) == 0);

        var wrist = new PlayerArmSwing();
        var first = wrist.Advance(.3, 1d / 60);
        var second = wrist.Advance(.3, 1d / 60);
        double settled = 0;
        for (var frame = 0; frame < 60; frame++) settled = wrist.Advance(.3, 1d / 60);
        var reversal = wrist.Advance(-.3, 1d / 60);
        check("The wrist follows the forearm with bounded lag, then settles",
            first is < 0 and > -.1 && second < first && Math.Abs(settled) < .001 && reversal is > 0 and < .1);
        wrist.Reset();
        check("Teleport clears residual wrist motion", wrist.Advance(0, .016) == 0);
        Span<double> fingers = stackalloc double[3];
        Span<double> peaks = stackalloc double[3];
        Span<int> peakFrames = stackalloc int[3];
        peaks.Clear();
        peakFrames.Clear();
        for (var frame = 0; frame < 90; frame++)
        {
            const double step = 1d / 60;
            wrist.Advance(.3, step);
            wrist.AdvanceFingers(.3, step, fingers);
            for (var joint = 0; joint < fingers.Length; joint++)
            {
                var excursion = Math.Abs(fingers[joint]);
                if (excursion <= peaks[joint]) continue;
                peaks[joint] = excursion;
                peakFrames[joint] = frame;
            }
        }
        check("Finger motion reaches the base, middle and tip in sequence",
            peaks[0] > peaks[1] && peaks[1] > peaks[2] && peaks[2] > .001 &&
            peakFrames[0] < peakFrames[1] && peakFrames[1] < peakFrames[2]);
        check("The whole hand settles after the forearm stops", fingers.ToArray().All(angle => Math.Abs(angle) < .001));
    }

    private static void CheckSkinHeight(Action<string, bool> check)
    {
        var height = new PlayerSkinHeight();
        double lowest = double.PositiveInfinity, highest = double.NegativeInfinity;
        for (var frame = 0; frame < 60; frame++)
        {
            var root = .03 + .03 * Math.Cos(frame * .43);
            var body = root + height.Advance(root, .03, 1, .12, 1d / 60);
            lowest = Math.Min(lowest, body);
            highest = Math.Max(highest, body);
        }
        check("The visual hips reject cyclic ankle-solver root height", highest - lowest < .001);
        check("A low ankle target constrains the smoothed hip height", height.Advance(.1, .1, 0, -.12, .016) <= -.12);
        height.Reset();
        check("The visual height snaps to a new support after teleport", Math.Abs(height.Advance(5, 5, 0, .12, .016)) < .001);

        Span<double> feet = stackalloc double[10];
        PlayerPose.FootTargets(feet, 0, .5 * Math.PI + .01, 1, 1.75, .09, .05, softSwing: true);
        check("The foot leaves the ground with a soft vertical onset", feet[1] is > 0 and < .00001);
    }

    private static void CheckGaitPose(Action<string, bool> check)
    {
        Span<double> rightLeads = stackalloc double[PlayerGaitPose.ChannelCount];
        Span<double> leftLeads = stackalloc double[PlayerGaitPose.ChannelCount];
        PlayerGaitPose.Write(rightLeads, Math.PI / 2, 1, PlayerMovement.WalkSpeed, 1.75);
        PlayerGaitPose.Write(leftLeads, 3 * Math.PI / 2, 1, PlayerMovement.WalkSpeed, 1.75);
        check("The hips and chest counterrotate and the arms alternate their flexion",
            rightLeads[0] < 0 && rightLeads[3] > 0 && rightLeads[8] < rightLeads[9] &&
            leftLeads[0] > 0 && leftLeads[3] < 0 && leftLeads[8] > leftLeads[9]);
        PlayerGaitPose.Write(leftLeads, 0, 1, PlayerMovement.RunSpeed, 1.75);
        check("The pelvis moves toward the support side and the running elbows bend further",
            leftLeads[2] < 0 && leftLeads[8] < rightLeads[8]);
        PlayerGaitPose.Write(leftLeads, Math.PI / 2, 0, PlayerMovement.RunSpeed, 1.75);
        check("The gait blends back into the relaxed body pose", leftLeads[..10].IndexOfAnyExcept(0d) < 0);
    }

    private static void CheckSkinRetargeting(Action<string, bool> check)
    {
        double[] leg = [0, .92, 0, 0, .50, .12, 0, .10, 0, .12, .18, .25, 0, 0, 1];
        Span<double> result = stackalloc double[PlayerSkinLegs.LegOutputSize];

        PlayerSkinLegs.Solve(leg, result);
        check("Skin IK closes on a reachable ankle target", Distance(leg.AsSpan(9), result[3..]) < 1e-5);
        check("Skin IK preserves both bind segment lengths",
            Math.Abs(Distance(leg, result) - Distance(leg, leg.AsSpan(3))) < 1e-5 &&
            Math.Abs(Distance(result, result[3..]) - Distance(leg.AsSpan(3), leg.AsSpan(6))) < 1e-5);
        check("Skin IK keeps the knee on the forward side of the leg", result[2] > result[5] / 2);

        leg[10] = -.5;
        PlayerSkinLegs.Solve(leg, result);
        check("Skin IK clamps an unreachable target without stretching",
            Distance(leg, result[3..]) < Distance(leg, leg.AsSpan(3)) + Distance(leg.AsSpan(3), leg.AsSpan(6)) &&
            Math.Abs(Distance(result, result[3..]) - Distance(leg.AsSpan(3), leg.AsSpan(6))) < 1e-5);

        leg[10] = .085;
        var hipShift = PlayerSkinLegs.HighestReachableHipShift(leg);
        PlayerSkinLegs.Solve(leg, result, hipShift);
        check("Lowering the visual hips allows the projected step to reach its support",
            hipShift is < 0 and > -.15f && Distance(leg.AsSpan(9), result[3..]) < 1e-5);

        static double Distance(ReadOnlySpan<double> a, ReadOnlySpan<double> b)
            => double.Hypot(a[0] - b[0], double.Hypot(a[1] - b[1], a[2] - b[2]));
    }
}
