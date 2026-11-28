using System.Numerics;
using Ultima.Models.Core;

namespace Ultima.Terrain;

internal static class PlayerSegmentMass
{
    internal const double Gravity = 9.8;
    // Rounded gameplay fractions informed by de Leva (1996), Table 4. Limb fractions are per side.
    // Hands include the fingers and feet include the toes. Fractions total one.
    internal const double Head = .07, Thorax = .16, Abdomen = .16, Pelvis = .11;
    internal const double UpperArm = .027, Forearm = .016, Hand = .007;
    internal const double Thigh = .142, Shin = .044, Foot = .014;

    private static readonly Vector3 Up = Vector3.UnitY;
    private static readonly Vector3 Down = -Vector3.UnitY;

    // Positions use actor-space metres above the support plane. Each segment pivots at the
    // upstream landmark and contributes the mass at its own position along the chain.
    internal static Vector3 Center(HumanoidBody body, ReadOnlySpan<double> pose, ReadOnlySpan<double> gait, double crouch)
    {
        var pelvis = new Vector3((float)gait[2], (float)(body.HipHeight - crouch), 0);
        var torso = Quaternion.CreateFromYawPitchRoll((float)gait[0], (float)pose[2], (float)(pose[3] + gait[1]));
        var chest = torso * Quaternion.CreateFromYawPitchRoll((float)gait[3], 0, (float)gait[4]);
        var belly = pelvis + Axis(Up, torso, (body.ShoulderHeight - body.HipHeight) * .53);
        var shoulders = belly + Axis(Up, chest, (body.ShoulderHeight - body.HipHeight) * .47);
        var neck = shoulders + Axis(Up, chest, body.Height * .058);
        var head = neck + Axis(Up, Angles(pose[4], pose[5]), Math.Max(.04, body.Height * .93 - body.ShoulderHeight - body.Height * .058));
        var sum = pelvis * (float)Pelvis + Segment(pelvis, belly, Abdomen, .48) +
            Segment(belly, neck, Thorax, .53) + head * (float)Head;

        for (var side = 0; side < 2; side++)
        {
            var sign = side == 0 ? -1 : 1;
            var shoulder = shoulders + Axis(Vector3.UnitX * sign, chest, body.ArmOffset);
            var shoulderRotation = chest * Angles(pose[10 + side], sign * .10);
            var elbow = shoulder + Axis(Down, shoulderRotation, body.Height * body.ArmLengthRatio * body.UpperArmRatio);
            var forearmRotation = shoulderRotation * Angles(gait[8 + side], 0);
            var wrist = elbow + Axis(Down, forearmRotation, body.Height * body.ArmLengthRatio * (1 - body.UpperArmRatio));
            var fingertips = wrist + Axis(Down, forearmRotation * Angles(gait[10 + side], 0), body.Height * .095);
            sum += Segment(shoulder, elbow, UpperArm, .58) + Segment(elbow, wrist, Forearm, .46) +
                Segment(wrist, fingertips, Hand, .50);

            var i = 18 + 3 * side;
            var hip = pelvis + Vector3.UnitX * (float)(sign * body.LegSpacing);
            var thigh = Angles(pose[i], pose[37 + side]);
            var shin = thigh * Angles(pose[i + 1], 0);
            var knee = hip + Axis(Down, thigh, body.FemurLength);
            var ankle = knee + Axis(Down, shin, body.TibiaLength);
            var toe = ankle + Axis(Vector3.UnitZ, shin * Angles(pose[i + 2], 0), body.Height * .12);
            sum += Segment(hip, knee, Thigh, .41) + Segment(knee, ankle, Shin, .44) + Segment(ankle, toe, Foot, .45);
        }
        return sum;
    }

    private static Quaternion Angles(double pitch, double roll) =>
        Quaternion.CreateFromYawPitchRoll(0, (float)pitch, (float)roll);

    private static Vector3 Axis(Vector3 axis, Quaternion rotation, double length) =>
        Vector3.Transform(axis, rotation) * (float)length;

    private static Vector3 Segment(Vector3 start, Vector3 end, double fraction, double center) =>
        Vector3.Lerp(start, end, (float)center) * (float)fraction;
}
