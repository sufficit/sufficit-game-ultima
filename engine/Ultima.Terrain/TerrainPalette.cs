using static Ultima.Terrain.TerrainNoise;

namespace Ultima.Terrain;

internal sealed class TerrainPalette(TerrainSurface surface)
{
    private enum Soil { Grass, Dirt, Sand, Rock, Snow, Water }
    private readonly record struct Paint(double X, double Z, double Radius, Soil Material);
    private readonly record struct Sample(double Red, double Green, double Blue, double Grass, double Dirt, Soil Material);

    private static readonly Sample FullSnow = new(0.93, 0.95, 0.97, 0, 0, Soil.Snow);
    private static readonly TerrainNoise Grain = new(9090);
    private static readonly TerrainNoise Mineral = new(577);
    private static readonly (double R, double G, double B)[] PaintColors =
        [(0.30, 0.46, 0.16), (0.47, 0.36, 0.23), (0.78, 0.70, 0.50),
         (0.46, 0.45, 0.44), (0.93, 0.95, 0.97), (0.16, 0.42, 0.62)];
    private static readonly string[] Labels = ["Solo com grama", "Terra", "Areia", "Rocha", "Neve", "Água"];
    private Paint[] _paints = [];

    internal void SetPaints(double[] values)
    {
        var paints = new Paint[values.Length / 4];
        for (var i = 0; i < paints.Length; i++)
        {
            var j = i * 4;
            paints[i] = new(values[j], values[j + 1], values[j + 2], (Soil)values[j + 3]);
        }
        _paints = paints;
    }

    internal string MaterialLabel(double x, double y, double z)
        => Labels[(int)Color(x, y, z, TerrainSurface.PathOffset(z), surface.Depth(x, z), _paints).Material];

    // Os planos RGBA podem ser copiados em bloco para o ImageData. As duas máscaras usam apenas alfa.
    internal byte[] Rasterize(int cx, int cz, int samples, int gutter, bool detailed)
    {
        var side = samples + 2 * gutter;
        var plane = side * side * 4;
        var data = new byte[detailed ? plane * 3 : plane];
        var x0 = cx * TerrainSurface.Chunk - TerrainSurface.Chunk / 2;
        var z0 = cz * TerrainSurface.Chunk - TerrainSurface.Chunk / 2;
        var step = (double)TerrainSurface.Chunk / samples;
        var paints = PaintsTouchingChunk(x0, z0, TerrainSurface.Chunk, gutter * step);
        var pixel = 0;
        for (var j = 0; j < side; j++)
        {
            var z = z0 + (j - gutter + 0.5) * step;
            var pathOffset = TerrainSurface.PathOffset(z);
            for (var i = 0; i < side; i++, pixel += 4)
            {
                var x = x0 + (i - gutter + 0.5) * step;
                var depth = surface.Depth(x, z);
                var y = surface.Height(x, z, depth);
                var color = Color(x, y, z, pathOffset, depth, paints);
                var mottling = Grain.Sample(x * 0.94, z * 0.94);
                var grit = Grain.Sample(x * 2.1 + 13, z * 2.1 - 17);
                var relief = detailed ? 1 - (color.Grass + color.Dirt) * 0.52 : 1;
                var grain = 1.07 + (mottling * 0.25 + grit * 0.22) * relief;
                data[pixel] = ClampedByte(color.Red * grain * 255);
                data[pixel + 1] = ClampedByte(color.Green * grain * 255);
                data[pixel + 2] = ClampedByte(color.Blue * grain * 255);
                data[pixel + 3] = 255;
                if (detailed)
                {
                    data[plane + pixel + 3] = ClampedByte(color.Grass * 196);
                    data[2 * plane + pixel + 3] = ClampedByte(color.Dirt * 184);
                }
            }
        }
        return data;
    }

    private Paint[] PaintsTouchingChunk(double x, double z, double size, double gutter)
    {
        if (_paints.Length == 0) return _paints;

        // Inclui a margem rasterizada e até 0,5 m de deslocamento horizontal
        // causado pelo ruído do pincel, inclusive em pinturas sobre a divisa.
        var minX = x - gutter - 0.5;
        var maxX = x + size + gutter + 0.5;
        var minZ = z - gutter;
        var maxZ = z + size + gutter;
        List<Paint>? relevant = null;
        foreach (var paint in _paints)
        {
            var radius = paint.Radius;
            if (paint.X + radius >= minX && paint.X - radius <= maxX &&
                paint.Z + radius >= minZ && paint.Z - radius <= maxZ)
                (relevant ??= []).Add(paint);
        }
        return relevant?.ToArray() ?? [];
    }

    private Sample Color(double x, double y, double z, double pathOffset, double soilDepth, Paint[] paints)
    {
        // O ruído está entre -2 e 2, e a neve satura antes de 14 m.
        // Sem pintura, cor e máscaras deixam de depender da inclinação nessa faixa.
        if (y >= 14 && paints.Length == 0) return FullSnow;

        var slope = surface.Slope(x, z, false);
        var noise = TerrainSurface.Noise;
        var patch = noise.Sample(x * 0.11 + 100, z * 0.11 - 60);
        var fine = noise.Sample(x * 0.85 - 13.7, z * 0.85 + 77.1) * 0.5 + 0.5;
        var t = Math.Clamp(y * 0.16 + 0.42 + patch * 0.45 + (fine - 0.5) * 0.18, 0, 1);
        var dry = Math.Max(0, patch - 0.35) * 1.4;
        var r = Lerp(Lerp(0.10, 0.25, t), 0.44, dry);
        var g = Lerp(Lerp(0.27, 0.44, t), 0.42, dry);
        var b = Lerp(Lerp(0.09, 0.13, t), 0.18, dry);
        var fringe = noise.Sample(x * 0.67 + 4.2, z * 0.67 - 3.1) * 0.55 + (fine - 0.5) * 0.35;
        var distance = Math.Abs(x - pathOffset) + fringe;
        var path = Smooth(1.3, 3.6, distance);
        var core = (1 - path) * (1 - Smooth(0.4, 2.6, distance)) * 0.5;
        var dr = Lerp(0.58, 0.47, path);
        var dg = Lerp(0.47, 0.36, path);
        var db = Lerp(0.32, 0.23, path);
        r = Lerp(Lerp(dr, r, path), dr * 1.08, core);
        g = Lerp(Lerp(dg, g, path), dg * 1.08, core);
        b = Lerp(Lerp(db, b, path), db * 1.08, core);

        var rocky = Smooth(0.44, 1.22, slope + fringe * 0.35);
        var exposedRock = Smooth(-0.5, 0.35, surface.CarveInset(x, z) + fine * 0.18 - 0.09);
        var mineralCover = 1 - (1 - exposedRock) * (1 - rocky);
        var rockU = x * 1.15 + z * 0.36;
        var rockV = z * 0.8 + y * 1.3;
        double? mineral = null;
        if (mineralCover > 0)
        {
            mineral = RockRelief(rockU, rockV);
            var shade = 1 + mineral.Value * 0.4;
            r = Lerp(r, Lerp(0.42, 0.55, fine) * shade, mineralCover);
            g = Lerp(g, Lerp(0.41, 0.54, fine) * shade, mineralCover);
            b = Lerp(b, Lerp(0.40, 0.52, fine) * shade, mineralCover);
        }
        var snowy = 0.0;
        if (y > 4)
        {
            var snowLine = 8.5 + noise.Sample(x * 0.18 + 7.7, z * 0.18 - 3.3) * 1.6 - rocky * 1.2;
            snowy = Smooth(snowLine, snowLine + 1.6, y);
        }
        r = Lerp(r, 0.93, snowy);
        g = Lerp(g, 0.95, snowy);
        b = Lerp(b, 0.97, snowy);
        var material = path >= 0.5 ? Soil.Grass : Soil.Dirt;
        if (rocky >= 0.5 || exposedRock >= 0.5) material = Soil.Rock;
        if (snowy >= 0.5) material = Soil.Snow;
        var cover = (1 - mineralCover) * (1 - snowy);
        var grass = path * cover;
        var dirt = (1 - path) * cover;
        foreach (var paint in paints)
        {
            var deltaX = x + (paint.Material == Soil.Water ? 0 : fringe * 0.32) - paint.X;
            var deltaZ = z - paint.Z;
            if (Math.Abs(deltaX) >= paint.Radius || Math.Abs(deltaZ) >= paint.Radius) continue;
            var weight = 1 - Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ) / paint.Radius;
            if (weight <= 0) continue;
            weight = weight * weight * (3 - 2 * weight);
            var c = PaintColors[(int)paint.Material];
            if (weight >= 0.5) material = paint.Material;
            grass = Lerp(grass, paint.Material == Soil.Grass ? 1 : 0, weight);
            dirt = Lerp(dirt, paint.Material == Soil.Dirt ? 1 : 0, weight);
            var relief = paint.Material == Soil.Rock ? 1 + (mineral ??= RockRelief(rockU, rockV)) * 0.36 : 1;
            r = Lerp(r, c.R * relief, weight);
            g = Lerp(g, c.G * relief, weight);
            b = Lerp(b, c.B * relief, weight);
        }
        if (grass > 0 && soilDepth > 0)
        {
            var exposed = Math.Min(1, soilDepth / TerrainSurface.DigDepth) * 0.85 * grass;
            r = Lerp(r, 0.47, exposed);
            g = Lerp(g, 0.36, exposed);
            b = Lerp(b, 0.23, exposed);
            grass = Lerp(grass, 0, exposed);
            dirt = Lerp(dirt, 1, exposed);
            if (material == Soil.Grass && exposed >= 0.5) material = Soil.Dirt;
        }
        return new(r, g, b, grass, dirt, material);
    }

    private static double RockRelief(double u, double v)
    {
        var coarse = Mineral.Sample(u * 0.8, v * 0.8);
        var fine = Mineral.Sample(u * 3.3 + 19, v * 2.6 - 7);
        var strata = Mineral.Sample(u * 0.46, (v + coarse * 0.38) * 3.8);
        return coarse * 0.55 + fine * 0.25 + strata * 0.55;
    }

    internal static byte[] RockTexture(int size)
    {
        var pixels = size * size;
        var data = new byte[pixels * 8];
        var heights = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(data.AsSpan(pixels * 4));
        var angles = new (double Sin, double Cos)[size];
        for (var i = 0; i < size; i++) angles[i] = Math.SinCos((double)i / size * 2 * Math.PI);
        for (var y = 0; y < size; y++)
        {
            var (sv, cv) = angles[y];
            for (var x = 0; x < size; x++)
            {
                var (su, cu) = angles[x];
                var relief = RockRelief(cu * 5 + sv * 3, su * 3 + cv * 5);
                var value = 0.42 + relief * 0.25;
                var i = y * size + x;
                heights[i] = (float)value;
                data[i * 4] = ClampedByte(value * 268);
                data[i * 4 + 1] = ClampedByte(value * 255);
                data[i * 4 + 2] = ClampedByte(value * 233);
                data[i * 4 + 3] = 255;
            }
        }
        return data;
    }

    private static byte ClampedByte(double value) => (byte)Math.Round(Math.Clamp(value, 0, 255));
}
