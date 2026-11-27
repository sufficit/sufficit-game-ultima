namespace Ultima.Models.Core;

/// <summary>Public numeric parameter of a placed model instance.</summary>
public sealed record PlacementParameter(string Key, string Label, string Unit,
    double Min, double Max, double Default, string? Inherits = null, bool Integral = false)
{
    public double Read(IReadOnlyDictionary<string, double> values) =>
        values.TryGetValue(Key, out var value) ? value : Default;
}

/// <summary>A model declares and validates its own instance parameters.</summary>
public interface IEditablePlacementModel
{
    string? DisplayName { get; }
    IReadOnlyList<PlacementParameter> Parameters { get; }

    /// <summary>Resolves a parameter against persisted overrides and the placement transform.</summary>
    double Read(PlacementParameter parameter, IReadOnlyDictionary<string, double> values, double scale, double rotation);

    /// <summary>Accepts the requested patch and preserves omitted values.</summary>
    IReadOnlyDictionary<string, double>? Apply(IReadOnlyDictionary<string, double> current,
        IReadOnlyDictionary<string, double> patch);
}

/// <summary>Default transform contract, including imported models.</summary>
public class PlacementModel : IEditablePlacementModel
{
    protected static IReadOnlyList<PlacementParameter> CommonParameters { get; } = Array.AsReadOnly<PlacementParameter>(
    [
        new("scaleX", "Largura", "multiplicador", 0.05, 8, 1, "scale"),
        new("scaleY", "Altura", "multiplicador", 0.05, 8, 1, "scale"),
        new("scaleZ", "Profundidade", "multiplicador", 0.05, 8, 1, "scale"),
        new("rotationY", "Rotação horizontal", "rad", -2 * Math.PI, 2 * Math.PI, 0, "rot")
    ]);

    public virtual string? DisplayName => null;
    public virtual IReadOnlyList<PlacementParameter> Parameters => CommonParameters;

    public virtual double Read(PlacementParameter parameter, IReadOnlyDictionary<string, double> values, double scale, double rotation) =>
        values.TryGetValue(parameter.Key, out var value) ? value : parameter.Inherits switch
        {
            "scale" => scale,
            "rot" => rotation,
            _ => parameter.Default
        };

    public virtual IReadOnlyDictionary<string, double>? Apply(IReadOnlyDictionary<string, double> current,
        IReadOnlyDictionary<string, double> patch)
    {
        if (patch.Count == 0) return null;

        var next = new Dictionary<string, double>(current, StringComparer.Ordinal);
        foreach (var (key, value) in patch)
        {
            var parameter = Parameters.FirstOrDefault(p => p.Key == key);
            if (parameter is null || !double.IsFinite(value) || value < parameter.Min || value > parameter.Max ||
                (parameter.Integral && Math.Truncate(value) != value))
                return null;
            next[key] = Normalize(parameter, value);
        }
        return next;
    }

    protected virtual double Normalize(PlacementParameter parameter, double value) => value;
}

/// <summary>Lantern properties in addition to the shared transform.</summary>
public sealed class LanternPlacementModel : PlacementModel
{
    public const string AssetId = "prop.lantern";
    public override string DisplayName => "Luminária";
    public static PlacementParameter PoleHeight { get; } = new("poleHeight", "Altura da haste", "m", 0.5, 5, 1.4);
    public static PlacementParameter LightIntensity { get; } = new("lightIntensity", "Intensidade da luz", "intensidade", 0, 3, 1.05);
    public static PlacementParameter LightLumens { get; } = new("lightLumens", "Fluxo luminoso", "lm", 0, 5000, 1.05 * 4 * Math.PI);
    public static PlacementParameter LightColor { get; } = new("lightRgb", "Cor da luz", "sRGB24", 0, 0xffffff, 0xffab4d, Integral: true);
    public override IReadOnlyList<PlacementParameter> Parameters { get; } =
        Array.AsReadOnly<PlacementParameter>([.. CommonParameters, PoleHeight, LightLumens, LightColor, LightIntensity]);

    public override double Read(PlacementParameter parameter, IReadOnlyDictionary<string, double> values, double scale, double rotation) =>
        parameter == LightLumens && !values.ContainsKey(LightLumens.Key)
            ? LightIntensity.Read(values) * 4 * Math.PI
            : base.Read(parameter, values, scale, rotation);

    public override IReadOnlyDictionary<string, double>? Apply(IReadOnlyDictionary<string, double> current,
        IReadOnlyDictionary<string, double> patch)
    {
        if (patch.ContainsKey(LightLumens.Key))
            current = current.Where(entry => entry.Key != LightIntensity.Key).ToDictionary();
        else if (patch.ContainsKey(LightIntensity.Key))
            current = current.Where(entry => entry.Key != LightLumens.Key).ToDictionary();
        return base.Apply(current, patch);
    }

    // Pole geometry shares prototypes at centimetre precision.
    protected override double Normalize(PlacementParameter parameter, double value) =>
        parameter == PoleHeight || parameter == LightIntensity ? Math.Round(value, 2) : value;
}

/// <summary>Instance properties supported by the corresponding vegetation renderer.</summary>
public sealed class VegetationPlacementModel(string displayName, double? baseHeightM, double maxHeightM) : PlacementModel
{
    public override string DisplayName => displayName;

    private static readonly PlacementParameter Tint = new("tintRgb", "Cor da vegetação", "sRGB24",
        0, 0xffffff, 0xffffff, Integral: true);

    public override IReadOnlyList<PlacementParameter> Parameters { get; } = Array.AsReadOnly<PlacementParameter>(baseHeightM is { } height
        ? [.. CommonParameters, Tint, new("heightM", "Altura", "m", 0.1, maxHeightM, height)]
        : [.. CommonParameters, Tint]);

    public override IReadOnlyDictionary<string, double>? Apply(IReadOnlyDictionary<string, double> current,
        IReadOnlyDictionary<string, double> patch)
    {
        if (patch.ContainsKey("scaleY") && !patch.ContainsKey("heightM"))
            current = current.Where(entry => entry.Key != "heightM").ToDictionary();
        return base.Apply(current, patch);
    }

    public override double Read(PlacementParameter parameter, IReadOnlyDictionary<string, double> values, double scale, double rotation) =>
        (parameter.Key, baseHeightM) switch
        {
            ("heightM", _) when !values.ContainsKey(parameter.Key) =>
                parameter.Default * (values.TryGetValue("scaleY", out var vertical) ? vertical : scale),
            ("scaleY", { } height) when values.TryGetValue("heightM", out var metres) => metres / height,
            _ => base.Read(parameter, values, scale, rotation)
        };
}
