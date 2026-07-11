using BadmintonClub.Models;

namespace BadmintonClub.Services;

/// <summary>Niveau de correspondance d'une personne en attente avec les contacts existants.</summary>
public enum MatchLevel
{
    Connue,   // vert   : au moins 2 colonnes renseignées correspondent EXACTEMENT à un seul contact
    Doute,    // jaune  : correspondance partielle / ambiguë
    Inconnue  // rouge  : aucune correspondance
}

public sealed record PendingMatch(MatchLevel Level, List<Adherent> Candidates);

/// <summary>
/// Évalue à quel(s) contact(s) existant(s) une personne en attente (nom/prénom/tél, sans e-mail)
/// pourrait correspondre.
/// </summary>
public static class PendingMatcher
{
    public static PendingMatch Match(PendingPerson p, IEnumerable<Adherent> adherents)
    {
        var list = adherents.ToList();

        var hasNom = !string.IsNullOrWhiteSpace(p.Nom);
        var hasPrenom = !string.IsNullOrWhiteSpace(p.Prenom);
        var hasTel = !string.IsNullOrWhiteSpace(p.Telephone);
        var filled = (hasNom ? 1 : 0) + (hasPrenom ? 1 : 0) + (hasTel ? 1 : 0);

        // Correspondance exacte : TOUTES les colonnes renseignées de la personne collent au contact.
        bool ExactAll(Adherent a) =>
            (!hasNom || Eq(p.Nom, a.Nom)) &&
            (!hasPrenom || Eq(p.Prenom, a.Prenom)) &&
            (!hasTel || DigitsEq(p.Telephone, a.Telephone));

        var exact = filled > 0 ? list.Where(ExactAll).ToList() : new List<Adherent>();

        // Connue : au moins 2 colonnes renseignées et exactement UN contact correspond.
        if (filled >= 2 && exact.Count == 1)
            return new PendingMatch(MatchLevel.Connue, exact);

        // Doute : correspondances exactes multiples/uniques-sur-1-colonne, OU nom/prénom contenu
        // dans l'e-mail d'un contact.
        var cand = new List<Adherent>(exact);
        foreach (var a in list)
        {
            if (cand.Contains(a))
                continue;
            var email = a.Email ?? string.Empty;
            if ((hasNom && email.Contains(p.Nom.Trim(), StringComparison.OrdinalIgnoreCase)) ||
                (hasPrenom && email.Contains(p.Prenom.Trim(), StringComparison.OrdinalIgnoreCase)))
                cand.Add(a);
        }

        return cand.Count > 0
            ? new PendingMatch(MatchLevel.Doute, cand)
            : new PendingMatch(MatchLevel.Inconnue, cand);
    }

    private static bool Eq(string? a, string? b)
        => !string.IsNullOrWhiteSpace(a) &&
           string.Equals(a.Trim(), (b ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Digits(string? s) => new string((s ?? string.Empty).Where(char.IsDigit).ToArray());

    private static bool DigitsEq(string? a, string? b)
    {
        var da = Digits(a);
        return da.Length > 0 && da == Digits(b);
    }
}
