namespace Ultima.Terrain;

internal sealed class TerrainNoise(uint seed)
{
    private readonly byte[] _table = CreateTable(seed);

    private static byte[] CreateTable(uint seed)
    {
        var table = new byte[512];
        for (var i = 0; i < 256; i++) table[i] = (byte)i;
        for (var i = 255; i > 0; i--)
        {
            seed = unchecked(seed * 1664525 + 1013904223);
            var j = (int)(((ulong)seed * (ulong)(i + 1)) >> 32);
            (table[i], table[j]) = (table[j], table[i]);
        }
        Array.Copy(table, 0, table, 256, 256);
        return table;
    }

    internal double Sample(double x, double z)
    {
        var ix = (int)Math.Floor(x);
        var iz = (int)Math.Floor(z);
        var a = _table[ix & 255] + (iz & 255);
        var b = _table[(ix & 255) + 1] + (iz & 255);
        var u = x - ix;
        var v = z - iz;
        var fade = u * u * (3 - 2 * u);
        return Lerp(
            Lerp(Gradient(_table[a], u, v), Gradient(_table[b], u - 1, v), fade),
            Lerp(Gradient(_table[a + 1], u, v - 1), Gradient(_table[b + 1], u - 1, v - 1), fade),
            v * v * (3 - 2 * v));
    }

    private static double Gradient(int bits, double x, double z)
        => ((bits & 1) != 0 ? x : -x) + ((bits & 2) != 0 ? z : -z);

    internal static double Lerp(double a, double b, double t) => a + (b - a) * t;

    internal static double Smooth(double a, double b, double value)
    {
        var t = Math.Clamp((value - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
