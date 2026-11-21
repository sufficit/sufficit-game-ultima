using Ultima.Models.Core;

namespace Ultima.Models.Characters;

/// <summary>
/// Guardiã: figura encapuzada dourada — mesmas formas, cores e proporções do protótipo em
/// JavaScript (manto cilíndrico que alarga para baixo, túnica em cápsula escura, rosto e
/// capuz cônico escuro), agora descrita pelo Core com origem nos pés (y = 0). No protótipo
/// a figura era centrada (pés em -H/2); as alturas abaixo já estão convertidas. A única
/// diferença: a cabeça encosta na ponta da túnica (o original deixava 5 cm de vão).
/// </summary>
public sealed class Guardian : ModelBase
{
    public override string Id => "character.guardian";
    public override string Name => "Guardiã";
    public override int Version => 1;
    public override string Category => "character";

    protected override void Compose(ModelBuilder b)
    {
        const double H = 1.7, robeH = H * 0.52, torsoH = H * 0.30, headR = H * 0.075;

        b.Material("gold", color: [0.85, 0.64, 0.20], emissive: [0.10, 0.06, 0.01], specular: 0.2)
         .Material("dark", color: [0.66, 0.48, 0.14], emissive: [0.08, 0.05, 0.01], specular: 0.2)
         .Material("skin", color: [0.92, 0.76, 0.60], specular: 0.2)
         .Material("hood", color: [0.52, 0.36, 0.10], emissive: [0.06, 0.035, 0.008], specular: 0.2);

        // Manto: do chão até a cintura (0,884 m).
        b.Group("robe", "gold")
         .Cylinder("robe", 0.66, 0.30, robeH, tessellation: 12);

        // Túnica: cápsula escura cuja ponta inferior toca o topo do manto.
        b.Group("tunic", "dark")
         .CapsuleOn("torso", "robe", 2 * H * 0.085, torsoH, subdivisions: 3);

        // Rosto: apoiado na ponta da túnica (protótipo: base a 1,445 m; túnica termina a 1,394 m).
        b.Group("skin", "skin")
         .SphereAt("head", "torso", 2 * headR, robeH + torsoH, segments: 8);

        // Capuz: cone nascendo dentro da cabeça, com a base onde o protótipo a tinha
        // (1,64 m), 9 mm abaixo do topo da cabeça.
        b.Group("hood", "hood")
         .CylinderOn("hood", "head", (robeH + torsoH + 2 * headR) - (H + 0.09 - 0.15), 0.34, 0, 0.30, tessellation: 10);

        b.Collision(0.33);
    }
}
