using System.Numerics;

namespace Ultima.Terrain;

internal sealed class TerrainVolumes(TerrainSurface surface)
{
    private readonly List<Tunnel> _tunnels = [];

    internal void Set(double[] values)
    {
        _tunnels.Clear();
        for (var i = 0; i + 2 <= values.Length;)
        {
            var radius = (float)values[i++];
            var count = (int)values[i++];
            if (radius is < 1.5f or > 3f || count is < 2 or > 8 || i + count * 3 > values.Length) break;
            var points = new Vector3[count];
            for (var j = 0; j < count; j++)
                points[j] = new((float)values[i++], (float)values[i++], (float)values[i++]);
            _tunnels.Add(new(points, radius));
        }
    }

    internal Tunnel[] InChunk(int cx, int cz)
    {
        if (_tunnels.Count == 0) return [];
        var x = cx * TerrainSurface.Chunk;
        var z = cz * TerrainSurface.Chunk;
        const double half = TerrainSurface.Chunk / 2;
        return _tunnels.Where(tunnel => tunnel.Intersects(x - half, z - half, x + half, z + half)).ToArray();
    }

    internal double VoidDistance(double x, double y, double z, IReadOnlyList<Tunnel>? tunnels = null)
    {
        var distance = double.PositiveInfinity;
        foreach (var tunnel in tunnels ?? _tunnels)
        {
            if (y < tunnel.MinY || y > tunnel.MaxY || !tunnel.Intersects(x, z, x, z)) continue;
            distance = Math.Min(distance, tunnel.Distance(x, y, z));
        }
        return distance;
    }

    internal bool Near(double x, double z) =>
        _tunnels.Any(tunnel => tunnel.Intersects(x, z, x, z));

    internal bool Blocks(double x, double y, double z, double clearance)
        => surface.Height(x, z) - y > -clearance && VoidDistance(x, y, z) > -clearance;

    internal double Floor(double x, double z, double foot, double reach)
    {
        var surfaceY = surface.Height(x, z);
        if (!Near(x, z)) return surfaceY;
        var start = Math.Min(surfaceY + 0.01, foot + reach);
        var wasAir = !Blocks(x, start, z, 0);
        for (var y = start - 0.1; y >= start - 14; y -= 0.1)
        {
            var solid = Blocks(x, y, z, 0);
            if (!wasAir && !solid) wasAir = true;
            else if (wasAir && solid)
            {
                var low = y;
                var high = y + 0.1;
                for (var step = 0; step < 6; step++)
                {
                    var mid = (low + high) * 0.5;
                    if (Blocks(x, mid, z, 0)) low = mid;
                    else high = mid;
                }
                return (low + high) * 0.5;
            }
        }
        return surfaceY;
    }

    internal sealed class Tunnel(Vector3[] points, float radius)
    {
        private readonly float _minX = points.Min(p => p.X) - radius - 0.5f;
        private readonly float _maxX = points.Max(p => p.X) + radius + 0.5f;
        private readonly float _minZ = points.Min(p => p.Z) - radius - 0.5f;
        private readonly float _maxZ = points.Max(p => p.Z) + radius + 0.5f;
        internal readonly float MinY = points.Min(p => p.Y) - radius - 0.5f;
        internal readonly float MaxY = points.Max(p => p.Y) + radius + 0.5f;

        internal bool Intersects(double minX, double minZ, double maxX, double maxZ)
            => minX <= _maxX && maxX >= _minX && minZ <= _maxZ && maxZ >= _minZ;

        // União de cápsulas entre os pontos do trajeto. A seção tem piso firme
        // na parte inferior e arredondamento nas paredes e no teto.
        internal double Distance(double x, double y, double z)
        {
            var best = double.PositiveInfinity;
            for (var i = 1; i < points.Length; i++)
            {
                var a = points[i - 1];
                var b = points[i];
                var dx = b.X - a.X;
                var dz = b.Z - a.Z;
                var along = Math.Clamp(((x - a.X) * dx + (z - a.Z) * dz) / Math.Max(1e-9, dx * dx + dz * dz), 0, 1);
                var u = x - a.X - dx * along;
                var v = z - a.Z - dz * along;
                var h = y - a.Y - (b.Y - a.Y) * along;
                var curved = Math.Sqrt(u * u + v * v + h * h) - radius;
                var flatFloor = -h - radius * 0.65;
                best = Math.Min(best, Math.Max(curved, flatFloor));
            }
            return best;
        }
    }
}
