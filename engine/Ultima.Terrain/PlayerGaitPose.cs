namespace Ultima.Terrain;

internal static class PlayerGaitPose
{
    internal const int ChannelCount = 14;

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
}
