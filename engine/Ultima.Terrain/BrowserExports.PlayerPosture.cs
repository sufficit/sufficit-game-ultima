using System.Runtime.InteropServices.JavaScript;

namespace Ultima.Terrain;

internal static partial class BrowserExports
{
    private static PlayerPosture _posture = new();
    private static readonly double[] PostureFrame = new double[9];

    [JSExport]
    internal static void ResetPlayerPosture()
    {
        _posture = new();
        Balance.ResetDynamics();
        LimbPendulums.Reset();
        Array.Clear(PostureFrame);
        _movement.SpeedScale = 1;
    }

    [JSExport]
    internal static double[] AdvancePlayerPosture(double x, double z, double support, double delta, double phase, double weight,
        bool hasTunnels)
    {
        var ceiling = hasTunnels && _volumes.Near(x, z) ? support : _movement.JumpCeiling(x, z, support);
        _posture.Advance(PostureFrame, support, ceiling, delta, phase, weight);
        _movement.SpeedScale = _posture.SpeedScale;
        return PostureFrame;
    }

    private static bool HandlePostureKey(string code, bool down)
    {
        if (code is not ("Space" or "KeyC" or "KeyX" or "KeyZ" or "KeyF")) return false;
        if (!down || !_movement.NaturalGait) return true;
        if (code == "Space")
        {
            var fallen = _posture.Target == PlayerPosture.Kind.Fallen;
            _posture.Jump();
            if (fallen && _posture.Target == PlayerPosture.Kind.Standing)
            {
                Balance.ResetDynamics();
                LimbPendulums.Reset();
            }
        }
        else _posture.Toggle(code switch
        {
            "KeyC" => PlayerPosture.Kind.Crouching,
            "KeyX" => PlayerPosture.Kind.Crawling,
            "KeyZ" => PlayerPosture.Kind.Lying,
            _ => PlayerPosture.Kind.Sitting
        });
        _movement.SpeedScale = _posture.SpeedScale;
        return true;
    }
}
