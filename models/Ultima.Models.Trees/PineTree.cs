using Ultima.Models.Core;

namespace Ultima.Models.Trees;

/// <summary>
/// Pinheiro de ~6,3 m: tronco estriado com casca e copa em dois cones com folhagem.
/// Mesma forma que o protótipo em JavaScript, agora descrita pelo Core: o tronco nasce
/// no chão e cada cone da copa se apoia na parte de baixo, então nada flutua.
/// </summary>
public sealed class PineTree : ModelBase
{
    public override string Id => "tree.pine";
    public override string Name => "Pinheiro";
    public override int Version => 1;
    public override string Category => "vegetation";

    protected override void Compose(ModelBuilder b)
    {
        b.Material("bark", texture: "bark", specular: 0.01, bump: 3.2, uScale: 2)
         .Material("foliage", texture: "foliage", specular: 0.02, emissive: new[] { 0.015, 0.045, 0.02 });

        b.Group("trunk", "bark")
         .Cylinder("trunk", diameterBottom: 0.36, diameterTop: 0.22, height: 2.6, tessellation: b.Tess(9))
         .Flute(0.10, 311);

        b.Group("crown", "foliage")
         .CylinderOn("crown-low", restsOn: "trunk", overlap: 0.6, diameterBottom: 3.0, diameterTop: 0, height: 3.2, tessellation: b.Tess(11))
         .CylinderOn("crown-top", restsOn: "crown-low", overlap: 1.1, diameterBottom: 2.1, diameterTop: 0, height: 2.2, tessellation: b.Tess(11))
         .Jitter(0.09, 613)
         .AmbientOcclusion(0.45, 91);

        b.GroundSink(0.25).Collision(0.3);
    }
}
