using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace Ultima.Terrain;

[SupportedOSPlatform("browser")]
internal static partial class BrowserExports
{
    private static TerrainSurface _surface = new();
    private static TerrainVolumes _volumes = new(_surface);
    private static TerrainPalette _palette = new(_surface);

    [JSExport]
    internal static int[] CrossedAnimationMarks(double previous, double current, bool loops, double[] marks)
        => AnimationTimeline.Crossed(previous, current, loops, marks);

    [JSExport]
    internal static void Reset()
    {
        _surface = new TerrainSurface();
        _volumes = new(_surface);
        _palette = new(_surface);
    }

    [JSExport]
    internal static double Height(double x, double z) => _surface.Height(x, z);

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
    internal static byte[] TraceVolumeCamera(double x, double y, double z, double dx, double dy, double dz,
        double distance, double clearance) => _volumes.TraceCamera(x, y, z, dx, dy, dz, distance, clearance);

    [JSExport]
    internal static bool CanCoverCarve(double x, double z, double radius, double roof)
        => _surface.CanCoverCarve(x, z, radius, roof);

    [JSExport]
    internal static double VolumeFloor(double x, double z, double foot, double reach)
        => _volumes.Floor(x, z, foot, reach);

    [JSExport]
    internal static void SetPaints(double[] values) => _palette.SetPaints(values);

    [JSExport]
    internal static string SoilItem(double x, double y, double z) => _palette.MaterialLabel(x, y, z);

    [JSExport]
    internal static byte[] RasterizePalette(int cx, int cz, int samples, int gutter, bool detailed)
        => _palette.Rasterize(cx, cz, samples, gutter, detailed);

    [JSExport]
    internal static byte[] RasterizeRock(int size) => TerrainPalette.RockTexture(size);
}
