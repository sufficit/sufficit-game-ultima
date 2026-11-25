using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Sufficit.Blazor.UI;
using Ultima;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Base visual Sufficit (SUI): componentes, hosts de notificação e o tema do jogo
// (paleta jardim/fantasia publicada como tokens --sui-* pelo SUIThemeProvider).
builder.Services.AddSufficitUI(options => options.Theme = UltimaTheme.Instance);

// HttpClient compartilhado (usado para consumir a API de conteúdo opcional).
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Carregador de modelos 3D em runtime (pacotes C# listados em models/models.json).
builder.Services.AddScoped<Ultima.Models.ModelLoader>();

builder.Services.AddTransient<Ultima.Browser.Backpack>();
builder.Services.AddScoped<Ultima.Browser.GuardianGenius>();
builder.Services.AddTransient<Ultima.Browser.Workshop>();
builder.Services.AddTransient<Ultima.Browser.GameUpdate>();
builder.Services.AddTransient<Ultima.Browser.DialogLog>();
builder.Services.AddTransient<Ultima.Browser.LiveGame>();
builder.Services.AddScoped<Ultima.Browser.WorldTime>();

await builder.Build().RunAsync();
