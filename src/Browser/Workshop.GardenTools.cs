using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.JSInterop;
using Sufficit.AI.Genius.Embedded;

namespace Ultima.Browser;

public sealed partial class Workshop
{
    private const string CustomizeModelTool = "customize_model";
    private const string SubmitGardenPlan = "submit_garden_plan";
    private const string GardenPlanTool = """
        {"type":"function","function":{"name":"submit_garden_plan",
        "description":"Propose placement, landscape work or a batch of edits from the garden catalog.",
        "parameters":{"type":"object","properties":{"reply":{"type":"string"},
        "actions":{"type":"array","items":{"type":"object"}}},
        "required":["reply","actions"],"additionalProperties":false}}}
        """;
    private const string ToolInstructions = """
        You are the Garden Guardian. Only change the garden for an explicit request.
        Choose the exact property ID from the list. Use add for a difference, set for
        an absolute value or color. Convert to the unit shown. Divide centimetres by
        one hundred when the property unit is metres.
        Use inspect_scene to locate existing objects and resolve spatial references.
        Use the observed uid when updating one specific object. Never invent a uid.
        Negations and unsupported requests must not propose edits.
        Answer in Portuguese. A read-only scene inspection may precede an answer or a proposal.
        """;

    private static string ModelTools(Dictionary<string, GardenProperty> properties)
    {
        var valueSchema = properties.Values.All(property => property.Unit != "sRGB24")
            ? (object)new { type = "number", description = "Numeric amount in the property's unit" }
            : new { description = "Numeric amount or a six-digit hexadecimal sRGB color prefixed with #",
                anyOf = new[] { new { type = "number" }, new { type = "string" } } };
        return JsonSerializer.Serialize(new[]
        {
            new
            {
                type = "function",
                function = new
                {
                    name = CustomizeModelTool,
                    description = "Edit one existing model property listed in the catalog.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            property = new { type = "string", @enum = properties.Keys },
                            operation = new { type = "string", @enum = new[] { "add", "set" } },
                            uid = new { type = "string", description = "Observed instance UID, if the edit targets one specific object" },
                            value = valueSchema
                        },
                        required = new[] { "property", "operation", "value" },
                        additionalProperties = false
                    }
                }
            }
        });
    }

    private async Task<GuardianTurnResult> ProposeGardenToolAsync(Endpoint endpoint, string text, string engine, GardenModelProposal? pending, CancellationToken token)
    {
        var source = GeniusSource(engine);
        var failure = "context_failed";
        var units = RequestedUnits(text);
        var properties = new Dictionary<string, GardenProperty>(StringComparer.Ordinal);
        var allowPlan = units.Count == 0;
        try
        {
            var result = await GetAsync<JsonElement>(endpoint, GardenRoute + "/tools", 8, token);
            var catalog = result.GetProperty("models");

            AddProperties(catalog, units, properties);
            var inspect = result.TryGetProperty("sceneInspectionSupported", out var capability) && capability.ValueKind == JsonValueKind.True;
            if (properties.Count == 0 && !allowPlan && !inspect)
            {
                const string prompt = "Responda como a Guardiã em português. Nenhuma propriedade compatível está disponível para editar.";
                failure = "generation_failed";
                var answer = await GenerateLocalAsync(prompt, text, engine, token);
                return new(answer ?? "Não encontrei uma propriedade compatível por aqui.", source,
                    QualityOutcome: answer is null ? failure : null);
            }
            var instructions = ToolInstructions;
            if (pending is not null)
            {
                instructions += "\nA proposed edit is awaiting the player's approval. It has not been applied. " +
                    "Use the proposal to interpret references and answer questions. Propose a replacement only " +
                    "if the player explicitly asks to change the edit. Pending proposal: " + pending.Preview;
            }
            if (allowPlan)
            {
                var context = await GetAsync<JsonElement>(endpoint, GardenRoute + "/context", 8, token);
                if (context.TryGetProperty("toolInstructions", out var prompt) || context.TryGetProperty("prompt", out prompt))
                    instructions += "\n" + prompt.GetString();
            }
            var tools = BuildGardenTools(properties, allowPlan, inspect);
            instructions += PropertyInstructions(properties.Values);
            failure = "generation_failed";
            GardenModelProposal? proposal = null;
            var reply = await _genius.ReplyAsync(instructions, text, engine, token, output =>
            {
                // Failures after entering the interpreter concern the model output.
                failure = "tool_rejected";
                proposal = ReadModelProposal(output, properties, allowPlan);
                var response = proposal?.Preview ?? ReadToolReply(output);
                failure = "turn_failed";
                return response;
            }, tools, inspect ? ObserveAsync : null);
            return new(reply, source, KeepDraft: proposal is not null, Proposal: proposal);
        }
        catch (Exception ex) when (!token.IsCancellationRequested &&
            ex is JSException or OperationCanceledException or HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return GuardianTurnResult.Retry("A IA local não conseguiu propor uma edição válida. Nenhuma alteração foi aplicada.", source)
                with { QualityOutcome = failure };
        }

        async Task<(InferenceRequest Request, string Tools)?> ObserveAsync(InferenceRequest request, string output, CancellationToken ct)
        {
            failure = "tool_rejected";
            if (!TryReadSceneRequest(output, out var arguments))
            {
                failure = "generation_failed";
                return null;
            }
            failure = "context_failed";
            using var query = JsonContent.Create(new { name = InspectSceneTool, arguments });
            var scene = await SendAsync<JsonObject>(endpoint, HttpMethod.Post, GardenRoute + "/tools", query, 8, ct)
                ?? throw new JsonException("Missing scene observation.");
            if (scene.Remove("models", out var descriptions) && descriptions is JsonObject)
                AddProperties(JsonSerializer.SerializeToElement(descriptions), units, properties);
            failure = "generation_failed";
            var next = request with { Instructions = request.Instructions +
                "\nScene observation (read-only): " + scene.ToJsonString() + PropertyInstructions(properties.Values) +
                "\nAnswer the original request using this observation. Call an edit tool only to propose a requested change." };
            return (next, BuildGardenTools(properties, allowPlan, inspect: false));
        }
    }

    private static string ReadToolReply(string output)
    {
        using var document = JsonDocument.Parse(output);
        return document.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String
            && content.GetString() is { Length: > 0 } answer
            ? Limit(answer, 300)
            : "Nenhuma alteração foi aplicada no jardim.";
    }

    // Measurements narrow tool discovery by unit without guessing a model or an operation.
    private static HashSet<string> RequestedUnits(string message)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (LengthInRequest().IsMatch(message)) result.Add("m");
        if (LumensInRequest().IsMatch(message)) result.Add("lm");
        if (ColorInRequest().IsMatch(message)) result.Add("sRGB24");
        return result;
    }

    [GeneratedRegex(@"\d\s*(?:cm|m|metros?|cent[ií]metros?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LengthInRequest();

    [GeneratedRegex(@"\d\s*(?:lm|l[uú]mens?|l[uú]menes)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LumensInRequest();

    [GeneratedRegex(@"#[0-9a-f]{6}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ColorInRequest();

    private static GardenModelProposal? ReadModelProposal(string output, IReadOnlyDictionary<string, GardenProperty> properties, bool allowPlan)
    {
        using var document = JsonDocument.Parse(output);
        var calls = document.RootElement.GetProperty("calls");
        if (calls.GetArrayLength() == 0) return null;
        if (calls.GetArrayLength() != 1 || calls[0].GetProperty("type").GetString() != "function")
            throw new JsonException("Expected one garden tool call.");
        var function = calls[0].GetProperty("function");
        var name = function.GetProperty("name").GetString();
        using var parsed = JsonDocument.Parse(function.GetProperty("arguments").GetString() ?? throw new JsonException("Missing arguments."));
        var args = parsed.RootElement;
        if (allowPlan && name == SubmitGardenPlan)
        {
            var actions = args.GetProperty("actions");
            if (actions.ValueKind != JsonValueKind.Array)
                throw new JsonException("A garden plan must contain an action array.");
            if (actions.GetArrayLength() == 0) return null;
            return new(args.Clone(),
                $"Proponho {actions.GetArrayLength()} alterações no jardim. Quer aplicar?", IsPlan: true);
        }
        if (name != CustomizeModelTool) throw new JsonException("The requested garden tool is unavailable.");
        if (args.EnumerateObject().Any(p => p.Name is not ("property" or "operation" or "value" or "uid")))
            throw new JsonException("Unknown garden tool argument.");
        var id = args.GetProperty("property").GetString() ?? "";
        var operation = args.GetProperty("operation").GetString();
        if (operation is not ("set" or "add") || !properties.TryGetValue(id, out var property))
            throw new JsonException("The model does not declare that operation or parameter.");
        var value = args.GetProperty("value");
        var display = DisplayValue(value, property.Unit, operation);
        var verb = operation == "set" ? "Definir" : "Somar";
        var uid = args.TryGetProperty("uid", out var target) ? target.GetString() : null;
        if (args.TryGetProperty("uid", out _) && string.IsNullOrWhiteSpace(uid))
            throw new JsonException("An instance UID must not be empty.");
        var arguments = new JsonObject
        {
            ["asset"] = property.Asset, ["parameter"] = property.Key, ["operation"] = operation,
            ["value"] = JsonNode.Parse(value.GetRawText()),
        };
        if (uid is not null) arguments["uid"] = uid;
        var scope = uid is null ? $"até duas instâncias de {property.Model}" : $"{property.Model} ({uid})";
        return new(JsonSerializer.SerializeToElement(arguments), $"{verb} {display} em {property.Label} de {scope}. Quer aplicar?");
    }

    private sealed record GardenProperty(string Asset, string Key, string Model, string? Label, string? Unit)
    {
        internal string Id => Asset + "/" + Key;
    }

    private static string DisplayValue(JsonElement value, string? unit, string operation)
    {
        if (unit == "sRGB24" && operation == "set")
        {
            if (value.ValueKind == JsonValueKind.String && value.GetString() is { Length: 7 } color && color[0] == '#' &&
                color.AsSpan(1).ToArray().All(char.IsAsciiHexDigit))
                return color.ToUpperInvariant();
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var rgb) && rgb is >= 0 and <= 0xffffff)
                return $"#{rgb:X6}";
        }
        else if (unit != "sRGB24" && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number))
            return $"{number.ToString("G", CultureInfo.GetCultureInfo("pt-BR"))} {unit}";
        throw new JsonException("Invalid parameter value.");
    }

    internal async Task<GuardianTurnResult> ApplyGardenToolAsync(GardenModelProposal proposal, string confirmation,
        string engine, CancellationToken token)
    {
        var local = engine is "cpu" or "gpu";
        var source = local ? GeniusSource(engine) + " · Oficina do jardim" : "Oficina do jardim";
        var result = await SendGardenProposalAsync(proposal, source, token);
        if (local && !token.IsCancellationRequested)
            await _genius.RememberReceiptAsync(confirmation, $"Proposta confirmada pelo jogador: {proposal.Preview}\n{result.Reply}", token);
        return result;
    }

    private async Task<GuardianTurnResult> SendGardenProposalAsync(GardenModelProposal proposal, string source,
        CancellationToken token)
    {
        try
        {
            var endpoint = await EndpointAsync(token);
            if (string.IsNullOrEmpty(endpoint.Token)) return GuardianTurnResult.Retry(OfflineGarden, source);
            using var request = proposal.IsPlan
                ? JsonContent.Create(new { actions = proposal.Arguments.GetProperty("actions") })
                : JsonContent.Create(new { name = CustomizeModelTool, arguments = proposal.Arguments });
            var result = await SendAsync<JsonElement>(endpoint, HttpMethod.Post,
                GardenRoute + (proposal.IsPlan ? "/edits" : "/tools"), request, 12, token);
            var reply = ReadReply(result) ?? (proposal.IsPlan ? ReadReply(proposal.Arguments) : null);
            return await GardenReplyAsync(result, reply ?? "O jardim não confirmou o ajuste.", source, token, requiresChanges: proposal.IsPlan);
        }
        catch (WorkshopException ex) when (!token.IsCancellationRequested && ex.Code == "model_parameter_out_of_range")
        {
            return GuardianTurnResult.Retry("A alteração ultrapassaria os limites do modelo. Nenhuma instância foi alterada.", source);
        }
        catch (Exception ex) when (!token.IsCancellationRequested && ex is HttpRequestException or JSException or JsonException or OperationCanceledException)
        {
            return GuardianTurnResult.Retry("Não consegui confirmar o ajuste. Confira o cenário antes de tentar novamente.", source);
        }
    }
}

internal sealed record GardenModelProposal(JsonElement Arguments, string Preview, bool IsPlan = false);
