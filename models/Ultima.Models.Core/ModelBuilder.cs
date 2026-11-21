using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ultima.Models.Core;

/// <summary>
/// Construtor comum usado por todos os modelos. Concentra as regras do jogo:
/// a origem do modelo é o chão; cada parte é posicionada pela BASE; uma parte que
/// "se apoia" em outra começa um pouco abaixo do topo dela (encaixe, sem vão).
/// <see cref="Build"/> recusa modelos com partes flutuando.
/// </summary>
public sealed class ModelBuilder
{
    private readonly ModelDescriptor _d;
    private readonly Dictionary<string, PartDef> _parts = new(StringComparer.Ordinal);
    private PartGroup? _group;

    public ModelParams Params { get; }

    internal ModelBuilder(string id, string name, int version, string category, ModelParams p)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ModelValidationException("Modelo sem Id.");
        Params = p;
        _d = new ModelDescriptor { Id = id, Name = name, Version = version, Category = category };
    }

    /// <summary>Tesselação ajustada ao nível de detalhe (mínimo 3).</summary>
    public int Tess(int baseValue) => Math.Max(3, (int)Math.Round(baseValue * Math.Clamp(Params.Detail, 0.25, 2.0)));

    public ModelBuilder Material(string id, string texture = "none", double[]? color = null, double[]? emissive = null,
        double specular = 0.02, double bump = 0, double uScale = 1)
    {
        _d.Materials.Add(new MaterialDef { Id = id, Texture = texture, Color = color ?? [1, 1, 1], Emissive = emissive ?? [0, 0, 0], Specular = specular, BumpStrength = bump, UScale = uScale });
        return this;
    }

    /// <summary>Inicia um grupo de partes que compartilham material (vira uma malha no motor).</summary>
    public ModelBuilder Group(string id, string material, bool castShadow = true)
    {
        if (_d.Materials.All(m => m.Id != material)) throw new ModelValidationException($"Grupo '{id}' usa material inexistente '{material}'.");
        _group = new PartGroup { Id = id, Material = material, CastShadow = castShadow };
        _d.Groups.Add(_group);
        return this;
    }

    public ModelBuilder Jitter(double amount, int seed) => Modifier("jitter", amount, seed);
    public ModelBuilder Flute(double amount, int seed) => Modifier("flute", amount, seed);
    public ModelBuilder AmbientOcclusion(double darkest, int seed) => Modifier("ao", darkest, seed);

    /// <summary>Cilindro (ou cone, com diameterTop = 0) apoiado no chão.</summary>
    public ModelBuilder Cylinder(string id, double diameterBottom, double diameterTop, double height, int tessellation = 12, double sink = 0)
        => Add(new PartDef { Id = id, Shape = "cylinder", DiameterBottom = diameterBottom, DiameterTop = diameterTop, Height = height, Tessellation = tessellation, BaseY = -sink });

    /// <summary>
    /// Cilindro/cone apoiado sobre <paramref name="restsOn"/>: a base fica <paramref name="overlap"/>
    /// metros abaixo do topo da parte de suporte (encaixe garantido).
    /// </summary>
    public ModelBuilder CylinderOn(string id, string restsOn, double overlap, double diameterBottom, double diameterTop, double height, int tessellation = 12)
        => Add(new PartDef { Id = id, Shape = "cylinder", DiameterBottom = diameterBottom, DiameterTop = diameterTop, Height = height, Tessellation = tessellation, BaseY = TopOf(restsOn) - overlap, RestsOn = restsOn });

    public ModelBuilder IcoSphere(string id, double diameter, int subdivisions = 2, double sink = 0)
        => Add(new PartDef { Id = id, Shape = "icosphere", Diameter = diameter, Height = diameter, Subdivisions = subdivisions, BaseY = -sink });

    /// <summary>
    /// Cápsula (cilindro com pontas arredondadas) apoiada sobre <paramref name="restsOn"/>
    /// (ou no chão, com restsOn vazio), com afastamento lateral opcional — usada em
    /// personagens (tronco, pernas, braços). <paramref name="overlap"/> desce a base para
    /// dentro do suporte, como em <see cref="CylinderOn"/>.
    /// </summary>
    public ModelBuilder CapsuleOn(string id, string restsOn, double diameter, double height, double x = 0, double z = 0, double overlap = 0, int subdivisions = 4)
        => Add(new PartDef { Id = id, Shape = "capsule", DiameterBottom = diameter, DiameterTop = diameter, Height = height, Subdivisions = subdivisions, BaseY = (restsOn.Length == 0 ? 0.0 : TopOf(restsOn)) - overlap, X = x, Z = z, RestsOn = restsOn });

    /// <summary>
    /// Esfera posicionada por X/Z com base em <paramref name="baseY"/> (altura absoluta da
    /// base), apoiada em <paramref name="restsOn"/> (chão se vazio) — detalhes de rosto,
    /// mãos e cabelo de personagens.
    /// </summary>
    public ModelBuilder SphereAt(string id, string restsOn, double diameter, double baseY, double x = 0, double z = 0, int segments = 8, double scaleX = 1, double scaleY = 1, double scaleZ = 1)
        => Add(new PartDef { Id = id, Shape = "sphere", Diameter = diameter, Height = diameter, Tessellation = segments, BaseY = baseY, X = x, Z = z, ScaleX = scaleX, ScaleY = scaleY, ScaleZ = scaleZ, RestsOn = restsOn });

    /// <summary>
    /// Par de planos finos cruzados a 90° (vira um quad no motor): base no chão,
    /// usado para vegetação com alfa (ex.: tufos de grama). A base do primeiro plano
    /// fica em <paramref name="baseY"/> (0 = chão); o segundo é espelhado em X.
    /// </summary>
    public ModelBuilder CrossPlane(string id, double width, double height, double baseY = 0, double depth = 0.02, int tessellation = 2)
    {
        Add(new PartDef { Id = id + "-a", Shape = "plane", Width = width, Height = height, Depth = depth, Tessellation = tessellation, BaseY = baseY });
        Add(new PartDef { Id = id + "-b", Shape = "plane", Width = depth, Height = height, Depth = width, Tessellation = tessellation, BaseY = baseY });
        return this;
    }

    public ModelBuilder Box(string id, double width, double height, double depth, double sink = 0)
        => Add(new PartDef { Id = id, Shape = "box", Width = width, Height = height, Depth = depth, BaseY = -sink });

    public ModelBuilder Collision(double radius) { _d.CollisionRadius = Math.Max(0, radius); return this; }
    public ModelBuilder GroundSink(double meters) { _d.GroundSink = Math.Max(0, meters); return this; }

    /// <summary>Altura do topo de uma parte já adicionada.</summary>
    public double TopOf(string partId) =>
        _parts.TryGetValue(partId, out var p) ? p.BaseY + p.Height : throw new ModelValidationException($"Parte de apoio '{partId}' não existe.");

    private ModelBuilder Modifier(string kind, double amount, int seed)
    {
        Current().Modifiers.Add(new ModifierDef { Kind = kind, Amount = amount, Seed = seed });
        return this;
    }

    private ModelBuilder Add(PartDef part)
    {
        if (!_parts.TryAdd(part.Id, part)) throw new ModelValidationException($"Parte duplicada '{part.Id}'.");
        Current().Parts.Add(part);
        return this;
    }

    private PartGroup Current() => _group ?? throw new ModelValidationException("Chame Group(...) antes de adicionar partes.");

    internal ModelDescriptor Build()
    {
        if (_d.Groups.Count == 0 || _d.Groups.All(g => g.Parts.Count == 0)) throw new ModelValidationException($"Modelo '{_d.Id}' sem partes.");
        const double eps = 1e-6;
        double minY = double.MaxValue, maxY = double.MinValue, radius = 0;
        foreach (var p in _parts.Values)
        {
            if (p.Height <= 0) throw new ModelValidationException($"Parte '{p.Id}' com altura inválida.");
            // Regra comum: nada flutua. Apoiada no chão => base <= 0; sobre outra parte => base <= topo do suporte.
            var support = p.RestsOn.Length == 0 ? 0.0 : TopOf(p.RestsOn);
            if (p.BaseY > support + eps)
                throw new ModelValidationException($"Parte '{p.Id}' flutua {p.BaseY - support:0.###} m acima de '{(p.RestsOn.Length == 0 ? "chão" : p.RestsOn)}'.");
            minY = Math.Min(minY, p.BaseY);
            maxY = Math.Max(maxY, p.BaseY + p.Height);
            var r = Math.Max(Math.Max(p.DiameterBottom, p.DiameterTop), Math.Max(p.Diameter, Math.Max(p.Width, p.Depth))) / 2;
            radius = Math.Max(radius, Math.Sqrt(p.X * p.X + p.Z * p.Z) + r);
        }
        _d.Bounds = new Bounds { MinY = minY, MaxY = maxY, Radius = radius };
        return _d;
    }
}

/// <summary>Serialização do descritor (gerada em compilação: segura com trimming no WebAssembly).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ModelDescriptor))]
[JsonSerializable(typeof(ModelCatalog))]
public sealed partial class ModelJsonContext : JsonSerializerContext
{
    public static string Serialize(ModelDescriptor d) => JsonSerializer.Serialize(d, Default.ModelDescriptor);
}

/// <summary>Catálogo models.json: lista dos pacotes de modelos que o jogo pode carregar.</summary>
public sealed class ModelCatalog
{
    public int CoreApi { get; set; }
    public List<ModelPackageEntry> Packages { get; set; } = [];
}

public sealed class ModelPackageEntry
{
    /// <summary>Caminho relativo ao site, ex.: "models/Ultima.Models.Trees.dll".</summary>
    public string File { get; set; } = "";
    /// <summary>SHA-256 do arquivo em hexadecimal minúsculo (obrigatório).</summary>
    public string Sha256 { get; set; } = "";
    public int CoreApi { get; set; }
}
