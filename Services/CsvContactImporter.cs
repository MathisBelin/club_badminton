using System.Globalization;
using System.IO;
using System.Text;
using BadmintonClub.Models;

namespace BadmintonClub.Services;

/// <summary>
/// Import de contacts depuis un fichier CSV. Détecte automatiquement le séparateur
/// (; , ou tabulation), la ligne d'en-têtes, et mappe les colonnes NOM / PRENOM /
/// TÉLÉPHONE / E-MAIL même si des lignes de titre les précèdent.
/// </summary>
public static class CsvContactImporter
{
    public static List<Adherent> Parse(string path)
    {
        var text = ReadText(path);
        var rawLines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        var delimiter = DetectDelimiter(rawLines);

        var rows = new List<string[]>();
        foreach (var line in rawLines)
        {
            if (line.Length == 0)
                continue;
            rows.Add(SplitCsvLine(line, delimiter));
        }

        return FromRows(rows);
    }

    /// <summary>
    /// Construit les adhérents à partir de lignes déjà découpées (CSV ou Excel).
    /// Détecte la ligne d'en-têtes et n'importe QUE les entrées ayant un e-mail.
    /// </summary>
    public static List<Adherent> FromRows(IReadOnlyList<string[]> rows)
    {
        var headerIndex = -1;
        Dictionary<string, int>? map = null;
        for (var i = 0; i < rows.Count; i++)
        {
            var candidate = MapHeader(rows[i]);
            if (candidate.ContainsKey("nom") || candidate.ContainsKey("email"))
            {
                headerIndex = i;
                map = candidate;
                break;
            }
        }

        if (map == null)
            throw new InvalidDataException(
                "Impossible de trouver les colonnes attendues (NOM, PRENOM, TEL, e-mail) dans le fichier.");

        var result = new List<Adherent>();
        for (var i = headerIndex + 1; i < rows.Count; i++)
        {
            var fields = rows[i];

            string Get(string key) =>
                map.TryGetValue(key, out var idx) && idx < fields.Length
                    ? fields[idx].Trim()
                    : string.Empty;

            var email = Get("email");

            // On n'importe pas les entrées sans e-mail.
            if (string.IsNullOrWhiteSpace(email))
                continue;

            result.Add(new Adherent
            {
                Nom = Get("nom"),
                Prenom = Get("prenom"),
                Email = email,
                Telephone = Get("tel")
            });
        }

        return result;
    }

    // ---- Détails ----------------------------------------------------------

    private static Dictionary<string, int> MapHeader(string[] fields)
    {
        var map = new Dictionary<string, int>();
        for (var i = 0; i < fields.Length; i++)
        {
            var n = Normalize(fields[i]);
            if (string.IsNullOrEmpty(n))
                continue;

            if (n.StartsWith("PRENOM"))
                map.TryAdd("prenom", i);
            else if (n == "NOM")
                map.TryAdd("nom", i);
            else if (n.Contains("MAIL") || n.Contains("COURRIEL") || n == "EMAIL")
                map.TryAdd("email", i);
            else if (n == "TEL" || n.Contains("TELEPHONE") || n.Contains("PORTABLE") ||
                     n.Contains("MOBILE") || n.Contains("NUMERO"))
                map.TryAdd("tel", i);
        }
        return map;
    }

    private static char DetectDelimiter(IEnumerable<string> lines)
    {
        var candidates = new[] { ';', ',', '\t' };
        var best = ';';
        var bestCount = -1;
        foreach (var c in candidates)
        {
            var count = lines.Sum(l => l.Count(ch => ch == c));
            if (count > bestCount)
            {
                bestCount = count;
                best = c;
            }
        }
        return best;
    }

    private static string[] SplitCsvLine(string line, char delimiter)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == delimiter)
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        result.Add(sb.ToString());
        return result.ToArray();
    }

    /// <summary>Majuscules sans accents, pour comparer les en-têtes de façon tolérante.</summary>
    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var formD = value.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in formD)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Lit le fichier en UTF-8 si possible, sinon en Latin-1 (exports Excel FR).</summary>
    private static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);

        // BOM UTF-8 ?
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }
}
