using System.Numerics;
using System.Runtime.InteropServices.JavaScript;

namespace Ultima.Terrain;

internal static partial class BrowserExports
{
    private static readonly PlayerBalance Balance = new();
    private static readonly PlayerLimbPendulums LimbPendulums = new();
    private static readonly double[] BalanceSnapshot = new double[10];

    [JSExport]
    internal static bool SetPlayerBalanceProfile(string profile)
    {
        switch (profile)
        {
            case "normal": Balance.Mode = PlayerBalance.Profile.Normal; return true;
            case "drunk": Balance.Mode = PlayerBalance.Profile.Drunk; return true;
            case "focused": Balance.Mode = PlayerBalance.Profile.Focused; return true;
            default: return false;
        }
    }

    [JSExport]
    internal static bool ApplyPlayerBalanceImpulse(double lateral, double forward)
    {
        if (!_movement.NaturalGait || !double.IsFinite(lateral) || !double.IsFinite(forward) ||
            _posture.Target is not (PlayerPosture.Kind.Standing or PlayerPosture.Kind.Crouching)) return false;
        var impulse = new Vector2((float)Math.Clamp(lateral, -350, 350), (float)Math.Clamp(forward, -350, 350));
        var magnitude = impulse.Length();
        if (magnitude > 350) impulse *= 350 / magnitude;
        Balance.Impulse(impulse, _body.Mass, Math.Max(.32, Balance.Center.Y > 0 ? Balance.Center.Y : _body.HipHeight));
        return true;
    }

    [JSExport]
    internal static double[] GetPlayerBalance()
    {
        BalanceSnapshot[0] = (double)Balance.Mode;
        BalanceSnapshot[1] = (double)Balance.Status;
        BalanceSnapshot[2] = _body.Mass;
        BalanceSnapshot[3] = Balance.Center.X;
        BalanceSnapshot[4] = Balance.Center.Y;
        BalanceSnapshot[5] = Balance.Center.Z;
        BalanceSnapshot[6] = Balance.Capture.X;
        BalanceSnapshot[7] = Balance.Capture.Y;
        BalanceSnapshot[8] = Balance.Margin;
        BalanceSnapshot[9] = PlayerSegmentMass.Gravity;
        return BalanceSnapshot;
    }

    private static void AdvancePlayerBalance(Span<double> pose, Span<double> gait, double speed, double delta)
    {
        var center = PlayerSegmentMass.Center(_body, pose, gait, PostureFrame[2]);

        var left = new Vector2((float)pose[41], (float)(pose[28] + _body.ShoeOffset));
        var right = new Vector2((float)pose[42], (float)(pose[33] + _body.ShoeOffset));
        var leftPlanted = pose[29] < .012 && pose[27] >= pose[24] - _body.KneeHeight;
        var rightPlanted = pose[34] < .012 && pose[32] >= pose[24] - _body.KneeHeight;
        if (!leftPlanted) left = right;
        if (!rightPlanted) right = left;
        var sole = new Vector2((float)(_body.LegRadius * 1.25), (float)(_body.Height * .085));

        var impact = _movement.TakeBalanceImpact();
        if (impact != 0)
            Balance.Impulse(new(0, (float)(_body.Mass * impact)), _body.Mass, center.Y);
        var previous = Balance.Status;
        var status = Balance.Advance(center, left, right, sole, speed, delta,
            !_posture.Airborne && (leftPlanted || rightPlanted), true);
        if (status == PlayerBalance.State.Fallen)
        {
            _posture.Fall(Balance.FallYaw);
            _movement.SpeedScale = 0;
            _movement.Stop();
            return;
        }
        if (status == PlayerBalance.State.Stumbling && previous != status)
            _movement.BrakeForBalance();

        var lean = Balance.Lean;
        // Keep the foot IK targets fixed while the torso and head absorb the correction.
        pose[2] += Math.Clamp(lean.Y * .65, -.32, .32);
        pose[3] -= Math.Clamp(lean.X * .65, -.32, .32);
        pose[4] -= Math.Clamp(lean.Y * .48, -.23, .23);
        pose[5] += Math.Clamp(lean.X * .48, -.23, .23);
        if (status == PlayerBalance.State.Stumbling)
        {
            var brace = Math.Min(.5, lean.Length() + .1);
            pose[10] -= brace;
            pose[11] -= brace;
        }
    }
}
