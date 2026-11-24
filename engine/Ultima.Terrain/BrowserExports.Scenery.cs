using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;

namespace Ultima.Terrain;

internal static partial class BrowserExports
{
    private static readonly double[] ScatterResult = new double[TerrainScatter.Header + TerrainScatter.Batch * TerrainScatter.Channels];

    [JSExport]
    internal static double[] ScatterBatch(double state, int remaining, int attempts, bool tree, bool workshop,
        double guardianX, double guardianZ, double minRadius, double maxAltitude, double maxSlope,
        double minScale, double maxScale, double sink)
    {
        TerrainScatter.Fill(ScatterResult, _surface, (uint)state, remaining, attempts, tree, workshop,
            guardianX, guardianZ, minRadius, maxAltitude, maxSlope, minScale, maxScale, sink);
        return ScatterResult;
    }

    [JSExport]
    internal static byte[] RasterizeBark(int size) => TerrainSceneryTextures.Bark(size);

    [JSExport]
    internal static byte[] RasterizeFoliage(int size) => TerrainSceneryTextures.Foliage(size);

    [JSExport]
    internal static byte[] RasterizeWater(int size) => TerrainSceneryTextures.Water(size);

    [JSExport]
    internal static byte[] RasterizeBump(byte[] height, int size, double strength)
        => TerrainSceneryTextures.Bump(MemoryMarshal.Cast<byte, float>(height), size, strength);

    [JSExport]
    internal static byte[] PrepareMesh(byte[] positions, double[] effects)
        => TerrainMeshEffects.Prepare(positions, effects);

    private static TerrainCrowd? _crowd;

    [JSExport]
    internal static double[] CreateCrowd(int count, double[] heights)
        => (_crowd = new(_surface, count, heights)).Positions;

    [JSExport]
    internal static int[] CullCrowd(double x, double y, double z, double[] planes, int renderHeight,
        double fieldOfView, double fps)
        => _crowd!.Cull(x, y, z, planes, renderHeight, fieldOfView, fps);
}
