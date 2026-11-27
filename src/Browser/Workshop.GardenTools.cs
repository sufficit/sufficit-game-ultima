using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;

namespace Ultima.Browser;

public sealed partial class Workshop
{
    private const string CustomizeModelTool = "customize_model";
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

    internal async Task<GuardianTurnResult> ProposeGardenToolAsync(string text, CancellationToken token)
    {
        if (await _genius.MemoryCommandAsync(text, token) is { } memory)
            return new(memory, GuardianGenius.SourcePrefix);
        var source = GeniusSource("cpu");
        try
        {
            var endpoint = await EndpointAsync(token);
            if (string.IsNullOrEmpty(endpoint.Token)) return GuardianTurnResult.Retry(OfflineGarden, source);
            var result = await GetAsync<JsonElement>(endpoint, GardenRoute + "/tools", 8, token);
            var catalog = result.GetProperty("models");
            if (!catalog.EnumerateObject().Any())
                return GuardianTurnResult.Retry("Ainda não há modelos colocados para personalizar.", source);

            var properties = catalog.EnumerateObject().SelectMany(asset =>
                asset.Value.GetProperty("parameters").EnumerateObject().Select(parameter =>
                {
                    var model = asset.Value.GetProperty("label").GetString() ?? asset.Name;
                    var label = parameter.Value.GetProperty("label").GetString();
                    var unit = parameter.Value.GetProperty("unit").GetString();
                    return new GardenProperty(asset.Name, parameter.Name, model, label, unit);
                })).ToDictionary(p => p.Id, StringComparer.Ordinal);
            var definitions = string.Join('\n', properties.Values.Select(p => $"{p.Id}: {p.Model}, {p.Label} ({p.Unit})"));
            GardenModelProposal? proposal = null;
            var reply = await _genius.ReplyAsync(ToolInstructions + "\nCatalog:\n" + definitions, text, "cpu", 120, 100, token, output =>
            {
                proposal = ReadModelProposal(output, properties);
                if (proposal is null) return "Nenhuma alteração foi aplicada no jardim.";
                return proposal.Preview;
            }, ModelTools(properties.Keys));
            return new(reply, source, KeepDraft: true, Proposal: proposal);
        }
        catch (Exception ex) when (!token.IsCancellationRequested &&
            ex is JSException or OperationCanceledException or HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return GuardianTurnResult.Retry("A IA local não conseguiu propor uma edição válida. Nenhuma alteração foi aplicada.", source);
        }
    }

    private static GardenModelProposal? ReadModelProposal(string output, IReadOnlyDictionary<string, GardenProperty> properties)
    {
        using var document = JsonDocument.Parse(output);
        var calls = document.RootElement.GetProperty("calls");
        if (calls.GetArrayLength() == 0) return null;
        if (calls.GetArrayLength() != 1 || calls[0].GetProperty("type").GetString() != "function")
            throw new JsonException("Expected one garden tool call.");
        var function = calls[0].GetProperty("function");
        if (function.GetProperty("name").GetString() != CustomizeModelTool)
            throw new JsonException("The requested garden tool is unavailable.");
        using var parsed = JsonDocument.Parse(function.GetProperty("arguments").GetString() ?? throw new JsonException("Missing arguments."));
        var args = parsed.RootElement;
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
            using var request = JsonContent.Create(new { name = CustomizeModelTool, arguments = proposal.Arguments });
            var result = await SendAsync<JsonElement>(endpoint, HttpMethod.Post, GardenRoute + "/tools", request, 12, token);
            return await GardenReplyAsync(result, ReadReply(result) ?? "O jardim não confirmou o ajuste.", source, token);
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

internal sealed record GardenModelProposal(JsonElement Arguments, string Preview);
