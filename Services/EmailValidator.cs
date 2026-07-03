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
