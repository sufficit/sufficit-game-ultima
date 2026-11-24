namespace Ultima.Terrain;

internal sealed class PlayerMovement(double height, double kneeHeight, double footGap, PlayerCharacterSpace? characters = null)
{
    internal const double WalkSpeed = 3.5;
    internal const double RunSpeed = 6.5;
    private const double Radius = 0.25;
    private const double WorldHalf = 400;
    private const double Deadzone = 0.18;
    internal const int ColliderStride = 6;
    private double[] _colliders = [];
    private double _speed, _directionX, _directionZ;
    private double _stumble, _strength;
    private const double RecoveryTime = 0.48;

    internal void Trip(double strength)
    {
        if (_stumble > 0) return;
        _stumble = RecoveryTime;
        _strength = strength;
        _speed *= 0.45;
    }

    internal double Recover(double delta)
    {
        _stumble = Math.Max(0, _stumble - delta);
        var t = 1 - _stumble / RecoveryTime;
        return _stumble > 0 ? Math.Sin(Math.PI * t) * _strength : 0;
    }

    internal void ResetRecovery() => _stumble = 0;

    internal void SetColliders(double[] values) => _colliders = values;
    internal void Stop() => _speed = 0;

    internal static (double X, double Y) Stick(double x, double y)
    {
        var magnitude = double.Hypot(x, y);
        if (magnitude < Deadzone) return (0, 0);
        var scale = Math.Min(1, (magnitude - Deadzone) / (1 - Deadzone));
        return (x / magnitude * scale, y / magnitude * scale);
    }

    internal bool CanStep(double minY, double maxY, double ground)
        => maxY > ground && maxY - ground <= kneeHeight && minY <= ground + 0.10;

    internal (double X, double Z, double Yaw, double Distance) Advance(
        double x, double y, double z, double facing, double forwardX, double forwardZ,
        double keyX, double keyZ, double analogX, double analogZ, bool sprint, double delta,
        TerrainSurface surface, TerrainVolumes volumes, bool hasTunnels)
    {
        var inputX = keyX + analogX;
        var inputZ = keyZ + analogZ;
        var magnitude = Math.Min(1, double.Hypot(inputX, inputZ));
        if (magnitude > 0)
        {
            var dx = forwardX * inputZ + forwardZ * inputX;
            var dz = forwardZ * inputZ - forwardX * inputX;
            var length = double.Hypot(dx, dz);
            if (length == 0) length = 1;
            _directionX = dx / length;
            _directionZ = dz / length;
        }
        var running = sprint || double.Hypot(analogX, analogZ) > 0.92;
        var targetSpeed = (running ? RunSpeed : WalkSpeed) * magnitude;
        _speed += Math.Clamp(targetSpeed - _speed, -26 * delta, 22 * delta);
        if (_speed <= 0) return (x, z, double.NaN, 0);

        var ground = hasTunnels ? volumes.Floor(x, z, y, kneeHeight) : surface.Height(x, z);
        var stepping = y - footGap > ground + 0.015;
        var distance = (stepping ? Math.Min(2.8, _speed) : _speed) * delta;
        var (nextX, nextZ) = ResolveMove(x, y, z, _directionX * distance, _directionZ * distance,
            ground, volumes, hasTunnels);
        var actualX = nextX - x;
        var actualZ = nextZ - z;
        var travelled = double.Hypot(actualX, actualZ);
        var yaw = travelled > 0.001 ? Math.Atan2(actualX, actualZ) : Math.Atan2(_directionX, _directionZ);
        var scale = TurnTravelScale(yaw - facing);
        if (scale < 0.999 && travelled > 0)
        {
            // Mede a direção percorrível antes de virar. Revalida o trecho reduzido com os colisores.
            (nextX, nextZ) = ResolveMove(x, y, z, actualX * scale, actualZ * scale, ground, volumes, hasTunnels);
            travelled = double.Hypot(nextX - x, nextZ - z);
        }
        if (characters is not null)
        {
            var permitted = characters.Move(PlayerCharacterSpace.Player, x, y, z, nextX, nextZ);
            (nextX, nextZ) = (permitted.X, permitted.Z);
            if (permitted.Blocked && (nextX != x || nextZ != z))
            {
                // O ponto interrompido pode estar no segmento que contornou um obstáculo.
                (nextX, nextZ) = ResolveMove(x, y, z, nextX - x, nextZ - z, ground, volumes, hasTunnels);
                var validated = characters.Move(PlayerCharacterSpace.Player, x, y, z, nextX, nextZ);
                (nextX, nextZ) = (validated.X, validated.Z);
            }
            var actualDistance = double.Hypot(nextX - x, nextZ - z);
            if (permitted.Blocked && delta > 0 && (travelled - actualDistance) / delta > 1.8) Trip(1);
            travelled = actualDistance;
            characters.Set(PlayerCharacterSpace.Player, nextX, y, nextZ);
        }
        return (nextX, nextZ, yaw, travelled);
    }

    internal static double TurnTravelScale(double difference)
    {
        var alignment = Math.Max(0, Math.Cos(difference));
        var squared = alignment * alignment;
        return squared * squared;
    }

    private (double X, double Z) ResolveMove(double x, double y, double z, double dx, double dz,
        double ground, TerrainVolumes volumes, bool hasTunnels)
    {
        var (nextX, nextZ) = Sweep(x, y, z, dx, dz, ground);
        if (!hasTunnels || PassesVolume(volumes, x, z, nextX, nextZ, y, ground)) return (nextX, nextZ);
        if (PassesVolume(volumes, x, z, nextX, z, y, ground)) return (nextX, z);
        return PassesVolume(volumes, x, z, x, nextZ, y, ground) ? (x, nextZ) : (x, z);
    }

    internal (double X, double Z) Sweep(double x, double y, double z, double dx, double dz, double ground)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            dx = Math.Clamp(x + dx, -WorldHalf, WorldHalf) - x;
            dz = Math.Clamp(z + dz, -WorldHalf, WorldHalf) - z;
            var lengthSquared = dx * dx + dz * dz;
            if (lengthSquared < 1e-10) break;
            var hit = FirstContact(x, y, z, dx, dz, lengthSquared, ground);
            if (hit is not { } contact) return (x + dx, z + dz);

            var t = Math.Max(0, contact.Time - 0.001 / Math.Sqrt(lengthSquared));
            x += dx * t;
            z += dz * t;
            var inward = dx * contact.X + dz * contact.Z;
            dx = (dx - inward * contact.X) * (1 - contact.Time);
            dz = (dz - inward * contact.Z) * (1 - contact.Time);
        }
        return (x, z);
    }

    private (double Time, double X, double Z)? FirstContact(double x, double y, double z,
        double dx, double dz, double lengthSquared, double ground)
    {
        (double Time, double X, double Z)? hit = null;
        var best = 1.0;
        var distance = Math.Sqrt(lengthSquared);
        for (var i = 0; i < _colliders.Length; i += ColliderStride)
        {
            var minY = _colliders[i + 3];
            var maxY = _colliders[i + 4];
            if (!TouchesHeight(y, minY, maxY) || CanStep(minY, maxY, ground)) continue;
            var offsetX = x - _colliders[i];
            var offsetZ = z - _colliders[i + 1];
            var radius = Radius + _colliders[i + 2];
            var reach = distance + radius;
            if (Math.Abs(offsetX) > reach || Math.Abs(offsetZ) > reach) continue;
            var toward = offsetX * dx + offsetZ * dz;
            if (toward >= 0) continue;
            var gap = offsetX * offsetX + offsetZ * offsetZ - radius * radius;
            var discriminant = toward * toward - lengthSquared * gap;
            if (discriminant <= 0) continue;
            var t = gap <= 0 ? 0 : (-toward - Math.Sqrt(discriminant)) / lengthSquared;
            if (t >= best) continue;
            var nx = offsetX + dx * t;
            var nz = offsetZ + dz * t;
            var normalLength = double.Hypot(nx, nz);
            hit = (t, nx / normalLength, nz / normalLength);
            best = t;
        }
        return hit;
    }

    internal int NearbyObstacle(double x, double y, double z)
    {
        var best = 1.15;
        var index = -1;
        for (var i = 0; i < _colliders.Length; i += ColliderStride)
        {
            if (!TouchesHeight(y, _colliders[i + 3], _colliders[i + 4])) continue;
            var dx = x - _colliders[i];
            var dz = z - _colliders[i + 1];
            var radius = _colliders[i + 2];
            if (Math.Abs(dx) > best + radius || Math.Abs(dz) > best + radius) continue;
            var distance = double.Hypot(dx, dz) - radius;
            if (distance >= best) continue;
            best = distance;
            index = (int)_colliders[i + 5];
        }
        return index;
    }

    private bool TouchesHeight(double y, double minY, double maxY) => y + height > minY && y < maxY;

    private bool PassesVolume(TerrainVolumes volumes, double x, double z, double nextX, double nextZ,
        double y, double originalFloor)
    {
        if (!volumes.Near(nextX, nextZ) && !volumes.Near(x, z)) return true;
        var floor = volumes.Floor(nextX, nextZ, y, kneeHeight);
        if (floor - originalFloor > kneeHeight) return false;
        var dx = nextX - x;
        var dz = nextZ - z;
        var length = double.Hypot(dx, dz);
        if (length < 1e-5) return true;
        var head = floor + height - 0.10;
        var sideX = -dz * Radius / length;
        var sideZ = dx * Radius / length;
        return !volumes.Blocks(nextX, head, nextZ, 0.06) &&
            !volumes.Blocks(nextX + sideX, head, nextZ + sideZ, 0.03) &&
            !volumes.Blocks(nextX - sideX, head, nextZ - sideZ, 0.03);
    }
}
