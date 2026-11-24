using System.Text.Json;

internal sealed partial class WorldCollision
{
    private async Task TerrainAsync(string? build)
    {
        await ResetAsync();
        await ApplyAsync(Tree);
        await SettleAsync();
        var blocked = await WalkAsync(["w"], 1150);
        Log(new { build, mode = Mode, blocked });
        Check(blocked.Z is > 1.28 and < 1.61, "o tronco deve parar o jogador em z≈1,47", blocked);
        await page.Locator(".ultima-hint").GetByText("para examinar").WaitForAsync();
        await page.Keyboard.PressAsync("e");
        await page.GetByText("Você examinou: Pinheiro", new() { Exact = true }).WaitForAsync();
        await WaitAsync(async () => await Probe<double>("torsoBend") > .10, "o jogador se inclina ao examinar");

        var slide = await WalkAsync(["w", "d"], 450);
        Check(slide.X > .45 && slide.Z > 2, "desliza pela lateral", slide);
        await RemoveAsync(Tree.Uid);
        await ResetAsync();
        await SettleAsync();
        var free = await WalkAsync(["w"], 820);
        Check(free.Z > 2.65, "remoção libera o caminho", free);
        var soft = await WalkPastPropAsync(Tree with { Uid = "browser-soft", Asset = "flora.fern" });
        Check(soft.Z > 2.65, "samambaia permite passagem", soft);
        var stump = await WalkPastPropAsync(Tree with { Uid = "browser-stump", Asset = "prop.stump", Scale = 1.45 });
        Check(stump.Z is > .7 and < 1.55, "toco sólido", stump);
        var rock = await WalkPastPropAsync(Tree with { Uid = "browser-rock", Asset = "rock.boulder2" });
        Check(rock.Z is > .45 and < 1.7, "pedra sólida", rock);

        await StepOverLogAsync();
        var highLog = await WalkPastPropAsync(Tree with { Uid = "browser-high-log", Asset = "prop.log", Scale = .95 });
        Check(highLog.Z is > .5 and < 1.55 && highLog.Y < .19, "tronco acima do joelho bloqueia", highLog);

        Position? nativeTree = null;
        if (MainThread)
        {
            var tree = await Probe<Position?>("placeAtNativeTree") ?? throw new InvalidOperationException("tronco DLL");
            await SettleAsync();
            nativeTree = await WalkAsync(["w"], 480);
            Check(Math.Abs(nativeTree.X - tree.X) < .1 && nativeTree.Z < tree.Z - .38 && nativeTree.Z > tree.Z - .95,
                "pinheiro do cenário bloqueia", new { nativeTree, tree });
        }
        Log(new { slide, free, soft, stump, rock, highLog, nativeTree });
    }

    private async Task<Position> WalkPastPropAsync(WorldEdit prop)
    {
        await ResetAsync();
        await ApplyAsync(prop);
        await SettleAsync();
        var position = await WalkAsync(["w"], 850);
        await RemoveAsync(prop.Uid);
        return position;
    }

    private async Task StepOverLogAsync()
    {
        const string id = "browser-log";
        await ResetAsync();
        await ApplyAsync(Tree with { Uid = id, Asset = "prop.log", Scale = .56 });
        await SettleAsync(400);
        await Probe<JsonElement>("startTrace", "step");
        StepFrame[] samples;
        try
        {
            await WithKeysAsync(["w"], false, () => WaitAsync(async () =>
                (await Probe<Position>("position")).Z > 3.2, "o jogador não passou do tronco"));
            await SettleAsync();
        }
        finally
        {
            samples = await Probe<StepFrame[]>("stopTrace");
        }
        var overLog = samples.Where(sample => sample.Z is > 1.4 and < 2.6).ToArray();
        Check(overLog.Length > 0, "registra quadros durante a passagem pelo tronco");
        var step = new
        {
            AnklePeak = overLog.Max(sample => sample.Heights.Max()),
            Flex = overLog.Max(sample => sample.Knees.Max()),
            FeetDifference = overLog.Max(sample => Math.Abs(sample.Heights[0] - sample.Heights[1])),
            End = await PositionAsync()
        };
        Log(new { step });
        Check(step.AnklePeak > .28 && step.End.Z > 3.2 && step.End.Y < .19, "a perna sobe sobre o tronco e o jogador volta ao chão", step);
        Check(step.Flex > .55 && step.FeetDifference > .10, "joelho flexiona e os pés alternam apoio", step);

        var contact = await Probe<LogSample[]>("logContact", new[] { 1.7, 1.8, 1.9, 2, 2.1, 2.2, 2.3 }).WaitAsync(TimeSpan.FromSeconds(20));
        Log(new { contact });
        Check(contact.Any(sample => sample.Feet.All(foot => foot.Log.HasValue)), "os pés chegam ao topo do tronco", contact);
        Check(contact.All(sample => sample.Feet.All(foot => Math.Abs(foot.Sole - Math.Max(foot.Floor, foot.Log ?? foot.Floor)) < .04)),
            "cada sola deve ficar a menos de 4 cm do tronco ou do chão, mesmo nas bordas", contact);
        var restOnLog = await IdleAsync();
        Log(new { restOnLog });
        Check(Stable(restOnLog), "postura estável com as duas solas apoiadas sobre o tronco", restOnLog);
        await RemoveAsync(id);
    }

    private sealed record StepFrame(double Z, double[] Heights, double[] Knees);
    private sealed record LogSample(double Z, FootContact[] Feet);
    private sealed record FootContact(double Sole, double Floor, double? Log);
}
