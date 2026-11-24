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
    internal bool Apply(Span<double> feet, Span<double> extra, double phase, double spacing, double shoeOffset, double delta = 0)
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
            if (!_ready) contact.RecoveryLift = 0;
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
            var localX = dx * cos - dz * sin;
            var yawDifference = Math.IEEERemainder(yaw - _yaw, Math.Tau);
            contact.RecoveryLift = Math.Max(0, contact.RecoveryLift - delta * 0.6);
            if (contact.RecoveryLift > feet[offset + 1])
            {
                feet[offset + 1] = contact.RecoveryLift;
                feet[8 + i] = 1.5 * contact.RecoveryLift;
            }
            feet[offset] = dx * sin + dz * cos;
            feet[offset + 2] = x - _x;
            feet[offset + 3] = z - _z;
            extra[i] = localX - side;
            extra[2 + i] = yawDifference;
        }
        _ready = true;
        return KeepFeetApart(feet, extra, spacing, sin, cos);
    }

    private bool KeepFeetApart(Span<double> feet, Span<double> extra, double spacing, double sin, double cos)
    {
        var ankleGap = 2 * spacing + extra[1] - extra[0];
        var shoeGap = (feet[6] - feet[2]) * cos - (feet[7] - feet[3]) * sin;
        var correction = Math.Max(0.14 - ankleGap, ShoeWidth(extra[2]) + ShoeWidth(extra[3]) - shoeGap);
        if (correction <= 0) return false;

        // Preserva a sola em apoio e desvia a que estiver avançando.
        var foot = _feet[0].Swing ? 0 : 1;
        var lateral = foot == 0 ? -correction : correction;
        var offset = 4 * foot;
        var dx = lateral * cos;
        var dz = -lateral * sin;
        feet[offset + 2] += dx;
        feet[offset + 3] += dz;
        extra[foot] += lateral;
        ref var contact = ref _feet[foot];
        if (!contact.Swing)
        {
            contact.X += dx;
            contact.Z += dz;
        }
        if (correction > 0.012)
        {
            const double lift = 0.085;
            if (!contact.Swing) contact.RecoveryLift = lift;
            feet[offset + 1] = Math.Max(feet[offset + 1], lift);
            if (feet[8 + foot] == 0) feet[8 + foot] = 1.5 * lift;
        }
        return correction > 0.025;
    }

    // Projeção lateral da sola elipsoidal.
    private static double ShoeWidth(double relativeYaw)
    {
        var (sin, cos) = Math.SinCos(relativeYaw);
        return double.Hypot(0.086 * cos, 0.163 * sin);
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
        internal double X, Z, Yaw, SwingStart, RecoveryLift;
        internal bool Swing;
    }
}
