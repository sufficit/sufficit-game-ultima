namespace Ultima;

internal static class AnimationTimeline
{
    // Elapsed em unidades da duração do clipe. O intervalo aberto à esquerda
    // evita emitir outra vez a marca da fronteira do frame anterior.
    internal static int[] Crossed(double previous, double current, bool loops, double[] marks)
    {
        if (current <= previous || marks.Length == 0) return [];
        if (!loops) current = Math.Min(current, 1);
        if (current <= previous) return [];

        List<int>? crossed = null;
        var firstCycle = loops ? (int)Math.Floor(previous) : 0;
        var lastCycle = loops ? (int)Math.Floor(current) : 0;
        for (var cycle = firstCycle; cycle <= lastCycle; cycle++)
        for (var index = 0; index < marks.Length; index++)
        {
            var at = cycle + marks[index];
            if (previous < at && at <= current) (crossed ??= []).Add(index);
        }
        return crossed?.ToArray() ?? [];
    }
}
