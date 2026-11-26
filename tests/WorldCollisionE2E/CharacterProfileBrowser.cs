using System.Net.Http.Json;
using Microsoft.Playwright;
using static BrowserTest;

internal static class CharacterProfileBrowser
{
    internal static async Task RunAsync(IBrowser browser)
    {
        var api = Environment.GetEnvironmentVariable("ULTIMA_API");
        Check(Uri.TryCreate(api, UriKind.Absolute, out var address) && address.IsLoopback,
            "A disposable loopback API is required for the character profile test.");
        var garden = new UriBuilder(GameUrl(forceWebGl: true))
        {
            Query = "rede=0&gpu=0&api=" + Uri.EscapeDataString(api!) + (MainThread ? "&render=main" : "")
        };
        var page = await browser.NewPageAsync();
        var errors = TrackErrors(page);
        await page.GotoAsync(garden.Uri.AbsoluteUri, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var build = await CheckBuildAsync(page);
        await WaitAsync(() => page.EvaluateAsync<bool>("() => !!window.game?.live?.token?.() && !!game.live.context().profileKey"),
            "The browser receives and saves its character credential.");
        var token = await page.EvaluateAsync<string>("() => game.live.token()");
        using var http = new HttpClient { BaseAddress = address };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/game/characters/me/body")
        {
            Content = JsonContent.Create(new { height = 1.82, mass = 91, femurRatio = .56, waistCircumference = 1.13 })
        };
        request.Headers.Add("X-Game-Token", token);
        using var response = await http.SendAsync(request);
        Check(response.IsSuccessStatusCode, "Save through the character API", await response.Content.ReadAsStringAsync());

        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await VerifyAsync();
        var office = new UriBuilder(garden.Uri) { Path = garden.Path.TrimEnd('/') + "/environments", Query = garden.Query[1..] + "&id=office" };
        await page.GotoAsync(office.Uri.AbsoluteUri, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await VerifyAsync();
        Check(errors.IsEmpty, "Character profile browser errors", errors);
        Log(new { build, mode = Mode, garden = true, office = true, height = 1.82 });

        async Task VerifyAsync()
        {
            await WaitAsync(() => page.EvaluateAsync<bool>("""
                async () => {
                    const body = await window.game?.playerBody?.();
                    return body?.height === 1.82 && body.mass === 91 && body.femurRatio === .56 &&
                        body.waistCircumference === 1.13;
                }
                """), "The renderer loads the persisted body at scene startup.");
            Check(await page.EvaluateAsync<string>("() => game.mode") == Mode, "Expected renderer mode.");
        }
    }
}
