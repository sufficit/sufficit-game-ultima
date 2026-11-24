using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Playwright;

internal static class BrowserTest
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static bool MainThread { get; } = Environment.GetEnvironmentVariable("ULTIMA_MODE") == "main";
    internal static string Mode => MainThread ? "main" : "worker";

    internal static string GameUrl(bool forceWebGl = false)
    {
        var site = Environment.GetEnvironmentVariable("ULTIMA_SITE") ?? "https://sufficit.github.io/sufficit-game-ultima/";
        var url = new UriBuilder(site)
        {
            Query = "live=0&rede=0&api=http%3A%2F%2F127.0.0.1%3A19999" +
                (MainThread ? "&render=main" : "") + (forceWebGl || MainThread ? "&gpu=0" : "")
        };
        return url.Uri.AbsoluteUri;
    }

    internal static async Task<string?> CheckBuildAsync(IPage page)
    {
        var build = await page.Locator("meta[name=ultima-build]").GetAttributeAsync("content");
        var expected = Environment.GetEnvironmentVariable("ULTIMA_BUILD");
        Check(string.IsNullOrEmpty(expected) || build == expected, "build publicado esperado", new { build, expected });
        return build;
    }

    internal static ConcurrentQueue<string> TrackErrors(IPage page)
    {
        var errors = new ConcurrentQueue<string>();
        page.PageError += (_, error) => errors.Enqueue(error);
        return errors;
    }

    internal static async Task<T> ReadAsync<T>(IPage page, string expression, object? arg = null, IWorker? worker = null)
    {
        // O conversor de EvaluateAsync exige construtor sem parâmetros para objetos.
        // Uma serialização por lote permite usar registros posicionais no C#.
        var script = $"async arg => JSON.stringify((await ({expression})(arg)) ?? null)";
        var data = worker is null
            ? await page.EvaluateAsync<string>(script, arg)
            : await worker.EvaluateAsync<string>(script, arg);
        return JsonSerializer.Deserialize<T>(data, Json)!;
    }

    internal static Task<T> RenderAsync<T>(IPage page, string expression, object? arg = null) =>
        ReadAsync<T>(page, expression, arg, MainThread ? null : page.Workers[0]);

    internal static int LoadTimeoutMs(int defaultTries) =>
        250 * (int.TryParse(Environment.GetEnvironmentVariable("ULTIMA_WAIT_TRIES"), out var tries) && tries > 0 ? tries : defaultTries);

    internal static async Task WaitAsync(Func<Task<bool>> condition, string description, int timeoutMs = 60_000, int intervalMs = 250)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < timeoutMs)
        {
            if (await condition()) return;
            await Task.Delay(intervalMs);
        }
        throw new InvalidOperationException(description);
    }

    internal static void Check(bool passed, string description, object? evidence = null)
    {
        if (!passed)
            throw new InvalidOperationException(evidence is null ? description : $"{description}: {JsonSerializer.Serialize(evidence, Json)}");
    }

    internal static void Log(object value) => Console.WriteLine(JsonSerializer.Serialize(value, Json));
}
