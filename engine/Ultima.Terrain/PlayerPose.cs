namespace Ultima.Terrain;

internal static class PlayerPose
{
    internal static bool CanCollect(double dx, double dy, double dz)
        => dx * dx + dy * dy + dz * dz <= 3.5 * 3.5;

    internal static void Animate(Span<double> output, double phase, double weight, double clock,
        double distance, double delta, int nameLength, bool listening, bool speaking,
        double progress, double bend, double reach)
    {
        var speed = delta > 0 ? distance / delta : 0;
        var run = SmoothStep(PlayerMovement.WalkSpeed, PlayerMovement.RunSpeed, speed);
        weight = Lerp(weight, Math.Min(1, speed / 1.1), 1 - Math.Exp(-13 * delta));
        phase = (phase + distance * Math.Tau / Lerp(1.1, 2.35,
            SmoothStep(1.4, PlayerMovement.RunSpeed, speed))) % Math.Tau;

        var gesture = Math.Sin(Math.PI * Math.Min(1, progress));
        var breath = Math.Sin(clock * 2.9) * 0.012;
        var swing = Math.Cos(phase) * weight * Lerp(0.34, 0.74, run);
        var blink = (clock + nameLength % 7 * 0.43) % 4.7;
        output[0] = phase;
        output[1] = weight;
        output[2] = breath + 0.12 * run + gesture * bend;
        output[3] = Math.Sin(phase) * 0.035 * weight;
        output[4] = -0.06 * run + gesture * 0.14 + (speaking ? Math.Sin(clock * 9) * 0.036 : 0);
        output[5] = listening && !speaking ? 0.09 : 0;
        output[6] = 1 - 0.94 * (1 - SmoothStep(0.015, 0.085, Math.Abs(blink - 0.09)));
        output[7] = listening ? 0.054 : 0.048;
        output[8] = speaking ? 0.012 + Math.Abs(Math.Sin(clock * 16)) * 0.021 : 0.008;
        output[9] = 0.26 * gesture - (listening ? 0.15 : 0);
        output[10] = swing - gesture * 0.16;
        output[11] = -swing - gesture * reach - (speaking ? 0.24 + 0.10 * Math.Sin(clock * 4.5) : 0);
    }

    internal static void FootTargets(Span<double> output, double yaw, double phase, double weight,
        double speed, double spacing, double shoeOffset)
    {
        var run = SmoothStep(PlayerMovement.WalkSpeed, PlayerMovement.RunSpeed, speed);
        var (sine, cosine) = Math.SinCos(yaw);
        for (var i = 0; i < 2; i++)
        {
            var legPhase = phase + i * Math.PI;
            var forward = Math.Cos(legPhase) * Lerp(0.18, 0.27, run) * weight;
            var lift = Math.Max(0, -Math.Sin(legPhase)) * Lerp(0.11, 0.17, run) * weight;
            var localX = i == 0 ? -spacing : spacing;
            var offset = 4 * i;
            output[offset] = forward;
            output[offset + 1] = lift;
            output[offset + 2] = localX * cosine + (forward + shoeOffset) * sine;
            output[offset + 3] = -localX * sine + (forward + shoeOffset) * cosine;
        }
    }

    internal static void SolveLegs(Span<double> output, double ground, double previousY, double delta,
        double footGap, double hipHeight, double legLength, double ankleOffset,
        double leftForward, double leftLift, double leftSupport, bool leftOnObstacle,
        double rightForward, double rightLift, double rightSupport, bool rightOnObstacle)
    {
        var leftAnkle = leftSupport + ankleOffset + leftLift;
        var rightAnkle = rightSupport + ankleOffset + rightLift;
        var bodyY = ground + footGap;
        if (leftOnObstacle || rightOnObstacle)
        {
            var top = Math.Max(leftSupport, rightSupport) + footGap;
            var spanSquared = 4 * legLength * legLength;
            var leftReach = leftAnkle + Math.Sqrt(Math.Max(0, spanSquared - leftForward * leftForward)) - hipHeight;
            var rightReach = rightAnkle + Math.Sqrt(Math.Max(0, spanSquared - rightForward * rightForward)) - hipHeight;
            var riseLimit = Math.Max(bodyY, previousY) + delta * 3;
            bodyY = Math.Min(Math.Max(bodyY, top), Math.Min(Math.Min(leftReach, rightReach), riseLimit));
        }
        output[0] = bodyY;
        SetAngles(output[1..4], bodyY + hipHeight - leftAnkle, leftForward, legLength);
        SetAngles(output[4..7], bodyY + hipHeight - rightAnkle, rightForward, legLength);
    }

    private static void SetAngles(Span<double> output, double drop, double forward, double legLength)
    {
        drop = Math.Max(0.16, drop);
        var extent = double.Hypot(drop, forward);
        var flex = Math.Acos(Math.Min(1, extent / (2 * legLength)));
        output[0] = -Math.Atan2(forward, drop) - flex;
        output[1] = 2 * flex;
        output[2] = -output[0] - output[1];
    }

    private static double Lerp(double from, double to, double weight) => from + (to - from) * weight;

    private static double SmoothStep(double from, double to, double value)
    {
        var t = Math.Clamp((value - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
