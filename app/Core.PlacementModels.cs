namespace Ultima.Models.Core;

/// <summary>Parâmetro numérico público de uma instância colocada no mapa.</summary>
public sealed record PlacementParameter(string Key, string Label, string Unit,
    double Min, double Max, double Default, string? Inherits = null)
{
    public double Read(IReadOnlyDictionary<string, double> values) =>
        values.TryGetValue(Key, out var value) ? value : Default;
}

/// <summary>Um modelo declara e valida a própria personalização por instância.</summary>
public interface IEditablePlacementModel
{
    IReadOnlyList<PlacementParameter> Parameters { get; }

    /// <summary>Recebe apenas o delta do pedido e conserva valores válidos que foram omitidos.</summary>
    IReadOnlyDictionary<string, double>? Apply(IReadOnlyDictionary<string, double> current,
        IReadOnlyDictionary<string, double> patch);
}

/// <summary>Contrato padrão, inclusive para modelos importados sem parâmetros próprios.</summary>
public class PlacementModel : IEditablePlacementModel
{
    protected static IReadOnlyList<PlacementParameter> CommonParameters { get; } = Array.AsReadOnly<PlacementParameter>(
    [
        new("scaleX", "Largura", "multiplicador", 0.05, 8, 1, "scale"),
        new("scaleY", "Altura", "multiplicador", 0.05, 8, 1, "scale"),
        new("scaleZ", "Profundidade", "multiplicador", 0.05, 8, 1, "scale"),
        new("rotationY", "Rotação horizontal", "rad", -2 * Math.PI, 2 * Math.PI, 0, "rot")
    ]);

    public virtual IReadOnlyList<PlacementParameter> Parameters => CommonParameters;

    public virtual IReadOnlyDictionary<string, double>? Apply(IReadOnlyDictionary<string, double> current,
        IReadOnlyDictionary<string, double> patch)
    {
        if (patch.Count == 0) return null;

        var next = new Dictionary<string, double>(current, StringComparer.Ordinal);
        foreach (var (key, value) in patch)
        {
            var parameter = Parameters.FirstOrDefault(p => p.Key == key);
            if (parameter is null || !double.IsFinite(value) || value < parameter.Min || value > parameter.Max)
                return null;
            next[key] = Normalize(parameter, value);
        }
        return next;
    }

    protected virtual double Normalize(PlacementParameter parameter, double value) => value;
}

/// <summary>Parâmetros próprios da luminária, além da transformação comum.</summary>
public sealed class LanternPlacementModel : PlacementModel
{
    public const string AssetId = "prop.lantern";
    public static PlacementParameter PoleHeight { get; } = new("poleHeight", "Altura da haste", "m", 0.5, 5, 1.4);
    public static PlacementParameter LightIntensity { get; } = new("lightIntensity", "Intensidade da luz", "intensidade", 0, 3, 1.05);
    public override IReadOnlyList<PlacementParameter> Parameters { get; } =
        Array.AsReadOnly<PlacementParameter>([.. CommonParameters, PoleHeight, LightIntensity]);

    // A geometria das hastes compartilha protótipos com precisão de centímetro.
    protected override double Normalize(PlacementParameter parameter, double value) =>
        parameter == PoleHeight || parameter == LightIntensity ? Math.Round(value, 2) : value;
}
