using static BrowserTest;

internal sealed partial class WorldCollision
{
    private async Task ContactsAsync()
    {
        await AddRemoteAsync("pose-arm", "Contato em repouso", .63, 0);
        await WaitAsync(async () =>
            (await Probe<Position?>("nodePosition", "remote-pose-arm"))?.X == .63 &&
            await Probe<bool>("meshExists", "char-character.wanderer-arm-left-remote-pose-arm"),
            "avatar em repouso pronto");
        var forced = await Probe<ArmPose>("forceArms");
        Check(forced.Overlap, "a pose de teste produz interseção dos braços", forced);

        ArmPose? arm = null;
        await WaitAsync(async () =>
        {
            arm = await Probe<ArmPose>("armPose");
            return !arm.Overlap && Magnitude(arm.Player.X, arm.Player.Z) < .005 &&
                Math.Abs(Distance(arm.Player, arm.Remote) - .63) < .005;
        }, "articulações separam os braços sem mover os dois personagens");
        Log(new { armGap = Distance(arm!.Player, arm.Remote) });

        var frames = await Probe<ArmPose[]>("armFrames", 8).WaitAsync(TimeSpan.FromSeconds(20));
        Check(frames.Length == 8 && frames.All(frame => !frame.Overlap &&
            Magnitude(frame.Player.X, frame.Player.Z) < .005 &&
            Magnitude(frame.Remote.X - .63, frame.Remote.Z) < .005) &&
            frames.Any(frame => Math.Abs(frame.LeftBend) > .1 || Math.Abs(frame.RightBend) > .1),
            "os braços continuam separados nos quadros seguintes", frames);
        await Probe<bool?>("resetShoulder");
        await RemoveRemoteAsync("pose-arm");
        await SettleAsync();

        await AddRemoteAsync("contact-a", "Contato A", 0, 3);
        await AddRemoteAsync("contact-b", "Contato B", 0, 3);
        var avatarGap = 0d;
        await WaitAsync(async () =>
        {
            var a = await Probe<Position?>("nodePosition", "remote-contact-a");
            var b = await Probe<Position?>("nodePosition", "remote-contact-b");
            if (a is null || b is null || Math.Abs(a.Z - 3) > .65 || Math.Abs(b.Z - 3) > .65) return false;
            avatarGap = Distance(a, b);
            return avatarGap is >= .33 and <= .69;
        }, "duas poses sobrepostas se separam perto da silhueta");
        await RemoveRemoteAsync("contact-b");

        await Probe<bool?>("startTrace", "impact");
        try
        {
            await WithKeysAsync(["w"], true, async () =>
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                var maxZ = 0d;
                var minBend = 0d;
                while (!timeout.IsCancellationRequested)
                {
                    var batch = await Probe<Impact[]>("drainTrace");
                    foreach (var frame in batch)
                    {
                        maxZ = Math.Max(maxZ, frame.Z);
                        minBend = Math.Min(minBend, frame.Bend);
                        if (frame is { Z: >= 2.32 and <= 2.85, Bend: < -.16, Gap: < .1 })
                        {
                            Log(new { avatarGap, stumble = frame });
                            return;
                        }
                    }
                    await Task.Delay(15);
                }
                Check(false, "choque não observado", new { maxZ, minBend });
            });
        }
        finally
        {
            await Probe<Impact[]>("stopTrace");
        }

        var beside = await TraverseAsync("d", 1.3, "x", .85);
        var around = await TraverseAsync("w", 1.1, "z", 3.7);
        Check(beside.X > .8 && around.Z > 3.7, "é possível contornar os personagens", new { beside, around });
        await RemoveRemoteAsync("contact-a");
        await ResetAsync();
        await SettleAsync();
        await AddRemoteAsync("contact-side", "Contato lateral", 1.8, 0);
        await WaitAsync(async () => (await Probe<Position?>("nodePosition", "remote-contact-side"))?.X == 1.8,
            "personagem lateral pronto");
        SideContact[] contacts = [];
        await WithKeysAsync(["d"], false, async () => contacts = await Probe<SideContact[]>("sideContactFrames", "remote-contact-side"));
        Check(contacts.Any(frame => frame.X is > 1 and < 1.66 && frame.Gap < .14) &&
            contacts.All(frame => frame.X < 1.66), "reaches the lateral blocker without passing through",
            contacts.OrderBy(frame => frame.Gap).Take(3));
        var sideContact = contacts[^1];
        await RemoveRemoteAsync("contact-side");
        await ResetAsync();
        await SettleAsync();
        await AddRemoteAsync("contact-gap-left", "Passagem esquerda", -.60, 2.65);
        await AddRemoteAsync("contact-gap-right", "Passagem direita", .60, 2.65);
        await WaitAsync(async () =>
            (await Probe<Position?>("nodePosition", "remote-contact-gap-left"))?.Z == 2.65 &&
            (await Probe<Position?>("nodePosition", "remote-contact-gap-right"))?.Z == 2.65,
            "passagem entre personagens pronta");
        var throughGap = await TraverseAsync("w", 1.37, "z", 3.45);
        Check(throughGap.Z > 3.45 && Math.Abs(throughGap.X) < .15, "passa entre dois corpos com espaço livre", throughGap);
        Log(new { sideContact, beside, around, throughGap });
        await RemoveRemoteAsync("contact-gap-left");
        await RemoveRemoteAsync("contact-gap-right");
        await ResetAsync();
        await SettleAsync();
    }

    private static double Distance(Position a, Position b) => Magnitude(a.X - b.X, a.Z - b.Z);
    private static double Magnitude(double x, double z) => double.Hypot(x, z);

    private sealed record ArmPose(bool Overlap, Position Player, Position Remote, double LeftBend, double RightBend);
    private sealed record Impact(double Z, double Bend, double Gap);
    private sealed record SideContact(double X, double Gap);
}
