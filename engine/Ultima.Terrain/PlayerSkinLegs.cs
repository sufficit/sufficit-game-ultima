using System.Numerics;

namespace Ultima.Terrain;

internal static class PlayerSkinLegs
{
    // World positions are in metres. Each input leg contains hip, knee, ankle,
    // target ankle and the actor's forward direction. Output is knee and ankle.
    internal const int LegInputSize = 15;
    internal const int LegOutputSize = 6;

    internal static float LowestReachableHipShift(ReadOnlySpan<double> input)
    {
        var hip = Read(input);
        var knee = Read(input[3..]);
        var ankle = Read(input[6..]);
        var reach = Read(input[9..]) - hip;
        var limit = Vector3.Distance(hip, knee) + Vector3.Distance(knee, ankle) - 1e-4f;
        var verticalReach = MathF.Sqrt(Math.Max(0, limit * limit - reach.X * reach.X - reach.Z * reach.Z));
        return Math.Min(0, reach.Y + verticalReach);
    }

    internal static void Solve(ReadOnlySpan<double> input, Span<double> output, float hipShift = 0)
    {
        var shift = Vector3.UnitY * hipShift;
        var hip = Read(input) + shift;
        var knee = Read(input[3..]) + shift;
        var ankle = Read(input[6..]) + shift;
        var target = Read(input[9..]);
        var forward = Read(input[12..]);
        var thighLength = Vector3.Distance(hip, knee);
        var calfLength = Vector3.Distance(knee, ankle);
        var reach = target - hip;
        var distance = reach.Length();
        var axis = distance > 1e-5f ? reach / distance : -Vector3.UnitY;

        // Keep the knee on the same side as the incoming animation. For an almost
        // straight leg, take the actor's forward direction projected on the bend plane.
        var originalReach = ankle - hip;
        var originalAxis = originalReach.LengthSquared() > 1e-10f ? Vector3.Normalize(originalReach) : axis;
        var bend = Project(knee - hip, originalAxis);
        bend = Project(bend, axis);
        if (bend.LengthSquared() < 1e-8f) bend = Project(forward, axis);
        if (bend.LengthSquared() < 1e-8f)
            bend = Project(Math.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX, axis);

        // Leave a small bend at full extension so the knee plane stays defined.
        var extent = Math.Clamp(distance, Math.Abs(thighLength - calfLength) + 1e-5f, thighLength + calfLength - 1e-5f);
        var along = (thighLength * thighLength - calfLength * calfLength + extent * extent) / (2 * extent);
        var away = MathF.Sqrt(Math.Max(0, thighLength * thighLength - along * along));

        Write(output, hip + along * axis + away * Vector3.Normalize(bend));
        Write(output[3..], hip + extent * axis);
    }

    private static Vector3 Project(Vector3 vector, Vector3 axis)
        => vector - Vector3.Dot(vector, axis) * axis;

    private static Vector3 Read(ReadOnlySpan<double> values)
        => new((float)values[0], (float)values[1], (float)values[2]);

    private static void Write(Span<double> values, Vector3 point)
    {
        values[0] = point.X;
        values[1] = point.Y;
        values[2] = point.Z;
    }
}
