namespace Ultima.Terrain;

internal sealed partial class PlayerFootContacts
{
    private bool _pivoting;
    private int _pivotStage, _pivotFoot;

    // Ao retomar a marcha, a perna oposta à que acabou de pousar começa o balanço.
    internal double PivotExitPhase => _pivotFoot == 0 ? 1.5 * Math.PI : 0.5 * Math.PI;

    internal void ApplyPivot(Span<double> feet, Span<double> extra, double spacing, double shoeOffset,
        double start, double turn, double progress, double speed = 0)
    {
        var stage = Math.Min(3, (int)(progress * 4));
        if (!_pivoting)
        {
            _pivoting = true;
            _resting = false;
            _pivotStage = -1;
            _pivotFoot = _feet[0].Last.Lift > _feet[1].Last.Lift ? 0 : 1;
        }

        if (stage != _pivotStage)
        {
            if (_pivotStage >= 0) _pivotFoot = 1 - _pivotFoot;
            _pivotStage = stage;
            var (startSin, startCos) = Math.SinCos(start);
            for (var i = 0; i < 2; i++)
            {
                var side = i == 0 ? -spacing : spacing;
                _feet[i].RestStart = _ready ? _feet[i].Last
                    : new(_x + side * startCos + shoeOffset * startSin,
                        _z - side * startSin + shoeOffset * startCos, start, 0, 0);
            }
        }

        var (sin, cos) = Math.SinCos(_yaw);
        var footProgress = progress * 4 - stage;
        var targetYaw = start + turn * Math.Min(1, (stage + 1) / 3.0);
        var lead = stage == 3 ? Math.Min(0.19, speed * 0.055) : 0;
        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? -spacing : spacing;
            var origin = _feet[i].RestStart;
            // O pé plantado gira sem mudar o ponto de apoio. O outro se aproxima
            // da nova direção em arco curto. A última troca já inicia a passada.
            var pose = i == _pivotFoot
                ? PivotStep(origin, side, shoeOffset, targetYaw, footProgress, lead)
                : origin with { Yaw = _yaw, Lift = 0, Pitch = 0 };
            WriteFoot(feet, extra, i, side, shoeOffset, sin, cos, pose);
        }

        KeepFeetApart(feet, extra, spacing, sin, cos, _pivotFoot);
        _ready = true;
        RememberFeet(feet, extra);
    }

    private Foot PivotStep(Foot origin, double side, double shoeOffset, double targetYaw, double progress, double lead)
    {
        var (toeSin, toeCos) = Math.SinCos(origin.Yaw);
        var x = origin.X - _x - shoeOffset * toeSin;
        var z = origin.Z - _z - shoeOffset * toeCos;
        var radius = double.Hypot(x, z);
        var angle = Math.Atan2(x, z);
        var targetAngle = targetYaw + Math.CopySign(Math.PI / 2, side);
        var blend = Smooth(progress);
        angle += Math.IEEERemainder(targetAngle - angle, Math.Tau) * blend;
        var compactRadius = Math.Abs(side) + .012;
        radius += (compactRadius - radius) * Math.Sqrt(blend);
        var (sin, cos) = Math.SinCos(angle);
        var (yawSin, yawCos) = Math.SinCos(_yaw);
        var lift = origin.Lift * (1 - blend) + .055 * 4 * progress * (1 - progress);
        var pitch = progress < 1 ? origin.Pitch * (1 - blend) + .12 * Math.Sin(Math.Tau * progress) : 0;
        var forward = shoeOffset + lead * blend;
        return new(_x + radius * sin + forward * yawSin, _z + radius * cos + forward * yawCos, _yaw, lift, pitch);
    }
}
