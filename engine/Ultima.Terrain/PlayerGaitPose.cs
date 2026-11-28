namespace Ultima.Terrain;

internal static class PlayerGaitPose
{
    internal const int ChannelCount = 18;

    // These presentation channels follow the same phase as the foot cycle.
    // At phase zero the left foot supports the body. A quarter cycle later
    // the right leg leads and the left arm swings forward.
    internal static void Write(Span<double> pose, double phase, double weight, double speed, double height,
        double walkSpeed = PlayerMovement.WalkSpeed, double runSpeed = PlayerMovement.RunSpeed)
    {
        var (sine, cosine) = Math.SinCos(phase);
        var run = PlayerPose.SmoothStep(walkSpeed, runSpeed, speed);
        var stride = weight * sine;
        var transfer = weight * cosine;
        var chestYaw = weight * Math.Sin(phase - .20) * (.095 + .025 * run);
        var rebound = weight * Math.Sin(phase - .38);
        var elbowBend = weight * (.18 + .65 * run);
        var elbowSwing = rebound * (.12 + .10 * run);

        pose[0] = -stride * (.075 + .035 * run); // Pelvis yaw, radians.
        pose[1] = -transfer * .031; // Pelvis roll, radians.
        pose[2] = -transfer * height * .010; // Side-to-side weight transfer, metres.
        pose[3] = chestYaw;
        pose[4] = transfer * .024; // Chest roll relative to the pelvis rhythm.
        pose[5] = -chestYaw * .73; // Head counterrotation steadies the gaze.
        pose[6] = rebound * .065; // Clavicle protraction.
        pose[7] = -rebound * .036; // Clavicle elevation.
        pose[8] = -elbowBend - elbowSwing;
        pose[9] = -elbowBend + elbowSwing;
    }

    internal static void ApplyAuthoredWalk(Span<double> pose, Span<double> gait, double weight,
        double speed, double height, double walkSpeed, double runSpeed)
    {
        var walk = 1 - PlayerPose.SmoothStep(walkSpeed, runSpeed, speed);
        if (weight == 0 || walk == 0) return;

        Span<double> clip = stackalloc double[PlayerAuthoredWalk.Channels];
        PlayerAuthoredWalk.Sample(pose[0], clip);
        var phase = Math.Sin(pose[0]);
        var running = 1 - walk;
        // Replace only the neutral arm swing. Preserve actions, impact bracing and conversation gestures.
        var proceduralSwing = -phase * pose[1] * (.34 + .40 * running);
        pose[10] += walk * (clip[10] * weight - proceduralSwing);
        pose[11] += walk * (clip[11] * weight + proceduralSwing);

        gait[0] += walk * (clip[0] * weight - gait[0]);
        gait[1] += walk * (clip[1] * weight - gait[1]);
        gait[2] += walk * (clip[2] * height / 1.7 * weight * .65 - gait[2]);
        gait[3] += walk * (clip[3] * weight * .65 - gait[3]);
        gait[4] += walk * (clip[4] * weight * .70 - gait[4]);
        // The head counterturns with the ribcage while the existing wrist and finger springs
        // follow the new arm trajectories. The source head position is lateral lean, not yaw.
        gait[5] += walk * (-clip[3] * .44 * weight - gait[5]);
        gait[4] += walk * clip[5] * .15 * weight;
        gait[6] += walk * (clip[6] * .42 * weight - gait[6]);
        gait[7] += walk * (clip[7] * weight - gait[7]);
        gait[8] += walk * ((clip[8] * 1.55 - .15) * weight - gait[8]);
        gait[9] += walk * ((clip[9] * 1.55 - .15) * weight - gait[9]);
    }
}
