namespace Ultima.Environments;

internal sealed partial class SceneWalk
{
    private const double TargetHeight = 1.18;
    private const double CameraClearance = .10;

    private void Follow(Span<double> result, double sin, double cos)
    {
        var dx = -sin * 2.8;
        var dy = 1.04 - 1.65 * Math.Sin(_pitch);
        var dz = -cos * 2.8;
        var fraction = CameraFraction(dx, dy, dz);

        if (fraction < .34)
        {
            // In a tight corner, look from a higher angle so the avatar stays framed.
            var overhead = CameraFraction(dx * .22, 1.42, dz * .22);
            if (overhead * 1.42 > fraction * double.Hypot(2.8, dy))
            {
                dx *= .22;
                dy = 1.42;
                dz *= .22;
                fraction = overhead;
            }
        }

        (result[6], result[7], result[8], result[9]) =
            (_x + dx * fraction, TargetHeight + dy * fraction, _z + dz * fraction, TargetHeight);
    }

    private double CameraFraction(double dx, double dy, double dz)
    {
        var fraction = 1d;
        foreach (var solid in _solids)
        {
            var start = 0d;
            var end = fraction;
            if (Clip(_x, dx, solid.MinX, solid.MaxX, ref start, ref end) &&
                Clip(TargetHeight, dy, solid.MinY, solid.MaxY, ref start, ref end) &&
                Clip(_z, dz, solid.MinZ, solid.MaxZ, ref start, ref end))
                fraction = start;
        }
        return fraction;
    }

    private static bool Clip(double start, double delta, double min, double max, ref double enter, ref double exit)
    {
        min -= CameraClearance;
        max += CameraClearance;
        if (Math.Abs(delta) < .00001) return start >= min && start <= max;
        var a = (min - start) / delta;
        var b = (max - start) / delta;
        enter = Math.Max(enter, Math.Min(a, b));
        exit = Math.Min(exit, Math.Max(a, b));
        return enter <= exit;
    }
}
