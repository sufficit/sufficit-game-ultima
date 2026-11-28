namespace Ultima.Terrain;

internal sealed partial class PlayerMovement
{
    private double _minX = -400, _maxX = 400, _minZ = -400, _maxZ = 400;
    private Box[] _solidBoxes = [];

    internal void SetSceneBounds(ReadOnlySpan<double> bounds)
    {
        if (bounds.Length != 4) return;
        (_minX, _maxX, _minZ, _maxZ) =
            (bounds[0] + _radius, bounds[1] - _radius, bounds[2] + _radius, bounds[3] - _radius);
    }

    internal (double X, double Z) ClampPosition(double x, double z) =>
        (Math.Clamp(x, _minX, _maxX), Math.Clamp(z, _minZ, _maxZ));

    internal void SetSolidBoxes(ReadOnlySpan<double> values)
    {
        _solidBoxes = new Box[values.Length / 6];
        for (var i = 0; i < _solidBoxes.Length; i++)
        {
            var offset = i * 6;
            _solidBoxes[i] = new Box(values[offset], values[offset + 1], values[offset + 2],
                values[offset + 3], values[offset + 4], values[offset + 5]);
        }
    }

    private (double Time, double X, double Z)? FirstBoxContact(
        double x, double y, double z, double dx, double dz, double ground, double best)
    {
        (double Time, double X, double Z)? hit = null;
        foreach (var box in _solidBoxes)
        {
            if (!TouchesHeight(y, box.MinY, box.MaxY) || CanStep(box.MinY, box.MaxY, ground)) continue;
            var (minX, maxX, minZ, maxZ) =
                (box.MinX - _radius, box.MaxX + _radius, box.MinZ - _radius, box.MaxZ + _radius);
            if (!ClipAxis(x, dx, minX, maxX, out var enterX, out var exitX) ||
                !ClipAxis(z, dz, minZ, maxZ, out var enterZ, out var exitZ)) continue;

            var enter = Math.Max(enterX, enterZ);
            if (enter >= best || enter < 0 || enter >= Math.Min(exitX, exitZ)) continue;

            best = enter;
            hit = enterX >= enterZ ? (enter, -Math.Sign(dx), 0) : (enter, 0, -Math.Sign(dz));
        }
        return hit;
    }

    private static bool ClipAxis(double position, double delta, double min, double max,
        out double enter, out double exit)
    {
        if (Math.Abs(delta) < 1e-9)
        {
            enter = double.NegativeInfinity;
            exit = double.PositiveInfinity;
            return position > min && position < max;
        }

        var first = (min - position) / delta;
        var second = (max - position) / delta;
        enter = Math.Min(first, second);
        exit = Math.Max(first, second);
        return true;
    }

    private readonly record struct Box(double MinX, double MaxX, double MinY, double MaxY, double MinZ, double MaxZ);

    internal double JumpCeiling(double x, double z, double support)
    {
        var ceiling = double.PositiveInfinity;
        foreach (var box in _solidBoxes)
        {
            if (box.MinY > support + .15 && x + _radius > box.MinX && x - _radius < box.MaxX &&
                z + _radius > box.MinZ && z - _radius < box.MaxZ)
                ceiling = Math.Min(ceiling, box.MinY - _standingHeight);
        }
        return ceiling;
    }
}
