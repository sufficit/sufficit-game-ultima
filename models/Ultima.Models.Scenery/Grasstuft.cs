using Ultima.Models.Core;

namespace Ultima.Models.Scenery;

/// <summary>
/// Tufo de grama (~0,8 × 0,45 m): par de planos cruzados com a base no chão e a
/// textura "grass" (lâminas desenhadas com alfa) do renderizador. Mesma forma do
/// protótipo em JavaScript (dois planos cruzados a 90°): a origem fica no ponto de
/// apoio, então as lâminas nascem rente ao terreno em qualquer escala.
/// </summary>
public sealed class Grasstuft : ModelBase
{
    public override string Id => "scenery.grass";
    public override string Name => "Tufo de grama";
    public override int Version => 1;
    public override string Category => "vegetation";

    protected override void Compose(ModelBuilder b)
    {
        b.Material("grass", texture: "grass", specular: 0);

        // Sem sombra própria: vegetação rasteira translúcida (igual ao protótipo).
        b.Group("tuft", "grass", castShadow: false)
         .CrossPlane("blades", width: 0.8, height: 0.45);
    }
}
