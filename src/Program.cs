using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Ultima;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// HttpClient compartilhado (usado para consumir a API de conteúdo opcional).
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Carregador de modelos 3D em runtime (pacotes C# listados em models/models.json).
builder.Services.AddScoped<Ultima.Models.ModelLoader>();

await builder.Build().RunAsync();
