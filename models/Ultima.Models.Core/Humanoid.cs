namespace Ultima.Models.Core;

public abstract class Humanoid
{
    public abstract string Id { get; }

    public abstract HumanoidBody Resolve(HumanoidProfile profile);

    public static HumanoidBody ResolveBody(HumanoidProfile? profile)
    {
        profile ??= new HumanoidProfile();
        return profile.Species switch
        {
            HomoSapiens.SpeciesId => HomoSapiens.Instance.Resolve(profile),
            _ => throw new ArgumentException("Unknown humanoid species.", nameof(profile))
        };
    }

    protected static double Read(double? value, double fallback, double min, double max, string name)
    {
        if (value is null) return fallback;
        if (!double.IsFinite(value.Value) || value.Value < min || value.Value > max)
            throw new ArgumentOutOfRangeException(name);
        return value.Value;
    }

    protected static BodyColor ReadColor(BodyColor? value, BodyColor fallback, string name)
    {
        if (value is not { } color) return fallback;
        if (!Valid(color.Red) || !Valid(color.Green) || !Valid(color.Blue))
            throw new ArgumentOutOfRangeException(name);
        return color;

        static bool Valid(double channel) => double.IsFinite(channel) && channel is >= 0 and <= 1;
    }
}

public sealed class HomoSapiens : Humanoid
{
    public const string SpeciesId = "homo-sapiens";
    public static HomoSapiens Instance { get; } = new();
    public override string Id => SpeciesId;

    public override HumanoidBody Resolve(HumanoidProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Species != Id) throw new ArgumentException("Profile species does not match.", nameof(profile));
        return new(Id,
            Read(profile.Height, 1.75, 1.45, 2.10, nameof(profile.Height)),
            Read(profile.Mass, 70, 35, 220, nameof(profile.Mass)),
            Read(profile.Build, 1, .8, 1.2, nameof(profile.Build)),
            Read(profile.LegRatio, .44, .40, .48, nameof(profile.LegRatio)),
            Read(profile.FaceWidth, 1, .85, 1.15, nameof(profile.FaceWidth)),
            Read(profile.EarScale, 0, 0, 1.4, nameof(profile.EarScale)),
            ReadColor(profile.Skin, new(.90, .72, .58), nameof(profile.Skin)),
            ReadColor(profile.Eyes, new(.05, .05, .06), nameof(profile.Eyes)),
            ReadColor(profile.Hair, new(.20, .13, .08), nameof(profile.Hair)));
    }
}
