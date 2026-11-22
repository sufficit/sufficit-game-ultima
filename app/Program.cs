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
    log.LogWarning(ex, "dataDir {Dir} não é gravável — usando {Fallback}", dataDir, fallback);
    dataDir = fallback;
}

var content = new ContentStore(Path.Combine(baseDir, "content.json"));
var chat = new NpcChat(log);
var worldStore = new JournalWorldStore(dataDir, log);
var game = new GameWorld(worldStore, content, chat, log);
var net = new Signaling();
var assetsSvc = new WorldAssets(dataDir, game, log);
game.Start(app.Lifetime.ApplicationStopping);

var api = new Api(content, game, net, chat, assetsSvc, new DialogAnalytics(log), log);
app.Run((RequestDelegate)api.HandleAsync);
app.Run();
