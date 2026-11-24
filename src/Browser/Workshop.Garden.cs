using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;

namespace Ultima.Browser;

public sealed partial class Workshop
{
    private const string GardenRoute = "/v1/game/garden";

    public async Task<(string Reply, string Source)> TalkToGuardianAsync(
        string text, string recent, bool gpu, bool cpu, CancellationToken token)
    {
        try
        {
            var endpoint = await EndpointAsync(token);
            if (string.IsNullOrEmpty(endpoint.Token))
                return ("Conecte-se ao servidor do jogo para conversar comigo e editar o jardim.", "");
            if (gpu || cpu)
            {
                var context = await GetAsync<JsonElement>(endpoint, GardenRoute + "/context", 8, token);
                if (context.TryGetProperty("prompt", out var prompt) && prompt.GetString() is { Length: > 0 } instructions)
                {
                    if (recent.Length > 0) instructions += "\nContexto de turnos anteriores:\n" + recent;
                    if (gpu && await AskLocalGardenAsync(endpoint, instructions, text, "ai", "GPU do jogador", 60, token) is { } gpuReply)
                        return gpuReply;
                    if (cpu && await AskLocalGardenAsync(endpoint, instructions, text, "aiCpu", "CPU do jogador", 120, token) is { } cpuReply)
                        return cpuReply;
                }
            }
            using var message = JsonContent.Create(new Dictionary<string, string> { ["message"] = text });
            var turn = await SendAsync<JsonElement>(endpoint, HttpMethod.Post, GardenRoute + "/dialog", message, 18, token);
            return await GardenReplyAsync(turn, ReadReply(turn) ?? "O pedido foi recebido.", "IA do servidor", token);
        }
        catch (WorkshopException ex) when (!token.IsCancellationRequested)
        {
            return ex.Code switch
            {
                "ai_unavailable" => ("Minha IA está indisponível. Ative um modelo local ou tente quando a IA do servidor estiver disponível.", ""),
                "guardian_out_of_reach" => ("Aproxime-se de mim para conversar sobre o jardim.", ""),
                "invalid_ai_response" => ("Não consegui interpretar o pedido. Tente descrevê-lo de outra forma.", ""),
                "rate_limited" => ("Dê-me um instante antes de fazer outro pedido.", ""),
                _ when ex.StatusCode == HttpStatusCode.BadRequest => ($"O jardim recusou estas mudanças ({ex.Code ?? "pedido inválido"}).", ""),
                _ => ("Não consegui acessar a Oficina do jardim.", "")
            };
        }
        catch (Exception ex) when (!token.IsCancellationRequested && ex is HttpRequestException or JSException or JsonException or OperationCanceledException)
        {
            return ("Não consegui acessar a Oficina do jardim.", "");
        }
    }

    private async Task<(string Reply, string Source)?> AskLocalGardenAsync(Endpoint endpoint, string prompt, string text,
        string engine, string source, int seconds, CancellationToken token)
    {
        string? output;
        using (var timeout = TimeoutToken(seconds, token))
        {
            try { output = await js.InvokeAsync<string?>($"game.{engine}.garden", timeout.Token, prompt, text); }
            catch (Exception ex) when (!token.IsCancellationRequested && ex is JSException or OperationCanceledException) { return null; }
        }
        if (string.IsNullOrWhiteSpace(output)) return null;
        JsonDocument plan;
        try
        {
            output = output.Trim();
            if (output.StartsWith("```json", StringComparison.Ordinal) && output.EndsWith("```", StringComparison.Ordinal))
                output = output[7..^3].Trim();
            plan = JsonDocument.Parse(output);
        }
        catch (JsonException) { return null; }
        using (plan)
        {
            var root = plan.RootElement;
            var reply = ReadReply(root);
            if (reply is null || !root.TryGetProperty("actions", out var actions) || actions.ValueKind != JsonValueKind.Array)
                return null;
            if (actions.GetArrayLength() == 0) return (reply, source);
            token.ThrowIfCancellationRequested();
            using var request = new StringContent("{\"actions\":" + actions.GetRawText() + "}", System.Text.Encoding.UTF8, "application/json");
            var result = await SendAsync<JsonElement>(endpoint, HttpMethod.Post, GardenRoute + "/edits", request, 12, token);
            return await GardenReplyAsync(result, reply, source, token);
        }
    }

    private async Task<(string Reply, string Source)> GardenReplyAsync(JsonElement result, string reply, string source, CancellationToken token)
    {
        if (result.TryGetProperty("map", out var map))
            await js.InvokeVoidAsync("game.live.paintMap", token, map);
        if (result.TryGetProperty("applied", out var count) && count.TryGetInt32(out var applied) && applied > 0)
            reply = $"Apliquei as mudanças no jardim. {reply}";
        return (reply, source);
    }

    private static string? ReadReply(JsonElement value) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty("reply", out var property) && property.ValueKind == JsonValueKind.String &&
        property.GetString()?.Trim() is { Length: > 0 } text ? Limit(text, 340) : null;
}
