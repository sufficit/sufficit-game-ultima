using System.Numerics;
using Ultima.Models.Core;
using Ultima.Terrain;

internal static class PlayerBalanceChecks
{
    private static readonly Vector2 Left = new(-.09f, .06f), Right = new(.09f, .06f), Sole = new(.08f, .14f);
    private static readonly Vector3 Center = new(0, .95f, .035f);

    internal static void Run(Action<string, bool> check)
    {
        var paired = PlayerSegmentMass.UpperArm + PlayerSegmentMass.Forearm + PlayerSegmentMass.Hand +
            PlayerSegmentMass.Thigh + PlayerSegmentMass.Shin + PlayerSegmentMass.Foot;
        var total = 2 * paired + PlayerSegmentMass.Head + PlayerSegmentMass.Thorax +
            PlayerSegmentMass.Abdomen + PlayerSegmentMass.Pelvis;
        check("Segment masses sum to the body mass and gravity is 9.8 m/s²",
            Math.Abs(total - 1) < 1e-9 && PlayerSegmentMass.Gravity == 9.8);
        var body = Humanoid.ResolveBody(null);
        Span<double> pose = stackalloc double[46];
        Span<double> gait = stackalloc double[18];
        var neutral = PlayerSegmentMass.Center(body, pose, gait, 0);
        pose[10] = -.75;
        var reaching = PlayerSegmentMass.Center(body, pose, gait, 0);
        check("A raised arm shifts the weighted center of mass forward", reaching.Z > neutral.Z + .006f);
        check("Crouching lowers the center of mass", PlayerSegmentMass.Center(body, pose, gait, .28).Y < reaching.Y - .25);
        check("Two feet support the space between them; one foot does not",
            PlayerBalance.SupportMargin(new(0, .06f), Left, Right, Sole) > 0 &&
            PlayerBalance.SupportMargin(new(0, .06f), Left, Left, Sole) < 0);

        var normal = Simulate(PlayerBalance.Profile.Normal, 0, 70, 120);
        var focused = Simulate(PlayerBalance.Profile.Focused, 0, 70, 120);
        var drunk = Simulate(PlayerBalance.Profile.Drunk, 0, 70, 120);
        check("Focused balance is quieter and drunken balance sways farther",
            focused.Peak < normal.Peak && normal.Peak < drunk.Peak && !drunk.Fell);
        var at30 = Simulate(PlayerBalance.Profile.Normal, 42, 70, 30);
        var at144 = Simulate(PlayerBalance.Profile.Normal, 42, 70, 144);
        check("Fixed balance ticks agree at 30 and 144 frames per second",
            Vector2.Distance(at30.Lean, at144.Lean) < 1e-5f && !at30.Fell && !at144.Fell);

        var light = Simulate(PlayerBalance.Profile.Normal, 62, 45, 120);
        var heavy = Simulate(PlayerBalance.Profile.Normal, 62, 130, 120);
        check("The same impulse tilts a lighter character more", light.Peak > heavy.Peak * 1.8);
        var falling = Simulate(PlayerBalance.Profile.Drunk, 300, 70, 120);
        var airborne = Simulate(PlayerBalance.Profile.Drunk, 300, 70, 120, supported: false);
        check("A sustained support loss leads to a fall, while an airborne pose has no support check",
            falling.Fell && !airborne.Fell);

        var posture = new PlayerPosture();
        Span<double> packet = stackalloc double[9];
        posture.Fall(.6);
        posture.Jump();
        var locked = posture.Target == PlayerPosture.Kind.Fallen && posture.SpeedScale == 0;
        for (var frame = 0; frame < 80; frame++) posture.Advance(packet, 0, 2, 1d / 120, 0, 0);
        locked &= packet[3] == 1 && packet[8] == .6;
        posture.Jump();
        check("Falling locks movement and recovery waits for ground contact",
            locked && posture.Target == PlayerPosture.Kind.Standing && posture.SpeedScale == 0);
    }

    private static (Vector2 Lean, double Peak, bool Fell) Simulate(PlayerBalance.Profile mode, double impulse,
        double mass, int framesPerSecond, bool supported = true)
    {
        var balance = new PlayerBalance { Mode = mode };
        balance.Impulse(new((float)impulse, 0), mass, Center.Y);
        var peak = 0d;
        for (var frame = 0; frame < framesPerSecond * 3; frame++)
        {
            balance.Advance(Center, Left, Right, Sole, 0, 1d / framesPerSecond, supported, true);
            peak = Math.Max(peak, balance.Lean.Length());
        }
        return (balance.Lean, peak, balance.Status == PlayerBalance.State.Fallen);
    }
}
