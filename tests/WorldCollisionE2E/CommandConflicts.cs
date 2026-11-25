using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static BrowserTest;

/// <summary>
/// O Chromium registra no console "Failed to load resource … 409" mesmo quando a aplicação trata
/// a resposta. Cada POST de ação pose que recebe 409/too_fast do servidor autoriza uma ocorrência
/// dessa mensagem; qualquer outro 409 em /v1/game/command é falha. Substitui
/// tests/game-command-conflicts.cjs e o teste dele em node.
/// </summary>
internal sealed class CommandConflicts
{
    internal static readonly Regex Notice = new(
        @"^Failed to load resource: the server responded with a status of 409 \(Conflict\) <<([^>]+)>>$", RegexOptions.CultureInvariant);
    private static readonly Regex Noise = new(
        "favicon|wllama|React DevTools|WebGPU|webkitExitFullscreen", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private readonly ConcurrentQueue<Task<Observation>> _seen = new();

    internal sealed record Observation(string Url, bool Expected);
    internal sealed record Verdict(string[] Failures, int Tolerated);

    private const string CommandRoute = "/v1/game/command";

    /// <param name="prefix">Caminho da API quando o site encaminha as chamadas ao backend (só no ensaio).</param>
    internal CommandConflicts(IPage page, string prefix = "") => page.Response += (_, response) =>
    {
        if (response.Status == 409 && new Uri(response.Url).AbsolutePath == prefix.TrimEnd('/') + CommandRoute)
            _seen.Enqueue(ReadAsync(response));
    };

    internal static bool IsExpected(string method, string? action, string? error) =>
        method == "POST" && action == "pose" && error == "too_fast";

    private static async Task<Observation> ReadAsync(IResponse response)
    {
        var expected = false;
        try
        {
            var request = response.Request;
            expected = IsExpected(request.Method, request.PostDataJSON()?.GetProperty("action").GetString(),
                (await response.JsonAsync())?.GetProperty("error").GetString());
        }
        catch (Exception) { /* evidência ilegível ou ausente conta como conflito inesperado */ }
        return new(response.Url, expected);
    }

    internal static Verdict Classify(IEnumerable<Observation> seen, IEnumerable<string> errors)
    {
        var remaining = new Dictionary<string, int>(StringComparer.Ordinal);
        var failures = new List<string>();
        foreach (var observation in seen)
        {
            if (observation.Expected) remaining[observation.Url] = remaining.GetValueOrDefault(observation.Url) + 1;
            else failures.Add("HTTP 409 inesperado em /v1/game/command");
        }
        var tolerated = 0;
        foreach (var error in errors)
        {
            var match = Notice.Match(error);
            var url = match.Success ? match.Groups[1].Value : "";
            if (match.Success && remaining.GetValueOrDefault(url) > 0)
            {
                remaining[url]--;
                tolerated++;
            }
            else if (!Noise.IsMatch(error)) failures.Add(error);
        }
        return new([.. failures], tolerated);
    }

    internal async Task<Verdict> ClassifyAsync(IEnumerable<string> errors) =>
        Classify(await Task.WhenAll(_seen), errors);

    /// <summary>Os casos do antigo teste em node, avaliados antes de abrir o navegador.</summary>
    internal static void SelfCheck()
    {
        const string url = "http://localhost/v1/game/command";
        var notice = $"Failed to load resource: the server responded with a status of 409 (Conflict) <<{url}>>";
        var pose = Classify([new Observation(url, IsExpected("POST", "pose", "too_fast"))], [notice, notice]);
        Check(pose.Failures.SequenceEqual(new[] { notice }) && pose.Tolerated == 1, "conflito de pose tolera uma ocorrência no console", pose);
        foreach (var (method, action, error) in new[] { ("GET", "pose", "too_fast"), ("POST", "order", "too_fast"), ("POST", "pose", "busy") })
        {
            var unexpected = Classify([new Observation(url, IsExpected(method, action, error))], []);
            Check(unexpected.Failures.Length == 1 && unexpected.Tolerated == 0,
                $"conflito inesperado falha mesmo sem mensagem no console: {method}/{action}/{error}", unexpected);
        }
    }
}
