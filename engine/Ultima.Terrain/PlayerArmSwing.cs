namespace Ultima.Terrain;

internal sealed class PlayerArmSwing
{
    private double _upperArm, _elbow;
    private double _forearm, _wrist;
    private double _hand, _base, _middle, _tip;

    // Local flexion is negative around the rig's X axis. A slowly following
    // upper-arm reference gives the forearm its own phase at the elbow.
    internal double AdvanceElbow(double upperArm, double authoredFlexion, double walking, double weight, double delta)
    {
        var step = Math.Clamp(delta, 0, .05);
        _upperArm = Damp(_upperArm, upperArm, 7, step);
        var inertia = Math.Clamp((_upperArm - upperArm) * .92, -.24, .24);
        _elbow = Damp(_elbow, inertia, 18, step);
        var walkingFlexion = Math.Clamp(authoredFlexion + _elbow, -.42 * weight, -.025 * weight);
        return authoredFlexion + (walkingFlexion - authoredFlexion) * walking;
    }

    // Inertia opposes changes to the forearm angle. Two damped responses
    // model lag at the wrist and let the hand settle when the arm stops.
    internal double Advance(double forearm, double delta)
    {
        var step = Math.Clamp(delta, 0, .05);
        _forearm += (forearm - _forearm) * (1 - Math.Exp(-9 * step));
        var target = Math.Clamp((_forearm - forearm) * .8, -.22, .22);
        _wrist += (target - _wrist) * (1 - Math.Exp(-21 * step));
        return _wrist;
    }

    // The angles are local offsets in radians. Each stage follows the
    // preceding one, so motion arrives at the tips last.
    internal void AdvanceFingers(double forearm, double delta, Span<double> offsets)
    {
        var hand = forearm + _wrist;
        var step = Math.Clamp(delta, 0, .05);
        _hand = Damp(_hand, hand, 14, step);
        _base = Damp(_base, Math.Clamp((hand - _hand) * .32, -.055, .055), 16, step);
        _middle = Damp(_middle, _base * .68, 14, step);
        _tip = Damp(_tip, _middle * .74, 12, step);
        offsets[0] = _base;
        offsets[1] = _middle;
        offsets[2] = _tip;
    }

    private static double Damp(double value, double target, double rate, double step)
        => value + (target - value) * (1 - Math.Exp(-rate * step));

    internal void Reset() => (_upperArm, _elbow, _forearm, _wrist, _hand, _base, _middle, _tip) = (0, 0, 0, 0, 0, 0, 0, 0);
}
