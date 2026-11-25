using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Ultima.Terrain;

namespace Ultima.Environments;

internal static partial class EnvironmentExports
{
    private static readonly PlayerInput Input = new();
    private static readonly double[] Intent = new double[7];
    private static readonly double[] Pose = new double[56];
    private static SceneWalk? _walk;

    internal static void Configure(SceneSettings scene)
    {
        Input.Clear();
        _walk = OperatingSystem.IsBrowser() && scene.Walk is { } walk ? new SceneWalk(walk, scene.Boxes) : null;
        if (OperatingSystem.IsBrowser() && _walk is not null) BrowserExports.ConfigureInteriorPlayer();
    }

    [SupportedOSPlatform("browser")]
    [JSExport]
    internal static void Key(string code, bool down) => Input.SetKey(code, down);

    [SupportedOSPlatform("browser")]
    [JSExport]
    internal static void ClearInput()
    {
        Input.Clear();
        _walk?.StopAnimation();
    }

    [SupportedOSPlatform("browser")]
    [JSExport]
    internal static int CycleCameraView() => _walk?.CycleCameraView() ?? 0;

    [SupportedOSPlatform("browser")]
    [JSExport]
    internal static double[] WalkFrame(double side, double forward, double lookX, double lookY, double dx, double dy, double dt)
    {
        Input.Read(Intent);
        var stick = PlayerMovement.Stick(side, forward);
        var look = PlayerMovement.Stick(lookX, lookY);
        _walk?.Advance(Pose, Intent[0] + stick.X, Intent[1] + stick.Y, Intent[6] != 0,
            dx * .003 + (Intent[4] + look.X) * 1.8 * dt, dy * .003 + (Intent[5] + look.Y) * 1.6 * dt, dt);
        return Pose;
    }
}
