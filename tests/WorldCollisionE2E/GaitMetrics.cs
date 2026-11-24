internal sealed record GaitFrame(double X, double Z, double G, double Yaw, double TorsoYaw, FootFrame[] Feet)
{
    public double Distance(GaitFrame other) => double.Hypot(X - other.X, Z - other.Z);
}

internal sealed record FootFrame(double X, double Z, Position Direction, double Bend, double Lateral, double SoleHeight, double Floor)
{
    public double Pitch => Math.Asin(Math.Clamp(-Direction.Y, -1, 1));
    public bool Planted => Math.Abs(Pitch) < 1e-6 && SoleHeight - Floor < .012;
    public double Swivel => Bend > .025 ? Lateral / Bend : 0;
    public double ToeSide(double yaw) => Direction.X * Math.Cos(yaw) - Direction.Z * Math.Sin(yaw);
    public double Distance(FootFrame other) => double.Hypot(X - other.X, Z - other.Z);
}

internal sealed class GaitMetrics
{
    public double V { get; private set; }
    public double X { get; private set; }
    public double Z { get; private set; }
    public int Frames { get; private set; }
    public int PlantedFrames { get; private set; }
    public double MaxSlide { get; private set; }
    public int TurningFrames { get; private set; }
    public double TurnSlide { get; private set; }
    public int StopFrames { get; private set; }
    public double StopSlide { get; private set; }
    public int SwingSamples { get; private set; }
    public double SwingRetreat { get; private set; }
    public double SwingRetreatAfterTakeoff { get; private set; }
    public double MaxSideSpeed { get; private set; }
    public double MaxBackSpeed { get; private set; }
    public double ClosestGap { get; private set; }
    public double KneeAlignment { get; private set; }
    public double MinPitch { get; private set; }
    public double MaxPitch { get; private set; }
    public double PivotDrift { get; private set; }
    public double PivotReach { get; private set; }
    public double PivotWidth { get; private set; }
    public int PivotFrames { get; private set; }
    public int OverlapFrames { get; private set; }
    public double OverlapDistance { get; private set; }
    public double TorsoLag { get; private set; }

    public static GaitMetrics Measure(GaitFrame[] frames)
    {
        if (frames.Length == 0) throw new InvalidOperationException("marcha sem quadros");
        var first = frames[0];
        var last = frames[^1];
        var result = new GaitMetrics { X = last.X, Z = last.Z, Frames = frames.Length };
        var origins = new (GaitFrame Frame, double Allowance)?[2];
        var elapsed = 0d;
        var measuredTime = 0d;
        var measuredDistance = 0d;
        var measuredFrames = 0;
        for (var i = 1; i < frames.Length; i++)
        {
            var previous = frames[i - 1];
            var current = frames[i];
            var travelled = previous.Distance(current);
            // Tempo e distância usam a mesma janela, após a rampa inicial.
            if (elapsed > .3)
            {
                measuredTime += current.G;
                measuredDistance += travelled;
                measuredFrames++;
                result.MeasureMotion(previous, current, travelled, origins);
            }
            elapsed += current.G;
        }

        if (measuredFrames < 3)
        {
            result.V = elapsed > 0 ? last.Distance(first) / elapsed : 0;
            return result;
        }

        result.V = measuredTime > 0 ? measuredDistance / measuredTime : 0;
        result.MeasureStop(frames);
        result.MeasurePivot(frames);
        result.ClosestGap = frames.Min(frame =>
            (frame.Feet[1].X - frame.Feet[0].X) * Math.Cos(frame.Yaw) -
            (frame.Feet[1].Z - frame.Feet[0].Z) * Math.Sin(frame.Yaw));
        var feet = frames.SelectMany(frame => frame.Feet);
        result.MinPitch = feet.Min(foot => foot.Pitch);
        result.MaxPitch = feet.Max(foot => foot.Pitch);
        result.KneeAlignment = frames.SelectMany(frame => frame.Feet
            .Where(foot => Math.Abs(foot.ToeSide(frame.Yaw)) > .45)
            .Select(foot => foot.Swivel * Math.Sign(foot.ToeSide(frame.Yaw)))).Append(0).Max();
        return result;
    }

    private void MeasureMotion(GaitFrame previous, GaitFrame current, double travelled,
        (GaitFrame Frame, double Allowance)?[] origins)
    {
        var (sin, cos) = Math.SinCos(current.Yaw);
        var dx = current.X - previous.X;
        var dz = current.Z - previous.Z;
        var turn = Math.Abs(current.Yaw - previous.Yaw);
        if (current.G > 0)
        {
            MaxSideSpeed = Math.Max(MaxSideSpeed, Math.Abs(dx * cos - dz * sin) / current.G);
            MaxBackSpeed = Math.Max(MaxBackSpeed, -(dx * sin + dz * cos) / current.G);
        }

        for (var foot = 0; foot < 2; foot++)
        {
            var a = previous.Feet[foot];
            var b = current.Feet[foot];
            if (b.Pitch <= 1e-6 || turn >= .001) origins[foot] = null;
            else if (a.Planted) origins[foot] = (previous, travelled);
            if (origins[foot] is { Frame: var start, Allowance: var allowance } && travelled > .02)
            {
                var planted = start.Feet[foot];
                var retreat = ((current.X - start.X) - (b.X - planted.X)) * sin +
                    ((current.Z - start.Z) - (b.Z - planted.Z)) * cos;
                SwingRetreat = Math.Max(SwingRetreat, retreat);
                SwingRetreatAfterTakeoff = Math.Max(SwingRetreatAfterTakeoff, retreat - allowance);
                SwingSamples++;
            }

            if (!a.Planted || !b.Planted || travelled >= .2) continue;
            var slide = b.Distance(a);
            if (travelled > .02)
            {
                PlantedFrames++;
                MaxSlide = Math.Max(MaxSlide, slide);
            }
            if (turn > .01)
            {
                TurningFrames++;
                TurnSlide = Math.Max(TurnSlide, slide);
            }
        }
    }

    private void MeasureStop(GaitFrame[] frames)
    {
        for (var i = frames.Length - 1; i > 0; i--)
        {
            var a = frames[i - 1];
            var b = frames[i];
            if (a.Distance(b) > 1e-6) break;
            for (var foot = 0; foot < 2; foot++)
            {
                if (!a.Feet[foot].Planted || !b.Feet[foot].Planted) continue;
                StopFrames++;
                StopSlide = Math.Max(StopSlide, a.Feet[foot].Distance(b.Feet[foot]));
            }
        }
    }

    private void MeasurePivot(GaitFrame[] frames)
    {
        var startYaw = frames[0].Yaw;
        var (sin, cos) = Math.SinCos(startYaw);
        GaitFrame? firstTurning = null;
        for (var i = 1; i < frames.Length; i++)
        {
            var previous = frames[i - 1];
            var current = frames[i];
            var yaw = current.Yaw - startYaw;
            var angle = Math.Abs(Math.Atan2(Math.Sin(yaw), Math.Cos(yaw)));
            if (angle > Math.PI * .31 && angle < Math.PI * .81)
            {
                PivotFrames++;
                if (angle <= Math.PI / 2)
                    PivotDrift = Math.Max(PivotDrift, current.Distance(firstTurning ??= current));
                PivotReach = Math.Max(PivotReach, current.Feet.Max(foot => double.Hypot(foot.X - current.X, foot.Z - current.Z)));
                PivotWidth = Math.Max(PivotWidth, current.Feet[0].Distance(current.Feet[1]));
                TorsoLag = Math.Max(TorsoLag, Math.Abs(current.TorsoYaw));
            }
            if (angle <= Math.PI / 2 || angle >= Math.PI * .96 || Math.Abs(current.Yaw - previous.Yaw) < .005)
                continue;
            var advance = (previous.X - current.X) * sin + (previous.Z - current.Z) * cos;
            if (advance <= .001) continue;
            OverlapFrames++;
            OverlapDistance += advance;
        }
    }
}
