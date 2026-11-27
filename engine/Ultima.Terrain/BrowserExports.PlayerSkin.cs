using System.Runtime.InteropServices.JavaScript;

namespace Ultima.Terrain;

internal static partial class BrowserExports
{
    private static readonly double[] SkinLegResults = new double[2 * PlayerSkinLegs.LegOutputSize + 1];

    [JSExport]
    internal static double[] SolvePlayerSkinLegs(double[] legs)
    {
        var right = legs.AsSpan(PlayerSkinLegs.LegInputSize);
        // Both legs share one visual pelvis. Avoid lowering it for extreme or invalid targets.
        var shift = Math.Max(-0.15f, Math.Min(PlayerSkinLegs.LowestReachableHipShift(legs), PlayerSkinLegs.LowestReachableHipShift(right)));
        PlayerSkinLegs.Solve(legs, SkinLegResults, shift);
        PlayerSkinLegs.Solve(right, SkinLegResults.AsSpan(PlayerSkinLegs.LegOutputSize), shift);
        SkinLegResults[^1] = shift;
        return SkinLegResults;
    }
}
