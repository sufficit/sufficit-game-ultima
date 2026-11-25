using Ultima.Terrain;

namespace Ultima.Environments;

internal sealed partial class SceneWalk(SceneWalkSettings settings, SceneBox[] boxes)
{
    private const double Radius = .26;
    private const double Height = 1.75;
    private const double Speed = 3.1;
    private const double SprintSpeed = 4.8;

    private readonly Obstacle[] _solids = boxes.Where(box => box.Solid).Select(box => new Obstacle(
        box.X - box.Width / 2, box.X + box.Width / 2, box.Y - box.Height / 2, box.Y + box.Height / 2,
        box.Z - box.Depth / 2, box.Z + box.Depth / 2)).ToArray();

    private double _x = settings.X, _z = settings.Z, _yaw = settings.Yaw, _pitch;
    private double _heading = settings.Yaw, _phase, _weight;
    private readonly double[] _gait = new double[12];

    internal void Advance(Span<double> result, double side, double forward, bool sprint,
        double yawDelta, double pitchDelta, double seconds)
    {
        _yaw = Math.IEEERemainder(_yaw + yawDelta, Math.Tau);
        _pitch = Math.Clamp(_pitch + pitchDelta, -.66, .63);
        var scale = Math.Max(1, double.Hypot(side, forward));
        var delta = Math.Clamp(seconds, 0, .05);
        var distance = (sprint ? SprintSpeed : Speed) * delta / scale;
        var (sin, cos) = Math.SinCos(_yaw);
        var dx = (side * cos + forward * sin) * distance;
        var dz = (forward * cos - side * sin) * distance;

        var x = Math.Clamp(_x + dx, settings.MinX + Radius, settings.MaxX - Radius);
        var z = Math.Clamp(_z + dz, settings.MinZ + Radius, settings.MaxZ - Radius);
        var oldX = _x;
        var oldZ = _z;
        if (!Blocked(x, _z)) _x = x;
        if (!Blocked(_x, z)) _z = z;

        var travelled = double.Hypot(_x - oldX, _z - oldZ);
        if (travelled > .00001)
        {
            var desired = Math.Atan2(_x - oldX, _z - oldZ);
            _heading += Math.IEEERemainder(desired - _heading, Math.Tau) * (1 - Math.Exp(-18 * delta));
        }

        PlayerPose.Animate(_gait, _phase, _weight, 0, travelled, delta, 0, false, false, 0, 0, 0, playerGait: true);
        (_phase, _weight) = (_gait[0], _gait[1]);
        (result[0], result[1], result[2]) = (_x, _z, _heading);
        (result[3], result[4], result[5]) = (Math.Sin(_phase) * _weight * .32, _gait[10], _gait[11]);
        Follow(result, sin, cos);
    }

    internal void StopAnimation() => _weight = 0;

    private bool Blocked(double x, double z)
    {
        foreach (var wall in _solids)
            if (wall.MaxY > .06 && wall.MinY < Height && x > wall.MinX - Radius && x < wall.MaxX + Radius &&
                z > wall.MinZ - Radius && z < wall.MaxZ + Radius) return true;
        return false;
    }

    private readonly record struct Obstacle(double MinX, double MaxX, double MinY, double MaxY, double MinZ, double MaxZ);
}
