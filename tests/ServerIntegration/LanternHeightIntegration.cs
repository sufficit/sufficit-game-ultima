using System.Text.Json.Nodes;
using static Kit;

internal static class LanternHeightIntegration
{
    public static async Task VerifyAsync(Harness harness, Uri url, string token, JsonObject[] lamps)
    {
        var resized = await harness.Json(url, "/v1/game/garden/dialog",
            new JsonObject { ["message"] = "aumente a altura dessas luminarias" }, token);
        Check(Str(resized["source"]) == "garden" && Num(resized["applied"]) == lamps.Length,
            "The exact height-only request uses the garden editor.", resized);

        var placements = Arr(Obj(resized["map"])["placements"]).Select(Obj).ToArray();
        var brighter = await harness.Json(url, "/v1/game/garden/dialog",
            new JsonObject { ["message"] = "Aumente a intensidade da luz dessas luminárias." }, token);
        Check(Str(brighter["source"]) == "garden" && Num(brighter["applied"]) == lamps.Length,
            "The intensity-only request uses the garden editor.", brighter);
        var brighterPlacements = Arr(Obj(brighter["map"])["placements"]).Select(Obj).ToArray();
        foreach (var lamp in lamps)
        {
            var updated = placements.Single(placement => Same(placement["uid"], lamp["uid"]));
            var expected = (JsonObject)lamp["parameters"]!.DeepClone();
            expected["poleHeight"] = Num(expected["poleHeight"]) + 0.6;
            Check(Same(updated["parameters"], expected) && Same(updated["x"], lamp["x"]) && Same(updated["z"], lamp["z"]),
                "Height increases without moving the lamp or changing its intensity.", updated);

            var bright = brighterPlacements.Single(placement => Same(placement["uid"], lamp["uid"]));
            expected["lightIntensity"] = Math.Round(Num(expected["lightIntensity"]) * 1.25, 2);
            Check(Same(bright["parameters"], expected), "Intensity increases without changing height.", bright);
        }

        const string tools = "/v1/game/garden/tools";
        var tool = new JsonObject { ["name"] = "adjust_lantern_height", ["arguments"] = new JsonObject { ["delta_m"] = 1 } };
        await harness.Json(url, tools, tool, expect: 401);
        var applied = await harness.Json(url, tools, tool, token);
        Check(Num(applied["applied"]) == 2, "Tool call applies to two nearby fixtures.", applied);
        var adjusted = Arr(Obj(applied["map"])["placements"]).Select(Obj);
        foreach (var lamp in brighterPlacements.Where(candidate => lamps.Any(lamp => Same(lamp["uid"], candidate["uid"]))))
        {
            var expected = (JsonObject)lamp["parameters"]!.DeepClone();
            expected["poleHeight"] = Math.Round(Num(expected["poleHeight"]) + 1, 2);
            Check(Same(adjusted.Single(candidate => Same(candidate["uid"], lamp["uid"]))["parameters"], expected),
                "The tool changes height by exactly one metre.", applied);
        }

        tool["arguments"]!["delta_m"] = 30;
        await harness.Json(url, tools, tool, token, 400);
        tool["arguments"]!["delta_m"] = 1;
        await harness.Json(url, tools, tool, token, 409);
        var afterRejected = await harness.Json(url, "/v1/game/map");
        Check(Same(afterRejected["placements"], Obj(applied["map"])["placements"]),
            "Invalid or out-of-range edits leave all fixtures unchanged.");
    }
}
