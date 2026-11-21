namespace Ultima.Models.Core;

/// <summary>
/// Descrição de um modelo em dados puros (serializável em JSON). Não depende do motor 3D:
/// o renderizador genérico do jogo (Babylon.js) e o servidor podem ler o mesmo formato.
/// Unidades em metros; origem do modelo = ponto de apoio no chão (y = 0).
/// </summary>
public sealed class ModelDescriptor
{
    public int CoreApi { get; set; } = ModelCore.ApiVersion;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Version { get; set; }
    public string Category { get; set; } = "";
    public List<MaterialDef> Materials { get; set; } = [];
    /// <summary>Grupos de partes; cada grupo vira uma malha (mesmo material) no motor.</summary>
    public List<PartGroup> Groups { get; set; } = [];
    public Bounds Bounds { get; set; } = new();
    /// <summary>Raio de colisão no plano XZ (0 = sem colisão).</summary>
    public double CollisionRadius { get; set; }
    /// <summary>Quanto a base pode afundar no terreno para não deixar vão em encostas.</summary>
    public double GroundSink { get; set; }
}

public sealed class MaterialDef
{
    public string Id { get; set; } = "";
    /// <summary>Textura procedural do Core: "bark", "foliage", "rock", "none".</summary>
    public string Texture { get; set; } = "none";
    /// <summary>Cor difusa RGB 0..1 (multiplica a textura quando houver).</summary>
    public double[] Color { get; set; } = [1, 1, 1];
    public double[] Emissive { get; set; } = [0, 0, 0];
    public double Specular { get; set; } = 0.02;
    public double BumpStrength { get; set; }
    public double UScale { get; set; } = 1;
}

public sealed class PartGroup
{
    public string Id { get; set; } = "";
    public string Material { get; set; } = "";
    public bool CastShadow { get; set; } = true;
    public List<PartDef> Parts { get; set; } = [];
    /// <summary>Deformações aplicadas ao grupo inteiro, em ordem.</summary>
    public List<ModifierDef> Modifiers { get; set; } = [];
}

/// <summary>Forma primitiva já posicionada pelo <see cref="ModelBuilder"/>.</summary>
public sealed class PartDef
{
    public string Id { get; set; } = "";
    /// <summary>"cylinder" (cone se DiameterTop = 0), "sphere", "icosphere", "box" ou "plane".</summary>
    public string Shape { get; set; } = "cylinder";
    public double DiameterBottom { get; set; }
    public double DiameterTop { get; set; }
    public double Diameter { get; set; }
    public double Width { get; set; }
    public double Depth { get; set; }
    public double Height { get; set; }
    public int Tessellation { get; set; } = 12;
    public int Subdivisions { get; set; } = 2;
    /// <summary>Altura da BASE da parte (não do centro), relativa à origem do modelo.</summary>
    public double BaseY { get; set; }
    public double X { get; set; }
    public double Z { get; set; }
    /// <summary>Escala não uniforme da parte (1 = sem escala), aplicada antes da fusão do grupo.</summary>
    public double ScaleX { get; set; } = 1;
    public double ScaleY { get; set; } = 1;
    public double ScaleZ { get; set; } = 1;
    /// <summary>Parte em que esta se apoia ("" = chão).</summary>
    public string RestsOn { get; set; } = "";
}

public sealed class ModifierDef
{
    /// <summary>"jitter" (silhueta orgânica), "flute" (estrias verticais), "ao" (oclusão assada).</summary>
    public string Kind { get; set; } = "";
    public double Amount { get; set; }
    public int Seed { get; set; }
}

public sealed class Bounds
{
    public double MinY { get; set; }
    public double MaxY { get; set; }
    public double Radius { get; set; }
}
