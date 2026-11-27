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

    internal async Task<GuardianTurnResult> LightGardenAsync(CancellationToken token)
    {
        try
        {
            var endpoint = await EndpointAsync(token);
            if (string.IsNullOrEmpty(endpoint.Token))
                return GuardianTurnResult.Retry(OfflineGarden);

            var map = await GetAsync<GardenMap>(endpoint, "/v1/game/map", 8, token);
            if (map?.Placements is null)
                return GuardianTurnResult.Retry("Não consegui consultar as luminárias do jardim. Tente novamente.");

            PlaceLantern[] entrance = [new(-1.8, -11), new(1.8, -11)];
            var missing = entrance.Where(lantern => !map.Placements.Any(placed =>
                placed.Asset == lantern.Asset && double.Hypot(placed.X - lantern.X, placed.Z - lantern.Z) < 0.25)).ToArray();
            if (missing.Length == 0)
                return new("A entrada do jardim já está iluminada.", "Oficina do jardim");

            using var request = JsonContent.Create(new LanternBatch(missing));
            var result = await SendAsync<JsonElement>(endpoint, HttpMethod.Post, GardenRoute + "/decor", request, 12, token);
            return await GardenReplyAsync(result, "As duas luminárias estão na entrada do jardim.", "Oficina do jardim", token, requiresChanges: true);
        }
        catch (WorkshopException ex) when (!token.IsCancellationRequested && ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            return GuardianTurnResult.Retry(OfflineGarden);
        }
        catch (Exception ex) when (!token.IsCancellationRequested && ex is HttpRequestException or JSException or JsonException or OperationCanceledException)
        {
            return GuardianTurnResult.Retry("Não consegui confirmar a iluminação do jardim. Tente novamente.");
        }
    }

    internal async Task<GuardianTurnResult> TalkToGuardianAsync(
        string text, bool gpu, bool cpu, CancellationToken token)
    {
        if (await _genius.MemoryCommandAsync(text, token) is { } reply)
            return new(reply, GuardianGenius.SourcePrefix);
        var local = (gpu || cpu) && GardenLanternIntent.Parse(text) == LanternAdjustment.None;
        try
        {
            var endpoint = await EndpointAsync(token);
            if (string.IsNullOrEmpty(endpoint.Token))
                return await TalkOfflineAsync(text, gpu, cpu, token);
            if (local)
            {
                var context = await GetAsync<JsonElement>(endpoint, GardenRoute + "/context", 8, token);
                if (context.ValueKind == JsonValueKind.Object && context.TryGetProperty("prompt", out var prompt) &&
                    prompt.ValueKind == JsonValueKind.String && prompt.GetString() is { Length: > 0 } instructions)
                {
                    if (gpu && await AskLocalGardenAsync(endpoint, instructions, text, "gpu", 60, token) is { } gpuReply)
                        return gpuReply;
                    if (cpu && await AskLocalGardenAsync(endpoint, instructions, text, "cpu", 120, token) is { } cpuReply)
                        return cpuReply;
                }
                return GuardianTurnResult.Retry("A IA local não conseguiu concluir o pedido. Tente novamente.", GuardianGenius.SourcePrefix);
            }
            using var message = JsonContent.Create(new Dictionary<string, string> { ["message"] = text });
            var turn = await SendAsync<JsonElement>(endpoint, HttpMethod.Post, GardenRoute + "/dialog", message, 18, token);
            if (turn.ValueKind != JsonValueKind.Object)
                return GuardianTurnResult.Retry("O jardim retornou uma resposta inválida. Confira o cenário antes de tentar novamente.");
            var source = turn.TryGetProperty("source", out var origin) && origin.ValueKind == JsonValueKind.String && origin.GetString() == "garden"
                ? "Oficina do jardim" : "IA do servidor";
            if (ReadReply(turn) is not { } serverReply)
                return GuardianTurnResult.Retry("O jardim não confirmou o pedido. Confira o cenário antes de tentar novamente.", source);
            return await GardenReplyAsync(turn, serverReply, source, token);
        }
        catch (WorkshopException ex) when (!token.IsCancellationRequested)
        {
            var source = local ? GuardianGenius.SourcePrefix : "";
            return ex.Code switch
            {
                "ai_unavailable" when gpu || cpu => GuardianTurnResult.Retry("A IA local está ativa, mas não gerou um plano válido para este pedido. Tente pedir uma mudança por vez.", source),
                "ai_unavailable" => GuardianTurnResult.Retry("Minha IA está indisponível. Ative um modelo local ou tente quando a IA do servidor estiver disponível.", source),
                "guardian_out_of_reach" => GuardianTurnResult.Retry("Aproxime-se de mim para conversar sobre o jardim.", source),
                "invalid_ai_response" => GuardianTurnResult.Retry("Não consegui interpretar o pedido. Tente descrevê-lo de outra forma.", source),
                "rate_limited" => GuardianTurnResult.Retry("Dê-me um instante antes de fazer outro pedido.", source),
                _ when ex.StatusCode == HttpStatusCode.BadRequest => GuardianTurnResult.Retry($"O jardim recusou estas mudanças ({ex.Code ?? "pedido inválido"}).", source),
                _ => GuardianTurnResult.Retry("Não consegui acessar a Oficina do jardim.", source)
            };
        }
        catch (Exception ex) when (!token.IsCancellationRequested && ex is HttpRequestException or JSException or JsonException or OperationCanceledException)
        {
            return GuardianTurnResult.Retry("Não consegui confirmar o pedido com a Oficina do jardim. Confira o cenário antes de tentar novamente.",
                local ? GuardianGenius.SourcePrefix : "");
        }
    }

    private async Task<GuardianTurnResult> TalkOfflineAsync(
        string text, bool gpu, bool cpu, CancellationToken token)
    {
        var reply = gpu ? await GenerateLocalAsync(OfflinePrompt, text, "gpu", 60, 120, token) : null;
        if (reply is not null)
            return new($"{reply} {OfflineGarden}", GeniusSource("gpu"));
        reply = cpu ? await GenerateLocalAsync(OfflinePrompt, text, "cpu", 120, 120, token) : null;
        if (reply is not null)
            return new($"{reply} {OfflineGarden}", GeniusSource("cpu"));
        return gpu || cpu ? GuardianTurnResult.Retry($"A IA local não conseguiu responder. {OfflineGarden}", GuardianGenius.SourcePrefix)
            : GuardianTurnResult.Retry("Ative a IA local ou conecte-se ao servidor do jogo para conversar comigo.");
    }

    private static string GeniusSource(string engine) => $"{GuardianGenius.SourcePrefix} · {engine.ToUpperInvariant()}";

    private async Task<string?> GenerateLocalAsync(string prompt, string text, string engine, int seconds,
        int maxTokens, CancellationToken token, Func<string, string>? interpret = null)
    {
        try { return await _genius.ReplyAsync(prompt, text, engine, seconds, maxTokens, token, interpret ?? (reply => Limit(reply, 300))); }
        catch (Exception ex) when (!token.IsCancellationRequested &&
            ex is JSException or OperationCanceledException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private async Task<GuardianTurnResult?> AskLocalGardenAsync(Endpoint endpoint, string prompt, string text,
        string engine, int seconds, CancellationToken token)
    {
        JsonElement actions = default;
        string? proposedReply = null;
        var reply = await GenerateLocalAsync(prompt, text, engine, seconds, 1200, token, output =>
        {
            using var document = JsonDocument.Parse(UnwrapJson(output));
            var root = document.RootElement;
            var response = ReadReply(root);
            if (response is null || !root.TryGetProperty("actions", out var proposed) || proposed.ValueKind != JsonValueKind.Array)
                throw new JsonException("Plano do jardim inválido.");
            actions = proposed.Clone();
            proposedReply = response;
            return actions.GetArrayLength() == 0 ? response : "Propus alterações para o jardim.";
        });
        if (reply is null || proposedReply is null) return null;
        var source = GeniusSource(engine);
        if (actions.GetArrayLength() == 0) return new(reply, source);

        token.ThrowIfCancellationRequested();
        using var request = new StringContent("{\"actions\":" + actions.GetRawText() + "}", System.Text.Encoding.UTF8, "application/json");
        var result = await SendAsync<JsonElement>(endpoint, HttpMethod.Post, GardenRoute + "/edits", request, 12, token);
        return await GardenReplyAsync(result, proposedReply, source, token, requiresChanges: true);
    }

    private static string UnwrapJson(string output)
    {
        if (output.StartsWith("```", StringComparison.Ordinal) && output.EndsWith("```", StringComparison.Ordinal)
            && output.IndexOf('\n') is > 0 and var firstLine && firstLine < output.Length - 3)
            return output[(firstLine + 1)..^3].Trim();
        return output;
    }

    private async Task<GuardianTurnResult> GardenReplyAsync(JsonElement result, string reply, string source,
        CancellationToken token, bool requiresChanges = false)
    {
        if (result.ValueKind != JsonValueKind.Object)
            return GuardianTurnResult.Retry("O jardim retornou uma resposta inválida. Confira o cenário antes de tentar novamente.", source);

        var confirmed = result.TryGetProperty("applied", out var count) && count.ValueKind == JsonValueKind.Number &&
            count.TryGetInt32(out var applied) && applied > 0;
        if (result.TryGetProperty("map", out var map))
        {
            try { await js.InvokeVoidAsync("game.live.paintMap", token, map); }
            catch (JSException) when (!token.IsCancellationRequested)
            {
                // The server receipt remains authoritative when the local refresh fails.
                if (confirmed) return new("O servidor aplicou as mudanças. Recarregue a página para atualizar o cenário.", source);
            }
        }
        if (confirmed) return new($"Apliquei as mudanças no jardim. {reply}", source);
        return requiresChanges
            ? GuardianTurnResult.Retry("O jardim não confirmou nenhuma alteração. Confira o cenário antes de tentar novamente.", source)
            : new(reply, source);
    }

    private static string? ReadReply(JsonElement value) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty("reply", out var property) && property.ValueKind == JsonValueKind.String &&
        property.GetString()?.Trim() is { Length: > 0 } text ? Limit(text, 340) : null;
}

/// <summary>The visible reply and whether the submitted text should stay available to retry.</summary>
internal sealed record GuardianTurnResult(string Reply, string Source, bool KeepDraft = false)
{
    public static GuardianTurnResult Retry(string reply, string source = "") => new(reply, source, KeepDraft: true);
}
