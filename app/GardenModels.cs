using System.Text.Json.Nodes;
using System.Globalization;
using Ultima.Models.Core;

namespace Ultima.Server;

sealed partial class GameWorld
{
    static readonly IEditablePlacementModel DefaultPlacementModel = new PlacementModel();
    static readonly Dictionary<string, IEditablePlacementModel> PlacementModels = new(StringComparer.Ordinal)
    {
        [LanternPlacementModel.AssetId] = new LanternPlacementModel(),
        ["flora.pine"] = new VegetationPlacementModel("Pinheiro", 7, 56),
        ["flora.flowers"] = new VegetationPlacementModel("Flores", null, 0)
    };

    static JsonObject DescribeModel(IEditablePlacementModel model)
    {
        var parameters = new JsonObject();
        foreach (var parameter in model.Parameters)
        {
            var definition = new JsonObject { ["label"] = parameter.Label, ["unit"] = parameter.Unit,
                ["min"] = parameter.Min, ["max"] = parameter.Max, ["default"] = parameter.Default };
            if (parameter.Integral) definition["integer"] = true;
            if (parameter.Inherits is not null) definition["inherits"] = parameter.Inherits;
            parameters[parameter.Key] = definition;
        }
        return new JsonObject { ["label"] = model.DisplayName, ["parameters"] = parameters };
    }

    static JsonObject ModelCatalog()
    {
        var result = new JsonObject { ["*"] = DescribeModel(DefaultPlacementModel) };
        foreach (var (asset, model) in PlacementModels) result[asset] = DescribeModel(model);
        return result;
    }

    static Dictionary<string, double>? ModelValues(JsonObject? values)
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        if (values is not null)
            foreach (var (key, node) in values)
            {
                if (!J.TryFloat(node, out var value)) return null;
                result[key] = value;
            }
        return result;
    }

    static IEditablePlacementModel PlacementContract(string asset) => PlacementModels.GetValueOrDefault(asset, DefaultPlacementModel);

    static bool ReadModelValue(PlacementParameter parameter, JsonNode? value, out double result)
    {
        if (parameter.Unit == "sRGB24" && value is JsonValue color && color.TryGetValue<string>(out var text) &&
            text.Length == 7 && text[0] == '#' && int.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture, out var rgb))
        {
            result = rgb;
            return true;
        }
        return J.TryFloat(value, out result);
    }

    static JsonObject? ReadGardenParameters(string asset, JsonNode? input, JsonObject? existing)
    {
        if (input is not JsonObject { Count: > 0 } parameters || ModelValues(existing) is not { } current)
            return null;
        var model = PlacementContract(asset);
        var patch = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (key, node) in parameters)
        {
            var parameter = model.Parameters.FirstOrDefault(item => item.Key == key);
            if (parameter is null || !ReadModelValue(parameter, node, out var value)) return null;
            patch.Add(key, value);
        }
        if (model.Apply(current, patch) is not { } next) return null;
        var result = new JsonObject();
        foreach (var (key, value) in next) result[key] = value;
        return result;
    }
}
