using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using static Kit;

internal static class OfficeIntegration
{
    internal static async Task RunAsync(Harness harness)
    {
        const string missions = "/v1/game/environments/office/missions";
        const string events = missions + "/daily-review/events";
        const string author = "test-author-key-only-for-loopback-00";
        const string agent = "test-agent-key-only-for-loopback-000";
        const string definition = """
            {"id":"daily-review","title":"Revisar fila","station":"review",
             "objectives":[{"kind":"task.completed","target":"daily-summary","required":2}]}
            """;
        const string first = """
            {"id":"job-1","kind":"task.completed","target":"daily-summary","agent":"Test agent"}
            """;
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        var path = harness.TempDir();
        var settings = new Dictionary<string, string?>
        {
            ["ULTIMA_MISSIONS_AUTHOR_KEY"] = author,
            ["ULTIMA_MISSIONS_AGENT_KEY"] = agent,
        };

        using (var server = await harness.Boot(path, settings))
        {
            await RequestAsync(server.Url, missions, 401);
            await RequestAsync(server.Url, missions, 403, agent, definition);
            await RequestAsync(server.Url, missions, 400, author, definition.Replace("\"station\":\"review\"", "\"station\":\"missing station\""));
            await RequestAsync(server.Url, missions, 201, author, definition);
            await RequestAsync(server.Url, missions, 409, author, definition);
            const string another = "/v1/game/environments/atelier/missions";
            await RequestAsync(server.Url, another, 201, author, definition.Replace("\"station\":\"review\"", "\"station\":\"atelier-station\""));
            await RequestAsync(server.Url, another + "/daily-review/events", 201, agent, first);
            await RequestAsync(server.Url, events, 422, agent, first.Replace("daily-summary", "other-work"));

            var concurrent = await Task.WhenAll(Enumerable.Range(0, 4)
                .Select(_ => SendAsync(server.Url, events, agent, first)));
            Check(concurrent.Count(item => item.Status == 201) == 1 &&
                concurrent.Count(item => item.Status == 200) == 3 &&
                concurrent.All(item => Num(item.Body["objectives"]![0]!["count"]) == 1),
                "Concurrent retries count once.");
            await RequestAsync(server.Url, events, 409, agent, first.Replace("Test agent", "Another agent"));
        }

        using (var restarted = await harness.Boot(path, settings))
        {
            var board = await RequestAsync(restarted.Url, missions, 200, agent);
            var mission = board["missions"]![0]!;
            var other = await RequestAsync(restarted.Url, "/v1/game/environments/atelier/missions", 200, agent);
            Check(Str(other["missions"]![0]!["station"]) == "atelier-station", "Environment and station IDs are data.");
            Check(Num(mission["objectives"]![0]!["count"]) == 1, "Progress survives restart.", mission);
            await RequestAsync(restarted.Url, events, 200, agent, first);
            var completed = await RequestAsync(restarted.Url, events, 201, agent, first.Replace("job-1", "job-2"));
            Check(completed["complete"]!.GetValue<bool>(), "Two distinct events complete the mission.");
            await RequestAsync(restarted.Url, events, 409, agent, first.Replace("job-1", "job-3"));
        }

        async Task<JsonObject> RequestAsync(Uri url, string route, int expected, string? key = null, string? payload = null)
        {
            var (status, body) = await SendAsync(url, route, key, payload);
            Check(status == expected, $"{route}: expected {expected}, received {status}.", body);
            return body;
        }

        async Task<(int Status, JsonObject Body)> SendAsync(Uri url, string route, string? key, string? payload)
        {
            using var request = new HttpRequestMessage(payload is null ? HttpMethod.Get : HttpMethod.Post, new Uri(url, route));
            if (key is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            if (payload is not null) request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(request);
            Check(response.Headers.CacheControl?.NoStore == true, "Office responses are private.");
            return ((int)response.StatusCode, Obj(JsonNode.Parse(await response.Content.ReadAsStringAsync())));
        }
    }
}
