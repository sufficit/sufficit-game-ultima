namespace Ultima.Terrain;

internal static class PlayerPose
{
    internal const double StrideLength = 0.96;

    internal static bool CanCollect(double dx, double dy, double dz)
        => dx * dx + dy * dy + dz * dz <= 3.5 * 3.5;

    internal static (double Elapsed, double Previous, double Progress) AdvanceAction(
        double elapsed, double duration, double delta, bool frozen)
    {
        if (duration <= 0) return (0, 0, 0);
        var next = frozen ? elapsed : elapsed + delta;
        return (next, elapsed / duration, next / duration);
    }

    internal static void Animate(Span<double> output, double phase, double weight, double clock,
        double distance, double delta, int nameLength, bool listening, bool speaking,
        double progress, double bend, double reach)
    {
        var speed = delta > 0 ? distance / delta : 0;
        var run = SmoothStep(PlayerMovement.WalkSpeed, PlayerMovement.RunSpeed, speed);
        weight = Lerp(weight, Math.Min(1, speed / 1.1), 1 - Math.Exp(-13 * delta));
        phase = (phase + distance * Math.Tau / StrideLength) % Math.Tau;

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
            var cycle = (phase / Math.PI + 0.5 + i) % 2;
            var swing = cycle > 1;
            var t = cycle - (swing ? 1 : 0);
            // No apoio, o pé recua exatamente a distância que o corpo avança.
            var forward = StrideLength / 4 * (swing ? -1 - 2 * t + t * t * (12 - 8 * t) : 1 - 2 * t);
            var lift = swing ? Math.Sin(Math.PI * t) * Lerp(0.11, 0.17, run) * weight : 0;
            // A sola roda para a ponta na saída e para o calcanhar ao voltar.
            output[8 + i] = swing ? Math.Sin(Math.Tau * t) * Math.Min(0.38, lift * 3) : 0;
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
        double rightForward, double rightLift, double rightSupport, bool rightOnObstacle,
        double leftPitch = 0, double rightPitch = 0)
    {
        var leftAnkle = leftSupport + ankleOffset + leftLift;
        var rightAnkle = rightSupport + ankleOffset + rightLift;
        var bodyY = ground + footGap;
        var spanSquared = 4 * legLength * legLength;
        var leftReach = leftAnkle + Math.Sqrt(Math.Max(0, spanSquared - leftForward * leftForward)) - hipHeight;
        var rightReach = rightAnkle + Math.Sqrt(Math.Max(0, spanSquared - rightForward * rightForward)) - hipHeight;
        if (leftOnObstacle || rightOnObstacle)
        {
            var top = Math.Max(leftSupport, rightSupport) + footGap;
            var riseLimit = Math.Max(bodyY, previousY) + delta * 3;
            bodyY = Math.Min(Math.Max(bodyY, top), riseLimit);
        }
        bodyY = Math.Min(bodyY, Math.Min(leftReach, rightReach));
        output[0] = bodyY;
        SetAngles(output[1..4], bodyY + hipHeight - leftAnkle, leftForward, legLength, leftPitch);
        SetAngles(output[4..7], bodyY + hipHeight - rightAnkle, rightForward, legLength, rightPitch);
    }

    private static void SetAngles(Span<double> output, double drop, double forward, double legLength, double pitch)
    {
        drop = Math.Max(0.16, drop);
        var extent = double.Hypot(drop, forward);
        var flex = Math.Acos(Math.Min(1, extent / (2 * legLength)));
        output[0] = -Math.Atan2(forward, drop) - flex;
        output[1] = 2 * flex;
        output[2] = pitch - output[0] - output[1];
    }

    private static double Lerp(double from, double to, double weight) => from + (to - from) * weight;

    private static double SmoothStep(double from, double to, double value)
    {
        var t = Math.Clamp((value - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
