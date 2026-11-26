namespace Ultima.Terrain;

internal sealed class PlayerCamera
{
    internal const double DefaultRadius = 7.5;
    private double _orbit = DefaultRadius;
    private double _zoom = DefaultRadius;

    private const double MaxVerticalLag = 0.24;
    private const double FollowRate = 8;
    private double _floor = double.NaN;
    private double _firstPersonPitch;
    private PlayerCloseCamera _closeLook = new(0);

    internal double AimYaw => View == PlayerCameraView.Close ? _closeLook.Yaw : double.NaN;

    internal PlayerCameraView View { get; private set; }

    internal PlayerCameraView Cycle(double facing)
    {
        View = PlayerCameraViews.Next(View);
        if (View == PlayerCameraView.Close) _closeLook.Align(facing);
        return View;
    }

    internal void Reset(double facing = 0)
    {
        _orbit = _zoom = DefaultRadius;
        _closeLook = new(facing);
        ResetFollow();
    }

    internal void ResetFollow() => _floor = double.NaN;

    // Angles use radians, pointer movement uses CSS pixels, and radius uses meters.
    internal void Advance(Span<double> output, double alpha, double beta, double radius, double maxZoom,
        double delta, bool frozen, double lookX, double lookY, double dragX, double dragY, double wheel, double floor)
    {
        var maxBeta = Math.PI / 2.15;
        if (View == PlayerCameraView.Close)
        {
            _closeLook.Advance(frozen ? 0 : lookX * delta * 2.2, frozen ? 0 : lookY * delta * 1.7,
                frozen ? 0 : dragX, frozen ? 0 : dragY, frozen ? 0 : wheel, delta);
        }
        else
        {
            if (!frozen)
            {
                alpha -= lookX * delta * 2.2;
                if (View == PlayerCameraView.FirstPerson)
                    _firstPersonPitch = Math.Clamp(_firstPersonPitch + lookY * delta * 1.7, -.95, .95);
                else beta = Math.Clamp(beta + lookY * delta * 1.7, 0.18, maxBeta);
            }
            if (dragX != 0 || dragY != 0)
            {
                alpha -= dragX * 0.01;
                if (View == PlayerCameraView.FirstPerson)
                    _firstPersonPitch = Math.Clamp(_firstPersonPitch + dragY * .006, -.95, .95);
                else beta = Math.Clamp(beta + dragY * 0.01, 0.18, maxBeta);
            }
            if (wheel != 0 && View != PlayerCameraView.FirstPerson)
            {
                var notches = -wheel / 120;
                _zoom *= notches > 0 ? Math.Pow(0.9015, notches) : Math.Pow(1.1212, -notches);
                _zoom = Math.Clamp(_zoom, 2.4, maxZoom);
            }
        }

        if (Math.Abs(_zoom - _orbit) <= 1e-6) _orbit = _zoom;
        else _orbit += (_zoom - _orbit) * (1 - Math.Exp(-14 * delta));
        var (sinBeta, cosBeta) = Math.SinCos(beta);
        var (sinAlpha, cosAlpha) = Math.SinCos(alpha);
        var horizontal = _orbit * sinBeta;
        output[0] = alpha;
        output[1] = beta;
        output[2] = _zoom;
        output[3] = horizontal * cosAlpha;
        output[4] = _orbit * cosBeta;
        output[5] = horizontal * sinAlpha;
        if (View == PlayerCameraView.Close)
            (output[3], output[4], output[5]) = _closeLook.Offset;
        output[6] = Math.Abs(_orbit - radius) <= 1e-6
            ? _orbit
            : radius + (_orbit - radius) * (1 - Math.Exp(-12 * delta));
        var difference = floor - _floor;
        _floor = !double.IsFinite(_floor) || Math.Abs(difference) < 0.0001
            ? floor
            : floor - Math.Clamp(difference * Math.Exp(-FollowRate * delta), -MaxVerticalLag, MaxVerticalLag);
        output[8] = _floor;
        if (output.Length > 9) output[9] = _firstPersonPitch;
        if (output.Length > 10) output[10] = AimYaw;
        if (output.Length > 11) output[11] = PlayerCloseCamera.TargetHeight;
        output[7] = (View != PlayerCameraView.Close || _closeLook.Settled) && _floor == floor && output[6] == _orbit && _orbit == _zoom && beta >= 0.18 && beta <= maxBeta ? 1 : 0;
    }
}
