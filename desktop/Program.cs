using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.StaticFiles;
using Ultima.Desktop;

// A fixed default origin keeps the browser's player identity and save data across upgrades.
const int DefaultPort = 48757;
var port = DefaultPort;
var serveOnly = false;
var offline = false;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--serve-only": serveOnly = true; break;
        case "--offline": offline = true; break;
        case "--port" when i + 1 < args.Length && int.TryParse(args[++i], out port) && port is >= 0 and <= 65535: break;
        case "--help" or "-h":
            Console.WriteLine("Ultima.Desktop [--offline] [--serve-only] [--port 0-65535]");
            return 0;
        default:
            Console.Error.WriteLine("Opção inválida. Use --help.");
            return 2;
    }
}

var root = Path.Combine(AppContext.BaseDirectory, "wwwroot");
if (!File.Exists(Path.Combine(root, "index.html")))
{
    Console.Error.WriteLine("Os arquivos do Ultima precisam estar na pasta wwwroot, ao lado do executável.");
    return 2;
}

var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
{
    ContentRootPath = AppContext.BaseDirectory,
    WebRootPath = root,
});
builder.Configuration.Sources.Clear();
builder.Logging.ClearProviders();
builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, port));
await using var app = builder.Build();
app.Use(async (ctx, next) =>
{
    if (!string.Equals(ctx.Request.Host.Host, "127.0.0.1", StringComparison.Ordinal))
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.XContentTypeOptions = "nosniff";
    await next(ctx);
});
var types = new FileExtensionContentTypeProvider();
types.Mappings[".mjs"] = "text/javascript";
types.Mappings[".bin"] = "application/octet-stream";
types.Mappings[".dat"] = "application/octet-stream";
types.Mappings[".wasm"] = "application/wasm";
types.Mappings[".glb"] = "model/gltf-binary";
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = types });
app.MapMethods("/", ["GET", "HEAD"], SendIndex);
app.MapMethods("/environments", ["GET", "HEAD"], SendIndex);

try
{
    await app.StartAsync();
}
catch (IOException ex)
{
    Console.Error.WriteLine($"Não consegui abrir o Ultima na porta {port}. {ex.Message}");
    return 1;
}

var origin = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
var url = origin + (offline ? "/?live=0&rede=0&telemetry=0&dialogos=0" : "/");
Console.WriteLine($"Ultima aberto em {url}");
using var window = serveOnly ? null : DesktopBrowser.Open(url);
if (window is not null)
{
    try { await window.WaitForExitAsync(app.Lifetime.ApplicationStopping); }
    catch (OperationCanceledException) when (app.Lifetime.ApplicationStopping.IsCancellationRequested) { }
    app.Lifetime.StopApplication();
}
await app.WaitForShutdownAsync();
return 0;

IResult SendIndex() => Results.File(Path.Combine(root, "index.html"), "text/html; charset=utf-8");
