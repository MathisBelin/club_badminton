using System.IO;
using System.Text;
using BadmintonClub.Models;

namespace BadmintonClub.Services;

/// <summary>
/// Export des contacts vers un fichier CSV (séparateur « ; », UTF-8 avec BOM pour Excel FR).
/// Les en-têtes sont compatibles avec l'import.
/// </summary>
public static class CsvContactExporter
{
    private const char Delimiter = ';';

    public static void Export(string path, IEnumerable<Adherent> adherents)
    {
        var sb = new StringBuilder();
        sb.Append("Nom;Prénom;Téléphone;E-mail\r\n");

        foreach (var a in adherents)
        {
            sb.Append(Escape(a.Nom)).Append(Delimiter)
              .Append(Escape(a.Prenom)).Append(Delimiter)
              .Append(Escape(a.Telephone)).Append(Delimiter)
              .Append(Escape(a.Email)).Append("\r\n");
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
