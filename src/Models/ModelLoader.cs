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
    public const string PlayerModelId = "character.villager";

    /// <summary>Modelos que o mundo atual espera encontrar no catálogo.</summary>
    private static readonly string[] Expected = ["tree.pine", "rock.boulder", "scenery.grass", PlayerModelId, "character.wanderer", "character.guardian"];

    /// <summary>Instâncias carregadas por id — permitem reconstruir com outros parâmetros (variação paramétrica).</summary>
    private readonly Dictionary<string, IModel> _models = new(StringComparer.Ordinal);

    public List<string> Log { get; } = [];
    private HumanoidBody? _playerBody;

    internal void ConfigurePlayerBody(HumanoidProfile? profile) =>
        _playerBody = profile is null ? null : Humanoid.ResolveBody(profile);

    private ModelParams ParametersFor(string id) => id == PlayerModelId && _playerBody is not null
        ? new ModelParams { Body = _playerBody }
        : ModelParams.Default;

    public async Task LoadAllAsync()
    {
        var registered = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var catalog = await ReadCatalogAsync();
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

    public async Task<string> DescribeAsync(string id)
    {
        if (!_models.ContainsKey(id))
        {
            try
            {
                var catalog = await ReadCatalogAsync();
                var registered = new HashSet<string>(StringComparer.Ordinal);
                foreach (var package in catalog.Packages)
                {
                    await LoadPackageAsync(package, registered, id);
                    if (registered.Contains(id)) break;
                }
            }
            catch (Exception exception)
            {
                Note($"catálogo indisponível: {exception.Message}");
            }
        }
        var model = _models.TryGetValue(id, out var loaded) ? loaded : new PlaceholderModel(id);
        return ModelJsonContext.Serialize(model.Build(ParametersFor(id)));
    }

    private async Task<ModelCatalog> ReadCatalogAsync() =>
        await http.GetFromJsonAsync("models/models.json?v=" + DateTime.UtcNow.Ticks, ModelJsonContext.Default.ModelCatalog)
        ?? new ModelCatalog();

    private async Task LoadPackageAsync(ModelPackageEntry pkg, HashSet<string> registered, string? onlyId = null)
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
                    if (onlyId is not null && model.Id != onlyId) continue;
                    if (model.RequiredCoreApi != ModelCore.ApiVersion) { Note($"{model.Id}: pede Core v{model.RequiredCoreApi}"); continue; }
                    if (registered.Contains(model.Id)) { Note($"{model.Id}: duplicado, ignorado"); continue; }
                    if (onlyId is null) await RegisterAsync(model.Build(ParametersFor(model.Id)), placeholder: false);
                    _models[model.Id] = model;
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

    /// <summary>
    /// Variação paramétrica (PLAN-PLAYER-ASSETS, fase 2): reconstrói um modelo já carregado
    /// com outro <see cref="ModelParams"/> (seed/detalhe) e devolve o descritor JSON para o
    /// cliente trocar as malhas e re-espalhar — o modelo continua sendo os mesmos dados +
    /// parâmetros, sem gerar malha nova.
    /// </summary>
    public string? Rebuild(string id, int seed, double detail)
    {
        if (!_models.TryGetValue(id, out var model)) return null;
        detail = Math.Clamp(detail, 0.25, 2.0);
        var descriptor = model.Build(new ModelParams
        { Seed = seed, Detail = detail, Body = ParametersFor(id).Body });
        return ModelJsonContext.Serialize(descriptor);
    }

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
