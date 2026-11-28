using System.Numerics;

namespace Ultima.Terrain;

internal sealed class PlayerBalance
{
    internal enum Profile { Normal, Drunk, Focused }
    internal enum State { Stable, Swaying, Stumbling, Fallen }
    private readonly record struct Control(float Stiffness, float Damping, float Noise, float Recovery, double Grace);

    // 120 Hz semi-implicit integration, at most six steps per render frame.
    internal const double Step = 1d / 120;
    private Vector2 _lean, _previousLean, _angularVelocity;
    private double _remaining, _clock, _overreach;
    internal Profile Mode { get; set; }
    internal State Status { get; private set; }
    internal Vector3 Center { get; private set; }
    internal Vector2 Capture { get; private set; }
    internal double Margin { get; private set; }
    internal double FallYaw { get; private set; }
    internal Vector2 Lean => Vector2.Lerp(_previousLean, _lean, (float)(_remaining / Step));

    internal void ResetDynamics()
    {
        _lean = _previousLean = _angularVelocity = Capture = Vector2.Zero;
        Center = default;
        _remaining = _overreach = _clock = Margin = FallYaw = 0;
        Status = State.Stable;
    }

    // Contact impulse in newton-seconds; dividing by body mass gives a change in m/s.
    internal void Impulse(Vector2 impulse, double mass, double height)
    {
        if (Status == State.Fallen) return;
        _angularVelocity = Vector2.Clamp(_angularVelocity + impulse / (float)(mass * height), new(-7), new(7));
    }

    internal State Advance(Vector3 center, Vector2 left, Vector2 right, Vector2 sole,
        double speed, double delta, bool supported, bool upright)
    {
        Center = center;
        if (!upright || Status == State.Fallen) return Status;
        var control = Mode switch
        {
            Profile.Drunk => new Control(17, 2.7f, .85f, .10f, .15),
            Profile.Focused => new Control(35, 9.0f, .09f, .29f, .25),
            _ => new Control(26, 6.2f, .20f, .22f, .20)
        };
        var h = Math.Clamp(center.Y, .32f, 1.4f);
        var gravity = (float)PlayerSegmentMass.Gravity / h;
        _remaining += Math.Clamp(delta, 0, 6 * Step);
        while (_remaining + 1e-10 >= Step)
        {
            _remaining = Math.Max(0, _remaining - Step);
            _previousLean = _lean;
            _clock += Step;
            var noise = new Vector2((float)Math.Sin(_clock * 2.7), (float)Math.Sin(_clock * 3.9 + .5));
            if (supported)
            {
                // Gravity destabilizes upright tilt. Muscle feedback counters it with
                // profile-dependent stiffness, damping and a small postural disturbance.
                var torque = -_lean * Math.Max(1.2f, control.Stiffness - gravity) -
                    _angularVelocity * control.Damping + noise * control.Noise;
                _angularVelocity += torque * (float)Step;
            }
            _lean = Vector2.Clamp(_lean + _angularVelocity * (float)Step, new(-.9f), new(.9f));
            Capture = new Vector2(center.X, center.Z) + h * (_lean + _angularVelocity / MathF.Sqrt(gravity));
            if (!supported)
            {
                _overreach = 0;
                continue;
            }

            Margin = SupportMargin(Capture, left, right, sole);
            // Moving feet can provide the next support, beyond the current planted soles.
            // The speed term budgets a planned step rather than treating steady gait as a fall.
            var stepReach = control.Recovery + (float)Math.Min(.33, speed * .15);
            var excess = -Margin - stepReach;
            _overreach = excess > 0 ? _overreach + Step : Math.Max(0, _overreach - 2 * Step);
            if (_overreach >= control.Grace || _lean.LengthSquared() > .65f)
            {
                // The resting rig faces up with the head towards local -Z.
                FallYaw = Math.Atan2(-Capture.X, -Capture.Y);
                return Status = State.Fallen;
            }
            Status = excess > 0 || _lean.LengthSquared() > .13f
                ? State.Stumbling
                : _lean.LengthSquared() > .0016f ? State.Swaying : State.Stable;
        }
        return Status;
    }

    // Capsule in sole-normalized space: a single footprint or the convex bridge of two.
    // Positive margin is inside this approximation of the support polygon.
    internal static double SupportMargin(Vector2 point, Vector2 left, Vector2 right, Vector2 sole)
    {
        var relative = (point - left) / sole;
        var axis = (right - left) / sole;
        var lengthSquared = axis.LengthSquared();
        var projection = lengthSquared > 1e-7f ? Math.Clamp(Vector2.Dot(relative, axis) / lengthSquared, 0, 1) : 0;
        return (1 - (relative - axis * projection).Length()) * Math.Min(sole.X, sole.Y);
    }
}
