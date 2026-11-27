// Testes de integração do servidor Ultima (ASP.NET Core): sobem o build Release em um
// Uso: dotnet run --project tests/ServerIntegration -c Release -- [world] [assets] [garden] [diagnostics]
// O servidor precisa estar compilado: dotnet build app/UltimaServer.csproj -c Release
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static Kit;

var wanted = args.ToHashSet(StringComparer.OrdinalIgnoreCase);
string[] scenarios = ["world", "assets", "garden", "diagnostics", "office", "character-profile", "guardian-telemetry"];
if (wanted.Any(name => !scenarios.Contains(name)))
{
    Console.Error.WriteLine($"Cenários disponíveis: {string.Join(", ", scenarios)}.");
    return 2;
}

var root = AppContext.BaseDirectory;
while (root is not null && !File.Exists(Path.Combine(root, "app/UltimaServer.csproj")))
    root = Path.GetDirectoryName(root);
var server = Path.Combine(root ?? "", "app/bin/Release/net10.0/UltimaServer.dll");
if (!File.Exists(server))
{
    Console.Error.WriteLine("Compile o servidor antes: dotnet build app/UltimaServer.csproj -c Release");
    return 2;
}

using var harness = new Harness(server);
try
{
    foreach (var scenario in scenarios.Where(name => wanted.Count == 0 || wanted.Contains(name)))
    {
        var clock = Stopwatch.StartNew();
        switch (scenario)
        {
            case "world": await World(harness, root!); break;
            case "assets": await Assets(harness, root!); break;
            case "garden": await Garden(harness, root!); break;
            case "character-profile": await CharacterProfileIntegration.RunAsync(harness); break;
            case "office": await OfficeIntegration.RunAsync(harness); break;
            case "guardian-telemetry": await GuardianTelemetryIntegration.RunAsync(harness); break;
            default: await Diagnostics(harness, root!); break;
        }
        Console.WriteLine($"OK {scenario} ({clock.ElapsedMilliseconds} ms)");
    }
    return 0;
}
catch (Failure failure)
{
    Console.Error.WriteLine("FALHOU: " + failure.Message);
    return 1;
}
catch (Exception error)
{
    Console.Error.WriteLine("FALHOU (exceção): " + error.Message);
    return 1;
}

// ---------- cenário: persistência do mundo (migração legada, undo, replay) ----------

async Task World(Harness h, string root)
{
    var dir = h.TempDir();
    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
    var legacy = new JsonObject
    {
        ["revision"] = 17,
        ["tiles"] = new JsonObject
        {
            ["1,1"] = new JsonObject { ["type"] = "rock", ["by"] = "legado", ["at"] = now, ["rev"] = 17 },
        },
        ["jobs"] = new JsonArray(new JsonObject
        {
            ["id"] = "jlegacy", ["type"] = "dig", ["cell"] = "2,2", ["cx"] = 2, ["cz"] = 2, ["owner"] = "legacy",
            ["ownerName"] = "legado", ["startedAt"] = now - 7, ["doneAt"] = now + 23,
        }),
    };
    File.WriteAllText(Path.Combine(dir, "map.json"), legacy.ToJsonString());

    JsonObject initial, final, paint, tunnel, tile;
    List<JsonObject> ops;
    using (var s = await h.Boot(dir))
    {
        var url = s.Url;
        initial = await h.Json(url, "/v1/game/map");
        Check(Same(initial["tiles"], legacy["tiles"]), "tiles legados", initial["tiles"]);
        var resumed = Obj(Arr(initial["jobs"])[0]);
        Check(Str(resumed["id"]) == "jlegacy" && Num(resumed["progress"]) > 0.20, "job legado retomado", resumed);
        Check(File.Exists(Path.Combine(dir, "map.json.migrated")) && File.Exists(Path.Combine(dir, "world/world.snapshot.json")),
            "mapa legado migrado");

        var alice = await h.Json(url, "/v1/game/sessions", new JsonObject { ["name"] = "Alice" });
        var bob = await h.Json(url, "/v1/game/sessions", new JsonObject { ["name"] = "Bob" });
        var (a, b) = (Str(alice["token"]), Str(bob["token"]));
        const string command = "/v1/game/command";
        // Both occupied chunks must finish crystallizing before the test captures the map.
        // Their edits have no owner, and replay must keep them free of journal sequence numbers.
        await h.Json(url, command, new JsonObject { ["action"] = "pose", ["x"] = 0, ["z"] = -12 }, a);
        bool ready = false;
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var map = await h.Json(url, "/v1/game/map");
            var chunks = Arr(map["edits"]).Select(Obj).Select(op => Str(op["chk"])).ToHashSet();
            if (ready = chunks.Contains("0,-1") && chunks.Contains("0,0")) break;
            await Task.Delay(100);
        }
        Require(ready, "The garden and spawn chunks must crystallize before comparing revisions.");
        tile = await h.Json(url, "/v1/game/map/edits", new JsonObject
        {
            ["edits"] = new JsonArray(new JsonObject { ["cx"] = 3, ["cz"] = 3, ["type"] = "path" }),
        }, a);
        var sculpt = await h.Json(url, command, new JsonObject { ["action"] = "sculpt", ["x"] = 7, ["z"] = 7, ["r"] = 2, ["dh"] = 1 }, a);
        paint = await h.Json(url, command, new JsonObject { ["action"] = "paint", ["x"] = 7, ["z"] = 7, ["r"] = 2, ["mat"] = "water" }, b);
        var passage = await h.Json(url, command, new JsonObject { ["action"] = "passage", ["x1"] = 4, ["z1"] = 5, ["x2"] = 9, ["z2"] = 5, ["r"] = 2 }, a);
        var carves = await h.Json(url, command, new JsonObject { ["action"] = "carve", ["x"] = 10, ["z"] = 5, ["r"] = 2, ["y0"] = -3 }, a);
        foreach (var batch in new[] { sculpt, passage, carves })
        {
            var undo = new JsonObject { ["action"] = "undo_surface", ["edit"] = batch["edit"]!.DeepClone() };
            Check(Str((await h.Json(url, command, undo, b, 404))["error"]) == "edit_not_found", "undo de edição alheia recusado");
            Check(Flag((await h.Json(url, command, undo, a))["ok"]), "undo do dono", batch);
            await h.Json(url, command, undo, a, 404);
        }

        var points = new JsonArray(
            new JsonObject { ["x"] = 0, ["y"] = 0, ["z"] = 4 },
            new JsonObject { ["x"] = 4, ["y"] = 0.5, ["z"] = 10 },
            new JsonObject { ["x"] = 10, ["y"] = 0.4, ["z"] = 18 });
        var corridor = new JsonObject { ["action"] = "tunnel", ["points"] = points, ["r"] = 2.4 };
        var invalid = await h.Json(url, command, new JsonObject
        {
            ["action"] = "tunnel", ["points"] = new JsonArray(points[0]!.DeepClone(), points[1]!.DeepClone()),
        }, a, 400);
        Check(Str(invalid["error"]) == "tunnel_path_3_to_8", "corredor precisa de 3 a 8 pontos", invalid);
        var removed = await h.Json(url, command, corridor, a);
        await h.Json(url, command, new JsonObject { ["action"] = "undo_surface", ["edit"] = removed["edit"]!.DeepClone() }, a);
        tunnel = await h.Json(url, command, corridor, b);

        final = await h.Json(url, "/v1/game/map");
        Check(Flag(final["volumeTunnels"]), "corredores volumétricos anunciados", final["volumeTunnels"]);
        // Só as edições dos jogadores têm "edit": a cristalização de chunk vem em nome da Guardiã.
        var owned = Arr(final["edits"]).Select(Obj).Where(op => op["edit"] is not null).ToList();
        Check(owned.Select(op => Str(op["edit"])).SequenceEqual([Str(paint["edit"]), Str(tunnel["edit"])]),
            "edições ativas dos jogadores", owned);
        var last = owned[^1];
        Check(Str(last["kind"]) == "tunnel" && Num(Obj(Arr(last["points"])[1])["y"]) < -2.5, "corredor escavado", last);
        Check(Num(Obj(Obj(final["tiles"])["3,3"])["rev"]) == Num(tile["revision"]), "célula 3,3 na revisão do lote");

        foreach (var session in new[] { alice, bob })
        {
            var token = Str(session["token"]);
            var since = (long)Num(session["eventsFrom"]);
            var events = await h.Json(url, $"/v1/game/events?since={since}&wait=0", token: token);
            Check(Arr(events["events"]).Select(Obj).Count(e => e["op"] is not null && Str(Obj(e["op"])["kind"]) == "undo_surface") == 4,
                "quatro undos nos eventos de " + Str(Obj(session["character"])["name"]), events["events"]);
            Check(Same(Obj((await h.Json(url, "/v1/game/state", token: token))["map"])["edits"], final["edits"]), "estado do cliente igual ao mapa");
        }

        ops = Journal(dir);
        Check(ops.Count(op => Str(op["kind"]) == "surface_batch") == 6, "seis lotes de superfície no journal",
            ops.Select(op => Str(op["kind"])).ToArray());
        Check(ops.Any(op => Str(op["kind"]) == "tile" && Str(op["cell"]) == "3,3"), "op de célula no journal");
        Check(ops.Select(op => (long)Num(op["op"])).SequenceEqual(Enumerable.Range(1, ops.Count).Select(i => (long)i)),
            "ops numeradas em sequência");
    }

    using (var s = await h.Boot(dir))
    {
        var url = s.Url;
        var replay = await h.Json(url, "/v1/game/map");
        foreach (var key in new[] { "revision", "tiles", "edits", "placements" })
            Check(Same(replay[key], final[key]), "replay de " + key, new { expected = final[key], actual = replay[key] });
        var job = Obj(Arr(replay["jobs"])[0]);
        Check(Str(job["id"]) == "jlegacy" && Num(job["progress"]) >= Num(Obj(Arr(initial["jobs"])[0])["progress"]), "job segue o relógio após replay", job);
        var token = Str((await h.Json(url, "/v1/game/sessions", new JsonObject { ["name"] = "Alice" }))["token"]);
        var later = await h.Json(url, "/v1/game/command", new JsonObject { ["action"] = "sculpt", ["x"] = 8, ["z"] = 7, ["dh"] = 1 }, token);
        Check(Num(later["revision"]) > Num(replay["revision"]), "revisão cresce após o replay");
        Check(Num(Journal(dir)[^1]["op"]) > Num(ops[^1]["op"]), "journal continua a numeração");
    }
}

// ---------- cenário: catálogo de assets segregado, allowlist e limpeza ----------

async Task Assets(Harness h, string root)
{
    var dir = h.TempDir();
    var approved = File.ReadAllBytes(Path.Combine(root, "assets/glb/prop.stump.glb"));
    var rejected = File.ReadAllBytes(Path.Combine(root, "assets/glb/prop.log.glb"));
    var digest = Convert.ToHexString(SHA256.HashData(approved)).ToLowerInvariant();
    var settings = new Dictionary<string, string?> { ["ULTIMA_WORLD_ID"] = "bosque", ["ULTIMA_UPLOAD_SHA256_ALLOWLIST"] = digest };
    JsonObject asset;
    using (var s = await h.Boot(dir, settings))
    {
        var token = Str((await h.Json(s.Url, "/v1/game/sessions", new JsonObject { ["name"] = "Alice" }))["token"]);
        const string endpoint = "/v1/assets/upload?name=modelo";
        Check(Str((await h.Binary(s.Url, endpoint, rejected, token, 403))["error"]) == "asset_not_approved", "GLB fora da allowlist recusado");
        asset = Obj((await h.Binary(s.Url, endpoint, approved, token))["asset"]);
        Check(Str(asset["sha256"]) == digest, "SHA-256 do GLB publicado", asset);
        Check(Same((await h.Json(s.Url, "/v1/assets"))["assets"], new JsonArray(asset.DeepClone())), "catálogo do mundo bosque", asset);
    }
    using (var s = await h.Boot(dir, new Dictionary<string, string?> { ["ULTIMA_WORLD_ID"] = "lago" }))
    {
        Check(Arr((await h.Json(s.Url, "/v1/assets"))["assets"]).Count == 0, "catálogo de outro mundo vazio");
        Check(Obj((await h.Json(s.Url, "/v1/game/map"))["tiles"]).Count == 0, "mapa de outro mundo vazio");
    }

    var assets = Path.Combine(dir, "worlds/bosque/assets");
    var live = Path.Combine(assets, Str(asset["file"]).Split('/')[^1]);
    var stale = Path.Combine(assets, "player.modelo-deadbeef.glb");
    File.WriteAllBytes(stale, rejected);
    var old = DateTime.UtcNow - TimeSpan.FromDays(32);
    foreach (var file in new[] { live, stale }) File.SetLastWriteTimeUtc(file, old);
    using (var s = await h.Boot(dir, new Dictionary<string, string?>(settings) { ["ULTIMA_ASSET_GC_DAYS"] = "30" }))
    {
        // A limpeza roda no boot e a cada 24 h; o órfão já pode ter saído antes da primeira consulta.
        for (var attempt = 0; attempt < 50 && File.Exists(stale); attempt++) await Task.Delay(100);
        var listed = Arr((await h.Json(s.Url, "/v1/assets"))["assets"]);
        Check(!File.Exists(stale) && File.Exists(live), "órfão expirado removido e arquivo vivo preservado", new { live, stale });
        Check(Same(listed, new JsonArray(asset.DeepClone())), "catálogo preservado pela limpeza", listed);
    }
}

// ---------- cenário: famílias, escopo, lote atômico, diálogo da Guardiã e replay ----------

async Task Garden(Harness h, string root)
{
    using var provider = new Provider();
    var dir = h.TempDir();
    var glb = File.ReadAllBytes(Path.Combine(root, "assets/glb/prop.stump.glb"));
    var approved = Convert.ToHexString(SHA256.HashData(glb)).ToLowerInvariant();
    var settings = new Dictionary<string, string?>(provider.Settings) { ["ULTIMA_UPLOAD_SHA256_ALLOWLIST"] = approved };
    const string garden = "/v1/game/garden";
    const string command = "/v1/game/command";
    JsonObject final = new();
    using (var s = await h.Boot(dir, settings))
    {
        var url = s.Url;
        var catalog = await h.Json(url, garden);
        var families = Obj(catalog["families"]);
        Check(families.Select(pair => pair.Key).Order(StringComparer.Ordinal).SequenceEqual(["decor", "plants", "terrain"]), "famílias do jardim", families);
        Check(Str(Obj(Obj(Obj(Obj(catalog["models"])["*"])["parameters"])["scaleY"])["inherits"]) == "scale",
            "scaleY herda de scale", catalog["models"]);
        Check(Arr(Obj(families["plants"])["actions"]).Any(action => Str(action) == "customize"), "plants aceita customize", families["plants"]);
        Check(Num(Obj(Obj(Obj(Obj(catalog["models"])["prop.lantern"])["parameters"])["poleHeight"])["default"]) == 1.4,
            "poleHeight padrão da luminária", catalog["models"]);

        var session = await h.Json(url, "/v1/game/sessions", new JsonObject { ["name"] = "Jardineiro" });
        var token = Str(session["token"]);
        var observer = await h.Json(url, "/v1/game/sessions", new JsonObject { ["name"] = "Observador" });
        var initial = await h.Json(url, "/v1/game/map");
        var place = new JsonObject { ["action"] = "place", ["asset"] = "prop.lantern", ["x"] = 2, ["z"] = -12 };
        Check(Str((await h.Json(url, garden + "/edits", new JsonObject { ["actions"] = new JsonArray(place.DeepClone()) }, expect: 401))["error"]) == "invalid_token",
            "edição sem sessão recusada");
        Check(Str((await h.Json(url, garden + "/plants", new JsonObject { ["actions"] = new JsonArray(place.DeepClone()) }, token, 400))["error"]) == "wrong_family",
            "decor não entra por plants");
        Check(Str((await h.Json(url, garden + "/edits", new JsonObject
        {
            ["actions"] = new JsonArray(place.DeepClone(), new JsonObject { ["action"] = "place", ["asset"] = "prop.lantern", ["x"] = 70, ["z"] = -12 }),
        }, token, 400))["error"]) == "outside_garden", "fora do jardim recusado");
        Check(Same(Owned(await h.Json(url, "/v1/game/map")), Owned(initial)), "recusas não alteram o mapa");
        Check(Str((await h.Json(url, garden + "/dialog", new JsonObject { ["message"] = "Trilha" }, token, 409))["error"]) == "guardian_out_of_reach",
            "Guardiã longe");
        await h.Json(url, command, new JsonObject { ["action"] = "pose", ["x"] = 0, ["z"] = -12 }, token);
        Check(Str((await h.Json(url, garden + "/dialog", new JsonObject { ["message"] = "só texto" }, token, 422))["error"]) == "invalid_ai_response",
            "plain text cannot act as a hosted tool call");
        Check(Str((await h.Json(url, garden + "/dialog", new JsonObject { ["message"] = "resposta ruim" }, token, 422))["error"]) == "invalid_ai_response",
            "resposta inválida da IA");
        Check(Str((await h.Json(url, garden + "/dialog", new JsonObject { ["message"] = "provedor fora" }, token, 503))["error"]) == "ai_unavailable",
            "provedor fora");
        Check(Same(Owned(await h.Json(url, "/v1/game/map")), Owned(initial)), "falhas da IA preservam o mundo");

        var contextual = await h.Json(url, garden + "/context", token: token);
        Check(contextual["toolProposalsSupported"]?.GetValue<bool>() == true, "Hosted proposal capability is declared.");
        var preview = await h.Json(url, garden + "/dialog",
            new JsonObject { ["message"] = "Prévia do jardim", ["preview"] = true }, token);
        Check(Arr(preview["actions"]).Count == 3, "Hosted preview returns structured actions.", preview);
        Check(Same(Owned(await h.Json(url, "/v1/game/map")), Owned(initial)), "Hosted preview leaves the world untouched.");

        var edited = await h.Json(url, garden + "/dialog", new JsonObject { ["message"] = "Luminária, flores e trilha" }, token);
        Check(Str(edited["source"]) == "llm" && Num(edited["applied"]) == 7, "plano do provedor aplicado", edited);
        Check(Same(Owned(Obj(edited["map"])), Owned(await h.Json(url, "/v1/game/map"))), "mapa do diálogo igual ao mapa público");
        var events = await h.Json(url, $"/v1/game/events?since={(long)Num(observer["eventsFrom"])}&wait=0", token: Str(observer["token"]));
        Check(Arr(events["events"]).Select(Obj).Any(e => Str(e["type"]) == "world_edited" && Num(e["revision"]) == Num(edited["revision"])),
            "observador recebeu a edição", events["events"]);
        Check(Num((await h.Json(url, garden + "/dialog", new JsonObject { ["message"] = "e o que eu pedi?" }, token))["applied"]) == 0,
            "histórico mantém o pedido anterior");
        var external = new JsonObject { ["message"] = "histórico gerenciado pelo Genius", ["externalHistory"] = true, ["preview"] = true };
        Check(Num((await h.Json(url, garden + "/dialog", external, token))["applied"]) == 0,
            "A Genius-owned turn bypasses the legacy hosted transcript.");

        var flower = Arr(edited["operations"]).Select(Obj).First(op => Str(op["asset"]) == "flora.flowers");
        var replaced = await h.Json(url, garden + "/plants", new JsonObject
        {
            ["actions"] = new JsonArray(
                new JsonObject { ["action"] = "remove", ["uid"] = flower["uid"]!.DeepClone() },
                new JsonObject { ["action"] = "place", ["asset"] = "flora.fern", ["x"] = flower["x"]!.DeepClone(), ["z"] = flower["z"]!.DeepClone() }),
        }, token);
        Check(Num(replaced["applied"]) == 2, "troca da flor pela samambaia", replaced);
        var before = await h.Json(url, "/v1/game/map");
        var planted = Arr(Owned(before)["placements"]).Select(Obj).ToList();
        Check(planted.Select(p => Str(p["asset"])).Order(StringComparer.Ordinal).SequenceEqual(["flora.fern", "prop.lantern"]), "colocações do jardim", planted);
        var lamp = planted.First(p => Str(p["asset"]) == "prop.lantern");
        var custom = new JsonObject { ["action"] = "customize", ["uid"] = lamp["uid"]!.DeepClone(), ["parameters"] = new JsonObject { ["lightIntensity"] = 1.2 } };
        var bad = new JsonObject { ["action"] = "customize", ["uid"] = lamp["uid"]!.DeepClone(), ["parameters"] = new JsonObject { ["poleHeight"] = 12 } };
        var missing = (JsonObject)bad.DeepClone();
        missing["uid"] = "missing";
        Check(Str((await h.Json(url, garden + "/decor", new JsonObject { ["actions"] = new JsonArray(custom, missing) }, token, 400))["error"]) == "invalid_placement",
            "uid inexistente");
        Check(Str((await h.Json(url, garden + "/decor", new JsonObject { ["actions"] = new JsonArray(bad.DeepClone()) }, token, 400))["error"]) == "invalid_model_parameters",
            "parâmetro fora da faixa");
        Check(Same(Owned(await h.Json(url, "/v1/game/map")), Owned(before)), "parâmetros inválidos não alteram o mapa");

        var taller = (JsonObject)place.DeepClone();
        taller["x"] = -2;
        taller["parameters"] = new JsonObject { ["poleHeight"] = 2.4 };
        var made = await h.Json(url, garden + "/decor", new JsonObject { ["actions"] = new JsonArray(taller) }, token);
        var newLamp = Obj(Arr(made["operations"])[0]);
        await h.Json(url, garden + "/decor", new JsonObject
        {
            ["actions"] = new JsonArray(new JsonObject
            {
                ["action"] = "customize", ["uid"] = newLamp["uid"]!.DeepClone(), ["parameters"] = new JsonObject { ["lightIntensity"] = 1.2 },
            }),
        }, token);
        var adjusted = await GardenModelIntegration.UpdateAsync(h, url, token, "prop.lantern", "poleHeight", "add", 0.6);
        Check(Num(adjusted["applied"]) == 2, "The generic tool adjusts both lanterns.", adjusted);
        final = await h.Json(url, "/v1/game/map");
        var lamps = new[] { lamp["uid"], newLamp["uid"] }.Select(uid => Arr(Owned(final)["placements"]).Select(Obj).First(p => Same(p["uid"], uid))).ToArray();
        Check(Same(lamps[0]["x"], lamp["x"]) && Same(lamps[0]["z"], lamp["z"]), "posição da luminária preservada", lamps[0]);
        Check(Same(lamps[0]["parameters"], new JsonObject { ["poleHeight"] = 2 }) &&
              Same(lamps[1]["parameters"], new JsonObject { ["poleHeight"] = 3, ["lightIntensity"] = 1.2 }), "parâmetros ajustados", lamps);
        events = await h.Json(url, $"/v1/game/events?since={(long)Num(observer["eventsFrom"])}&wait=0", token: Str(observer["token"]));
        Check(Arr(events["events"]).Select(Obj).Any(e => Str(e["type"]) == "world_edited" && Num(e["revision"]) == Num(final["revision"])),
            "observador recebeu o ajuste", events["events"]);
        await GardenModelIntegration.VerifyAsync(h, url, token, lamps);
        var fern = Arr(Owned(final)["placements"]).Select(Obj).First(p => Str(p["asset"]) == "flora.fern");
        await h.Json(url, garden + "/plants", new JsonObject
        {
            ["actions"] = new JsonArray(new JsonObject
            {
                ["action"] = "customize", ["uid"] = fern["uid"]!.DeepClone(), ["parameters"] = new JsonObject { ["poleHeight"] = 2 },
            }),
        }, token, 400);
        var scaled = Obj((await h.Json(url, garden + "/plants", new JsonObject
        {
            ["actions"] = new JsonArray(new JsonObject
            {
                ["action"] = "customize", ["uid"] = fern["uid"]!.DeepClone(), ["parameters"] = new JsonObject { ["scaleY"] = 2, ["rotationY"] = 1.57 },
            }),
        }, token))["map"]);
        var updated = Arr(Owned(scaled)["placements"]).Select(Obj).First(p => Same(p["uid"], fern["uid"]));
        var expected = (JsonObject)fern.DeepClone();
        expected["parameters"] = new JsonObject { ["scaleY"] = 2, ["rotationY"] = 1.57 };
        Check(Same(updated, expected), "samambaia escalada", updated);

        var imported = Str(Obj((await h.Binary(url, "/v1/assets/upload?name=tronco", glb, token))["asset"])["id"]);
        Check(Str((await h.Json(url, garden + "/context", token: token))["prompt"]).Contains(imported, StringComparison.Ordinal), "modelo importado entra no contexto");
        var placedTrunk = await h.Json(url, garden + "/decor", new JsonObject
        {
            ["actions"] = new JsonArray(new JsonObject
            {
                ["action"] = "place", ["asset"] = imported, ["x"] = 6, ["z"] = -12, ["scale"] = 1.25,
                ["parameters"] = new JsonObject { ["scaleX"] = 0.8, ["scaleZ"] = 1.6 },
            }),
        }, token);
        var trunkUid = Obj(Arr(placedTrunk["operations"])[0])["uid"];
        final = Obj((await h.Json(url, garden + "/decor", new JsonObject
        {
            ["actions"] = new JsonArray(new JsonObject
            {
                ["action"] = "customize", ["uid"] = trunkUid!.DeepClone(), ["parameters"] = new JsonObject { ["scaleY"] = 2.25 },
            }),
        }, token))["map"]);
        var variant = Arr(Owned(final)["placements"]).Select(Obj).First(p => Same(p["uid"], trunkUid));
        Check(Num(variant["scale"]) == 1.25 && Same(variant["parameters"], new JsonObject { ["scaleX"] = 0.8, ["scaleY"] = 2.25, ["scaleZ"] = 1.6 }),
            "variante do modelo importado", variant);
    }

    using (var s = await h.Boot(dir, new Dictionary<string, string?> { ["NPC_SERVER_AI"] = "off" }))
    {
        var url = s.Url;
        Check(Same(Owned(await h.Json(url, "/v1/game/map")), Owned(final)), "replay do jardim");
        var token = Str((await h.Json(url, "/v1/game/sessions", new JsonObject { ["name"] = "Jardineiro" }))["token"]);
        await h.Json(url, command, new JsonObject { ["action"] = "pose", ["x"] = 0, ["z"] = -12 }, token);
        Check(Str((await h.Json(url, garden + "/dialog", new JsonObject { ["message"] = "adicione flores" }, token, 503))["error"]) == "ai_unavailable",
            "IA do servidor desligada");
        Check(Same(Owned(await h.Json(url, "/v1/game/map")), Owned(final)), "mapa preservado sem IA");
    }
    Check(provider.Failures.Length == 0, "provedor de IA", provider.Failures);
}

// ---------- cenário: relatos de diagnóstico (persistência, campos permitidos e limites) ----------

async Task Diagnostics(Harness h, string root)
{
    var dir = h.TempDir();
    using var s = await h.Boot(dir);
    var url = s.Url;
    const string route = "/v1/diagnostics/reports";
    var report = new JsonObject
    {
        ["build"] = "aabbccdd", ["description"] = "GPU indisponível", ["renderer"] = "WebGL2",
        ["gpu"] = new JsonObject { ["state"] = "unsupported", ["progress"] = 0 },
        ["cpu"] = new JsonObject { ["state"] = "ready", ["model"] = "Gemma 3 270M", ["progress"] = 1 },
        ["token"] = "segredo-fictício",
        ["messages"] = new JsonArray("conversa fictícia"),
    };
    await h.Json(url, route, new JsonObject { ["build"] = "invalid" }, expect: 400);
    await h.Json(url, route, new JsonObject { ["build"] = "dev", ["description"] = new string('x', 5000) }, expect: 413);
    var receipt = await h.Json(url, route, report, expect: 201);
    var log = Path.Combine(dir, "diagnostic-reports.jsonl");
    var saved = Obj(JsonNode.Parse(File.ReadAllText(log)));
    Check(Str(saved["id"]) == Str(receipt["id"]) && Str(saved["id"]).Length == 32, "relato persistido com o id do recibo", saved);
    Check(Str(saved["description"]) == Str(report["description"]) && Str(Obj(saved["cpu"])["state"]) == "ready", "descrição e estado da IA salvos", saved);
    Check(!saved.ContainsKey("token") && !saved.ContainsKey("messages"), "token e mensagens ficam fora do relato", saved);
    await h.Json(url, route, expect: 404);   // GET: só existe o POST
    for (var i = 0; i < 7; i++) await h.Json(url, route, report, expect: 201);
    await h.Json(url, route, report, expect: 429);
    var lines = File.ReadLines(log).Count();
    Check(lines == 8, "oito relatos no limite por minuto", lines);
}

// ---------- auxiliares ----------

static class Kit
{
    public static List<JsonObject> Journal(string dir) =>
        [.. File.ReadLines(Path.Combine(dir, "world/world.journal.jsonl")).Select(line => Obj(JsonNode.Parse(line)))];

    /// <summary>
    /// O que os jogadores colocaram no mapa: células, edições com dono e colocações que não
    /// vêm da cristalização de chunks (assinada pela Guardiã, assíncrona ao tick).
    /// Revisão e progresso de jobs mudam com o tempo e ficam fora da comparação.
    /// </summary>
    public static JsonObject Owned(JsonObject map) => new()
    {
        ["tiles"] = map["tiles"]?.DeepClone(),
        ["edits"] = new JsonArray(Arr(map["edits"]).Where(op => op?["edit"] is not null).Select(op => op!.DeepClone()).ToArray()),
        ["placements"] = new JsonArray(Arr(map["placements"])
            .Where(p => !Str(p?["by"]).StartsWith("chunk ", StringComparison.Ordinal)).Select(p => p!.DeepClone()).ToArray()),
    };

    public static JsonObject Obj(JsonNode? node)
    {
        var text = node?.ToJsonString() ?? "null";
        return node as JsonObject ?? throw new Failure("esperado objeto, veio " + text[..Math.Min(text.Length, 200)]);
    }
    public static JsonArray Arr(JsonNode? node)
    {
        var text = node?.ToJsonString() ?? "null";
        return node as JsonArray ?? throw new Failure("esperado array, veio " + text[..Math.Min(text.Length, 200)]);
    }
    public static string Str(JsonNode? node) => node is null ? "" : Element(node).ValueKind == JsonValueKind.String ? Element(node).GetString() ?? "" : node.ToJsonString();
    public static double Num(JsonNode? node) => node is null ? double.NaN : Element(node).GetDouble();
    public static bool Flag(JsonNode? node) => node is not null && Element(node).ValueKind == JsonValueKind.True;
    // Números vindos do JSON podem ser inteiros ou decimais: o elemento converte, GetValue<double>() não.
    static JsonElement Element(JsonNode node) => JsonSerializer.SerializeToElement(node);

    /// <summary>Igualdade por valor: chaves ordenadas e números comparados numericamente.</summary>
    public static string Canon(JsonNode? node)
    {
        var text = new StringBuilder();
        Write(node, text);
        return text.ToString();

        static void Write(JsonNode? current, StringBuilder into)
        {
            switch (current)
            {
                case null: into.Append("null"); break;
                case JsonObject o:
                    into.Append('{');
                    var first = true;
                    foreach (var key in o.Select(pair => pair.Key).Order(StringComparer.Ordinal))
                    {
                        if (!first) into.Append(',');
                        first = false;
                        into.Append('"').Append(key).Append("\":");
                        Write(o[key], into);
                    }
                    into.Append('}');
                    break;
                case JsonArray a:
                    into.Append('[');
                    for (var i = 0; i < a.Count; i++)
                    {
                        if (i > 0) into.Append(',');
                        Write(a[i], into);
                    }
                    into.Append(']');
                    break;
                case JsonValue v when v.GetValueKind() == JsonValueKind.Number:
                    into.Append(Element(v).GetDouble().ToString("R", CultureInfo.InvariantCulture));
                    break;
                default: into.Append(current.ToJsonString()); break;
            }
        }
    }
    public static bool Same(JsonNode? a, JsonNode? b) => Canon(a) == Canon(b);
    public static void Check(bool passed, string what, object? evidence = null)
    {
        if (!passed)
        {
            var text = evidence as JsonNode ?? JsonNode.Parse(JsonSerializer.Serialize(evidence, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var shown = text?.ToJsonString() ?? "null";
            throw new Failure($"{what}: {shown[..Math.Min(shown.Length, 600)]}");
        }
    }
    public static void Require(bool passed, string what)
    {
        if (!passed) throw new Failure(what);
    }
}

sealed class Failure(string message) : Exception(message);

sealed class Server(Process process) : IDisposable
{
    public Uri Url { get; set; } = new Uri("http://127.0.0.1:1/");
    public void Dispose()
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit(10_000);
        }
        catch (InvalidOperationException) { /* já saiu */ }
    }
}

/// <summary>Sobe o servidor Release como processo externo em portas dinâmicas e faz as chamadas.</summary>
sealed class Harness(string server) : IDisposable
{
    readonly List<Server> _servers = [];
    readonly List<string> _dirs = [];
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ultima-server-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _dirs.Add(dir);
        return dir;
    }

    public async Task<Server> Boot(string dataDir, Dictionary<string, string?>? settings = null)
    {
        var start = new ProcessStartInfo("dotnet", $"\"{server}\"")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        start.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
        start.Environment["ULTIMA_DATA_DIR"] = dataDir;
        start.Environment["MESHY_API_KEY"] = "";
        foreach (var (key, value) in settings ?? []) start.Environment[key] = value;

        var output = new StringBuilder();
        var process = Process.Start(start) ?? throw new Failure("dotnet não iniciou o servidor");
        void Collect(object sender, DataReceivedEventArgs e) { if (e.Data is not null) lock (output) output.AppendLine(e.Data); }
        process.OutputDataReceived += Collect;
        process.ErrorDataReceived += Collect;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var instance = new Server(process);
        _servers.Add(instance);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            string text;
            lock (output) text = output.ToString();
            var match = Regex.Match(text, @"Now listening on: (http://127\.0\.0\.1:\d+)");
            if (match.Success)
            {
                instance.Url = new Uri(match.Groups[1].Value);
                return instance;
            }
            if (process.HasExited) break;
            await Task.Delay(25);
        }
        instance.Dispose();
        string tail;
        lock (output) tail = output.ToString();
        throw new Failure("servidor não escutou: " + tail[..Math.Min(tail.Length, 2000)]);
    }

    public Task<JsonObject> Json(Uri url, string path, JsonNode? payload = null, string? token = null, int expect = 200) =>
        Send(url, path, payload is null ? null : new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"), token, expect);

    public Task<JsonObject> Binary(Uri url, string path, byte[] payload, string? token = null, int expect = 200)
    {
        var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue("model/gltf-binary");
        return Send(url, path, content, token, expect);
    }

    async Task<JsonObject> Send(Uri url, string path, HttpContent? content, string? token, int expect)
    {
        using var request = new HttpRequestMessage(content is null ? HttpMethod.Get : HttpMethod.Post, new Uri(url, path));
        if (content is not null) request.Content = content;
        if (token is not null) request.Headers.Add("X-Game-Token", token);
        using var response = await _http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        var body = JsonNode.Parse(text) as JsonObject;
        if ((int)response.StatusCode != expect || body is null)
            throw new Failure($"{path} respondeu {(int)response.StatusCode} (esperado {expect}): {text[..Math.Min(text.Length, 400)]}");
        return body;
    }

    public void Dispose()
    {
        foreach (var instance in _servers) instance.Dispose();
        foreach (var dir in _dirs)
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception) { /* temporário do sistema */ }
        }
    }
}
