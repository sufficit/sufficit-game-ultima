using System.Text.Json.Nodes;
using Ultima.Garden;
using Ultima.Models.Core;

namespace Ultima.Server;

sealed partial class GameWorld
{
    static readonly IEditablePlacementModel DefaultPlacementModel = new PlacementModel();
    static readonly Dictionary<string, IEditablePlacementModel> PlacementModels = new(StringComparer.Ordinal)
    {
        [LanternPlacementModel.AssetId] = new LanternPlacementModel()
    };

    static JsonObject DescribeModel(IEditablePlacementModel model)
    {
        var parameters = new JsonObject();
        foreach (var parameter in model.Parameters)
        {
            var definition = new JsonObject { ["label"] = parameter.Label, ["unit"] = parameter.Unit,
                ["min"] = parameter.Min, ["max"] = parameter.Max, ["default"] = parameter.Default };
            if (parameter.Inherits is not null) definition["inherits"] = parameter.Inherits;
            parameters[parameter.Key] = definition;
        }
        return new JsonObject { ["parameters"] = parameters };
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

    static JsonObject? ReadGardenParameters(string asset, JsonNode? input, JsonObject? existing)
    {
        if (input is not JsonObject { Count: > 0 } parameters ||
            ModelValues(parameters) is not { } patch || ModelValues(existing) is not { } current)
            return null;
        var model = PlacementModels.GetValueOrDefault(asset, DefaultPlacementModel);
        if (model.Apply(current, patch) is not { } next) return null;
        var result = new JsonObject();
        foreach (var (key, value) in next) result[key] = value;
        return result;
    }

    // The dialogue lock keeps selection, current values and writes on the same world revision.
    (int, JsonObject) AdjustNearbyLanterns(string? token, double x, double z, LanternAdjustment adjustment)
    {
        var nearby = _placements.Where(p => p.AssetId == LanternPlacementModel.AssetId && InGarden(p.X, p.Z))
            .OrderBy(p => double.Hypot(p.X - x, p.Z - z)).Take(2).ToArray();
        var actions = new JsonArray();
        foreach (var lamp in nearby)
        {
            var parameters = new JsonObject();
            var current = ModelValues(lamp.Parameters) ?? [];
            var pole = LanternPlacementModel.PoleHeight;
            var light = LanternPlacementModel.LightIntensity;
            var height = pole.Read(current);
            var intensity = light.Read(current);
            if (adjustment.HasFlag(LanternAdjustment.Height) && height < pole.Max) parameters["poleHeight"] = Math.Min(pole.Max, height + 0.6);
            if (adjustment.HasFlag(LanternAdjustment.Intensity) && intensity < light.Max)
                parameters["lightIntensity"] = Math.Min(light.Max, Math.Max(0.25, intensity * 1.25));
            if (parameters.Count > 0)
                actions.Add(new JsonObject { ["action"] = "customize", ["uid"] = lamp.Uid, ["parameters"] = parameters });
        }

        if (actions.Count == 0)
            return (200, new JsonObject { ["applied"] = 0, ["source"] = "garden",
                ["reply"] = nearby.Length == 0 ? "Ainda não há luminárias instaladas no jardim."
                    : "As luminárias próximas já estão no limite dos parâmetros solicitados." });

        var (status, result) = GardenEdit(token, actions, requireGuardian: true);
        if (status == 200)
        {
            result["source"] = "garden";
            result["reply"] = adjustment switch
            {
                LanternAdjustment.Height => "Aumentei a altura das hastes das luminárias próximas.",
                LanternAdjustment.Intensity => "Aumentei a intensidade da luz das luminárias próximas.",
                _ => "Ajustei a altura das hastes e a intensidade da luz das luminárias próximas."
            };
        }
        return (status, result);
    }
}
