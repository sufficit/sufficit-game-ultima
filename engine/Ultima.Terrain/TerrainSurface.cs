namespace Ultima.Terrain;

internal sealed class TerrainSurface
{
    internal const int Chunk = 32;
    internal const int Cells = 64;
    internal const int DetailSplit = 4;
    internal const double Step = (double)Chunk / Cells;
    internal const double SlopeRadius = 0.7;
    internal const double DigRadius = 1;
    internal const double DigDepth = 0.48;

    private static readonly byte[] Permutations = CreateNoiseTable();
    private readonly List<Edit> _sculpts = [];
    private readonly List<Edit> _carves = [];
    private readonly Dictionary<(int X, int Z), List<int>> _buckets = [];
    private readonly List<Brush> _cuts = [];

    internal readonly record struct Brush(double X, double Z, double Radius);
    private readonly record struct Edit(Brush Brush, double Height);

    internal void SetEdits(double[] values)
    {
        _sculpts.Clear();
        _carves.Clear();
        for (var i = 0; i + 4 < values.Length; i += 5)
        {
            var edit = new Edit(new Brush(values[i + 1], values[i + 2], values[i + 3]), values[i + 4]);
            (values[i] == 0 ? _sculpts : _carves).Add(edit);
        }
    }

    internal void AddDig(double x, double z)
    {
        var cut = new Brush(x, z, DigRadius);
        var index = _cuts.Count;
        _cuts.Add(cut);
        for (var bx = Bucket(x - DigRadius); bx <= Bucket(x + DigRadius); bx++)
        for (var bz = Bucket(z - DigRadius); bz <= Bucket(z + DigRadius); bz++)
        {
            if (!_buckets.TryGetValue((bx, bz), out var ids))
                _buckets[(bx, bz)] = ids = [];
            ids.Add(index);
        }
    }

    internal double Depth(double x, double z)
    {
        if (_buckets.Count == 0 || !_buckets.TryGetValue((Bucket(x), Bucket(z)), out var ids)) return 0;
        var depth = 0.0;
        foreach (var id in ids)
        {
            var cut = _cuts[id];
            depth += DigDepth * (1 - Smooth(0.48, cut.Radius, Length(x - cut.X, z - cut.Z)));
        }
        return depth;
    }

    internal double Height(double x, double z)
    {
        var sculpted = 0.0;
        foreach (var (brush, height) in _sculpts)
        {
            var t = 1 - Length(x - brush.X, z - brush.Z) / brush.Radius;
            if (t > 0) sculpted += height * t * t * (3 - 2 * t);
        }
        var y = BaseHeight(x, z) + sculpted;
        foreach (var (brush, height) in _carves)
        {
            var floor = height + sculpted;
            var inset = brush.Radius - Length(x - brush.X, z - brush.Z);
            if (inset > 0 && y > Math.Max(height, floor))
                y = Lerp(y, floor, Smooth(0, Math.Min(1.2, brush.Radius / 4), inset));
        }
        return y - Depth(x, z);
    }

    internal double Slope(double x, double z, bool natural)
    {
        const double e = SlopeRadius;
        var dx = natural ? BaseHeight(x + e, z) - BaseHeight(x - e, z) : Height(x + e, z) - Height(x - e, z);
        var dz = natural ? BaseHeight(x, z + e) - BaseHeight(x, z - e) : Height(x, z + e) - Height(x, z - e);
        return Math.Max(Math.Abs(dx), Math.Abs(dz)) / (2 * e);
    }

    internal IEnumerable<Brush> NearbyEdits(double x0, double z0)
    {
        foreach (var edit in _sculpts) yield return edit.Brush;
        foreach (var edit in _carves) yield return edit.Brush;
        if (_buckets.Count == 0) yield break;
        var seen = new HashSet<int>();
        for (var bx = Bucket(x0 - Step); bx <= Bucket(x0 + Chunk + Step); bx++)
        for (var bz = Bucket(z0 - Step); bz <= Bucket(z0 + Chunk + Step); bz++)
        {
            if (!_buckets.TryGetValue((bx, bz), out var ids)) continue;
            foreach (var id in ids)
                if (seen.Add(id)) yield return _cuts[id];
        }
    }

    internal static double BaseHeight(double x, double z)
    {
        var radius = Length(x, z);
        var edge = radius + Noise(x * 0.07 - 51.7, z * 0.07 + 91.3) * 5;
        var rim = Smooth(24, 45, edge);
        var y = rim * Math.Sqrt(rim) * (21 + Noise(x * 0.04 + 5, z * 0.04 - 5) * 7);
        y += rim * Math.Abs(Noise(x * 0.16 + 3, z * 0.16 - 8)) * 3.5;
        y += Noise(x * 0.03, z * 0.03) * 2.2;
        y += Noise(x * 0.085 + 40.2, z * 0.085 - 17.4) * 0.6;
        y += Noise(x * 0.3 - 9.3, z * 0.3 + 23.8) * 0.15;
        var path = Noise(z * 0.09 + 3.1, 17.7) * 1.6;
        var flat = (1 - Smooth(1.5, 3.4, Math.Abs(x - path))) * (1 - Smooth(22, 34, radius));
        y = Lerp(y, Math.Max(0, y * 0.12), flat * 0.9);
        y = Lerp(0.05, y, Smooth(3.2, 9, radius));
        return Lerp(y, 0.05, Smooth(6, 3, Length(x, z + 14)) * 0.9);
    }

    private static byte[] CreateNoiseTable()
    {
        uint seed = 20261115;
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

    private static double Noise(double x, double z)
    {
        var ix = (int)Math.Floor(x);
        var iz = (int)Math.Floor(z);
        var a = Permutations[ix & 255] + (iz & 255);
        var b = Permutations[(ix & 255) + 1] + (iz & 255);
        var u = x - ix;
        var v = z - iz;
        var fade = u * u * (3 - 2 * u);
        return Lerp(
            Lerp(Gradient(Permutations[a], u, v), Gradient(Permutations[b], u - 1, v), fade),
            Lerp(Gradient(Permutations[a + 1], u, v - 1), Gradient(Permutations[b + 1], u - 1, v - 1), fade),
            v * v * (3 - 2 * v));
    }

    private static double Gradient(int bits, double x, double z)
        => ((bits & 1) != 0 ? x : -x) + ((bits & 2) != 0 ? z : -z);
    private static int Bucket(double value) => (int)Math.Floor(value / 2);
    private static double Length(double x, double z) => Math.Sqrt(x * x + z * z);
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static double Smooth(double a, double b, double value)
    {
        var t = Math.Clamp((value - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
