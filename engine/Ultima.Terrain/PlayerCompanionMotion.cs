namespace Ultima.Terrain;

internal static class PlayerCompanionMotion
{
    internal readonly record struct Frame(double X, double Z, double Yaw, double Phase, double Distance);

    internal static Frame Patrol(double x, double z, double yaw, double homeX, double homeZ, double phase, double delta)
    {
        phase = (phase + delta * 0.65) % Math.Tau;
        var (sin, cos) = Math.SinCos(phase);
        var nextX = homeX + sin * 1.4;
        var nextZ = homeZ + cos * 1.4;
        var nextYaw = Turn(yaw, phase + Math.PI / 2, 1 - Math.Exp(-7 * delta));
        return new(nextX, nextZ, nextYaw, phase, Distance(nextX - x, nextZ - z));
    }

    internal static Frame Follow(double x, double z, double yaw, double targetX, double targetZ, double targetYaw,
        double delta)
    {
        if (x == targetX && z == targetZ && yaw == targetYaw) return new(x, z, yaw, 0, 0);
        var blend = 1 - Math.Exp(-12 * delta);
        var nextX = x + (targetX - x) * blend;
        var nextZ = z + (targetZ - z) * blend;
        return new(nextX, nextZ, Turn(yaw, targetYaw, blend), 0, Distance(nextX - x, nextZ - z));
    }

    private static double Turn(double yaw, double target, double blend)
    {
        var difference = target - yaw;
        if (Math.Abs(difference) > Math.PI)
            difference = Math.Atan2(Math.Sin(difference), Math.Cos(difference));
        return yaw + difference * blend;
    }

    private static double Distance(double x, double z) => Math.Sqrt(x * x + z * z);
}
