using Ultima.Models.Core;

namespace Ultima.Models.Rocks;

/// <summary>
/// Pedra irregular de ~1,4 m: icosaedro deformado (jitter 0,22) com sombreamento
/// ambiente nos vértices e a textura "rock" (manchas minerais) do Core.
/// Mesma forma do protótipo em JavaScript: a origem fica no CENTRO da pedra
/// (sink = raio, 0,7 m), porque o jogo enterra o centro 0,25 m no terreno ao
/// espalhar as instâncias. <see cref="ModelDescriptor.GroundSink"/> = 0,95 m
/// (raio 0,7 + enterro 0,25) é o afundamento máximo da base em encostas.
/// </summary>
public sealed class Boulder : ModelBase
{
    public override string Id => "rock.boulder";
    public override string Name => "Pedra";
    public override int Version => 1;
    public override string Category => "rock";

    protected override void Compose(ModelBuilder b)
    {
        b.Material("stone", texture: "rock", specular: 0.04, bump: 4.0);

        b.Group("stone", "stone")
         .IcoSphere("stone", diameter: 1.4, subdivisions: 2, sink: 0.7)
         .Jitter(0.22, 733)
         .AmbientOcclusion(0.55, 17);

        b.GroundSink(0.95).Collision(0.7);
    }
}
