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
        await VerifyLookKeysAsync(office: false);
        await MiddleAsync(canvas);
        await VerifyAsync(office: false, view: 1);
        await VerifyLookKeysAsync(office: false);
        await SetR3Async(true);
        await VerifyAsync(office: false, view: 2);
        await Task.Delay(450);
        await VerifyAsync(office: false, view: 2);
        await SetR3Async(false);
        await VerifyLookKeysAsync(office: false);
        await VerifyCloseMouseLookAsync(office: false);
        await page.Keyboard.PressAsync("v");
        await VerifyAsync(office: false, view: 0);
        await Button("Abrir painel do jogo").ClickAsync();
        await Button("Alternar câmera").ClickAsync();
        await VerifyAsync(office: false, view: 1);
        await VerifyPointerLockAsync(office: false);

        await Button("Ambientes").ClickAsync();
        await Button("Escritório").ClickAsync();
        canvas = page.Locator(".environment__scene canvas");
        await VerifyAsync(office: true, view: 0);
        await canvas.FocusAsync();
        await VerifyLookKeysAsync(office: true);
        await MiddleAsync(canvas);
        await VerifyAsync(office: true, view: 1);
        await VerifyLookKeysAsync(office: true);
        await SetR3Async(true);
        await VerifyAsync(office: true, view: 2);
        await Task.Delay(450);
        await VerifyAsync(office: true, view: 2);
        await SetR3Async(false);
        await VerifyLookKeysAsync(office: true);
        await VerifyCloseMouseLookAsync(office: true);
        await page.Keyboard.PressAsync("v");
        await VerifyAsync(office: true, view: 0);
        await Button("Alternar câmera").ClickAsync();
        await VerifyAsync(office: true, view: 1);
        await VerifyPointerLockAsync(office: true);

        Check(errors.IsEmpty, "Perspective switches produce no page errors.", errors);
        Log(new { build, mode = Mode, garden = true, office = true, inputs = new[] { "middle", "R3", "V", "button", "arrow-look", "mouse-look", "escape" } });

        ILocator Button(string name) => page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });
        Task MiddleAsync(ILocator target) => target.ClickAsync(new() { Button = MouseButton.Middle });
        Task SetR3Async(bool pressed) => page.EvaluateAsync("pressed => testPerspectivePad.buttons[11].pressed = pressed", pressed);

        async Task<LookPose> ReadLookAsync(bool office)
        {
            var worker = office || MainThread ? null : page.Workers[0];
            return await ReadAsync<LookPose>(page, """
                office => {
                    const scene = BABYLON.EngineStore.LastCreatedScene;
                    const player = scene.getTransformNodeByName(office ? 'environment-player' : 'player');
                    const direction = scene.activeCamera.getForwardRay().direction;
                    return { x: player.position.x, z: player.position.z,
                        yaw: Math.atan2(direction.x, direction.z), vertical: direction.y, heading: player.rotation.y };
                }
                """, office, worker);
        }

        async Task VerifyPointerLockAsync(bool office)
        {
            await canvas.FocusAsync();
            await page.Keyboard.PressAsync("v");
            await VerifyAsync(office, view: 2);
            await WaitAsync(() => page.EvaluateAsync<bool>("() => document.pointerLockElement !== null && document.pointerLockElement === document.activeElement"),
                "Keyboard selection captures the mouse without clicking the game surface.", 4_000, 25);
            await VerifyCloseMouseLookAsync(office, captured: true);
            await page.Keyboard.PressAsync("v");
            await VerifyAsync(office, view: 0);
        }

        async Task VerifyCloseMouseLookAsync(bool office, bool captured = false)
        {
            var before = await ReadLookAsync(office);
            var bounds = await canvas.BoundingBoxAsync() ?? throw new InvalidOperationException("Missing game surface.");
            var x = bounds.X + bounds.Width / 2;
            var y = bounds.Y + bounds.Height / 2;
            if (captured)
            {
                // Chromium's headless mouse driver injects absolute positions even under pointer lock.
                await canvas.EvaluateAsync("""
                    canvas => {
                        canvas.dispatchEvent(new MouseEvent('mousemove', { movementX: 100, movementY: 35 }));
                    }
                    """);
            }
            else
            {
                await page.Mouse.MoveAsync(x, y);
                await page.Mouse.MoveAsync(x + 100, y + 35, new() { Steps = 5 });
            }
            var after = before;
            try
            {
                await WaitAsync(async () =>
                {
                    after = await ReadLookAsync(office);
                    return AngleDelta(after.Yaw, before.Yaw) > .1 && Math.Abs(after.Vertical - before.Vertical) > .04 &&
                        AngleDelta(after.Heading, after.Yaw) < .05 && double.Hypot(after.X - before.X, after.Z - before.Z) < .01;
                }, "Mouse motion alone turns and tilts the close camera and aims the stationary character.", 4_000, 25);
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException($"{exception.Message} {new { office, captured, before, after }}", exception);
            }

            await page.Keyboard.PressAsync("Escape");
            await WaitAsync(() => page.EvaluateAsync<bool>("() => document.pointerLockElement === null"),
                "Escape releases mouse capture.", 4_000, 25);
            var released = await ReadLookAsync(office);
            await page.Mouse.MoveAsync(x - 90, y - 35, new() { Steps = 5 });
            await Task.Delay(200);
            Check(AngleDelta((await ReadLookAsync(office)).Yaw, released.Yaw) < .02,
                "Escape stops mouse look until the player resumes.");
        }

        async Task VerifyLookKeysAsync(bool office)
        {
            var before = await ReadLookAsync(office);
            await page.Keyboard.DownAsync("ArrowRight");
            try
            {
                await WaitAsync(async () => Math.Abs(Math.IEEERemainder((await ReadLookAsync(office)).Yaw - before.Yaw, Math.Tau)) > .08,
                    "The right arrow turns the camera.", 4_000, 25);
            }
            finally { await page.Keyboard.UpAsync("ArrowRight"); }

            var horizontal = await ReadLookAsync(office);
            await page.Keyboard.DownAsync("ArrowUp");
            try
            {
                await WaitAsync(async () => Math.Abs((await ReadLookAsync(office)).Vertical - horizontal.Vertical) > .045,
                    "The up arrow tilts the camera.", 4_000, 25);
            }
            finally { await page.Keyboard.UpAsync("ArrowUp"); }

            var after = await ReadLookAsync(office);
            Check(double.Hypot(after.X - before.X, after.Z - before.Z) < .01,
                "Arrow keys do not translate the player.", new { before, after, office });
        }

        async Task VerifyAsync(bool office, int view)
        {
            var scene = office ? null : MainThread ? null : page.Workers[0];
            try
            {
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
                                return Math.hypot(dx, dz) < 2.16 &&
                                    dx * Math.sin(player.rotation.y) + dz * Math.cos(player.rotation.y) < -.1;
                            return Math.hypot(dx, dz) > .2;
                        }
                        """, new { office, view }, scene);
                }, $"Perspective {view} displayed and correctly placed in {(office ? "office" : "garden")}.", 20_000);
            }
            catch (InvalidOperationException)
            {
                var details = await ReadAsync<string>(page, """
                    office => {
                        const scene = BABYLON.EngineStore.LastCreatedScene;
                        const camera = scene.activeCamera;
                        const player = scene.getTransformNodeByName(office ? 'environment-player' : 'player');
                        return JSON.stringify({ camera: camera.name, position: camera.position.asArray(),
                            player: player.position.asArray(), heading: player.rotation.y, mask: camera.layerMask });
                    }
                    """, office, scene);
                Console.Error.WriteLine(details);
                foreach (var error in errors) Console.Error.WriteLine(error);
                throw;
            }
        }
    }

    private static double AngleDelta(double a, double b) => Math.Abs(Math.IEEERemainder(a - b, Math.Tau));

    private sealed record LookPose(double X, double Z, double Yaw, double Vertical, double Heading);
}
