using System.Runtime.InteropServices.JavaScript;

namespace Ultima.Terrain;

internal static partial class BrowserExports
{
    private static PlayerMovement _movement = new(0, 0, 0);
    private static readonly double[] MovementResult = new double[4];
    private static readonly double[] PadResult = new double[4];

    private static readonly PlayerCamera Camera = new();
    private static readonly double[] CameraResult = new double[8];
    private static double _kneeHeight, _footGap, _hipHeight, _legLength, _ankleOffset, _spacing, _shoeOffset;

    [JSExport]
    internal static void ConfigurePlayer(double height, double kneeHeight, double footGap,
        double hipHeight, double legLength, double ankleOffset, double spacing, double shoeOffset)
    {
        _movement = new(height, kneeHeight, footGap);
        (_kneeHeight, _footGap, _hipHeight, _legLength, _ankleOffset, _spacing, _shoeOffset) =
            (kneeHeight, footGap, hipHeight, legLength, ankleOffset, spacing, shoeOffset);
        Camera.Reset();
    }

    [JSExport]
    internal static void ResetCamera() => Camera.Reset();

    [JSExport]
    internal static double[] AdvanceCamera(double alpha, double beta, double radius, double maxZoom,
        double delta, bool frozen, double lookX, double lookY, double dragX, double dragY, double wheel)
    {
        Camera.Advance(CameraResult, alpha, beta, radius, maxZoom, delta, frozen, lookX, lookY, dragX, dragY, wheel);
        return CameraResult;
    }

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

    // 12 pose channels, 5 timeline channels, 7 leg channels and 11 support-probe channels.
    private static readonly double[] CharacterFrame = new double[35];
    private static readonly double[] LegResult = new double[7];

    [JSExport]
    internal static bool CanCollect(double dx, double dy, double dz) => PlayerPose.CanCollect(dx, dy, dz);

    [JSExport]
    internal static double[] AnimateCharacter(double x, double y, double z, double yaw,
        double phase, double weight, double clock, double speech, double elapsed, double duration,
        double distance, double delta, int nameLength, bool listening, bool frozen, double bend, double reach,
        bool hasLegs, bool isPlayer, bool hasTunnels)
    {
        var result = CharacterFrame.AsSpan();
        clock += delta;
        speech = Math.Max(0, speech - delta);
        var (next, previous, progress) = PlayerPose.AdvanceAction(elapsed, duration, delta, frozen);
        PlayerPose.Animate(result, phase, weight, clock, distance, delta, nameLength, listening,
            speech > 0 && listening, progress, bend, reach);
        result[12] = clock;
        result[13] = speech;
        result[14] = next;
        result[15] = previous;
        result[16] = progress;
        if (!hasLegs) return CharacterFrame;

        Span<double> feet = stackalloc double[8];
        PlayerPose.FootTargets(feet, yaw, result[0], result[1], delta > 0 ? distance / delta : 0, _spacing, _shoeOffset);
        var leftX = x + feet[2];
        var leftZ = z + feet[3];
        var rightX = x + feet[6];
        var rightZ = z + feet[7];
        var useVolume = isPlayer && hasTunnels;
        var ground = Floor(x, z, y, useVolume);
        var leftFloor = Floor(leftX, leftZ, y, useVolume);
        var rightFloor = Floor(rightX, rightZ, y, useVolume);
        PlayerPose.SolveLegs(result[17..24], ground, y, delta, _footGap, _hipHeight, _legLength, _ankleOffset,
            feet[0], feet[1], leftFloor, false, feet[4], feet[5], rightFloor, false);
        result[24] = ground;
        result[25] = leftX;
        result[26] = leftZ;
        result[27] = leftFloor;
        result[28] = feet[0];
        result[29] = feet[1];
        result[30] = rightX;
        result[31] = rightZ;
        result[32] = rightFloor;
        result[33] = feet[4];
        result[34] = feet[5];
        return CharacterFrame;
    }

    private static double Floor(double x, double z, double y, bool useVolume)
        => useVolume ? _volumes.Floor(x, z, y, _kneeHeight) : _surface.Height(x, z);

    [JSExport]
    internal static double[] SolveSupportedLegs(double ground, double previousY, double delta,
        double leftForward, double leftLift, double leftFloor, double leftSupport,
        double rightForward, double rightLift, double rightFloor, double rightSupport)
    {
        PlayerPose.SolveLegs(LegResult, ground, previousY, delta, _footGap, _hipHeight, _legLength, _ankleOffset,
            leftForward, leftLift, leftSupport, leftSupport > leftFloor,
            rightForward, rightLift, rightSupport, rightSupport > rightFloor);
        return LegResult;
    }
}
