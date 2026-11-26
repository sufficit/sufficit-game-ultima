namespace Ultima.Terrain;

internal struct PlayerCloseCamera(double yaw)
{
    internal const double TargetHeight = 1.3;
    private const double Distance = 2.15;
    private const double MouseRate = .004;
    private double _pitch = .25;

    internal double Yaw { get; private set; } = yaw;

    internal (double X, double Y, double Z) Offset
    {
        get
        {
            var (sinYaw, cosYaw) = Math.SinCos(Yaw);
            var (sinPitch, cosPitch) = Math.SinCos(_pitch);
            var horizontal = Distance * cosPitch;
            return (-sinYaw * horizontal, sinPitch * Distance, -cosYaw * horizontal);
        }
    }

    internal void Advance(double yawDelta, double pitchDelta, double mouseX, double mouseY)
    {
        Yaw = Math.IEEERemainder(Yaw + yawDelta + mouseX * MouseRate, Math.Tau);
        _pitch = Math.Clamp(_pitch + pitchDelta + mouseY * MouseRate, -.30, .85);
    }
}
