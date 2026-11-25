using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using Microsoft.JSInterop;
using Ultima.Environments;
using Ultima.Missions;

namespace Ultima.Pages;

public partial class EnvironmentBrowser : IAsyncDisposable
{
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private HttpClient Http { get; set; } = default!;
    [Inject] private Ultima.Models.ModelLoader Models { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "id")] private string? EnvironmentId { get; set; }
    private EnvironmentCatalog? _catalog;
    private EnvironmentDefinition? _environment;
    private ElementReference _canvas;
    private IJSObjectReference? _module, _scene;
    private CancellationTokenSource? _connection;
    private bool _missionPanel;
    private void ToggleMissionPanel() => _missionPanel = !_missionPanel;

    private bool _sceneStarting, _sceneError, _connecting, _connected, _disposed;
    private string _endpoint = "https://w-5bee2eba74174628a92a487c6a938f9b.run.sufficit.com.br";
    private string _draftKey = "", _status = "", _station = "";
    private MissionView[] _missions = [];
    private MissionView[] StationMissions => _missions.Where(mission => _station.Length == 0 || mission.Station == _station).ToArray();

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _catalog = await EnvironmentCatalog.LoadAsync(Http, CancellationToken.None);
            _environment = _catalog.Environments.FirstOrDefault(environment => environment.Id == EnvironmentId && environment.Scene is not null);
            _station = _environment?.Scene?.Stations.FirstOrDefault()?.Id ?? "";
            if (EnvironmentId is not null && _environment is null) _status = "Ambiente indisponível.";
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidDataException)
        {
            _status = "O catálogo de ambientes está indisponível.";
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_disposed || _environment?.Scene is not { } definition || _sceneStarting || _module is not null || _sceneError) return;
        _sceneStarting = true;
        try
        {
            var endpoint = await JS.InvokeAsync<string>("game.live.api");
            if (!_disposed && endpoint.Length > 0) _endpoint = endpoint;
            var build = await JS.InvokeAsync<string>("game.buildStamp");
            var module = await JS.InvokeAsync<IJSObjectReference>("import", $"./js/environment-scene.js?v={Uri.EscapeDataString(build)}");
            if (_disposed)
            {
                await module.DisposeAsync();
                return;
            }

            _module = module;
            EnvironmentExports.Configure(definition);
            var avatar = definition.Walk is null ? null : await Models.DescribeAsync(Ultima.Models.ModelLoader.PlayerModelId);
            if (_disposed) return;
            var scene = await module.InvokeAsync<IJSObjectReference>("create", _canvas, definition, _station, avatar);
            if (_disposed) await DisposeSceneAsync(scene);
            else _scene = scene;
        }
        catch (JSException) { _sceneError = true; }
        finally { _sceneStarting = false; }
        if (!_disposed) StateHasChanged();
    }

    private void ShowCatalog() => Navigation.NavigateTo(Navigation.GetUriWithQueryParameter("id", (string?)null), forceLoad: true);

    private void Open(EnvironmentDefinition environment)
    {
        if (environment.Scene is not null)
        {
            Navigation.NavigateTo(Navigation.GetUriWithQueryParameter("id", environment.Id), forceLoad: true);
            return;
        }

        var isDefault = environment.Id == _catalog?.DefaultEnvironment;
        var parameters = new Dictionary<string, object?> { ["id"] = null, ["world"] = isDefault ? null : environment.Id };
        if (!isDefault)
        {
            parameters["live"] = "0";
            parameters["rede"] = "0";
        }
        var query = new Uri(Navigation.GetUriWithQueryParameters(parameters)).Query;
        Navigation.NavigateTo(Navigation.BaseUri + query, forceLoad: true);
    }

    private async Task CycleCameraViewAsync()
    {
        if (_scene is not null) await _scene.InvokeVoidAsync("cycleCameraView");
    }

    private async Task SelectStationAsync(string id)
    {
        _station = id;
        if (_scene is not null) await _scene.InvokeVoidAsync("select", id);
    }

    private async Task ConnectAsync()
    {
        if (_connecting || _connected || _disposed || _environment is null) return;
        if (!Uri.TryCreate(_endpoint.Trim(), UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttps && !(endpoint.IsLoopback && endpoint.Scheme == Uri.UriSchemeHttp)) ||
            endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
        {
            _status = "Informe um endereço HTTPS válido para a API.";
            return;
        }
        var key = _draftKey.Trim();
        _draftKey = "";
        if (key.Length < 32 || key.Length > 512 || key.Any(character => character <= ' ' || character >= 127))
        {
            _status = "Informe uma chave de missões válida.";
            return;
        }

        var missionsUri = new Uri(endpoint.AbsoluteUri.TrimEnd('/') + $"/v1/game/environments/{_environment.Id}/missions");
        var connection = new CancellationTokenSource();
        _connection = connection;
        _connecting = true;
        try
        {
            if (await ReadBoardAsync(missionsUri, key, connection.Token))
            {
                _connected = true;
                _ = ObserveAsync(missionsUri, key, connection.Token);
            }
            else await DisconnectAsync();
        }
        finally { _connecting = false; }
    }

    private async Task ObserveAsync(Uri endpoint, string key, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);
                if (!await ReadBoardAsync(endpoint, key, token))
                {
                    await DisconnectAsync();
                    break;
                }
                StateHasChanged();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private async Task<bool> ReadBoardAsync(Uri endpoint, string key, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            request.SetBrowserRequestOption("redirect", "error");
            using var response = await Http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                _status = response.StatusCode == HttpStatusCode.Unauthorized
                    ? "Chave recusada pelo servidor."
                    : "A API de missões está indisponível neste servidor.";
                return false;
            }

            var board = await response.Content.ReadFromJsonAsync<MissionBoard>(cancellationToken: timeout.Token);
            if (board?.Missions is null)
            {
                _status = "A API retornou um painel inválido.";
                return false;
            }
            _missions = board.Missions;
            _status = "";
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException)
        {
            _status = "Não foi possível consultar as missões.";
            return false;
        }
    }

    private Task DisconnectAsync()
    {
        _connection?.Cancel();
        _connection?.Dispose();
        _connection = null;
        _connected = false;
        _missions = [];
        if (!_disposed) StateHasChanged();
        return Task.CompletedTask;
    }

    private static string ObjectiveLabel(string kind) => kind switch
    {
        "task.started" => "Iniciar",
        "task.completed" => "Entregar",
        "task.reviewed" => "Revisar",
        _ => kind,
    };

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await DisconnectAsync();
        if (_scene is not null) await DisposeSceneAsync(_scene);
        if (_module is not null) await _module.DisposeAsync();
    }

    private static async Task DisposeSceneAsync(IJSObjectReference scene)
    {
        await scene.InvokeVoidAsync("dispose");
        await scene.DisposeAsync();
    }
}
