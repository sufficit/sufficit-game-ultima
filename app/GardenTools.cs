using System.Text.Json.Nodes;
using Ultima.Garden;

namespace Ultima.Server;

sealed partial class GameWorld
{
    // Scope and current dimensions come from the authoritative world under the edit lock.
    internal (int, JsonObject) GardenTool(string? token, JsonObject data)
    {
        lock (_lock)
        {
            var character = Session(token);
            if (character is null) return (401, J.Err("invalid_token"));
            if (Dist(character, NpcX, NpcZ) > TalkRadius) return (409, J.Err("guardian_out_of_reach"));
            if (J.Str(data["name"]) != "adjust_lantern_height") return (400, J.Err("unknown_garden_tool"));
            if (data["arguments"] is not JsonObject { Count: 1 } arguments ||
                arguments["delta_m"] is not JsonValue number || !number.TryGetValue<double>(out var delta) ||
                !double.IsFinite(delta) || Math.Abs(delta) is < 0.01 or > 4.5 || Math.Round(delta, 2) != delta)
                return (400, J.Err("invalid_tool_arguments"));

            return AdjustNearbyLanterns(token, character.X, character.Z, LanternAdjustment.Height, delta);
        }
    }
}
