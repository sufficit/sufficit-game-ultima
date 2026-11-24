namespace Ultima.Terrain;

internal sealed class PlayerRemoteAvatars(TerrainSurface surface, double footGap, double hipHeight,
    double legLength, double ankleOffset, double spacing, double shoeOffset, PlayerCharacterSpace? characters = null)
{
    internal const int Stride = 18;
    internal const int PoseReady = 1;
    internal const int NearPlayer = 2;
    internal const double NearEnter = 28;
    internal const double NearExit = 32;
    internal const double DistantInterval = 0.1;
    private readonly Dictionary<int, Avatar> _avatars = [];
    private double[] _frame = [];

    internal void Add(int id, int nameLength, double x, double y, double z, double yaw)
    {
        if (characters is not null)
        {
            (x, z) = characters.Place(id, x, y, z);
            characters.Set(id, x, y, z);
        }
        _avatars.Add(id, new(nameLength, x, y, z, yaw));
    }

    internal void Remove(int id)
    {
        _avatars.Remove(id);
        characters?.Remove(id);
    }

    internal bool Target(int id, double x, double z, double yaw)
    {
        if (!_avatars.TryGetValue(id, out var actor)) return false;
        var dx = x - actor.X;
        var dz = z - actor.Z;
        var snap = !actor.HasTarget || dx * dx + dz * dz > 25;
        actor.HasTarget = true;
        var destinationY = snap ? surface.Height(x, z) + footGap : actor.Y;
        (x, z) = characters?.Place(id, x, destinationY, z) ?? (x, z);
        (actor.TargetX, actor.TargetZ, actor.TargetYaw) = (x, z, yaw);
        if (snap)
        {
            (actor.X, actor.Y, actor.Z, actor.Yaw) = (x, destinationY, z, yaw);
            characters?.Set(id, x, destinationY, z);
            actor.Travel = 0;
            actor.ForcePose = true;
        }
        return snap;
    }

    // Identificador, transform, flags de pose e distância, seis canais do tronco e seis das pernas.
    internal double[] Tick(double delta, double playerX, double playerZ)
    {
        var size = _avatars.Count * Stride;
        if (_frame.Length != size) _frame = new double[size];
        Span<double> pose = stackalloc double[12];
        Span<double> feet = stackalloc double[10];
        Span<double> legs = stackalloc double[7];
        var offset = 0;
        foreach (var (id, actor) in _avatars)
        {
            if (actor.HasTarget)
            {
                var motion = PlayerCompanionMotion.Follow(actor.X, actor.Z, actor.Yaw,
                    actor.TargetX, actor.TargetZ, actor.TargetYaw, delta);
                var allowed = characters?.Move(id, actor.X, actor.Y, actor.Z, motion.X, motion.Z)
                    ?? (motion.X, motion.Z, Blocked: false);
                var travelled = double.Hypot(allowed.X - actor.X, allowed.Z - actor.Z);
                (actor.X, actor.Z, actor.Yaw) = (allowed.X, allowed.Z, motion.Yaw);
                actor.Travel += travelled;
                if (allowed.Blocked && delta > 0 && (motion.Distance - travelled) / delta > 1.8 && actor.Stumble <= 0)
                    actor.Stumble = 0.48;
            }
            actor.Stumble = Math.Max(0, actor.Stumble - delta);
            actor.Clock += delta;
            actor.Elapsed += delta;

            var dx = actor.X - playerX;
            var dz = actor.Z - playerZ;
            var nearLimit = actor.Near ? NearExit : NearEnter;
            actor.Near = dx * dx + dz * dz <= nearLimit * nearLimit;
            var poseReady = actor.ForcePose || actor.Near || actor.Elapsed >= DistantInterval ||
                actor.Revision != surface.Revision;
            var row = _frame.AsSpan(offset, Stride);
            if (poseReady)
            {
                if (actor.Travel == 0 && actor.Weight < 0.0001) actor.Weight = 0;
                PlayerPose.Animate(pose, actor.Phase, actor.Weight, actor.Clock, actor.Travel, actor.Elapsed,
                    actor.NameLength, false, false, 0, 0, 0);
                if (actor.Stumble > 0) PlayerPose.Brace(pose, Math.Sin(Math.PI * actor.Stumble / 0.48));
                actor.Phase = pose[0];
                actor.Weight = pose[1];
                var speed = actor.Elapsed > 0 ? actor.Travel / actor.Elapsed : 0;
                PlayerPose.FootTargets(feet, actor.Yaw, actor.Phase, actor.Weight, speed, spacing, shoeOffset);
                var ground = actor.Ground.Sample(surface, actor.X, actor.Z);
                var left = actor.Left.Sample(surface, actor.X + feet[2], actor.Z + feet[3]);
                var right = actor.Right.Sample(surface, actor.X + feet[6], actor.Z + feet[7]);
                PlayerPose.SolveLegs(legs, ground, actor.Y, actor.Elapsed, footGap, hipHeight, legLength, ankleOffset,
                    feet[0], feet[1], left, false, feet[4], feet[5], right, false, feet[8], feet[9]);
                actor.Y = legs[0];
                actor.Elapsed = actor.Travel = 0;
                actor.ForcePose = false;
                actor.Revision = surface.Revision;

                row[6] = pose[2];
                row[7] = pose[3];
                row[8] = pose[4];
                row[9] = pose[6];
                row[10] = pose[10];
                row[11] = pose[11];
                legs[1..].CopyTo(row[12..]);
            }
            characters?.Set(id, actor.X, actor.Y, actor.Z);
            row[0] = id;
            row[1] = actor.X;
            row[2] = actor.Y;
            row[3] = actor.Z;
            row[4] = actor.Yaw;
            row[5] = (poseReady ? PoseReady : 0) | (actor.Near ? NearPlayer : 0);
            offset += Stride;
        }
        return _frame;
    }

    internal struct GroundSample
    {
        private TerrainSurface? _surface;
        private int _revision;
        private double _x, _z, _height;

        internal double Sample(TerrainSurface surface, double x, double z)
        {
            if (_surface == surface && _revision == surface.Revision && _x == x && _z == z) return _height;
            _surface = surface;
            _revision = surface.Revision;
            _x = x;
            _z = z;
            _height = surface.Height(x, z);
            return _height;
        }
    }

    private sealed class Avatar(int nameLength, double x, double y, double z, double yaw)
    {
        internal readonly int NameLength = nameLength;
        internal double X = x, Y = y, Z = z, Yaw = yaw;
        internal double TargetX, TargetZ, TargetYaw;
        internal double Phase, Weight, Clock, Elapsed, Travel, Stumble;
        internal bool HasTarget, ForcePose = true, Near;
        internal int Revision = -1;
        internal GroundSample Ground, Left, Right;
    }
}
