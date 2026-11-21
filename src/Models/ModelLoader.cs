using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.JSInterop;
using Ultima.Models.Core;

namespace Ultima.Models;

/// <summary>
/// Carrega pacotes de modelos (DLLs C#) em tempo de execução, a partir do catálogo
/// models.json servido pelo próprio site. Para cada pacote: confere SHA-256 e versão do
/// Core, carrega o assembly, instancia cada <see cref="IModel"/>, monta o descritor e o
/// entrega ao renderizador genérico (game.models.register). Falha de um pacote ou de um
/// modelo não derruba os demais: o modelo esperado vira marcador provisório.
/// </summary>
public sealed class ModelLoader(HttpClient http, IJSRuntime js)
{
    /// <summary>Modelos que o mundo atual espera encontrar no catálogo.</summary>
    private static readonly string[] Expected = ["tree.pine", "rock.boulder", "scenery.grass"];

    public List<string> Log { get; } = [];

    public async Task LoadAllAsync()
    {
        var registered = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var catalog = await http.GetFromJsonAsync("models/models.json?v=" + DateTime.UtcNow.Ticks, ModelJsonContext.Default.ModelCatalog)
                          ?? new ModelCatalog();
            foreach (var pkg in catalog.Packages)
            {
                await LoadPackageAsync(pkg, registered);
            }
        }
        catch (Exception ex)
        {
            Note($"catálogo indisponível: {ex.Message}");
        }

        foreach (var id in Expected.Where(id => !registered.Contains(id)))
        {
            Note($"{id}: usando marcador provisório");
            await RegisterAsync(new PlaceholderModel(id).Build(ModelParams.Default), placeholder: true);
        }
        await js.InvokeVoidAsync("game.models.loaderDone", Log);
    }

    private async Task LoadPackageAsync(ModelPackageEntry pkg, HashSet<string> registered)
    {
        try
        {
            if (pkg.CoreApi != ModelCore.ApiVersion) { Note($"{pkg.File}: Core v{pkg.CoreApi} incompatível (jogo v{ModelCore.ApiVersion})"); return; }
            if (!IsSafeRelative(pkg.File)) { Note($"{pkg.File}: caminho recusado"); return; }

            var bytes = await http.GetByteArrayAsync(pkg.File + "?h=" + pkg.Sha256);
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (!string.Equals(hash, pkg.Sha256, StringComparison.Ordinal)) { Note($"{pkg.File}: SHA-256 não confere"); return; }

            var asm = Assembly.Load(bytes);
            foreach (var type in SafeTypes(asm).Where(t => typeof(IModel).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) is not null))
            {
                try
                {
                    var model = (IModel)Activator.CreateInstance(type)!;
                    if (model.RequiredCoreApi != ModelCore.ApiVersion) { Note($"{model.Id}: pede Core v{model.RequiredCoreApi}"); continue; }
                    if (registered.Contains(model.Id)) { Note($"{model.Id}: duplicado, ignorado"); continue; }
                    var descriptor = model.Build(ModelParams.Default);
                    await RegisterAsync(descriptor, placeholder: false);
                    registered.Add(model.Id);
                    Note($"{model.Id} v{model.Version} carregado de {pkg.File}");
                }
                catch (Exception ex)
                {
                    Note($"{type.Name}: falhou ({ex.Message})");
                }
            }
        }
        catch (Exception ex)
        {
            Note($"{pkg.File}: falhou ({ex.Message})");
        }
    }

    private Task RegisterAsync(ModelDescriptor d, bool placeholder) =>
        js.InvokeVoidAsync("game.models.register", ModelJsonContext.Serialize(d), placeholder).AsTask();

    private static IEnumerable<Type> SafeTypes(Assembly asm)
    {
        try { return asm.GetExportedTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }

    private static bool IsSafeRelative(string path) =>
        path.StartsWith("models/", StringComparison.Ordinal) && !path.Contains("..") && !path.Contains("://") && path.EndsWith(".bin", StringComparison.Ordinal);

    private void Note(string message)
    {
        Log.Add(message);
        Console.WriteLine("[modelos] " + message);
    }
}

/// <summary>Marcador provisório (caixa cinza) usado quando um modelo esperado não carrega.</summary>
internal sealed class PlaceholderModel(string id) : ModelBase
{
    public override string Id => id;
    public override string Name => "Marcador provisório";
    public override string Category => "placeholder";

    protected override void Compose(ModelBuilder b)
    {
        b.Material("placeholder", color: [0.6, 0.6, 0.62], emissive: [0.08, 0.08, 0.08])
         .Group("body", "placeholder")
         .Box("body", 0.6, 1.2, 0.6, sink: 0.05);
    }
}
