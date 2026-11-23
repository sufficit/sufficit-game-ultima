using System.Text.Json.Nodes;

namespace Ultima.Server;

/// <summary>Objeto plantado no mundo por um jogador (F6): asset do catálogo com transform.</summary>
sealed class Placement
{
    public string Uid = "", AssetId = "", By = "";
    public double X, Y, Z, Rot, Scale = 1, At;

    public JsonObject ToJson() => new()
    {
        ["uid"] = Uid, ["asset"] = AssetId,
        ["x"] = J.R(X, 3), ["y"] = J.R(Y, 3), ["z"] = J.R(Z, 3),
        ["rot"] = J.R(Rot, 3), ["scale"] = J.R(Scale, 3),
        ["by"] = By, ["at"] = J.R(At, 3),
    };

    public static Placement FromJson(JsonObject o) => new()
    {
        Uid = J.Str(o["uid"]), AssetId = J.Str(o["asset"]), By = J.Str(o["by"]),
        X = J.TryFloat(o["x"], out var x) ? x : 0, Y = J.TryFloat(o["y"], out var y) ? y : 0,
        Z = J.TryFloat(o["z"], out var z) ? z : 0,
        Rot = J.TryFloat(o["rot"], out var r) ? r : 0,
        Scale = J.TryFloat(o["scale"], out var s) ? Math.Clamp(s, 0.05, 8) : 1,
        At = J.TryFloat(o["at"], out var a) ? a : 0,
    };
}

/// <summary>
/// Edição do mundo pelas mãos dos jogadores (F4/F6 do PLAN-WORLD-BUILDER): escultura e
/// pintura de superfície (ops de pincel aplicadas deterministicamente pelos clientes nos
/// chunks) e plantio/remoção de assets. O servidor valida, registra no journal (atrás da
/// IWorldStore) e transmite o evento world_edited — não computa geometria.
/// </summary>
sealed partial class GameWorld
{
    // Ativos estáticos do repositório (assets/glb CC0, servidos pelo site) plantáveis sem
    // passarem pelo catálogo dinâmico do servidor.
    static readonly string[] BuiltinAssets =
        ["scenery.bush", "flora.flowers", "flora.mushroom", "flora.fern", "prop.log", "prop.stump",
         "flora.pine", "rock.boulder2", "scenery.grass2"];

    static readonly string[] PaintMaterials = ["grass", "dirt", "sand", "rock", "snow", "water"];

    const double EditBound = 400;                 // edições até 400 m do centro (mundo cresce na F3)
    const int MaxSurfaceEdits = 20_000, MaxPlacements = 500;

    /// <summary>Validação de assets do catálogo dinâmico (ligado no Program após o WorldAssets existir).</summary>
    public Func<string, bool>? AssetExists { get; set; }

    bool AssetAllowed(string id) => BuiltinAssets.Contains(id) || (AssetExists?.Invoke(id) ?? false);

    static bool InEditBounds(double x, double z) => Math.Abs(x) <= EditBound && Math.Abs(z) <= EditBound;

    // Pincel de superfície comum a sculpt/paint: valida x/z/r e devolve os valores saneados.
    static bool Brush(JsonObject data, out double x, out double z, out double r)
    {
        x = z = r = 0;
        if (!J.TryFloat(data["x"], out x) || !J.TryFloat(data["z"], out z)) return false;
        if (!InEditBounds(x, z)) return false;
        r = 4;
        if (data.ContainsKey("r") && !J.TryFloat(data["r"], out r)) return false;
        r = Math.Clamp(r, 0.5, 8);
        return true;
    }

    void RecordSurfaceEdit(string kind, JsonObject op)
    {
        _revision++;
        op["rev"] = _revision;
        _edits.Add(op);
        _store.Append(kind, op);
        Emit(new JsonObject { ["type"] = "world_edited", ["revision"] = _revision, ["op"] = op.DeepClone() });
    }

    /// <summary>Esculpe o terreno (pincel aditivo): todos os clientes aplicam {x,z,r,dh} nos chunks.</summary>
    (int, JsonObject) SculptCmd(Character ch, JsonObject data, double now)
    {
        if (!Brush(data, out var x, out var z, out var r)) return (400, J.Err("x_z_r_invalid"));
        if (!J.TryFloat(data["dh"], out var dh) || Math.Abs(dh) < 1e-4) return (400, J.Err("dh_required"));
        dh = Math.Clamp(dh, -2, 2);
        if (_edits.Count >= MaxSurfaceEdits) return (507, J.Err("too_many_surface_edits"));
        var op = new JsonObject
        {
            ["kind"] = "sculpt", ["x"] = J.R(x, 3), ["z"] = J.R(z, 3), ["r"] = J.R(r, 2), ["dh"] = J.R(dh, 3),
            ["by"] = ch.Name, ["at"] = J.R(now, 3), ["cid"] = J.Str(data["cid"]),
        };
        RecordSurfaceEdit("sculpt", op);
        return (200, new JsonObject { ["ok"] = true, ["revision"] = _revision });
    }

    /// <summary>Pinta o material do solo (grama/terra/areia/rocha/neve/água) num raio.</summary>
    (int, JsonObject) PaintCmd(Character ch, JsonObject data, double now)
    {
        if (!Brush(data, out var x, out var z, out var r)) return (400, J.Err("x_z_r_invalid"));
        var mat = J.Str(data["mat"]).Trim().ToLowerInvariant();
        if (!PaintMaterials.Contains(mat)) return (400, new JsonObject { ["error"] = "invalid_material", ["allowed"] = J.StrArr(PaintMaterials) });
        if (_edits.Count >= MaxSurfaceEdits) return (507, J.Err("too_many_surface_edits"));
        var op = new JsonObject
        {
            ["kind"] = "paint", ["x"] = J.R(x, 3), ["z"] = J.R(z, 3), ["r"] = J.R(r, 2), ["mat"] = mat,
            ["by"] = ch.Name, ["at"] = J.R(now, 3), ["cid"] = J.Str(data["cid"]),
        };
        RecordSurfaceEdit("paint", op);
        return (200, new JsonObject { ["ok"] = true, ["revision"] = _revision });
    }

    /// <summary>Planta um asset do catálogo (builtin ou do servidor) com transform completo.</summary>
    (int, JsonObject) PlaceCmd(Character ch, JsonObject data, double now)
    {
        var asset = J.Str(data["asset"]).Trim();
        if (!AssetAllowed(asset)) return (400, J.Err("unknown_asset"));
        if (!J.TryFloat(data["x"], out var x) || !J.TryFloat(data["z"], out var z) || !InEditBounds(x, z))
            return (400, J.Err("x_z_required"));
        var y = 0.0;
        if (data.ContainsKey("y") && !J.TryFloat(data["y"], out y)) return (400, J.Err("y_invalid"));
        y = Math.Clamp(y, -50, 200);
        var rot = 0.0;
        if (data.ContainsKey("rot") && !J.TryFloat(data["rot"], out rot)) return (400, J.Err("rot_invalid"));
        rot = Math.Clamp(rot, -Math.PI * 2, Math.PI * 2);
        var scale = 1.0;
        if (data.ContainsKey("scale") && !J.TryFloat(data["scale"], out scale)) return (400, J.Err("scale_invalid"));
        scale = Math.Clamp(scale, 0.05, 8);
        if (_placements.Count >= MaxPlacements) return (507, J.Err("too_many_placements"));
        var p = new Placement
        {
            Uid = "p" + J.Hex(4), AssetId = asset, X = x, Y = y, Z = z, Rot = rot, Scale = scale,
            By = ch.Name, At = now,
        };
        _placements.Add(p);
        _revision++;
        var op = p.ToJson();
        op["kind"] = "place";
        op["rev"] = _revision;
        _store.Append("place", op);
        Emit(new JsonObject { ["type"] = "world_edited", ["revision"] = _revision, ["op"] = op.DeepClone() });
        return (200, new JsonObject { ["ok"] = true, ["uid"] = p.Uid, ["revision"] = _revision });
    }

    /// <summary>Remove um objeto plantado (uid).</summary>
    (int, JsonObject) RemoveCmd(Character ch, JsonObject data, double now)
    {
        var uid = J.Str(data["uid"]);
        var p = _placements.FirstOrDefault(x => x.Uid == uid);
        if (p is null) return (404, J.Err("placement_not_found"));
        _placements.Remove(p);
        _revision++;
        var op = new JsonObject { ["kind"] = "remove", ["uid"] = uid, ["rev"] = _revision, ["by"] = ch.Name, ["at"] = J.R(now, 3) };
        _store.Append("remove", op);
        Emit(new JsonObject { ["type"] = "world_edited", ["revision"] = _revision, ["op"] = op.DeepClone() });
        return (200, new JsonObject { ["ok"] = true, ["revision"] = _revision });
    }
}
