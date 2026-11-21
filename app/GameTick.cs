using System.Text.Json.Nodes;

namespace Ultima.Server;

sealed partial class GameWorld
{
    /// <summary>Laço de simulação a 10 Hz em segundo plano; um erro no tick nunca derruba o servidor.</summary>
    public void Start(CancellationToken stop) => _ = Task.Run(async () =>
    {
        var lastSnapshot = 0.0;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(TickSecs));
        while (!stop.IsCancellationRequested)
        {
            try
            {
                lock (_lock)
                {
                    Tick();
                    var now = J.Now;
                    if (now - lastSnapshot >= SnapshotEvery && _chars.Count > 0)
                    {
                        lastSnapshot = now;
                        Emit(new JsonObject
                        {
                            ["type"] = "snapshot",
                            ["characters"] = J.Arr(_chars.Values.Select(c => (JsonNode?)CharPublic(c))),
                            ["jobs"] = J.Arr(_jobs.Select(j => (JsonNode?)JobPublic(j))),
                            ["mapRevision"] = _revision,
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning("tick error: {Type}", ex.GetType().Name);
            }
            try { await timer.WaitForNextTickAsync(stop); } catch (OperationCanceledException) { break; }
        }
    });

    void Tick()
    {
        var now = J.Now;
        var step = WalkSpeed * TickSecs;
        foreach (var ch in _chars.Values)
        {
            var m = ch.Move;
            if (m is null) continue;
            if (m.Kind == "dir")
            {
                if (now >= m.Until) ch.Move = null;
                else (ch.X, ch.Z) = ClampWorld(ch.X + m.Dx * step, ch.Z + m.Dz * step);
            }
            else
            {
                double dx = m.X - ch.X, dz = m.Z - ch.Z, dist = Math.Sqrt(dx * dx + dz * dz);
                if (dist <= step) { ch.X = m.X; ch.Z = m.Z; ch.Move = null; }
                else (ch.X, ch.Z) = ClampWorld(ch.X + dx / dist * step, ch.Z + dz / dist * step);
            }
        }

        // Jobs por tempo de parede: continuam mesmo sem comandos do dono (automação).
        foreach (var job in _jobs.Where(j => now >= j.DoneAt).ToList())
        {
            _jobs.Remove(job);
            _revision++;
            var tile = JobTypes[job.Type].Tile;
            _tiles[job.Cell] = new JsonObject
            {
                ["type"] = tile, ["rev"] = _revision, ["by"] = job.OwnerName, ["at"] = J.R(now, 3),
            };
            MapSave();
            Emit(new JsonObject
            {
                ["type"] = "job_done", ["cell"] = job.Cell, ["tile"] = tile, ["revision"] = _revision, ["by"] = job.OwnerName,
            });
        }

        // Fila de ordens: sem job ativo, a ordem faz o personagem andar até a célula e,
        // chegando, iniciar o trabalho sozinho.
        foreach (var ch in _chars.Values)
        {
            if (ch.Orders.Count == 0 || _jobs.Any(j => j.Owner == ch.Id)) continue;
            var o = ch.Orders[0];
            var (px, pz) = CellCenter(o.Cx, o.Cz);
            if (!o.Walking)
            {
                o.Walking = true;
                ch.Move = new Move { Kind = "to", X = px, Z = pz };
                Emit(new JsonObject
                {
                    ["type"] = "order_walking", ["order"] = OrderPublic(o), ["character"] = CharRef(ch),
                }, [ch.Token]);
            }
            else if (ch.Move is null) // chegou
            {
                string? reason = Dist(ch, px, pz) > DigReach ? "too_far"
                    : _jobs.Any(j => j.Cell == o.Cell) || IsTile(o.Cell, JobTypes["dig"].Tile) ? "cell_busy" : null;
                ch.Orders.RemoveAt(0);
                if (reason is not null)
                {
                    Emit(new JsonObject { ["type"] = "order_failed", ["order"] = OrderPublic(o), ["reason"] = reason }, [ch.Token]);
                    continue;
                }
                var job = NewJob(ch, o.Cx, o.Cz, o.Cell, now);
                _jobs.Add(job);
                MapSave();
                Emit(new JsonObject { ["type"] = "job_started", ["job"] = JobPublic(job), ["order"] = OrderPublic(o) });
            }
        }

        // Sessões ociosas saem (jobs sobrevivem: pertencem ao mapa).
        foreach (var ch in _chars.Values.Where(c => now - c.Last > SessionIdle).ToList())
        {
            _chars.Remove(ch.Id);
            _events.Remove(ch.Token);
            Emit(new JsonObject { ["type"] = "leave", ["character"] = CharRef(ch) });
        }
    }

    /// <summary>Edição em lote do mapa compartilhado (path/water/rock/pit/erase), persistida.</summary>
    public (int, JsonObject) MapEdit(string? token, JsonObject data)
    {
        if (data["edits"] is not JsonArray edits || edits.Count == 0 || edits.Count > 100)
            return (400, J.Err("edits_must_be_array_1_to_100"));
        string[] allowed = ["erase", "path", "pit", "rock", "water"];
        var parsed = new List<(long Cx, long Cz, string Type)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in edits)
        {
            if (node is not JsonObject item || J.IsBool(item["cx"]) || J.IsBool(item["cz"]))
                return (400, J.Err("invalid_edit"));
            var a = J.StrictInt(item["cx"], out var cx);
            var b = J.StrictInt(item["cz"], out var cz);
            if (a == 1 || b == 1) return (400, J.Err("cx_cz_required"));
            if (a == 2 || b == 2) return (400, J.Err("cx_cz_must_be_integers"));
            if (OutOfWorld(cx, cz)) return (400, new JsonObject { ["error"] = "cell_out_of_world", ["cell"] = $"{cx},{cz}" });
            var type = J.Str(item["type"]).Trim().ToLowerInvariant();
            if (!allowed.Contains(type)) return (400, new JsonObject { ["error"] = "invalid_tile_type", ["allowed"] = J.StrArr(allowed) });
            var key = $"{cx},{cz}";
            if (!seen.Add(key)) return (400, new JsonObject { ["error"] = "duplicate_cell", ["cell"] = key });
            parsed.Add((cx, cz, type));
        }

        lock (_lock)
        {
            var ch = Session(token);
            if (ch is null) return (401, J.Err("invalid_token"));
            var busy = seen.Where(k => _jobs.Any(j => j.Cell == k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
            if (busy.Count > 0) return (409, new JsonObject { ["error"] = "cell_has_active_job", ["cells"] = J.StrArr(busy) });
            var now = J.Now;
            _revision++;
            foreach (var (cx, cz, type) in parsed)
            {
                var key = $"{cx},{cz}";
                if (type == "erase") _tiles.Remove(key);
                else _tiles[key] = new JsonObject { ["type"] = type, ["by"] = ch.Name, ["at"] = J.R(now, 3), ["rev"] = _revision };
            }
            MapSave();
            var cells = seen.OrderBy(k => k, StringComparer.Ordinal).ToList();
            Emit(new JsonObject { ["type"] = "map_edited", ["revision"] = _revision, ["cells"] = J.StrArr(cells), ["by"] = ch.Name });
            var tiles = new JsonObject();
            foreach (var k in seen) if (_tiles[k] is JsonNode t) tiles[k] = t.DeepClone();
            return (200, new JsonObject
            {
                ["ok"] = true, ["revision"] = _revision, ["tiles"] = tiles,
                ["erased"] = J.StrArr(cells.Where(k => !_tiles.ContainsKey(k))),
            });
        }
    }
}
