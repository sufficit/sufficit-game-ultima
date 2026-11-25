using System.Globalization;
using System.Text;

namespace Ultima.Garden;

internal static class GardenLanternIntent
{
    internal static bool Matches(string message)
    {
        var words = new string(message.ToLowerInvariant().Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray());
        var text = " " + string.Join(' ', words.Split(' ', StringSplitOptions.RemoveEmptyEntries)) + " ";
        if (!text.Contains(" luminarias ", StringComparison.Ordinal)
            || !text.Contains(" quero ", StringComparison.Ordinal)
            || text.Contains(" nao ", StringComparison.Ordinal)
            || text.Contains(" todas ", StringComparison.Ordinal)
            || text.Any(char.IsDigit))
            return false;

        // Pedidos livres continuam no planejador. Este atalho só reconhece a combinação
        // relativa pedida no jogo e evita que um modelo pequeno devolva apenas texto.
        return text.Contains(" mais altas ", StringComparison.Ordinal) &&
               text.Contains(" mais fortes ", StringComparison.Ordinal);
    }
}
