using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using Microsoft.JSInterop;

namespace Ultima.Browser;

public sealed class GameUpdate(HttpClient http, IJSRuntime js)
{
    public string Build { get; private set; } = "dev";
    private DateTimeOffset _checkAfter;

    public async Task InitializeAsync(CancellationToken token)
        => Build = await js.InvokeAsync<string>("game.browser.build", token);

    public void Dismiss() => _checkAfter = DateTimeOffset.UtcNow.AddMinutes(10);

    public async Task WatchAsync(Action<string> show, CancellationToken token)
    {
        if (Build == "dev" || Build.StartsWith("__", StringComparison.Ordinal)) return;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), token);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            do
            {
                if (DateTimeOffset.UtcNow < _checkAfter) continue;
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, "build.json");
                    request.SetBrowserRequestCache(BrowserRequestCache.NoStore);
                    using var response = await http.SendAsync(request, token);
                    if (!response.IsSuccessStatusCode) continue;
                    var latest = (await response.Content.ReadFromJsonAsync<BuildInfo>(token))?.Build;
                    if (!string.IsNullOrEmpty(latest) && latest != Build)
                        show(latest[..Math.Min(7, latest.Length)]);
                }
                catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException) { }
            } while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private sealed record BuildInfo(string? Build);
}
