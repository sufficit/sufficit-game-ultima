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
        string legacyUuid;
        var browserUuid = Guid.NewGuid().ToString("D");
        using (var server = await harness.Boot(data))
        {
            var player = await JoinAsync(server, "Alex");
            var other = await JoinAsync(server, "Alex");
            key = Str(player["profileKey"]);
            legacyUuid = Str(player["playerUuid"]);
            Check(Guid.TryParseExact(legacyUuid, "D", out var legacyId) && legacyId.Version == 4 &&
                legacyUuid != Str(other["playerUuid"]), "Independent profiles have distinct UUIDs.");
            var browser = await JoinAsync(server, "Alex", playerUuid: browserUuid);
            var resumedBrowser = await JoinAsync(server, "Alex", Str(browser["profileKey"]), browserUuid);
            Check(Str(browser["playerUuid"]) == browserUuid && Same(browser["playerUuid"], resumedBrowser["playerUuid"]) &&
                !Same(browser["token"], resumedBrowser["token"]) &&
                !Same(browser["character"]?["id"], resumedBrowser["character"]?["id"]),
                "The player UUID survives a fresh session with its own token and transport ID.");
            var state = await harness.Json(server.Url, "/v1/game/state", token: Str(browser["token"]));
            Check(Str(state["playerUuid"]) == browserUuid && state["me"]?["playerUuid"] is null,
                "The UUID is returned only in the caller's session state.");
            await harness.Json(server.Url, Sessions, new JsonObject { ["name"] = "Alex", ["playerUuid"] = "bad" }, expect: 400);
            var token = Str(player["token"]);
            var otherToken = Str(other["token"]);
            Check(key.Length == 64 && key != Str(other["profileKey"]), "Session names do not share character credentials");
            await harness.Json(server.Url, Body, token: null, expect: 401);
            await harness.Json(server.Url, Body, new JsonObject { ["build"] = 1.1 }, expect: 401);

            var first = await harness.Json(server.Url, Body,
                new JsonObject { ["height"] = 1.8, ["mass"] = 90, ["femurRatio"] = .56 }, token);
            var update = new JsonObject { ["waistCircumference"] = 1.1, ["thighScale"] = 1.3, ["shoulderHeightRatio"] = .83 };
            var result = await harness.Json(server.Url, Body, update, token);
            var profile = Obj(result["profile"]);
            Check(Num(Obj(first["profile"])["height"]) == 1.8 &&
                Num(profile["mass"]) == 90 && Num(profile["femurRatio"]) == .56 &&
                Num(profile["waistCircumference"]) == 1.1 && Num(profile["thighScale"]) == 1.3 &&
                Num(profile["shoulderHeightRatio"]) == .83,
                "Partial edits preserve other measurements", result);
            Check(Same(result, await harness.Json(server.Url, Body, update, token)), "Identical edits are idempotent");

            foreach (var invalid in new[] {
                new JsonObject { ["height"] = 4 }, new JsonObject { ["shoulderHeightRatio"] = .85 }, new JsonObject { ["profileKey"] = key },
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
            Check(Str(resumed["playerUuid"]) == legacyUuid, "Legacy profile UUID survives a process restart.");
            var profile = Obj((await harness.Json(server.Url, Body, token: token))["profile"]);
            Check(Str(resumed["profileKey"]) == key && Num(profile["waistCircumference"]) == 1.1 &&
                Num(profile["shoulderHeightRatio"]) == .83,
                "The owner can resume a saved character after a process restart", profile);
            var result = await harness.Json(server.Url, Body, new JsonObject { ["waistCircumference"] = null }, token);
            profile = Obj(result["profile"]);
            Check(profile["waistCircumference"] is null && Num(profile["thighScale"]) == 1.3, "Null resets only one override");
        }

        Task<JsonObject> JoinAsync(Server server, string name, string? profileKey = null, string? playerUuid = null) =>
            harness.Json(server.Url, Sessions, new JsonObject
            { ["name"] = name, ["profileKey"] = profileKey, ["playerUuid"] = playerUuid });
    }
}
