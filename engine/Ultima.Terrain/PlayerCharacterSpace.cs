using System.Numerics;
using System.Runtime.InteropServices;

namespace Ultima.Terrain;

internal sealed class PlayerCharacterSpace
{
    internal const int Player = -1, Guardian = -2, Wanderer = -3;
    internal const float TorsoDepth = 0.17f;
    private const double WorldHalf = 400;
    private const float Skin = 0.002f;
    private static readonly PlayerCharacterBox[] Fallback =
        [new(new(0, 0.9f, 0), new(0.23f, 0, 0), new(0, 0.79f, 0), new(0, 0, TorsoDepth))];
    private readonly Dictionary<int, Body> _bodies = [];
    private int _revision = 1;

    internal void Set(int id, double x, double y, double z)
    {
        if (!_bodies.TryGetValue(id, out var body)) _bodies.Add(id, body = new());
        body.Position = new((float)x, (float)y, (float)z);
    }

    internal void Remove(int id) => _bodies.Remove(id);

    // Lista intercalada por malha: ator, centro relativo e três semieixos no espaço do mundo.
    internal void SetHitboxes(ReadOnlySpan<double> values)
    {
        _revision++;
        for (var i = 0; i + 12 < values.Length; i += 13)
        {
            if (!_bodies.TryGetValue((int)values[i], out var body)) continue;
            if (body.Revision != _revision)
            {
                body.Revision = _revision;
                body.Boxes.Clear();
            }
            body.Boxes.Add(new(ReadVector(values, i + 1), ReadVector(values, i + 4),
                ReadVector(values, i + 7), ReadVector(values, i + 10)));
        }
    }

    internal (double X, double Z, bool Blocked) Move(int id, double x, double y, double z, double targetX, double targetZ)
    {
        var remaining = new Vector3((float)(targetX - x), 0, (float)(targetZ - z));
        var position = new Vector3((float)x, (float)y, (float)z);
        var moved = Vector3.Zero;
        var blocked = false;
        for (var pass = 0; pass < 3 && remaining.LengthSquared() > 1e-12f; pass++)
        {
            if (!FindContact(id, position + moved, remaining, out var time, out var normal))
            {
                if (!blocked) return (targetX, targetZ, false);
                moved += remaining;
                break;
            }

            blocked = true;
            moved += remaining * Math.Max(0, time - Skin / remaining.Length());
            remaining *= 1 - time;
            remaining -= normal * Math.Min(0, Vector3.Dot(remaining, normal));
        }
        return (x + moved.X, z + moved.Z, blocked);
    }

    internal (double X, double Z) Place(int id, double x, double y, double z)
    {
        var position = new Vector3((float)x, (float)y, (float)z);
        if (!FindContact(id, position, Vector3.Zero, out _, out _)) return (x, z);

        // Procura o ponto livre mais próximo sem impor um afastamento universal.
        for (var ring = 1; ring <= 24 + _bodies.Count * 4; ring++)
        for (var direction = 0; direction < 16; direction++)
        {
            var (sin, cos) = Math.SinCos(Math.Tau * direction / 16);
            var px = x + ring * 0.04 * cos;
            var pz = z + ring * 0.04 * sin;
            if (Math.Abs(px) > WorldHalf || Math.Abs(pz) > WorldHalf) continue;
            if (!FindContact(id, new((float)px, (float)y, (float)pz), Vector3.Zero, out _, out _))
                return (px, pz);
        }
        return (x, z);
    }

    private bool FindContact(int id, Vector3 position, Vector3 motion, out float time, out Vector3 normal)
    {
        time = 1;
        normal = default;
        var reach = 1.8f + motion.Length();
        var boxes = GetBoxes(_bodies.GetValueOrDefault(id));
        foreach (var (other, body) in _bodies)
        {
            if (other == id) continue;
            var offset = position - body.Position;
            if (MathF.Abs(offset.X) > reach || MathF.Abs(offset.Z) > reach || MathF.Abs(offset.Y) > 2.5f) continue;
            foreach (ref readonly var a in boxes)
            foreach (ref readonly var b in GetBoxes(body))
            {
                if (!PlayerCharacterBox.Sweep(a, b, offset, motion, out var t, out var n) || t >= time) continue;
                time = t;
                normal = n;
                if (motion == Vector3.Zero) return true;
            }
        }
        return time < 1;
    }

    private ReadOnlySpan<PlayerCharacterBox> GetBoxes(Body? body)
        => body?.Revision == _revision ? CollectionsMarshal.AsSpan(body.Boxes) : Fallback;

    private static Vector3 ReadVector(ReadOnlySpan<double> values, int index)
        => new((float)values[index], (float)values[index + 1], (float)values[index + 2]);

    private sealed class Body
    {
        internal Vector3 Position;
        internal int Revision;
        internal readonly List<PlayerCharacterBox> Boxes = [];
    }
}
