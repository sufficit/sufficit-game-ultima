namespace Ultima.Terrain;

// Volumes conservadores no plano horizontal, com intervalo vertical por ator.
internal sealed class PlayerCharacterSpace
{
    internal const int Player = -1, Guardian = -2, Wanderer = -3;
    internal const double Radius = 0.54;
    private const double Diameter = Radius * 2;
    private const double Height = 1.75;
    private readonly Dictionary<int, Body> _bodies = [];

    internal void Set(int id, double x, double y, double z) => _bodies[id] = new(x, y, z);
    internal void Remove(int id) => _bodies.Remove(id);

    internal (double X, double Z, bool Blocked) Move(int id, double x, double y, double z, double targetX, double targetZ)
    {
        var dx = targetX - x;
        var dz = targetZ - z;
        var lengthSquared = dx * dx + dz * dz;
        if (lengthSquared < 1e-12) return (x, z, false);
        var limit = 1.0;
        foreach (var (other, body) in _bodies)
        {
            if (other == id || Math.Abs(y - body.Y) >= Height) continue;
            var ox = x - body.X;
            var oz = z - body.Z;
            if (Math.Abs(ox) > Diameter + Math.Abs(dx) || Math.Abs(oz) > Diameter + Math.Abs(dz)) continue;
            var approach = ox * dx + oz * dz;
            if (approach >= 0) continue;
            var gap = ox * ox + oz * oz - Diameter * Diameter;
            var discriminant = approach * approach - lengthSquared * gap;
            if (discriminant <= 0) continue;
            var hit = gap <= 0 ? 0 : (-approach - Math.Sqrt(discriminant)) / lengthSquared;
            limit = Math.Min(limit, hit);
        }
        if (limit >= 1) return (targetX, targetZ, false);
        limit = Math.Max(0, limit - 0.002 / Math.Sqrt(lengthSquared));
        return (x + dx * limit, z + dz * limit, true);
    }

    // Poses recebidas da rede podem nascer dentro de um volume já ocupado.
    internal (double X, double Z) Place(int id, double x, double y, double z)
    {
        const double worldHalf = 400;
        var originX = x;
        var originZ = z;
        for (var pass = 0; pass < 8; pass++)
        {
            var changed = false;
            foreach (var (other, body) in _bodies)
            {
                if (id == other || Math.Abs(y - body.Y) >= Height) continue;
                var dx = x - body.X;
                var dz = z - body.Z;
                if (Math.Abs(dx) >= Diameter || Math.Abs(dz) >= Diameter) continue;
                var distance = double.Hypot(dx, dz);
                if (distance >= Diameter) continue;
                var scale = (Diameter + 0.002) / (distance > 1e-8 ? distance : 1);
                x = Math.Clamp(body.X + (distance > 1e-8 ? dx : id < other ? -1 : 1) * scale, -worldHalf, worldHalf);
                z = Math.Clamp(body.Z + dz * scale, -worldHalf, worldHalf);
                changed = true;
            }
            if (!changed) return (x, z);
        }

        // Se várias pessoas chegam ao mesmo ponto, procura um lugar livre ao redor.
        for (var ring = 1; ring <= _bodies.Count + 1; ring++)
        for (var direction = 0; direction < 16; direction++)
        {
            var (sin, cos) = Math.SinCos(Math.Tau * direction / 16);
            x = originX + ring * Diameter * cos;
            z = originZ + ring * Diameter * sin;
            if (Math.Abs(x) > worldHalf || Math.Abs(z) > worldHalf) continue;
            if (IsFree(id, x, y, z)) return (x, z);
        }
        return (originX, originZ);
    }

    private bool IsFree(int id, double x, double y, double z)
    {
        foreach (var (other, body) in _bodies)
        {
            if (id != other && Math.Abs(y - body.Y) < Height && double.Hypot(x - body.X, z - body.Z) < Diameter)
                return false;
        }
        return true;
    }

    private readonly record struct Body(double X, double Y, double Z);
}
