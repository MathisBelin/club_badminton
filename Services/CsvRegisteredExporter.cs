using System.IO;
using System.Text;

namespace BadmintonClub.Services;

/// <summary>
/// Export CSV des personnes inscrites affichées (page « ✅ Inscrits »), séparateur « ; »,
/// UTF-8 avec BOM pour Excel FR. Les colonnes reprennent celles du tableau (contact tel quel).
/// </summary>
public static class CsvRegisteredExporter
{
    private const char Delimiter = ';';

    /// <summary>Interface minimale d'une ligne de la page des inscrits.</summary>
    public interface IRegisteredLine
    {
        string Nom { get; }
        string Prenom { get; }
        string Telephone { get; }
        string Email { get; }
        IReadOnlyList<string> SecondaryEmails { get; }
        string AddedText { get; }
        bool NotInContacts { get; }
    }

    public static void Export(string path, IEnumerable<IRegisteredLine> lines)
    {
        var sb = new StringBuilder();
        sb.Append("Nom;Prénom;Téléphone;E-mail;Mails secondaires;Ajouté le;Dans mes contacts\r\n");

        foreach (var l in lines)
        {
            sb.Append(Escape(l.Nom)).Append(Delimiter)
              .Append(Escape(l.Prenom)).Append(Delimiter)
              .Append(Escape(l.Telephone)).Append(Delimiter)
              .Append(Escape(l.Email)).Append(Delimiter)
              .Append(Escape(string.Join(", ", l.SecondaryEmails))).Append(Delimiter)
              .Append(Escape(l.AddedText)).Append(Delimiter)
              .Append(l.NotInContacts ? "Non" : "Oui").Append("\r\n");
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
