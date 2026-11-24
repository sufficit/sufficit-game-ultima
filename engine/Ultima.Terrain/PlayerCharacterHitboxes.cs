using System.Numerics;

namespace Ultima.Terrain;

// Caixa orientada pelo transform de uma malha. Os vetores U/V/W têm o
// comprimento de cada semieixo, de modo que a projeção usa só produtos escalares.
internal readonly record struct PlayerCharacterBox(Vector3 Center, Vector3 U, Vector3 V, Vector3 W)
{
    internal float RadiusSquared => U.LengthSquared() + V.LengthSquared() + W.LengthSquared();

    private float Projection(Vector3 axis) => MathF.Abs(Vector3.Dot(U, axis)) +
        MathF.Abs(Vector3.Dot(V, axis)) + MathF.Abs(Vector3.Dot(W, axis));

    internal static bool Sweep(in PlayerCharacterBox a, in PlayerCharacterBox b, Vector3 offset, Vector3 motion,
        out float time, out Vector3 normal)
    {
        var separation = offset + a.Center - b.Center;
        var reach = MathF.Sqrt(a.RadiusSquared) + MathF.Sqrt(b.RadiusSquared) + motion.Length();
        time = 0;
        normal = default;
        if (separation.LengthSquared() > reach * reach) return false;

        Span<Vector3> axesA = [a.U, a.V, a.W];
        Span<Vector3> axesB = [b.U, b.V, b.W];
        var exit = 1f;
        var minimumDepth = float.PositiveInfinity;
        var overlapNormal = Vector3.Zero;
        for (var i = 0; i < 15; i++)
        {
            var axis = i < 3 ? axesA[i] : i < 6 ? axesB[i - 3] :
                Vector3.Cross(axesA[(i - 6) / 3], axesB[(i - 6) % 3]);
            var axisSquared = axis.LengthSquared();
            if (axisSquared < 1e-12f) continue;

            var span = a.Projection(axis) + b.Projection(axis);
            var position = Vector3.Dot(separation, axis);
            var speed = Vector3.Dot(motion, axis);
            var horizontal = axis.X * axis.X + axis.Z * axis.Z;
            if (horizontal > axisSquared * 0.04f)
            {
                var depth = (span - MathF.Abs(position)) / MathF.Sqrt(horizontal);
                if (depth < minimumDepth)
                {
                    minimumDepth = depth;
                    overlapNormal = HorizontalNormal(axis, position);
                }
            }
            if (MathF.Abs(speed) < 1e-9f)
            {
                if (MathF.Abs(position) >= span) return false;
                continue;
            }

            var entry = (-span - position) / speed;
            var end = (span - position) / speed;
            if (entry > end) (entry, end) = (end, entry);
            if (entry > time)
            {
                time = entry;
                normal = HorizontalNormal(axis, position + speed * entry);
            }
            exit = Math.Min(exit, end);
            if (time >= exit) return false;
        }

        // Se a própria animação já encostou duas caixas, permite afastar ou sair pela tangente.
        if (time == 0)
        {
            normal = overlapNormal;
            return exit > 0 && (motion == Vector3.Zero || Vector3.Dot(motion, normal) < -1e-6f);
        }
        return normal != Vector3.Zero;
    }

    private static Vector3 HorizontalNormal(Vector3 axis, float side)
    {
        axis.Y = 0;
        return axis.LengthSquared() > 1e-12f ? Vector3.Normalize(axis) * (side < 0 ? -1 : 1) : Vector3.Zero;
    }
}
