using System.Text;

namespace BadmintonClub.Helpers;

/// <summary>Formatage et validation des numéros de téléphone (format français).</summary>
public static class PhoneFormatter
{
    /// <summary>
    /// Nettoie et met en forme : ne garde que les chiffres, ajoute un « 0 » en tête si absent,
    /// et insère un espace tous les deux chiffres (ex. « 06 12 34 56 78 »).
    /// </summary>
    public static string Format(string? raw)
    {
        var digits = OnlyDigits(raw);
        if (digits.Length == 0)
            return string.Empty;
        if (!digits.StartsWith('0'))
            digits = "0" + digits;

        var sb = new StringBuilder();
        for (var i = 0; i < digits.Length; i += 2)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(digits.Substring(i, Math.Min(2, digits.Length - i)));
        }
        return sb.ToString();
    }

    /// <summary>Numéro valide (10 chiffres commençant par 0). Un champ vide est considéré valide (ignoré).</summary>
    public static bool IsValid(string? raw)
    {
        var digits = OnlyDigits(raw);
        if (digits.Length == 0)
            return true;
        if (!digits.StartsWith('0'))
            digits = "0" + digits;
        return digits.Length == 10;
    }

    private static string OnlyDigits(string? s)
        => new((s ?? string.Empty).Where(char.IsDigit).ToArray());
}
