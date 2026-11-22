using System.Text.Json.Nodes;

namespace Ultima.Server;

/// <summary>
/// t50: registro de conversas dos personagens para análise e melhoria dos diálogos.
/// O cliente enfileira turnos em localStorage e envia lotes periódicos; aqui eles são
/// validados, sanitizados e acumulados em memória com teto (o padrão do mapa: memória
/// + rotação, sem disco). IDs determinísticos permitem deduplicar reenvios — o beacon
/// de pagehide não traz confirmação, então o mesmo lote pode chegar duas vezes.
/// Privacidade: mesma sanitização do nome do jogador — sem controles, sem &lt; &gt;,
/// textos truncados; nada de token, coordenadas ou dados de máquina.
/// </summary>
sealed class DialogAnalytics(ILogger log)
{
    const int MaxTurnsPerBatch = 200;
    const int MaxStore = 20_000;          // teto do buffer (rotação: mais antigo sai)
    const int MaxText = 500;
    const int BatchesPerMinPerIp = 60;    // anti-abuso: acima disso, 429

    public sealed class Turn
    {
        public string Id = "";
        public double T;
        public string Npc = "";
        public string Player = "";
        public string Reply = "";
        public string Source = "";
        public string Model = "";
        public double Ms;
    }

    readonly object _lock = new();
    readonly LinkedList<Turn> _turns = new();                    // rotação O(1)
    readonly Dictionary<string, double> _recentIds = new(StringComparer.Ordinal);  // dedupe por id
    readonly Dictionary<string, List<double>> _rate = new(StringComparer.Ordinal);
    double _lastTrim;

    bool RateOk(string ip, double now)
    {
        var hits = (_rate.GetValueOrDefault(ip) ?? []).Where(t => now - t < 60).ToList();
        _rate[ip] = hits;
        if (_rate.Count > 4000) _rate.Clear();                   // teto grosseiro de memória
        if (hits.Count >= BatchesPerMinPerIp) return false;
        hits.Add(now);
        return true;
    }

    static string Sanitize(string s, int limit) =>
        J.Cut(s.Replace('<', ' ').Replace('>', ' ').Trim(), limit);

    static Turn? ParseTurn(JsonNode? n, double now)
    {
        if (n is not JsonObject o) return null;
        var id = J.Str(o["id"]).Trim();
        if (id.Length is < 3 or > 40) return null;
        var npc = Sanitize(J.Str(o["npc"]), 40);
        var player = Sanitize(J.Str(o["player"]), MaxText);
        var reply = Sanitize(J.Str(o["reply"]), MaxText);
        if (npc.Length == 0 || (player.Length == 0 && reply.Length == 0)) return null;
        J.TryFloat(o["t"], out var t);
        if (!double.IsFinite(t) || t is <= 0 or > 4102444800000) t = now;   // ~ano 2100
        J.TryFloat(o["ms"], out var ms);
        if (!double.IsFinite(ms) || ms < 0) ms = 0;
        return new Turn
        {
            Id = id,
            T = t,
            Npc = npc,
            Player = player,
            Reply = reply,
            Source = Sanitize(J.Str(o["source"]), 60),
            Model = Sanitize(J.Str(o["model"]), 60),
            Ms = Math.Min(ms, 600_000),
        };
    }

    public (int Status, JsonObject Body) Post(string ip, JsonObject data)
    {
        var now = J.Now;
        lock (_lock)
        {
            if (!RateOk(ip, now))
                return (429, new JsonObject { ["error"] = "rate_limited" });
        }
        if (data["turns"] is not JsonArray turns)
            return (400, new JsonObject { ["error"] = "turns_required" });
        if (turns.Count is < 1 or > MaxTurnsPerBatch)
            return (400, new JsonObject { ["error"] = "turns_limit_200" });
        var player = Sanitize(J.Str(data["player"]), 40);
        var build = Sanitize(J.Str(data["build"]), 20);

        var accepted = new List<string>();
        lock (_lock)
        {
            foreach (var n in turns.Take(MaxTurnsPerBatch))
            {
                var turn = ParseTurn(n, now);
                if (turn is null) continue;
                if (_recentIds.TryGetValue(turn.Id, out var seen) && now - seen < 3600) continue;  // duplicado
                _recentIds[turn.Id] = now;
                _turns.AddLast(turn);
                accepted.Add(turn.Id);
            }
            // Dedupe e rotação: mantêm o buffer e o índice de ids sob controle.
            if (now - _lastTrim > 300)
            {
                _lastTrim = now;
                foreach (var stale in _recentIds.Where(kv => now - kv.Value >= 3600).Select(kv => kv.Key).ToList())
                    _recentIds.Remove(stale);
            }
            while (_turns.Count > MaxStore) _turns.RemoveFirst();
        }
        if (accepted.Count == 0)
            return (400, new JsonObject { ["error"] = "no_valid_turns" });
        log.LogInformation("dialog analytics: +{Count} turnos (total {Total})", accepted.Count, _turns.Count);
        return (200, new JsonObject { ["ok"] = true, ["ids"] = J.StrArr(accepted) });
    }

    public JsonObject List(string npc, int limit)
    {
        lock (_lock)
        {
            var q = _turns.AsEnumerable();
            if (npc.Length > 0) q = q.Where(t => t.Npc.Contains(npc, StringComparison.OrdinalIgnoreCase));
            var items = q.TakeLast(Math.Clamp(limit, 1, 500)).ToList();
            return new JsonObject
            {
                ["total"] = _turns.Count,
                ["count"] = items.Count,
                ["turns"] = new JsonArray(items.Select(t => (JsonNode?)new JsonObject
                {
                    ["id"] = t.Id,
                    ["t"] = t.T,
                    ["npc"] = t.Npc,
                    ["player"] = t.Player,
                    ["reply"] = t.Reply,
                    ["source"] = t.Source,
                    ["model"] = t.Model,
                    ["ms"] = t.Ms,
                }).ToArray()),
            };
        }
    }
}
