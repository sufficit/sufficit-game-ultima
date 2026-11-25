using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static BrowserTest;

/// <summary>
/// Layout e controles de toque: geometria do HUD em retrato, paisagem, tablet e desktop,
/// sticks e botões A/B por toque, menu móvel, diálogo com teclado virtual simulado e a
/// regressão do desktop. Substitui tests/ui-mobile.e2e.js. Roda na thread principal
/// (render=main), como o legado: no headless com SwiftShader o quadro chega a 1–2 por
/// segundo, e todas as esperas são por sondagem, nunca por quadro de animação.
/// </summary>
internal static class MobileLayout
{
    private const string IsolatedApi = "http://127.0.0.1:19999";
    private static readonly Regex SourcePosition = new(@":\d+:\d+$", RegexOptions.CultureInvariant);
    private static readonly string? Screenshots = Environment.GetEnvironmentVariable("ULTIMA_SCREENSHOTS");

    private static readonly string[] Hud =
    [
        ".ultima-status", ".ultima-panel-toggle", ".ultima-panel", ".ultima-chat-log", "form.ultima-chat-compose",
        ".ultima-dialog", ".ultima-menu", "#ultima-menu-fab", ".ultima-menu-backdrop",
    ];
    private static readonly string[] Touch =
        ["#move-stick", "#move-thumb", "#look-stick", "#look-thumb", "#touch-action", "#touch-cancel"];

    private static readonly View[] Views =
    [
        new("retrato", 390, 844, true), new("paisagem", 844, 390, true),
        new("desktop", 1280, 720, false), new("tablet", 768, 1024, true),
    ];

    // Medição na página: retângulos visíveis do HUD e dos controles de toque, canvas,
    // rolagem do documento, alvos menores que 48 px e sobreposições entre elementos.
    private const string Measure = """
        arg => {
            const visible = (el) => {
                if (!el) return false;
                const r = el.getBoundingClientRect();
                if (r.width < 1 || r.height < 1) return false;
                const s = getComputedStyle(el);
                return s.display !== 'none' && s.visibility !== 'hidden';
            };
            const rect = (q) => {
                const el = document.querySelector(q);
                if (!el || !visible(el)) return null;
                const r = el.getBoundingClientRect();
                return { x: r.x, y: r.y, w: r.width, h: r.height };
            };
            const out = { viewport: [innerWidth, innerHeight], rects: {}, touch: {}, smallTouchTargets: [], overlaps: [] };
            for (const s of arg.sel) { const r = rect(s); if (r) out.rects[s] = r; }
            for (const s of arg.touch) { const r = rect(s); if (r) out.touch[s] = r; }
            const canvas = document.getElementById('game-canvas');
            const cr = canvas && canvas.getBoundingClientRect();
            out.canvas = cr ? { x: cr.x, y: cr.y, w: cr.width, h: cr.height } : null;
            const se = document.scrollingElement;
            out.page = { scrollW: se.scrollWidth, clientW: se.clientWidth, scrollH: se.scrollHeight, clientH: se.clientHeight };
            const small = [];
            for (const el of document.querySelectorAll('button, input, select, textarea, a[href], summary, [role="button"]')) {
                if (!visible(el) || el.closest('.ultima-chat-log')) continue;
                const r = el.getBoundingClientRect();
                if (r.width < 48 || r.height < 48) {
                    const name = el.id ? '#' + el.id : el.tagName.toLowerCase()
                        + (typeof el.className === 'string' && el.className ? '.' + el.className.split(' ')[0] : '');
                    small.push(name + ' ' + r.width.toFixed(0) + 'x' + r.height.toFixed(0));
                }
            }
            out.smallTouchTargets = small.slice(0, 12);
            const rects = [...arg.sel, ...arg.touch].map((n) => ({ n, r: rect(n) })).filter((o) => o.r);
            for (let i = 0; i < rects.length; i++) for (let j = i + 1; j < rects.length; j++) {
                // o polegar vive dentro do próprio stick: pares pai→filho não são sobreposição
                const a = document.querySelector(rects[i].n), b = document.querySelector(rects[j].n);
                if (a && b && (a.contains(b) || b.contains(a))) continue;
                const p = rects[i].r, q = rects[j].r;
                const ix = Math.min(p.x + p.w, q.x + q.w) - Math.max(p.x, q.x);
                const iy = Math.min(p.y + p.h, q.y + q.h) - Math.max(p.y, q.y);
                if (ix > 2 && iy > 2) out.overlaps.push(rects[i].n + ' ∩ ' + rects[j].n + ' ' + ix.toFixed(0) + 'x' + iy.toFixed(0));
            }
            return out;
        }
        """;

    internal static async Task RunAsync(IBrowser browser)
    {
        CommandConflicts.SelfCheck();
        string? build = null;
        foreach (var view in Views)
        {
            var errors = new ConcurrentQueue<string>();
            var (page, conflicts) = await OpenAsync(browser, view, errors);
            build ??= await CheckBuildAsync(page);
            var layout = await MeasureAsync(page);
            Log(new { view = view.Name, layout.Canvas, layout.Page, hud = layout.Rects.Keys, touch = layout.Touch,
                layout.SmallTouchTargets, layout.Overlaps });
            await ShotAsync(page, view.Name);

            var noScroll = layout.Page.ScrollW <= layout.Page.ClientW && layout.Page.ScrollH <= layout.Page.ClientH;
            if (view.Mobile && view.Name != "tablet")
            {
                Check(layout.Canvas is { } canvas && Fills(canvas, view), $"{view.Name}: canvas == viewport", layout.Canvas);
                Check(noScroll, $"{view.Name}: sem rolagem", layout.Page);
            }
            Check(layout.Overlaps.Length == 0, $"{view.Name}: sem sobreposição de HUD", layout.Overlaps);
            switch (view.Name)
            {
                case "retrato": await PortraitAsync(page, layout, conflicts, errors); break;
                case "desktop": await DesktopAsync(page, layout, conflicts, errors); break;
            }
            await page.Context.CloseAsync();
        }
        Log(new { build, mode = "main", views = Views.Select(view => $"{view.Name} {view.Width}x{view.Height}").ToArray(), touch = true });
    }

    // ---------- retrato: rotação, sticks, A/B, diálogo, teclado virtual e menu ----------

    private static async Task PortraitAsync(IPage page, Layout first, CommandConflicts conflicts, ConcurrentQueue<string> errors)
    {
        var hidden = !first.Rects.ContainsKey(".ultima-status") && !first.Rects.ContainsKey(".ultima-panel") &&
                     !first.Rects.ContainsKey(".ultima-chat-log");
        Check(hidden && first.Rects.ContainsKey("#ultima-menu-fab"), "retrato: menus ocultos por padrão (só fab e controles)", first.Rects.Keys);
        Check(first.Touch.GetValueOrDefault("#move-stick") is { W: >= 112, H: >= 112 } &&
              first.Touch.GetValueOrDefault("#look-stick") is { W: >= 112, H: >= 112 } &&
              first.Touch.GetValueOrDefault("#touch-action") is { W: >= 68 } &&
              first.Touch.GetValueOrDefault("#touch-cancel") is { W: >= 56 }, "retrato: sticks ≥112 e A ≥68/B ≥56", first.Touch);

        // rotação no meio da sessão
        await page.SetViewportSizeAsync(844, 390);
        await Task.Delay(1200);
        var rotated = await MeasureAsync(page);
        Check(rotated.Canvas is { } c && Math.Abs(c.W - 844) <= 1 && Math.Abs(c.H - 390) <= 1, "retrato→paisagem: canvas acompanha", rotated.Canvas);
        Check(rotated.Overlaps.Length == 0, "retrato→paisagem: sem sobreposição", rotated.Overlaps);
        await ShotAsync(page, "retrato-para-paisagem");
        await page.SetViewportSizeAsync(390, 844);
        await Task.Delay(1500);

        // O arrasto fica segurado por vários quadros: o motor amostra o stick a cada quadro e,
        // no headless, a simulação avança no máximo 50 ms por quadro.
        var start = await PlayerAsync(page);
        var end = await StickDownAsync(page, "move-stick", 0, -72, 6);
        Position? moved = null;
        await UntilAsync(async () => Distance(start, moved = await PlayerAsync(page)) > 1.5, 25_000, 1000);
        await StickUpAsync(page, "move-stick", end);
        Check(Distance(start, moved) > 1, "retrato: arrastar o stick de andar move o jogador (>1 m)", new { start, moved });

        var yaw0 = await CameraAsync(page);
        end = await StickDownAsync(page, "look-stick", 70, 0, 5);
        Camera? yaw1 = null;
        await UntilAsync(async () => Turned(yaw0, yaw1 = await CameraAsync(page), 0.15), 20_000, 1000);
        await StickUpAsync(page, "look-stick", end);
        Check(Turned(yaw0, yaw1, 0.02), "retrato: arrastar o stick de câmera muda o yaw", new { yaw0, yaw1 });

        // Perto da Guardiã: A abre o diálogo; sticks somem; B fecha.
        await WaitAsync(() => GuardAsync(() => page.EvaluateAsync<bool>("() => game.teleportToGuardian()")), "chega à Guardiã", 30_000, 1000);
        await page.Locator(".ultima-hint").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        // A e B respondem a pointerdown: um click sintético nunca os dispara.
        await PressAsync(page, "touch-action");
        await page.Locator(".ultima-dialog").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });

        var dialog = await MeasureAsync(page);
        Check(!dialog.Touch.ContainsKey("#move-stick") && !dialog.Touch.ContainsKey("#look-stick"), "diálogo: sticks ocultos enquanto aberto", dialog.Touch.Keys);
        Check(dialog.Touch.GetValueOrDefault("#touch-action") is { W: >= 68 } && dialog.Touch.GetValueOrDefault("#touch-cancel") is { W: >= 56 },
            "diálogo: A/B continuam ≥68/56", dialog.Touch);
        var line = await page.EvaluateAsync<string>("() => { const el = document.querySelector('.ultima-dialog__line, .ultima-dialog'); return el ? el.textContent.trim().slice(0, 80) : ''; }");
        Check(line.Trim().Length > 3, "diálogo: texto visível/legível", line);
        var fontSize = await page.EvaluateAsync<double>("() => { const el = document.getElementById('dialog-input'); return el ? parseFloat(getComputedStyle(el).fontSize) : 0; }");
        Check(fontSize >= 16, "diálogo: campo ≥16px (sem auto-zoom iOS)", fontSize);
        await ShotAsync(page, "retrato-dialogo");

        // teclado virtual simulado: a altura encolhe para 500
        await page.SetViewportSizeAsync(390, 500);
        await Task.Delay(700);
        var keyboard = await MeasureAsync(page);
        Check(keyboard.Rects.GetValueOrDefault(".ultima-dialog") is { } sheet && sheet.Y + sheet.H <= 501,
            "diálogo com teclado (390x500): continua na tela", keyboard.Rects.GetValueOrDefault(".ultima-dialog"));
        await page.SetViewportSizeAsync(390, 844);
        await Task.Delay(2500);   // render=main: o redimensionamento do motor trava a thread principal por alguns segundos

        Check(await CloseAsync(page, "touch-cancel", ".ultima-dialog"), "retrato: B fecha o diálogo");
        await ShotAsync(page, "retrato-dialogo-fechado");

        // menu abre pelo fab; a folha cabe e rola; os alvos têm ≥48 px; fecha por toque
        await page.EvaluateAsync("() => document.getElementById('ultima-menu-fab').click()");
        await page.Locator(".game-shell.ultima-menu-open .ultima-menu").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        await Task.Delay(400);
        var menu = await MeasureAsync(page);
        Check(menu.Rects.GetValueOrDefault(".ultima-menu") is { } folha && folha.H <= 844 && folha.W <= 390 && folha.X >= -1 && folha.Y >= -1,
            "menu: cabe no viewport", menu.Rects.GetValueOrDefault(".ultima-menu"));
        Check(menu.SmallTouchTargets.Length == 0, "menu: alvos ≥48px dentro", menu.SmallTouchTargets);
        Log(new
        {
            menuScrollsInternally = await page.EvaluateAsync<bool>(
                "() => { const el = document.querySelector('.ultima-menu'); return !!el && (el.scrollHeight > el.clientHeight + 8 || getComputedStyle(el).overflowY === 'auto'); }"),
        });
        await ShotAsync(page, "retrato-menu");
        var shut = false;
        for (var attempt = 0; attempt < 4 && !shut; attempt++)
        {
            await page.EvaluateAsync("() => { const el = document.querySelector('.ultima-menu__close, .ultima-menu-backdrop'); if (el) el.click(); }");
            shut = await UntilAsync(() => GoneAsync(page, ".game-shell.ultima-menu-open"), 15_000, 500);
        }
        Check(shut, "menu: fecha por toque e volta à tela limpa");
        var verdict = await conflicts.ClassifyAsync(errors);
        Check(verdict.Failures.Length == 0, "retrato: console sem erros", verdict);
    }

    // ---------- desktop: sem controles de toque, WASD, mouse, E/Esc e painel ----------

    private static async Task DesktopAsync(IPage page, Layout layout, CommandConflicts conflicts, ConcurrentQueue<string> errors)
    {
        Check(!layout.Touch.ContainsKey("#move-stick") && !layout.Touch.ContainsKey("#touch-action"), "desktop: sticks ocultos", layout.Touch.Keys);
        CheckDesktopBaseline(layout);
        foreach (var name in new[] { ".ultima-status", ".ultima-panel-toggle" })
            Check(layout.Rects.GetValueOrDefault(name) is { } r && r.X >= -1 && r.Y >= -1 && r.X + r.W <= 1281 && r.Y + r.H <= 721,
                $"desktop: {name} dentro da tela", layout.Rects.GetValueOrDefault(name));

        var start = await PlayerAsync(page);
        await page.Keyboard.DownAsync("w");
        Position? moved = null;
        await UntilAsync(async () => Distance(start, moved = await PlayerAsync(page)) > 1.5, 25_000, 1000);
        await page.Keyboard.UpAsync("w");
        Check(Distance(start, moved) > 1, "desktop: WASD anda", new { start, moved });

        var yaw0 = await CameraAsync(page);
        await page.Mouse.MoveAsync(640, 300);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(760, 300, new() { Steps = 8 });
        await page.Mouse.UpAsync();
        await Task.Delay(600);
        var yaw1 = await CameraAsync(page);
        Check(Turned(yaw0, yaw1, 0.01), "desktop: câmera com mouse", new { yaw0, yaw1 });

        await WaitAsync(() => GuardAsync(() => page.EvaluateAsync<bool>("() => game.teleportToGuardian()")), "chega à Guardiã", 30_000, 1000);
        await page.Locator(".ultima-hint").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await page.Locator("#game-canvas").FocusAsync();
        await page.Keyboard.PressAsync("e");
        await page.Locator(".ultima-dialog").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        var closed = false;
        for (var attempt = 0; attempt < 4 && !closed; attempt++)
        {
            await page.Keyboard.PressAsync("Escape");
            closed = await UntilAsync(() => GoneAsync(page, ".ultima-dialog"), 15_000, 500);
        }
        Check(closed, "desktop: Esc fecha o diálogo");

        await page.GetByRole(AriaRole.Button, new() { Name = "Abrir painel do jogo", Exact = true }).ClickAsync();
        await page.Locator(".ultima-panel").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        await ShotAsync(page, "desktop-painel");
        var verdict = await conflicts.ClassifyAsync(errors);
        Check(verdict.Failures.Length == 0, "desktop: console sem erros", verdict);
    }

    // A linha de base de 22/11 (decisions/ui-mobile-baseline.json) é de antes do layout mobile: o
    // desktop não pode ter se deslocado por causa dele. A altura do cartão de status varia com o
    // texto dinâmico; o restante precisa coincidir até 1 px.
    private static void CheckDesktopBaseline(Layout layout)
    {
        var file = Path.Combine(AppContext.BaseDirectory, "ui-mobile-baseline.json");
        using var baseline = JsonDocument.Parse(File.ReadAllText(file));
        var rects = baseline.RootElement.GetProperty("views").GetProperty("desktop").GetProperty("rects");
        foreach (var name in new[] { ".ultima-status", ".ultima-panel-toggle" })
        {
            var expected = rects.GetProperty(name);
            var actual = layout.Rects[name];
            double Baseline(string key) => expected.GetProperty(key).GetDouble();
            var same = Math.Abs(Baseline("x") - actual.X) <= 1 && Math.Abs(Baseline("y") - actual.Y) <= 1 &&
                      Math.Abs(Baseline("w") - actual.W) <= 1 &&
                      Math.Abs(Baseline("h") - actual.H) <= (name == ".ultima-status" ? 60 : 1);
            Check(same, $"desktop: retângulo igual à linha de base {name}", new { baseline = expected.GetRawText(), actual });
        }
    }

    // ---------- auxiliares ----------

    private sealed record View(string Name, int Width, int Height, bool Mobile);
    private sealed record Rect(double X, double Y, double W, double H);
    private sealed record PageSize(int ScrollW, int ClientW, int ScrollH, int ClientH);
    private sealed record Layout(int[] Viewport, Dictionary<string, Rect> Rects, Dictionary<string, Rect> Touch,
        Rect? Canvas, PageSize Page, string[] SmallTouchTargets, string[] Overlaps);
    private sealed record Point(double X, double Y);
    private sealed record Camera(double Alpha);

    private static bool Fills(Rect rect, View view) =>
        rect.X == 0 && rect.Y == 0 && Math.Abs(rect.W - view.Width) <= 1 && Math.Abs(rect.H - view.Height) <= 1;

    private static double Distance(Position? a, Position? b) =>
        a is null || b is null ? 0 : Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

    private static bool Turned(Camera? a, Camera? b, double minimum) =>
        a is not null && b is not null && Math.Abs(b.Alpha - a.Alpha) > minimum;

    private static Task<Layout> MeasureAsync(IPage page) => ReadAsync<Layout>(page, Measure, new { sel = Hud, touch = Touch });

    private static Task<Position?> PlayerAsync(IPage page) =>
        GuardAsync(() => ReadAsync<Position?>(page, "() => { const p = game.playerState(); return p ? { x: p.x, y: 0, z: p.z } : null; }"));

    private static Task<Camera?> CameraAsync(IPage page) =>
        GuardAsync(() => ReadAsync<Camera?>(page, "() => game.cameraState()"));

    private static Task<bool> GoneAsync(IPage page, string selector) =>
        GuardAsync(() => page.EvaluateAsync<bool>("sel => { const el = document.querySelector(sel); return !el || el.getBoundingClientRect().height === 0; }", selector));

    /// <summary>Sondagem com tolerância a falhas transitórias (navegação, motor ainda sem cena).</summary>
    private static async Task<T> GuardAsync<T>(Func<Task<T>> read)
    {
        try { return await read(); }
        catch (Exception) { return default!; }
    }

    private static async Task<bool> UntilAsync(Func<Task<bool>> condition, int timeoutMs, int intervalMs)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < timeoutMs)
        {
            if (await GuardAsync(condition) is true) return true;
            await Task.Delay(intervalMs);
        }
        return false;
    }

    private static Task FireAsync(IPage page, string id, string type, double x, double y) =>
        page.EvaluateAsync("""
            ({ id, type, x, y }) => document.getElementById(id).dispatchEvent(new PointerEvent(type, {
                pointerId: 9, pointerType: 'touch', isPrimary: true, bubbles: true, composed: true, clientX: x, clientY: y, buttons: 1 }))
            """, new { id, type, x, y });

    private static Task<Point> CenterAsync(IPage page, string id) => ReadAsync<Point>(
        page, "id => { const r = document.getElementById(id).getBoundingClientRect(); return { x: r.x + r.width / 2, y: r.y + r.height / 2 }; }", id);

    private static async Task<Point> StickDownAsync(IPage page, string id, double dx, double dy, int steps)
    {
        var center = await CenterAsync(page, id);
        await FireAsync(page, id, "pointerdown", center.X, center.Y);
        for (var i = 1; i <= steps; i++)
        {
            await FireAsync(page, id, "pointermove", center.X + dx * i / steps, center.Y + dy * i / steps);
            await Task.Delay(200);
        }
        return new(center.X + dx, center.Y + dy);
    }

    private static async Task StickUpAsync(IPage page, string id, Point end)
    {
        await FireAsync(page, id, "pointerup", end.X, end.Y);
        await FireAsync(page, id, "lostpointercapture", end.X, end.Y);
    }

    /// <summary>Toque em um botão que escuta pointerdown; repete até o elemento alvo sumir.</summary>
    private static async Task<bool> CloseAsync(IPage page, string button, string gone)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (await GuardAsync(() => CenterAsync(page, button)) is { } center)
            {
                await FireAsync(page, button, "pointerdown", center.X, center.Y);
                await FireAsync(page, button, "pointerup", center.X, center.Y);
            }
            if (await UntilAsync(() => GoneAsync(page, gone), 15_000, 500)) return true;
        }
        return false;
    }

    private static async Task PressAsync(IPage page, string id)
    {
        var center = await CenterAsync(page, id);
        await FireAsync(page, id, "pointerdown", center.X, center.Y);
        await FireAsync(page, id, "pointerup", center.X, center.Y);
    }

    private static async Task ShotAsync(IPage page, string name)
    {
        if (Screenshots is null) return;
        try
        {
            Directory.CreateDirectory(Screenshots);
            await page.ScreenshotAsync(new() { Path = Path.Combine(Screenshots, $"mobile-{name}.png"), Timeout = 30_000 });
        }
        catch (Exception error) { Log(new { capture = name, falha = error.Message }); }
    }

    private static async Task<(IPage Page, CommandConflicts Conflicts)> OpenAsync(IBrowser browser, View view, ConcurrentQueue<string> errors)
    {
        var context = await browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = view.Width, Height = view.Height },
            HasTouch = view.Mobile, IsMobile = view.Mobile, DeviceScaleFactor = 1
        });
        var page = await context.NewPageAsync();
        var conflicts = new CommandConflicts(page);
        page.PageError += (_, error) => errors.Enqueue(error);
        page.Console += (_, message) =>
        {
            // A localização da mensagem é uma string no Playwright para .NET (não um objeto).
            if (message.Type != "error") return;
            // No Playwright para .NET a localização vem como "url:linha:coluna"; o Chromium só põe
            // a URL do recurso ali, e o classificador compara a URL inteira — a posição sai.
            var where = SourcePosition.Replace(message.Location ?? "", "");
            // O ensaio roda com o mundo compartilhado desligado e uma API isolada que ninguém serve:
            // a conexão recusada dessas chamadas é ruído do ensaio, não do jogo.
            if (where.StartsWith(IsolatedApi + "/", StringComparison.Ordinal) &&
                message.Text.Contains("ERR_CONNECTION_REFUSED", StringComparison.Ordinal)) return;
            errors.Enqueue($"{message.Text} <<{where}>>");
        };
        var site = Environment.GetEnvironmentVariable("ULTIMA_SITE") ?? "https://sufficit.github.io/sufficit-game-ultima/";
        var url = new UriBuilder(new Uri(site))
        {
            Query = $"live=0&rede=0&api={Uri.EscapeDataString(IsolatedApi)}&render=main&gpu=0"
        };
        await page.GotoAsync(url.Uri.AbsoluteUri, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 45_000 });
        // Boot por sondagem, em até três rodadas com recarga: o runtime WASM pode estagnar por
        // contenção de CPU na máquina compartilhada, e o mesmo build sobe em segundos na seguinte.
        var booted = false;
        for (var round = 0; round < 3 && !booted; round++)
        {
            if (round > 0) await page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 45_000 });
            booted = await UntilAsync(() => page.EvaluateAsync<bool>("() => !!(window.game && document.getElementById('game-canvas'))"), 45_000, 1000);
        }
        Check(booted, $"{view.Name}: boot do motor", errors.Take(4).ToArray());
        Check(await UntilAsync(async () => double.IsFinite((await CameraAsync(page))?.Alpha ?? double.NaN), 45_000, 1000),
            $"{view.Name}: cena ativa");
        await Task.Delay(400);
        return (page, conflicts);
    }
}
