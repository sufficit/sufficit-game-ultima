using Ultima.Models.Core;

namespace Ultima.Models.Characters;

/// <summary>
/// Humanoide de 1,75 m — mesmas proporções, cores e partes do protótipo em JavaScript
/// (pernas, tronco, braços, cabeça, sapatos, mãos, olhos, pescoço e cabelo), agora
/// descritas pelo Core com origem nos pés (y = 0). No protótipo antigo a figura era
/// centrada (pés em -H/2); aqui todas as alturas já estão convertidas para a base.
/// Cada cor vira um material (especular 0,08 como no original) e um grupo = uma malha.
/// </summary>
public abstract class VillagerBase : ModelBase
{
    private readonly double[] _shirt, _pants, _skin, _hair;

    protected VillagerBase(double[] shirt, double[] pants, double[] skin, double[] hair)
    {
        _shirt = shirt; _pants = pants; _skin = skin; _hair = hair;
    }

    protected override void Compose(ModelBuilder b)
    {
        const double H = 1.75;                      // PLAYER_HEIGHT do jogo
        const double legH = H * 0.44, torsoH = H * 0.38, armH = H * 0.36, headR = H * 0.07;
        const double legR = H * 0.040, torsoR = H * 0.088, armR = H * 0.026;
        const double legX = torsoR * 0.5, armX = torsoR + armR * 1.3;

        b.Material("pants", color: _pants, specular: 0.08)
         .Material("shirt", color: _shirt, specular: 0.08)
         .Material("skin", color: _skin, specular: 0.08)
         .Material("shoe", color: [0.13, 0.10, 0.09], specular: 0.08)
         .Material("hair", color: _hair, specular: 0.08)
         .Material("eye", color: [0.05, 0.05, 0.06], specular: 0.08);

        // Pernas: cápsulas apoiadas no chão; braços e tronco se apoiam no topo da perna
        // (no original os braços começam exatamente no topo das pernas: torsoH - 0,20·H = armH/2).
        b.Group("legs", "pants")
         .CapsuleOn("leg-l", "", 2 * legR, legH, x: -legX, subdivisions: 4)
         .CapsuleOn("leg-r", "", 2 * legR, legH, x: legX, subdivisions: 4);

        // Braços: nascem exatamente no topo das pernas (ombros do protótipo).
        b.Group("torso", "shirt")
         .CapsuleOn("torso", "leg-l", 2 * torsoR, torsoH, subdivisions: 5)
         .CapsuleOn("arm-l", "leg-l", 2 * armR, armH, x: -armX, subdivisions: 2)
         .CapsuleOn("arm-r", "leg-l", 2 * armR, armH, x: armX, subdivisions: 2);

        // Cabeça, pescoço e mãos: o pescoço se apoia no topo do tronco (no original
        // nascia ~4 cm acima; agora encosta, diferença escondida pela cabeça); a cabeça
        // se apoia no pescoço.
        b.Group("skin", "skin")
         .CylinderOn("neck", "torso", 0, 0.9 * headR, 0.9 * headR, 0.7 * headR, tessellation: 10)
         .SphereAt("head", "neck", 2 * headR, H - 2 * headR, segments: 8)
         .SphereAt("hand-l", "leg-l", 2.6 * armR, legH + torsoH - 0.20 * H - armH / 2 - 1.3 * armR, x: -armX, segments: 8)
         .SphereAt("hand-r", "leg-l", 2.6 * armR, legH + torsoH - 0.20 * H - armH / 2 - 1.3 * armR, x: armX, segments: 8);

        // Sapatos: esferas achatadas (55%) e alongadas (1,9) com a frente para +Z. A
        // escala é embutida nos vértices pelo renderizador, então a base usa o raio
        // escalado: o sapato afunda 9,6 mm no chão como no protótipo.
        b.Group("shoes", "shoe")
         .SphereAt("shoe-l", "", 2.5 * legR, 0.55 * legR - 0.6875 * legR, x: -legX, z: 0.9 * legR, segments: 8, scaleX: 1, scaleY: 0.55, scaleZ: 1.9)
         .SphereAt("shoe-r", "", 2.5 * legR, 0.55 * legR - 0.6875 * legR, x: legX, z: 0.9 * legR, segments: 8, scaleX: 1, scaleY: 0.55, scaleZ: 1.9);

        b.Group("eyes", "eye")
         .SphereAt("eye-l", "head", 0.22 * headR, H - headR + 0.1 * headR - 0.11 * headR, x: -0.36 * headR, z: 0.9 * headR, segments: 6)
         .SphereAt("eye-r", "head", 0.22 * headR, H - headR + 0.1 * headR - 0.11 * headR, x: 0.36 * headR, z: 0.9 * headR, segments: 6);

        // Cabelo: esfera 2,18·headR levemente achatada (0,82) e deslocada para trás
        // (base = centro − raio vertical já escalado).
        b.Group("hair", "hair")
         .SphereAt("hair", "head", 2.18 * headR, H - headR + 0.28 * headR - 1.09 * headR * 0.82, z: -0.14 * headR, segments: 10, scaleX: 1, scaleY: 0.82, scaleZ: 1.02);

        b.Collision(0.25);
    }
}

/// <summary>Personagem do jogador: camisa azul, calça escura e pele clara.</summary>
public sealed class Villager : VillagerBase
{
    public override string Id => "character.villager";
    public override string Name => "Aldeão (jogador)";
    public override string Category => "character";

    public Villager() : base(
        shirt: [0.20, 0.50, 0.92],
        pants: [0.20, 0.22, 0.28],
        skin: [0.90, 0.72, 0.58],
        hair: [0.20, 0.13, 0.08])
    { }
}

/// <summary>Jogador remoto (P2P): mesmas proporções, roupas roxas.</summary>
public sealed class Wanderer : VillagerBase
{
    public override string Id => "character.wanderer";
    public override string Name => "Andarilho (jogador remoto)";
    public override string Category => "character";

    public Wanderer() : base(
        shirt: [0.66, 0.30, 0.82],
        pants: [0.24, 0.18, 0.30],
        skin: [0.88, 0.70, 0.56],
        hair: [0.20, 0.13, 0.08])
    { }
}
