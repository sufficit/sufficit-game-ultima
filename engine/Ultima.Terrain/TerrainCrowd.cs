namespace Ultima.Terrain;

internal sealed class TerrainCrowd
{
    internal const int ResultHeader = 4;
    private readonly bool[] _low;
    private readonly double[] _heights;
    private int _cutoff = 26;
    internal double[] Positions { get; }
    private int[] Result { get; }

    internal TerrainCrowd(TerrainSurface surface, int count, double[] heights)
    {
        _heights = heights;
        Positions = new double[count * 3];
        Result = new int[count + ResultHeader];
        _low = new bool[count];
        var columns = (int)Math.Ceiling(Math.Sqrt(count * 1.4));
        var rows = (int)Math.Ceiling((double)count / columns);
        for (var i = 0; i < count; i++)
        {
            var x = (i % columns - (columns - 1) / 2.0) * 2.2;
            var z = 8 + (i / columns - (rows - 1) / 2.0) * 2.2;
            Positions[i * 3] = x;
            Positions[i * 3 + 1] = surface.Height(x, z);
            Positions[i * 3 + 2] = z;
        }
    }

    internal int[] Cull(double x, double y, double z, double[] planes, int renderHeight, double fieldOfView, double fps)
    {
        _cutoff = Math.Clamp(_cutoff + (fps < 36 ? 3 : -1), 26, 64);
        var distanceCulled = 0;
        var frustumCulled = 0;
        var visible = 0;
        var scale = renderHeight / (2 * Math.Tan(fieldOfView / 2));
        for (var i = 0; i < _low.Length; i++)
        {
            var offset = i * 3;
            var height = _heights[i % _heights.Length];
            var px = Positions[offset];
            var py = Positions[offset + 1] + height / 2;
            var pz = Positions[offset + 2];
            var dx = x - px;
            var dy = y - py;
            var dz = z - pz;
            var distance = dx * dx + dy * dy + dz * dz;
            if (distance > 75 * 75)
            {
                distanceCulled++;
                continue;
            }
            if (Outside(planes, px, py, pz))
            {
                frustumCulled++;
                continue;
            }

            var limit = _cutoff + (_low[i] ? 6 : -6);
            var screenHeight = scale * height / Math.Max(1, Math.Sqrt(distance));
            var low = _low[i] = screenHeight < limit;
            Result[ResultHeader + visible++] = (i << 1) | (low ? 1 : 0);
        }
        Result[0] = visible;
        Result[1] = distanceCulled;
        Result[2] = frustumCulled;
        Result[3] = _cutoff;
        return Result;
    }

    private static bool Outside(double[] planes, double x, double y, double z)
    {
        for (var j = 0; j < planes.Length; j += 4)
            if (planes[j] * x + planes[j + 1] * y + planes[j + 2] * z + planes[j + 3] < -1.2) return true;
        return false;
    }
}
