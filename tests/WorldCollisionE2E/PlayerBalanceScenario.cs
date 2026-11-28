using Microsoft.Playwright;
using static BrowserTest;

internal static class PlayerBalanceScenario
{
    internal static async Task RunAsync(IBrowser browser)
    {
        var page = await browser.NewPageAsync();
        var errors = TrackErrors(page);
        await page.GotoAsync(GameUrl(forceWebGl: true));
        var build = await CheckBuildAsync(page);
        await WaitAsync(async () => await ReadAsync<string?>(page,
            "async () => (await globalThis.game?.models?.status())?.skinPreview") == "ready", "The default humanoid did not load.");
        await WaitAsync(async () => (await Snapshot(page)).Center.Y > .4, "The center of mass was not computed.");
        var initial = await Snapshot(page);
        Check(initial is { Profile: "normal", State: "stable", Gravity: 9.8 } && initial.Mass > 0 &&
            Math.Abs(initial.Weight - initial.Mass * 9.8) < .0001, "The player starts with normal balance and a measurable weight.", initial);
        Check(!await ReadAsync<bool>(page, "() => game.balance.setProfile('unknown')"),
            "Unknown balance profiles are rejected.");
        Check(await ReadAsync<bool>(page, "() => game.balance.setProfile('focused')") &&
            (await Snapshot(page)).Profile == "focused", "Events can select focused balance.");
        Check(await ReadAsync<bool>(page, "() => game.balance.impulse(20, 0)"), "A minor impulse is accepted.");
        await page.WaitForTimeoutAsync(500);
        Check((await Snapshot(page)).State != "fallen", "Focused balance recovers from a minor impulse.");

        Check(await ReadAsync<bool>(page, "() => game.balance.setProfile('drunk')"), "Events can select drunken balance.");
        Check(await ReadAsync<bool>(page, "() => game.balance.impulse(300, 0)"), "A strong lateral impulse is accepted.");
        await WaitAsync(async () => (await Snapshot(page)).State == "fallen", "The player should fall after losing support.");
        await WaitAsync(async () => await RenderAsync<double>(page, """
            () => BABYLON.EngineStore.LastCreatedScene.getTransformNodeByName('innerscene-player-preview').rotation.x
            """) < -1.54, "The visible avatar did not reach the ground pose.");

        var fall = await Snapshot(page);
        var orientation = await RenderAsync<double>(page, """
            () => BABYLON.EngineStore.LastCreatedScene.getTransformNodeByName('innerscene-player-preview').rotation.y
            """);
        Check(Math.Abs(Math.Abs(orientation) - Math.PI / 2) < .3, "The avatar falls towards the lateral impulse.", orientation);
        var before = await ReadAsync<Position>(page, "() => game.playerState()");
        await page.Keyboard.DownAsync("w");
        await page.WaitForTimeoutAsync(280);
        await page.Keyboard.UpAsync("w");
        var after = await ReadAsync<Position>(page, "() => game.playerState()");
        Check(double.Hypot(before.X - after.X, before.Z - after.Z) < .03, "A fallen avatar cannot walk.");
        await page.Keyboard.PressAsync("Space");
        await WaitAsync(async () => (await Snapshot(page)).State != "fallen", "Space should recover from a completed fall.");
        Check((await Snapshot(page)).Profile == "drunk", "Recovering preserves the game-selected profile.");
        Check(errors.IsEmpty, "Browser errors in balance scenario.", errors);
        Log(new { build, mode = Mode, initial, fall, orientation });
    }

    private static Task<BalanceState> Snapshot(IPage page) =>
        ReadAsync<BalanceState>(page, "() => game.balance.state()");

    private sealed record BalanceState(string Profile, string State, double Mass, double Weight, double Gravity, double Margin, Center Center);
    private sealed record Center(double X, double Y, double Z);
    private sealed record Position(double X, double Z);
}
