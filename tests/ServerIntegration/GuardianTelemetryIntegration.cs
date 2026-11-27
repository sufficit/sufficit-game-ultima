using System.Text.Json.Nodes;
using static Kit;

internal static class GuardianTelemetryIntegration
{
    private const string Route = "/v1/analytics/guardian";
    private const string Build = "0123456789abcdef";
    private static JsonObject Entry(string outcome, long elapsedMs) => new()
    {
        ["id"] = Guid.NewGuid().ToString("N"), ["build"] = Build, ["engine"] = "cpu",
        ["model"] = "lfm350", ["outcome"] = outcome, ["elapsedMs"] = elapsedMs,
    };
    private static JsonObject Batch(params JsonObject[] events) =>
        new() { ["events"] = new JsonArray(events.Select(entry => entry.DeepClone()).ToArray()) };

    internal static async Task RunAsync(Harness harness)
    {
        var data = harness.TempDir();
        var events = Batch(Entry("proposal", 100), Entry("proposal", 300), Entry("applied", 40));
        using (var server = await harness.Boot(data))
        {
            var bad = Entry("reply", 900);
            bad["message"] = "Unexpected fields are rejected";
            await harness.Json(server.Url, Route, Batch(bad), expect: 400);
            bad.Remove("message");
            bad["model"] = "arbitrary-label";
            await harness.Json(server.Url, Route, Batch(bad), expect: 400);
            await harness.Json(server.Url, Route, Batch(Enumerable.Range(0, 33).Select(_ => Entry("reply", 0)).ToArray()),
                expect: 400);
            await harness.Json(server.Url, Route, events);
            await harness.Json(server.Url, Route, events);
            await VerifySummary(server);
            var empty = await harness.Json(server.Url, Route + "?build=abcdef0");
            Check(empty["groups"] is JsonArray { Count: 0 }, "Build filters isolate deployments", empty);
        }

        using (var server = await harness.Boot(data))
        {
            await harness.Json(server.Url, Route, events);
            await VerifySummary(server);
        }

        async Task VerifySummary(Server server)
        {
            var summary = await harness.Json(server.Url, Route + "?build=" + Build);
            var groups = (JsonArray)summary["groups"]!;
            var proposals = groups.OfType<JsonObject>().Single(group => Str(group["outcome"]) == "proposal");
            Check(Num(summary["retained"]) == 3 && groups.Count == 2 && Num(proposals["count"]) == 2 &&
                Num(proposals["meanMs"]) == 200 && Str(proposals["model"]) == "lfm350",
                "The summary aggregates by model and deduplicates retried batches", summary);
            Check(!summary.ToJsonString().Contains(Str(((JsonArray)events["events"]!)[0]!["id"]), StringComparison.Ordinal),
                "Event identifiers are private to the collector", summary);
        }
    }
}
