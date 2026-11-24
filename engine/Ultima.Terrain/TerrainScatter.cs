using static Ultima.Terrain.TerrainNoise;

namespace Ultima.Terrain;

internal static class TerrainScatter
{
    internal const int Channels = 6;
    internal const int Header = 3;
    internal const int Batch = 24;
    private const double Extent = 52;

    internal static void Fill(Span<double> output, TerrainSurface surface, uint state, int remaining, int attempts,
        bool tree, bool workshop, double guardianX, double guardianZ, double minRadius, double maxAltitude,
        double maxSlope, double minScale, double maxScale, double sink)
    {
        var treeExtent = tree ? 404 : Extent;
        var sanctuary = tree ? 5.5 : 4.5;
        var pathLimit = tree ? 0.75 : 0.72;
        var count = Math.Min(Batch, remaining);
        var placed = 0;
        while (placed < count && attempts > 0)
        {
            attempts--;
            var x = (Next(ref state) * 2 - 1) * Extent;
            var z = (Next(ref state) * 2 - 1) * treeExtent;
            if (x * x + z * z < minRadius * minRadius) continue;
            var dx = x - guardianX;
            var dz = z - guardianZ;
            if (dx * dx + dz * dz < sanctuary * sanctuary) continue;
            if (Smooth(1.5, 3.4, Math.Abs(x - TerrainSurface.PathOffset(z))) < pathLimit) continue;
            var natural = TerrainSurface.BaseHeight(x, z);
            if (natural > maxAltitude || surface.Slope(x, z, true) > maxSlope) continue;

            var scale = minScale + Next(ref state) * (tree ? 0.9 : maxScale - minScale);
            double heightScale, rotation;
            if (tree)
            {
                heightScale = scale * (0.9 + Next(ref state) * 0.5);
                rotation = Next(ref state) * Math.PI * 2;
            }
            else
            {
                rotation = Next(ref state) * Math.PI * 2;
                heightScale = scale * (workshop ? 0.9 + Next(ref state) * 0.3 : 0.85 + Next(ref state) * 0.5);
            }
            var offset = Header + placed * Channels;
            output[offset] = x;
            output[offset + 1] = surface.HeightFromBase(x, z, natural) + (tree ? -sink * heightScale : sink * scale);
            output[offset + 2] = z;
            output[offset + 3] = scale;
            output[offset + 4] = heightScale;
            output[offset + 5] = rotation;
            placed++;
        }
        output[0] = state;
        output[1] = attempts;
        output[2] = placed;
    }

    private static double Next(ref uint state)
    {
        state = unchecked(state + 0x6D2B79F5u);
        var t = unchecked((state ^ (state >> 15)) * (1 | state));
        t = unchecked(t + (t ^ (t >> 7)) * (61 | t)) ^ t;
        return (t ^ (t >> 14)) / 4294967296.0;
    }
}
