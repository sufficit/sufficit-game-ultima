namespace Ultima.Terrain;

// A sola mantém posição e orientação no mundo durante o apoio.
internal sealed class PlayerFootContacts
{
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
                var t = Math.Min(1, (cycle - 1) / 0.45);
                var blend = t * t * (3 - 2 * t);
                x = contact.X + (x - contact.X) * blend;
                z = contact.Z + (z - contact.Z) * blend;
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

    private struct Contact
    {
        internal double X, Z, Yaw;
        internal bool Swing;
    }
}
