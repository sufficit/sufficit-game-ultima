namespace Ultima.Models.Core;

public sealed record HumanoidProfile(
    string Species = HomoSapiens.SpeciesId,
    double? Height = null,
    double? Mass = null,
    double? Build = null,
    double? LegRatio = null,
    double? FaceWidth = null,
    double? EarScale = null,
    BodyColor? Skin = null,
    BodyColor? Eyes = null,
    BodyColor? Hair = null);

public readonly record struct BodyColor(double Red, double Green, double Blue)
{
    public double[] ToArray() => [Red, Green, Blue];
}

public sealed record HumanoidBody(
    string Species, double Height, double Mass, double Build, double LegRatio,
    double FaceWidth, double EarScale, BodyColor Skin, BodyColor Eyes, BodyColor Hair)
{
    public const double SoleClearance = .004;
    public double EyeHeight => Height * .937;
    public double CameraTargetHeight => Height * (1.30 / 1.75);
    public double FloorOffset => .03;
    public double HipHeight => Height * LegRatio;
    public double LegRadius => Height * .039 * Build;
    public double AnkleHeight => LegRadius * 1.25 * .55;
    public double AnkleJoint => AnkleHeight - SoleClearance;
    public double LegSegment => (HipHeight - AnkleJoint) / 2;
    public double KneeHeight => HipHeight - LegSegment;
    public double LegSpacing => Height * .052 * Build;
    public double ShoeOffset => .9 * LegRadius;
    public double TorsoRadius => Height * .088 * Build;
    public double ArmRadius => Height * .026 * Build;
    public double ArmOffset => TorsoRadius + ArmRadius * 1.3;
    public double ColliderRadius => .25 * (Height / 1.75) * Build;
}
