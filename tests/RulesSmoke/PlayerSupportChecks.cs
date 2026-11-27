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
        check("The gait blends back into the relaxed body pose", leftLeads.IndexOfAnyExcept(0d) < 0);
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
        var hipShift = PlayerSkinLegs.LowestReachableHipShift(leg);
        PlayerSkinLegs.Solve(leg, result, hipShift);
        check("Lowering the visual hips allows the projected step to reach its support",
            hipShift is < 0 and > -.15f && Distance(leg.AsSpan(9), result[3..]) < 1e-5);

        static double Distance(ReadOnlySpan<double> a, ReadOnlySpan<double> b)
            => double.Hypot(a[0] - b[0], double.Hypot(a[1] - b[1], a[2] - b[2]));
    }
}
