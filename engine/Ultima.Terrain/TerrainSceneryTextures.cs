using System.Runtime.InteropServices;

namespace Ultima.Terrain;

internal static class TerrainSceneryTextures
{
    private static readonly TerrainNoise BarkNoise = new(421);
    private static readonly TerrainNoise LeafNoise = new(907);
    private static readonly TerrainNoise WaterNoise = new(4242);

    internal static byte[] Bark(int size)
    {
        var pixels = size * size;
        var data = new byte[pixels * 8];
        var height = MemoryMarshal.Cast<byte, float>(data.AsSpan(pixels * 4));
        var angles = Circle(size);
        var ridge = new double[size];
        for (var x = 0; x < size; x++)
            ridge[x] = BarkNoise.Sample(angles[x].Cos * 6 + 3, angles[x].Sin * 6 + 3);

        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var (sin, cos) = angles[x];
            var fibre = BarkNoise.Sample(cos * 14 - 9, y * 0.35 + sin * 14);
            var value = 0.52 + ridge[x] * 0.30 + (fibre - 0.5) * 0.22;
            var i = y * size + x;
            height[i] = (float)value;
            Pixel(data, i * 4, Truncate(value * 190), Truncate(value * 135), Truncate(value * 88));
        }
        return data;
    }

    internal static byte[] Foliage(int size)
    {
        var data = new byte[size * size * 4];
        var angles = Circle(size);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var (sin, cos) = angles[x];
            var blob = LeafNoise.Sample(cos * 5 + 1, sin * 5 + angles[y].Cos * 5);
            var leaf = LeafNoise.Sample(cos * 16 + 8, angles[y].Sin * 16 - 4);
            var tone = blob * 0.7 + leaf * 0.3;
            Pixel(data, (y * size + x) * 4, Truncate((0.10 + tone * 0.22) * 255),
                Truncate((0.26 + tone * 0.42) * 255), Truncate((0.10 + tone * 0.16) * 255));
        }
        return data;
    }

    internal static byte[] Water(int size)
    {
        var data = new byte[size * size * 4];
        var angles = Circle(size);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var (sinU, cosU) = angles[x];
            var (sinV, cosV) = angles[y];
            var dx = (WaterNoise.Sample(cosU * 4 + 1, sinV * 4) - 0.5) * 0.9;
            var dy = (WaterNoise.Sample(cosV * 4 - 2, sinU * 4 + 7) - 0.5) * 0.9;
            Normal(data, (y * size + x) * 4, dx, dy, 3);
        }
        return data;
    }

    internal static byte[] Bump(ReadOnlySpan<float> height, int size, double strength)
    {
        var data = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            var above = (y == 0 ? size - 1 : y - 1) * size;
            var below = (y + 1 == size ? 0 : y + 1) * size;
            var row = y * size;
            for (var x = 0; x < size; x++)
            {
                var left = x == 0 ? size - 1 : x - 1;
                var right = x + 1 == size ? 0 : x + 1;
                var dx = ((double)height[row + right] - height[row + left]) * strength;
                var dy = ((double)height[below + x] - height[above + x]) * strength;
                Normal(data, (row + x) * 4, dx, dy, 1);
            }
        }
        return data;
    }

    private static (double Sin, double Cos)[] Circle(int size)
    {
        var angles = new (double Sin, double Cos)[size];
        for (var i = 0; i < size; i++) angles[i] = Math.SinCos((double)i / size * Math.PI * 2);
        return angles;
    }

    private static void Normal(byte[] data, int i, double x, double y, double z)
    {
        var inverse = 1 / Math.Sqrt(x * x + y * y + z * z);
        Pixel(data, i, Round((-x * inverse * 0.5 + 0.5) * 255),
            Round((-y * inverse * 0.5 + 0.5) * 255), Round((z * inverse * 0.5 + 0.5) * 255));
    }

    private static void Pixel(byte[] data, int i, byte r, byte g, byte b)
    {
        data[i] = r;
        data[i + 1] = g;
        data[i + 2] = b;
        data[i + 3] = 255;
    }

    private static byte Truncate(double value) => (byte)Math.Clamp(value, 0, 255);
    private static byte Round(double value)
    {
        // Os componentes da normal já estão em [0, 255]. Repete ties-to-even
        // do ImageData sem passar por clamp em cada canal.
        var whole = (int)value;
        var fraction = value - whole;
        return (byte)(whole + (fraction > 0.5 || (fraction == 0.5 && (whole & 1) != 0) ? 1 : 0));
    }
}
