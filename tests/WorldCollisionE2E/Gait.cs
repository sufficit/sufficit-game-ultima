using static BrowserTest;

internal sealed partial class WorldCollision
{
    private async Task GaitAsync()
    {
        var walking = await SpeedAsync(["w"], 1800, false);
        Check(walking.PlantedFrames >= 5 && walking.MaxSlide < .012,
            "pé de apoio fixo durante a marcha em linha reta", walking);
        Check(walking.SwingSamples >= 3 && walking.SwingRetreatAfterTakeoff < .025,
            "pé levantado avança na passada", walking);
        Check(walking.MinPitch < -.1 && walking.MaxPitch > .1, "tornozelo alterna a inclinação", walking);
        var parents = await Probe<string?[]>("parents", new[] { "ankle-left", "wrist-right", "toe-1-left-tip" });
        Check(parents.SequenceEqual(["player-knee-left", "player-elbow-right", "player-toe-1-left"]),
            "jogador instancia o esqueleto humano", parents);

        await ResetAsync();
        await SettleAsync();
        var turning = await SpeedAsync(["w"], 1500, false, "d");
        Log(new { turning });
        Check(turning.MaxSideSpeed < 1.1 && turning.V > 2, "orientação antes de acelerar lateralmente", turning);
        Check(turning.TurningFrames >= 2 && turning.TurnSlide < .012, "pé plantado durante a curva", turning);

        await ResetAsync();
        await SettleAsync();
        var reversing = await SpeedAsync(["w"], 1500, false, "s");
        Log(new { reversing });
        Check(reversing.OverlapFrames >= 2 && reversing.OverlapDistance > .045, "avanço durante a inversão", reversing);
        Check(reversing.PivotFrames >= 3 && reversing.PivotDrift < .015 &&
            reversing.PivotReach < .23 && reversing.PivotWidth < .30, "eixo firme e base compacta", reversing);
        Check(turning.KneeAlignment > .2 && reversing.TorsoLag is > .12 and < .28,
            "joelho acompanha a curva e quadril conduz a inversão", new { turning, reversing });
        Check(reversing.ClosestGap >= .14, "sapatos sem cruzar sob o corpo", reversing);
        Check(reversing.MaxBackSpeed < .1, "avanço no sentido do quadril", reversing);
        Check(reversing.TurningFrames >= 2 && reversing.TurnSlide < .012, "pé plantado durante a inversão", reversing);
        Check(reversing.StopFrames >= 2 && reversing.StopSlide < .012, "pé plantado ao parar", reversing);

        await ResetAsync();
        await SettleAsync();
        var rests = new List<RestMetrics>();
        foreach (var (key, axis, target) in new[] { ("w", "z", .42), ("d", "x", .93), ("w", "z", 1.41) })
        {
            await TraverseAsync(key, 1.3, axis, target);
            rests.Add(await IdleAsync());
            await ResetAsync();
            await SettleAsync();
        }
        Log(new { rests });
        Check(rests.All(Stable), "postura estável com as duas solas apoiadas após parar", rests);
        Check(rests.All(rest => rest.MaxAnkleOffset < .025 && rest.MaxKneeBend < .65),
            "ao parar no chão, os tornozelos sustentam o quadril", rests);

        var diagonal = await SpeedAsync(["w", "d"], 1600, false);
        Log(new { diagonal });
        Check(diagonal.V is > 3.3 and < 3.7 && diagonal.X > 1 && Math.Abs(diagonal.X - diagonal.Z) < .08,
            "diagonal mantém rumo e velocidade de caminhada", diagonal);
        await ResetAsync();
        await SettleAsync();
        var running = await SpeedAsync(["w"], 1400);
        Log(new { walking, running });
        Check(walking.V > 2.2 && running.V > walking.V * 1.45, "Shift distingue caminhada e corrida", new { walking, running });
    }

    private async Task<GaitMetrics> SpeedAsync(string[] keys, int ms, bool sprint = true, string? turn = null)
    {
        await Probe<bool?>("startTrace", "gait");
        GaitFrame[] frames;
        try
        {
            var duration = ms / 1000d / (turn is null ? 1 : 2);
            if (sprint) await page.Keyboard.DownAsync("Shift");
            try
            {
                await WithKeysAsync(keys, false, () => AdvanceAsync(duration));
                if (turn is not null)
                    await WithKeysAsync([turn], false, () => AdvanceAsync(duration));
            }
            finally
            {
                if (sprint) await page.Keyboard.UpAsync("Shift");
            }
            if (turn is not null) await AdvanceAsync(.3);
        }
        finally
        {
            frames = await Probe<GaitFrame[]>("stopTrace");
        }
        await SettleAsync();
        CheckPageErrors();
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ULTIMA_TRACE")))
            Log(new { trace = frames.Select((frame, i) => new { frame.G, distance = i == 0 ? 0 : frame.Distance(frames[i - 1]) }) });
        return GaitMetrics.Measure(frames);
    }

    private async Task<RestMetrics> IdleAsync()
    {
        await Probe<bool?>("startTrace", "rest");
        RestFrame[] frames;
        try { await AdvanceAsync(1.5); }
        finally { frames = await Probe<RestFrame[]>("stopTrace"); }
        var elapsed = 0d;
        var stable = frames.Where(frame => (elapsed += frame.G) >= .7).ToArray();
        Check(stable.Length >= 15, "quadros de repouso suficientes", stable.Length);
        var first = stable[0];
        return new(stable.Length,
            stable.Max(frame => Math.Sqrt(Math.Pow(frame.X - first.X, 2) + Math.Pow(frame.Y - first.Y, 2) + Math.Pow(frame.Z - first.Z, 2))),
            stable.Max(frame => frame.Feet.Select((foot, i) => Math.Abs(foot.Ankle.Y - first.Feet[i].Ankle.Y)).Max()),
            stable.Max(frame => Magnitude(frame.Bend - first.Bend, frame.Roll - first.Roll)),
            stable.Max(frame => frame.Feet.Max(foot => Math.Abs(foot.Up))),
            stable.Max(frame => frame.Feet.Max(foot => Distance(foot.Ankle, foot.Hip))),
            stable.Max(frame => frame.Feet.Max(foot => foot.Knee)));
    }

    private static bool Stable(RestMetrics rest) => rest is
        { Frames: >= 15, RootDrift: < .001, VerticalDrift: < .001, Sway: < .002, MaxPitch: < .001 };

    private sealed record RestFoot(Position Ankle, Position Hip, double Up, double Knee);
    private sealed record RestFrame(double X, double Y, double Z, double G, double Bend, double Roll, RestFoot[] Feet);
    private sealed record RestMetrics(int Frames, double RootDrift, double VerticalDrift, double Sway,
        double MaxPitch, double MaxAnkleOffset, double MaxKneeBend);
}
