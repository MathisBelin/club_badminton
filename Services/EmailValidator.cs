using System.Text.RegularExpressions;

namespace BadmintonClub.Services;

/// <summary>Validation et correction heuristique d'adresses e-mail.</summary>
public static partial class EmailValidator
{
    // Local et domaine sans espaces ni virgules ; domaine avec au moins un point + TLD >= 2.
    [GeneratedRegex(@"^[^@\s,]+@[^@\s,]+\.[^@\s,]{2,}$")]
    private static partial Regex EmailRegex();

    public static bool IsValid(string? email)
        => !string.IsNullOrWhiteSpace(email) && EmailRegex().IsMatch(email.Trim());

    /// <summary>
    /// Vrai si l'e-mail est valide tel quel, OU le devient après correction automatique
    /// (<see cref="Suggest"/> : virgule→point, espaces…). Sert à distinguer un e-mail
    /// réellement au mauvais format d'une simple faute de frappe rattrapable.
    /// </summary>
    public static bool IsValidOrFixable(string? email)
        => IsValid(email) || IsValid(Suggest(email));

    /// <summary>
    /// Propose une correction : supprime les espaces, remplace les virgules par des points,
    /// fusionne les points doubles et enlève les points en début/fin.
    /// </summary>
    public static string Suggest(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return string.Empty;

        var s = email.Trim().Replace(" ", string.Empty).Replace(",", ".");
        while (s.Contains(".."))
            s = s.Replace("..", ".");
        s = s.Trim('.');
        return s;
    }
}
