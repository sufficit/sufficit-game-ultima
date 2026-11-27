namespace Ultima.Models.Core;

/// <summary>
/// Optional authoring values for one humanoid. The species supplies defaults and validates ranges.
/// The same height and mass can describe distinct skeletons and body shapes.
/// </summary>
/// <param name="Species">Stable catalog identifier for the species resolver.</param>
/// <param name="Height">Standing height in meters, measured from the sole.</param>
/// <param name="Mass">Body mass in kilograms. Currently stored for future dynamics.</param>
/// <param name="Build">Baseline width multiplier for the body, including limb diameters.</param>
/// <param name="LegRatio">Hip height as a fraction of standing height.</param>
/// <param name="FaceWidth">Horizontal multiplier for the head and facial features.</param>
/// <param name="EarScale">Ear size multiplier. Zero omits the separate ear geometry.</param>
/// <param name="Skin">Skin color in normalized RGB channels.</param>
/// <param name="Eyes">Iris color in normalized RGB channels.</param>
/// <param name="Hair">Hair color in normalized RGB channels.</param>
/// <param name="FemurRatio">Fraction of the hip-to-ankle length assigned to the femur.</param>
/// <param name="ArmLengthRatio">Shoulder-to-wrist length as a fraction of standing height.</param>
/// <param name="UpperArmRatio">Fraction of the shoulder-to-wrist length assigned to the upper arm.</param>
/// <param name="ChestScale">Chest width multiplier applied after baseline build.</param>
/// <param name="WaistCircumference">Optional abdominal circumference in meters, independent of build.</param>
/// <param name="ThighScale">Thigh diameter multiplier applied after baseline build.</param>
/// <param name="CalfScale">Calf diameter multiplier applied after baseline build.</param>
/// <param name="UpperArmScale">Upper arm diameter multiplier applied after baseline build.</param>
/// <param name="ForearmScale">Forearm diameter multiplier applied after baseline build.</param>
/// <param name="ShoulderHeightRatio">Shoulder joint height as a fraction of standing height, independent of arm length.</param>
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
    double? ForearmScale = null,
    double? ShoulderHeightRatio = null);

/// <summary>A color expressed as red, green, and blue channels in the range 0 to 1.</summary>
public readonly record struct BodyColor(double Red, double Green, double Blue)
{
    public double[] ToArray() => [Red, Green, Blue];
}

/// <summary>
/// Resolved, symmetric body measurements shared by model generation, the rig,
/// movement and camera. Lengths are in meters. Vertical joint positions are
/// measured from the soles in the neutral pose.
/// </summary>
public sealed record HumanoidBody(
    string Species, double Height, double Mass, double Build, double LegRatio,
    double FaceWidth, double EarScale, BodyColor Skin, BodyColor Eyes, BodyColor Hair,
    double FemurRatio, double ArmLengthRatio, double UpperArmRatio, double ChestScale,
    double? WaistCircumference, double ThighScale, double CalfScale, double UpperArmScale, double ForearmScale,
    double ShoulderHeightRatio = HumanoidBody.DefaultShoulderHeightRatio)
{
    /// <summary>Offsets the ankle pivot from the center of the sole geometry.</summary>
    public const double SoleClearance = .004;
    public double EyeHeight => Height * .937;
    public double CameraTargetHeight => Height * (1.30 / 1.75);
    public double FloorOffset => .03;
    public double HipHeight => Height * LegRatio;
    // The baseline leg radius sets shoe and ankle dimensions even when individual leg widths change.
    public double LegRadius => Height * .039 * Build;
    public double ThighRadius => LegRadius * ThighScale;
    public double CalfRadius => LegRadius * CalfScale;
    public double AnkleHeight => LegRadius * 1.25 * .55;
    public double AnkleJoint => AnkleHeight - SoleClearance;
    public double LegLength => HipHeight - AnkleJoint;
    public double FemurLength => LegLength * FemurRatio;
    // The two segments always add up to the same hip-to-ankle span.
    public double TibiaLength => LegLength - FemurLength;
    public double KneeHeight => HipHeight - FemurLength;
    public double LegSpacing => Height * .052 * Build;
    public double ShoeOffset => .9 * LegRadius;
    public double TorsoRadius => Height * .088 * Build * ChestScale;
    // The current model uses a circular cross-section at the widest part of the abdomen.
    public double WaistRadius => WaistCircumference is { } waist ? waist / Math.Tau : TorsoRadius;
    public double ArmRadius => Height * .026 * Build;
    public double UpperArmRadius => ArmRadius * UpperArmScale;
    public double ForearmRadius => ArmRadius * ForearmScale;
    public double ArmOffset => TorsoRadius + Math.Max(UpperArmRadius, ForearmRadius) * 1.3;
    public const double DefaultShoulderHeightRatio = .80;
    /// <summary>Height of the shoulder pivot above the sole in meters.</summary>
    public double ShoulderHeight => Height * ShoulderHeightRatio;
    public double ElbowHeight => ShoulderHeight - Height * ArmLengthRatio * UpperArmRatio;
    public double WristHeight => ShoulderHeight - Height * ArmLengthRatio;
    /// <summary>
    /// Horizontal movement envelope. Regional geometry can expand the baseline
    /// radius to account for wider arms, chest, abdomen, or legs.
    /// </summary>
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
