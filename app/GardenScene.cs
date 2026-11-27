using System.Text.Json.Nodes;

namespace Ultima.Server;

sealed partial class GameWorld
{
    private const string InspectGardenSceneTool = "inspect_scene";

    // Coordinates and distance are in world metres. Yaw is in radians.
    private (int, JsonObject) InspectGardenScene(Character player, JsonObject arguments)
    {
        if (arguments.Any(pair => pair.Key is not ("x" or "z" or "radius" or "asset")) ||
            arguments.ContainsKey("x") != arguments.ContainsKey("z"))
            return (400, J.Err("invalid_tool_arguments"));
        var (x, z) = (player.X, player.Z);
        if (arguments.ContainsKey("x") &&
            (!ReadCoordinate("x", out x) || !ReadCoordinate("z", out z) || !InGarden(x, z)))
            return (400, J.Err("invalid_scene_center"));

        var radius = 18d;
        if (arguments.ContainsKey("radius") && (!ReadCoordinate("radius", out radius) || radius is <= 0 or > GardenRadius))
            return (400, J.Err("invalid_scene_radius"));
        var asset = J.Str(arguments["asset"]);
        if (arguments.ContainsKey("asset") && (asset.Length == 0 || !AssetAllowed(asset)))
            return (400, J.Err("invalid_scene_model"));

        var matches = _placements.Where(p => InGarden(p.X, p.Z) && (asset.Length == 0 || p.AssetId == asset))
            .Select(p => (Placement: p, Distance: double.Hypot(p.X - x, p.Z - z)))
            .Where(item => item.Distance <= radius)
            .OrderBy(item => item.Distance).ThenBy(item => item.Placement.Uid, StringComparer.Ordinal)
            .Take(9).ToArray();
        var models = new JsonObject();
        var instances = new JsonArray();
        foreach (var (item, distance) in matches.Take(8))
        {
            var model = PlacementContract(item.AssetId);
            if (!models.ContainsKey(item.AssetId)) models[item.AssetId] = DescribeModel(model);
            var overrides = ModelValues(item.Parameters);
            var values = new JsonObject();
            if (overrides is not null)
                foreach (var parameter in model.Parameters)
                    values[parameter.Key] = model.Read(parameter, overrides, item.Scale, item.Rot);
            instances.Add(new JsonObject
            {
                ["uid"] = item.Uid, ["asset"] = item.AssetId, ["x"] = item.X, ["z"] = item.Z,
                ["distance"] = J.R(distance, 2), ["parameters"] = values,
            });
        }
        return (200, new JsonObject
        {
            ["player"] = new JsonObject { ["x"] = player.X, ["z"] = player.Z, ["yaw"] = player.Yaw },
            ["center"] = new JsonObject { ["x"] = x, ["z"] = z, ["radius"] = radius },
            ["scope"] = new JsonObject { ["x"] = NpcX, ["z"] = NpcZ, ["radius"] = GardenRadius },
            ["revision"] = _revision, ["instances"] = instances, ["models"] = models,
            ["more"] = matches.Length > instances.Count,
        });

        bool ReadCoordinate(string key, out double value)
        {
            value = 0;
            return arguments[key] is JsonValue number && number.TryGetValue(out value) && double.IsFinite(value);
        }
    }
}
