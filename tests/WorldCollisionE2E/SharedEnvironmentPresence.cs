using Microsoft.Playwright;
using static BrowserTest;

internal static class SharedEnvironmentPresence
{
    internal static async Task RunAsync(IBrowser browser)
    {
        var api = Environment.GetEnvironmentVariable("ULTIMA_API");
        Check(Uri.TryCreate(api, UriKind.Absolute, out var endpoint) && endpoint.IsLoopback,
            "Use a disposable loopback signaling server.");
        var room = "office-" + Guid.NewGuid().ToString("N")[..12];
        var garden = new UriBuilder(GameUrl(forceWebGl: true))
        {
            Query = $"live=0&gpu=0&sala={room}&api={Uri.EscapeDataString(api!)}" + (MainThread ? "&render=main" : "")
        };
        var first = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1100, Height = 720 } });
        var second = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1100, Height = 720 } });
        var firstErrors = TrackErrors(first);
        var secondErrors = TrackErrors(second);
        await Task.WhenAll(first.GotoAsync(garden.Uri.AbsoluteUri), second.GotoAsync(garden.Uri.AbsoluteUri));
        var build = await CheckBuildAsync(first);
        var firstId = await IdentityAsync(first);
        var secondId = await IdentityAsync(second);
        Check(firstId != secondId, "Each browser has its own peer identity.");

        await SeenAsync(first, secondId);
        await SeenAsync(second, firstId);
        await OpenOfficeAsync(first);
        await WaitAsync(async () => await PositionAsync(second, firstId) is null,
            "The office avatar leaves the garden scene.");
        await WaitAsync(async () => await PositionAsync(first, secondId) is null, "A garden peer stays out of the office.");
        await OpenOfficeAsync(second);
        await SeenAsync(first, secondId);
        await SeenAsync(second, firstId);
        Check(await IdentityAsync(first) == firstId && await IdentityAsync(second) == secondId, "Navigation keeps the players in the same room.");

        var before = await PositionAsync(second, firstId) ?? throw new InvalidOperationException("The remote avatar is missing.");
        await first.Locator("#game-canvas").FocusAsync();
        await first.Keyboard.DownAsync("w");
        try
        {
            await WaitAsync(async () => await PositionAsync(second, firstId) is { } after &&
                double.Hypot(after.X - before.X, after.Z - before.Z) > .25,
                "The other player sees the office avatar walk.", 15_000);
        }
        finally { await first.Keyboard.UpAsync("w"); }

        await Button(first, "Trocar ambiente").ClickAsync();
        await Button(first, "Jardim").ClickAsync();
        await WaitAsync(async () => await PositionAsync(second, firstId) is null, "Returning to the garden clears the office avatar.");
        Check(firstErrors.IsEmpty && secondErrors.IsEmpty, "Both players use the room without browser errors",
            new { firstErrors, secondErrors });
        Log(new { build, mode = Mode, room, garden = true, office = true, movement = true });

        async Task<string> IdentityAsync(IPage page)
        {
            var info = await ReadAsync<Peer>(page, "() => game.net.info()");
            Check(info.Room == room, "Expected shared room", info);
            return info.Id;
        }

        async Task OpenOfficeAsync(IPage page)
        {
            await Button(page, "Abrir painel do jogo").ClickAsync();
            await Button(page, "Ambientes").ClickAsync();
            await Button(page, "Escritório").ClickAsync();
            await page.GetByRole(AriaRole.Heading, new() { Name = "Escritório", Exact = true }).WaitForAsync();
            await WaitAsync(async () => await page.EvaluateAsync<string?>("() => window.game?.mode") == Mode &&
                await page.EvaluateAsync<string?>("async () => await game.coreId()") is not null, "The office engine is ready.");
        }
    }

    private static ILocator Button(IPage page, string name) =>
        page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });

    private static Task<Position?> PositionAsync(IPage page, string id)
    {
        if (!MainThread && page.Workers.Count == 0) return Task.FromResult<Position?>(null);
        return RenderAsync<Position?>(page, """
            id => {
                const scene = globalThis.BABYLON?.EngineStore.LastCreatedScene;
                const actor = scene?.getTransformNodeByName('remote-' + id);
                return actor?.isEnabled() ? { x: actor.position.x, z: actor.position.z } : null;
            }
            """, id);
    }

    private static Task SeenAsync(IPage page, string id) =>
        WaitAsync(async () => await PositionAsync(page, id) is not null, "Both players see the avatar in the same environment.");

    private sealed record Peer(string Id, string Room);
    private sealed record Position(double X, double Z);
}
