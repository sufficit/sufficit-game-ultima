namespace Ultima.Terrain;

// A árvore agrupa as projeções no chão das caixas estáticas. O renderizador
// reutiliza as malhas de uma região e faz a colisão tridimensional exata.
internal sealed class PlayerCameraObstacles
{
    private Entry[] _entries = [];
    private readonly List<Node> _nodes = [];
    private readonly List<int> _hits = [];

    internal void Set(ReadOnlySpan<double> bounds, double padding)
    {
        _entries = new Entry[bounds.Length / 4];
        _nodes.Clear();
        for (var i = 0; i < _entries.Length; i++)
        {
            var j = i * 4;
            _entries[i] = new(i, new(bounds[j] - padding, bounds[j + 1] - padding,
                bounds[j + 2] + padding, bounds[j + 3] + padding));
        }
        if (_entries.Length > 0) Build(0, _entries.Length);
    }

    internal int[] Query(double minX, double minZ, double maxX, double maxZ)
    {
        if (_nodes.Count == 0) return [];
        _hits.Clear();
        var area = new Bounds(minX, minZ, maxX, maxZ);
        Visit(0, area);
        return _hits.ToArray();
    }

    private void Visit(int index, Bounds area)
    {
        var node = _nodes[index];
        if (!node.Box.Intersects(area)) return;
        if (node.Count == 0)
        {
            Visit(node.First, area);
            Visit(node.Right, area);
            return;
        }
        for (var i = node.First; i < node.First + node.Count; i++)
            if (_entries[i].Box.Intersects(area)) _hits.Add(_entries[i].Id);
    }

    private int Build(int first, int count)
    {
        var box = _entries[first].Box;
        for (var i = first + 1; i < first + count; i++) box = box.Union(_entries[i].Box);
        var index = _nodes.Count;
        _nodes.Add(new(box, first, count, 0));
        if (count <= 4) return index;

        var byX = box.MaxX - box.MinX >= box.MaxZ - box.MinZ;
        Array.Sort(_entries, first, count, byX ? ByX : ByZ);
        var half = count / 2;
        var left = Build(first, half);
        var right = Build(first + half, count - half);
        _nodes[index] = new(box, left, 0, right);
        return index;
    }

    private static readonly IComparer<Entry> ByX = Comparer<Entry>.Create((a, b) =>
        (a.Box.MinX + a.Box.MaxX).CompareTo(b.Box.MinX + b.Box.MaxX));
    private static readonly IComparer<Entry> ByZ = Comparer<Entry>.Create((a, b) =>
        (a.Box.MinZ + a.Box.MaxZ).CompareTo(b.Box.MinZ + b.Box.MaxZ));

    private readonly record struct Entry(int Id, Bounds Box);
    private readonly record struct Node(Bounds Box, int First, int Count, int Right);

    private readonly record struct Bounds(double MinX, double MinZ, double MaxX, double MaxZ)
    {
        internal Bounds Union(Bounds other) => new(Math.Min(MinX, other.MinX), Math.Min(MinZ, other.MinZ),
            Math.Max(MaxX, other.MaxX), Math.Max(MaxZ, other.MaxZ));

        internal bool Intersects(Bounds area) =>
            MinX <= area.MaxX && MaxX >= area.MinX && MinZ <= area.MaxZ && MaxZ >= area.MinZ;
    }
}
