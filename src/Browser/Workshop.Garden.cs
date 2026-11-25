using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;
using Ultima.Garden;

namespace Ultima.Browser;

public sealed partial class Workshop
{
    private const string GardenRoute = "/v1/game/garden";
    private const string OfflineGarden = "Conecte-se ao servidor do jogo para aplicar alterações no jardim.";
    private const string OfflinePrompt =
        "Você é a Guardiã do Jardim de Ultima. Responda em português do Brasil com até duas frases curtas. " +
        "Converse e sugira ideias. Você está sem conexão com o jardim e não pode fazer alterações.";

    private sealed record GardenPlacement(string Asset, double X, double Z);
    private sealed record GardenMap(GardenPlacement[] Placements);
    private sealed record PlaceLantern(double X, double Z)
    {
        public string Action => "place";
        public string Asset => "prop.lantern";
    }
    private sealed record LanternBatch(PlaceLantern[] Actions);

    public async Task<(string Reply, string Source)> LightGardenAsync(CancellationToken token)
    {
        try
        {
            var endpoint = await EndpointAsync(token);
            if (string.IsNullOrEmpty(endpoint.Token))
                return (OfflineGarden, "");

            var map = await GetAsync<GardenMap>(endpoint, "/v1/game/map", 8, token);
            if (map?.Placements is null)
                return ("Não consegui consultar as luminárias do jardim. Tente novamente.", "");

            PlaceLantern[] entrance = [new(-1.8, -11), new(1.8, -11)];
            var missing = entrance.Where(lantern => !map.Placements.Any(placed =>
                placed.Asset == lantern.Asset && double.Hypot(placed.X - lantern.X, placed.Z - lantern.Z) < 0.25)).ToArray();
            if (missing.Length == 0)
                return ("A entrada do jardim já está iluminada.", "Oficina do jardim");

            using var request = JsonContent.Create(new LanternBatch(missing));
            var result = await SendAsync<JsonElement>(endpoint, HttpMethod.Post, GardenRoute + "/decor", request, 12, token);
            return await GardenReplyAsync(result, "As duas luminárias estão na entrada do jardim.", "Oficina do jardim", token);
        }
        catch (WorkshopException ex) when (!token.IsCancellationRequested && ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            return (OfflineGarden, "");
        }
        catch (Exception ex) when (!token.IsCancellationRequested && ex is HttpRequestException or JSException or JsonException or OperationCanceledException)
        {
            return ("Não consegui confirmar a iluminação do jardim. Tente novamente.", "");
        }
    }

    public async Task<(string Reply, string Source)> TalkToGuardianAsync(
        string text, string recent, bool gpu, bool cpu, CancellationToken token)
    {
        try
        {
            var endpoint = await EndpointAsync(token);
            if (string.IsNullOrEmpty(endpoint.Token))
                return await TalkOfflineAsync(text, recent, gpu, cpu, token);
            if ((gpu || cpu) && !GardenLanternIntent.Matches(text))
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
            var source = turn.TryGetProperty("source", out var origin) && origin.GetString() == "garden"
                ? "Oficina do jardim" : "IA do servidor";
            return await GardenReplyAsync(turn, ReadReply(turn) ?? "O pedido foi recebido.", source, token);
        }
        catch (WorkshopException ex) when (!token.IsCancellationRequested)
        {
            return ex.Code switch
            {
                "ai_unavailable" when gpu || cpu => ("A IA local está ativa, mas não gerou um plano válido para este pedido. Tente pedir uma mudança por vez.", ""),
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

    private async Task<(string Reply, string Source)> TalkOfflineAsync(
        string text, string recent, bool gpu, bool cpu, CancellationToken token)
    {
        var prompt = recent.Length == 0 ? OfflinePrompt : OfflinePrompt + "\nConversa recente:\n" + Limit(recent, 700);
        var reply = gpu ? await GenerateLocalAsync(prompt, text, "ai", 60, 120, token) : null;
        if (!string.IsNullOrWhiteSpace(reply))
            return ($"{Limit(reply.Trim(), 300)} {OfflineGarden}", "GPU do jogador");
        reply = cpu ? await GenerateLocalAsync(prompt, text, "aiCpu", 120, 120, token) : null;
        if (!string.IsNullOrWhiteSpace(reply))
            return ($"{Limit(reply.Trim(), 300)} {OfflineGarden}", "CPU do jogador");
        return (gpu || cpu ? $"A IA local está ativa, mas não conseguiu responder. {OfflineGarden}"
            : "Ative a IA local ou conecte-se ao servidor do jogo para conversar comigo.", "");
    }

    private async Task<string?> GenerateLocalAsync(string prompt, string text, string engine, int seconds,
        int maxTokens, CancellationToken token)
    {
        using var timeout = TimeoutToken(seconds, token);
        try { return await js.InvokeAsync<string?>($"game.{engine}.garden", timeout.Token, prompt, text, maxTokens); }
        catch (Exception ex) when (!token.IsCancellationRequested && ex is JSException or OperationCanceledException) { return null; }
    }

    private async Task<(string Reply, string Source)?> AskLocalGardenAsync(Endpoint endpoint, string prompt, string text,
        string engine, string source, int seconds, CancellationToken token)
    {
        var output = await GenerateLocalAsync(prompt, text, engine, seconds, 1200, token);
        if (string.IsNullOrWhiteSpace(output)) return null;
        JsonDocument plan;
        try
        {
            output = output.Trim();
            if (output.StartsWith("```", StringComparison.Ordinal) && output.EndsWith("```", StringComparison.Ordinal)
                && output.IndexOf('\n') is > 0 and var firstLine && firstLine < output.Length - 3)
                output = output[(firstLine + 1)..^3].Trim();
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
