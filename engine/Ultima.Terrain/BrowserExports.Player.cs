using System.Runtime.InteropServices.JavaScript;

namespace Ultima.Terrain;

internal static partial class BrowserExports
{
    private static PlayerMovement _movement = new(0, 0, 0);
    private static readonly double[] MovementResult = new double[4];
    private static readonly double[] PadResult = new double[4];

    [JSExport]
    internal static void ConfigurePlayer(double height, double kneeHeight, double footGap)
        => _movement = new(height, kneeHeight, footGap);

    [JSExport]
    internal static void StopPlayer() => _movement.Stop();

    [JSExport]
    internal static void SetColliders(double[] colliders) => _movement.SetColliders(colliders);

    [JSExport]
    internal static double[] GamepadAxes(double x, double y, double lookX, double lookY)
    {
        (PadResult[0], PadResult[1]) = PlayerMovement.Stick(x, y);
        (PadResult[2], PadResult[3]) = PlayerMovement.Stick(lookX, lookY);
        return PadResult;
    }

    [JSExport]
    internal static double[] MovePlayer(double x, double y, double z, double forwardX, double forwardZ,
        double keyX, double keyZ, double analogX, double analogZ, bool sprint, double delta, bool hasTunnels)
    {
        (MovementResult[0], MovementResult[1], MovementResult[2], MovementResult[3]) = _movement.Advance(
            x, y, z, forwardX, forwardZ, keyX, keyZ, analogX, analogZ, sprint, delta, _surface, _volumes, hasTunnels);
        return MovementResult;
    }

    [JSExport]
    internal static int NearbyObstacle(double x, double y, double z) => _movement.NearbyObstacle(x, y, z);

    [JSExport]
    internal static bool CanStepOn(double minY, double maxY, double ground) => _movement.CanStep(minY, maxY, ground);

    private static readonly double[] PoseResult = new double[12];
    private static readonly double[] FootResult = new double[8];
    private static readonly double[] LegResult = new double[7];

    [JSExport]
    internal static bool CanCollect(double dx, double dy, double dz) => PlayerPose.CanCollect(dx, dy, dz);

    [JSExport]
    internal static double[] AnimatePlayer(double phase, double weight, double clock, double distance, double delta,
        int nameLength, bool listening, bool speaking, double progress, double bend, double reach)
    {
        PlayerPose.Animate(PoseResult, phase, weight, clock, distance, delta, nameLength, listening, speaking,
            progress, bend, reach);
        return PoseResult;
    }

    [JSExport]
    internal static double[] FootTargets(double yaw, double phase, double weight, double speed, double spacing, double shoeOffset)
    {
        PlayerPose.FootTargets(FootResult, yaw, phase, weight, speed, spacing, shoeOffset);
        return FootResult;
    }

    [JSExport]
    internal static double[] SolveLegs(double ground, double previousY, double delta,
        double footGap, double hipHeight, double legLength, double ankleOffset,
        double leftForward, double leftLift, double leftSupport, bool leftOnObstacle,
        double rightForward, double rightLift, double rightSupport, bool rightOnObstacle)
    {
        PlayerPose.SolveLegs(LegResult, ground, previousY, delta, footGap, hipHeight, legLength, ankleOffset,
            leftForward, leftLift, leftSupport, leftOnObstacle, rightForward, rightLift, rightSupport, rightOnObstacle);
        return LegResult;
    }
}
