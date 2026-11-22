using System.Text.Json.Nodes;

namespace Ultima.Server;

sealed partial class GameWorld
{
    static readonly string[] Actions =
        ["move", "moveto", "look", "pose", "stop", "say", "interact", "dig", "queue", "cancel_order", "stop_job"];

    static bool CmdAllowed(Character ch, double now)
    {
        ch.Cmds.RemoveAll(t => now - t >= 10);
        if (ch.Cmds.Count >= CmdPer10s) return false;
        ch.Cmds.Add(now);
        return true;
    }

    static (long, long)? JobCell(JsonObject data)
    {
        if (data.ContainsKey("cx") && data.ContainsKey("cz"))
            return J.TryInt(data["cx"], out var cx) && J.TryInt(data["cz"], out var cz) ? (cx, cz) : null;
        return J.TryFloat(data["x"], out var x) && J.TryFloat(data["z"], out var z) ? CellOf(x, z) : null;
    }

    JsonObject Ok(Character ch) => new() { ["ok"] = true, ["character"] = CharPublic(ch) };

    public async Task<(int, JsonObject)> CommandAsync(string? token, JsonObject data, string ip)
    {
        var action = J.Str(data["action"]).Trim();
        string? sayText = null;
        Character? sayer = null;
        List<string> others = [];
        lock (_lock)
        {
            var now = J.Now;
            var ch = Session(token);
            if (ch is null) return (401, J.Err("invalid_token"));
            if (!CmdAllowed(ch, now)) return (429, J.Err("rate_limited"));
            ch.Last = now;

            switch (action)
            {
                case "move":
                {
                    if (!J.TryFloat(data["dx"], out var dx) || !J.TryFloat(data["dz"], out var dz))
                        return (400, J.Err("dx_dz_required"));
                    var dur = 0.5;
                    if (data.ContainsKey("dur") && !J.TryFloat(data["dur"], out dur)) return (400, J.Err("dx_dz_required"));
                    var norm = Math.Sqrt(dx * dx + dz * dz);
                    ch.Move = norm < 1e-6 ? null : new Move
                    {
                        Kind = "dir", Dx = dx / norm, Dz = dz / norm, Until = now + Math.Clamp(dur, 0.1, 2.0),
                    };
                    break;
                }
                case "moveto":
                {
                    if (!J.TryFloat(data["x"], out var x) || !J.TryFloat(data["z"], out var z))
                        return (400, J.Err("x_z_required"));
                    var (tx, tz) = ClampWorld(x, z);
                    ch.Move = new Move { Kind = "to", X = tx, Z = tz };
                    break;
                }
                case "look":
                    if (!J.TryFloat(data["yaw"], out var yawL)) return (400, J.Err("yaw_required"));
                    ch.Yaw = Math.Clamp(yawL, -3.15, 3.15);
                    break;
                case "stop":
                    ch.Move = null;
                    break;
                case "pose":
                {
                    if (!J.TryFloat(data["x"], out var x) || !J.TryFloat(data["z"], out var z))
                        return (400, J.Err("x_z_required"));
                    var yaw = ch.Yaw;
                    if (data.ContainsKey("yaw") && !J.TryFloat(data["yaw"], out yaw)) return (400, J.Err("x_z_required"));
                    var dt = Math.Max(0.05, now - (ch.LastPose ?? now - 0.5));
                    // Primeira pose do navegador calibra o spawn; depois, teleporte é recusado.
                    var jumped = ch.LastPose is null ? 0 : Math.Sqrt((x - ch.X) * (x - ch.X) + (z - ch.Z) * (z - ch.Z));
                    if (jumped > WalkSpeed * dt * 3.0 + 2.0)
                        return (409, new JsonObject { ["error"] = "too_fast", ["character"] = CharPublic(ch) });
                    (ch.X, ch.Z) = ClampWorld(x, z);
                    ch.Yaw = Math.Clamp(yaw, -3.15, 3.15);
                    ch.Move = null;
                    ch.LastPose = now;
                    break;
                }
                case "say":
                {
                    var text = J.Clean(data["text"], NpcChat.MaxMsg);
                    if (text.Length == 0) return (400, J.Err("text_required"));
                    others = NearbyTokens(ch, SayRadius).Where(t => t != ch.Token).ToList();
                    Emit(new JsonObject
                    {
                        ["type"] = "say", ["from"] = ch.Id, ["name"] = ch.Name, ["text"] = text,
                        ["x"] = J.R(ch.X, 2), ["z"] = J.R(ch.Z, 2),
                    }, [.. others, ch.Token]);
                    // Perto da Guardiã, ela responde (mesmo motor de chat do navegador).
                    if (Dist(ch, NpcX, NpcZ) <= TalkRadius) { sayText = text; sayer = ch; }
                    break;
                }
                case "interact":
                {
                    if (Dist(ch, NpcX, NpcZ) > InteractRadius + 1.5)
                    {
                        var r = Ok(ch);
                        r["npc"] = null;
                        r["hint"] = "Ninguém por perto para interagir.";
                        return (200, r);
                    }
                    var npc = _content.Guardia();
                    var greeting = J.Str(npc["greeting"]);
                    if (greeting.Length == 0) greeting = "Sou a Guardiã do Jardim. Cuido deste lugar e acolho quem chega pela Trilha.";
                    Emit(new JsonObject
                    {
                        ["type"] = "npc", ["from"] = "guardia", ["name"] = "Guardiã", ["text"] = greeting, ["to"] = ch.Id,
                    }, [ch.Token]);
                    var res = Ok(ch);
                    res["npc"] = new JsonObject
                    {
                        ["id"] = "guardia", ["name"] = "Guardiã", ["greeting"] = greeting, ["persona"] = J.Str(npc["persona"]),
                    };
                    return (200, res);
                }
                case "dig":
                case "start_job":
                {
                    if (JobCell(data) is not var (cx, cz)) return (400, J.Err("x_z_or_cx_cz_required"));
                    var key = $"{cx},{cz}";
                    if (OutOfWorld(cx, cz)) return (400, J.Err("cell_out_of_world"));
                    var (px, pz) = CellCenter(cx, cz);
                    if (Dist(ch, px, pz) > DigReach)
                        return (400, new JsonObject { ["error"] = "too_far", ["hint"] = "Aproxime-se da célula para iniciar o trabalho." });
                    if (_jobs.FirstOrDefault(j => j.Cell == key) is { } existing)
                        return (409, new JsonObject { ["error"] = "job_exists", ["job"] = JobPublic(existing) });
                    var job = NewJob(ch, cx, cz, key, now);
                    _jobs.Add(job);
                    RecordJob(job);
                    var pub = JobPublic(job);
                    Emit(new JsonObject { ["type"] = "job_started", ["job"] = pub });
                    var res = Ok(ch);
                    res["job"] = pub.DeepClone();
                    return (200, res);
                }
                case "queue":
                case "order_dig":
                {
                    if (JobCell(data) is not var (cx, cz)) return (400, J.Err("x_z_or_cx_cz_required"));
                    if (OutOfWorld(cx, cz)) return (400, J.Err("cell_out_of_world"));
                    var key = $"{cx},{cz}";
                    if (ch.Orders.FirstOrDefault(o => o.Cell == key) is { } dup)
                        return (409, new JsonObject { ["error"] = "order_exists", ["order"] = OrderPublic(dup) });
                    if (_jobs.FirstOrDefault(j => j.Cell == key) is { } existing)
                        return (409, new JsonObject { ["error"] = "job_exists", ["job"] = JobPublic(existing) });
                    if (IsTile(key, JobTypes["dig"].Tile)) return (409, J.Err("already_dug"));
                    if (ch.Orders.Count >= MaxOrders)
                        return (429, new JsonObject { ["error"] = "queue_full", ["max"] = MaxOrders });
                    var order = new Order { Id = "o" + J.Hex(4), Cx = cx, Cz = cz, Cell = key, QueuedAt = now };
                    ch.Orders.Add(order);
                    Emit(new JsonObject
                    {
                        ["type"] = "order_queued", ["order"] = OrderPublic(order), ["queue"] = ch.Orders.Count,
                    }, [ch.Token]);
                    var res = Ok(ch);
                    res["order"] = OrderPublic(order);
                    res["queue"] = QueuePublic(ch);
                    return (200, res);
                }
                case "cancel_order":
                {
                    var oid = J.Str(data["id"]);
                    if (oid == "all")
                    {
                        if (ch.Move is { Kind: "to" } && ch.Orders.Count > 0) ch.Move = null;
                        ch.Orders = [];
                        Emit(new JsonObject { ["type"] = "orders_cleared", ["character"] = CharRef(ch) }, [ch.Token]);
                        var r = Ok(ch);
                        r["queue"] = new JsonArray();
                        return (200, r);
                    }
                    var o = ch.Orders.FirstOrDefault(x => x.Id == oid);
                    if (o is null)
                        return (404, new JsonObject { ["error"] = "no_order", ["hint"] = "Use cancel_order com id da ordem ou 'all'." });
                    ch.Orders.Remove(o);
                    if (ch.Orders.Count == 0 && ch.Move is { Kind: "to" }) ch.Move = null; // ia executar a ordem cancelada
                    Emit(new JsonObject { ["type"] = "order_cancelled", ["order"] = OrderPublic(o) }, [ch.Token]);
                    var res = Ok(ch);
                    res["queue"] = QueuePublic(ch);
                    return (200, res);
                }
                case "stop_job":
                {
                    var job = _jobs.FirstOrDefault(j => j.Owner == ch.Id);
                    var hadOrders = ch.Orders.Count > 0;
                    ch.Orders = []; // parar também esvazia a fila de automação
                    if (hadOrders && ch.Move is { Kind: "to" }) ch.Move = null;
                    if (job is null && !hadOrders) return (404, J.Err("no_job"));
                    if (job is not null)
                    {
                        _jobs.Remove(job);
                        RecordJobCancel(job);
                        Emit(new JsonObject
                        {
                            ["type"] = "job_cancelled", ["job"] = new JsonObject { ["id"] = job.Id, ["cell"] = job.Cell },
                        });
                    }
                    Emit(new JsonObject { ["type"] = "orders_cleared", ["character"] = CharRef(ch) }, [ch.Token]);
                    var res = Ok(ch);
                    res["queue"] = new JsonArray();
                    return (200, res);
                }
                default:
                    return (400, new JsonObject { ["error"] = "unknown_action", ["actions"] = J.StrArr(Actions) });
            }
            if (sayText is null) return (200, Ok(ch));
        }

        // Resposta da Guardiã fora do lock (o modo servidor de IA pode levar segundos).
        var (status, payload) = await _chat.ChatAsync(_content.Guardia(), "game:" + sayer!.Id, ip, sayer.Name, sayText);
        var reply = J.Str(payload["reply"]);
        lock (_lock)
        {
            Emit(new JsonObject
            {
                ["type"] = "npc", ["from"] = "guardia", ["name"] = "Guardiã", ["text"] = reply, ["to"] = sayer.Id,
            }, [sayer.Token, .. others]);
            var res = Ok(sayer);
            res["npcReply"] = reply;
            res["npcSource"] = payload["source"]?.DeepClone();
            res["npcStatus"] = status;
            return (200, res);
        }
    }

    bool IsTile(string key, string type) => _tiles[key] is JsonObject t && J.Str(t["type"]) == type;

    static JsonArray QueuePublic(Character ch) => J.Arr(ch.Orders.Select(o => (JsonNode?)OrderPublic(o)));
}
