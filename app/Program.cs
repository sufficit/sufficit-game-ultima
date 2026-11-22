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

var content = new ContentStore(Path.Combine(baseDir, "content.json"));
var chat = new NpcChat(log);
var game = new GameWorld(dataDir, content, chat, log);
var net = new Signaling();
var assetsSvc = new WorldAssets(dataDir, game, log);
game.Start(app.Lifetime.ApplicationStopping);

var api = new Api(content, game, net, chat, assetsSvc, new DialogAnalytics(log), log);
app.Run((RequestDelegate)api.HandleAsync);
app.Run();
