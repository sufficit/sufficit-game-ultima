using System.Text.Json.Nodes;

namespace Ultima.Server;

sealed partial class GameWorld
{
    internal const string CustomizeModelTool = "customize_model";
    private const int DefaultToolTargets = 2;

    internal (int, JsonObject) GardenToolContext(string? token)
    {
        lock (_lock)
        {
            var error = GardenToolAccess(token);
            if (error is { } denied) return denied;
            var character = Session(token)!;
            var models = new JsonObject();
            var assets = _placements.Where(p => InGarden(p.X, p.Z))
                .OrderBy(p => double.Hypot(p.X - character.X, p.Z - character.Z))
                .ThenBy(p => p.AssetId, StringComparer.Ordinal)
                .Select(p => p.AssetId).Distinct().Take(12);
            foreach (var asset in assets)
                models[asset] = DescribeModel(PlacementContract(asset));
            return (200, new JsonObject { ["models"] = models, ["sceneInspectionSupported"] = true });
        }
    }

    // Resolve the current value, select placements, and commit through the ordinary editor under its lock.
    internal (int, JsonObject) GardenTool(string? token, JsonObject data)
    {
        lock (_lock)
        {
            var error = GardenToolAccess(token);
            if (error is { } denied) return denied;
            var character = Session(token)!;
            if (data["arguments"] is not JsonObject arguments) return (400, J.Err("invalid_tool_arguments"));
            var name = J.Str(data["name"]);
            if (name == InspectGardenSceneTool) return InspectGardenScene(character, arguments);
            if (name != CustomizeModelTool) return (400, J.Err("unknown_garden_tool"));
            if (arguments.Any(entry => entry.Key is not ("asset" or "parameter" or "operation" or "value" or "count" or "uid")))
                return (400, J.Err("invalid_tool_arguments"));

            var asset = J.Str(arguments["asset"]);
            var key = J.Str(arguments["parameter"]);
            var operation = J.Str(arguments["operation"]);
            var model = PlacementContract(asset);
            var parameter = model.Parameters.FirstOrDefault(item => item.Key == key);
            if (!AssetAllowed(asset) || parameter is null || operation is not ("set" or "add") ||
                (operation == "add" && parameter.Unit == "sRGB24") ||
                !ReadModelValue(parameter, arguments["value"], out var value) || !double.IsFinite(value))
                return (400, J.Err("invalid_tool_arguments"));

            var count = DefaultToolTargets;
            if (arguments.ContainsKey("count") &&
                (arguments["count"] is not JsonValue number || !number.TryGetValue<int>(out count) || count is < 1 or > GardenActions))
                return (400, J.Err("invalid_tool_arguments"));

            var uid = J.Str(arguments["uid"]);
            if (arguments.ContainsKey("uid") && (uid.Length == 0 || arguments.ContainsKey("count")))
                return (400, J.Err("invalid_tool_arguments"));
            var actions = new JsonArray();
            var targets = _placements.Where(p => p.AssetId == asset && InGarden(p.X, p.Z) && (uid.Length == 0 || p.Uid == uid))
                .OrderBy(p => double.Hypot(p.X - character.X, p.Z - character.Z))
                .ThenBy(p => p.Uid, StringComparer.Ordinal).Take(count);
            foreach (var placement in targets)
            {
                if (ModelValues(placement.Parameters) is not { } current)
                    return (409, J.Err("invalid_existing_parameters"));
                var previous = model.Read(parameter, current, placement.Scale, placement.Rot);
                var next = operation == "add" ? previous + value : value;
                if (model.Apply(current, new Dictionary<string, double> { [key] = next }) is not { } applied)
                    return (409, J.Err("model_parameter_out_of_range"));
                if (applied[key] == previous) continue;
                actions.Add(new JsonObject { ["action"] = "customize", ["uid"] = placement.Uid,
                    ["parameters"] = new JsonObject { [key] = applied[key] } });
            }

            if (uid.Length > 0 && !targets.Any()) return (404, J.Err("invalid_placement"));
            if (actions.Count == 0)
                return (200, new JsonObject { ["applied"] = 0, ["reply"] = "Nenhuma instância próxima precisou ser alterada." });
            var (status, result) = GardenEdit(token, actions, requireGuardian: true);
            if (status == 200) result["reply"] = $"Atualizei a propriedade: {parameter.Label.ToLowerInvariant()} em {actions.Count} instância(s).";
            return (status, result);
        }
    }

    private (int, JsonObject)? GardenToolAccess(string? token)
    {
        var character = Session(token);
        if (character is null) return (401, J.Err("invalid_token"));
        return Dist(character, NpcX, NpcZ) > TalkRadius ? (409, J.Err("guardian_out_of_reach")) : null;
    }
}
