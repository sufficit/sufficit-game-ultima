using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static BrowserTest;

internal sealed class BrowserServicesFixture(Uri root, byte[] glb)
{
    internal const string Token = "migration-session";
    internal const string QueueKey = "ultima-dialog-queue";
    internal const string AssetId = "migration.good";
    internal const string InvalidId = "migration.bad";
    internal const string Prompt = "Cristal de teste";
    internal static readonly DialogEntry Previous = new("d-legacy", 1700000000000, "Guardiã",
        "Olá", "Bem-vindo", "", "servidor · regras", 23);

    private static readonly Regex BuildMeta = new("""(<meta name="ultima-build" content=")([^"]+)""");
    private readonly Asset _asset = new(AssetId, "good.glb", Convert.ToHexStringLower(SHA256.HashData(glb)), .8);
    private readonly Asset _invalid = new(InvalidId, "bad.glb", new string('0', 64), null);
    private readonly ConcurrentQueue<Command> _commands = new();
    private readonly ConcurrentQueue<DialogBatch> _batches = new();
    private readonly ConcurrentQueue<Upload> _uploads = new();
    private readonly ConcurrentQueue<Generation> _generation = new();
    private volatile bool _rejectBatches = true;

    internal string Api { get; } = new Uri(root, "migration-fixture/").AbsoluteUri.TrimEnd('/');
    internal string? Build { get; private set; }
    internal Command[] Commands => _commands.ToArray();
    internal Upload[] Uploads => _uploads.ToArray();
    internal Generation[] Generations => _generation.ToArray();
    internal DialogBatch[] Batches => _batches.ToArray();
    internal void AcceptBatches() => _rejectBatches = false;

    internal async Task ConfigureAsync(IPage page)
    {
        var previous = JsonSerializer.Serialize(new[] { Previous }, Json);
        await page.AddInitScriptAsync($$"""
            localStorage.setItem('{{QueueKey}}', JSON.stringify({{previous}}));
            Object.defineProperty(navigator, 'clipboard', {
                value: { writeText: async text => { window.migrationCopied = text; } }
            });
            """);
        await page.RouteAsync(Api + "/**", HandleAsync);
        await page.RouteAsync("**/build.json", route => JsonAsync(route, new { build = "fixture-release" }));
        await page.RouteAsync(IsFixtureNavigation, async route =>
        {
            var original = await route.FetchAsync();
            var source = await original.TextAsync();
            var version = BuildMeta.Match(source);
            Build = version.Success ? version.Groups[2].Value : null;
            var html = BuildMeta.Replace(source, m => m.Groups[1].Value + "migration-test", 1);
            await route.FulfillAsync(new() { Response = original, Body = html });
        });
    }

    private bool IsFixtureNavigation(string address)
    {
        var uri = new Uri(address);
        return uri.AbsolutePath == root.AbsolutePath &&
            System.Web.HttpUtility.ParseQueryString(uri.Query)["migrationTest"] == "1";
    }

    private async Task HandleAsync(IRoute route)
    {
        var request = route.Request;
        var url = new Uri(request.Url);
        var path = url.AbsolutePath[new Uri(Api).AbsolutePath.Length..];
        var token = request.Headers.GetValueOrDefault("x-game-token");
        var query = System.Web.HttpUtility.ParseQueryString(url.Query);
        switch (path)
        {
            case "/v1/game/sessions":
                await JsonAsync(route, new { token = Token, character = new { id = "migration-test-player" } });
                break;
            case "/v1/game/events":
                await Task.Delay(500);
                object[] events = query["since"] == "0"
                    ? [new { type = "snapshot", seq = 1, characters = new[] { new { id = "other", name = "Visitante", x = 0, z = 8, yaw = 0 } } },
                       new { type = "say", seq = 2, from = "other", name = "Visitante", text = "Olá do servidor fictício" }]
                    : [];
                await JsonAsync(route, new { events });
                break;
            case "/v1/game/map":
                await JsonAsync(route, new { revision = 1, placements = new[] {
                    new { kind = "place", uid = "migration-map", asset = "prop.stump", x = 4, z = 7, scale = 1 } } });
                break;
            case "/v1/game/command":
                var command = request.PostDataJSON()?.Deserialize<GameCommand>(Json);
                if (command is not null) _commands.Enqueue(new(command.Action, command.Yaw, command.R, token));
                await JsonAsync(route, new { ok = true });
                break;
            case "/v1/assets":
                await JsonAsync(route, new { assets = new[] { _asset, _invalid } });
                break;
            case "/v1/assets/generate":
                _generation.Enqueue(new(token, request.PostDataJSON()));
                await JsonAsync(route, new { asset = _asset with { Id = "migration.generated" } });
                break;
            case "/v1/assets/upload":
                _uploads.Enqueue(new(query["name"], token, request.Headers.GetValueOrDefault("content-type"), request.PostDataBuffer?.Length));
                await JsonAsync(route, new { asset = _asset with { Id = "migration.upload" } });
                break;
            case "/v1/analytics/dialogs":
                var batch = request.PostDataJSON()?.Deserialize<DialogBatch>(Json);
                if (batch is not null) _batches.Enqueue(batch);
                if (_rejectBatches) await route.FulfillAsync(new() { Status = 503 });
                else await JsonAsync(route, new { ok = true, ids = batch?.Turns.Where(turn => turn.Id.Length <= 40).Select(turn => turn.Id).ToArray() ?? [] });
                break;
            default:
                if (path.EndsWith(".glb", StringComparison.Ordinal))
                    await route.FulfillAsync(new() { BodyBytes = glb, ContentType = "model/gltf-binary" });
                else await JsonAsync(route, new { });
                break;
        }
    }

    private static Task JsonAsync(IRoute route, object payload) =>
        route.FulfillAsync(new() { Body = JsonSerializer.Serialize(payload, Json), ContentType = "application/json" });

    internal sealed record DialogEntry(string Id, long T, string Npc, string Player, string Reply, string Model, string Source, int Ms);
    internal sealed record DialogBatch(DialogEntry[] Turns);
    internal sealed record Command(string Action, double? Yaw, double? R, string? Token);
    internal sealed record Upload(string? Name, string? Token, string? Mime, int? Bytes);
    internal sealed record Generation(string? Token, JsonElement? Body);
    private sealed record GameCommand(string Action, double? Yaw, double? R);
    private sealed record Asset(string Id, string File, string Sha256, double? ScaleM);
}
