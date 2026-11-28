namespace Ultima.Terrain;

/// <summary>Samples the Quaternius Walk_Loop on Ultima's distance-driven foot phase.</summary>
internal static class PlayerAuthoredWalk
{
    internal const int Channels = 12;

    // Twenty-four evenly spaced samples of the 1.333 s CC0 clip, wrapped at the seam.
    // Angles are milliradians and pelvis sway is millimetres on the source rig.
    // Each channel is centered over the full cycle to preserve Ultima's relaxed bind pose.
    // Source clip time zero is left heel strike, at phase 3π/2 in Ultima.
    private static ReadOnlySpan<short> Keys =>
    [
        -7, 0, -27, -42, 0, 6, -24, 2, 10, -43, 91, 39,
        1, 11, -32, 0, 10, -8, 4, 8, -3, -29, 16, 92,
        8, 21, -35, 40, 21, -22, 29, 14, -13, -12, -67, 128,
        15, 30, -35, 78, 31, -33, 53, 19, -20, 6, -150, 151,
        22, 37, -33, 112, 39, -43, 77, 24, -23, 22, -228, 168,
        27, 41, -29, 140, 45, -50, 101, 27, -24, 36, -291, 183,
        32, 43, -22, 160, 46, -55, 125, 30, -25, 46, -333, 199,
        34, 41, -15, 170, 44, -55, 145, 30, -27, 52, -345, 216,
        35, 37, -6, 167, 38, -52, 156, 27, -32, 54, -316, 226,
        32, 30, 3, 151, 30, -45, 154, 22, -38, 52, -242, 222,
        26, 21, 12, 125, 20, -35, 138, 15, -42, 42, -136, 202,
        19, 11, 20, 91, 10, -22, 110, 6, -42, 29, -36, 165,
        9, 0, 27, 53, 0, -8, 71, -3, -36, 16, 43, 110,
        -1, -11, 32, 11, -11, 7, 25, -12, -23, 4, 97, 40,
        -11, -21, 35, -31, -21, 21, -24, -20, -6, -6, 134, -40,
        -20, -30, 35, -72, -31, 33, -71, -26, 11, -12, 160, -125,
        -28, -37, 33, -110, -38, 44, -114, -30, 26, -15, 178, -208,
        -33, -41, 29, -144, -43, 52, -148, -30, 38, -17, 194, -283,
        -35, -43, 22, -172, -45, 56, -173, -28, 47, -20, 207, -346,
        -34, -41, 14, -186, -43, 57, -181, -24, 52, -26, 222, -368,
        -31, -37, 6, -179, -38, 53, -166, -19, 55, -34, 230, -336,
        -26, -30, -3, -156, -30, 45, -134, -15, 51, -44, 223, -254,
        -21, -21, -12, -122, -21, 34, -94, -10, 40, -50, 197, -142,
        -14, -11, -20, -83, -11, 21, -57, -4, 25, -51, 153, -39,
    ];

    internal static void Sample(double phase, Span<double> values)
    {
        var count = Keys.Length / Channels;
        var sample = phase / Math.Tau * count;
        var whole = Math.Floor(sample);
        var frame = ((int)whole % count + count) % count;
        var previous = ((frame + count - 1) % count) * Channels;
        var current = frame * Channels;
        var next = ((frame + 1) % count) * Channels;
        var following = ((frame + 2) % count) * Channels;
        var t = sample - whole;
        for (var i = 0; i < values.Length; i++)
        {
            var a = (double)Keys[current + i];
            var b = (double)Keys[next + i];
            var startSlope = (b - Keys[previous + i]) * .5;
            var endSlope = (Keys[following + i] - a) * .5;
            // Periodic cubic Hermite interpolation keeps angular velocity continuous.
            values[i] = (a + t * (startSlope + t *
                (3 * (b - a) - 2 * startSlope - endSlope + t *
                    (2 * (a - b) + startSlope + endSlope)))) * .001;
        }
    }
}
