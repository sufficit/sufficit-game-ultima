namespace Ultima.Terrain;

internal sealed class PlayerHandSwing
{
    private double _forearm, _wrist;

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

    internal void Reset() => (_forearm, _wrist) = (0, 0);
}
