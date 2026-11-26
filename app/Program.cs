using System.Text.Json.Nodes;
using Ultima.Server;

// Backend do Ultima em ASP.NET Core. O endereço de escuta vem de ASPNETCORE_URLS
// (a hospedagem usa a porta 8080); /health responde 200.
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

var app = builder.Build();
var log = app.Logger;

var baseDir = AppContext.BaseDirectory;
var dataDir = Environment.GetEnvironmentVariable("ULTIMA_DATA_DIR");
if (string.IsNullOrWhiteSpace(dataDir))
    dataDir = Path.Combine(baseDir, "data");

var worldId = Environment.GetEnvironmentVariable("ULTIMA_WORLD_ID");
if (!string.IsNullOrWhiteSpace(worldId))
{
    if (worldId.Length > 40 || !worldId.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))
        throw new InvalidOperationException("ULTIMA_WORLD_ID aceita apenas a-z, 0-9 e hífen (até 40 caracteres).");
    dataDir = Path.Combine(dataDir, "worlds", worldId);
}

// O dataDir precisa ser gravável (mapa persistido + assets do mundo). Na plataforma
// run o diretório do app é montado read-only: em vez de falhar com
// UnauthorizedAccessException em runtime, cai para o temporário gravável (emptyDir
// no contêiner) avisando no log — dados não sobrevivem à recriação do pod, e um
// volume dedicado via ULTIMA_DATA_DIR continua sendo a configuração ideal.
try
{
    Directory.CreateDirectory(dataDir);
    var probe = Path.Combine(dataDir, ".write-probe");
    File.WriteAllText(probe, "ok");
    File.Delete(probe);
}
catch (Exception ex)
{
    var fallback = Path.Combine(Path.GetTempPath(), "ultima-data");
    if (!string.IsNullOrWhiteSpace(worldId)) fallback = Path.Combine(fallback, "worlds", worldId);
    log.LogWarning(ex, "dataDir {Dir} não é gravável — usando {Fallback}", dataDir, fallback);
    dataDir = fallback;
}

var content = new ContentStore(Path.Combine(baseDir, "content.json"));
var chat = new NpcChat(log);
var worldStore = new JournalWorldStore(dataDir, log);
var game = new GameWorld(worldStore, content, chat, log, new CharacterProfiles(Path.Combine(dataDir, "character-profiles")));
var net = new Signaling();
var assetsSvc = new WorldAssets(dataDir, game, log);
game.ImportedAssetIds = assetsSvc.Ids;
game.Start(app.Lifetime.ApplicationStopping);
if (int.TryParse(Environment.GetEnvironmentVariable("ULTIMA_ASSET_GC_DAYS"), out var days) && days >= 7)
    _ = assetsSvc.PruneOrphansAsync(TimeSpan.FromDays(days), app.Lifetime.ApplicationStopping);

var api = new Api(content, game, net, chat, assetsSvc, new DialogAnalytics(log), new DiagnosticReports(dataDir, log), log);
var missions = new MissionApi(new MissionStore(Path.Combine(dataDir, "missions.json")),
    Environment.GetEnvironmentVariable("ULTIMA_MISSIONS_AUTHOR_KEY"),
    Environment.GetEnvironmentVariable("ULTIMA_MISSIONS_AGENT_KEY"));
app.Run((RequestDelegate)(context => context.Request.Path.StartsWithSegments(MissionApi.Prefix)
    ? missions.HandleAsync(context, context.Request.Path.Value!.TrimEnd('/'))
    : api.HandleAsync(context)));
app.Run();
