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
    private readonly List<double> _corrections = [];
    private readonly List<int> _limbs = [];
    private readonly HashSet<(int Id, int Limb)> _reportedLimbs = [];

    internal void Set(int id, double x, double y, double z)
    {
        if (!_bodies.TryGetValue(id, out var body)) _bodies.Add(id, body = new());
        body.Position = new((float)x, (float)y, (float)z);
    }

    internal void Remove(int id) => _bodies.Remove(id);

    // Lista intercalada por malha: ator, centro relativo e três semieixos no espaço do mundo.
    internal void SetHitboxes(ReadOnlySpan<double> values, bool tagged = false)
    {
        _revision++;
        var stride = tagged ? 14 : 13;
        for (var i = 0; i + stride <= values.Length; i += stride)
        {
            if (!_bodies.TryGetValue((int)values[i], out var body)) continue;
            if (body.Revision != _revision)
            {
                body.Revision = _revision;
                body.Boxes.Clear();
            }
            body.Boxes.Add(ReadBox(values, i, tagged ? (int)values[i + 13] : 0));
        }
    }

    internal int[] CollectLimbContacts()
    {
        _limbs.Clear();
        _reportedLimbs.Clear();
        foreach (var (id, body) in _bodies)
        {
            if (body.Revision != _revision) continue;
            foreach (var (other, target) in _bodies)
            {
                if (id == other || (target.Revision == _revision && id > other)) continue;
                var offset = body.Position - target.Position;
                if (!Near(offset)) continue;
                foreach (ref readonly var a in GetBoxes(body))
                foreach (ref readonly var b in GetBoxes(target))
                {
                    if (!PlayerCharacterBox.Sweep(a, b, offset, Vector3.Zero, out _, out _)) continue;
                    Report(id, a.Limb);
                    Report(other, b.Limb);
                }
            }
        }
        return _limbs.Count == 0 ? [] : _limbs.ToArray();
    }

    private void Report(int id, int limb)
    {
        if (!_reportedLimbs.Add((id, limb))) return;
        _limbs.Add(id);
        _limbs.Add(limb);
    }

    internal bool TryLimbPose(int id, int limb, ReadOnlySpan<double> values)
    {
        if (!_bodies.TryGetValue(id, out var body) || body.Revision != _revision || values.IsEmpty) return false;
        for (var i = 0; i + 14 <= values.Length; i += 14)
        {
            var box = ReadBox(values, i, limb);
            foreach (var (other, target) in _bodies)
            {
                if (id == other) continue;
                var offset = body.Position - target.Position;
                if (!Near(offset)) continue;
                foreach (ref readonly var b in GetBoxes(target))
                    if (PlayerCharacterBox.Sweep(box, b, offset, Vector3.Zero, out _, out _)) return false;
            }
        }

        body.Boxes.RemoveAll(box => box.Limb == limb);
        for (var i = 0; i + 14 <= values.Length; i += 14) body.Boxes.Add(ReadBox(values, i, limb));
        return true;
    }

    private static bool Near(Vector3 offset)
        => MathF.Abs(offset.X) <= 1.8f && MathF.Abs(offset.Z) <= 1.8f && MathF.Abs(offset.Y) <= 2.5f;

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

    internal double[] ResolveAnimatedContacts()
    {
        _corrections.Clear();
        // A Guardiã é fixa. Resolve primeiro os remotos para preservar o apoio do jogador.
        foreach (var (id, body) in _bodies)
        {
            if (id >= 0) ResolveAnimatedBody(id, body);
        }
        if (_bodies.TryGetValue(Wanderer, out var wanderer)) ResolveAnimatedBody(Wanderer, wanderer);
        if (_bodies.TryGetValue(Player, out var player)) ResolveAnimatedBody(Player, player);
        return _corrections.Count == 0 ? [] : _corrections.ToArray();
    }

    private void ResolveAnimatedBody(int id, Body body)
    {
        if (body.Revision != _revision) return;
        var (x, z) = Place(id, body.Position.X, body.Position.Y, body.Position.Z, 0.01);
        if (x == body.Position.X && z == body.Position.Z) return;
        body.Position.X = (float)x;
        body.Position.Z = (float)z;
        _corrections.Add(id);
        _corrections.Add(x);
        _corrections.Add(z);
    }

    internal (double X, double Z) Place(int id, double x, double y, double z, double step = 0.04)
    {
        var position = new Vector3((float)x, (float)y, (float)z);
        if (!FindContact(id, position, Vector3.Zero, out _, out _)) return (x, z);

        // Procura o ponto livre mais próximo sem impor um afastamento universal.
        for (var ring = 1; ring <= (int)((0.96 + _bodies.Count * 0.16) / step); ring++)
        for (var direction = 0; direction < 16; direction++)
        {
            var (sin, cos) = Math.SinCos(Math.Tau * direction / 16);
            var px = x + ring * step * cos;
            var pz = z + ring * step * sin;
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

    private static PlayerCharacterBox ReadBox(ReadOnlySpan<double> values, int i, int limb)
        => new(ReadVector(values, i + 1), ReadVector(values, i + 4), ReadVector(values, i + 7),
            ReadVector(values, i + 10), limb);

    private static Vector3 ReadVector(ReadOnlySpan<double> values, int index)
        => new((float)values[index], (float)values[index + 1], (float)values[index + 2]);

    private sealed class Body
    {
        internal Vector3 Position;
        internal int Revision;
        internal readonly List<PlayerCharacterBox> Boxes = [];
    }
}
