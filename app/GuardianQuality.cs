using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ultima.Telemetry;

/// <summary>Versioned vocabulary shared by the browser and the collector. No conversation data.</summary>
internal static class GuardianQuality
{
    public const string Route = "/v1/analytics/guardian";
    public const int BatchSize = 32;

    public static bool BuildValid(string? value) => value == "dev" ||
        value is { Length: >= 7 and <= 64 } && value.All(char.IsAsciiHexDigit);

    public static bool Valid(GuardianEvent? entry) => entry is not null &&
        Guid.TryParseExact(entry.Id, "N", out _) && BuildValid(entry.Build) &&
        (entry.Engine, entry.Model) is (("cpu", "lfm350" or "qwen05tools" or "unknown")
            or ("gpu", "unknown") or ("server", "unknown") or ("shortcut", "none") or ("local", "none")) &&
        entry.Outcome is ("reply" or "proposal" or "generation_failed" or "tool_rejected" or
            "context_failed" or "turn_failed" or "applied" or "no_change" or "apply_failed" or "dismissed") &&
        entry.ElapsedMs >= 0 && (entry.PlayerUuid is null || PlayerIdentity.IsValid(entry.PlayerUuid));
}

/// <summary>Elapsed milliseconds measure the turn or apply operation independently.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record GuardianEvent(string Id, string Build, string Engine, string Model, string Outcome, long ElapsedMs, string? PlayerUuid = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record GuardianBatch(GuardianEvent[] Events);
internal sealed record GuardianReceipt(bool Ok);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(GuardianBatch))]
[JsonSerializable(typeof(GuardianReceipt))]
internal partial class GuardianTelemetryJson : JsonSerializerContext;
