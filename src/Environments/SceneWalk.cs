using Ultima.Terrain;
using System.Runtime.Versioning;

namespace Ultima.Environments;

[SupportedOSPlatform("browser")]
internal sealed partial class SceneWalk(SceneWalkSettings settings, SceneBox[] boxes)
{
    private const double Radius = .26;
    private const double Height = 1.75;

    private readonly Obstacle[] _solids = boxes.Where(box => box.Solid).Select(box => new Obstacle(
        box.X - box.Width / 2, box.X + box.Width / 2, box.Y - box.Height / 2, box.Y + box.Height / 2,
        box.Z - box.Depth / 2, box.Z + box.Depth / 2)).ToArray();

    private double _x = settings.X, _z = settings.Z, _yaw = settings.Yaw, _pitch;
    private double _heading = settings.Yaw, _phase, _weight, _clock, _bodyY = .03;
    private Func<double, double, double, double, (double X, double Z)>? _resolve;

    internal void Advance(Span<double> result, double side, double forward, bool sprint,
        double yawDelta, double pitchDelta, double seconds)
    {
        _yaw = Math.IEEERemainder(_yaw + yawDelta, Math.Tau);
        _pitch = Math.Clamp(_pitch + pitchDelta, -.66, .63);
        var delta = Math.Clamp(seconds, 0, .05);
        var (sin, cos) = Math.SinCos(_yaw);
        var distance = 0d;
        if (delta > 0)
        {
            var motion = BrowserExports.MoveInteriorPlayer(_x, _bodyY, _z, _heading, sin, cos,
                side, forward, sprint, delta, _resolve ??= Resolve);
            (_x, _z, distance) = (motion.X, motion.Z, motion.Distance);
            if (motion.Pivot) _heading = motion.Yaw;
            else if (double.IsFinite(motion.Yaw))
                _heading += Math.IEEERemainder(motion.Yaw - _heading, Math.Tau) * (1 - Math.Exp(-16 * delta));
        }

        var animation = result[10..];
        BrowserExports.AnimateInteriorPlayer(animation, _x, _bodyY, _z, _heading, _phase, _weight, _clock, distance, delta);
        (_phase, _weight, _clock, _bodyY) = (animation[0], animation[1], animation[12], animation[17]);
        (result[0], result[1], result[2]) = (_x, _z, _heading);
        Follow(result, sin, cos);
    }

    internal void StopAnimation()
    {
        BrowserExports.StopPlayer();
        BrowserExports.ResetFootContacts();
        _weight = 0;
    }

    private (double X, double Z) Resolve(double x, double z, double dx, double dz)
    {
        var nextX = Math.Clamp(x + dx, settings.MinX + Radius, settings.MaxX - Radius);
        var nextZ = Math.Clamp(z + dz, settings.MinZ + Radius, settings.MaxZ - Radius);
        if (Blocked(nextX, z)) nextX = x;
        if (Blocked(nextX, nextZ)) nextZ = z;
        return (nextX, nextZ);
    }

    private bool Blocked(double x, double z)
    {
        foreach (var wall in _solids)
            if (wall.MaxY > .06 && wall.MinY < Height && x > wall.MinX - Radius && x < wall.MaxX + Radius &&
                z > wall.MinZ - Radius && z < wall.MaxZ + Radius) return true;
        return false;
    }

    private readonly record struct Obstacle(double MinX, double MaxX, double MinY, double MaxY, double MinZ, double MaxZ);
}
