namespace Ultima.Terrain;

// The sole keeps its world position and orientation during support.
internal sealed partial class PlayerFootContacts(double strideScale = 1, double widthScale = 1)
{
    internal const double StrideLength = 1.16;
    private const double ResumeDuration = 0.18;
    internal double Stride => StrideLength * strideScale * StrideMultiplier;
    internal double AimedStride => (StrideLength - .54 * Math.Abs(_travelLateral) + .26 * Math.Min(0, _travelForward)) * strideScale * StrideMultiplier;
    internal double StrideMultiplier { get; set; } = 1;

    private readonly Contact[] _feet = new Contact[2];
    private bool _ready, _moving, _aimed;
    private double _travelForward, _travelLateral;
    private double _x, _z, _yaw, _airborneBlend;

    internal void Reset()
    {
        _ready = false;
        _resting = _pivoting = false;
    }

    internal double Begin(double x, double z, double yaw, double distance, bool aimed = false)
    {
        var dx = x - _x;
        var dz = z - _z;
        var translation = double.Hypot(dx, dz);
        if (translation > distance + 0.02) Reset();
        var turned = _ready && !aimed ? Math.Abs(Math.IEEERemainder(yaw - _yaw, Math.Tau)) : 0;
        (_x, _z, _yaw, _aimed) = (x, z, yaw, aimed);
        (_travelForward, _travelLateral) = (0, 0);
        if (aimed && _ready && translation > 0.00001)
        {
            var (sin, cos) = Math.SinCos(yaw);
            _travelForward = (dx * sin + dz * cos) / translation;
            _travelLateral = (dx * cos - dz * sin) / translation;
        }
        var travelled = Math.Max(distance, turned * 0.28);
        _moving = travelled > 0.00001;
        return travelled;
    }

    // feet mantém os canais da marcha. extra contém deslocamento lateral e giro de cada sola.
    internal bool Apply(Span<double> feet, Span<double> extra, double phase, double spacing, double shoeOffset,
        double delta = 0, bool softSwing = false)
    {
        var (sin, cos) = Math.SinCos(_yaw);
        var resuming = _resting || _pivoting;
        _pivoting = false;
        if (!_moving)
        {
            ApplyStanding(feet, extra, spacing, shoeOffset, sin, cos, delta);
            return false;
        }

        _resting = false;
        if (resuming) _airborneBlend = 0;
        else if (!_ready) _airborneBlend = 1;
        _airborneBlend = Math.Min(1, _airborneBlend + delta / ResumeDuration);
        var gaitBlend = Smooth(_airborneBlend);
        for (var i = 0; i < 2; i++)
        {
            ref var contact = ref _feet[i];
            var offset = 4 * i;
            var side = i == 0 ? -spacing : spacing;
            var cycle = (phase / Math.PI + 0.5 + i) % 2;
            var swing = cycle > 1;
            var (x, z) = _aimed ? AimStep(side, shoeOffset, feet[offset], sin, cos)
                : (_x + feet[offset + 2], _z + feet[offset + 3]);
            var yaw = _yaw;
            if (!_ready || resuming) contact.RecoveryLift = 0;
            if (resuming)
            {
                (contact.X, contact.Z, contact.Yaw) = (contact.Last.X, contact.Last.Z, contact.Last.Yaw);
                contact.ResumeLift = contact.Last.Lift;
                contact.ResumePitch = contact.Last.Pitch;
            }
            else if (!_ready || !swing && contact.Swing)
                (contact.X, contact.Z, contact.Yaw) = (x, z, yaw);

            if (swing)
            {
                var swingPhase = cycle - 1;
                if (!_ready || resuming) contact.SwingStart = swingPhase;
                else if (!contact.Swing) contact.SwingStart = 0;
                var progress = (swingPhase - contact.SwingStart) / (1 - contact.SwingStart);
                var blend = SwingBlend(progress, softSwing);
                // Aterrissagem na ponta do próximo apoio, adiantada pela distância que ainda falta.
                var lead = (_aimed ? AimedStride : Stride) * (0.25 + 0.5 * (1 - swingPhase));
                var (landingX, landingZ) = _aimed ? AimStep(side, shoeOffset, lead, sin, cos)
                    : (_x + side * cos + (shoeOffset + lead) * sin, _z - side * sin + (shoeOffset + lead) * cos);
                x = contact.X + (landingX - contact.X) * blend;
                z = contact.Z + (landingZ - contact.Z) * blend;
                yaw = contact.Yaw + Math.IEEERemainder(yaw - contact.Yaw, Math.Tau) * blend;
            }
            else
            {
                (x, z, yaw) = (contact.X, contact.Z, contact.Yaw);
            }
            contact.Swing = swing;

            contact.RecoveryLift = Math.Max(0, contact.RecoveryLift - delta * 0.6);
            var lift = feet[offset + 1] * gaitBlend + contact.ResumeLift * (1 - gaitBlend);
            var pitch = feet[8 + i] * gaitBlend * (_aimed ? _travelForward : 1)
                + contact.ResumePitch * (1 - gaitBlend);
            if (contact.RecoveryLift > lift)
            {
                lift = contact.RecoveryLift;
                pitch = 1.5 * lift;
            }
            WriteFoot(feet, extra, i, side, shoeOffset, sin, cos, new(x, z, yaw, lift, pitch));
        }
        _ready = true;
        var crossed = KeepFeetApart(feet, extra, spacing, sin, cos, _feet[0].Swing ? 0 : 1);
        RememberFeet(feet, extra);
        return crossed;
    }

    private (double X, double Z) AimStep(double side, double shoeOffset, double lead, double sin, double cos)
    {
        var forward = shoeOffset + lead * _travelForward;
        var lateral = lead * _travelLateral;
        // Each foot keeps its lane. Lead with the outward foot in a sidestep.
        var width = side + Math.CopySign(Math.Clamp(lateral * Math.Sign(side), 0, .20), side);
        return (_x + width * cos + forward * sin, _z - width * sin + forward * cos);
    }

    private void WriteFoot(Span<double> feet, Span<double> extra, int foot, double side,
        double shoeOffset, double sin, double cos, Foot pose)
    {
        var offset = 4 * foot;
        var (footSin, footCos) = Math.SinCos(pose.Yaw);
        var dx = pose.X - _x;
        var dz = pose.Z - _z;
        var ankleX = dx - shoeOffset * footSin;
        var ankleZ = dz - shoeOffset * footCos;
        feet[offset] = ankleX * sin + ankleZ * cos;
        feet[offset + 1] = pose.Lift;
        feet[offset + 2] = dx;
        feet[offset + 3] = dz;
        feet[8 + foot] = pose.Pitch;
        extra[foot] = ankleX * cos - ankleZ * sin - side;
        extra[2 + foot] = Math.IEEERemainder(pose.Yaw - _yaw, Math.Tau);
    }

    private void RememberFeet(Span<double> feet, Span<double> extra)
    {
        _feet[0].Last = new(_x + feet[2], _z + feet[3], _yaw + extra[2], feet[1], feet[8]);
        _feet[1].Last = new(_x + feet[6], _z + feet[7], _yaw + extra[3], feet[5], feet[9]);
    }

    private bool KeepFeetApart(Span<double> feet, Span<double> extra, double spacing,
        double sin, double cos, int foot)
    {
        var ankleGap = 2 * spacing + extra[1] - extra[0];
        var shoeGap = (feet[6] - feet[2]) * cos - (feet[7] - feet[3]) * sin;
        var correction = Math.Max(0.14 * widthScale - ankleGap, (ShoeWidth(extra[2]) + ShoeWidth(extra[3])) * widthScale - shoeGap);
        if (correction <= 0) return false;

        // Preserva a sola em apoio e desvia a que estiver avançando.
        var lateral = foot == 0 ? -correction : correction;
        var offset = 4 * foot;
        var dx = lateral * cos;
        var dz = -lateral * sin;
        feet[offset + 2] += dx;
        feet[offset + 3] += dz;
        extra[foot] += lateral;
        ref var contact = ref _feet[foot];
        if (_moving && !_pivoting && !contact.Swing)
        {
            contact.X += dx;
            contact.Z += dz;
        }
        if (_moving && !_pivoting && correction > 0.012)
        {
            const double lift = 0.085;
            if (!contact.Swing) contact.RecoveryLift = lift;
            feet[offset + 1] = Math.Max(feet[offset + 1], lift);
            if (feet[8 + foot] == 0) feet[8 + foot] = 1.5 * lift;
        }
        return _moving && correction > 0.025;
    }

    // Projeção lateral da sola elipsoidal.
    private static double ShoeWidth(double relativeYaw)
    {
        var (sin, cos) = Math.SinCos(relativeYaw);
        return double.Hypot(0.086 * cos, 0.163 * sin);
    }

    private static double SwingBlend(double progress, bool softSwing)
    {
        var takeoff = softSwing ? .12 : .05;
        var touchdown = softSwing ? .22 : .15;
        var area = 1 - (takeoff + touchdown) / 2;
        if (progress < takeoff) return progress * progress / (2 * takeoff * area);
        if (progress <= 1 - touchdown) return (progress - takeoff / 2) / area;
        var remaining = 1 - progress;
        return 1 - remaining * remaining / (2 * touchdown * area);
    }

    private readonly record struct Foot(double X, double Z, double Yaw, double Lift, double Pitch);

    private struct Contact
    {
        internal double X, Z, Yaw, SwingStart, RecoveryLift, ResumeLift, ResumePitch;
        internal Foot Last, RestStart;
        internal bool Swing;
    }
}
