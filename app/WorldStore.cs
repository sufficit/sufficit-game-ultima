using System.Text.Json.Nodes;

namespace Ultima.Server;

/// <summary>Estado materializado do mundo: o que o GameWorld mantém em memória.</summary>
sealed class WorldState
{
    public long OpSeq;                 // ops do journal já consumidas pelo estado
    public int Revision = 1;
    public JsonObject Tiles = new();
    public List<Job> Jobs = [];
    public List<JsonObject> SurfaceEdits = [];   // ops sculpt/paint (replay determinístico no cliente)
    public List<Placement> Placements = [];      // objetos plantados (F6)
}

/// <summary>
/// Persistência do mundo (decisão world-store-v1). Implementações gravam ops e devolvem
/// o estado consolidado; o GameWorld nunca sabe o formato — a troca futura para
/// SQLite/MySQL é implementação nova desta interface, sem redesign do jogo.
/// </summary>
interface IWorldStore
{
    /// <summary>Estado completo (snapshot + replay do journal). Chamado uma vez no boot.</summary>
    WorldState Load();

    /// <summary>Registra uma op (kinds: tile/job/jobDone/jobCancel) no journal.</summary>
    void Append(string kind, JsonObject payload);

    /// <summary>Materializa o estado num snapshot e zera o journal (compaction).</summary>
    void Compact(WorldState state);
}

/// <summary>
/// Arquivo-first: journal append-only (world/world.journal.jsonl, uma op por linha com
/// seq crescente em "op") + snapshot atômico (world/world.snapshot.json, com o opSeq
/// consumido). Replay = snapshot + ops posteriores; compact automático na carga quando
/// o journal passa do limite. Migra um map.json legado na primeira carga sem snapshot.
/// </summary>
sealed class JournalWorldStore : IWorldStore
{
    const int CompactAfterOps = 2000;

    readonly object _lock = new();
    readonly string _dir;
    readonly string _journal;
    readonly string _snapshot;
    readonly string _legacy;
    readonly ILogger _log;
    long _seq;

    public JournalWorldStore(string dataDir, ILogger log)
    {
        _dir = Path.Combine(dataDir, "world");
        _journal = Path.Combine(_dir, "world.journal.jsonl");
        _snapshot = Path.Combine(_dir, "world.snapshot.json");
        _legacy = Path.Combine(dataDir, "map.json");
        _log = log;
    }

    // ---------- formato ----------
    // journal: {"op":N,"kind":"tile","cell":"1,-2","type":"path","by":"Ana","at":1.2,"rev":7}
    //          {"op":N,"kind":"job","id":"j12",...,"startedAt":..,"doneAt":..}
    //          {"op":N,"kind":"jobDone","id":"j12","cell":..,"tile":"pit",..,"rev":..}
    //          {"op":N,"kind":"jobCancel","id":"j12"}
    // snapshot: {"opSeq":N,"revision":R,"tiles":{...},"jobs":[...]}

    public WorldState Load()
    {
        lock (_lock)
        {
            Directory.CreateDirectory(_dir);
            var state = ReadSnapshot() ?? MigrateLegacy() ?? new WorldState();
            long maxSeq = state.OpSeq;
            var lines = 0;
            try
            {
                if (File.Exists(_journal))
                    foreach (var line in File.ReadLines(_journal))
                    {
                        lines++;
                        var seq = Replay(state, line);
                        if (seq > 0 && seq > maxSeq) maxSeq = seq;
                    }
            }
            catch (Exception ex)
            {
                // Linha parcial (crash no meio do append): o que foi lido vale, o resto ignora.
                _log.LogWarning("journal: linha ilegível ignorada: {Msg}", ex.Message);
            }
            // Contador sempre à frente do maior seq já visto — appends nunca reutilizam
            // número de op (o replay pula ops já consumidas por seq, não por posição).
            _seq = Math.Max(_seq, maxSeq);
            if (lines > CompactAfterOps) Compact(state);
            return state;
        }
    }

    public void Append(string kind, JsonObject payload)
    {
        lock (_lock)
        {
            var op = new JsonObject { ["op"] = ++_seq, ["kind"] = kind };
            foreach (var (k, v) in payload) op[k] = v?.DeepClone();
            File.AppendAllText(_journal, op.ToJsonString(J.Opts) + "\n");
        }
    }

    public void Compact(WorldState state)
    {
        lock (_lock)
        {
            WriteSnapshot(state);
            WriteAtomic(_journal, "");   // ops consumidas pelo snapshot
            _log.LogInformation("world: snapshot gravado (opSeq {OpSeq}, rev {Rev})", state.OpSeq, state.Revision);
        }
    }

    void WriteSnapshot(WorldState state)
    {
        var obj = new JsonObject
        {
            ["opSeq"] = state.OpSeq,
            ["revision"] = state.Revision,
            ["tiles"] = state.Tiles.DeepClone(),
            ["jobs"] = J.Arr(state.Jobs.Select(j => (JsonNode?)JobStored(j))),
            ["edits"] = J.Arr(state.SurfaceEdits.Select(e => (JsonNode?)e.DeepClone())),
            ["placements"] = J.Arr(state.Placements.Select(p => (JsonNode?)p.ToJson())),
        };
        WriteAtomic(_snapshot, obj.ToJsonString(J.Opts));
    }

    // ---------- internos ----------

    static JsonObject JobStored(Job j) => new()
    {
        ["id"] = j.Id, ["type"] = j.Type, ["cell"] = j.Cell, ["cx"] = j.Cx, ["cz"] = j.Cz,
        ["owner"] = j.Owner, ["ownerName"] = j.OwnerName,
        ["startedAt"] = j.StartedAt, ["doneAt"] = j.DoneAt,
    };

    static Job JobFrom(JsonObject o)
    {
        var now = J.Now;
        return new Job
        {
            Id = J.Str(o["id"]), Type = J.Str(o["type"]), Cell = J.Str(o["cell"]),
            Owner = J.Str(o["owner"]), OwnerName = J.Str(o["ownerName"]),
            Cx = J.TryInt(o["cx"], out var cx) ? cx : 0, Cz = J.TryInt(o["cz"], out var cz) ? cz : 0,
            StartedAt = J.TryFloat(o["startedAt"], out var s) ? s : now,
            DoneAt = J.TryFloat(o["doneAt"], out var d) ? d : now,
        };
    }

    static void SetTile(WorldState st, string cell, string type, string by, double at, long rev)
    {
        if (type == "erase") st.Tiles.Remove(cell);
        else st.Tiles[cell] = new JsonObject { ["type"] = type, ["by"] = by, ["at"] = J.R(at, 3), ["rev"] = rev };
        if (rev > st.Revision) st.Revision = (int)Math.Min(int.MaxValue, rev);
    }

    /// <summary>Aplica uma linha ao estado; devolve o seq da op (0 se linha inválida/pulada).</summary>
    long Replay(WorldState st, string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return 0;
        JsonObject? o = null;
        try { o = JsonNode.Parse(line) as JsonObject; }
        catch (Exception) { return 0; }
        if (o is null || !J.TryInt(o["op"], out var seq)) return 0;
        if (seq <= st.OpSeq) return seq;
        switch (J.Str(o["kind"]))
        {
            case "tile":
                SetTile(st, J.Str(o["cell"]), J.Str(o["type"]), J.Str(o["by"]),
                    J.TryFloat(o["at"], out var at) ? at : 0, J.TryInt(o["rev"], out var rev) ? rev : st.Revision);
                break;
            case "job":
                var job = JobFrom(o);
                if (st.Jobs.All(j => j.Id != job.Id)) st.Jobs.Add(job);
                break;
            case "jobDone":
                var id = J.Str(o["id"]);
                st.Jobs.RemoveAll(j => j.Id == id);
                SetTile(st, J.Str(o["cell"]), J.Str(o["tile"]), J.Str(o["by"]),
                    J.TryFloat(o["at"], out var at2) ? at2 : 0, J.TryInt(o["rev"], out var rev2) ? rev2 : st.Revision);
                break;
            case "jobCancel":
                st.Jobs.RemoveAll(j => j.Id == J.Str(o["id"]));
                break;
            case "garden_batch":
                if (o["changes"] is JsonArray garden)
                    foreach (var change in garden.OfType<JsonObject>())
                    {
                        switch (J.Str(change["kind"]))
                        {
                            case "place":
                                var placement = Placement.FromJson(change);
                                if (st.Placements.All(p => p.Uid != placement.Uid)) st.Placements.Add(placement);
                                break;
                            case "remove":
                                st.Placements.RemoveAll(p => p.Uid == J.Str(change["uid"]));
                                break;
                            case "paint" or "sculpt":
                                st.SurfaceEdits.Add((JsonObject)change.DeepClone());
                                break;
                        }
                    }
                if (J.TryInt(o["rev"], out var gardenRev) && gardenRev > st.Revision)
                    st.Revision = (int)Math.Min(int.MaxValue, gardenRev);
                break;
            case "surface_batch":
                if (o["changes"] is JsonArray changes)
                    foreach (var change in changes.OfType<JsonObject>())
                        st.SurfaceEdits.Add((JsonObject)change.DeepClone());
                if (J.TryInt(o["rev"], out var batchRev) && batchRev > st.Revision)
                    st.Revision = (int)Math.Min(int.MaxValue, batchRev);
                break;
            case "undo_surface":
                var edit = J.Str(o["edit"]);
                if (edit.Length != 0) st.SurfaceEdits.RemoveAll(change => J.Str(change["edit"]) == edit);
                if (J.TryInt(o["rev"], out var undoRev) && undoRev > st.Revision)
                    st.Revision = (int)Math.Min(int.MaxValue, undoRev);
                break;
            case "sculpt":
            case "paint":
            case "carve":   // base F5b: o cliente aplica (piso absoluto + malhas de rocha)
                st.SurfaceEdits.Add((JsonObject)o.DeepClone());
                if (J.TryInt(o["rev"], out var revE) && revE > st.Revision) st.Revision = (int)Math.Min(int.MaxValue, revE);
                break;
            case "place":
            {
                var p = Placement.FromJson(o);
                if (st.Placements.All(x => x.Uid != p.Uid)) st.Placements.Add(p);
                if (J.TryInt(o["rev"], out var revP) && revP > st.Revision) st.Revision = (int)Math.Min(int.MaxValue, revP);
                break;
            }
            case "remove":
                st.Placements.RemoveAll(p => p.Uid == J.Str(o["uid"]));
                if (J.TryInt(o["rev"], out var revR) && revR > st.Revision) st.Revision = (int)Math.Min(int.MaxValue, revR);
                break;
            // kinds desconhecidos (futuros) são pulados: forward compatibility
        }
        st.OpSeq = seq;
        return seq;
    }

    WorldState? ReadSnapshot()
    {
        try
        {
            if (!File.Exists(_snapshot)) return null;
            var o = JsonNode.Parse(File.ReadAllBytes(_snapshot)) as JsonObject;
            if (o is null) return null;
            var st = new WorldState
            {
                OpSeq = J.TryInt(o["opSeq"], out var seq) ? seq : 0,
                Revision = J.TryInt(o["revision"], out var rev) ? (int)Math.Min(int.MaxValue, rev) : 1,
                Tiles = o["tiles"] is JsonObject t ? (JsonObject)t.DeepClone() : new JsonObject(),
            };
            if (o["jobs"] is JsonArray jobs)
                foreach (var j in jobs.OfType<JsonObject>()) st.Jobs.Add(JobFrom(j));
            if (o["edits"] is JsonArray edits)
                foreach (var e in edits.OfType<JsonObject>()) st.SurfaceEdits.Add((JsonObject)e.DeepClone());
            if (o["placements"] is JsonArray placements)
                foreach (var p in placements.OfType<JsonObject>()) st.Placements.Add(Placement.FromJson(p));
            _seq = Math.Max(_seq, st.OpSeq);
            return st;
        }
        catch (Exception ex)
        {
            _log.LogWarning("snapshot ilegível, sigo com journal: {Msg}", ex.Message);
            return null;
        }
    }

    WorldState? MigrateLegacy()
    {
        try
        {
            if (!File.Exists(_legacy)) return null;
            var o = JsonNode.Parse(File.ReadAllBytes(_legacy)) as JsonObject;
            if (o is null) return null;
            var st = new WorldState
            {
                OpSeq = 0,
                Revision = J.TryInt(o["revision"], out var rev) ? (int)Math.Min(int.MaxValue, rev) : 1,
                Tiles = o["tiles"] is JsonObject t ? (JsonObject)t.DeepClone() : new JsonObject(),
            };
            if (o["jobs"] is JsonArray jobs)
                foreach (var j in jobs.OfType<JsonObject>()) st.Jobs.Add(JobFrom(j));
            // Materializa a base antes de tocar no arquivo legado. Um journal já
            // existente ainda deve ser reaplicado depois dessa migração.
            WriteSnapshot(st);
            File.Move(_legacy, _legacy + ".migrated", overwrite: true);
            _log.LogInformation("world: map.json legado migrado para snapshot ({Tiles} tiles, {Jobs} jobs)",
                st.Tiles.Count, st.Jobs.Count);
            return st;
        }
        catch (Exception ex)
        {
            _log.LogWarning("migração do map.json falhou: {Msg}", ex.Message);
            return ReadSnapshot();
        }
    }

    void WriteAtomic(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }
}
