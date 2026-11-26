using System.Text.Json.Nodes;
using static Kit;

internal static class CharacterProfileIntegration
{
    private const string Body = "/v1/game/characters/me/body";
    private const string Sessions = "/v1/game/sessions";

    internal static async Task RunAsync(Harness harness)
    {
        var data = harness.TempDir();
        string key;
        using (var server = await harness.Boot(data))
        {
            var player = await JoinAsync(server, "Alex");
            var other = await JoinAsync(server, "Alex");
            key = Str(player["profileKey"]);
            var token = Str(player["token"]);
            var otherToken = Str(other["token"]);
            Check(key.Length == 64 && key != Str(other["profileKey"]), "Session names do not share character credentials");
            await harness.Json(server.Url, Body, token: null, expect: 401);
            await harness.Json(server.Url, Body, new JsonObject { ["build"] = 1.1 }, expect: 401);

            var first = await harness.Json(server.Url, Body,
                new JsonObject { ["height"] = 1.8, ["mass"] = 90, ["femurRatio"] = .56 }, token);
            var update = new JsonObject { ["waistCircumference"] = 1.1, ["thighScale"] = 1.3 };
            var result = await harness.Json(server.Url, Body, update, token);
            var profile = Obj(result["profile"]);
            Check(Num(Obj(first["profile"])["height"]) == 1.8 &&
                Num(profile["mass"]) == 90 && Num(profile["femurRatio"]) == .56 &&
                Num(profile["waistCircumference"]) == 1.1 && Num(profile["thighScale"]) == 1.3,
                "Partial edits preserve other measurements", result);
            Check(Same(result, await harness.Json(server.Url, Body, update, token)), "Identical edits are idempotent");

            foreach (var invalid in new[] {
                new JsonObject { ["height"] = 4 }, new JsonObject { ["profileKey"] = key },
                new JsonObject { ["skin"] = new JsonObject { ["red"] = .4 } } })
                await harness.Json(server.Url, Body, invalid, token, 400);
            Check(Same(result, await harness.Json(server.Url, Body, token: token)), "Invalid edits leave the profile intact");
            Check((await harness.Json(server.Url, Body, token: otherToken))["profile"] is null,
                "Another session cannot read the player's profile by name");
        }

        using (var server = await harness.Boot(data))
        {
            var resumed = await JoinAsync(server, "Alex", key);
            var token = Str(resumed["token"]);
            var profile = Obj((await harness.Json(server.Url, Body, token: token))["profile"]);
            Check(Str(resumed["profileKey"]) == key && Num(profile["waistCircumference"]) == 1.1,
                "The owner can resume a saved character after a process restart", profile);
            var result = await harness.Json(server.Url, Body, new JsonObject { ["waistCircumference"] = null }, token);
            profile = Obj(result["profile"]);
            Check(profile["waistCircumference"] is null && Num(profile["thighScale"]) == 1.3, "Null resets only one override");
        }

        Task<JsonObject> JoinAsync(Server server, string name, string? profileKey = null) =>
            harness.Json(server.Url, Sessions, new JsonObject { ["name"] = name, ["profileKey"] = profileKey });
    }
}
