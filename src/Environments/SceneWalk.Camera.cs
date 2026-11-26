using Ultima.Terrain;

namespace Ultima.Environments;

internal sealed partial class SceneWalk
{
    private const double TargetHeight = 1.18;
    private const double CameraClearance = .10;
    private PlayerCameraView _view;
    private PlayerCloseCamera _closeLook = new(0);

    internal int CycleCameraView()
    {
        _view = PlayerCameraViews.Next(_view);
        if (_view == PlayerCameraView.Close) _closeLook.Align(_heading);
        return (int)_view;
    }

    private void Follow(Span<double> result, double sin, double cos)
    {
        if (_view == PlayerCameraView.FirstPerson)
        {
            var horizontal = Math.Cos(_pitch);
            (result[3], result[4], result[5]) = (_x, PlayerCameraViews.EyeHeight, _z);
            (result[6], result[7], result[8]) = (_x + sin * horizontal,
                PlayerCameraViews.EyeHeight - Math.Sin(_pitch), _z + cos * horizontal);
            result[9] = (int)_view;
            return;
        }

        var close = _view == PlayerCameraView.Close;
        var targetHeight = close ? PlayerCloseCamera.TargetHeight : TargetHeight;
        var (dx, dy, dz) = close ? _closeLook.Offset : (-sin * 2.8, 1.04 - 1.65 * Math.Sin(_pitch), -cos * 2.8);
        var fraction = CameraFraction(targetHeight, dx, dy, dz);

        if (!close && fraction < .34)
        {
            // In a tight corner, look from a higher angle so the avatar stays framed.
            var overhead = CameraFraction(targetHeight, dx * .22, 1.42, dz * .22);
            if (overhead * 1.42 > fraction * double.Hypot(2.8, dy))
            {
                dx *= .22;
                dy = 1.42;
                dz *= .22;
                fraction = overhead;
            }
        }

        (result[3], result[4], result[5]) =
            (_x + dx * fraction, targetHeight + dy * fraction, _z + dz * fraction);
        (result[6], result[7], result[8]) = (_x, targetHeight, _z);
        result[9] = (int)_view;
    }

    private double CameraFraction(double targetHeight, double dx, double dy, double dz)
    {
        var fraction = 1d;
        foreach (var solid in _solids)
        {
            var start = 0d;
            var end = fraction;
            if (Clip(_x, dx, solid.MinX, solid.MaxX, ref start, ref end) &&
                Clip(targetHeight, dy, solid.MinY, solid.MaxY, ref start, ref end) &&
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
