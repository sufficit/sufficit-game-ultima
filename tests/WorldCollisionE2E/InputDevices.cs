using Microsoft.Playwright;
using static BrowserTest;

internal static class InputDevices
{
    internal static async Task RunAsync(IBrowser browser)
    {
        var page = await browser.NewPageAsync(new()
        {
            HasTouch = true,
            IsMobile = true,
            ViewportSize = new() { Width = 390, Height = 844 },
        });
        var errors = TrackErrors(page);
        var consoleErrors = new System.Collections.Concurrent.ConcurrentQueue<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error") consoleErrors.Enqueue(message.Text);
        };
        await page.AddInitScriptAsync("""
            (() => {
                const pad = { id: 'Controle de teste', index: 0, connected: true, mapping: 'standard',
                    timestamp: 0, axes: [0, 0, 0, 0],
                    buttons: Array.from({ length: 17 }, () => ({ pressed: false, touched: false, value: 0 })) };
                globalThis.testInput = { pad, polls: 0, axisMessages: 0 };
                Object.defineProperty(navigator, 'getGamepads', { value: () => {
                    testInput.polls++;
                    return [pad.connected ? pad : null];
                } });
                const send = Worker.prototype.postMessage;
                Worker.prototype.postMessage = function (message, ...options) {
                    if (message?.type === 'input' && message.method === 'pad') { testInput.axisMessages++; }
                    return send.call(this, message, ...options);
                };
            })();
            """);
        await page.GotoAsync(GameUrl(forceWebGl: true), new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var build = await CheckBuildAsync(page);
        var timeout = LoadTimeoutMs(180);
        Task<Position?> PositionAsync() => ReadAsync<Position?>(page, "async () => await window.game?.playerState?.()");
        try
        {
            await WaitAsync(async () => await PositionAsync() is not null, "o personagem não carregou", timeout);
        }
        catch
        {
            Log(new { mode = Mode, errors = errors.ToArray(), consoleErrors = consoleErrors.ToArray() });
            throw;
        }
        Check(await page.EvaluateAsync<string>("() => game.mode") == Mode, "modo do renderizador", Mode);

        var baseline = await SampleAsync(page);
        var observationOnly = Environment.GetEnvironmentVariable("ULTIMA_INPUT_BASELINE") == "1";
        Log(new { build, mode = Mode, idleControl = baseline, observationOnly });
        if (observationOnly) return;
        Check(baseline.AxisMessages == 0, "o gamepad em repouso não envia eixos ao worker", baseline);

        var canvas = page.Locator("#game-canvas");
        await canvas.FocusAsync();
        var start = (await PositionAsync())!;
        await page.Keyboard.DownAsync("w");
        await MovedAsync(start, "W moves the player.");
        await page.EvaluateAsync("""
            () => {
                const input = document.createElement('input');
                input.id = 'input-test-field';
                document.body.append(input);
                input.focus();
            }
            """);
        await StableAsync("o personagem continuou andando ao focar campo de texto");
        await page.Keyboard.UpAsync("w");
        await page.EvaluateAsync("() => document.getElementById('input-test-field').remove()");
        await canvas.FocusAsync();

        await page.EvaluateAsync("() => testInput.pad.axes = [.1, -.1, .1, -.1]");
        await SampleAsync(page);
        Check(await InputModeAsync() == "keyboard", "ruído do controle alterou as dicas");
        await StableAsync("ruído do controle moveu o jogador");

        await page.EvaluateAsync("() => game.resetPlayer()");
        await WaitAsync(async () => (await PositionAsync())!.Distance(new(0, 0)) < 0.1,
            "o jogador não voltou à origem", timeout);
        start = (await PositionAsync())!;
        await page.EvaluateAsync("() => testInput.pad.axes = [0, -1, 0, 0]");
        await MovedAsync(start, "o analógico do gamepad não moveu o jogador");
        await WaitAsync(async () => await InputModeAsync() == "gamepad", "dicas do gamepad", timeout);
        var held = await SampleAsync(page);
        Check(held.AxisMessages == 0, "eixos inalterados não são reenviados", held);
        await page.EvaluateAsync("() => testInput.pad.connected = false");
        await WaitAsync(async () => await InputModeAsync() == "keyboard", "dicas após desconectar", timeout);
        await StableAsync("desconectar o controle deixou o analógico ativo");

        await page.EvaluateAsync("() => game.resetPlayer()");
        await WaitAsync(async () => (await PositionAsync())!.Distance(new(0, 0)) < 0.1,
            "o jogador não voltou à origem para o teste de toque", timeout);
        start = (await PositionAsync())!;
        await page.Locator("#move-stick").WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await TouchAsync("pointerdown", 1);
        await TouchAsync("pointerdown", 2);
        await TouchAsync("pointerup", 2);
        await MovedAsync(start, "o segundo toque interrompeu o analógico do primeiro");
        await TouchAsync("pointercancel", 1);
        await StableAsync("cancelar o toque deixou o analógico ativo");
        Check(await InputModeAsync() == "touch", "dicas de toque");

        await page.EvaluateAsync("() => { testInput.pad.axes.fill(0); testInput.pad.connected = true; }");
        Check(await page.EvaluateAsync<bool>("() => game.teleportToGuardian()"), "o jogador chegou à Guardiã");
        await page.Locator(".ultima-hint").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeout });
        await ButtonAsync(0, true);
        var dialog = page.Locator(".ultima-dialog");
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeout });
        await page.Keyboard.PressAsync("Escape");
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = timeout });
        await SampleAsync(page);
        Check(!await dialog.IsVisibleAsync(), "segurar o botão A reabriu a conversa");
        await ButtonAsync(0, false);
        await SampleAsync(page);
        await ButtonAsync(0, true);
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeout });
        await ButtonAsync(0, false);
        await ButtonAsync(1, true);
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = timeout });
        await ButtonAsync(1, false);

        Check(errors.IsEmpty, "erros no navegador", errors);
        Log(new { build, mode = Mode, heldControl = held, keyboard = true, touch = true, gamepad = true });
        await page.CloseAsync();

        Task<string> InputModeAsync() => page.EvaluateAsync<string>("() => document.body.dataset.input || 'keyboard'");
        Task ButtonAsync(int index, bool pressed) => page.EvaluateAsync(
            "({ index, pressed }) => testInput.pad.buttons[index].pressed = pressed", new { index, pressed });

        Task TouchAsync(string type, int pointerId) => page.EvaluateAsync("""
            ({ type, pointerId }) => {
                const stick = document.getElementById('move-stick');
                const r = stick.getBoundingClientRect();
                stick.dispatchEvent(new PointerEvent(type, { pointerId, pointerType: 'touch',
                    bubbles: true, clientX: r.x + r.width / 2, clientY: r.y + r.height * .1 }));
            }
            """, new { type, pointerId });

        Task MovedAsync(Position origin, string message) =>
            WaitAsync(async () => (await PositionAsync())!.Distance(origin) > 0.25, message, timeout);

        // Resolves after `count` frames of the renderer that runs the simulation: the page on the
        // main thread, the worker (OffscreenCanvas) otherwise.
        const string Frames = """
            count => new Promise(resolve => {
                let n = 0;
                const step = () => { if (++n >= count) { resolve(n); return; } requestAnimationFrame(step); };
                requestAnimationFrame(step);
            })
            """;

        async Task StableAsync(string message)
        {
            // Releasing the stick reaches the simulation on its next frame, and the deceleration
            // that follows takes a few frames more. Count frames of the renderer, not gamepad
            // polls of the main thread: the two rates have no fixed relation, and with software
            // rendering one renderer frame can outlast a whole window of polls. The player must
            // come to rest and stay put.
            const int window = 12;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var frames = 0;
            var still = 0;
            var last = (await PositionAsync())!;
            while (still < window && frames < 240 && clock.ElapsedMilliseconds < 40_000)
            {
                frames += await RenderAsync<int>(page, Frames, 4);
                var now = (await PositionAsync())!;
                still = now.Distance(last) < 0.002 ? still + 4 : 0;
                last = now;
            }
            Log(new { stable = message, frames, seconds = Math.Round(clock.Elapsed.TotalSeconds, 1) });
            Check(still >= window, message, new { frames, still, position = last });
        }
    }

    private static Task<Sample> SampleAsync(IPage page) => ReadAsync<Sample>(page, """
        () => new Promise(resolve => {
            const start = testInput.polls, messages = testInput.axisMessages;
            function wait() {
                if (testInput.polls - start < 12) { requestAnimationFrame(wait); return; }
                resolve({ polls: testInput.polls - start, axisMessages: testInput.axisMessages - messages });
            }
            requestAnimationFrame(wait);
        })
        """);

    private sealed record Sample(int Polls, int AxisMessages);

    private sealed record Position(double X, double Z)
    {
        internal double Distance(Position other) => double.Hypot(X - other.X, Z - other.Z);
    }
}
