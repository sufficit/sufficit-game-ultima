using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using static Kit;

/// <summary>Loopback OpenAI-compatible provider for tool-call integration tests.</summary>
sealed class Provider : IDisposable
{
    readonly HttpListener _listener = new();
    readonly ConcurrentQueue<string> _failures = new();

    public Dictionary<string, string?> Settings { get; }

    public Provider()
    {
        var port = FreePort();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        Settings = new Dictionary<string, string?>
        {
            ["NPC_SERVER_AI"] = "on", ["NPC_LLM_URL"] = $"http://127.0.0.1:{port}/chat",
            ["NPC_LLM_KEY"] = "isolado", ["NPC_LLM_MODEL"] = "teste",
        };
        _ = Task.Run(ServeAsync);
    }

    public string[] Failures => [.. _failures];

    async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception) when (!_listener.IsListening) { return; }
            _ = Task.Run(() => AnswerAsync(context));
        }
    }

    async Task AnswerAsync(HttpListenerContext context)
    {
        var response = context.Response;
        try
        {
            string text;
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                text = await reader.ReadToEndAsync();
            var request = JsonNode.Parse(text);
            Require(request?["max_tokens"] is null, "hosted garden inference must not specify a generation cap");
            var messages = Arr(request?["messages"]);
            const string tool = "submit_garden_plan";
            Require(Str(request?["tools"]?[0]?["function"]?["name"]) == tool &&
                Str(request?["tool_choice"]?["function"]?["name"]) == tool, "hosted model must receive a named tool");
            var said = Str(messages[^1]?["content"]);
            Require(Str(messages[0]?["content"]).Contains("prop.lantern", StringComparison.Ordinal), "prompt da Guardiã sem o catálogo");
            if (said == "provedor fora")
            {
                response.StatusCode = 503;
                return;
            }
            var actions = new JsonArray(
                new JsonObject
                {
                    ["action"] = "path", ["mat"] = "dirt", ["r"] = 1,
                    ["points"] = new JsonArray(new JsonObject { ["x"] = 0, ["z"] = -12 }, new JsonObject { ["x"] = 0, ["z"] = -8 }),
                },
                new JsonObject { ["action"] = "place", ["asset"] = "prop.lantern", ["x"] = 2, ["z"] = -12 },
                new JsonObject { ["action"] = "place", ["asset"] = "flora.flowers", ["x"] = -2, ["z"] = -12 });
            if (said == "e o que eu pedi?")
            {
                Require(messages.Any(message => Str(message?["content"]) == "Luminária, flores e trilha"), "histórico sem o pedido anterior");
                actions = [];
            }
            var content = said == "resposta ruim"
                ? "JSON truncado"
                : new JsonObject { ["reply"] = "Trilha, luz e flores.", ["actions"] = actions }.ToJsonString();
            var message = said == "só texto" ? new JsonObject { ["content"] = content } : new JsonObject
            {
                ["tool_calls"] = new JsonArray(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject { ["name"] = tool, ["arguments"] = content }
                })
            };
            var body = Encoding.UTF8.GetBytes(new JsonObject
            {
                ["choices"] = new JsonArray(new JsonObject { ["message"] = message }),
            }.ToJsonString());
            response.StatusCode = 200;
            response.ContentType = "application/json";
            response.ContentLength64 = body.Length;
            await response.OutputStream.WriteAsync(body);
        }
        catch (Failure failure) { _failures.Enqueue(failure.Message); response.StatusCode = 500; }
        catch (Exception error) { _failures.Enqueue(error.Message); response.StatusCode = 500; }
        finally { response.Close(); }
    }

    static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    public void Dispose()
    {
        _listener.Stop();
        _listener.Close();
    }
}
