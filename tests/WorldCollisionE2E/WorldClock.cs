using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static BrowserTest;
using static Microsoft.Playwright.Assertions;

internal sealed class WorldClock(IBrowser browser)
{
    private static readonly DateTime Day = new(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Night = new(2026, 11, 24, 0, 30, 0, DateTimeKind.Utc);
    private static readonly bool Mobile = Environment.GetEnvironmentVariable("ULTIMA_MOBILE") == "1";
    private static readonly string? Screenshots = Environment.GetEnvironmentVariable("ULTIMA_SCREENSHOTS");

    internal async Task RunAsync()
    {
        var (page, errors) = await OpenAsync(Day);
        var solar = (await SkyAsync(page))!;
        var clear = await LightingAsync(page);
        Check(solar[1] > .87 && solar[7] == 1 && clear.Direct > .9 && clear.DirectionY < -.87,
            "Sol alto de verão ilumina a cena", new { solar, clear });
        await OpenPanelAsync(page);
        await ScreenshotAsync(page, "menu");
        await CheckTextAsync(page, "#world-time-gmt", @"GMT 21/06/2026 12:00");
        await CheckTextAsync(page, "#world-time-local", "09:00 GMT-3");
        var zone = page.Locator("#world-time-zone");
        var placeholder = await zone.GetAttributeAsync("placeholder");
        Check(Regex.IsMatch(placeholder ?? "", "Automático.*Sao Paulo"), "o fuso automático usa São Paulo", placeholder);
        await zone.FillAsync("London");
        await page.GetByRole(AriaRole.Option, new() { Name = "Europe/London", Exact = true }).ClickAsync();
        await WaitTextAsync(page, "#world-time-local", "13:00");
        await CheckTextAsync(page, "#world-time-local", @"13:00 GMT\+1");
        Check(await page.EvaluateAsync<string>("() => localStorage.getItem('ultima.fuso')") == "Europe/London",
            "fuso persistido");
        var localSky = await SkyAsync(page);
        Check(localSky is not null && solar.SequenceEqual(localSky), "o fuso não altera o céu");

        await SelectWeatherAsync(page, "Chuva forte");
        await WaitAsync(async () => await WeatherAsync(page) is { Mode: "heavy-rain", Rain: > 1.2, Drops: > 0 },
            "chuva forte aplicada na cena");
        var rainy = await LightingAsync(page);
        Check(rainy.Direct < clear.Direct * .6, "chuva reduz luz direta", new { clear, rainy });

        await SelectWeatherAsync(page, "Neblina");
        await WaitAsync(async () => await WeatherAsync(page) is { Mode: "fog", Rain: 0 }, "neblina sem chuva");
        var foggy = await LightingAsync(page);
        Check(foggy.Fog > clear.Fog * 4, "neblina aumenta a densidade", new { clear, foggy });
        await page.ReloadAsync();
        await WaitForSkyAsync(page);
        await OpenPanelAsync(page);
        await CheckTextAsync(page, "#world-time-local", @"13:00 GMT\+1");
        await CheckTextAsync(page, "#weather-mode", "Neblina");
        await page.GetByRole(AriaRole.Button, new() { Name = "Usar fuso automático" }).ClickAsync();
        await WaitTextAsync(page, "#world-time-local", "09:00");
        Check(errors.IsEmpty, "erros no navegador durante o dia", errors);
        await page.CloseAsync();

        var (dark, nightErrors) = await OpenAsync(Night);
        var lunar = (await SkyAsync(dark))!;
        var nightLight = await LightingAsync(dark);
        Check(lunar[1] < 0 && lunar[4] > 0 && lunar[6] > .98 && lunar[9] == 4, "Lua cheia acima do horizonte", lunar);
        Check(nightLight.Direct == 0 && nightLight.Ambient < clear.Ambient && nightLight.Moon == 1,
            "cena noturna com Lua visível", nightLight);
        await ScreenshotAsync(dark, "noite");
        await OpenPanelAsync(dark);
        await CheckTextAsync(dark, "#world-time-gmt", @"24/11/2026 00:30");
        await CheckTextAsync(dark, "#world-time-local", @"23/11/2026.*21:30 GMT-3");
        await CheckTextAsync(dark, "#world-moon-phase", "Lua cheia");
        Check(nightErrors.IsEmpty, "erros no navegador durante a noite", nightErrors);
        await dark.CloseAsync();

        await SynchronizeAsync();
        Log(new { synchronized = true, mode = Mode, clear, nightLight, moon = lunar[6], mobile = Mobile });
    }

    private async Task SynchronizeAsync()
    {
        var requests = 0;
        var device = new DateTime(2026, 6, 21, 3, 0, 0, DateTimeKind.Utc);
        var (page, errors) = await OpenAsync(device, () => Interlocked.Increment(ref requests) switch
        {
            1 => new() { Json = new { utcMs = new DateTimeOffset(Day).ToUnixTimeMilliseconds() } },
            2 => new() { Status = 503, Body = "temporariamente indisponível" },
            _ => new() { Json = new { utcMs = new DateTimeOffset(Night).ToUnixTimeMilliseconds() } }
        });
        await WaitAsync(async () => (await SkyAsync(page))?[1] > .87, "céu segue o servidor com relógio local divergente");
        await OpenPanelAsync(page);
        await WaitTextAsync(page, "#world-time-gmt", "12:00");
        await CheckTextAsync(page, "#world-time-local", "09:00 GMT-3");
        Check(Volatile.Read(ref requests) == 1, "sincroniza ao iniciar", requests);

        await page.Clock.FastForwardAsync("00:59:00");
        await WaitTextAsync(page, "#world-time-gmt", "12:59");
        Check(Volatile.Read(ref requests) == 1, "aguarda uma hora para consultar novamente", requests);
        var failed = page.WaitForResponseAsync(response => response.Url.EndsWith("/v1/worlds/ultima/time") && response.Status == 503);
        await page.Clock.FastForwardAsync("00:02:00");
        await failed;
        await WaitTextAsync(page, "#world-time-gmt", "13:01");
        Check(Volatile.Read(ref requests) == 2, "consulta horária indisponível", requests);
        Check((await SkyAsync(page))?[1] > .8, "na falha, o céu mantém a calibração anterior");

        await page.Clock.FastForwardAsync("01:01:00");
        await WaitAsync(async () => (await SkyAsync(page))?[1] < 0, "o céu aplica a amostra seguinte");
        await page.Clock.FastForwardAsync("00:00:10");
        await WaitTextAsync(page, "#world-time-gmt", "00:30");
        Check(Volatile.Read(ref requests) == 3, "ressincroniza quando o servidor volta", requests);
        Check(errors.IsEmpty, "erros no navegador durante a sincronização", errors);
        await page.CloseAsync();
    }

    private async Task<(IPage Page, ConcurrentQueue<string> Errors)> OpenAsync(DateTime date, Func<RouteFulfillOptions>? serverClock = null)
    {
        var page = await browser.NewPageAsync(new()
        {
            ViewportSize = Mobile ? new() { Width = 390, Height = 844 } : new() { Width = 1200, Height = 720 },
            TimezoneId = "America/Sao_Paulo"
        });
        var errors = TrackErrors(page);
        var warnings = new ConcurrentQueue<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "warning") warnings.Enqueue(message.Text);
        };
        if (serverClock is null)
            await page.Clock.SetFixedTimeAsync(date);
        else
        {
            await page.Clock.InstallAsync(new() { TimeDate = date });
            await page.RouteAsync("**/v1/worlds/ultima/time", route => route.FulfillAsync(serverClock()));
        }
        if (!MainThread)
        {
            await page.RouteAsync("**/js/game-core.js*", async route =>
            {
                var response = await route.FetchAsync();
                const string clock = "worldTimeAnchor.monotonic : Date.now()";
                // A emulação do Playwright na página não alcança o worker de renderização.
                var source = await response.TextAsync();
                Check(source.Contains(clock), "ponto de emulação do relógio no worker");
                var time = new DateTimeOffset(date).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
                var body = source.Replace(clock, "worldTimeAnchor.monotonic : " + time);
                await route.FulfillAsync(new() { Response = response, Body = body });
            });
        }
        await page.GotoAsync(GameUrl(forceWebGl: true));
        await WaitForSkyAsync(page);
        await CheckBuildAsync(page);
        await WaitAsync(async () => await page.EvaluateAsync<string>("() => game.mode") == Mode,
            $"modo do renderizador {Mode}", timeoutMs: 30_000);
        Check(errors.IsEmpty, "erros ao carregar o céu", new { errors, warnings });
        return (page, errors);
    }

    private static async Task OpenPanelAsync(IPage page)
    {
        var menu = Mobile ? "Abrir menu" : "Abrir painel do jogo";
        await page.GetByRole(AriaRole.Button, new() { Name = menu, Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Tempo e clima" }).ClickAsync();
        await WaitTextAsync(page, "#world-time-local", "GMT");
    }

    private static async Task CheckTextAsync(IPage page, string selector, string pattern)
    {
        var text = await page.Locator(selector).InnerTextAsync();
        Check(Regex.IsMatch(text, pattern), $"texto esperado em {selector}: {pattern}", text);
    }

    private static Task WaitTextAsync(IPage page, string selector, string text) =>
        Expect(page.Locator(selector)).ToContainTextAsync(text, new() { Timeout = 30_000 });

    private static async Task SelectWeatherAsync(IPage page, string weather)
    {
        await page.Locator("#weather-mode").ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = weather, Exact = true }).ClickAsync();
    }

    private static async Task ScreenshotAsync(IPage page, string suffix)
    {
        if (string.IsNullOrEmpty(Screenshots)) return;
        await WaitAsync(async () => (await ReadAsync<MapState?>(page, "() => game.build.state()"))?.Chunks >= 25,
            "terreno carregado para a captura");
        await page.ScreenshotAsync(new() { Path = $"{Screenshots}-{suffix}.png" });
    }

    private static Task<double[]?> SkyAsync(IPage page) =>
        ReadAsync<double[]?>(page, "async () => { const sky = await window.game?.skyStatus?.(); return sky ? Array.from(sky) : null; }");
    private static Task WaitForSkyAsync(IPage page) => WaitAsync(async () => await SkyAsync(page) is not null, "céu carregado");
    private static Task<Weather> WeatherAsync(IPage page) => ReadAsync<Weather>(page, "() => game.weather.status()");
    private static Task<Lighting> LightingAsync(IPage page) => RenderAsync<Lighting>(page, """
        () => {
            const scene = BABYLON.EngineStore.LastCreatedScene;
            return { direct: scene.getLightByName('sun').intensity, ambient: scene.getLightByName('hemi').intensity,
                directionY: scene.getLightByName('sun').direction.y, moon: scene.getMeshByName('moon').visibility,
                fog: scene.fogDensity };
        }
        """);

    private sealed record Weather(string Mode, double Rain, int Drops);
    private sealed record Lighting(double Direct, double Ambient, double DirectionY, double Moon, double Fog);
}
