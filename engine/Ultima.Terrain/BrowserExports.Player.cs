using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;

namespace Ultima.Terrain;

internal static partial class BrowserExports
{
    private static PlayerMovement _movement = new(0, 0, 0);
    private static readonly double[] MovementResult = new double[4];
    private static readonly double[] PadResult = new double[4];

    private static readonly PlayerCamera Camera = new();
    private static readonly double[] CameraResult = new double[8];
    private static double _kneeHeight, _footGap, _hipHeight, _legLength, _ankleOffset, _spacing, _shoeOffset;

    private static PlayerRemoteAvatars? _remoteAvatars;

    [JSExport]
    internal static string GetAnatomy(string kind, double height)
        => JsonSerializer.Serialize(PlayerAnatomy.Create(kind, height), AnatomyJsonContext.Default.AnatomyJointArray);

    [JSExport]
    internal static void ConfigurePlayer(double height, double kneeHeight, double footGap,
        double hipHeight, double legLength, double ankleOffset, double spacing, double shoeOffset)
    {
        _movement = new(height, kneeHeight, footGap);
        (_kneeHeight, _footGap, _hipHeight, _legLength, _ankleOffset, _spacing, _shoeOffset) =
            (kneeHeight, footGap, hipHeight, legLength, ankleOffset, spacing, shoeOffset);
        _remoteAvatars = new(_surface, footGap, hipHeight, legLength, ankleOffset, spacing, shoeOffset);
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

    // 12 pose channels, 5 timeline channels, 7 leg channels and 13 support-probe channels.
    private static readonly double[] CharacterFrame = new double[37];
    // 24 pose e pernas, seguidas de X, Z, orientação, fase da patrulha e distância.
    private static readonly double[] CompanionFrame = new double[29];
    private static readonly double[] LegResult = new double[7];

    [JSExport]
    internal static bool CanCollect(double dx, double dy, double dz) => PlayerPose.CanCollect(dx, dy, dz);

    [JSExport]
    internal static double[] AnimateCharacter(double x, double y, double z, double yaw,
        double phase, double weight, double clock, double speech, double elapsed, double duration,
        double distance, double delta, int nameLength, bool listening, bool frozen, double bend, double reach,
        bool hasLegs, bool isPlayer, bool hasTunnels)
    {
        AnimateCharacterFrame(CharacterFrame, x, y, z, yaw, phase, weight, clock, speech, elapsed, duration,
            distance, delta, nameLength, listening, frozen, bend, reach, hasLegs, isPlayer, hasTunnels);
        return CharacterFrame;
    }

    [JSExport]
    internal static double[] AnimateCompanion(double x, double y, double z, double yaw,
        double phase, double weight, double clock, double speech, double delta, int nameLength,
        double homeX, double homeZ, double heading)
    {
        var motion = PlayerCompanionMotion.Patrol(x, z, yaw, homeX, homeZ, heading, delta);
        AnimateCharacterFrame(CompanionFrame, motion.X, y, motion.Z, motion.Yaw, phase, weight, clock, speech,
            0, 0, motion.Distance, delta, nameLength, false, false, 0, 0, true, false, false);
        CompanionFrame[24] = motion.X;
        CompanionFrame[25] = motion.Z;
        CompanionFrame[26] = motion.Yaw;
        CompanionFrame[27] = motion.Phase;
        CompanionFrame[28] = motion.Distance;
        return CompanionFrame;
    }

    [JSExport]
    internal static void AddRemoteAvatar(int id, int nameLength, double x, double y, double z, double yaw)
        => _remoteAvatars!.Add(id, nameLength, x, y, z, yaw);

    [JSExport]
    internal static void RemoveRemoteAvatar(int id) => _remoteAvatars!.Remove(id);

    [JSExport]
    internal static bool SetRemoteAvatarTarget(int id, double x, double z, double yaw)
        => _remoteAvatars!.Target(id, x, z, yaw);

    [JSExport]
    internal static double[] UpdateRemoteAvatars(double delta, double playerX, double playerZ)
        => _remoteAvatars!.Tick(delta, playerX, playerZ);

    private static void AnimateCharacterFrame(Span<double> result, double x, double y, double z, double yaw,
        double phase, double weight, double clock, double speech, double elapsed, double duration,
        double distance, double delta, int nameLength, bool listening, bool frozen, double bend, double reach,
        bool hasLegs, bool isPlayer, bool hasTunnels)
    {
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
        if (!hasLegs) return;

        Span<double> feet = stackalloc double[10];
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
            feet[0], feet[1], leftFloor, false, feet[4], feet[5], rightFloor, false, feet[8], feet[9]);
        if (!isPlayer) return;
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
        result[35] = feet[8];
        result[36] = feet[9];
    }

    private static double Floor(double x, double z, double y, bool useVolume)
        => useVolume ? _volumes.Floor(x, z, y, _kneeHeight) : _surface.Height(x, z);

    [JSExport]
    internal static double[] SolveSupportedLegs(double ground, double previousY, double delta,
        double leftForward, double leftLift, double leftFloor, double leftSupport,
        double rightForward, double rightLift, double rightFloor, double rightSupport, double leftPitch, double rightPitch)
    {
        PlayerPose.SolveLegs(LegResult, ground, previousY, delta, _footGap, _hipHeight, _legLength, _ankleOffset,
            leftForward, leftLift, leftSupport, leftSupport > leftFloor,
            rightForward, rightLift, rightSupport, rightSupport > rightFloor, leftPitch, rightPitch);
        return LegResult;
    }
}
