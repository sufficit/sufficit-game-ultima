using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace Ultima.Terrain;

[SupportedOSPlatform("browser")]
internal static partial class BrowserExports
{
    private static TerrainSurface _surface = new();
    private static TerrainVolumes _volumes = new(_surface);

    [JSExport]
    internal static int[] CrossedAnimationMarks(double previous, double current, bool loops, double[] marks)
        => AnimationTimeline.Crossed(previous, current, loops, marks);

    [JSExport]
    internal static void Reset()
    {
        _surface = new TerrainSurface();
        _volumes = new(_surface);
    }

    [JSExport]
    internal static double BaseHeight(double x, double z) => TerrainSurface.BaseHeight(x, z);

    [JSExport]
    internal static double Height(double x, double z) => _surface.Height(x, z);

    [JSExport]
    internal static double Depth(double x, double z) => _surface.Depth(x, z);

    [JSExport]
    internal static double Slope(double x, double z, bool natural) => _surface.Slope(x, z, natural);

    [JSExport]
    internal static void AddDig(double x, double z) => _surface.AddDig(x, z);

    [JSExport]
    internal static void SetEdits(double[] values) => _surface.SetEdits(values);

    [JSExport]
    internal static byte[] Build(int cx, int cz)
    {
        var tunnels = _volumes.InChunk(cx, cz);
        return tunnels.Length == 0 ? TerrainMesh.Build(_surface, cx, cz)
            : TerrainVolumeMesh.Build(_surface, _volumes, tunnels, cx, cz);
    }

    [JSExport]
    internal static void SetVolumes(double[] values) => _volumes.Set(values);

    [JSExport]
    internal static bool HasVolume(int cx, int cz) => _volumes.InChunk(cx, cz).Length != 0;

    [JSExport]
    internal static bool NearVolume(double x, double z) => _volumes.Near(x, z);

    [JSExport]
    internal static bool TerrainBlocks(double x, double y, double z, double clearance)
        => _volumes.Blocks(x, y, z, clearance);

    [JSExport]
    internal static double VolumeFloor(double x, double z, double foot, double reach)
        => _volumes.Floor(x, z, foot, reach);

    [JSExport]
    internal static double[] SamplePalette(int cx, int cz, int samples, int gutter)
    {
        var side = samples + 2 * gutter;
        var data = new double[side * side * 3];
        var x0 = cx * TerrainSurface.Chunk - TerrainSurface.Chunk / 2;
        var z0 = cz * TerrainSurface.Chunk - TerrainSurface.Chunk / 2;
        var index = 0;
        for (var j = 0; j < side; j++)
        for (var i = 0; i < side; i++)
        {
            var x = x0 + (i - gutter + 0.5) * TerrainSurface.Chunk / samples;
            var z = z0 + (j - gutter + 0.5) * TerrainSurface.Chunk / samples;
            data[index++] = _surface.Height(x, z);
            data[index++] = _surface.Slope(x, z, false);
            data[index++] = _surface.Depth(x, z);
        }
        return data;
    }
}
