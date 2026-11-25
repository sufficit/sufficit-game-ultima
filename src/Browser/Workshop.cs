using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using Microsoft.JSInterop;

namespace Ultima.Browser;

public sealed partial class Workshop(HttpClient http, IJSRuntime js, GuardianGenius genius)
{
    private readonly GuardianGenius _genius = genius;
    public const int MaxUploadBytes = 2 * 1024 * 1024;
    private readonly HashSet<string> _known = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _register = new(1);
    private readonly SemaphoreSlim _editGate = new(1);
    private readonly Stack<string> _surfaceEdits = new();
    private string? _editSession;
    public bool CanUndoSurface => _surfaceEdits.Count > 0;
    private string[] _buildAssets = [];
    public event Action<string[]>? BuildAssetsChanged;
    public event Action<string>? Status;

    public async Task WatchAsync(CancellationToken token)
    {
        try { await Task.WhenAll(WatchBuildAssetsAsync(token), WatchCatalogAsync(token)); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private async Task WatchBuildAssetsAsync(CancellationToken token)
    {
        for (var attempt = 0; attempt <= 30; attempt++)
        {
            if (await RefreshBuildAssetsAsync(token)) return;
            await Task.Delay(TimeSpan.FromSeconds(1), token);
        }
    }

    private async Task WatchCatalogAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                var endpoint = await EndpointAsync(token);
                var catalog = await GetAsync<Catalog>(endpoint, "/v1/assets", 8, token);
                foreach (var asset in catalog?.Assets ?? [])
                {
                    try { await BringAsync(endpoint, asset, token); }
                    catch (Exception ex) when (!token.IsCancellationRequested) { Console.WriteLine($"[oficina] {asset.Id}: {ex.Message}"); }
                }
                await RefreshBuildAssetsAsync(token);
            }
            catch (Exception) when (!token.IsCancellationRequested) { /* catálogo opcional */ }
        } while (await timer.WaitForNextTickAsync(token));
    }

    public async Task<bool> RefreshBuildAssetsAsync(CancellationToken token)
    {
        try
        {
            var state = await js.InvokeAsync<GlbState?>("game.glb.status", token);
            var ids = (state?.Instances?.Keys ?? Enumerable.Empty<string>())
                .Where(id => !id.StartsWith("gen.", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
            if (!_buildAssets.SequenceEqual(ids))
            {
                _buildAssets = ids;
                BuildAssetsChanged?.Invoke(ids);
            }
            return ids.Length != 0;
        }
        catch (JSException) { return false; }
    }

    public Task EditAsync(JsonElement operation, CancellationToken token) =>
        SendWorldEditAsync(operation, false, token);

    public Task UndoSurfaceAsync(CancellationToken token) =>
        SendWorldEditAsync(null, true, token);

    private async Task SendWorldEditAsync(JsonElement? operation, bool undo, CancellationToken token)
    {
        await _editGate.WaitAsync(token);
        try
        {
            var endpoint = await EndpointAsync(token);
            if (_editSession != endpoint.Token)
            {
                _editSession = endpoint.Token;
                _surfaceEdits.Clear();
            }
            if (string.IsNullOrEmpty(endpoint.Token))
            {
                Status?.Invoke("Conecte-se ao servidor do jogo para editar o mundo.");
                return;
            }

            if (undo && !_surfaceEdits.TryPeek(out _))
            {
                Status?.Invoke("Nenhuma edição desta sessão para desfazer.");
                return;
            }
            using var content = undo
                ? JsonContent.Create(new { Action = "undo_surface", Edit = _surfaceEdits.Peek() })
                : JsonContent.Create(operation!.Value);
            var result = await SendAsync<Reply>(endpoint, HttpMethod.Post, "/v1/game/command", content, 15, token);
            if (result?.Error is { Length: > 0 } error)
            {
                Status?.Invoke($"Edição recusada: {error}.");
                return;
            }
            if (undo && result?.Ok == true)
            {
                _surfaceEdits.Pop();
                Status?.Invoke("Edição do terreno desfeita.");
            }
            else if (result?.Edit is { Length: > 0 } edit)
            {
                _surfaceEdits.Push(edit);
                Status?.Invoke("Edição do terreno salva.");
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            _surfaceEdits.Clear();
            Status?.Invoke("Conecte-se ao servidor do jogo para editar o mundo.");
        }
        catch (WorkshopException ex) when (undo && ex.Code == "edit_not_found")
        {
            _surfaceEdits.TryPop(out _);
            Status?.Invoke("A edição já foi removida do mundo.");
        }
        catch (Exception ex) when (!token.IsCancellationRequested) { Status?.Invoke($"Edição falhou: {ex.Message}"); }
        finally { _editGate.Release(); }
    }

    public async Task<string> GenerateAsync(string prompt, CancellationToken token)
    {
        var clean = Limit(prompt.Trim(), 120);
        if (clean.Length == 0) return "Escreva o que você quer criar.";
        try
        {
            var endpoint = await EndpointAsync(token);
            using var body = JsonContent.Create(new { Prompt = clean });
            var reply = await SendAsync<Reply>(endpoint, HttpMethod.Post, "/v1/assets/generate", body, 30, token);
            if (reply?.Job is { Length: > 0 } job && reply.Engine == "meshy")
            {
                Status?.Invoke("Gerando com IA na nuvem — leva alguns minutos…");
                for (var attempt = 0; attempt < 100; attempt++)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), token);
                    reply = await GetAsync<Reply>(endpoint, "/v1/assets/jobs/" + Uri.EscapeDataString(job), 10, token);
                    if (reply?.Status == "error") return $"A geração falhou: {reply.Error ?? "erro"}";
                    if (reply?.Status == "done" && reply.Asset is not null) break;
                }
                if (reply?.Asset is null) return "A geração demorou demais; tente de novo.";
            }
            if (reply?.Asset is null) return "O servidor não devolveu um modelo.";
            await BringAsync(endpoint, reply.Asset, token);
            return $"✓ \"{clean}\" criado e plantado no mundo!";
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed
            or HttpStatusCode.NotImplemented or HttpStatusCode.ServiceUnavailable)
        { return "Este servidor ainda não tem oficina de modelos."; }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        { return "Conecte-se ao servidor do jogo primeiro."; }
        catch (WorkshopException ex) when (ex.Code == "storage_unavailable")
        { return "O servidor não consegue gravar os assets (armazenamento indisponível)."; }
        catch (Exception ex) when (!token.IsCancellationRequested) { return $"Falhou: {ex.Message}"; }
    }

    public async Task UploadAsync(byte[] bytes, string fileName, double scale, CancellationToken token)
    {
        if (bytes.Length == 0) return;
        if (bytes.Length > MaxUploadBytes) { Status?.Invoke("Máximo 2 MB por modelo."); return; }
        try
        {
            var endpoint = await EndpointAsync(token);
            if (string.IsNullOrEmpty(endpoint.Token))
            {
                Status?.Invoke("Conecte-se ao servidor do jogo para enviar.");
                return;
            }
            Status?.Invoke(FormattableString.Invariant($"Enviando {bytes.Length / 1024.0:0} KB…"));
            using var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("model/gltf-binary");
            var name = fileName.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) ? fileName[..^4] : fileName;
            var query = FormattableString.Invariant($"?name={Uri.EscapeDataString(Limit(name, 40))}&scale={scale}");
            var reply = await SendAsync<Reply>(endpoint, HttpMethod.Post, "/v1/assets/upload" + query, content, 30, token);
            if (reply?.Asset is null) return;
            await BringAsync(endpoint, reply.Asset, token);
            Status?.Invoke($"✓ \"{name}\" publicado no mundo!");
        }
        catch (HttpRequestException ex)
        {
            var why = (ex as WorkshopException)?.Code switch
            {
                "too_many_triangles" => " (muitos triângulos: máx. 30 mil)",
                "not_a_glb" => " (arquivo não é um GLB válido)",
                "asset_not_approved" => " (o arquivo ainda não está aprovado para este mundo)",
                "storage_unavailable" => " (servidor sem armazenamento gravável)",
                _ => ""
            };
            Status?.Invoke($"Envio recusado{why}.");
        }
        catch (Exception ex) when (!token.IsCancellationRequested) { Status?.Invoke($"Falha no envio: {ex.Message}"); }
    }

    private async Task BringAsync(Endpoint endpoint, Asset asset, CancellationToken token)
    {
        await _register.WaitAsync(token);
        try
        {
            if (_known.Contains(asset.Id)) return;
            using var request = Request(new Endpoint(endpoint.Api, null), HttpMethod.Get,
                "/v1/assets/" + asset.File + "?h=" + Uri.EscapeDataString(asset.Sha256));
            using var timeout = TimeoutToken(20, token);
            using var response = await http.SendAsync(request, timeout.Token);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (!string.Equals(hash, asset.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException("SHA-256 divergente.");
            var registered = await js.InvokeAsync<bool>("game.glb.register", token,
                asset.Id, bytes, new { ScaleM = asset.ScaleM is > 0 ? asset.ScaleM : 1, Shadows = true });
            if (!registered) throw new InvalidDataException("O motor recusou o modelo.");
            await js.InvokeVoidAsync("game.glb.scatter", token,
                asset.Id, new { Count = 3, MinScale = 0.85, MaxScale = 1.25, Sink = 0.02 });
            _known.Add(asset.Id);
        }
        finally { _register.Release(); }
        await RefreshBuildAssetsAsync(token);
    }

    private async Task<Endpoint> EndpointAsync(CancellationToken token)
        => new(await js.InvokeAsync<string>("game.live.api", token),
            await js.InvokeAsync<string?>("game.live.token", token));

    private Task<T?> GetAsync<T>(Endpoint endpoint, string path, int seconds, CancellationToken token)
        => SendAsync<T>(endpoint, HttpMethod.Get, path, null, seconds, token);

    private async Task<T?> SendAsync<T>(Endpoint endpoint, HttpMethod method, string path,
        HttpContent? content, int seconds, CancellationToken token)
    {
        using var timeout = TimeoutToken(seconds, token);
        using var request = Request(endpoint, method, path);
        request.Content = content;
        using var response = await http.SendAsync(request, timeout.Token);
        if (response.IsSuccessStatusCode) return await response.Content.ReadFromJsonAsync<T>(timeout.Token);
        string? code = null;
        try { code = (await response.Content.ReadFromJsonAsync<Reply>(timeout.Token))?.Error; }
        catch (JsonException) { }
        throw new WorkshopException(response.StatusCode, code);
    }

    private static HttpRequestMessage Request(Endpoint endpoint, HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, endpoint.Api + path);
        request.SetBrowserRequestCache(BrowserRequestCache.NoStore);
        if (!string.IsNullOrEmpty(endpoint.Token)) request.Headers.Add("X-Game-Token", endpoint.Token);
        return request;
    }

    private static CancellationTokenSource TimeoutToken(int seconds, CancellationToken token)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        return timeout;
    }

    private static string Limit(string value, int max) => value[..Math.Min(value.Length, max)];
    private sealed record Endpoint(string Api, string? Token);
    private sealed record Catalog(Asset[]? Assets);
    private sealed record Asset(string Id, string File, string Sha256, double? ScaleM);
    private sealed record Reply(string? Status, string? Engine, string? Job, string? Error, Asset? Asset, string? Edit, bool Ok);
    private sealed record GlbState(Dictionary<string, JsonElement>? Instances);
    private sealed class WorkshopException(HttpStatusCode status, string? code) : HttpRequestException($"HTTP {(int)status}", null, status)
    {
        public string? Code => code;
    }
}
