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
    BodyColor? Hair = null,
    double? FemurRatio = null,
    double? ArmLengthRatio = null,
    double? UpperArmRatio = null,
    double? ChestScale = null,
    double? WaistCircumference = null,
    double? ThighScale = null,
    double? CalfScale = null,
    double? UpperArmScale = null,
    double? ForearmScale = null);

public readonly record struct BodyColor(double Red, double Green, double Blue)
{
    public double[] ToArray() => [Red, Green, Blue];
}

public sealed record HumanoidBody(
    string Species, double Height, double Mass, double Build, double LegRatio,
    double FaceWidth, double EarScale, BodyColor Skin, BodyColor Eyes, BodyColor Hair,
    double FemurRatio, double ArmLengthRatio, double UpperArmRatio, double ChestScale,
    double? WaistCircumference, double ThighScale, double CalfScale, double UpperArmScale, double ForearmScale)
{
    public const double SoleClearance = .004;
    public double EyeHeight => Height * .937;
    public double CameraTargetHeight => Height * (1.30 / 1.75);
    public double FloorOffset => .03;
    public double HipHeight => Height * LegRatio;
    public double LegRadius => Height * .039 * Build;
    public double ThighRadius => LegRadius * ThighScale;
    public double CalfRadius => LegRadius * CalfScale;
    public double AnkleHeight => LegRadius * 1.25 * .55;
    public double AnkleJoint => AnkleHeight - SoleClearance;
    public double LegLength => HipHeight - AnkleJoint;
    public double FemurLength => LegLength * FemurRatio;
    public double TibiaLength => LegLength - FemurLength;
    public double KneeHeight => HipHeight - FemurLength;
    public double LegSpacing => Height * .052 * Build;
    public double ShoeOffset => .9 * LegRadius;
    public double TorsoRadius => Height * .088 * Build * ChestScale;
    public double WaistRadius => WaistCircumference is { } waist ? waist / Math.Tau : TorsoRadius;
    public double ArmRadius => Height * .026 * Build;
    public double UpperArmRadius => ArmRadius * UpperArmScale;
    public double ForearmRadius => ArmRadius * ForearmScale;
    public double ArmOffset => TorsoRadius + Math.Max(UpperArmRadius, ForearmRadius) * 1.3;
    public double ElbowHeight => Height * (.80 - ArmLengthRatio * UpperArmRatio);
    public double WristHeight => Height * (.80 - ArmLengthRatio);
    public double ColliderRadius
    {
        get
        {
            var armGrowth = (TorsoRadius - Height * .088 * Build) +
                (Math.Max(UpperArmRadius, ForearmRadius) - ArmRadius) * 2.3;
            var radius = .25 * (Height / 1.75) * Build + Math.Max(0, armGrowth);
            return Math.Max(radius, Math.Max(WaistRadius, LegSpacing + Math.Max(ThighRadius, CalfRadius)));
        }
    }
}
