namespace Ultima.Environments;

internal sealed class SceneWalk(SceneWalkSettings settings, SceneBox[] boxes)
{
    internal const double EyeHeight = 1.62;
    private const double Radius = .26;
    private const double Speed = 3.1;
    private const double SprintSpeed = 4.8;

    private readonly Obstacle[] _solids = boxes.Where(box => box.Solid && box.Y + box.Height / 2 > .06 &&
        box.Y - box.Height / 2 < EyeHeight + .12).Select(box => new Obstacle(
        box.X - box.Width / 2 - Radius, box.X + box.Width / 2 + Radius,
        box.Z - box.Depth / 2 - Radius, box.Z + box.Depth / 2 + Radius)).ToArray();

    private double _x = settings.X, _z = settings.Z, _yaw = settings.Yaw, _pitch;

    internal void Advance(Span<double> result, double side, double forward, bool sprint,
        double yawDelta, double pitchDelta, double seconds)
    {
        _yaw = Math.IEEERemainder(_yaw + yawDelta, Math.Tau);
        _pitch = Math.Clamp(_pitch + pitchDelta, -1.05, 1.05);
        var scale = Math.Max(1, double.Hypot(side, forward));
        var distance = (sprint ? SprintSpeed : Speed) * Math.Clamp(seconds, 0, .05) / scale;
        var (sin, cos) = Math.SinCos(_yaw);
        var dx = (side * cos + forward * sin) * distance;
        var dz = (forward * cos - side * sin) * distance;

        var x = Math.Clamp(_x + dx, settings.MinX + Radius, settings.MaxX - Radius);
        var z = Math.Clamp(_z + dz, settings.MinZ + Radius, settings.MaxZ - Radius);
        if (!Blocked(x, _z)) _x = x;
        if (!Blocked(_x, z)) _z = z;

        (result[0], result[1], result[2], result[3]) = (_x, _z, _yaw, _pitch);
    }

    private bool Blocked(double x, double z)
    {
        foreach (var wall in _solids)
            if (x > wall.MinX && x < wall.MaxX && z > wall.MinZ && z < wall.MaxZ) return true;
        return false;
    }

    private readonly record struct Obstacle(double MinX, double MaxX, double MinZ, double MaxZ);
}
