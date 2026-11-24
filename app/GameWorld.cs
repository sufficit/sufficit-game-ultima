using System.Text.Json.Nodes;

namespace Ultima.Server;

sealed class Move { public string Kind = ""; public double Dx, Dz, Until, X, Z; }

sealed class Order
{
    public string Id = "", Type = "dig", Cell = "";
    public long Cx, Cz;
    public double QueuedAt;
    public bool Walking;
}

sealed class Job
{
    public string Id = "", Type = "dig", Cell = "", Owner = "", OwnerName = "";
    public long Cx, Cz;
    public double StartedAt, DoneAt;
}

sealed class Character
{
    public string Id = "", Name = "", Token = "";
    public readonly string EditOwner = J.Hex(16);
    public double X, Z = 8, Yaw, Last, Since;
    public double? LastPose;
    public Move? Move;
    public List<double> Cmds = [];
    public List<Order> Orders = [];
}

/// <summary>Núcleo autoritativo do jogo (API-first): sessões, movimento a 10 Hz, eventos
/// long-poll, mapa persistido editável, jobs de terreno por tempo de parede e fila de ordens.
/// Constantes espelham o cliente (game.js): WORLD_HALF 48, WALK_SPEED 3.5, Guardiã (0,-14).</summary>
sealed partial class GameWorld
{
    public const double TickSecs = 0.1, WorldHalf = 48, WalkSpeed = 3.5, CellSize = 4;
    const double SayRadius = 20, SessionIdle = 600, SnapshotEvery = 2, MaxPollWait = 20;
    const int MaxSessions = 200, CmdPer10s = 80, EventCap = 300, MaxOrders = 8;
    const double NpcX = 0, NpcZ = -14, TalkRadius = 4.5, InteractRadius = 3.0;
    static readonly double DigReach = CellSize * 1.5 + 1.0;
    static readonly Dictionary<string, (double Duration, string Tile, string Label)> JobTypes = new()
    {
        ["dig"] = (30.0, "pit", "escavação"),
    };

    readonly object _lock = new();
    readonly Dictionary<string, Character> _chars = new(StringComparer.Ordinal);
    readonly Dictionary<string, List<JsonObject>> _events = new(StringComparer.Ordinal);
    long _seq;
    int _revision = 1;
    JsonObject _tiles = new();
    List<Job> _jobs = [];
    List<JsonObject> _edits = [];        // ops sculpt/paint (F4) — replay no cliente
    List<Placement> _placements = [];    // objetos plantados (F6)
    readonly HashSet<string> _visitedChunks = new(StringComparer.Ordinal);   // F3: cristalização
    TaskCompletionSource _changed = NewSignal();
    readonly double _start = J.Now;
    readonly IWorldStore _store;
    readonly ContentStore _content;
    readonly NpcChat _chat;
    readonly ILogger _log;

    public GameWorld(IWorldStore store, ContentStore content, NpcChat chat, ILogger log)
    {
        _store = store;
        _content = content;
        _chat = chat;
        _log = log;
        MapLoad();
    }

    public double Uptime => J.R(J.Now - _start, 1);

    static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    // ---------- persistência (journal append-only atrás da IWorldStore; decisão world-store-v1) ----------
    void MapLoad()
    {
        var st = _store.Load();
        _revision = st.Revision;
        _tiles = st.Tiles;
        _jobs = st.Jobs;
        _edits = st.SurfaceEdits;
        _placements = st.Placements;
        // F3: chunks já cristalizados voltam marcados (ops carregam a tag "chk").
        foreach (var e in _edits)
        {
            var chk = J.Str(e["chk"]);
            if (chk.Length > 0) _visitedChunks.Add(chk);
        }
        foreach (var pl in _placements)
        {
            var chk = J.Str(pl.By);
            if (chk.StartsWith("chunk ", StringComparison.Ordinal)) _visitedChunks.Add(chk[6..]);
        }
    }

    // Ops do journal — quem muta o mundo registra a op correspondente (dentro do _lock).
    static JsonObject JobOp(Job j) => new()
    {
        ["id"] = j.Id, ["type"] = j.Type, ["cell"] = j.Cell, ["cx"] = j.Cx, ["cz"] = j.Cz,
        ["owner"] = j.Owner, ["ownerName"] = j.OwnerName, ["startedAt"] = j.StartedAt, ["doneAt"] = j.DoneAt,
    };

    void RecordTile(string cell, string type, string by, double at) =>
        _store.Append("tile", new JsonObject { ["cell"] = cell, ["type"] = type, ["by"] = by, ["at"] = J.R(at, 3), ["rev"] = _revision });

    void RecordJob(Job job) => _store.Append("job", JobOp(job));

    void RecordJobDone(Job job, string tile, double at) =>
        _store.Append("jobDone", new JsonObject
        {
            ["id"] = job.Id, ["cell"] = job.Cell, ["tile"] = tile, ["by"] = job.OwnerName,
            ["at"] = J.R(at, 3), ["rev"] = _revision,
        });

    void RecordJobCancel(Job job) => _store.Append("jobCancel", new JsonObject { ["id"] = job.Id });

    // ---------- eventos ----------
    void Emit(JsonObject ev, IEnumerable<string>? targets = null)
    {
        var e = new JsonObject { ["seq"] = ++_seq, ["t"] = J.R(J.Now, 3) };
        foreach (var (k, v) in ev) e[k] = v?.DeepClone();
        foreach (var token in (targets ?? _events.Keys).ToList())
            if (_events.TryGetValue(token, out var q))
            {
                q.Add(e);
                if (q.Count > EventCap) q.RemoveRange(0, q.Count - EventCap);
            }
        var old = _changed;
        _changed = NewSignal();
        old.TrySetResult();
    }

    // ---------- formas públicas ----------
    static JsonObject CharPublic(Character c) => new()
    {
        ["id"] = c.Id, ["name"] = c.Name, ["x"] = J.R(c.X, 2), ["z"] = J.R(c.Z, 2),
        ["yaw"] = J.R(c.Yaw, 3), ["moving"] = c.Move is not null, ["orderCount"] = c.Orders.Count,
    };

    static JsonObject CharRef(Character c) => new() { ["id"] = c.Id, ["name"] = c.Name };

    static JsonObject JobPublic(Job j)
    {
        var total = JobTypes[j.Type].Duration;
        var elapsed = Math.Max(0, Math.Min(total, J.Now - j.StartedAt));
        return new JsonObject
        {
            ["id"] = j.Id, ["type"] = j.Type, ["cell"] = j.Cell, ["cx"] = j.Cx, ["cz"] = j.Cz,
            ["owner"] = j.Owner, ["ownerName"] = j.OwnerName,
            ["progress"] = J.R(elapsed / total, 4), ["etaSec"] = J.R(Math.Max(0, total - elapsed), 1),
        };
    }

    static JsonObject OrderPublic(Order o) => new()
    {
        ["id"] = o.Id, ["type"] = o.Type, ["cx"] = o.Cx, ["cz"] = o.Cz,
        ["cell"] = $"{o.Cx},{o.Cz}", ["queuedAt"] = J.R(o.QueuedAt, 3), ["walking"] = o.Walking,
    };

    static JsonObject NpcInfo() => new()
    {
        ["id"] = "guardia", ["name"] = "Guardiã", ["x"] = NpcX, ["z"] = NpcZ,
        ["talkRadius"] = TalkRadius, ["interactRadius"] = InteractRadius,
    };

    public static JsonObject WorldInfo()
    {
        var types = new JsonObject();
        foreach (var (k, v) in JobTypes) types[k] = new JsonObject { ["duration"] = v.Duration, ["tile"] = v.Tile };
        return new JsonObject
        {
            ["half"] = WorldHalf, ["cell"] = CellSize, ["walkSpeed"] = WalkSpeed,
            ["tick"] = TickSecs, ["npc"] = NpcInfo(), ["jobTypes"] = types,
        };
    }

    JsonObject MapPublic() => new()
    {
        ["revision"] = _revision,
        ["tiles"] = _tiles.DeepClone(),
        ["jobs"] = J.Arr(_jobs.Select(j => (JsonNode?)JobPublic(j))),
        // Edição de mundo (F4/F6): ops de pincel para o replay do terreno e objetos plantados.
        ["edits"] = J.Arr(_edits.Select(e => (JsonNode?)e.DeepClone())),
        ["placements"] = J.Arr(_placements.Select(p => (JsonNode?)p.ToJson())),
    };

    // ---------- auxiliares ----------
    Character? Session(string? token) =>
        string.IsNullOrEmpty(token) ? null : _chars.Values.FirstOrDefault(c => c.Token == token);

    // Expostos para o catálogo de assets do mundo (WorldAssets): sessão válida e nome
    // do dono, para creditar uploads/gerações sem duplicar a busca de sessão.
    public bool HasSession(string? token) => Session(token) is not null;
    public string? SessionName(string? token) => Session(token)?.Name;

    static (long, long) CellOf(double x, double z) =>
        ((long)Math.Round(x / CellSize, MidpointRounding.ToEven), (long)Math.Round(z / CellSize, MidpointRounding.ToEven));

    static (double, double) CellCenter(long cx, long cz) => (cx * CellSize, cz * CellSize);

    static double Dist(Character c, double x, double z) => Math.Sqrt((c.X - x) * (c.X - x) + (c.Z - z) * (c.Z - z));

    // F3 — mundo expansível: andar até ±WalkBound (o gerador do cliente dá montanhas
    // naturais além do vale); tiles de overlay ficam no núcleo jogável (±MapBound).
    const double WalkBound = 400;
    const long MapCells = 96;   // ±384 m em células de 4 m

    static (double, double) ClampWorld(double x, double z) =>
        (Math.Clamp(x, -WalkBound, WalkBound), Math.Clamp(z, -WalkBound, WalkBound));

    static bool OutOfWorld(long cx, long cz) => Math.Abs(cx) > MapCells || Math.Abs(cz) > MapCells;

    List<string> NearbyTokens(Character ch, double radius) =>
        _chars.Values.Where(c => Dist(c, ch.X, ch.Z) <= radius).Select(c => c.Token).ToList();

    Job NewJob(Character ch, long cx, long cz, string key, double now) => new()
    {
        Id = "j" + J.Hex(4), Type = "dig", Cell = key, Cx = cx, Cz = cz, Owner = ch.Id, OwnerName = ch.Name,
        StartedAt = now, DoneAt = now + JobTypes["dig"].Duration,
    };

    // ---------- sessões e leitura ----------
    public (int, JsonObject) NewSession(string rawName)
    {
        var now = J.Now;
        var name = J.Clean(rawName, 40);
        if (name.Length == 0) name = "Viajante";
        lock (_lock)
        {
            if (_chars.Count >= MaxSessions) return (503, J.Err("busy"));
            var taken = _chars.Values.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
            var baseName = name;
            for (var n = 2; taken.Contains(name); n++) name = $"{baseName} #{n}";
            var ch = new Character
            {
                Id = "c" + J.Hex(4), Name = name, Token = J.Hex(16), X = 0, Z = 8, Last = now, Since = now,
            };
            _chars[ch.Id] = ch;
            _events[ch.Token] = [];
            Emit(new JsonObject { ["type"] = "join", ["character"] = CharPublic(ch) });
            return (200, new JsonObject
            {
                ["token"] = ch.Token, ["character"] = CharPublic(ch), ["world"] = WorldInfo(), ["eventsFrom"] = _seq,
            });
        }
    }

    public JsonObject Sessions()
    {
        lock (_lock)
            return new JsonObject
            {
                ["characters"] = J.Arr(_chars.Values.Select(c => (JsonNode?)CharPublic(c))),
                ["count"] = _chars.Count,
            };
    }

    public (int, JsonObject) State(string? token)
    {
        lock (_lock)
        {
            var ch = Session(token);
            if (ch is null) return (401, J.Err("invalid_token"));
            return (200, new JsonObject
            {
                ["now"] = J.R(J.Now, 3),
                ["world"] = WorldInfo(),
                ["me"] = CharPublic(ch),
                ["characters"] = J.Arr(_chars.Values.Select(c => (JsonNode?)CharPublic(c))),
                ["npc"] = NpcInfo(),
                ["map"] = MapPublic(),
                ["orders"] = J.Arr(ch.Orders.Select(o => (JsonNode?)OrderPublic(o))),
                ["eventsFrom"] = _seq,
            });
        }
    }

    public JsonObject Map() { lock (_lock) return MapPublic(); }

    public async Task<(int, JsonObject)> EventsAsync(string? token, long since, double wait)
    {
        var deadline = J.Now + Math.Max(0, Math.Min(wait, MaxPollWait));
        while (true)
        {
            Task signal;
            lock (_lock)
            {
                var ch = Session(token);
                if (ch is null) return (401, J.Err("invalid_token"));
                var evs = _events[ch.Token].Where(e => e["seq"]!.GetValue<long>() > since).ToList();
                if (evs.Count > 0 || J.Now >= deadline)
                    return (200, new JsonObject
                    {
                        ["events"] = J.Arr(evs.Select(e => (JsonNode?)e.DeepClone())),
                        ["seq"] = evs.Count > 0 ? evs[^1]["seq"]!.GetValue<long>() : since,
                    });
                signal = _changed.Task;
            }
            await Task.WhenAny(signal, Task.Delay(TimeSpan.FromSeconds(Math.Max(0.05, deadline - J.Now))));
        }
    }

    public (int, JsonObject) Leave(string? token)
    {
        lock (_lock)
        {
            var ch = Session(token);
            if (ch is null) return (401, J.Err("invalid_token"));
            _chars.Remove(ch.Id);
            _events.Remove(ch.Token);
            Emit(new JsonObject { ["type"] = "leave", ["character"] = CharRef(ch) });
            return (200, new JsonObject { ["ok"] = true });
        }
    }
}
