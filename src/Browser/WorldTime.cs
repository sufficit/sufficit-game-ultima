using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using Microsoft.JSInterop;

namespace Ultima.Browser;

public sealed class WorldTime(HttpClient http, IJSRuntime js)
{
    private (DateTimeOffset Utc, long Timestamp)? _anchor;

    // O relógio local fornece a passagem do tempo entre amostras e o UTC inicial.
    public DateTimeOffset UtcNow => _anchor is { } anchor
        ? anchor.Utc + Stopwatch.GetElapsedTime(anchor.Timestamp)
        : DateTimeOffset.UtcNow;

    public async Task RunAsync(CancellationToken token)
    {
        try
        {
            var api = await js.InvokeAsync<string>("game.live.api", token);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
            do
            {
                await SynchronizeAsync(api, token);
            }
            while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private async Task SynchronizeAsync(string api, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, api + "/v1/worlds/ultima/time");
            request.SetBrowserRequestCache(BrowserRequestCache.NoStore);
            var sent = Stopwatch.GetTimestamp();
            using var response = await http.SendAsync(request, deadline.Token);
            var received = Stopwatch.GetTimestamp();
            if (!response.IsSuccessStatusCode) return;

            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: deadline.Token);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("utcMs", out var timestamp) ||
                timestamp.ValueKind != JsonValueKind.Number || !timestamp.TryGetInt64(out var epoch))
                return;

            var utc = DateTimeOffset.FromUnixTimeMilliseconds(epoch) + Stopwatch.GetElapsedTime(sent, received) / 2;
            _anchor = (utc, received);
            await js.InvokeVoidAsync("game.syncWorldTime", token, UtcNow.ToUnixTimeMilliseconds());
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or ArgumentOutOfRangeException or JSException
            || error is OperationCanceledException && !token.IsCancellationRequested)
        {
            // Uma tentativa indisponível conserva a última amostra válida.
        }
    }
}
