using static Ultima.Terrain.TerrainNoise;

namespace Ultima.Terrain;

internal sealed class TerrainSurface(double? flatHeight = null)
{
    internal const int Chunk = 32;
    internal const int Cells = 64;
    internal const int DetailSplit = 4;
    internal const double Step = (double)Chunk / Cells;
    internal const double SlopeRadius = 0.7;
    internal const double DigRadius = 1;
    internal const double DigDepth = 0.48;

    internal static readonly TerrainNoise Noise = new(20261115);
    private readonly List<Edit> _sculpts = [];
    private readonly List<Edit> _carves = [];
    private readonly Dictionary<(int X, int Z), List<int>> _buckets = [];
    private readonly List<Brush> _cuts = [];

    internal readonly record struct Brush(double X, double Z, double Radius);
    private readonly record struct Edit(Brush Brush, double Height);

    internal int Revision { get; private set; }

    internal void SetEdits(double[] values)
    {
        Revision++;
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
        Revision++;
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

    internal double Height(double x, double z) => Height(x, z, Depth(x, z));

    internal double Height(double x, double z, double depth)
        => HeightFromBase(x, z, flatHeight ?? BaseHeight(x, z), depth);

    internal double HeightFromBase(double x, double z, double natural)
        => HeightFromBase(x, z, natural, Depth(x, z));

    private double HeightFromBase(double x, double z, double natural, double depth)
    {
        var sculpted = 0.0;
        foreach (var (brush, height) in _sculpts)
        {
            var t = 1 - Length(x - brush.X, z - brush.Z) / brush.Radius;
            if (t > 0) sculpted += height * t * t * (3 - 2 * t);
        }
        var y = natural + sculpted;
        foreach (var (brush, height) in _carves)
        {
            var floor = height + sculpted;
            var inset = brush.Radius - Length(x - brush.X, z - brush.Z);
            if (inset > 0 && y > Math.Max(height, floor))
                y = Lerp(y, floor, Smooth(0, Math.Min(1.2, brush.Radius / 4), inset));
        }
        return y - depth;
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

    internal bool CanCoverCarve(double x, double z, double radius, double roof)
    {
        for (var i = 0; i < 24; i++)
        {
            var angle = i * Math.Tau / 24;
            var (sin, cos) = Math.SinCos(angle);
            if (roof + 0.5 > Height(x + cos * radius, z + sin * radius)) return false;
        }
        return true;
    }

    internal double CarveInset(double x, double z)
    {
        var inset = double.NegativeInfinity;
        foreach (var (brush, _) in _carves)
            inset = Math.Max(inset, brush.Radius - Length(x - brush.X, z - brush.Z));
        return inset;
    }

    internal static double PathOffset(double z) => Noise.Sample(z * 0.09 + 3.1, 17.7) * 1.6;

    internal static double BaseHeight(double x, double z)
    {
        var radius = Length(x, z);
        if (radius <= 3.2) return 0.05;

        var edge = radius + Noise.Sample(x * 0.07 - 51.7, z * 0.07 + 91.3) * 5;
        var rim = Smooth(24, 45, edge);
        var y = 0.0;
        if (rim > 0)
        {
            y = rim * Math.Sqrt(rim) * (21 + Noise.Sample(x * 0.04 + 5, z * 0.04 - 5) * 7);
            y += rim * Math.Abs(Noise.Sample(x * 0.16 + 3, z * 0.16 - 8)) * 3.5;
        }
        y += Noise.Sample(x * 0.03, z * 0.03) * 2.2;
        y += Noise.Sample(x * 0.085 + 40.2, z * 0.085 - 17.4) * 0.6;
        y += Noise.Sample(x * 0.3 - 9.3, z * 0.3 + 23.8) * 0.15;
        if (radius < 34)
        {
            var flat = (1 - Smooth(1.5, 3.4, Math.Abs(x - PathOffset(z)))) * (1 - Smooth(22, 34, radius));
            y = Lerp(y, Math.Max(0, y * 0.12), flat * 0.9);
        }
        if (radius < 9) y = Lerp(0.05, y, Smooth(3.2, 9, radius));
        else y = Lerp(0.05, y, 1); // Mantém o arredondamento do gerador original.
        return Math.Abs(x) < 6 && Math.Abs(z + 14) < 6
            ? Lerp(y, 0.05, Smooth(6, 3, Length(x, z + 14)) * 0.9)
            : y;
    }

    private static int Bucket(double value) => (int)Math.Floor(value / 2);
    private static double Length(double x, double z) => Math.Sqrt(x * x + z * z);
}
