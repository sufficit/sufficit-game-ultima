using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Ultima.Telemetry;

namespace Ultima.Browser;

internal sealed class GuardianTelemetry(HttpClient http, IJSRuntime js, NavigationManager navigation)
{
    private const int QueueLimit = 256;
    private readonly SemaphoreSlim _gate = new(1);
    private readonly List<GuardianEvent> _queue = [];
    private string _storageKey = "";
    private string _build = "dev";
    private string? _playerUuid;
    private Uri? _endpoint;

    public async Task WatchAsync(string build, CancellationToken token)
    {
        try
        {
            if (new Uri(navigation.Uri).Query.TrimStart('?').Split('&').Contains("telemetry=0")) return;
            var api = await js.InvokeAsync<string>("game.live.api", token);
            if (!Uri.TryCreate(api.TrimEnd('/') + GuardianQuality.Route, UriKind.Absolute, out var endpoint) ||
                endpoint.Scheme is not ("http" or "https")) return;
            _build = GuardianQuality.BuildValid(build) ? build : "dev";
            _playerUuid = await js.InvokeAsync<string>("game.player.uuid", token);
            // A queue from one server must never be submitted to a different server.
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint.AbsoluteUri)))[..16];
            _storageKey = $"ultima-guardian-quality-v1-{key}";
            await _gate.WaitAsync(token);
            try
            {
                var json = await js.InvokeAsync<string?>("game.browser.read", token, _storageKey);
                if (json is not null) Restore(json);
                _endpoint = endpoint;
            }
            finally { _gate.Release(); }
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            do { await FlushAsync(endpoint, token); }
            while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (JSException) { /* Browser telemetry is optional. */ }
    }

    public async Task RecordAsync(string engine, string model, string outcome, long elapsedMs = 0)
    {
        await _gate.WaitAsync();
        try
        {
            if (_endpoint is null) return;
            var entry = new GuardianEvent(Guid.NewGuid().ToString("N"), _build, engine, model, outcome, Math.Max(0, elapsedMs), _playerUuid);
            if (!GuardianQuality.Valid(entry)) return;
            _queue.Add(entry);
            if (_queue.Count > QueueLimit) _queue.RemoveAt(0);
            await SaveAsync();
        }
        finally { _gate.Release(); }
    }

    private async Task FlushAsync(Uri endpoint, CancellationToken token)
    {
        GuardianEvent[] entries;
        await _gate.WaitAsync(token);
        try { entries = _queue.Take(GuardianQuality.BatchSize).ToArray(); }
        finally { _gate.Release(); }
        if (entries.Length == 0) return;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            using var response = await http.PostAsJsonAsync(endpoint, new GuardianBatch(entries),
                GuardianTelemetryJson.Default.GuardianBatch, timeout.Token);
            if (!response.IsSuccessStatusCode ||
                await response.Content.ReadFromJsonAsync(GuardianTelemetryJson.Default.GuardianReceipt, timeout.Token)
                    is not { Ok: true }) return;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or NotSupportedException) { return; }

        await _gate.WaitAsync(token);
        try
        {
            var sent = entries.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
            _queue.RemoveAll(entry => sent.Contains(entry.Id));
            await SaveAsync();
        }
        finally { _gate.Release(); }
    }

    private void Restore(string json)
    {
        try
        {
            var batch = JsonSerializer.Deserialize(json, GuardianTelemetryJson.Default.GuardianBatch);
            _queue.AddRange((batch?.Events ?? []).Where(GuardianQuality.Valid).TakeLast(QueueLimit));
        }
        catch (JsonException) { /* A malformed local queue cannot prevent future collection. */ }
    }

    private async Task SaveAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(new GuardianBatch(_queue.ToArray()), GuardianTelemetryJson.Default.GuardianBatch);
            await js.InvokeVoidAsync("game.browser.write", _storageKey, json);
        }
        catch (JSException) { /* Continue buffering in memory when storage is unavailable. */ }
    }
}
