namespace Ultima.Terrain;

internal sealed class PlayerCamera
{
    internal const double DefaultRadius = 7.5;
    private double _orbit = DefaultRadius;
    private double _zoom = DefaultRadius;

    internal void Reset() => _orbit = _zoom = DefaultRadius;

    // Ângulos em radianos, arrasto em pixels CSS e raio em metros.
    internal void Advance(Span<double> output, double alpha, double beta, double radius, double maxZoom,
        double delta, bool frozen, double lookX, double lookY, double dragX, double dragY, double wheel)
    {
        var maxBeta = Math.PI / 2.15;
        if (!frozen)
        {
            alpha -= lookX * delta * 2.2;
            beta = Math.Clamp(beta + lookY * delta * 1.7, 0.18, maxBeta);
        }
        if (dragX != 0 || dragY != 0)
        {
            alpha -= dragX * 0.01;
            beta = Math.Clamp(beta + dragY * 0.01, 0.18, maxBeta);
        }
        if (wheel != 0)
        {
            var notches = -wheel / 120;
            _zoom *= notches > 0 ? Math.Pow(0.9015, notches) : Math.Pow(1.1212, -notches);
            _zoom = Math.Clamp(_zoom, 2.4, maxZoom);
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
        output[6] = Math.Abs(_orbit - radius) <= 1e-6
            ? _orbit
            : radius + (_orbit - radius) * (1 - Math.Exp(-12 * delta));
        output[7] = output[6] == _orbit && _orbit == _zoom && beta >= 0.18 && beta <= maxBeta ? 1 : 0;
    }
}
