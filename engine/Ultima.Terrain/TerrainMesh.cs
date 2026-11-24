using System.Runtime.InteropServices;
using static Ultima.Terrain.TerrainSurface;

namespace Ultima.Terrain;

internal static class TerrainMesh
{
    private const int Row = Cells + 1;
    private const int FineCells = Cells * DetailSplit;
    private const int MaskSide = Cells + 2;

    internal static byte[] Build(TerrainSurface surface, int cx, int cz)
    {
        var x0 = cx * Chunk - Chunk / 2;
        var z0 = cz * Chunk - Chunk / 2;
        var mask = DetailMask(surface, x0, z0);
        var positions = new List<float>(Row * Row * 3);
        var normals = new List<float>(Row * Row * 3);
        var uvs = new List<float>(Row * Row * 2);
        var indices = new List<int>(Cells * Cells * 6);
        var starts = mask is null ? [] : new int[Cells * Cells + 1];
        var extra = new Dictionary<int, int>();
        var heights = new double[(Row + 2) * (Row + 2)];
        var stride = Row + 2;
        for (var j = -1; j <= Row; j++)
        for (var i = -1; i <= Row; i++)
            heights[(j + 1) * stride + i + 1] = surface.Height(x0 + i * Step, z0 + j * Step);

        for (var j = 0; j < Row; j++)
        for (var i = 0; i < Row; i++)
        {
            var h = (j + 1) * stride + i + 1;
            AppendVertex(x0 + i * Step, z0 + j * Step, heights[h],
                heights[h + 1] - heights[h - 1], heights[h + stride] - heights[h - stride]);
        }

        Span<int> edge = stackalloc int[DetailSplit * 4];
        for (var j = 0; j < Cells; j++)
        for (var i = 0; i < Cells; i++)
        {
            if (starts.Length != 0) starts[j * Cells + i] = indices.Count;
            var a = j * Row + i;
            var b = a + 1;
            var c = a + Row;
            var d = c + 1;
            var x = i * DetailSplit;
            var z = j * DetailSplit;
            if (IsFine(i, j))
            {
                for (var dz = 0; dz < DetailSplit; dz++)
                for (var dx = 0; dx < DetailSplit; dx++)
                {
                    var u = x + dx;
                    var v = z + dz;
                    Quad(Vertex(u, v), Vertex(u + 1, v), Vertex(u, v + 1), Vertex(u + 1, v + 1), ((dx + dz) & 1) != 0);
                }
                continue;
            }

            var top = IsFine(i, j - 1);
            var right = IsFine(i + 1, j);
            var bottom = IsFine(i, j + 1);
            var left = IsFine(i - 1, j);
            if (!(top || right || bottom || left))
            {
                Quad(a, b, c, d, ((i + j) & 1) != 0);
                continue;
            }

            // A célula de transição usa os mesmos pontos da borda refinada.
            var count = 0;
            edge[count++] = a;
            for (var t = 1; top && t < DetailSplit; t++) edge[count++] = Vertex(x + t, z);
            edge[count++] = b;
            for (var t = 1; right && t < DetailSplit; t++) edge[count++] = Vertex(x + DetailSplit, z + t);
            edge[count++] = d;
            for (var t = DetailSplit - 1; bottom && t > 0; t--) edge[count++] = Vertex(x + t, z + DetailSplit);
            edge[count++] = c;
            for (var t = DetailSplit - 1; left && t > 0; t--) edge[count++] = Vertex(x, z + t);
            var center = Vertex(x + DetailSplit / 2, z + DetailSplit / 2);
            for (var p = 0; p < count; p++) Triangle(center, edge[p], edge[(p + 1) % count]);
        }
        if (starts.Length != 0) starts[^1] = indices.Count;
        return Pack(positions, normals, uvs, indices, starts);

        bool IsFine(int i, int j) => mask is not null && mask[(j + 1) * MaskSide + i + 1];

        void AppendVertex(double x, double z, double height, double dx, double dz)
        {
            var length = Math.Sqrt(dx * dx + 4 * Step * Step + dz * dz);
            positions.Add((float)x);
            positions.Add((float)height);
            positions.Add((float)z);
            normals.Add((float)(-dx / length));
            normals.Add((float)(2 * Step / length));
            normals.Add((float)(-dz / length));
            uvs.Add((float)((x - x0) / Chunk));
            uvs.Add((float)((z - z0) / Chunk));
        }

        int Vertex(int ix, int iz)
        {
            if (ix % DetailSplit == 0 && iz % DetailSplit == 0)
                return iz / DetailSplit * Row + ix / DetailSplit;
            var key = iz * (FineCells + 1) + ix;
            if (extra.TryGetValue(key, out var existing)) return existing;
            var x = x0 + ix * Step / DetailSplit;
            var z = z0 + iz * Step / DetailSplit;
            var index = positions.Count / 3;
            AppendVertex(x, z, surface.Height(x, z),
                surface.Height(x + Step, z) - surface.Height(x - Step, z),
                surface.Height(x, z + Step) - surface.Height(x, z - Step));
            extra.Add(key, index);
            return index;
        }

        void Triangle(int a, int b, int c)
        {
            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }

        void Quad(int a, int b, int c, int d, bool alternate)
        {
            if (alternate) { Triangle(c, a, b); Triangle(c, b, d); }
            else { Triangle(d, a, b); Triangle(c, a, d); }
        }
    }

    private static bool[]? DetailMask(TerrainSurface surface, double x0, double z0)
    {
        bool[]? mask = null;
        foreach (var cut in surface.NearbyEdits(x0, z0))
        {
            var minX = Math.Max(-1, (int)Math.Floor((cut.X - cut.Radius - x0) / Step));
            var maxX = Math.Min(Cells, (int)Math.Floor((cut.X + cut.Radius - x0) / Step));
            var minZ = Math.Max(-1, (int)Math.Floor((cut.Z - cut.Radius - z0) / Step));
            var maxZ = Math.Min(Cells, (int)Math.Floor((cut.Z + cut.Radius - z0) / Step));
            for (var j = minZ; j <= maxZ; j++)
            for (var i = minX; i <= maxX; i++)
            {
                var dx = Math.Max(0, Math.Abs(cut.X - x0 - (i + 0.5) * Step) - Step / 2);
                var dz = Math.Max(0, Math.Abs(cut.Z - z0 - (j + 0.5) * Step) - Step / 2);
                if (dx * dx + dz * dz >= cut.Radius * cut.Radius) continue;
                mask ??= new bool[MaskSide * MaskSide];
                mask[(j + 1) * MaskSide + i + 1] = true;
            }
        }
        return mask;
    }

    // Cabeçalho Int32: vértices, índices, contagem da tabela de células.
    // Depois posições, normais e UVs Float32, seguidos de índices e células Int32.
    internal static byte[] Pack(List<float> positions, List<float> normals, List<float> uvs, List<int> indices, int[] cells)
    {
        var bytes = new byte[4 * (3 + positions.Count + normals.Count + uvs.Count + indices.Count + cells.Length)];
        var words = MemoryMarshal.Cast<byte, int>(bytes.AsSpan());
        words[0] = positions.Count / 3;
        words[1] = indices.Count;
        words[2] = cells.Length;
        var values = MemoryMarshal.Cast<int, float>(words[3..]);
        CollectionsMarshal.AsSpan(positions).CopyTo(values);
        CollectionsMarshal.AsSpan(normals).CopyTo(values[positions.Count..]);
        CollectionsMarshal.AsSpan(uvs).CopyTo(values[(positions.Count + normals.Count)..]);
        var indexStart = 3 + positions.Count + normals.Count + uvs.Count;
        CollectionsMarshal.AsSpan(indices).CopyTo(words[indexStart..]);
        cells.AsSpan().CopyTo(words[(indexStart + indices.Count)..]);
        return bytes;
    }
}
