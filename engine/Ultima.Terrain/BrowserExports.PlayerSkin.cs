using System.Runtime.InteropServices.JavaScript;

namespace Ultima.Terrain;

internal static partial class BrowserExports
{
    private static readonly double[] SkinLegResults = new double[2 * PlayerSkinLegs.LegOutputSize + 1];
    private static readonly PlayerSkinHeight SkinHeight = new();
    private static readonly PlayerHandSwing LeftHandSwing = new();
    private static readonly PlayerHandSwing RightHandSwing = new();

    [JSExport]
    internal static void UseNaturalGait() => _movement.NaturalGait = true;

    [JSExport]
    internal static double[] SolvePlayerSkinLegs(double[] legs, double root, double support, double weight, double delta)
    {
        var right = legs.AsSpan(PlayerSkinLegs.LegInputSize);
        var maximumShift = Math.Min(PlayerSkinLegs.HighestReachableHipShift(legs), PlayerSkinLegs.HighestReachableHipShift(right));
        var shift = (float)SkinHeight.Advance(root, support, weight, maximumShift, delta);
        PlayerSkinLegs.Solve(legs, SkinLegResults, shift);
        PlayerSkinLegs.Solve(right, SkinLegResults.AsSpan(PlayerSkinLegs.LegOutputSize), shift);
        SkinLegResults[^1] = shift;
        return SkinLegResults;
    }
}
