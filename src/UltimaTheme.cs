using Sufficit.Blazor.UI.Themes;

namespace Ultima;

/// <summary>
/// Tema SUI do Ultima: identidade "jardim/fantasia" (verdes-musgo, dourado suave,
/// cantos arredondados) publicada como tokens --sui-* pelo SUIThemeProvider.
/// A base visual continua sendo a Sufficit.Blazor.UI; este tema só substitui a paleta.
/// </summary>
public sealed class UltimaTheme : ISUITheme
{
    public static readonly UltimaTheme Instance = new();

    public bool IsDark => true;

    public SUIPalette Palette { get; } = new()
    {
        // Folha clara para acentos (foco, links, contornos) sobre o mundo 3D.
        Primary = "#8fd19c",
        PrimaryContrast = "#0c1710",
        // Ação primária: musgo profundo com texto claro (botões "Enviar", "Continuar").
        PrimaryAction = "#2f6b46",
        PrimaryActionContrast = "#eafff2",
        PrimarySoft = "color-mix(in srgb, var(--sui-color-primary) 16%, transparent)",
        // Dourado suave para o secundário (a Guardiã, destaques).
        Secondary = "#d9c37a",
        SecondaryContrast = "#171207",
        Info = "#8fc7e8",
        InfoContrast = "#0b1418",
        Success = "#9fe0b5",
        SuccessContrast = "#0c1710",
        Warning = "#ffd97a",
        WarningContrast = "#171207",
        Error = "#ff9fb2",
        ErrorContrast = "#1a0b10",
        Dark = "#0f1712",
        Light = "#eef4ea",
        // Superfícies: verdes muito escuros, quase neutros, para não competir com o 3D.
        Surface = "#111a14",
        Surface2 = "#18241c",
        Surface3 = "#203026",
        TextPrimary = "#e9f1e7",
        TextSecondary = "#aec2af",
        TextDisabled = "#6f8273",
        Border = "#28362c",
        BorderStrong = "#3d5142",
        Focus = "var(--sui-color-primary)",
        Overlay = "rgba(6, 10, 8, .55)",
    };

    public SUITypography Typography { get; } = SUITypography.Default;

    public SUILayout Layout { get; } = new()
    {
        // Cantos generosos: painéis de jogo arredondados sobre o mundo.
        RadiusSm = "6px",
        Radius = "12px",
        RadiusLg = "18px",
        RadiusFull = "9999px",
        Shadow1 = "0 1px 3px rgba(4, 10, 6, .35)",
        Shadow2 = "0 6px 16px rgba(4, 10, 6, .45)",
        Shadow3 = "0 14px 34px rgba(4, 10, 6, .55)",
    };
}
