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
            Rejects(new(Skin: new(1.5, .1, .2))) && Rejects(new(WaistCircumference: -1)) &&
            Rejects(new(FemurRatio: double.PositiveInfinity)) && Rejects(new(UpperArmScale: 2)));

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

        var broadWaist = Humanoid.ResolveBody(new(Height: 1.8, Mass: 90, LegRatio: .42, FemurRatio: .56,
            WaistCircumference: 1.10, ChestScale: .94, ArmLengthRatio: .32, UpperArmRatio: .44));
        var broadLimbs = Humanoid.ResolveBody(new(Height: 1.8, Mass: 90, LegRatio: .47, FemurRatio: .44,
            WaistCircumference: .78, ChestScale: 1.18, ArmLengthRatio: .40, UpperArmRatio: .54,
            UpperArmScale: 1.42, ForearmScale: 1.2, ThighScale: 1.35, CalfScale: 1.15));
        check("Equal height and mass can yield different bone lengths and regional circumferences",
            broadWaist.Height == broadLimbs.Height && broadWaist.Mass == broadLimbs.Mass &&
            broadWaist.LegLength < broadLimbs.LegLength && broadWaist.FemurLength > broadLimbs.FemurLength &&
            broadWaist.TibiaLength < broadLimbs.TibiaLength && broadWaist.WristHeight > broadLimbs.WristHeight &&
            broadWaist.WaistRadius > broadLimbs.WaistRadius && broadWaist.TorsoRadius < broadLimbs.TorsoRadius &&
            broadWaist.UpperArmRadius < broadLimbs.UpperArmRadius && broadWaist.ThighRadius < broadLimbs.ThighRadius);

        foreach (var body in new[] { broadWaist, broadLimbs })
        {
            var joints = PlayerAnatomy.Create("humano", body.Height, body).ToDictionary(joint => joint.Name);
            var descriptor = new Villager().Build(new() { Body = body });
            var abdomen = descriptor.Groups.Single(group => group.Id == "torso").Parts.Single(part => part.Id == "abdomen");
            check("Custom bone lengths and measured waist match the rig and model",
                Close(joints["hip-left"].Y - joints["knee-left"].Y, body.FemurLength) &&
                Close(joints["knee-left"].Y - joints["ankle-left"].Y, body.TibiaLength) &&
                Close(joints["elbow-left"].Y, body.ElbowHeight) && Close(joints["wrist-left"].Y, body.WristHeight) &&
                Close(Math.PI * abdomen.Diameter * abdomen.ScaleX, body.WaistCircumference!.Value));
            CheckAnkleTarget(body, check);
        }
    }

    private static bool Close(double a, double b) => Math.Abs(a - b) < 1e-8;

    private static void CheckAnkleTarget(HumanoidBody body, Action<string, bool> check)
    {
        const double drop = .52, forward = .19, lateral = -.07, pitch = .10;
        Span<double> angles = stackalloc double[4];
        PlayerPose.SolveSingleLeg(angles, drop, forward, lateral, body.FemurLength, pitch, body.TibiaLength);
        var down = body.FemurLength * Math.Cos(angles[0]) + body.TibiaLength * Math.Cos(angles[0] + angles[1]);
        var advance = -body.FemurLength * Math.Sin(angles[0]) - body.TibiaLength * Math.Sin(angles[0] + angles[1]);
        check("Unequal leg segments reach the planted foot in three dimensions",
            Close(advance, forward) && Close(down * Math.Cos(angles[3]), drop) &&
            Close(down * Math.Sin(angles[3]), lateral) && Close(angles[0] + angles[1] + angles[2], pitch));
    }

    private static bool Rejects(HumanoidProfile profile)
    {
        try { _ = Humanoid.ResolveBody(profile); return false; }
        catch (ArgumentException) { return true; }
    }
}
