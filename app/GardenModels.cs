using System.Text.Json.Nodes;

namespace Ultima.Server;

sealed partial class GameWorld
{
    const string LanternAsset = "prop.lantern";
    static readonly ModelParameter PoleHeight = new(0.5, 5, 1.4);
    static readonly ModelParameter LightIntensity = new(0, 3, 1.05);

    readonly record struct ModelParameter(double Min, double Max, double Default)
    {
        public JsonObject Describe(string unit) => new()
        { ["min"] = Min, ["max"] = Max, ["default"] = Default, ["unit"] = unit };

        public double Current(JsonObject? parameters, string key) =>
            J.TryFloat(parameters?[key], out var value) ? Math.Clamp(value, Min, Max) : Default;
    }

    static JsonObject? ReadGardenParameters(string asset, JsonNode? input, JsonObject? existing)
    {
        if (asset != LanternAsset || input is not JsonObject { Count: > 0 } values) return null;
        var result = existing?.DeepClone() as JsonObject ?? new JsonObject();
        foreach (var (key, node) in values)
        {
            var definition = key switch
            {
                "poleHeight" => PoleHeight,
                "lightIntensity" => LightIntensity,
                _ => (ModelParameter?)null
            };
            if (definition is not { } bounds || !J.TryFloat(node, out var value) ||
                value < bounds.Min || value > bounds.Max) return null;
            result[key] = J.R(value, 2);
        }
        return result;
    }

    // Chamado sob o mesmo lock do diálogo. Seleção, leitura de parâmetros e gravação
    // usam a mesma revisão, inclusive quando dois jogadores fazem o pedido juntos.
    (int, JsonObject) AdjustNearbyLanterns(string? token, double x, double z)
    {
        var nearby = _placements.Where(p => p.AssetId == LanternAsset && InGarden(p.X, p.Z))
            .OrderBy(p => double.Hypot(p.X - x, p.Z - z)).Take(2);
        var actions = new JsonArray();
        foreach (var lamp in nearby)
        {
            var parameters = new JsonObject();
            var height = PoleHeight.Current(lamp.Parameters, "poleHeight");
            var intensity = LightIntensity.Current(lamp.Parameters, "lightIntensity");
            if (height < PoleHeight.Max) parameters["poleHeight"] = Math.Min(PoleHeight.Max, height + 0.6);
            if (intensity < LightIntensity.Max)
                parameters["lightIntensity"] = Math.Min(LightIntensity.Max, Math.Max(0.25, intensity * 1.25));
            if (parameters.Count > 0)
                actions.Add(new JsonObject { ["action"] = "customize", ["uid"] = lamp.Uid, ["parameters"] = parameters });
        }

        if (actions.Count == 0)
            return (200, new JsonObject { ["applied"] = 0, ["source"] = "garden",
                ["reply"] = nearby.Any() ? "As luminárias próximas já estão no limite de altura e intensidade."
                    : "Ainda não há luminárias instaladas no jardim." });

        var (status, result) = GardenEdit(token, actions, requireGuardian: true);
        if (status == 200)
        {
            result["source"] = "garden";
            result["reply"] = "Ajustei a altura das hastes e a intensidade da luz das luminárias próximas.";
        }
        return (status, result);
    }
}
