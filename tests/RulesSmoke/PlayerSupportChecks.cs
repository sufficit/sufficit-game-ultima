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
    }
}
