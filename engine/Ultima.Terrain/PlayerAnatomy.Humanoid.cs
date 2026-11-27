using Ultima.Models.Core;

namespace Ultima.Terrain;

internal static partial class PlayerAnatomy
{
    // Authoring bounds. The runtime solver will use them when the skin binding is integrated.
    private static readonly AnatomyFlexion SpinePitch = Pitch(1, -25, 45);
    private static readonly AnatomyFlexion NeckPitch = Pitch(1, -45, 45);
    private static readonly AnatomyFlexion JawPitch = Pitch(1, 0, 35);
    private static readonly AnatomyFlexion ShoulderPitch = Pitch(-1, -45, 165);
    private static readonly AnatomyFlexion ElbowFlexion = Pitch(-1, 0, 145);
    private static readonly AnatomyFlexion WristPitch = Pitch(-1, -65, 75);
    private static readonly AnatomyFlexion HipFlexion = Pitch(-1, -25, 120);
    private static readonly AnatomyFlexion KneeFlexion = Pitch(1, 0, 145);
    private static readonly AnatomyFlexion AnklePitch = Pitch(-1, -45, 25);
    private static readonly AnatomyFlexion BallPitch = Pitch(-1, -25, 60);
    private static readonly AnatomyFlexion FingerFlexion = Pitch(-1, 0, 90);
    private static readonly AnatomyFlexion ToePitch = Pitch(-1, -25, 45);

    private static AnatomyFlexion Pitch(int axisX, double minDegrees, double maxDegrees) =>
        new(axisX, 0, 0, double.DegreesToRadians(minDegrees), double.DegreesToRadians(maxDegrees));

    private static readonly HumanoidBody DefaultHumanoid = Humanoid.ResolveBody(null);

    private static void AddHumanoid(List<AnatomyJoint> joints, double height, HumanoidBody? body)
    {
        void Add(string name, string parent, double x, double y, double z,
            string motion = "ball", AnatomyFlexion? flexion = null) =>
            joints.Add(new(name, parent, x * height, y * height, z * height, motion, flexion));

        var hip = body?.LegRatio ?? .44;
        Add("pelvis", "root", 0, hip, 0);
        Add("upper-body", "pelvis", 0, hip, 0, flexion: SpinePitch);
        Add("chest", "upper-body", 0, .70, 0, flexion: SpinePitch);
        Add("neck-pivot", "chest", 0, .82, 0, flexion: NeckPitch);
        Add("head-pivot", "neck-pivot", 0, .86, 0, flexion: NeckPitch);
        Add("eye-pivot", "head-pivot", 0, .937, 0);
        Add("jaw", "head-pivot", 0, .9, .035, "hinge", flexion: JawPitch);
        var ankle = body is null ? .039 * 1.25 * .55 - .004 / height : body.AnkleJoint / height;
        var knee = body is null ? (hip + ankle) / 2 : body.KneeHeight / height;
        var shoulder = body?.ShoulderHeightRatio ?? HumanoidBody.DefaultShoulderHeightRatio;
        var armBody = body ?? DefaultHumanoid;
        var elbow = armBody.ElbowHeight / armBody.Height;
        var wrist = armBody.WristHeight / armBody.Height;
        foreach (var side in new[] { "left", "right" })
        {
            var sign = side == "left" ? -1 : 1;
            var arm = sign * armBody.ArmOffset / armBody.Height;
            var leg = sign * (body is null ? .052 : body.LegSpacing / height);
            // Use the inner clavicle and upper-arm pivot guides from MPFB2.
            // The neutral rig uses their X and Y proportions with zero depth offset.
            Add("clavicle-" + side, "chest", sign * HumanoidBody.ClavicleOffsetRatio, HumanoidBody.ClavicleHeightRatio, 0);
            Add("shoulder-" + side, "clavicle-" + side, arm, shoulder, 0, flexion: ShoulderPitch);
            Add("elbow-" + side, "shoulder-" + side, arm, elbow, 0, "hinge", flexion: ElbowFlexion);
            Add("wrist-" + side, "elbow-" + side, arm, wrist, 0, flexion: WristPitch);
            Add("hip-" + side, "pelvis", leg, hip, 0, flexion: HipFlexion);
            Add("knee-" + side, "hip-" + side, leg, knee, 0, "hinge", flexion: KneeFlexion);
            Add("ankle-" + side, "knee-" + side, leg, ankle, 0, flexion: AnklePitch);
            Add("ball-" + side, "ankle-" + side, leg, .009, .065, "hinge", flexion: BallPitch);
            for (var digit = 1; digit <= 5; digit++)
            {
                var x = arm + sign * (digit - 3) * .009;
                var finger = $"finger-{digit}-{side}";
                Add(finger, "wrist-" + side, x, wrist - .018, 0, flexion: FingerFlexion);
                Add(finger + "-middle", finger, x, wrist - .033, 0, "hinge", flexion: FingerFlexion);
                Add(finger + "-tip", finger + "-middle", x, wrist - .043, 0, "hinge", flexion: FingerFlexion);
                var toe = $"toe-{digit}-{side}";
                var toeX = leg + sign * (digit - 3) * .01;
                Add(toe, "ball-" + side, toeX, .009, .082, "hinge", flexion: ToePitch);
                Add(toe + "-tip", toe, toeX, .009, .104, "hinge", flexion: ToePitch);
            }
        }
    }
}
