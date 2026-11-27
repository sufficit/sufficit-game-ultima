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
    }
}
