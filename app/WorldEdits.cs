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
        var revision = _revision + 1;
        op["rev"] = revision;
        _store.Append(kind, op);
        _edits.Add(op);
        _revision = revision;
        EmitSurfaceEdit(op);
    }

    void EmitSurfaceEdit(JsonObject op) =>
        Emit(new JsonObject { ["type"] = "world_edited", ["revision"] = _revision, ["op"] = op.DeepClone() });

    (int, JsonObject) RecordPlayerEdits(Character player, IReadOnlyList<JsonObject> changes)
    {
        if (_edits.Count > MaxSurfaceEdits - changes.Count) return (507, J.Err("too_many_surface_edits"));
        var edit = "e" + J.Hex(12);
        var revision = _revision;
        foreach (var change in changes)
        {
            change["edit"] = edit;
            change["owner"] = player.EditOwner;
            change["rev"] = ++revision;
        }
        var batch = new JsonObject
        {
            ["rev"] = revision,
            ["changes"] = J.Arr(changes.Select(change => change.DeepClone())),
        };
        _store.Append("surface_batch", batch);
        _edits.AddRange(changes);
        _revision = revision;
        foreach (var change in changes) EmitSurfaceEdit(change);
        return (200, new JsonObject { ["ok"] = true, ["revision"] = revision, ["edit"] = edit });
    }

    (int, JsonObject) UndoSurfaceCmd(Character player, JsonObject data)
    {
        var edit = J.Str(data["edit"]);
        if (edit.Length != 25 || !edit.StartsWith('e')) return (400, J.Err("edit_required"));
        if (!_edits.Any(change => J.Str(change["edit"]) == edit && J.Str(change["owner"]) == player.EditOwner))
            return (404, J.Err("edit_not_found"));

        var revision = _revision + 1;
        var operation = new JsonObject { ["kind"] = "undo_surface", ["edit"] = edit, ["rev"] = revision };
        _store.Append("undo_surface", operation);
        _edits.RemoveAll(change => J.Str(change["edit"]) == edit);
        _revision = revision;
        EmitSurfaceEdit(operation);
        return (200, new JsonObject { ["ok"] = true, ["revision"] = revision });
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
        return RecordPlayerEdits(ch, [op]);
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
        return RecordPlayerEdits(ch, [op]);
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

    // ---------- F3: cristalização assistida (agente v1, regras) ----------
    // Um chunk inédito visitado por um jogador é enriquecido UMA vez com feições
    // determinísticas (hash das coordenadas) e cristalizado no journal — daí em diante
    // é autoritativo para todos. v1 são regras leves; o LLM (quando disponível) entra
    // como produtor assíncrono sugerindo feições, nunca no caminho crítico do tick.

    /// <summary>RNG determinístico mínimo (mulberry32) — mesmo algoritmo do cliente.</summary>
    static double ChunkRnd(uint seed)
    {
        seed += 0x6D2B79F5;
        var t = seed;
        t = unchecked((t ^ (t >> 15)) * (t | 1));
        t ^= t + unchecked((t ^ (t >> 7)) * (t | 61));
        return ((t ^ (t >> 14)) & 0xFFFFFF) / 16777216.0;
    }

    /// <summary>Chama-se dentro do lock do tick; devolve true quando cristalizou um chunk.</summary>
    public bool CrystallizeIfNew(double x, double z)
    {
        var cx = (long)Math.Floor(x / 32);
        var cz = (long)Math.Floor(z / 32);
        if (Math.Abs(cx) > 12 || Math.Abs(cz) > 12) return false;   // ±384 m
        var key = cx + "," + cz;
        if (!_visitedChunks.Add(key)) return false;
        var seed = (uint)(HashCode.Combine(cx, cz) & 0x7FFFFFFF);
        var rnd = new Func<double>(() => ChunkRnd(seed++));
        var by = "Guardiã";
        var now = J.Now;
        double Px() => cx * 32 - 16 + rnd() * 32;
        double Pz() => cz * 32 - 16 + rnd() * 32;
        var roll = rnd();
        if (roll < 0.35)
        {
            // Clareira: mancha de material mais claro no meio da vegetação.
            var mat = rnd() < 0.5 ? "sand" : rnd() < 0.5 ? "grass" : "dirt";
            RecordSurfaceEdit("paint", new JsonObject
            {
                ["kind"] = "paint", ["x"] = J.R(Px(), 3), ["z"] = J.R(Pz(), 3), ["r"] = J.R(4 + rnd() * 3, 2),
                ["mat"] = mat, ["by"] = by, ["at"] = J.R(now, 3), ["chk"] = key,
            });
        }
        else if (roll < 0.6)
        {
            // Lagoa: água pintada numa depressão suave.
            var lx = Px(); var lz = Pz();
            RecordSurfaceEdit("sculpt", new JsonObject
            {
                ["kind"] = "sculpt", ["x"] = J.R(lx, 3), ["z"] = J.R(lz, 3), ["r"] = J.R(3 + rnd() * 2, 2),
                ["dh"] = -1.2, ["by"] = by, ["at"] = J.R(now, 3), ["chk"] = key,
            });
            RecordSurfaceEdit("paint", new JsonObject
            {
                ["kind"] = "paint", ["x"] = J.R(lx, 3), ["z"] = J.R(lz, 3), ["r"] = J.R(2.5 + rnd() * 2, 2),
                ["mat"] = "water", ["by"] = by, ["at"] = J.R(now, 3), ["chk"] = key,
            });
        }
        else if (roll < 0.9)
        {
            // Marco: um asset builtin marca o lugar (pedra/registro/samambaia).
            var asset = BuiltinAssets[(int)(rnd() * BuiltinAssets.Length)];
            var p = new Placement
            {
                Uid = "k" + J.Hex(4), AssetId = asset, X = J.R(Px(), 3), Z = J.R(Pz(), 3),
                Rot = J.R(rnd() * Math.PI * 2, 3), Scale = J.R(0.8 + rnd() * 0.8, 3), By = "chunk " + key, At = now,
            };
            _placements.Add(p);
            _revision++;
            var op = p.ToJson();
            op["kind"] = "place";
            op["rev"] = _revision;
            _store.Append("place", op);
            Emit(new JsonObject { ["type"] = "world_edited", ["revision"] = _revision, ["op"] = op.DeepClone() });
        }
        // 10%: o lugar fica como o gerador deixou — nem todo chunk precisa de feição.
        return true;
    }

    /// <summary>
    /// Gruta (F5b base): carve cilíndrico {x,z,r,y0,y1} — remove o terreno do nível
    /// absoluto y0 até y1 e o cliente constrói piso caminhável em y0, paredão de rocha
    /// e, quando y1 fica abaixo do entorno, teto (sala subterrânea ligável a passagens).
    /// </summary>
    (int, JsonObject) CarveCmd(Character ch, JsonObject data, double now)
    {
        if (!Brush(data, out var x, out var z, out var r)) return (400, J.Err("x_z_r_invalid"));
        var y0 = 0.0;
        if (!J.TryFloat(data["y0"], out y0) || y0 < -50 || y0 > 200) return (400, J.Err("y0_invalid"));
        var depth = 3.0;
        if (data.ContainsKey("depth") && !J.TryFloat(data["depth"], out depth)) return (400, J.Err("depth_invalid"));
        depth = Math.Clamp(depth, 1.5, 12);
        var y1 = y0 + depth;   // v1: gruta aberta (teto acima do entorno na vizinhança)
        if (_edits.Count >= MaxSurfaceEdits - 10) return (507, J.Err("too_many_surface_edits"));
        var op = new JsonObject
        {
            ["kind"] = "carve", ["x"] = J.R(x, 3), ["z"] = J.R(z, 3), ["r"] = J.R(r, 2),
            ["y0"] = J.R(y0, 3), ["y1"] = J.R(y1, 3),
            ["by"] = ch.Name, ["at"] = J.R(now, 3), ["cid"] = J.Str(data["cid"]),
        };
        var rock = new JsonObject
        {
            ["kind"] = "paint", ["x"] = J.R(x, 3), ["z"] = J.R(z, 3), ["r"] = J.R(r, 2), ["mat"] = "rock",
            ["by"] = ch.Name, ["at"] = J.R(now, 3), ["cid"] = J.Str(data["cid"]),
        };
        return RecordPlayerEdits(ch, [op, rock]);
    }

    /// <summary>
    /// Passagem (F5a): cava um corredor entre dois pontos numa única ação atômica —
    /// o servidor expande a linha em ops de escultura espaçadas de 0,8·r (mais barato
    /// para o cliente e o journal do que N comandos).
    /// </summary>
    (int, JsonObject) PassageCmd(Character ch, JsonObject data, double now)
    {
        if (!J.TryFloat(data["x1"], out var x1) || !J.TryFloat(data["z1"], out var z1) ||
            !J.TryFloat(data["x2"], out var x2) || !J.TryFloat(data["z2"], out var z2))
            return (400, J.Err("x1_z1_x2_z2_required"));
        if (!InEditBounds(x1, z1) || !InEditBounds(x2, z2)) return (400, J.Err("x_z_r_invalid"));
        var len = Math.Sqrt((x2 - x1) * (x2 - x1) + (z2 - z1) * (z2 - z1));
        if (len is < 1 or > 60) return (400, J.Err("length_1_to_60"));
        var r = 4.0;
        if (data.ContainsKey("r") && !J.TryFloat(data["r"], out r)) return (400, J.Err("x_z_r_invalid"));
        r = Math.Clamp(r, 1, 8);
        var dh = -1.5;
        if (data.ContainsKey("dh") && !J.TryFloat(data["dh"], out dh)) return (400, J.Err("dh_required"));
        dh = Math.Clamp(dh, -2, 2);
        if (Math.Abs(dh) < 1e-4) return (400, J.Err("dh_required"));
        if (_edits.Count >= MaxSurfaceEdits - 70) return (507, J.Err("too_many_surface_edits"));
        // Ops ao longo da linha + paredes de rocha nas laterais para ler como passagem.
        var step = r * 0.8;
        var n = (int)Math.Ceiling(len / step) + 1;
        var changes = new List<JsonObject>();
        for (var i = 0; i <= n; i++)
        {
            var t = n == 0 ? 0 : (double)i / n;
            var x = x1 + (x2 - x1) * t;
            var z = z1 + (z2 - z1) * t;
            var op = new JsonObject
            {
                ["kind"] = "sculpt", ["x"] = J.R(x, 3), ["z"] = J.R(z, 3), ["r"] = J.R(r, 2), ["dh"] = J.R(dh, 3),
                ["by"] = ch.Name, ["at"] = J.R(now, 3), ["cid"] = J.Str(data["cid"]),
            };
            changes.Add(op);
            if (i % 2 == 0)
            {
                var wall = new JsonObject
                {
                    ["kind"] = "paint", ["x"] = J.R(x, 3), ["z"] = J.R(z, 3), ["r"] = J.R(r * 1.4, 2), ["mat"] = "rock",
                    ["by"] = ch.Name, ["at"] = J.R(now, 3), ["cid"] = J.Str(data["cid"]),
                };
                changes.Add(wall);
            }
        }
        var (status, reply) = RecordPlayerEdits(ch, changes);
        if (status == 200) reply["ops"] = n + 1;
        return (status, reply);
    }

    (int, JsonObject) TunnelCmd(Character ch, JsonObject data, double now)
    {
        if (data["points"] is not JsonArray path || path.Count is < 3 or > 8)
            return (400, J.Err("tunnel_path_3_to_8"));
        var radius = 2.4;
        if (data.ContainsKey("r") && !J.TryFloat(data["r"], out radius))
            return (400, J.Err("x_z_r_invalid"));
        radius = Math.Clamp(radius, 1.8, 3);
        var points = new List<(double X, double Y, double Z)>(path.Count);
        var length = 0.0;
        foreach (var point in path)
        {
            if (point is not JsonObject node || !J.TryFloat(node["x"], out var x) ||
                !J.TryFloat(node["y"], out var y) || !J.TryFloat(node["z"], out var z) ||
                !InEditBounds(x, z) || y is < -20 or > 45)
                return (400, J.Err("invalid_tunnel_point"));
            if (points.Count > 0)
            {
                var previous = points[^1];
                var dx = x - previous.X;
                var dz = z - previous.Z;
                var segment = Math.Sqrt(dx * dx + dz * dz);
                if (segment < 5 || Math.Abs(y - previous.Y) > segment * 0.8)
                    return (400, J.Err("tunnel_points_too_close_or_steep"));
                length += segment;
            }
            points.Add((x, y, z));
        }
        if (length > 60) return (400, J.Err("tunnel_too_long"));
        if (_edits.Count(op => J.Str(op["kind"]) == "tunnel") >= 48)
            return (507, J.Err("too_many_tunnels"));

        var centerX = (points.Min(point => point.X) + points.Max(point => point.X)) / 2;
        var centerZ = (points.Min(point => point.Z) + points.Max(point => point.Z)) / 2;
        var extent = points.Max(point => Math.Max(Math.Abs(point.X - centerX), Math.Abs(point.Z - centerZ))) + radius + 0.5;
        var stored = points.Select((point, index) => new JsonObject
        {
            ["x"] = J.R(point.X, 3),
            ["z"] = J.R(point.Z, 3),
            ["y"] = J.R(point.Y + radius * (index == 0 || index == points.Count - 1 ? 0.4 : -1.35), 3),
        });
        var op = new JsonObject
        {
            ["kind"] = "tunnel", ["x"] = J.R(centerX, 3), ["z"] = J.R(centerZ, 3),
            ["r"] = J.R(extent, 3), ["radius"] = J.R(radius, 3), ["points"] = J.Arr(stored),
            ["by"] = ch.Name, ["at"] = J.R(now, 3), ["cid"] = J.Str(data["cid"]),
        };
        return RecordPlayerEdits(ch, [op]);
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
