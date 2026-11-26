using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using Microsoft.JSInterop;

namespace Ultima.Browser;

public sealed partial class LiveGame(HttpClient http, IJSRuntime js)
{
    private string _api = "";
    private string? _token;
    private string? _id;
    private long _since;
    private CancellationToken _lifetime;
    private bool _refreshingMap;
    private bool _pendingMap;
    private bool _rejoin;
    private long _lastMapAt;
    private long _lastPoseAt;
    private (double X, double Z)? _lastPose;
    public event Action<string>? Status;
    public event Action<string, string>? Chat;
    public event Action<bool>? VolumeTunnelsChanged;
    public bool HasVolumeTunnels { get; private set; }

    public async Task RunAsync(CancellationToken token)
    {
        _lifetime = token;
        try
        {
            var context = await js.InvokeAsync<Context>("game.live.context", token);
            if (!context.Enabled) return;
            _api = context.Api;
            if (!string.IsNullOrEmpty(context.Token))
            {
                _token = context.Token;
                try
                {
                    var state = await SendAsync<Session>("GET", "/v1/game/state", null, 8);
                    if (state?.Me?.Id is { } id)
                    {
                        _id = id;
                        _since = state.EventsFrom;
                        await ApplyCharactersAsync(state.Characters);
                        await ApplyMapAsync(state.Map);
                        await js.InvokeVoidAsync("game.live.setSession", token, (object?)_token);
                    }
                    else { await ClearSessionAsync(); }
                }
                catch (Exception) when (!token.IsCancellationRequested) { await ClearSessionAsync(); }
            }

            while (!token.IsCancellationRequested)
            {
                if (_rejoin) await ClearSessionAsync();
                if (_token is null && !await JoinAsync()) return;
                try
                {
                    var events = await SendAsync<EventBatch>("GET", $"/v1/game/events?since={_since}&wait=15", null, 22);
                    if (_rejoin) continue;
                    foreach (var message in events?.Events ?? [])
                    {
                        await ApplyEventAsync(message);
                        _since = Math.Max(_since, message.Seq);
                    }
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized) { _rejoin = true; }
                catch (Exception) when (!token.IsCancellationRequested) { await Task.Delay(3000, token); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (JSException) when (!token.IsCancellationRequested) { Status?.Invoke("Servidor do jogo inacessível"); }
    }

    private async Task<bool> JoinAsync()
    {
        var name = await js.InvokeAsync<string?>("game.net.getName", _lifetime);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                var context = await js.InvokeAsync<Context>("game.live.context", _lifetime);
                var session = await SendAsync<Session>("POST", "/v1/game/sessions",
                    new { Name = name ?? "Jogador", context.ProfileKey }, 10);
                if (string.IsNullOrEmpty(session?.Token)) throw new InvalidDataException("Sessão sem token.");
                _id = session.Character?.Id;
                _since = session.EventsFrom;
                _token = session.Token;
                if (session.ProfileKey is { } profileKey)
                    await js.InvokeVoidAsync("game.live.setProfileKey", _lifetime, profileKey);
                await js.InvokeVoidAsync("game.live.setSession", _lifetime, (object?)_token);
                _ = RefreshMapAsync();
                return true;
            }
            catch (Exception) when (!_lifetime.IsCancellationRequested)
            {
                await ClearSessionAsync();
                await Task.Delay(1500 * attempt, _lifetime);
            }
        }
        Status?.Invoke("Servidor do jogo inacessível");
        return false;
    }

    private async Task ClearSessionAsync()
    {
        _token = _id = null;
        _rejoin = false;
        _lastPose = null;
        await js.InvokeVoidAsync("game.live.setSession", _lifetime, (object?)null);
    }

    public Task PoseAsync(double x, double z, double yaw)
    {
        if (_token is null || _rejoin || !double.IsFinite(x) || !double.IsFinite(z) || !double.IsFinite(yaw))
            return Task.CompletedTask;
        var now = Now();
        if (_lastPose is { } last && now - _lastPoseAt < 400 && Math.Abs(x - last.X) + Math.Abs(z - last.Z) <= 2)
            return Task.CompletedTask;
        _lastPoseAt = now;
        _lastPose = (x, z);
        return CommandAsync(new { Action = "pose", X = x, Z = z, Yaw = yaw }, 8);
    }

    public Task SayAsync(string text)
    {
        if (text.Length == 0) return Task.CompletedTask;
        return CommandAsync(new { Action = "say", Text = text[..Math.Min(text.Length, 300)] }, 10);
    }

    private async Task CommandAsync(object command, int seconds)
    {
        if (_token is null || _rejoin || _lifetime.IsCancellationRequested) return;
        var sessionToken = _token;
        try { await SendAsync<JsonElement>("POST", "/v1/game/command", command, seconds); }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        { if (sessionToken == _token) _rejoin = true; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException) { }
    }

    private async Task ApplyEventAsync(WorldEvent message)
    {
        switch (message.Type)
        {
            case "snapshot":
                await ApplyCharactersAsync(message.Characters);
                _ = RefreshMapAsync();
                break;
            case "join" when message.Character?.Id is { } id && id != _id:
                await js.InvokeVoidAsync("game.addRemote", _lifetime, id, message.Character.Name);
                break;
            case "leave" when message.Character?.Id is { } id:
                await js.InvokeVoidAsync("game.removeRemote", _lifetime, id);
                break;
            case "say" when message.From != _id:
                Chat?.Invoke(message.Name ?? "Jogador", message.Text ?? "");
                break;
            case "npc" when string.IsNullOrEmpty(message.To) || message.To == _id:
                Chat?.Invoke(message.Name ?? "Guardiã", message.Text ?? "");
                break;
            case "job_started" or "job_done" or "job_cancelled" or "map_edited" or "world_edited":
                _ = RefreshMapAsync();
                break;
        }
    }

    private async Task ApplyCharactersAsync(Character[]? characters)
    {
        if (characters is not { Length: > 0 }) return;
        var others = characters.Where(character => character.Id is not null && character.Id != _id).ToArray();
        if (others.Length != 0) await js.InvokeVoidAsync("game.live.paintCharacters", _lifetime, (object)others);
    }

    private async Task RefreshMapAsync()
    {
        if (_token is null) return;
        _pendingMap = true;
        if (_refreshingMap) return;
        _refreshingMap = true;
        try
        {
            while (_pendingMap && _token is not null && !_rejoin)
            {
                var delay = 1000 - (Now() - _lastMapAt);
                if (delay > 0) await Task.Delay(TimeSpan.FromMilliseconds(delay), _lifetime);
                _pendingMap = false;
                _lastMapAt = Now();
                var sessionToken = _token;
                var map = await SendAsync<WorldMap>("GET", "/v1/game/map", null, 10);
                if (map is not null && sessionToken == _token && !_rejoin) await ApplyMapAsync(map);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or JSException) { }
        finally { _refreshingMap = false; }
    }

    private async Task ApplyMapAsync(WorldMap? map)
    {
        var supported = map?.VolumeTunnels == true;
        if (HasVolumeTunnels != supported)
        {
            HasVolumeTunnels = supported;
            VolumeTunnelsChanged?.Invoke(supported);
        }
        await js.InvokeVoidAsync("game.live.paintMap", _lifetime, map);
    }

    private async Task<T?> SendAsync<T>(string method, string path, object? data, int seconds)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        using var request = new HttpRequestMessage(new HttpMethod(method), _api + path);
        request.SetBrowserRequestCache(BrowserRequestCache.NoStore);
        if (_token is not null) request.Headers.Add("X-Game-Token", _token);
        if (data is not null) request.Content = JsonContent.Create(data);
        using var response = await http.SendAsync(request, timeout.Token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(timeout.Token);
    }

    private static long Now() => Environment.TickCount64;
    private sealed record Context(string Api, bool Enabled, string? Token, string? ProfileKey);
    private sealed record Session(string? Token, string? ProfileKey, Character? Me, Character? Character, long EventsFrom, Character[]? Characters, WorldMap? Map);
    private sealed record Character(string? Id, string? Name, double X, double Z, double Yaw);
    private sealed record WorldMap(JsonElement? Tiles, JsonElement[]? Jobs, JsonElement[]? Edits, long? Revision, JsonElement[]? Placements, bool VolumeTunnels);
    private sealed record EventBatch(WorldEvent[]? Events);
    private sealed record WorldEvent(string Type, long Seq, Character[]? Characters, Character? Character, string? From, string? To,
        string? Name, string? Text);
}
