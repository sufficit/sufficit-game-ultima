namespace Ultima.Terrain;

internal sealed partial class PlayerFootContacts
{
    private const double RestStepDuration = 0.22;
    private bool _resting;
    private int _restFirst;
    private double _restTime;

    private void ApplyStanding(Span<double> feet, Span<double> extra, double spacing, double shoeOffset,
        double sin, double cos, double delta)
    {
        if (!_resting)
        {
            _resting = true;
            _restFirst = _feet[0].Last.Lift > _feet[1].Last.Lift ? 0 : 1;
            _restTime = _ready ? 0 : 2 * RestStepDuration;
            for (var i = 0; i < 2; i++) _feet[i].RestStart = _feet[i].Last;
        }

        _restTime = Math.Min(2 * RestStepDuration, _restTime + delta);
        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? -spacing : spacing;
            var neutral = new Foot(_x + side * cos + shoeOffset * sin, _z - side * sin + shoeOffset * cos, _yaw, 0, 0);
            var elapsed = i == _restFirst ? _restTime : _restTime - RestStepDuration;
            var pose = elapsed >= RestStepDuration
                ? neutral : Reposition(_feet[i].RestStart, neutral, Math.Max(0, elapsed) / RestStepDuration);
            WriteFoot(feet, extra, i, side, shoeOffset, sin, cos, pose);
        }

        if (_restTime < 2 * RestStepDuration)
            KeepFeetApart(feet, extra, spacing, sin, cos, _restTime <= RestStepDuration ? _restFirst : 1 - _restFirst);
        _ready = true;
        RememberFeet(feet, extra);
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
