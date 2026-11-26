namespace Ultima.Terrain;

internal sealed partial class PlayerFootContacts
{
    private const double RestStepDuration = 0.22;
    private bool _resting, _turningInPlace;
    private int _restFirst;
    private double _restTime, _restYaw;

    private void ApplyStanding(Span<double> feet, Span<double> extra, double spacing, double shoeOffset,
        double sin, double cos, double delta)
    {
        var turn = Math.IEEERemainder(_yaw - _restYaw, Math.Tau);
        var turnStep = _aimed && _resting && _restTime >= 2 * RestStepDuration && Math.Abs(turn) > .20;
        if (!_resting || turnStep)
        {
            _resting = true;
            _turningInPlace = turnStep;
            _restYaw = turnStep ? _restYaw + Math.Clamp(turn, -Math.PI / 2, Math.PI / 2) : _yaw;
            _restFirst = _feet[0].Last.Lift > _feet[1].Last.Lift ? 0 : 1;
            _restTime = _ready ? 0 : 2 * RestStepDuration;
            for (var i = 0; i < 2; i++) _feet[i].RestStart = _feet[i].Last;
        }

        _restTime = Math.Min(2 * RestStepDuration, _restTime + delta);
        var (stanceSin, stanceCos) = _aimed ? Math.SinCos(_restYaw) : (sin, cos);
        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? -spacing : spacing;
            var neutral = new Foot(_x + side * stanceCos + shoeOffset * stanceSin,
                _z - side * stanceSin + shoeOffset * stanceCos, _aimed ? _restYaw : _yaw, 0, 0);
            var elapsed = i == _restFirst ? _restTime : _restTime - RestStepDuration;
            var progress = Math.Clamp(elapsed / RestStepDuration, 0, 1);
            var origin = _feet[i].RestStart;
            var pose = progress >= 1 ? neutral
                : _aimed && _turningInPlace ? RepositionOnArc(origin, neutral, progress) : Reposition(origin, neutral, progress);
            WriteFoot(feet, extra, i, side, shoeOffset, sin, cos, pose);
        }

        if (!_aimed && _restTime < 2 * RestStepDuration)
            KeepFeetApart(feet, extra, spacing, sin, cos, _restTime <= RestStepDuration ? _restFirst : 1 - _restFirst);
        _ready = true;
        RememberFeet(feet, extra);
    }

    private Foot RepositionOnArc(Foot start, Foot end, double progress)
    {
        var pose = Reposition(start, end, progress);
        var blend = Smooth(progress);
        var (sx, sz, ex, ez) = (start.X - _x, start.Z - _z, end.X - _x, end.Z - _z);
        var startAngle = Math.Atan2(sx, sz);
        var angle = startAngle + Math.IEEERemainder(Math.Atan2(ex, ez) - startAngle, Math.Tau) * blend;
        var startRadius = double.Hypot(sx, sz);
        var radius = startRadius + (double.Hypot(ex, ez) - startRadius) * blend;
        var (sin, cos) = Math.SinCos(angle);
        return pose with { X = _x + radius * sin, Z = _z + radius * cos };
    }

    private static Foot Reposition(Foot start, Foot end, double progress)
    {
        var blend = Smooth(progress);
        var yawDelta = Math.IEEERemainder(end.Yaw - start.Yaw, Math.Tau);
        var travel = double.Hypot(end.X - start.X, end.Z - start.Z) + Math.Abs(yawDelta) * 0.05;
        var clearance = Math.Min(0.075, travel * 0.34);
        var lift = start.Lift * (1 - blend) + clearance * 4 * progress * (1 - progress);
        return new(start.X + (end.X - start.X) * blend, start.Z + (end.Z - start.Z) * blend,
            start.Yaw + yawDelta * blend, lift, start.Pitch * (1 - blend));
    }

    private static double Smooth(double t) => t * t * (3 - 2 * t);
}
