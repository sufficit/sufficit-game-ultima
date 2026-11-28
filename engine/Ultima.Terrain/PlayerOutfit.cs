namespace Ultima.Terrain;

internal sealed class PlayerOutfit
{
    private static readonly int[] Shirts = [0x355A73, 0x8A533F, 0x41634F, 0x6F5073, 0xAA7840, 0x455C56];
    private static readonly int[] Trousers = [0x333B42, 0x52534D, 0x5B4A38, 0x39495F, 0x4D454A];
    private static readonly int[] Boots = [0x332A25, 0x252729, 0x523C2D];
    private double _angle, _velocity;

    internal static int[] Select(string identity)
    {
        uint seed = 2166136261;
        foreach (var character in identity)
            seed = unchecked((seed ^ character) * 16777619);
        return [Shirts[seed % Shirts.Length], Trousers[(seed >> 9) % Trousers.Length],
            Boots[(seed >> 17) % Boots.Length], (int)((seed >> 23) & 1)];
    }

    internal double Advance(double speed, double weatherWind, double yaw, double delta)
    {
        // Reduced pendulum model for a short cloth tail. Speed is metres per
        // second. Wind is the world weather intensity, projected onto facing.
        const double length = .28, gravity = 9.81;
        var airflow = Math.Clamp(speed - 3 * weatherWind * Math.Cos(yaw), -6, 6);
        var dragTorque = .85 * airflow * Math.Abs(airflow);
        var dt = Math.Clamp(delta, 0, .05);
        for (var elapsed = 0d; elapsed < dt;)
        {
            var step = Math.Min(1d / 120, dt - elapsed);
            _velocity += (dragTorque - gravity / length * Math.Sin(_angle) - 7 * _velocity) * step;
            _angle = Math.Clamp(_angle + _velocity * step, -.15, .65);
            elapsed += step;
        }
        return _angle;
    }
}
