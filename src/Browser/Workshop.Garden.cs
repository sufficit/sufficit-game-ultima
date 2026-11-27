using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;

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
                return new("A entrada do jardim já está iluminada.", "Oficina do jardim", QualityOutcome: "no_change");

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
        var local = gpu || cpu;
        try
        {
            var endpoint = await EndpointAsync(token);
            if (string.IsNullOrEmpty(endpoint.Token))
                return await TalkOfflineAsync(text, gpu, cpu, token);
            if (local)
                return await ProposeGardenToolAsync(endpoint, text, gpu ? "gpu" : "cpu", token);
            // Preview support is required before starting a hosted background turn.
            var protocol = await GetAsync<JsonElement>(endpoint, GardenRoute + "/context", 8, token);
            if (protocol.ValueKind != JsonValueKind.Object ||
                !protocol.TryGetProperty("toolProposalsSupported", out var required) || required.ValueKind != JsonValueKind.True)
                return GuardianTurnResult.Retry("A IA do servidor ainda não oferece prévias de edição. Use a IA na CPU.");

            using var message = JsonContent.Create(new { message = text, preview = true });
            var turn = await SendAsync<JsonElement>(endpoint, HttpMethod.Post, GardenRoute + "/dialog", message, null, token);
            if (turn.ValueKind != JsonValueKind.Object)
                return GuardianTurnResult.Retry("O jardim retornou uma resposta inválida. Confira o cenário antes de tentar novamente.");
            var source = turn.TryGetProperty("source", out var origin) && origin.ValueKind == JsonValueKind.String && origin.GetString() == "garden"
                ? "Oficina do jardim" : "IA do servidor";
            if (ReadReply(turn) is not { } serverReply)
                return GuardianTurnResult.Retry("O jardim não confirmou o pedido. Confira o cenário antes de tentar novamente.", source);
            if (turn.TryGetProperty("actions", out var actions) && actions.ValueKind == JsonValueKind.Array && actions.GetArrayLength() > 0)
            {
                var proposal = new GardenModelProposal(turn.Clone(),
                    $"Proponho {actions.GetArrayLength()} alterações no jardim. Quer aplicar?", IsPlan: true);
                return new(proposal.Preview, source, KeepDraft: true, Proposal: proposal);
            }
            return new(serverReply, source);
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
        var reply = gpu ? await GenerateLocalAsync(OfflinePrompt, text, "gpu", token) : null;
        if (reply is not null)
            return new($"{reply} {OfflineGarden}", GeniusSource("gpu"));
        reply = cpu ? await GenerateLocalAsync(OfflinePrompt, text, "cpu", token) : null;
        if (reply is not null)
            return new($"{reply} {OfflineGarden}", GeniusSource("cpu"));
        return gpu || cpu ? GuardianTurnResult.Retry($"A IA local não conseguiu responder. {OfflineGarden}", GuardianGenius.SourcePrefix) with { QualityOutcome = "generation_failed" }
            : GuardianTurnResult.Retry("Ative a IA local ou conecte-se ao servidor do jogo para conversar comigo.");
    }

    private static string GeniusSource(string engine) => $"{GuardianGenius.SourcePrefix} · {engine.ToUpperInvariant()}";

    private async Task<string?> GenerateLocalAsync(string prompt, string text, string engine, CancellationToken token, Func<string, string>? interpret = null)
    {
        try { return await _genius.ReplyAsync(prompt, text, engine, token, interpret ?? (reply => Limit(reply, 300))); }
        catch (Exception ex) when (!token.IsCancellationRequested &&
            ex is JSException or OperationCanceledException or JsonException or InvalidOperationException)
        {
            return null;
        }
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
            : new(reply, source, QualityOutcome: "no_change");
    }

    private static string? ReadReply(JsonElement value) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty("reply", out var property) && property.ValueKind == JsonValueKind.String &&
        property.GetString()?.Trim() is { Length: > 0 } text ? Limit(text, 340) : null;
}

/// <summary>The visible reply and whether the submitted text should stay available to retry.</summary>
internal sealed record GuardianTurnResult(string Reply, string Source, bool KeepDraft = false, GardenModelProposal? Proposal = null,
    string? QualityOutcome = null)
{
    public static GuardianTurnResult Retry(string reply, string source = "") => new(reply, source, KeepDraft: true);
}
