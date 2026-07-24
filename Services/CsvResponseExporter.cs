using System.IO;
using System.Text;

namespace BadmintonClub.Services;

/// <summary>
/// Export CSV des réponses affichées d'un formulaire d'inscription (séparateur « ; »,
/// UTF-8 avec BOM pour Excel FR). Les colonnes reprennent celles du tableau : ce sont
/// donc les informations du contact rapproché, ou celles de la réponse à défaut.
/// </summary>
public static class CsvResponseExporter
{
    private const char Delimiter = ';';

    /// <summary>Interface minimale attendue d'une ligne du tableau des réponses.</summary>
    public interface IResponseLine
    {
        int Rang { get; }
        string Nom { get; }
        string Prenom { get; }
        string Telephone { get; }
        string EmailText { get; }
        IReadOnlyList<string> SecondaryEmails { get; }
        DateTime SubmittedAt { get; }
        string ModifiedText { get; }
        string Status { get; }
        bool NotInContacts { get; }
        IReadOnlyList<string> DiffFields { get; }
    }

    /// <param name="withRank">Vrai en mode liste d'attente : ajoute la colonne « Rang ».</param>
    public static void Export(string path, IEnumerable<IResponseLine> lines, bool withRank)
    {
        var sb = new StringBuilder();
        if (withRank)
            sb.Append("Rang").Append(Delimiter);
        sb.Append("Nom;Prénom;Téléphone;E-mail;Mails secondaires;Répondu le;Modifié le;Statut;")
          .Append("Dans mes contacts;Infos différentes\r\n");

        foreach (var l in lines)
        {
            if (withRank)
                sb.Append(l.Rang).Append(Delimiter);
            sb.Append(Escape(l.Nom)).Append(Delimiter)
              .Append(Escape(l.Prenom)).Append(Delimiter)
              .Append(Escape(l.Telephone)).Append(Delimiter)
              .Append(Escape(l.EmailText)).Append(Delimiter)
              .Append(Escape(string.Join(", ", l.SecondaryEmails))).Append(Delimiter)
              .Append(l.SubmittedAt.ToString("dd/MM/yyyy HH:mm")).Append(Delimiter)
              .Append(Escape(l.ModifiedText)).Append(Delimiter)
              .Append(Escape(l.Status)).Append(Delimiter)
              .Append(l.NotInContacts ? "Non" : "Oui").Append(Delimiter)
              .Append(Escape(string.Join(", ", l.DiffFields))).Append("\r\n");
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string Escape(string? value)
    {
        var v = value ?? string.Empty;
        if (v.Contains(Delimiter) || v.Contains('"') || v.Contains('\n') || v.Contains('\r'))
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        return v;
    }
}
