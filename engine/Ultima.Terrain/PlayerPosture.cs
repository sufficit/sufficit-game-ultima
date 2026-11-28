namespace Ultima.Terrain;

internal sealed class PlayerPosture
{
    internal enum Kind { Standing, Crouching, Crawling, Lying, Sitting }
    internal Kind Target { get; private set; }
    internal double SpeedScale => Target switch
    {
        Kind.Crouching => .58, Kind.Crawling => .36, Kind.Lying or Kind.Sitting => 0, _ => 1
    };
    private bool _jumpRequested, _airborne;
    private double _feet, _verticalSpeed;
    private double _crouch, _prone, _sit, _crawl;

    internal void Toggle(Kind target)
    {
        if (!_airborne) Target = Target == target ? Kind.Standing : target;
    }

    internal void Jump()
    {
        if (Target == Kind.Standing) _jumpRequested = true;
        else Target = Kind.Standing;
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
            const double gravity = 9.81;
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
        _prone = Blend(_prone, Target is Kind.Crawling or Kind.Lying, response);
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
    }

    private static double Blend(double value, bool active, double response)
    {
        var target = active ? 1 : 0;
        var next = value + (target - value) * response;
        return Math.Abs(next - target) < .001 ? target : next;
    }
}
