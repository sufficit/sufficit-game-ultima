using System.Text.Json.Nodes;
using Ultima.Garden;

namespace Ultima.Server;

sealed partial class GameWorld
{
    internal const string GardenRoute = "/v1/game/garden";
    static readonly string[] Plants = ["scenery.bush", "flora.flowers", "flora.mushroom", "flora.fern", "flora.pine", "scenery.grass2"];
    static readonly string[] Decorations = ["prop.log", "prop.stump", "rock.boulder2", "prop.lantern"];
    const int GardenActions = 24, GardenChanges = 96;
    const double GardenRadius = 40;

    static bool InGarden(double x, double z, double margin = 0) =>
        double.Hypot(x - NpcX, z - NpcZ) + margin <= GardenRadius;

    public JsonObject GardenCatalog() => new()
    {
        ["scope"] = new JsonObject { ["x"] = NpcX, ["z"] = NpcZ, ["radius"] = GardenRadius },
        ["limits"] = new JsonObject { ["actions"] = GardenActions, ["changes"] = GardenChanges },
        ["families"] = new JsonObject
        {
            ["terrain"] = new JsonObject { ["route"] = GardenRoute + "/terrain", ["actions"] = J.StrArr(["paint", "sculpt", "path"]), ["materials"] = J.StrArr(PaintMaterials) },
            ["plants"] = new JsonObject { ["route"] = GardenRoute + "/plants", ["actions"] = J.StrArr(["place", "remove", "customize"]), ["assets"] = J.StrArr(Plants) },
            ["decor"] = new JsonObject { ["route"] = GardenRoute + "/decor", ["actions"] = J.StrArr(["place", "remove", "customize"]), ["assets"] = J.StrArr(Decorations) },
        },
        ["models"] = ModelCatalog(),
        ["assetCatalog"] = "/v1/assets",
        ["edits"] = GardenRoute + "/edits",
        ["dialog"] = GardenRoute + "/dialog",
    };

    string GardenPrompt()
    {
        var placed = _placements.Where(p => InGarden(p.X, p.Z)).TakeLast(36)
            .Select(p => new JsonObject { ["uid"] = p.Uid, ["asset"] = p.AssetId, ["x"] = p.X, ["z"] = p.Z,
                ["scale"] = p.Scale, ["rot"] = p.Rot, ["parameters"] = p.Parameters?.DeepClone() });
        return """
            Você é a Guardiã do Jardim de Ultima. Converse em português e, quando o jogador pedir mudanças, planeje e execute com o catálogo abaixo.
            Retorne somente JSON no formato {"reply":"resposta curta","actions":[]}. Para conversa ou pedido sem informação suficiente, use actions vazio.
            Use coordenadas absolutas em metros. A Guardiã está em (0,-14), o eixo z positivo aponta para a clareira (0,0).
            Use o jardim com criatividade e composição. Se a posição não foi especificada, escolha um lugar coerente no jardim.
            Use ações do catálogo, sem código, URLs ou comandos externos. Não afirme que executou: o jogo confirma depois de gravar.
            Exemplos de ações:
            {"action":"place","asset":"prop.lantern","x":2,"z":-11,"parameters":{"poleHeight":2,"lightIntensity":1.3}}
            {"action":"customize","uid":"uid real da luminária","parameters":{"poleHeight":2.2,"lightIntensity":1.4}}
            {"action":"paint","mat":"dirt","x":0,"z":-9,"r":2}
            {"action":"sculpt","x":-4,"z":-10,"r":2,"dh":0.5}
            {"action":"path","mat":"rock","r":1.2,"points":[{"x":0,"z":-13},{"x":1,"z":-8},{"x":0,"z":-2}]}
            {"action":"remove","uid":"id existente"}
            {"action":"customize","uid":"uid real da planta","parameters":{"scaleY":1.5}}
            Use customize para modificar qualquer objeto colocado pelo UID. models["*"] vale para todos, inclusive importados.
            scaleX/Y/Z substituem scale no eixo indicado; rotationY substitui rot. Preserve os demais campos omitindo-os.
            Para criar uma variante de objeto existente, use place com o mesmo asset e os parâmetros desejados.
            Em pedidos relativos, use os valores atuais ou os defaults do catálogo e faça um ajuste moderado.
            Para substituir uma planta colocada, remova pelo uid e coloque outra na mesma posição. Plantas nativas não têm uid.
            Catálogo:
            """ + "\n" + GardenCatalog().ToJsonString(J.Opts) + "\nObjetos colocados no jardim: " + J.Arr(placed).ToJsonString(J.Opts)
            + "\nModelos recentes da Oficina: " + J.StrArr(ImportedAssetIds?.Invoke().TakeLast(16) ?? []).ToJsonString(J.Opts);
    }

    public (int, JsonObject) GardenContext(string? token)
    {
        lock (_lock)
        {
            if (Session(token) is null) return (401, J.Err("invalid_token"));
            return (200, new JsonObject { ["prompt"] = GardenPrompt() });
        }
    }

    public async Task<(int, JsonObject)> GardenDialogAsync(string? token, JsonObject data, string ip, CancellationToken cancellation)
    {
        var text = J.Clean(data["message"], NpcChat.MaxMsg);
        if (text.Length == 0) return (400, J.Err("message_required"));
        string prompt, id;
        lock (_lock)
        {
            var ch = Session(token);
            if (ch is null) return (401, J.Err("invalid_token"));
            if (Dist(ch, NpcX, NpcZ) > TalkRadius) return (409, J.Err("guardian_out_of_reach"));
            ch.Last = J.Now;
            if (GardenLanternIntent.Matches(text))
                return AdjustNearbyLanterns(token, ch.X, ch.Z);
            prompt = GardenPrompt();
            id = ch.Id;
        }
        var (status, plan) = await _chat.GardenReplyAsync(id, ip, prompt, text, cancellation);
        if (status != 200 || plan is null) return (status, plan ?? J.Err("ai_unavailable"));
        cancellation.ThrowIfCancellationRequested();
        var reply = J.Clean(plan["reply"], 340);
        if (plan["actions"] is not JsonArray actions || reply.Length == 0) return (422, J.Err("invalid_ai_response"));
        if (actions.Count == 0)
        {
            _chat.RememberGardenReply(id, text, reply);
            return (200, new JsonObject { ["reply"] = reply, ["source"] = "llm", ["applied"] = 0 });
        }
        var (appliedStatus, result) = GardenEdit(token, actions, requireGuardian: true);
        if (appliedStatus == 200)
        {
            _chat.RememberGardenReply(id, text, "Aplicado: " + reply);
            result["reply"] = reply;
            result["source"] = "llm";
        }
        return (appliedStatus, result);
    }

    public (int, JsonObject) GardenEdit(string? token, JsonNode? body, bool requireGuardian = false, string? family = null)
    {
        lock (_lock)
        {
            var ch = Session(token);
            if (ch is null) return (401, J.Err("invalid_token"));
            if (requireGuardian && Dist(ch, NpcX, NpcZ) > TalkRadius) return (409, J.Err("guardian_out_of_reach"));
            var now = J.Now;
            if (!CmdAllowed(ch, now)) return (429, J.Err("rate_limited"));
            ch.Last = now;
            if (body is not JsonArray input || input.Count is 0 or > GardenActions) return (400, J.Err("invalid_actions"));
            List<JsonObject> changes = [];
            var touched = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in input)
            {
                if (node is not JsonObject action) return (400, J.Err("invalid_action"));
                var error = PlanGardenAction(action, changes, touched, family);
                if (error is not null)
                    return (400, new JsonObject { ["error"] = error, ["action"] = J.Str(action["action"]) });
                if (changes.Count > GardenChanges) return (400, J.Err("too_many_garden_changes"));
            }
            if (_edits.Count + changes.Count(IsGardenSurface) > MaxSurfaceEdits) return (507, J.Err("too_many_surface_edits"));
            if (_placements.Count + changes.Count(op => J.Str(op["kind"]) == "place") - changes.Count(op => J.Str(op["kind"]) == "remove") > MaxPlacements)
                return (507, J.Err("too_many_placements"));

            var edit = "e" + J.Hex(12);
            var rev = _revision;
            foreach (var op in changes)
            {
                op["by"] = ch.Name;
                op["at"] = J.R(now, 3);
                op["rev"] = ++rev;
                if (IsGardenSurface(op))
                {
                    op["edit"] = edit;
                    op["owner"] = ch.EditOwner;
                }
            }
            _store.Append("garden_batch", new JsonObject { ["rev"] = rev, ["changes"] = J.Arr(changes.Select(op => op.DeepClone())) });
            foreach (var op in changes)
            {
                if (IsGardenSurface(op)) _edits.Add(op);
                else if (J.Str(op["kind"]) == "place") _placements.Add(Placement.FromJson(op));
                else if (J.Str(op["kind"]) == "customize") Placement.UpdateParameters(_placements, op);
                else _placements.RemoveAll(p => p.Uid == J.Str(op["uid"]));
            }
            _revision = rev;
            foreach (var op in changes) EmitSurfaceEdit(op);
            return (200, new JsonObject { ["ok"] = true, ["applied"] = changes.Count, ["revision"] = rev,
                ["operations"] = J.Arr(changes.Select(op => op.DeepClone())), ["map"] = MapPublic() });
        }
    }

    static bool IsGardenSurface(JsonObject op) => J.Str(op["kind"]) is "paint" or "sculpt";

    string? PlanGardenAction(JsonObject action, List<JsonObject> changes, HashSet<string> touched, string? family)
    {
        var kind = J.Str(action["action"]);
        var asset = J.Str(action["asset"]);
        if (kind is "remove" or "customize")
        {
            var uid = J.Str(action["uid"]);
            var placement = _placements.FirstOrDefault(p => p.Uid == uid);
            if (placement is null || !InGarden(placement.X, placement.Z) || !touched.Add(uid)) return "invalid_placement";
            if (family is not null && family != GardenFamily(placement.AssetId)) return "wrong_family";
            var change = new JsonObject { ["kind"] = kind, ["uid"] = uid };
            if (kind == "customize")
            {
                if (ReadGardenParameters(placement.AssetId, action["parameters"], placement.Parameters) is not { } parameters)
                    return "invalid_model_parameters";
                change["parameters"] = parameters;
            }
            changes.Add(change);
            return null;
        }
        if (kind == "place")
        {
            if (family is not null && family != GardenFamily(asset)) return "wrong_family";
            if (!AssetAllowed(asset)) return "unknown_asset";
            if (!GardenPoint(action, out var x, out var z) || !InGarden(x, z)) return "outside_garden";
            if (!OptionalNumber(action, "rot", 0, -2 * Math.PI, 2 * Math.PI, out var rot) ||
                !OptionalNumber(action, "scale", 1, .05, 8, out var scale)) return "invalid_transform";
            var change = new JsonObject { ["kind"] = kind, ["uid"] = "p" + J.Hex(8), ["asset"] = asset,
                ["x"] = J.R(x, 3), ["z"] = J.R(z, 3), ["rot"] = J.R(rot, 3), ["scale"] = J.R(scale, 3) };
            if (action.ContainsKey("parameters"))
            {
                if (ReadGardenParameters(asset, action["parameters"], null) is not { } parameters)
                    return "invalid_model_parameters";
                change["parameters"] = parameters;
            }
            changes.Add(change);
            return null;
        }
        if (family is not null and not "terrain") return "wrong_family";
        if (kind is not ("paint" or "sculpt" or "path")) return "unknown_action";
        if (!OptionalNumber(action, "r", 2, .5, 8, out var r)) return "invalid_radius";
        var mat = J.Str(action["mat"]);
        if (kind != "sculpt" && !PaintMaterials.Contains(mat)) return "invalid_material";
        if (kind == "path") return AddPath(action["points"], r, mat, changes);
        if (!GardenPoint(action, out var px, out var pz) || !InGarden(px, pz, r)) return "outside_garden";
        if (kind == "paint") changes.Add(GardenPaint(px, pz, r, mat));
        else
        {
            if (!OptionalNumber(action, "dh", 0, -2, 2, out var dh) || Math.Abs(dh) < .001) return "invalid_height";
            changes.Add(new JsonObject { ["kind"] = kind, ["x"] = J.R(px, 3), ["z"] = J.R(pz, 3), ["r"] = J.R(r, 2), ["dh"] = J.R(dh, 3) });
        }
        return null;
    }

    static string GardenFamily(string asset) => Plants.Contains(asset) ? "plants" : "decor";

    static string? AddPath(JsonNode? node, double radius, string material, List<JsonObject> changes)
    {
        if (node is not JsonArray { Count: >= 2 and <= 16 } points) return "invalid_path";
        double previousX = 0, previousZ = 0;
        for (var i = 0; i < points.Count; i++)
        {
            if (points[i] is not JsonObject point || !GardenPoint(point, out var x, out var z) || !InGarden(x, z, radius))
                return "outside_garden";
            if (i == 0) changes.Add(GardenPaint(x, z, radius, material));
            else
            {
                var count = (int)Math.Ceiling(double.Hypot(x - previousX, z - previousZ) / radius);
                if (changes.Count + count > GardenChanges) return "too_many_garden_changes";
                for (var j = 1; j <= count; j++)
                    changes.Add(GardenPaint(previousX + (x - previousX) * j / count,
                        previousZ + (z - previousZ) * j / count, radius, material));
            }
            (previousX, previousZ) = (x, z);
        }
        return null;
    }

    static JsonObject GardenPaint(double x, double z, double r, string mat) => new()
    { ["kind"] = "paint", ["x"] = J.R(x, 3), ["z"] = J.R(z, 3), ["r"] = J.R(r, 2), ["mat"] = mat };

    static bool GardenPoint(JsonObject data, out double x, out double z) =>
        J.TryFloat(data["x"], out x) & J.TryFloat(data["z"], out z);

    static bool OptionalNumber(JsonObject data, string key, double fallback, double min, double max, out double number)
    {
        number = fallback;
        return !data.ContainsKey(key) || J.TryFloat(data[key], out number) && number >= min && number <= max;
    }
}
