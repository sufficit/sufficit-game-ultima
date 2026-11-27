using System.Text.Json.Nodes;
using static Kit;

internal static class GardenModelIntegration
{
    private const string Tools = "/v1/game/garden/tools";

    public static Task<JsonObject> UpdateAsync(Harness harness, Uri url, string? token,
        string asset, string parameter, string operation, JsonNode? value, int expected = 200) =>
        harness.Json(url, Tools, new JsonObject {
            ["name"] = "customize_model",
            ["arguments"] = new JsonObject { ["asset"] = asset, ["parameter"] = parameter,
                ["operation"] = operation, ["value"] = value }
        }, token, expected);

    public static async Task VerifyAsync(Harness harness, Uri url, string token, JsonObject[] lamps)
    {
        await UpdateAsync(harness, url, null, "prop.lantern", "poleHeight", "add", 1, 401);
        var taller = await UpdateAsync(harness, url, token, "prop.lantern", "poleHeight", "add", 1);
        Check(Num(taller["applied"]) == 2, "The tool updates both placements.", taller);
        foreach (var lamp in lamps)
        {
            var updated = Placements(taller).Single(item => Same(item["uid"], lamp["uid"]));
            var expected = (JsonObject)lamp["parameters"]!.DeepClone();
            expected["poleHeight"] = Num(expected["poleHeight"]) + 1;
            Check(Same(updated["parameters"], expected) && Same(updated["x"], lamp["x"]) && Same(updated["z"], lamp["z"]),
                "Height changes without moving the lamp or changing its light.", updated);
        }

        var rejected = await UpdateAsync(harness, url, token, "prop.lantern", "poleHeight", "add", 1.5, 409);
        Check(Str(rejected["error"]) == "model_parameter_out_of_range" &&
            Same((await harness.Json(url, "/v1/game/map"))["placements"], Obj(taller["map"])["placements"]),
            "One target beyond its range rejects the whole batch.", rejected);

        await UpdateAsync(harness, url, token, "prop.lantern", "lightLumens", "set", 850);
        var colored = await UpdateAsync(harness, url, token, "prop.lantern", "lightRgb", "set", "#2355FF");
        Check(Placements(colored).Where(p => Str(p["asset"]) == "prop.lantern").All(p =>
            Num(p["parameters"]?["lightLumens"]) == 850 && Num(p["parameters"]?["lightRgb"]) == 0x2355ff &&
            p["parameters"]?["lightIntensity"] is null), "Distinct light properties are persisted.", colored);

        await harness.Json(url, "/v1/game/garden/plants", new JsonObject
        {
            ["actions"] = new JsonArray(
                new JsonObject { ["action"] = "place", ["asset"] = "flora.pine", ["x"] = 3, ["z"] = -9, ["scale"] = 2 },
                new JsonObject { ["action"] = "place", ["asset"] = "flora.flowers", ["x"] = 5, ["z"] = -9 })
        }, token);
        var discovery = Obj((await harness.Json(url, Tools, token: token))["models"]);
        Check(Str(discovery["flora.pine"]?["parameters"]?["heightM"]?["unit"]) == "m" &&
            Str(discovery["flora.flowers"]?["parameters"]?["tintRgb"]?["unit"]) == "sRGB24",
            "Placed models expose their declared properties.", discovery);
        var tree = await UpdateAsync(harness, url, token, "flora.pine", "heightM", "add", 1);
        Check(Num(Placements(tree).Single(p => Str(p["asset"]) == "flora.pine")["parameters"]?["heightM"]) == 15,
            "Relative height uses the existing placement scale.", tree);
        var flowers = await UpdateAsync(harness, url, token, "flora.flowers", "tintRgb", "set", "#A141FF");
        Check(Num(Placements(flowers).Single(p => Str(p["asset"]) == "flora.flowers")["parameters"]?["tintRgb"]) == 0xa141ff,
            "The same operation tints flowers.", flowers);
        await UpdateAsync(harness, url, token, "flora.flowers", "lightLumens", "set", 100, 400);
        await UpdateAsync(harness, url, token, "flora.flowers", "tintRgb", "add", 1, 400);
    }

    private static IEnumerable<JsonObject> Placements(JsonObject reply) => Arr(Obj(reply["map"])["placements"]).Select(Obj);
}
