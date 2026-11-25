using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static BrowserTest;

internal static class HudStatus
{
    internal static readonly string[] DefaultBrowserArgs =
        ["--use-gl=angle", "--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--disable-dev-shm-usage"];

    private static readonly Regex Import = new(@"@wllama/wllama@[^/]+/esm/index\.min\.js");
    private static readonly Regex ExpectedError = new("ia-cpu|wllama|ERR_FAILED", RegexOptions.CultureInvariant);
    private static readonly Regex Activity = Pattern(@"IA \((CPU|GPU)\)");
    private static readonly Regex Position = Pattern(@"pos \(");
    private static readonly Regex Downloading = Pattern(@"Baixando IA \(CPU\)");
    private static readonly Regex Progress = Pattern(@"Baixando IA \(CPU\) · \d+%");
    private static readonly Regex Failure = Pattern(@"Erro na IA \(CPU\)");

    internal static async Task RunAsync(IBrowser browser)
    {
        var page = await browser.NewPageAsync(new()
        {
            ViewportSize = new() { Width = 1100, Height = 640 },
            DeviceScaleFactor = 1
        });
        var errors = TrackErrors(page);
        page.Console += (_, message) =>
        {
            if (message.Type == "error") errors.Enqueue(message.Text);
        };
        var pending = new TaskCompletionSource<IRoute>(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync(Import, route => { pending.TrySetResult(route); }, new() { Times = 1 });
        var url = new UriBuilder(GameUrl()) { Query = "render=main&live=0&rede=0" };
        await page.GotoAsync(url.Uri.AbsoluteUri, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 45_000 });
        var build = await CheckBuildAsync(page);
        var status = page.Locator(".ultima-status");
        await status.WaitForAsync(new() { Timeout = 90_000 });
        Task<string> StatusAsync() => status.InnerTextAsync();
        Task WaitStatusAsync(Regex pattern, bool present, string description) =>
            WaitAsync(async () => pattern.IsMatch(await StatusAsync()) == present, description, 30_000);
        await WaitAsync(async () => (await StatusAsync()).Length > 0, "cartão de status disponível", 90_000);

        var announcement = page.Locator(".ultima-announce");
        await announcement.WaitForAsync(new() { Timeout = 20_000 });
        var reading = Stopwatch.StartNew();
        var text = (await announcement.InnerTextAsync()).Trim();
        Check(text.Length > 0, "aviso do servidor aparece");
        await Task.Delay(3000);
        Check(await announcement.CountAsync() != 0, "aviso continua na tela após 3 s");
        await announcement.WaitForAsync(new() { Timeout = 40_000, State = WaitForSelectorState.Detached });
        var seconds = reading.Elapsed.TotalSeconds;
        Check(seconds is >= 5 and <= 30, "aviso some sozinho no tempo de leitura", seconds);

        var initial = await StatusAsync();
        Check(!Position.IsMatch(initial) && !Activity.IsMatch(initial), "cartão recolhido e sem atividade de IA", initial);
        // A importação fica pendente até o HUD mostrar o estado de download.
        await page.EvaluateAsync("() => { window.game.aiCpu.load(); }");
        await WaitStatusAsync(Downloading, true, "mostra que a IA está baixando");
        var downloading = await StatusAsync();
        Check(Progress.IsMatch(downloading) && !Position.IsMatch(downloading), "mostra o progresso com o cartão recolhido", downloading);
        await (await pending.Task.WaitAsync(TimeSpan.FromSeconds(10))).AbortAsync();
        await WaitStatusAsync(Failure, true, "mostra erro após interromper a importação");
        var failure = await StatusAsync();
        Check(Failure.IsMatch(failure) && !Position.IsMatch(failure), "mostra erro com o cartão recolhido", failure);

        await page.EvaluateAsync("() => { window.game.aiCpu.disable(); }");
        await WaitStatusAsync(Activity, false, "selo some ao desligar a IA");
        await status.GetByRole(AriaRole.Button, new() { Name = "detalhes", Exact = true }).ClickAsync();
        await WaitStatusAsync(Position, true, "detalhes voltam a mostrar a posição");
        var unexpected = errors.Where(error => !ExpectedError.IsMatch(error)).ToArray();
        Check(unexpected.Length == 0, "sem erros de console inesperados", unexpected);
        Log(new { build, mode = "main", announcement = text, announcementSeconds = seconds, cpuStatus = true, details = true });
    }

    private static Regex Pattern(string value) => new(value, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
