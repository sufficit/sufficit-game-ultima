using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;

using Ultima.Models.Core;

namespace Ultima.Terrain;

internal static partial class BrowserExports
{
    private static PlayerCharacterSpace _characters = new();
    private static PlayerMovement _movement = new(0, 0, 0);
    private static readonly double[] SpawnResult = new double[2];
    private static readonly double[] MovementResult = new double[5];
    private static readonly PlayerInput Input = new();
    private static readonly double[] InputResult = new double[7];

    private static readonly HumanoidBody DefaultBody = Humanoid.ResolveBody(null);
    private static HumanoidBody _body = DefaultBody;
    private static PlayerFootContacts PlayerFeet = new();
    private static readonly PlayerCamera Camera = new();
    private static readonly PlayerCameraObstacles CameraObstacles = new();
    private static readonly double[] CameraResult = new double[12];
    private static double _kneeHeight, _footGap, _hipHeight, _femurLength, _tibiaLength, _ankleOffset, _spacing, _shoeOffset;

    private static PlayerRemoteAvatars? _remoteAvatars;

    [JSExport]
    internal static string GetAnatomy(string kind, double height)
        => JsonSerializer.Serialize(PlayerAnatomy.Create(kind, height), AnatomyJsonContext.Default.AnatomyJointArray);

    [JSExport]
    internal static string ConfigurePlayer(string profile)
    {
        // Resolve the catalog values once per scene. The physics state and renderer packet share this body.
        var body = Humanoid.ResolveBody(JsonSerializer.Deserialize(profile, PlayerBodyJson.Default.HumanoidProfile));
        _body = body;
        _characters = new();
        _movement = new(body.Height, body.KneeHeight, body.FloorOffset, _characters, body.ColliderRadius);
        ResetPlayerPosture();
        (_kneeHeight, _footGap, _hipHeight, _femurLength, _tibiaLength, _ankleOffset, _spacing, _shoeOffset) =
            (body.KneeHeight, body.FloorOffset, body.HipHeight, body.FemurLength, body.TibiaLength, body.AnkleHeight + HumanoidBody.SoleClearance,
                body.LegSpacing, body.ShoeOffset);
        _remoteAvatars = new(_surface, DefaultBody.FloorOffset, DefaultBody.HipHeight, DefaultBody.FemurLength,
            DefaultBody.AnkleHeight + HumanoidBody.SoleClearance, DefaultBody.LegSpacing, DefaultBody.ShoeOffset, _characters);
        Camera.SetDefaultRadius(PlayerCamera.DefaultRadius);
        CameraObstacles.Set([], 0);
        PlayerFeet = new(body.LegLength / DefaultBody.LegLength, body.Build * body.Height / DefaultBody.Height);
        SkinHeight.Reset();
        LeftArmSwing.Reset();
        RightArmSwing.Reset();
        Input.Clear();
        return JsonSerializer.Serialize(new PlayerBodyFrame(body, PlayerAnatomy.Create("humano", body.Height, body)),
            PlayerBodyJson.Default.PlayerBodyFrame);
    }

    [JSExport]
    internal static void ResetFootContacts()
    {
        PlayerFeet.Reset();
        SkinHeight.Reset();
        LeftArmSwing.Reset();
        RightArmSwing.Reset();
        _movement.ResetRecovery();
    }

    [JSExport]
    internal static double[] PlacePlayer(double x, double y, double z)
    {
        (x, z) = _movement.ClampPosition(x, z);
        (SpawnResult[0], SpawnResult[1]) = _characters.Place(PlayerCharacterSpace.Player, x, y, z);
        _characters.Set(PlayerCharacterSpace.Player, SpawnResult[0], y, SpawnResult[1]);
        Camera.ResetFollow();
        return SpawnResult;
    }

    [JSExport]
    internal static void SetCharacterPositions(
        double px, double py, double pz, double gx, double gy, double gz, double wx, double wy, double wz)
    {
        _characters.Set(PlayerCharacterSpace.Player, px, py, pz);
        _characters.Set(PlayerCharacterSpace.Guardian, gx, gy, gz);
        _characters.Set(PlayerCharacterSpace.Wanderer, wx, wy, wz);
    }

    [JSExport]
    internal static int[] SetCharacterHitboxes(double[] values, int length)
    {
        _characters.SetHitboxes(values.AsSpan(0, length), tagged: true);
        return length == 0 ? [] : _characters.CollectLimbContacts();
    }

    [JSExport]
    internal static bool TryCharacterLimbPose(int id, int limb, double[] values, int length)
        => _characters.TryLimbPose(id, limb, values.AsSpan(0, length));

    [JSExport]
    internal static double[] ResolveCharacterContacts()
    {
        var corrections = _characters.ResolveAnimatedContacts();
        for (var i = 0; i < corrections.Length; i += 3)
        {
            var id = (int)corrections[i];
            if (id >= 0) _remoteAvatars!.CorrectPosition(id, corrections[i + 1], corrections[i + 2]);
            else if (id == PlayerCharacterSpace.Player) PlayerFeet.Reset();
        }
        return corrections;
    }

    private static readonly double[] ShortFoot = new double[4];

    [JSExport]
    internal static double[] SolveShortenedFoot(double bodyY, double support, double forward, double lateral,
        double lift, double pitch)
    {
        PlayerPose.SolveSingleLeg(ShortFoot, bodyY + _hipHeight - support - _ankleOffset - lift,
            forward, lateral, _femurLength, pitch, _tibiaLength);
        return ShortFoot;
    }

    [JSExport]
    internal static void OnFootContact(double shortening) => _movement.TripOnFootContact(shortening);

    [JSExport]
    internal static void SetCameraObstacles(double[] bounds, double clearance)
        => CameraObstacles.Set(bounds, clearance);

    [JSExport]
    internal static int[] QueryCameraObstacles(double minX, double minZ, double maxX, double maxZ)
        => CameraObstacles.Query(minX, minZ, maxX, maxZ);

    [JSExport]
    internal static int CycleCameraView(double facing) => (int)Camera.Cycle(facing);

    [JSExport]
    internal static void ResetCamera(double facing) => Camera.Reset(facing);

    [JSExport]
    internal static double[] AdvanceCamera(double alpha, double beta, double radius, double maxZoom,
        double delta, bool frozen, double lookX, double lookY, double dragX, double dragY, double wheel, double floor)
    {
        Camera.Advance(CameraResult, alpha, beta, radius, maxZoom, delta, frozen, lookX, lookY, dragX, dragY, wheel, floor);
        CameraResult[11] = _body.CameraTargetHeight;
        return CameraResult;
    }

    [JSExport]
    internal static void StopPlayer() => _movement.Stop();

    [JSExport]
    internal static void ConfigureAuthoredScene(double[] bounds, double[] boxes, double orbitRadius)
    {
        _movement.SetSceneBounds(bounds);
        _movement.SetSolidBoxes(boxes);
        Camera.SetDefaultRadius(orbitRadius);
    }

    [JSExport]
    internal static void SetPlayerPosition(double x, double y, double z)
        => _characters.Set(PlayerCharacterSpace.Player, x, y, z);

    [JSExport]
    internal static void SetColliders(double[] colliders) => _movement.SetColliders(colliders);

    [JSExport]
    internal static bool SetPlayerInputKey(string code, bool down) => HandlePostureKey(code, down) || Input.SetKey(code, down);

    [JSExport]
    internal static bool SetPlayerInputTouch(bool look, double x, double y) => Input.SetTouch(look, x, y);

    [JSExport]
    internal static int SetPlayerInputPad(double x, double y, double lookX, double lookY, double dpadX, double dpadY)
        => Input.SetGamepad(x, y, lookX, lookY, dpadX, dpadY);

    [JSExport]
    internal static void ClearPlayerInput(bool keepPad) => Input.Clear(keepPad);

    [JSExport]
    internal static double[] ReadPlayerInput()
    {
        Input.Read(InputResult);
        return InputResult;
    }

    [JSExport]
    internal static double[] MovePlayer(double x, double y, double z, double facing, double forwardX, double forwardZ,
        double keyX, double keyZ, double analogX, double analogZ, bool sprint, double delta, bool hasTunnels)
    {
        (MovementResult[0], MovementResult[1], MovementResult[2], MovementResult[3]) = _movement.Advance(
            x, y, z, facing, forwardX, forwardZ, keyX, keyZ, analogX, analogZ, sprint, delta, _surface, _volumes, hasTunnels, aimYaw: Camera.AimYaw);
        MovementResult[4] = _movement.PivotProgress >= 0 ? 1 : 0;
        return MovementResult;
    }

    [JSExport]
    internal static int NearbyObstacle(double x, double y, double z) => _movement.NearbyObstacle(x, y, z);

    [JSExport]
    internal static bool CanStepOn(double minY, double maxY, double ground) => _movement.CanStep(minY, maxY, ground);

    // 12 pose channels, 5 timeline channels, 7 leg channels and 13 support-probe channels, 2 hip rolls, 2 foot yaws, 2 lateral offsets, 2 knee swivels and 1 torso yaw.
    private const int GaitChannelsStart = 46;
    private static readonly double[] CharacterFrame = new double[GaitChannelsStart + PlayerGaitPose.ChannelCount];
    // 24 pose e pernas, seguidas de X, Z, orientação, fase da patrulha e distância.
    private static readonly double[] CompanionFrame = new double[29];
    private static readonly double[] LegResult = new double[9];

    [JSExport]
    internal static bool CanCollect(double dx, double dy, double dz) => PlayerPose.CanCollect(dx, dy, dz);

    [JSExport]
    internal static double[] AnimateCharacter(double x, double y, double z, double yaw,
        double phase, double weight, double clock, double speech, double elapsed, double duration,
        double distance, double delta, int nameLength, bool listening, bool frozen, double bend, double reach,
        bool hasLegs, bool isPlayer, bool hasTunnels)
    {
        AnimateCharacterFrame(CharacterFrame, x, y, z, yaw, phase, weight, clock, speech, elapsed, duration,
            distance, delta, nameLength, listening, frozen, bend, reach, hasLegs, isPlayer, hasTunnels,
            aimed: isPlayer && Camera.View == PlayerCameraView.Close);
        return CharacterFrame;
    }

    [JSExport]
    internal static double[] AnimateCompanion(double x, double y, double z, double yaw,
        double phase, double weight, double clock, double speech, double delta, int nameLength,
        double homeX, double homeZ, double heading)
    {
        var motion = PlayerCompanionMotion.Patrol(x, z, yaw, homeX, homeZ, heading, delta);
        var allowed = _characters.Move(PlayerCharacterSpace.Wanderer, x, y, z, motion.X, motion.Z);
        var travelled = double.Hypot(allowed.X - x, allowed.Z - z);
        AnimateCharacterFrame(CompanionFrame, allowed.X, y, allowed.Z, motion.Yaw, phase, weight, clock, speech,
            0, 0, travelled, delta, nameLength, false, false, 0, 0, true, false, false);
        _characters.Set(PlayerCharacterSpace.Wanderer, allowed.X, CompanionFrame[17], allowed.Z);
        CompanionFrame[24] = allowed.X;
        CompanionFrame[25] = allowed.Z;
        CompanionFrame[26] = motion.Yaw;
        CompanionFrame[27] = allowed.Blocked ? heading : motion.Phase;
        CompanionFrame[28] = travelled;
        return CompanionFrame;
    }

    [JSExport]
    internal static void AddRemoteAvatar(int id, int nameLength, double x, double y, double z, double yaw)
        => _remoteAvatars!.Add(id, nameLength, x, y, z, yaw);

    [JSExport]
    internal static void RemoveRemoteAvatar(int id) => _remoteAvatars!.Remove(id);

    [JSExport]
    internal static bool SetRemoteAvatarTarget(int id, double x, double z, double yaw)
    {
        if (!double.IsFinite(x) || !double.IsFinite(z) || !double.IsFinite(yaw)) return false;
        var (sceneX, sceneZ) = _movement.ClampPosition(x, z);
        return _remoteAvatars!.Target(id, sceneX, sceneZ, yaw);
    }

    [JSExport]
    internal static double[] UpdateRemoteAvatars(double delta, double playerX, double playerZ)
        => _remoteAvatars!.Tick(delta, playerX, playerZ);

    private static void AnimateCharacterFrame(Span<double> result, double x, double y, double z, double yaw,
        double phase, double weight, double clock, double speech, double elapsed, double duration,
        double distance, double delta, int nameLength, bool listening, bool frozen, double bend, double reach,
        bool hasLegs, bool isPlayer, bool hasTunnels, bool aimed = false)
    {
        clock += delta;
        speech = Math.Max(0, speech - delta);
        var (next, previous, progress) = PlayerPose.AdvanceAction(elapsed, duration, delta, frozen);
        var naturalGait = isPlayer && _movement.NaturalGait;
        var walkSpeed = isPlayer ? _movement.WalkingSpeed : PlayerMovement.WalkSpeed;
        var runSpeed = isPlayer ? _movement.RunningSpeed : PlayerMovement.RunSpeed;
        var strideLength = PlayerPose.StrideLength;
        var pivot = isPlayer ? _movement.PivotProgress : -1;
        if (isPlayer)
        {
            var strideDistance = PlayerFeet.Begin(x, z, yaw, distance, aimed);
            if (pivot < 0) distance = strideDistance;
            if (naturalGait)
            {
                var pace = delta > 0 ? distance / delta : 0;
                var strideMultiplier = .94 + .51 * PlayerPose.SmoothStep(walkSpeed, runSpeed, pace);
                PlayerFeet.StrideMultiplier += (strideMultiplier - PlayerFeet.StrideMultiplier) * (1 - Math.Exp(-12 * delta));
            }
            strideLength = aimed ? PlayerFeet.AimedStride : PlayerFeet.Stride;
        }
        PlayerPose.Animate(result, phase, weight, clock, distance, delta, nameLength, listening,
            speech > 0 && listening, progress, bend, reach, strideLength, isPlayer, walkSpeed, runSpeed);
        result[12] = clock;
        result[13] = speech;
        result[14] = next;
        result[15] = previous;
        result[16] = progress;
        if (!hasLegs) return;

        Span<double> feet = stackalloc double[10];
        var spacing = isPlayer ? _spacing : DefaultBody.LegSpacing;
        var shoeOffset = isPlayer ? _shoeOffset : DefaultBody.ShoeOffset;
        var speed = delta > 0 ? distance / delta : 0;
        PlayerPose.FootTargets(feet, yaw, result[0], result[1], speed, spacing, shoeOffset, strideLength,
            walkSpeed, runSpeed, softSwing: naturalGait);
        Span<double> contact = stackalloc double[4];
        if (isPlayer)
        {
            if (pivot >= 0)
            {
                PlayerFeet.ApplyPivot(feet, contact, _spacing, _shoeOffset, _movement.PivotStart, _movement.PivotTurn, pivot,
                    delta > 0 ? distance / delta : 0);
                if (pivot >= 1) result[0] = PlayerFeet.PivotExitPhase;
            }
            else if (PlayerFeet.Apply(feet, contact, result[0], _spacing, _shoeOffset, delta, softSwing: naturalGait))
                _movement.Trip(0.7);
            PlayerPose.Brace(result, _movement.Recover(delta));
        }
        var leftX = x + feet[2];
        var leftZ = z + feet[3];
        var rightX = x + feet[6];
        var rightZ = z + feet[7];
        var useVolume = isPlayer && hasTunnels;
        var ground = Floor(x, z, y, useVolume);
        var leftFloor = Floor(leftX, leftZ, y, useVolume);
        var rightFloor = Floor(rightX, rightZ, y, useVolume);
        if (!isPlayer)
        {
            PlayerPose.SolveLegs(result[17..24], ground, y, delta, DefaultBody.FloorOffset,
                DefaultBody.HipHeight, DefaultBody.FemurLength, DefaultBody.AnkleHeight + HumanoidBody.SoleClearance,
                feet[0], feet[1], leftFloor, false, feet[4], feet[5], rightFloor, false, feet[8], feet[9]);
            return;
        }
        Span<double> legs = stackalloc double[9];
        PlayerPose.SolveLegs(legs, ground, y, delta, _footGap, _hipHeight, _femurLength, _ankleOffset,
            feet[0], feet[1], leftFloor, false, feet[4], feet[5], rightFloor, false,
            feet[8], feet[9], contact[0], contact[1], _tibiaLength);
        legs[..7].CopyTo(result[17..24]);
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
        result[37] = legs[7];
        result[38] = legs[8];
        result[39] = contact[2];
        result[40] = contact[3];
        result[41] = contact[0];
        result[42] = contact[1];
        result[43] = PlayerPose.KneeSwivel(contact[2]);
        result[44] = PlayerPose.KneeSwivel(contact[3]);
        result[45] = pivot is > 0 and < 1 ? -Math.Sign(_movement.PivotTurn) * 0.23 * Math.Sin(Math.PI * pivot) : 0;
        var gaitWeight = result[1] * (1 - Math.Sin(Math.PI * Math.Clamp(progress, 0, 1)));
        var gait = result[GaitChannelsStart..];
        PlayerGaitPose.Write(gait, result[0], gaitWeight, speed, _body.Height, walkSpeed, runSpeed);
        if (naturalGait)
        {
            PlayerGaitPose.ApplyAuthoredWalk(result, gait, gaitWeight, speed, _body.Height, walkSpeed, runSpeed);
            var walking = 1 - PlayerPose.SmoothStep(walkSpeed, runSpeed, speed);
            gait[8] = LeftArmSwing.AdvanceElbow(result[10], gait[8], walking, gaitWeight, delta);
            gait[9] = RightArmSwing.AdvanceElbow(result[11], gait[9], walking, gaitWeight, delta);
        }
        var leftForearm = result[2] + result[10] + gait[8];
        var rightForearm = result[2] + result[11] + gait[9];
        gait[10] = LeftArmSwing.Advance(leftForearm, delta);
        gait[11] = RightArmSwing.Advance(rightForearm, delta);
        LeftArmSwing.AdvanceFingers(leftForearm, delta, gait[12..15]);
        RightArmSwing.AdvanceFingers(rightForearm, delta, gait[15..18]);
    }

    private static double Floor(double x, double z, double y, bool useVolume)
        => useVolume ? _volumes.Floor(x, z, y, _kneeHeight) : _surface.Height(x, z);

    [JSExport]
    internal static double[] SolveSupportedLegs(double ground, double previousY, double delta,
        double leftForward, double leftLift, double leftFloor, double leftSupport,
        double rightForward, double rightLift, double rightFloor, double rightSupport, double leftPitch, double rightPitch,
        double leftLateral, double rightLateral, double bodyDrop = 0)
    {
        PlayerPose.SolveLegs(LegResult, ground, previousY, delta, _footGap, _hipHeight, _femurLength, _ankleOffset,
            leftForward, leftLift, leftSupport, leftSupport > leftFloor,
            rightForward, rightLift, rightSupport, rightSupport > rightFloor, leftPitch, rightPitch, leftLateral, rightLateral, _tibiaLength, bodyDrop);
        return LegResult;
    }
}
