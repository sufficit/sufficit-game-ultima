using Ultima.Models.Characters;
using Ultima.Models.Core;
using Ultima.Terrain;

internal static class HumanoidBodyChecks
{
    internal static void Run(Action<string, bool> check)
    {
        var standard = Humanoid.ResolveBody(null);
        check("Legacy characters receive the human body default",
            standard.Species == HomoSapiens.SpeciesId && standard.Height == 1.75 &&
            standard.ColliderRadius == .25 && standard.Skin == new BodyColor(.90, .72, .58));
        check("Unsupported species and out-of-range features are rejected",
            Rejects(new(Species: "elf")) && Rejects(new(Height: 4)) && Rejects(new(Mass: double.NaN)) &&
            Rejects(new(Skin: new(1.5, .1, .2))));

        foreach (var profile in new[]
        {
            new HumanoidProfile(Height: 1.45, Build: .8, LegRatio: .48, FaceWidth: .85),
            new HumanoidProfile(Height: 2.10, Build: 1.2, LegRatio: .40, FaceWidth: 1.15, EarScale: 1.4,
                Mass: 94, Skin: new(.34, .19, .12), Eyes: new(.15, .42, .28), Hair: new(.09, .05, .035))
        })
        {
            var body = Humanoid.ResolveBody(profile);
            var rig = PlayerAnatomy.Create("humano", body.Height, body).ToDictionary(joint => joint.Name);
            var descriptor = new Villager().Build(new() { Body = body });
            var skin = descriptor.Materials.Single(material => material.Id == "skin");
            var head = descriptor.Groups.Single(group => group.Id == "head").Parts.Single();
            check("Body, rig and collider share the same dimensions and skin",
                Math.Abs(rig["eye-pivot"].Y - body.EyeHeight) < 1e-8 &&
                Math.Abs(rig["hip-left"].X + body.LegSpacing) < 1e-8 &&
                Math.Abs(rig["hip-left"].Y - body.HipHeight) < 1e-8 &&
                Math.Abs(rig["ankle-left"].Y - body.AnkleJoint) < 1e-8 &&
                skin.Color.SequenceEqual(body.Skin.ToArray()) &&
                head.ScaleX == body.FaceWidth && descriptor.CollisionRadius == body.ColliderRadius &&
                descriptor.Groups.Any(group => group.Id == "ears") == (body.EarScale > 0));

            var movement = new PlayerMovement(body.Height, body.KneeHeight, body.FloorOffset, radius: body.ColliderRadius);
            movement.SetSolidBoxes([0, .1, 0, 3, -1, 1]);
            var wall = movement.Sweep(-2, body.FloorOffset, 0, 4, 0, 0);
            check("The shared sweep applies the character's profile radius",
                Math.Abs(wall.X + body.ColliderRadius + .001) < 1e-8);
        }
    }

    private static bool Rejects(HumanoidProfile profile)
    {
        try { _ = Humanoid.ResolveBody(profile); return false; }
        catch (ArgumentException) { return true; }
    }
}
