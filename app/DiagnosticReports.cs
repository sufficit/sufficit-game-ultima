using System.Text.Json.Nodes;

namespace Ultima.Server;

sealed class DiagnosticReports(string dataDir, ILogger log)
{
    const int MaxBytes = 1024 * 1024;
    readonly string _path = Path.Combine(dataDir, "diagnostic-reports.jsonl");
    readonly object _gate = new();
    readonly Dictionary<string, int> _byIp = new(StringComparer.Ordinal);
    long _minute;
    int _total;

    public (int Status, JsonObject Body) Post(string ip, JsonObject data)
    {
        var build = J.Str(data["build"]);
        if (build != "dev" && (build.Length is < 7 or > 64 || !build.All(char.IsAsciiHexDigit)))
            return (400, J.Err("invalid_build"));

        var description = Clean(J.Str(data["description"]), 400);
        var renderer = Clean(J.Str(data["renderer"]), 40);
        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var entry = new JsonObject
        {
            ["id"] = id, ["at"] = now.ToString("O"), ["build"] = build,
            ["description"] = description, ["renderer"] = renderer,
            ["gpu"] = Engine(data["gpu"]), ["cpu"] = Engine(data["cpu"]),
        };

        lock (_gate)
        {
            var minute = now.ToUnixTimeSeconds() / 60;
            if (minute != _minute)
            {
                _minute = minute;
                _byIp.Clear();
                _total = 0;
            }
            var count = _byIp.GetValueOrDefault(ip);
            if (count >= 8 || _total >= 120) return (429, J.Err("rate_limited"));
            _byIp[ip] = count + 1;
            _total++;
            try
            {
                if (File.Exists(_path) && new FileInfo(_path).Length >= MaxBytes)
                    File.Move(_path, _path + ".previous", overwrite: true);
                File.AppendAllText(_path, entry.ToJsonString(J.Opts) + "\n");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.LogWarning("Diagnostic report storage unavailable: {Type}", ex.GetType().Name);
                return (503, J.Err("diagnostic_storage_unavailable"));
            }
        }

        log.LogInformation("Diagnostic report {ReportId} accepted for build {Build}", id, build);
        return (201, new JsonObject { ["id"] = id });
    }

    static JsonObject Engine(JsonNode? node)
    {
        var data = node as JsonObject;
        var state = J.Str(data?["state"]);
        if (state is not ("checking" or "unsupported" or "off" or "model" or "downloading"
            or "compiling" or "loading" or "stalled" or "ready" or "thinking" or "error"))
            state = "unknown";
        return new JsonObject
        {
            ["state"] = state,
            ["model"] = Clean(J.Str(data?["model"]), 50),
            ["progress"] = J.TryFloat(data?["progress"], out var progress) && double.IsFinite(progress)
                ? Math.Clamp(progress, 0, 1) : 0,
            ["lastReplyMs"] = J.TryFloat(data?["lastReplyMs"], out var latency) && double.IsFinite(latency)
                ? JsonValue.Create(Math.Clamp(latency, 0, 600_000)) : null,
        };
    }

    static string Clean(string input, int length) =>
        new(input.Where(c => !char.IsControl(c)).Take(length).ToArray());
}
