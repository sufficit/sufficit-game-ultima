using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using Microsoft.JSInterop;

namespace Ultima.Pages;

public partial class Index
{
    private bool _reportOpen;
    private bool _reportBusy;
    private string _reportDraft = "";
    private string _reportStatus = "";

    private void BindDiagnosticReport(Dictionary<string, string> values)
    {
        values["reportOpen"] = _reportOpen ? "1" : "";
        values["reportBusy"] = _reportBusy ? "1" : "";
        values["reportDraft"] = _reportDraft;
        values["reportStatus"] = _reportStatus;
        values["reportToggle"] = _reportOpen ? "Cancelar relato" : "Reportar erro";
    }

    private async Task SendDiagnosticReportAsync()
    {
        if (!_reportOpen || _reportBusy) return;
        _reportBusy = true;
        _reportStatus = "Enviando diagnóstico…";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_browserLifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            var token = timeout.Token;
            var api = await JS.InvokeAsync<string>("game.live.api", token);
            var gpu = JS.InvokeAsync<LocalAiStatus>("game.ai.status", token).AsTask();
            var cpu = JS.InvokeAsync<LocalAiStatus>("game.aiCpu.status", token).AsTask();
            await Task.WhenAll(gpu, cpu);
            var description = _reportDraft.Trim();
            var payload = new GameDiagnosticReport(BuildId, _renderer, description[..Math.Min(400, description.Length)],
                DiagnosticEngine(await gpu), DiagnosticEngine(await cpu));
            using var request = new HttpRequestMessage(HttpMethod.Post, api.TrimEnd('/') + "/v1/diagnostics/reports")
            {
                Content = JsonContent.Create(payload, DiagnosticJson.Default.GameDiagnosticReport),
            };
            request.SetBrowserRequestCache(BrowserRequestCache.NoStore);
            using var response = await Http.SendAsync(request, token);
            if (response.StatusCode == HttpStatusCode.Created &&
                await response.Content.ReadFromJsonAsync(DiagnosticJson.Default.DiagnosticReceipt, token) is { Id.Length: 32 } receipt)
            {
                _reportStatus = $"Diagnóstico enviado. Protocolo {receipt.Id}.";
                _reportOpen = false;
                _reportDraft = "";
            }
            else
            {
                _reportStatus = response.StatusCode == HttpStatusCode.TooManyRequests
                    ? "Limite de relatos atingido. Aguarde um minuto."
                    : "O servidor ainda não confirmou o relato. Tente novamente.";
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JSException or JsonException)
        {
            _reportStatus = "Não consegui enviar o diagnóstico. Tente novamente.";
        }
        finally { _reportBusy = false; }
    }

    private static EngineDiagnostic DiagnosticEngine(LocalAiStatus status) =>
        new(status.State, status.Model, status.Progress, status.LastReplyMs);
}

internal sealed record EngineDiagnostic(string State, string? Model, double Progress, double? LastReplyMs);
internal sealed record GameDiagnosticReport(string Build, string Renderer, string Description,
    EngineDiagnostic Gpu, EngineDiagnostic Cpu);
internal sealed record DiagnosticReceipt(string Id);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GameDiagnosticReport))]
[JsonSerializable(typeof(DiagnosticReceipt))]
internal sealed partial class DiagnosticJson : JsonSerializerContext;
