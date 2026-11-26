namespace Ultima.Terrain;

internal struct PlayerCloseCamera(double yaw)
{
    internal const double TargetHeight = 1.3;
    private const double DefaultDistance = 2.15;
    private const double MinDistance = 1.85;
    private const double MaxDistance = 2.50;
    private const double ScrollRate = .08 / 120;
    private const double FollowRate = 14;
    private const double DefaultPitch = .25;
    private const double MouseRate = .004;
    private double _pitch = DefaultPitch;
    private double _distance = DefaultDistance;
    private double _targetDistance = DefaultDistance;

    internal bool Settled => _distance == _targetDistance;

    internal double Yaw { get; private set; } = yaw;

    internal (double X, double Y, double Z) Offset
    {
        get
        {
            var (sinYaw, cosYaw) = Math.SinCos(Yaw);
            var (sinPitch, cosPitch) = Math.SinCos(_pitch);
            var horizontal = _distance * cosPitch;
            return (-sinYaw * horizontal, sinPitch * _distance, -cosYaw * horizontal);
        }
    }

    internal void Align(double facing) => (Yaw, _pitch) = (facing, DefaultPitch);

    internal void Advance(double yawDelta, double pitchDelta, double mouseX, double mouseY, double wheel, double dt)
    {
        Yaw = Math.IEEERemainder(Yaw + yawDelta + mouseX * MouseRate, Math.Tau);
        _pitch = Math.Clamp(_pitch + pitchDelta + mouseY * MouseRate, -.30, .85);
        if (wheel != 0) _targetDistance = Math.Clamp(_targetDistance + wheel * ScrollRate, MinDistance, MaxDistance);
        if (Settled) return;

        var remaining = _targetDistance - _distance;
        _distance = Math.Abs(remaining) < .001 ? _targetDistance : _distance + remaining * (1 - Math.Exp(-FollowRate * dt));
    }
}
