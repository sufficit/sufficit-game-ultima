namespace Ultima.Terrain;

internal sealed class PlayerPosture
{
    internal enum Kind { Standing, Crouching, Crawling, Lying, Sitting, Fallen }
    internal Kind Target { get; private set; }
    internal double SpeedScale => _recovering && _prone > .18 ? 0 : Target switch
    {
        Kind.Crouching => .58, Kind.Crawling => .36, Kind.Lying or Kind.Sitting or Kind.Fallen => 0, _ => 1
    };
    private bool _jumpRequested, _airborne, _recovering;
    private double _feet, _verticalSpeed;
    private double _crouch, _prone, _sit, _crawl, _fallTime, _fallYaw;
    internal bool Airborne => _airborne;
    internal bool CanBalance => (Target is Kind.Standing or Kind.Crouching) && _prone + _sit < .15;

    internal void Fall(double yaw)
    {
        if (Target == Kind.Fallen) return;
        _jumpRequested = false;
        Target = Kind.Fallen;
        _fallTime = 0;
        _fallYaw = yaw;
    }

    internal void Toggle(Kind target)
    {
        if (!_airborne && Target != Kind.Fallen) Target = Target == target ? Kind.Standing : target;
    }

    internal void Jump()
    {
        if (Target == Kind.Standing) _jumpRequested = true;
        else if (Target != Kind.Fallen || _prone >= .98)
        {
            _recovering = Target == Kind.Fallen;
            Target = Kind.Standing;
        }
    }

    internal void Advance(Span<double> pose, double support, double ceiling, double dt, double phase, double gaitWeight)
    {
        var step = Math.Clamp(dt, 0, .05);
        if (_jumpRequested && Target == Kind.Standing && !_airborne && _prone + _sit + _crouch < .1 && ceiling - support > .035)
        {
            _airborne = true;
            _feet = support;
            _verticalSpeed = 3.5;
        }
        _jumpRequested = false;
        if (_airborne)
        {
            const double gravity = PlayerSegmentMass.Gravity;
            _feet += _verticalSpeed * step - .5 * gravity * step * step;
            _verticalSpeed -= gravity * step;
            if (_feet > ceiling && _verticalSpeed > 0)
            {
                _feet = ceiling;
                _verticalSpeed = 0;
            }
            if (_verticalSpeed <= 0 && _feet <= support) _airborne = false;
        }

        var response = 1 - Math.Exp(-11 * step);
        _crouch = Blend(_crouch, Target == Kind.Crouching, response);
        if (Target == Kind.Fallen)
        {
            _fallTime += step;
            // Approximate the supported fall as an accelerating tilt until the calibrated ground pose.
            _prone = Math.Max(_prone, Math.Min(1, .5 * PlayerSegmentMass.Gravity * _fallTime * _fallTime / 1.35));
        }
        else _prone = Blend(_prone, Target is Kind.Crawling or Kind.Lying, response);
        if (_prone == 0)
        {
            _fallYaw = 0;
            _recovering = false;
        }
        _sit = Blend(_sit, Target == Kind.Sitting, response);
        _crawl = Blend(_crawl, Target == Kind.Crawling, response);
        var swing = Math.Sin(phase) * _crawl * gaitWeight;
        pose[0] = (double)Target;
        pose[1] = _airborne ? Math.Max(0, _feet - support) : 0;
        pose[2] = _crouch * .28;
        pose[3] = _prone;
        pose[4] = _sit;
        pose[5] = swing;
        pose[6] = _airborne ? Math.Min(1, pose[1] * 4) : 0;
        pose[7] = _crawl;
        pose[8] = _fallYaw;
    }

    private static double Blend(double value, bool active, double response)
    {
        var target = active ? 1 : 0;
        var next = value + (target - value) * response;
        return Math.Abs(next - target) < .001 ? target : next;
    }
}
