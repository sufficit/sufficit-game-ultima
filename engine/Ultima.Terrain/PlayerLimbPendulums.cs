using Ultima.Models.Core;

namespace Ultima.Terrain;

internal sealed class PlayerLimbPendulums
{
    private Joint _thorax, _head;
    private readonly Joint[] _arms = new Joint[6];
    private readonly Joint[] _legs = new Joint[6];
    private double _remaining;

    internal void Reset()
    {
        _thorax = _head = default;
        Array.Clear(_arms);
        Array.Clear(_legs);
        _remaining = 0;
    }

    internal void Apply(HumanoidBody body, Span<double> pose, Span<double> gait, double tilt, double delta)
    {
        var upper = body.Height * body.ArmLengthRatio * body.UpperArmRatio;
        var lower = body.Height * body.ArmLengthRatio - upper;
        _remaining += Math.Clamp(delta, 0, 6 * PlayerBalance.Step);
        while (_remaining + 1e-10 >= PlayerBalance.Step)
        {
            _remaining = Math.Max(0, _remaining - PlayerBalance.Step);
            _thorax.Step(body.Mass * (PlayerSegmentMass.Thorax + PlayerSegmentMass.Abdomen),
                body.ShoulderHeight - body.HipHeight, tilt * .55, 48, 10);
            _head.Step(body.Mass * PlayerSegmentMass.Head, body.Height * .14, -_thorax.Angle, 48, 12);
            for (var side = 0; side < 2; side++)
            {
                var i = side * 3;
                _arms[i].Step(body.Mass * (PlayerSegmentMass.UpperArm + PlayerSegmentMass.Forearm + PlayerSegmentMass.Hand),
                    upper, -tilt * 1.3, 44, 8);
                _arms[i + 1].Step(body.Mass * (PlayerSegmentMass.Forearm + PlayerSegmentMass.Hand),
                    lower, -_arms[i].Angle, 34, 7);
                _arms[i + 2].Step(body.Mass * PlayerSegmentMass.Hand, body.Height * .095, -_arms[i + 1].Angle, 28, 7);
                _legs[i].Step(body.Mass * (PlayerSegmentMass.Thigh + PlayerSegmentMass.Shin + PlayerSegmentMass.Foot),
                    body.FemurLength, -tilt, 44, 8);
                _legs[i + 1].Step(body.Mass * (PlayerSegmentMass.Shin + PlayerSegmentMass.Foot),
                    body.TibiaLength, -_legs[i].Angle, 34, 8);
                _legs[i + 2].Step(body.Mass * PlayerSegmentMass.Foot, body.Height * .12, -_legs[i + 1].Angle, 28, 7);
            }
        }
        pose[2] += _thorax.Angle;
        pose[4] += _head.Angle;
        for (var side = 0; side < 2; side++)
        {
            var i = side * 3;
            pose[10 + side] += _arms[i].Angle;
            gait[8 + side] += _arms[i + 1].Angle;
            gait[10 + side] += _arms[i + 2].Angle;
            // Leg IK remains authoritative. Allow the unloaded foot to settle in pitch
            // along the thigh, shin and foot chain without rotating a planted sole.
            var clearance = Math.Min(1, pose[29 + 5 * side] / .09);
            var footPitch = clearance * (_legs[i].Angle + _legs[i + 1].Angle + _legs[i + 2].Angle);
            pose[20 + 3 * side] += footPitch;
            pose[35 + side] += footPitch;
        }
    }

    private struct Joint
    {
        internal double Angle;
        private double _velocity;

        internal void Step(double mass, double span, double target, double stiffness, double damping)
        {
            var radius = Math.Max(.025, span * .5);
            var inertia = mass * span * span / 3;
            var gravityTorque = -mass * PlayerSegmentMass.Gravity * radius * Math.Sin(Angle);
            var angularAcceleration = gravityTorque / inertia + stiffness * (target - Angle) - damping * _velocity;
            _velocity += angularAcceleration * PlayerBalance.Step;
            Angle = Math.Clamp(Angle + _velocity * PlayerBalance.Step, -.20, .20);
        }
    }
}
