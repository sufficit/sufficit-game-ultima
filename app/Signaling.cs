using System.Text.Json.Nodes;

namespace Ultima.Server;

/// <summary>Sinalização WebRTC P2P: salas, filas por par e long-poll. Os navegadores trocam
/// offer/answer/ice por aqui e depois falam diretamente entre si.</summary>
sealed class Signaling
{
    const double PruneSecs = 30;
    const int QueueCap = 100;
    const double MaxPollWait = 20;

    sealed class Peer { public string Name = ""; public double Last; }

    sealed class Room
    {
        public readonly Dictionary<string, Peer> Peers = new(StringComparer.Ordinal);
        public readonly Dictionary<string, List<JsonObject>> Queues = new(StringComparer.Ordinal);
    }

    readonly object _lock = new();
    readonly Dictionary<string, Room> _rooms = new(StringComparer.Ordinal);
    TaskCompletionSource _changed = NewSignal();

    static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    void Notify()
    {
        var old = _changed;
        _changed = NewSignal();
        old.TrySetResult();
    }

    Room GetRoom(string room)
    {
        if (!_rooms.TryGetValue(room, out var e))
            _rooms[room] = e = new Room();
        return e;
    }

    void Prune(Room e, double now)
    {
        var stale = e.Peers.Where(p => now - p.Value.Last > PruneSecs).Select(p => p.Key).ToList();
        foreach (var pid in stale)
        {
            e.Peers.Remove(pid);
            e.Queues.Remove(pid);
        }
        if (stale.Count > 0) Notify();
    }

    static IEnumerable<string> Others(Room e, string peer) =>
        e.Peers.Keys.Where(p => p != peer).OrderBy(p => p, StringComparer.Ordinal);

    public List<string> Join(string room, string peer, string name)
    {
        lock (_lock)
        {
            var e = GetRoom(room);
            Prune(e, J.Now);
            e.Peers[peer] = new Peer { Name = J.Cut(name, 40), Last = J.Now };
            if (!e.Queues.ContainsKey(peer)) e.Queues[peer] = [];
            Notify();
            return Others(e, peer).ToList();
        }
    }

    public void Leave(string room, string peer)
    {
        lock (_lock)
        {
            if (_rooms.TryGetValue(room, out var e))
            {
                e.Peers.Remove(peer);
                e.Queues.Remove(peer);
                Notify();
            }
        }
    }

    public int Signal(string room, string sender, string? target, string kind, JsonNode? data)
    {
        lock (_lock)
        {
            var e = GetRoom(room);
            Prune(e, J.Now);
            if (!e.Peers.ContainsKey(sender))
            {
                e.Peers[sender] = new Peer { Name = "?", Last = J.Now };
                if (!e.Queues.ContainsKey(sender)) e.Queues[sender] = [];
            }
            var message = new JsonObject { ["from"] = sender, ["kind"] = J.Cut(kind, 24), ["data"] = data?.DeepClone() };
            var targets = target is not null
                ? (e.Peers.ContainsKey(target) ? [target] : new List<string>())
                : e.Peers.Keys.Where(p => p != sender).ToList();
            foreach (var pid in targets)
            {
                if (!e.Queues.TryGetValue(pid, out var q)) e.Queues[pid] = q = [];
                q.Add(message);
                if (q.Count > QueueCap) q.RemoveRange(0, q.Count - QueueCap);
            }
            if (targets.Count > 0) Notify();
            return targets.Count;
        }
    }

    static JsonObject Expired() => new() { ["expired"] = true, ["peers"] = new JsonArray(), ["messages"] = new JsonArray() };

    public async Task<JsonObject> PollAsync(string room, string peer, double wait)
    {
        var deadline = J.Now + Math.Max(0, Math.Min(wait, MaxPollWait));
        while (true)
        {
            Task signal;
            lock (_lock)
            {
                var e = GetRoom(room);
                Prune(e, J.Now);
                if (!e.Peers.ContainsKey(peer)) return Expired();
                var q = e.Queues.GetValueOrDefault(peer);
                if ((q is { Count: > 0 }) || J.Now >= deadline)
                {
                    var messages = J.Arr((q ?? []).Select(m => (JsonNode?)m.DeepClone()));
                    e.Queues[peer] = [];
                    return new JsonObject { ["peers"] = J.StrArr(Others(e, peer)), ["messages"] = messages };
                }
                signal = _changed.Task;
            }
            await Task.WhenAny(signal, Task.Delay(TimeSpan.FromSeconds(Math.Max(0.05, deadline - J.Now))));
        }
    }

    public JsonObject Rooms()
    {
        lock (_lock)
        {
            var now = J.Now;
            var o = new JsonObject();
            foreach (var (room, e) in _rooms)
            {
                Prune(e, now);
                if (e.Peers.Count == 0) continue;
                var peers = new JsonObject();
                foreach (var (pid, info) in e.Peers) peers[pid] = info.Name;
                o[room] = peers;
            }
            return o;
        }
    }
}
