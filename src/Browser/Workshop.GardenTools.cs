using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.JSInterop;

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
        an absolute value or color. Convert to the unit shown. Use "#RRGGBB" for color.
        For "+1 m" use add and value 1. For "-30 cm" use add and value -0.3.
        Questions, negations or unsupported requests make no tool call.
        """;

    private static string ModelTools(IEnumerable<string> properties) =>
        JsonSerializer.Serialize(new[]
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
                            property = new { type = "string", @enum = properties },
                            operation = new { type = "string", @enum = new[] { "add", "set" } },
                            value = new { description = "Number in the listed unit, or #RRGGBB for color",
                                anyOf = new[] { new { type = "number" }, new { type = "string" } } }
                        },
                        required = new[] { "property", "operation", "value" },
                        additionalProperties = false
                    }
                }
            }
        });

    private async Task<GuardianTurnResult> ProposeGardenToolAsync(Endpoint endpoint, string text, string engine, CancellationToken token)
    {
        var source = GeniusSource(engine);
        var seconds = engine == "gpu" ? 60 : 120;
        try
        {
            var result = await GetAsync<JsonElement>(endpoint, GardenRoute + "/tools", 8, token);
            var catalog = result.GetProperty("models");

            var units = RequestedUnits(text);
            var properties = catalog.EnumerateObject().SelectMany(asset =>
                asset.Value.GetProperty("parameters").EnumerateObject().Select(parameter =>
                {
                    var model = asset.Value.GetProperty("label").GetString() ?? asset.Name;
                    var label = parameter.Value.GetProperty("label").GetString();
                    var unit = parameter.Value.GetProperty("unit").GetString();
                    return new GardenProperty(asset.Name, parameter.Name, model, label, unit);
                })).Where(p => units.Count == 0 || (p.Unit is { } unit && units.Contains(unit)))
                .ToDictionary(p => p.Id, StringComparer.Ordinal);
            var allowPlan = units.Count == 0;
            if (properties.Count == 0 && !allowPlan)
            {
                const string prompt = "Responda como a Guardiã em português. Nenhuma propriedade compatível está disponível para editar.";
                var answer = await GenerateLocalAsync(prompt, text, engine, seconds, 120, token);
                return new(answer ?? "Não encontrei uma propriedade compatível por aqui.", source);
            }
            var definitions = string.Join('\n', properties.Values.Select(p => $"{p.Id}: {p.Model}, {p.Label} ({p.Unit})"));
            var instructions = ToolInstructions + "\nCatalog:\n" + definitions;
            var tools = ModelTools(properties.Keys);
            if (allowPlan)
            {
                var context = await GetAsync<JsonElement>(endpoint, GardenRoute + "/context", 8, token);
                if (context.TryGetProperty("prompt", out var prompt))
                    instructions += "\n" + prompt.GetString();
                var calls = JsonNode.Parse(tools)!.AsArray();
                if (properties.Count == 0) calls.Clear();
                calls.Add(JsonNode.Parse(GardenPlanTool));
                tools = calls.ToJsonString();
            }
            GardenModelProposal? proposal = null;
            var reply = await _genius.ReplyAsync(instructions, text, engine, seconds, allowPlan ? 480 : 100, token, output =>
            {
                proposal = ReadModelProposal(output, properties, allowPlan);
                if (proposal is null)
                {
                    using var document = JsonDocument.Parse(output);
                    return document.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String
                        && content.GetString() is { Length: > 0 } answer
                        ? Limit(answer, 300)
                        : "Nenhuma alteração foi aplicada no jardim.";
                }
                return proposal.Preview;
            }, tools);
            return new(reply, source, KeepDraft: proposal is not null, Proposal: proposal);
        }
        catch (Exception ex) when (!token.IsCancellationRequested &&
            ex is JSException or OperationCanceledException or HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return GuardianTurnResult.Retry("A IA local não conseguiu propor uma edição válida. Nenhuma alteração foi aplicada.", source);
        }
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
        if (args.EnumerateObject().Any(p => p.Name is not ("property" or "operation" or "value")))
            throw new JsonException("Unknown garden tool argument.");
        var id = args.GetProperty("property").GetString() ?? "";
        var operation = args.GetProperty("operation").GetString();
        if (operation is not ("set" or "add") || !properties.TryGetValue(id, out var property))
            throw new JsonException("The model does not declare that operation or parameter.");
        var value = args.GetProperty("value");
        var display = DisplayValue(value, property.Unit, operation);
        var verb = operation == "set" ? "Definir" : "Somar";
        var arguments = JsonSerializer.SerializeToElement(new
        {
            asset = property.Asset, parameter = property.Key, operation, value
        });
        return new(arguments, $"{verb} {display} em {property.Label} de até duas instâncias de {property.Model}. Quer aplicar?");
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

    internal async Task<GuardianTurnResult> ApplyGardenToolAsync(GardenModelProposal proposal, CancellationToken token)
    {
        var source = GeniusSource("cpu") + " · Oficina do jardim";
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
