namespace Ultima.Terrain;

// A sola mantém posição e orientação no mundo durante o apoio.
internal sealed class PlayerFootContacts
{
    internal const double StrideLength = 1.16;

    private readonly Contact[] _feet = new Contact[2];
    private bool _ready;
    private double _x, _z, _yaw;

    internal void Reset() => _ready = false;

    internal double Begin(double x, double z, double yaw, double distance)
    {
        if (double.Hypot(x - _x, z - _z) > distance + 0.02) Reset();
        var turned = _ready ? Math.Abs(Math.IEEERemainder(yaw - _yaw, Math.Tau)) : 0;
        (_x, _z, _yaw) = (x, z, yaw);
        return Math.Max(distance, turned * 0.28);
    }

    // feet mantém os canais da marcha. extra contém deslocamento lateral e giro de cada sola.
    internal void Apply(Span<double> feet, Span<double> extra, double phase, double spacing, double shoeOffset)
    {
        var (sin, cos) = Math.SinCos(_yaw);
        for (var i = 0; i < 2; i++)
        {
            ref var contact = ref _feet[i];
            var offset = 4 * i;
            var side = i == 0 ? -spacing : spacing;
            var cycle = (phase / Math.PI + 0.5 + i) % 2;
            var swing = cycle > 1;
            var x = _x + feet[offset + 2];
            var z = _z + feet[offset + 3];
            var yaw = _yaw;
            if (!_ready || !swing && contact.Swing)
                (contact.X, contact.Z, contact.Yaw) = (x, z, yaw);

            if (swing)
            {
                var swingPhase = cycle - 1;
                if (!_ready) contact.SwingStart = swingPhase;
                else if (!contact.Swing) contact.SwingStart = 0;
                var progress = (swingPhase - contact.SwingStart) / (1 - contact.SwingStart);
                var blend = SwingBlend(progress);
                // Aterrissagem na ponta do próximo apoio, adiantada pela distância que ainda falta.
                var landing = shoeOffset + StrideLength * (0.25 + 0.5 * (1 - swingPhase));
                var landingX = _x + side * cos + landing * sin;
                var landingZ = _z - side * sin + landing * cos;
                x = contact.X + (landingX - contact.X) * blend;
                z = contact.Z + (landingZ - contact.Z) * blend;
                yaw = contact.Yaw + Math.IEEERemainder(yaw - contact.Yaw, Math.Tau) * blend;
            }
            else
            {
                (x, z, yaw) = (contact.X, contact.Z, contact.Yaw);
            }
            contact.Swing = swing;

            var (footSin, footCos) = Math.SinCos(yaw);
            var dx = x - shoeOffset * footSin - _x;
            var dz = z - shoeOffset * footCos - _z;
            feet[offset] = dx * sin + dz * cos;
            feet[offset + 2] = x - _x;
            feet[offset + 3] = z - _z;
            extra[i] = dx * cos - dz * sin - side;
            extra[2 + i] = Math.IEEERemainder(yaw - _yaw, Math.Tau);
        }
        _ready = true;
    }

    private static double SwingBlend(double progress)
    {
        const double takeoff = 0.05, touchdown = 0.15;
        const double area = 1 - (takeoff + touchdown) / 2;
        if (progress < takeoff) return progress * progress / (2 * takeoff * area);
        if (progress <= 1 - touchdown) return (progress - takeoff / 2) / area;
        var remaining = 1 - progress;
        return 1 - remaining * remaining / (2 * touchdown * area);
    }

    private struct Contact
    {
        internal double X, Z, Yaw, SwingStart;
        internal bool Swing;
    }
}
