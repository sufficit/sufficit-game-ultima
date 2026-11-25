using Microsoft.Playwright;
using static BrowserTest;

internal static class CameraViews
{
    internal static async Task RunAsync(IBrowser browser)
    {
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1200, Height = 800 } });
        var errors = TrackErrors(page);
        await page.AddInitScriptAsync("""
            (() => {
                const pad = { id: 'Camera test pad', index: 0, connected: true, mapping: 'standard',
                    timestamp: 0, axes: [0, 0, 0, 0],
                    buttons: Array.from({ length: 17 }, () => ({ pressed: false, touched: false, value: 0 })) };
                globalThis.testPerspectivePad = pad;
                Object.defineProperty(navigator, 'getGamepads', { value: () => [pad] });
            })();
            """);
        await page.GotoAsync(GameUrl(forceWebGl: true), new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var build = await CheckBuildAsync(page);
        await WaitAsync(() => page.EvaluateAsync<bool>("async () => !!(await window.game?.playerState?.())"), "Garden is ready.");
        Check(await page.EvaluateAsync<string>("() => game.mode") == Mode, "Expected garden renderer.");

        var canvas = page.Locator("#game-canvas");
        await canvas.FocusAsync();
        await MiddleAsync(canvas);
        await VerifyAsync(office: false, view: 1);
        await SetR3Async(true);
        await VerifyAsync(office: false, view: 2);
        await Task.Delay(450);
        await VerifyAsync(office: false, view: 2);
        await SetR3Async(false);
        await page.Keyboard.PressAsync("v");
        await VerifyAsync(office: false, view: 0);
        await Button("Abrir painel do jogo").ClickAsync();
        await Button("Alternar câmera").ClickAsync();
        await VerifyAsync(office: false, view: 1);

        await Button("Ambientes").ClickAsync();
        await Button("Escritório").ClickAsync();
        canvas = page.Locator(".environment__scene canvas");
        await VerifyAsync(office: true, view: 0);
        await MiddleAsync(canvas);
        await VerifyAsync(office: true, view: 1);
        await SetR3Async(true);
        await VerifyAsync(office: true, view: 2);
        await Task.Delay(450);
        await VerifyAsync(office: true, view: 2);
        await SetR3Async(false);
        await page.Keyboard.PressAsync("v");
        await VerifyAsync(office: true, view: 0);
        await Button("Alternar câmera").ClickAsync();
        await VerifyAsync(office: true, view: 1);

        Check(errors.IsEmpty, "Perspective switches produce no page errors.", errors);
        Log(new { build, mode = Mode, garden = true, office = true, inputs = new[] { "middle", "R3", "V", "button" } });

        ILocator Button(string name) => page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });
        Task MiddleAsync(ILocator target) => target.ClickAsync(new() { Button = MouseButton.Middle });
        Task SetR3Async(bool pressed) => page.EvaluateAsync("pressed => testPerspectivePad.buttons[11].pressed = pressed", pressed);

        async Task VerifyAsync(bool office, int view)
        {
            var scene = office ? null : MainThread ? null : page.Workers[0];
            await WaitAsync(async () =>
            {
                if (!office && await page.EvaluateAsync<int>("async () => (await game.cameraState()).view") != view)
                    return false;
                return await ReadAsync<bool>(page, """
                    ({ office, view }) => {
                        const scene = globalThis.BABYLON?.EngineStore.LastCreatedScene;
                        const camera = scene?.activeCamera;
                        if (!camera || (office && camera.metadata !== view)) return false;
                        const player = scene.getTransformNodeByName(office ? 'environment-player' : 'player');
                        if (!player) return false;
                        const visible = office ? player.isEnabled() : player.getChildMeshes().some(
                            mesh => Boolean(mesh.layerMask & camera.layerMask));
                        if (visible === (view === 1)) return false;
                        const dx = camera.position.x - player.position.x, dz = camera.position.z - player.position.z;
                        if (view === 1)
                            return Math.hypot(dx, dz) < .02 && camera.position.y - player.position.y > 1.5;
                        if (view === 2)
                            return dx * Math.sin(player.rotation.y) + dz * Math.cos(player.rotation.y) > .1;
                        return Math.hypot(dx, dz) > .2;
                    }
                    """, new { office, view }, scene);
            }, $"Perspective {view} displayed and correctly placed in {(office ? "office" : "garden")}.", 20_000);
        }
    }
}
