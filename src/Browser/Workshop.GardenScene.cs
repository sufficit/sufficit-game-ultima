using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ultima.Browser;

public sealed partial class Workshop
{
    private const string InspectSceneTool = "inspect_scene";
    private const string InspectSceneSchema = """
        {"type":"function","function":{"name":"inspect_scene",
        "description":"Read the player pose and nearby placements, with model IDs, instance UIDs, coordinates and current parameters. This does not change anything.",
        "parameters":{"type":"object","properties":{
          "x":{"type":"number","description":"Optional world X in metres; supply together with z"},
          "z":{"type":"number","description":"Optional world Z in metres; defaults to the player with x"},
          "radius":{"type":"number","minimum":0.1,"maximum":40,"description":"Search radius in metres"},
          "asset":{"type":"string","description":"Optional exact catalog model ID"}
        },"additionalProperties":false}}}
        """;

    private static string BuildGardenTools(Dictionary<string, GardenProperty> properties, bool allowPlan, bool inspect)
    {
        var tools = properties.Count == 0 ? new JsonArray() : JsonNode.Parse(ModelTools(properties))!.AsArray();
        if (inspect) tools.Add(JsonNode.Parse(InspectSceneSchema));
        if (allowPlan) tools.Add(JsonNode.Parse(GardenPlanTool));
        return tools.ToJsonString();
    }

    private static string PropertyInstructions(IEnumerable<GardenProperty> properties) =>
        "\nEditable properties:\n" + string.Join('\n',
            properties.Select(p => $"{p.Id}: {p.Model}, {p.Label} ({p.Unit})"));

    private static void AddProperties(JsonElement catalog, HashSet<string> units, Dictionary<string, GardenProperty> properties)
    {
        foreach (var asset in catalog.EnumerateObject())
        foreach (var parameter in asset.Value.GetProperty("parameters").EnumerateObject())
        {
            var model = asset.Value.GetProperty("label").GetString() ?? asset.Name;
            var label = parameter.Value.GetProperty("label").GetString();
            var unit = parameter.Value.GetProperty("unit").GetString();
            if (units.Count != 0 && (unit is null || !units.Contains(unit))) continue;
            var property = new GardenProperty(asset.Name, parameter.Name, model, label, unit);
            properties[property.Id] = property;
        }
    }

    private static bool TryReadSceneRequest(string output, out JsonElement arguments)
    {
        using var json = JsonDocument.Parse(output);
        var calls = json.RootElement.GetProperty("calls");
        arguments = default;
        if (calls.GetArrayLength() != 1 || calls[0].GetProperty("type").GetString() != "function")
            return false;
        var function = calls[0].GetProperty("function");
        if (function.GetProperty("name").GetString() != InspectSceneTool) return false;
        using var request = JsonDocument.Parse(function.GetProperty("arguments").GetString() ?? "");
        if (request.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Invalid scene arguments.");
        arguments = request.RootElement.Clone();
        return true;
    }
}
