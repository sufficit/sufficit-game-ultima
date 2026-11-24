using System.Runtime.InteropServices;

namespace Ultima.Terrain;

internal static class TerrainMeshEffects
{
    internal static byte[] Prepare(ReadOnlySpan<byte> vertices, double[] effects)
    {
        var hasColors = false;
        for (var i = 0; i < effects.Length; i += 3)
            hasColors |= effects[i] == 2;
        var data = new byte[vertices.Length + (hasColors ? vertices.Length / 3 * 4 : 0)];
        vertices.CopyTo(data);
        var positions = MemoryMarshal.Cast<byte, float>(data.AsSpan(0, vertices.Length));
        var colors = MemoryMarshal.Cast<byte, float>(data.AsSpan(vertices.Length));
        for (var i = 0; i < effects.Length; i += 3)
        {
            var noise = new TerrainNoise((uint)(int)effects[i + 2]);
            if (effects[i] == 2) Shade(positions, colors, effects[i + 1], noise);
            else Deform(positions, effects[i + 1], noise, flute: effects[i] == 1);
        }
        return data;
    }

    private static void Deform(Span<float> positions, double amount, TerrainNoise noise, bool flute)
    {
        for (var i = 0; i < positions.Length; i += 3)
        {
            double x = positions[i], y = positions[i + 1], z = positions[i + 2];
            var radius = x * x + z * z;
            double sample;
            if (flute)
            {
                if (radius < 1e-10) continue;
                radius = Math.Sqrt(radius);
                sample = noise.Sample(x / radius * 3 + 5, z / radius * 3 + 5);
            }
            else
            {
                if (radius + y * y < 1e-10) continue;
                sample = noise.Sample(x * 2.3 + y * 1.1, z * 2.3 - y * 0.7);
            }
            var factor = 1 + (sample - 0.5) * 2 * amount;
            positions[i] = (float)(x * factor);
            if (!flute) positions[i + 1] = (float)(y * factor);
            positions[i + 2] = (float)(z * factor);
        }
    }

    private static void Shade(ReadOnlySpan<float> positions, Span<float> colors, double darkest, TerrainNoise noise)
    {
        var min = float.PositiveInfinity;
        var max = float.NegativeInfinity;
        for (var i = 1; i < positions.Length; i += 3)
        {
            min = Math.Min(min, positions[i]);
            max = Math.Max(max, positions[i]);
        }

        var range = Math.Max(0.001, (double)max - min);
        for (int i = 0, c = 0; i < positions.Length; i += 3, c += 4)
        {
            var t = ((double)positions[i + 1] - min) / range;
            var shade = darkest + (1 - darkest) * Math.Pow(t, 0.7)
                + (noise.Sample(positions[i] * 1.7, positions[i + 2] * 1.7) - 0.5) * 0.12;
            var value = Math.Clamp(shade, 0.2, 1.05);
            colors[c] = colors[c + 1] = (float)value;
            colors[c + 2] = (float)(value * 0.97);
            colors[c + 3] = 1;
        }
    }
}
