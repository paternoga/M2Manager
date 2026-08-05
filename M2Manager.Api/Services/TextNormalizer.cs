using System.Globalization;
using System.Text;

namespace M2Manager.Api.Services;

/// <summary>
/// Normalizacja tekstu do porównań: bez wielkości liter, bez polskich znaków, bez interpunkcji.
/// „~Koszt szt.” → „kosztszt”, „Wyposażenie” → „wyposazenie”.
///
/// Używana w dwóch miejscach, gdzie dane przychodzą z zewnątrz i nie można liczyć na dokładny zapis:
/// przy dopasowaniu nagłówków importowanego arkusza i przy dopasowaniu kategorii zwróconej przez AI.
/// </summary>
public static class TextNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
        }

        // „ł” nie rozkłada się przez FormD — trzeba je zamienić ręcznie.
        return sb.ToString().Replace('ł', 'l');
    }
}
