namespace Ultima.Terrain;

internal sealed class PlayerSkinHeight
{
    private double _height = double.NaN;

    internal void Reset() => _height = double.NaN;

    // Keep the presentation root independent of cyclic changes to the gameplay
    // root. Limit its height to the reach of the shorter available leg chain.
    internal double Advance(double root, double support, double gaitWeight, double maximumShift, double delta)
    {
        // Leave reach in reserve through the stride instead of dipping only
        // when a support leg reaches full extension.
        var target = support - .060 * gaitWeight;
        if (!double.IsFinite(_height) || Math.Abs(target - _height) > .60) _height = target;
        _height += (target - _height) * (1 - Math.Exp(-10 * Math.Clamp(delta, 0, .05)));
        _height = Math.Clamp(_height, root - .18, root + Math.Max(-.18, maximumShift));
        return _height - root;
    }
}
