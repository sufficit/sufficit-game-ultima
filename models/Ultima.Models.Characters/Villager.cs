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

    public override int Version => 4;

    protected override void Compose(ModelBuilder b)
    {
        var body = b.Params.Body ?? Humanoid.ResolveBody(null);
        var height = body.Height;
        var legH = body.HipHeight;
        var torsoH = height * .82 - legH;
        var armH = height * (body.ArmLengthRatio + .01);
        var headR = height * .07;
        var legR = height * .040 * body.Build;
        var torsoR = body.TorsoRadius;
        var armR = body.ArmRadius;
        var legX = torsoR * .5;
        var armX = body.ArmOffset;
        var faceWidth = body.FaceWidth;

        b.Material("pants", color: _pants, specular: 0.08)
         .Material("shirt", color: _shirt, specular: 0.08)
         .Material("skin", color: b.Params.Body?.Skin.ToArray() ?? _skin, specular: 0.08)
         .Material("shoe", color: [0.13, 0.10, 0.09], specular: 0.08)
         .Material("hair", color: b.Params.Body?.Hair.ToArray() ?? _hair, specular: 0.08)
         .Material("eye", color: body.Eyes.ToArray(), specular: 0.08);

        // Pernas: cápsulas apoiadas no chão; braços e tronco se apoiam no topo da perna
        // (no original os braços começam exatamente no topo das pernas: torsoH - 0,20·H = armH/2).
        b.Group("legs", "pants")
         .CapsuleOn("leg-l", "", 2 * legR, legH, x: -legX, subdivisions: 4)
         .CapsuleOn("leg-r", "", 2 * legR, legH, x: legX, subdivisions: 4);

        b.Group("torso", "shirt");
        if (body.WaistCircumference is not null)
        {
            var abdomenHeight = torsoH * .65;
            var waistScale = 2 * body.WaistRadius / abdomenHeight;
            b.SphereAt("abdomen", "leg-l", abdomenHeight, legH, segments: 12,
                scaleX: waistScale, scaleZ: waistScale);
            var chestHeight = torsoH * .68;
            var chestScale = 2 * torsoR / chestHeight;
            b.SphereAt("torso", "abdomen", chestHeight, height * .82 - chestHeight, segments: 12,
                scaleX: chestScale, scaleZ: chestScale);
        }
        else
        {
            b.CapsuleOn("torso", "leg-l", 2 * torsoR, torsoH, subdivisions: 5);
        }
        b.Group("arm-left", "shirt")
         .CapsuleOn("arm-l", "leg-l", 2 * armR, armH, x: -armX, subdivisions: 2);
        b.Group("arm-right", "shirt")
         .CapsuleOn("arm-r", "leg-l", 2 * armR, armH, x: armX, subdivisions: 2);

        b.Group("neck", "skin")
         .CylinderOn("neck", "torso", 0, 0.9 * headR, 0.9 * headR, 0.7 * headR, tessellation: 10);
        b.Group("head", "skin")
         .SphereAt("head", "neck", 2 * headR, height - 2 * headR, segments: 8, scaleX: faceWidth);
        var handBase = body.WristHeight - .01 * height - 1.3 * armR;
        b.Group("hand-left", "skin")
         .SphereAt("hand-l", "torso", 2.6 * armR, handBase, x: -armX, segments: 8);
        b.Group("hand-right", "skin")
         .SphereAt("hand-r", "torso", 2.6 * armR, handBase, x: armX, segments: 8);

        // Sapatos: esferas achatadas (55%) e alongadas (1,9) com a frente para +Z. A
        // escala é embutida nos vértices pelo renderizador, então a base usa o raio
        // escalado: o sapato afunda 9,6 mm no chão como no protótipo.
        b.Group("shoes", "shoe")
         .SphereAt("shoe-l", "", 2.5 * legR, 0.55 * legR - 0.6875 * legR, x: -legX, z: 0.9 * legR, segments: 8, scaleX: 1, scaleY: 0.55, scaleZ: 1.9)
         .SphereAt("shoe-r", "", 2.5 * legR, 0.55 * legR - 0.6875 * legR, x: legX, z: 0.9 * legR, segments: 8, scaleX: 1, scaleY: 0.55, scaleZ: 1.9);

        b.Group("eyes", "eye")
         .SphereAt("eye-l", "head", 0.22 * headR, height - headR + 0.1 * headR - 0.11 * headR, x: -0.36 * headR * faceWidth, z: 0.9 * headR, segments: 6)
         .SphereAt("eye-r", "head", 0.22 * headR, height - headR + 0.1 * headR - 0.11 * headR, x: 0.36 * headR * faceWidth, z: 0.9 * headR, segments: 6);

        // Cabelo: esfera 2,18·headR levemente achatada (0,82) e deslocada para trás
        // (base = centro − raio vertical já escalado).
        b.Group("hair", "hair")
         .SphereAt("hair", "head", 2.18 * headR, height - headR + 0.28 * headR - 1.09 * headR * 0.82, z: -0.14 * headR, segments: 10, scaleX: faceWidth, scaleY: 0.82, scaleZ: 1.02);

        if (body.EarScale > 0)
        {
            var earSize = .45 * headR * body.EarScale;
            var earX = .94 * headR * faceWidth;
            b.Group("ears", "skin")
             .SphereAt("ear-l", "head", earSize, height - 1.30 * headR, x: -earX, segments: 8, scaleY: 1.4)
             .SphereAt("ear-r", "head", earSize, height - 1.30 * headR, x: earX, segments: 8, scaleY: 1.4);
        }

        b.Collision(body.ColliderRadius);
        b.BakeCrowdWalk(legH, height * 0.8);
        b.Action("inspect", new(0.56, 0.17, 0.42, []));
        b.Action("gather", new(0.44, 0.23, 0.84, [new("hit", 0.50, "collect"), new("sound", 0.66, "gather")]));
        b.Action("dig", new(0.50, 0.34, 0.62, [new("hit", 0.50, "collect"), new("sound", 0.62, "dig")]));
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
