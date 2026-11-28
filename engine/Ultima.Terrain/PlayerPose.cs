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
        double progress, double bend, double reach, double strideLength = StrideLength, bool playerGait = false,
        double walkSpeed = PlayerMovement.WalkSpeed, double runSpeed = PlayerMovement.RunSpeed)
    {
        var speed = delta > 0 ? distance / delta : 0;
        var run = SmoothStep(walkSpeed, runSpeed, speed);
        weight = Lerp(weight, Math.Min(1, speed / 1.1), 1 - Math.Exp(-13 * delta));
        if (playerGait && distance == 0 && weight < 0.0001) weight = 0;
        phase = (phase + distance * Math.Tau / strideLength) % Math.Tau;

        var gesture = Math.Sin(Math.PI * Math.Min(1, progress));
        var breath = playerGait ? 0 : Math.Sin(clock * 2.9) * 0.012;
        var armPhase = playerGait ? -Math.Sin(phase) : Math.Cos(phase);
        var swing = armPhase * weight * Lerp(0.34, 0.74, run);
        var blink = (clock + nameLength % 7 * 0.43) % 4.7;
        output[0] = phase;
        output[1] = weight;
        output[2] = breath + 0.12 * run + gesture * bend;
        output[3] = Math.Sin(phase) * (playerGait ? 0.02 : 0.035) * weight;
        output[4] = -0.06 * run + gesture * 0.14 + (speaking ? Math.Sin(clock * 9) * 0.036 : 0);
        output[5] = listening && !speaking ? 0.09 : 0;
        output[6] = 1 - 0.94 * (1 - SmoothStep(0.015, 0.085, Math.Abs(blink - 0.09)));
        output[7] = listening ? 0.054 : 0.048;
        output[8] = speaking ? 0.012 + Math.Abs(Math.Sin(clock * 16)) * 0.021 : 0.008;
        output[9] = 0.26 * gesture - (listening ? 0.15 : 0);
        output[10] = swing - gesture * 0.16;
        output[11] = -swing - gesture * reach - (speaking ? 0.24 + 0.10 * Math.Sin(clock * 4.5) : 0);
    }

    internal static void Brace(Span<double> pose, double impact)
    {
        // An external impact pushes the torso back. A crossover step leans into recovery.
        pose[2] += (impact < 0 ? 0.32 : 0.5) * impact;
        pose[4] -= 0.14 * impact;
        pose[10] -= (impact < 0 ? 0.22 : 0.62) * impact;
        pose[11] -= (impact < 0 ? 0.22 : 0.62) * impact;
    }

    internal static void FootTargets(Span<double> output, double yaw, double phase, double weight,
        double speed, double spacing, double shoeOffset, double strideLength = StrideLength,
        double walkSpeed = PlayerMovement.WalkSpeed, double runSpeed = PlayerMovement.RunSpeed, bool softSwing = false)
    {
        var run = SmoothStep(walkSpeed, runSpeed, speed);
        var (sine, cosine) = Math.SinCos(yaw);
        for (var i = 0; i < 2; i++)
        {
            var cycle = (phase / Math.PI + 0.5 + i) % 2;
            var swing = cycle > 1;
            var t = cycle - (swing ? 1 : 0);
            // During stance, the foot moves back by the distance the body advances.
            var forward = strideLength / 4 * (swing ? -1 - 2 * t + t * t * (12 - 8 * t) : 1 - 2 * t);
            var arc = Math.Sin(Math.PI * t);
            var lift = !swing ? 0 : softSwing ? arc * arc * Lerp(.065, .135, run) * weight
                : arc * Lerp(.11, .17, run) * weight;
            // The sole rolls toward the toe at lift-off and toward the heel at landing.
            var pitchLimit = softSwing ? Math.Min(.26, lift * 5) : Math.Min(.38, lift * 3);
            output[8 + i] = swing ? Math.Sin(Math.Tau * t) * pitchLimit : 0;
            var localX = i == 0 ? -spacing : spacing;
            var offset = 4 * i;
            output[offset] = forward;
            output[offset + 1] = lift;
            output[offset + 2] = localX * cosine + (forward + shoeOffset) * sine;
            output[offset + 3] = -localX * sine + (forward + shoeOffset) * cosine;
        }
    }

    // Swivel around the hip-to-ankle axis. Sine keeps the angle bounded and continuous across ±π.
    internal static double KneeSwivel(double footYaw) => 0.65 * Math.Sin(footYaw);

    // Solve the body height and both legs against the sampled ankle targets.
    // The output stores body Y, left and right hip/knee/ankle pitches, and optional hip rolls.
    internal static void SolveLegs(Span<double> output, double ground, double previousY, double delta,
        double footGap, double hipHeight, double legLength, double ankleOffset,
        double leftForward, double leftLift, double leftSupport, bool leftOnObstacle,
        double rightForward, double rightLift, double rightSupport, bool rightOnObstacle,
        double leftPitch = 0, double rightPitch = 0, double leftLateral = 0, double rightLateral = 0, double? tibiaLength = null, double bodyDrop = 0)
    {
        var leftAnkle = leftSupport + ankleOffset + leftLift;
        var rightAnkle = rightSupport + ankleOffset + rightLift;
        var bodyY = ground + footGap - bodyDrop;
        var lowerLength = tibiaLength ?? legLength;
        var reach = legLength + lowerLength;
        var spanSquared = reach * reach;
        var leftReach = leftAnkle + Math.Sqrt(Math.Max(0, spanSquared - leftForward * leftForward - leftLateral * leftLateral)) - hipHeight;
        var rightReach = rightAnkle + Math.Sqrt(Math.Max(0, spanSquared - rightForward * rightForward - rightLateral * rightLateral)) - hipHeight;
        if (leftOnObstacle || rightOnObstacle)
        {
            var top = Math.Max(leftSupport, rightSupport) + footGap - bodyDrop;
            var riseLimit = Math.Max(bodyY, previousY) + delta * 3;
            bodyY = Math.Min(Math.Max(bodyY, top), riseLimit);
        }
        // Lower the pelvis until both legs can reach their support points.
        bodyY = Math.Min(bodyY, Math.Min(leftReach, rightReach));
        output[0] = bodyY;
        var leftDrop = Math.Max(0.16, bodyY + hipHeight - leftAnkle);
        var rightDrop = Math.Max(0.16, bodyY + hipHeight - rightAnkle);
        SetAngles(output[1..4], double.Hypot(leftDrop, leftLateral), leftForward, legLength, lowerLength, leftPitch);
        SetAngles(output[4..7], double.Hypot(rightDrop, rightLateral), rightForward, legLength, lowerLength, rightPitch);
        if (output.Length >= 9)
        {
            output[7] = Math.Atan2(leftLateral, leftDrop);
            output[8] = Math.Atan2(rightLateral, rightDrop);
        }
    }

    internal static void SolveSingleLeg(Span<double> output, double drop, double forward, double lateral,
        double legLength, double pitch, double? tibiaLength = null)
    {
        drop = Math.Max(0.16, drop);
        SetAngles(output[..3], double.Hypot(drop, lateral), forward, legLength, tibiaLength ?? legLength, pitch);
        output[3] = Math.Atan2(lateral, drop);
    }

    private static void SetAngles(Span<double> output, double drop, double forward, double femur, double tibia, double pitch)
    {
        // Clamp the target to the interval that can close the hip-knee-ankle triangle.
        var extent = Math.Clamp(double.Hypot(drop, forward), Math.Max(1e-8, Math.Abs(femur - tibia)), femur + tibia);
        if (femur == tibia)
        {
            // Equal segments use the symmetric solver used by the default and remote avatars.
            var flex = Math.Acos(Math.Min(1, extent / (2 * femur)));
            output[0] = -Math.Atan2(forward, drop) - flex;
            output[1] = 2 * flex;
            output[2] = pitch - output[0] - output[1];
            return;
        }
        // The law of cosines gives knee flexion. Project the tibia to recover hip pitch.
        var squared = extent * extent;
        var knee = Math.Acos(Math.Clamp((squared - femur * femur - tibia * tibia) / (2 * femur * tibia), -1, 1));
        var hipOffset = Math.Atan2(tibia * Math.Sin(knee), femur + tibia * Math.Cos(knee));
        output[0] = -Math.Atan2(forward, drop) - hipOffset;
        output[1] = knee;
        output[2] = pitch - output[0] - output[1];
    }

    private static double Lerp(double from, double to, double weight) => from + (to - from) * weight;

    internal static double SmoothStep(double from, double to, double value)
    {
        var t = Math.Clamp((value - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
