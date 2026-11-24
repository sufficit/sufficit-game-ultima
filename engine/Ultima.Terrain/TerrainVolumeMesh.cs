using System.Numerics;
using static Ultima.Terrain.TerrainSurface;

namespace Ultima.Terrain;

/// <summary>Marching cubes com resolução das faces ambíguas pela amostra do centro.</summary>
internal static class TerrainVolumeMesh
{
    private static readonly (int X, int Y, int Z)[] Corners =
    [
        (0,0,0), (1,0,0), (1,1,0), (0,1,0),
        (0,0,1), (1,0,1), (1,1,1), (0,1,1)
    ];
    private static readonly (int A, int B)[] Edges =
    [
        (0,1), (1,2), (2,3), (3,0), (4,5), (5,6),
        (6,7), (7,4), (0,4), (1,5), (2,6), (3,7)
    ];
    private static readonly int[][] Faces =
    [
        [0,1,2,3], [4,5,6,7], [0,9,4,8],
        [2,10,6,11], [3,11,7,8], [1,10,5,9]
    ];

    internal static byte[] Build(TerrainSurface surface, TerrainVolumes volumes,
        TerrainVolumes.Tunnel[] tunnels, int cx, int cz)
    {
        var x0 = cx * Chunk - Chunk / 2;
        var z0 = cz * Chunk - Chunk / 2;
        var heights = new double[(Cells + 1) * (Cells + 1)];
        var sky = double.NegativeInfinity;
        var bed = tunnels.Min(tunnel => (double)tunnel.MinY);
        for (var j = 0; j <= Cells; j++)
        for (var i = 0; i <= Cells; i++)
        {
            var height = surface.Height(x0 + i * Step, z0 + j * Step);
            heights[j * (Cells + 1) + i] = height;
            sky = Math.Max(sky, height);
            bed = Math.Min(bed, height);
        }
        var y0 = Math.Floor(bed / Step) * Step - Step;
        var layers = (int)Math.Ceiling((sky - y0) / Step) + 1;
        var lower = new double[heights.Length];
        var upper = new double[heights.Length];
        var positions = new List<float>();
        var normals = new List<float>();
        var uvs = new List<float>();
        var indices = new List<int>();
        var values = new double[8];
        var vertices = new int[12];
        var links = new int[24];
        var faceHits = new int[4];
        var loop = new int[12];
        var cache = new Dictionary<(int X, int Y, int Z, int Axis), int>();
        SampleLayer(y0, lower);
        for (var k = 0; k < layers; k++)
        {
            var y = y0 + k * Step;
            SampleLayer(y + Step, upper);
            for (var j = 0; j < Cells; j++)
            for (var i = 0; i < Cells; i++)
            {
                var solid = 0;
                for (var n = 0; n < 8; n++)
                {
                    var (dx, dy, dz) = Corners[n];
                    values[n] = (dy == 0 ? lower : upper)[(j + dz) * (Cells + 1) + i + dx];
                    if (values[n] > 0) solid++;
                }
                if (solid is 0 or 8) continue;
                BuildCube(i, k, j);
            }
            (lower, upper) = (upper, lower);
        }
        return TerrainMesh.Pack(positions, normals, uvs, indices, []);

        void SampleLayer(double y, double[] into)
        {
            for (var j = 0; j <= Cells; j++)
            for (var i = 0; i <= Cells; i++)
            {
                var index = j * (Cells + 1) + i;
                var h = heights[index] - y;
                into[index] = Math.Min(h, volumes.VoidDistance(x0 + i * Step, y, z0 + j * Step, tunnels)) - 1e-5;
            }
        }

        void BuildCube(int i, int k, int j)
        {
            Array.Fill(vertices, -1);
            Array.Fill(links, -1);
            for (var e = 0; e < Edges.Length; e++)
            {
                var (a, b) = Edges[e];
                if ((values[a] > 0) == (values[b] > 0)) continue;
                var p = Corners[a];
                var q = Corners[b];
                var axis = p.X != q.X ? 0 : p.Y != q.Y ? 1 : 2;
                var key = (i + Math.Min(p.X, q.X), k + Math.Min(p.Y, q.Y), j + Math.Min(p.Z, q.Z), axis);
                if (!cache.TryGetValue(key, out var index))
                {
                    var t = values[a] / (values[a] - values[b]);
                    var x = x0 + (i + p.X + (q.X - p.X) * t) * Step;
                    var y = y0 + (k + p.Y + (q.Y - p.Y) * t) * Step;
                    var z = z0 + (j + p.Z + (q.Z - p.Z) * t) * Step;
                    index = positions.Count / 3;
                    var normal = Normal(x, y, z);
                    positions.AddRange([(float)x, (float)y, (float)z]);
                    normals.AddRange([normal.X, normal.Y, normal.Z]);
                    uvs.AddRange([(float)((x - x0) / Chunk), (float)((z - z0) / Chunk)]);
                    cache.Add(key, index);
                }
                vertices[e] = index;
            }

            foreach (var face in Faces)
            {
                var count = 0;
                foreach (var edge in face)
                    if (vertices[edge] >= 0) faceHits[count++] = edge;
                if (count == 2) Connect(faceHits[0], faceHits[1]);
                if (count != 4) continue;

                // O centro ponderado usa somente os quatro vértices da face.
                // Ambos os chunks veem os mesmos valores, independentemente da orientação.
                var sum = 0.0;
                foreach (var edge in face)
                {
                    var (a, b) = Edges[edge];
                    sum += values[a] + values[b];
                }
                for (var n = 0; n < 4; n++)
                {
                    var a = face[n];
                    var b = face[(n + 1) % 4];
                    var common = SharedCorner(Edges[a], Edges[b]);
                    if ((values[common] > 0) != (sum > 0)) Connect(a, b);
                }
            }

            for (var edge = 0; edge < Edges.Length; edge++)
            {
                if (vertices[edge] < 0 || links[edge * 2] < 0) continue;
                var count = 0;
                var current = edge;
                var previous = -1;
                while (current >= 0 && vertices[current] >= 0)
                {
                    loop[count++] = vertices[current];
                    vertices[current] = -1;
                    var next = links[current * 2];
                    if (next == previous) next = links[current * 2 + 1];
                    previous = current;
                    current = next;
                }
                for (var n = 2; n < count; n++) AddTriangle(loop[0], loop[n - 1], loop[n]);
            }
        }

        void Connect(int a, int b)
        {
            links[a * 2 + (links[a * 2] < 0 ? 0 : 1)] = b;
            links[b * 2 + (links[b * 2] < 0 ? 0 : 1)] = a;
        }

        Vector3 Normal(double x, double y, double z)
        {
            if (volumes.VoidDistance(x, y, z, tunnels) > 0.10)
            {
                var dx = surface.Height(x + Step, z) - surface.Height(x - Step, z);
                var dz = surface.Height(x, z + Step) - surface.Height(x, z - Step);
                return Vector3.Normalize(new((float)-dx, (float)(2 * Step), (float)-dz));
            }
            const double e = 0.025;
            var v = new Vector3((float)(Density(x - e, y, z) - Density(x + e, y, z)),
                (float)(Density(x, y - e, z) - Density(x, y + e, z)),
                (float)(Density(x, y, z - e) - Density(x, y, z + e)));
            return v.LengthSquared() > 1e-8f ? Vector3.Normalize(v) : Vector3.UnitY;
        }

        double Density(double x, double y, double z)
            => Math.Min(surface.Height(x, z) - y, volumes.VoidDistance(x, y, z, tunnels));

        void AddTriangle(int a, int b, int c)
        {
            var cross = Vector3.Cross(Point(b, positions) - Point(a, positions), Point(c, positions) - Point(a, positions));
            if (cross.LengthSquared() < 1e-12f) return;
            var face = Point(a, normals) + Point(b, normals) + Point(c, normals);
            // O Babylon usa winding no sentido horário para a frente da malha.
            if (Vector3.Dot(cross, face) > 0) (b, c) = (c, b);
            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }
    }

    private static int SharedCorner((int A, int B) x, (int A, int B) y)
        => x.A == y.A || x.A == y.B ? x.A : x.B;

    private static Vector3 Point(int index, List<float> values)
        => new(values[index * 3], values[index * 3 + 1], values[index * 3 + 2]);
}
