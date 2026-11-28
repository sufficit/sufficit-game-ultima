using System.Runtime.InteropServices.JavaScript;

namespace Ultima.Terrain;

internal static partial class BrowserExports
{
    private static PlayerOutfit _outfit = new();

    [JSExport]
    internal static int[] ChoosePlayerOutfit(string identity)
    {
        _outfit = new();
        return PlayerOutfit.Select(identity);
    }

    [JSExport]
    internal static double AdvancePlayerAccessory(double speed, double wind, double yaw, double delta)
        => _outfit.Advance(speed, wind, yaw, delta);
}
