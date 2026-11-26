using System.Text.Json.Serialization;

using Ultima.Models.Core;

namespace Ultima.Terrain;

// Neutral joint pivots in model-space meters. A parent precedes its children.
internal sealed record AnatomyJoint(string Name, string Parent, double X, double Y, double Z, string Motion);

internal static class PlayerAnatomy
{
    internal static AnatomyJoint[] Create(string kind, double height, HumanoidBody? body = null)
    {
        if (!double.IsFinite(height) || height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        var joints = new List<AnatomyJoint>();
        void Add(string name, string parent, double x, double y, double z, string motion = "ball")
            => joints.Add(new(name, parent, x * height, y * height, z * height, motion));

        switch (kind)
        {
            case "humano":
                var hip = body?.LegRatio ?? .44;
                Add("pelvis", "root", 0, hip, 0);
                Add("upper-body", "pelvis", 0, hip, 0);
                Add("chest", "upper-body", 0, .70, 0);
                Add("neck-pivot", "chest", 0, .82, 0);
                Add("head-pivot", "neck-pivot", 0, .86, 0);
                Add("eye-pivot", "head-pivot", 0, .937, 0);
                Add("jaw", "head-pivot", 0, .9, .035, "hinge");
                var ankle = body is null ? .039 * 1.25 * .55 - .004 / height : body.AnkleJoint / height;
                var knee = body is null ? (hip + ankle) / 2 : body.KneeHeight / height;
                var elbow = body is null ? .63 : body.ElbowHeight / height;
                var wrist = body is null ? .45 : body.WristHeight / height;
                foreach (var side in new[] { "left", "right" })
                {
                    var sign = side == "left" ? -1 : 1;
                    var arm = sign * (body is null ? .088 + .026 * 1.3 : body.ArmOffset / height);
                    var leg = sign * (body is null ? .052 : body.LegSpacing / height);
                    Add("clavicle-" + side, "chest", sign * .025, .80, 0);
                    Add("shoulder-" + side, "clavicle-" + side, arm, .80, 0);
                    Add("elbow-" + side, "shoulder-" + side, arm, elbow, 0, "hinge");
                    Add("wrist-" + side, "elbow-" + side, arm, wrist, 0);
                    Add("hip-" + side, "pelvis", leg, hip, 0);
                    Add("knee-" + side, "hip-" + side, leg, knee, 0, "hinge");
                    Add("ankle-" + side, "knee-" + side, leg, ankle, 0);
                    Add("ball-" + side, "ankle-" + side, leg, .009, .065, "hinge");
                    for (var digit = 1; digit <= 5; digit++)
                    {
                        var x = arm + sign * (digit - 3) * .009;
                        var finger = $"finger-{digit}-{side}";
                        Add(finger, "wrist-" + side, x, wrist - .018, 0);
                        Add(finger + "-middle", finger, x, wrist - .033, 0, "hinge");
                        Add(finger + "-tip", finger + "-middle", x, wrist - .043, 0, "hinge");
                        var toe = $"toe-{digit}-{side}";
                        var toeX = leg + sign * (digit - 3) * .01;
                        Add(toe, "ball-" + side, toeX, .009, .082, "hinge");
                        Add(toe + "-tip", toe, toeX, .009, .104, "hinge");
                    }
                }
                break;
            case "canino":
            case "equino":
                var horse = kind == "equino";
                Add("pelvis", "root", 0, .92, -.35);
                Add("spine", "pelvis", 0, .96, 0);
                Add("chest", "spine", 0, 1, .36);
                Add("neck", "chest", 0, horse ? 1.28 : 1.06, .57);
                Add("head", "neck", 0, horse ? 1.44 : 1.12, .73);
                Add("jaw", "head", 0, horse ? 1.40 : 1.08, .78, "hinge");
                Add("tail-base", "pelvis", 0, .96, -.52);
                Add("tail-mid", "tail-base", 0, horse ? .62 : 1.03, -.72);
                Add("tail-tip", "tail-mid", 0, horse ? .29 : 1.14, -.84);
                foreach (var side in new[] { "left", "right" })
                {
                    var x = side == "left" ? -.14 : .14;
                    Add("shoulder-" + side, "chest", x, .86, .43);
                    Add("elbow-" + side, "shoulder-" + side, x, .59, .30, "hinge");
                    Add("carpus-" + side, "elbow-" + side, x, .30, .40, "hinge");
                    Add("hip-" + side, "pelvis", x, .90, -.42);
                    Add("stifle-" + side, "hip-" + side, x, .64, -.29, "hinge");
                    Add("hock-" + side, "stifle-" + side, x, .34, -.52, "hinge");
                    var contact = horse ? "hoof" : "paw";
                    Add($"front-{contact}-{side}", "carpus-" + side, x, .05, .43, "hinge");
                    Add($"hind-{contact}-{side}", "hock-" + side, x, .05, -.44, "hinge");
                }
                break;
            case "ave":
                Add("pelvis", "root", 0, .53, -.09);
                Add("chest", "pelvis", 0, .62, .07);
                Add("neck", "chest", 0, .76, .12);
                Add("head", "neck", 0, .91, .18);
                Add("beak", "head", 0, .88, .28, "hinge");
                Add("tail", "pelvis", 0, .56, -.28);
                foreach (var side in new[] { "left", "right" })
                {
                    var sign = side == "left" ? -1 : 1;
                    Add("wing-shoulder-" + side, "chest", sign * .13, .62, 0);
                    Add("wing-elbow-" + side, "wing-shoulder-" + side, sign * .33, .59, -.11, "hinge");
                    Add("wing-wrist-" + side, "wing-elbow-" + side, sign * .51, .55, 0);
                    Add("wing-tip-" + side, "wing-wrist-" + side, sign * .75, .52, -.15);
                    Add("hip-" + side, "pelvis", sign * .09, .53, -.08);
                    Add("knee-" + side, "hip-" + side, sign * .09, .40, .07, "hinge");
                    Add("hock-" + side, "knee-" + side, sign * .09, .22, -.06, "hinge");
                    Add("foot-" + side, "hock-" + side, sign * .09, .03, 0, "hinge");
                }
                break;
            default:
                throw new ArgumentException("Espécie desconhecida.", nameof(kind));
        }

        return joints.ToArray();
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AnatomyJoint[]))]
internal partial class AnatomyJsonContext : JsonSerializerContext;
