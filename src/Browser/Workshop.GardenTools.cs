using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;

namespace Ultima.Browser;

public sealed partial class Workshop
{
    private const string HeightTool = "adjust_lantern_height";
    private const string ToolInstructions =
        "You are a model that can do function calling with the following functions. " +
        "Call a function only for explicit requests to change the garden. Never call it for a question, negation or unclear request.";
    private const string HeightTools = """
        [{
          "type": "function",
          "function": {
            "name": "adjust_lantern_height",
            "description": "Change the pole height of the nearby garden lanterns by a relative number of metres. Only call when the user requests a height change. Negative lowers. Em português: aumentar ou diminuir a altura das luminárias existentes em metros.",
            "parameters": {
              "type": "object",
              "properties": { "delta_m": { "type": "number", "description": "Signed height change per lantern in meters" } },
              "required": ["delta_m"],
              "additionalProperties": false
            }
          }
        }]
        """;

    internal async Task<GuardianTurnResult> ProposeGardenToolAsync(string text, CancellationToken token)
    {
        if (await _genius.MemoryCommandAsync(text, token) is { } memory)
            return new(memory, GuardianGenius.SourcePrefix);
        var source = GeniusSource("cpu");
        try
        {
            if (string.IsNullOrEmpty((await EndpointAsync(token)).Token))
                return GuardianTurnResult.Retry(OfflineGarden, source);
            LanternHeightProposal? proposal = null;
            var reply = await _genius.ReplyAsync(ToolInstructions, text, "cpu", 120, 80, token, output =>
            {
                proposal = ReadHeightProposal(output);
                if (proposal is null) return "Nenhuma alteração foi aplicada no jardim.";
                var magnitude = Math.Abs(proposal.DeltaM).ToString("0.##", CultureInfo.GetCultureInfo("pt-BR"));
                var verb = proposal.DeltaM > 0 ? "aumentar" : "diminuir";
                return $"Posso {verb} em {magnitude} m a altura de até duas luminárias próximas. Quer aplicar?";
            }, HeightTools);
            return new(reply, source, KeepDraft: true, Proposal: proposal);
        }
        catch (Exception ex) when (!token.IsCancellationRequested &&
            ex is JSException or OperationCanceledException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return GuardianTurnResult.Retry("A IA local não conseguiu propor uma edição válida. Nenhuma alteração foi aplicada.", source);
        }
    }

    private static LanternHeightProposal? ReadHeightProposal(string output)
    {
        using var document = JsonDocument.Parse(output);
        var calls = document.RootElement.GetProperty("calls");
        if (calls.GetArrayLength() == 0) return null;
        if (calls.GetArrayLength() != 1 || calls[0].GetProperty("type").GetString() != "function")
            throw new JsonException("Expected one garden tool call.");
        var function = calls[0].GetProperty("function");
        if (function.GetProperty("name").GetString() != HeightTool)
            throw new JsonException("The requested garden tool is unavailable.");
        var arguments = function.GetProperty("arguments").GetString() ?? throw new JsonException("Missing arguments.");
        using var parsed = JsonDocument.Parse(arguments);
        var fields = parsed.RootElement.EnumerateObject();
        if (!fields.MoveNext() || fields.Current.Name != "delta_m" || fields.Current.Value.ValueKind != JsonValueKind.Number ||
            !fields.Current.Value.TryGetDouble(out var delta) || !double.IsFinite(delta) ||
            Math.Abs(delta) is < 0.01 or > 4.5 || Math.Round(delta, 2) != delta || fields.MoveNext())
            throw new JsonException("Invalid height displacement.");
        return new(delta);
    }

    internal async Task<GuardianTurnResult> ApplyGardenToolAsync(LanternHeightProposal proposal, CancellationToken token)
    {
        var source = GeniusSource("cpu") + " · Oficina do jardim";
        try
        {
            var endpoint = await EndpointAsync(token);
            if (string.IsNullOrEmpty(endpoint.Token)) return GuardianTurnResult.Retry(OfflineGarden, source);
            using var request = JsonContent.Create(new
            {
                name = HeightTool,
                arguments = new { delta_m = proposal.DeltaM }
            });
            var result = await SendAsync<JsonElement>(endpoint, HttpMethod.Post, GardenRoute + "/tools", request, 12, token);
            var reply = ReadReply(result) ?? "O jardim não confirmou o ajuste.";
            return await GardenReplyAsync(result, reply, source, token);
        }
        catch (WorkshopException ex) when (!token.IsCancellationRequested && ex.Code == "lantern_height_out_of_range")
        {
            return GuardianTurnResult.Retry("Esse ajuste ultrapassaria o limite de uma luminária. Nenhuma foi alterada.", source);
        }
        catch (Exception ex) when (!token.IsCancellationRequested && ex is HttpRequestException or JSException or JsonException or OperationCanceledException)
        {
            return GuardianTurnResult.Retry("Não consegui confirmar o ajuste. Confira o cenário antes de tentar novamente.", source);
        }
    }
}

internal sealed record LanternHeightProposal(double DeltaM);
