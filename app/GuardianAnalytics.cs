using System.Text.Json;
using System.Text.Json.Nodes;
using Ultima.Telemetry;

namespace Ultima.Server;

sealed class GuardianAnalytics(string dataDir, ILogger log)
{
    const int RetainedEvents = 8192;
    const int MaxBatchesPerMinute = 120;
    const int MaxBatchesPerClient = 12;
    readonly string _path = Path.Combine(dataDir, "guardian-quality-v1.json");
    readonly object _gate = new();
    readonly Dictionary<string, int> _byIp = new(StringComparer.Ordinal);
    GuardianEvent[]? _events;
    long _minute;
    int _batches;

    public (int Status, JsonObject Body) Post(string ip, JsonObject data)
    {
        GuardianBatch? batch;
        try { batch = data.Deserialize(GuardianTelemetryJson.Default.GuardianBatch); }
        catch (JsonException) { return (400, J.Err("invalid_guardian_events")); }
        if (batch?.Events is not { Length: >= 1 and <= GuardianQuality.BatchSize } events ||
            events.Any(entry => !GuardianQuality.Valid(entry)))
            return (400, J.Err("invalid_guardian_events"));

        lock (_gate)
        {
            var minute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
            if (minute != _minute)
            {
                _minute = minute;
                _batches = 0;
                _byIp.Clear();
            }
            var count = _byIp.GetValueOrDefault(ip);
            if (count >= MaxBatchesPerClient || _batches >= MaxBatchesPerMinute)
                return (429, J.Err("rate_limited"));
            _byIp[ip] = count + 1;
            _batches++;

            try
            {
                var retained = ReadEvents();
                var ids = retained.Select(entry => entry.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var added = events.Where(entry => ids.Add(entry.Id)).ToArray();
                if (added.Length != 0)
                {
                    var snapshot = retained.Concat(added).TakeLast(RetainedEvents).ToArray();
                    WriteEvents(snapshot);
                    _events = snapshot;
                }
                return (200, new JsonObject { ["ok"] = true });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                log.LogWarning("Guardian telemetry storage unavailable: {Type}", ex.GetType().Name);
                return (503, J.Err("guardian_telemetry_unavailable"));
            }
        }
    }

    public (int Status, JsonObject Body) Summary(string build)
    {
        if (build.Length != 0 && !GuardianQuality.BuildValid(build))
            return (400, J.Err("invalid_build"));
        GuardianEvent[] snapshot;
        try { lock (_gate) snapshot = ReadEvents(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            log.LogWarning("Guardian telemetry storage unavailable: {Type}", ex.GetType().Name);
            return (503, J.Err("guardian_telemetry_unavailable"));
        }
        var selected = snapshot.Where(entry => build.Length == 0 ||
            string.Equals(entry.Build, build, StringComparison.OrdinalIgnoreCase)).ToArray();
        var groups = selected.GroupBy(entry => (entry.Build, entry.Engine, entry.Model, entry.Outcome))
            .OrderByDescending(group => group.Count());
        return (200, new JsonObject
        {
            ["schema"] = 1,
            ["retained"] = snapshot.Length,
            ["players"] = CountPlayers(selected),
            ["attributedEvents"] = selected.Count(entry => entry.PlayerUuid is not null),
            ["groups"] = new JsonArray(groups.Select(group => (JsonNode?)new JsonObject
            {
                ["build"] = group.Key.Build,
                ["engine"] = group.Key.Engine,
                ["model"] = group.Key.Model,
                ["outcome"] = group.Key.Outcome,
                ["count"] = group.Count(),
                ["players"] = CountPlayers(group),
                ["meanMs"] = group.Key.Outcome == "dismissed" ? null : JsonValue.Create(
                    Math.Round(group.Average(entry => (double)entry.ElapsedMs))),
            }).ToArray()),
        });
    }

    static int CountPlayers(IEnumerable<GuardianEvent> entries) => entries.Select(entry => entry.PlayerUuid)
        .Where(uuid => uuid is not null).Distinct(StringComparer.OrdinalIgnoreCase).Count();

    GuardianEvent[] ReadEvents()
    {
        if (_events is not null) return _events;
        if (!File.Exists(_path)) return _events = [];
        using var file = File.OpenRead(_path);
        var batch = JsonSerializer.Deserialize(file, GuardianTelemetryJson.Default.GuardianBatch);
        return _events = (batch?.Events ?? []).Where(GuardianQuality.Valid).TakeLast(RetainedEvents).ToArray();
    }

    void WriteEvents(GuardianEvent[] events)
    {
        var pending = _path + ".pending";
        // Publish complete snapshots so a restart cannot acknowledge a partially stored batch.
        using (var file = File.Create(pending))
            JsonSerializer.Serialize(file, new GuardianBatch(events), GuardianTelemetryJson.Default.GuardianBatch);
        File.Move(pending, _path, overwrite: true);
    }
}
