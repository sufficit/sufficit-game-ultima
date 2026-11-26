using Microsoft.Playwright;
using static BrowserTest;

/// <summary>
/// Journal opened from inside the phone menu sheet (portrait view). It complements the viewport
/// and touch assertions of MobileLayout.cs: each set of assertions keeps its own file within the
/// repository line limit.
/// </summary>
internal static partial class MobileLayout
{
    // The sheet goes away, the window stays alone on top (receives touch and focus) and the menu
    // button stays behind it; on close, everything returns and the focus goes back to the canvas.
    private static async Task JournalFromMenuAsync(IPage page)
    {
        await page.EvaluateAsync("() => document.getElementById('ultima-menu-fab').click()");
        await page.Locator(".game-shell.ultima-menu-open .ultima-menu").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Diário", Exact = true }).ClickAsync();
        var diario = page.GetByRole(AriaRole.Dialog, new() { Name = "Diário", Exact = true });
        await diario.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        Check(await GoneAsync(page, ".game-shell.ultima-menu-open"), "diário pelo menu: a folha fecha sozinha");
        var onTop = await page.EvaluateAsync<bool>("() => { const w = document.querySelector('.ultima-dialog'); const r = w.getBoundingClientRect(); const el = document.elementFromPoint(r.left + r.width / 2, r.top + Math.min(30, r.height / 2)); return !!el && w.contains(el); }");
        Check(onTop, "diário pelo menu: o toque no topo da janela cai nela");
        Check(await page.EvaluateAsync<bool>("() => !!document.activeElement && document.querySelector('.ultima-dialog').contains(document.activeElement)"), "diário pelo menu: o foco entra na janela");
        Check(await page.Locator("#ultima-menu-fab").EvaluateAsync<bool>("el => el.inert"), "diário pelo menu: o botão de menu fica atrás da janela");
        await ShotAsync(page, "retrato-diario-pelo-menu");
        await diario.GetByRole(AriaRole.Button, new() { Name = "Fechar · Esc", Exact = true }).ClickAsync();
        await diario.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 20_000 });
        Check(!await page.Locator("#ultima-menu-fab").EvaluateAsync<bool>("el => el.inert"), "ao fechar o Diário o botão de menu volta");
        Check(await UntilAsync(() => page.EvaluateAsync<bool>("() => document.activeElement?.id === 'game-canvas'"), 5_000, 250), "ao fechar o Diário o foco volta ao canvas");
    }
}
