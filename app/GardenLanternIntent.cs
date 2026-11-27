using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Ultima.Garden;

[Flags]
internal enum LanternAdjustment
{
    None = 0,
    Height = 1,
    Intensity = 2
}

/// <summary>Recognizes bounded relative edits that the garden can apply without a model.</summary>
internal static partial class GardenLanternIntent
{
    internal static LanternAdjustment Parse(string message)
    {
        var words = new string(message.ToLowerInvariant().Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray());
        var text = string.Join(' ', words.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (HeightRequest().IsMatch(text)) return LanternAdjustment.Height;
        if (IntensityRequest().IsMatch(text)) return LanternAdjustment.Intensity;
        if (CombinedRequest().IsMatch(text)) return LanternAdjustment.Height | LanternAdjustment.Intensity;
        return LanternAdjustment.None;
    }

    // Match complete commands so negation, quantities, and unsupported compound edits stay with the planner.
    [GeneratedRegex(@"\A(?:(?:aumente|aumentar) a altura (?:das|dessas|destas) luminarias|(?:deixe|quero) (?:(?:as|essas|estas) )?luminarias mais altas)\z")]
    private static partial Regex HeightRequest();

    [GeneratedRegex(@"\A(?:(?:aumente|aumentar) a intensidade(?: da luz)? (?:das|dessas|destas) luminarias|(?:deixe|quero) (?:(?:as|essas|estas) )?luminarias mais fortes)\z")]
    private static partial Regex IntensityRequest();

    [GeneratedRegex(@"\A(?:essas luminarias possuem o meu tamanho )?(?:deixe|quero) (?:(?:as|essas|estas) )?luminarias mais altas e (?:um pouco )?mais fortes\z")]
    private static partial Regex CombinedRequest();
}
