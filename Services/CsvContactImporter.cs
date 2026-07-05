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
    /// <summary>Colonnes détectées : ligne d'en-têtes et index de chaque champ connu.</summary>
    public sealed record ColumnMapping(int HeaderRowIndex, IReadOnlyDictionary<string, int> Columns);

    public static List<Adherent> Parse(string path) => FromRows(ReadRows(path));

    // ---- Conversions lettre ↔ index de colonne ---------------------------

    /// <summary>Index absolu (0 = A) d'une lettre de colonne (« B » → 1, « AA » → 26).</summary>
    public static int ColumnLetterToIndex(string letters)
    {
        var idx = 0;
        foreach (var c in letters.ToUpperInvariant())
            idx = idx * 26 + (c - 'A' + 1);
        return idx - 1;
    }

    /// <summary>Lettre de colonne à partir d'un index 0 (0 → A, 1 → B, 27 → AB).</summary>
    public static string ColumnLetter(int index)
    {
        index++;
        var s = string.Empty;
        while (index > 0)
        {
            index--;
            s = (char)('A' + index % 26) + s;
            index /= 26;
        }
        return s;
    }

    /// <summary>Index absolu d'une lettre de colonne ; null si la lettre est vide.</summary>
    public static int? ColIndex(string letter)
        => string.IsNullOrWhiteSpace(letter) ? null : ColumnLetterToIndex(letter.Trim());

    /// <summary>Lit un fichier CSV en lignes déjà découpées (séparateur auto-détecté).</summary>
    public static List<string[]> ReadRows(string path)
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

        return rows;
    }

    /// <summary>
    /// Construit les adhérents à partir de colonnes IMPOSÉES (indices relatifs aux lignes lues,
    /// 0 = 1re colonne lue). Aucune détection d'en-têtes : on garde chaque ligne dont la colonne
    /// e-mail contient une adresse (ce qui écarte naturellement titres et en-têtes).
    /// </summary>
    public static List<Adherent> BuildFromColumns(
        IReadOnlyList<string[]> rows, int? nom, int? prenom, int? tel, int? email)
    {
        var result = new List<Adherent>();
        foreach (var fields in rows)
        {
            string Get(int? idx) =>
                idx is int i && i >= 0 && i < fields.Length ? fields[i].Trim() : string.Empty;

            var mail = Get(email);
            if (string.IsNullOrWhiteSpace(mail) || !mail.Contains('@'))
                continue; // ligne de titre / en-tête / vide

            result.Add(new Adherent
            {
                Nom = Get(nom),
                Prenom = Get(prenom),
                Email = mail,
                Telephone = Helpers.PhoneFormatter.Format(Get(tel))
            });
        }
        return result;
    }

    /// <summary>
    /// Lignes ayant des informations (nom / prénom / téléphone) mais SANS e-mail (colonne e-mail
    /// vide ou sans « @ »). Sert à repérer les inscriptions incomplètes.
    /// </summary>
    public static List<(string Nom, string Prenom, string Tel)> BuildIncompleteFromColumns(
        IReadOnlyList<string[]> rows, int? nom, int? prenom, int? tel, int? email)
    {
        var result = new List<(string, string, string)>();
        foreach (var fields in rows)
        {
            string Get(int? idx) =>
                idx is int i && i >= 0 && i < fields.Length ? fields[i].Trim() : string.Empty;

            var mail = Get(email);
            if (mail.Contains('@'))
                continue; // a un e-mail → traité comme contact normal

            var n = Get(nom);
            var p = Get(prenom);
            var t = Get(tel);
            if (string.IsNullOrWhiteSpace(n) && string.IsNullOrWhiteSpace(p) && string.IsNullOrWhiteSpace(t))
                continue; // ligne vide

            result.Add((n, p, Helpers.PhoneFormatter.Format(t)));
        }
        return result;
    }

    /// <summary>Ne conserve que les lignes de <paramref name="startRow"/> à <paramref name="endRow"/> (1-based ; endRow ≤ 0 = jusqu'à la fin).</summary>
    public static IReadOnlyList<string[]> SliceRows(IReadOnlyList<string[]> rows, int startRow, int endRow)
    {
        var from = Math.Max(1, startRow) - 1;
        if (from >= rows.Count)
            return System.Array.Empty<string[]>();
        var count = endRow >= from + 1 ? Math.Min(endRow, rows.Count) - from : rows.Count - from;
        return rows.Skip(from).Take(count).ToList();
    }

    /// <summary>Résultat d'une vérification de colonnes (les 4 infos ressortent-elles ?).</summary>
    public sealed record ColumnCheckResult(bool Ok, List<string> Missing, List<Adherent> Contacts);

    /// <summary>
    /// Vérifie que les colonnes indiquées (lettres) donnent bien les 4 informations sur les
    /// lignes fournies. Une info est validée si sa colonne est renseignée ET qu'au moins une
    /// ligne en donne une valeur (colonnes non adjacentes acceptées).
    /// </summary>
    public static ColumnCheckResult CheckColumns(
        IReadOnlyList<string[]> rows, string colNom, string colPrenom, string colTel, string colEmail)
    {
        var contacts = BuildFromColumns(rows, ColIndex(colNom), ColIndex(colPrenom), ColIndex(colTel), ColIndex(colEmail))
            .Where(c => EmailValidator.IsValid(c.Email)).ToList();

        bool Ok(string letter, Func<Adherent, string> sel) =>
            !string.IsNullOrWhiteSpace(letter) && contacts.Any(a => !string.IsNullOrWhiteSpace(sel(a)));

        var missing = new List<string>();
        if (!Ok(colNom, a => a.Nom)) missing.Add("Nom");
        if (!Ok(colPrenom, a => a.Prenom)) missing.Add("Prénom");
        if (!Ok(colTel, a => a.Telephone)) missing.Add("Téléphone");
        if (!Ok(colEmail, a => a.Email)) missing.Add("E-mail");

        return new ColumnCheckResult(missing.Count == 0 && contacts.Count > 0, missing, contacts);
    }

    /// <summary>Message affichable (verdict + colonnes + exemple) pour un résultat de vérification.</summary>
    public static string BuildCheckMessage(
        ColumnCheckResult r, string colNom, string colPrenom, string colTel, string colEmail)
    {
        string Col(string letter, string label) =>
            string.IsNullOrWhiteSpace(letter)
                ? $"{label} → non renseignée"
                : $"{label} → colonne {letter.Trim().ToUpperInvariant()}";

        var sb = new StringBuilder();
        sb.AppendLine(r.Ok
            ? "✔ Test réussi — les 4 informations sont bien lues."
            : r.Contacts.Count == 0
                ? "✘ Test non validé — aucune ligne avec e-mail lue (vérifiez les colonnes et les lignes début/fin)."
                : $"✘ Test non validé — information(s) manquante(s) : {string.Join(", ", r.Missing)}.");
        sb.AppendLine();
        sb.AppendLine("   • " + Col(colNom, "Nom"));
        sb.AppendLine("   • " + Col(colPrenom, "Prénom"));
        sb.AppendLine("   • " + Col(colTel, "Téléphone"));
        sb.Append("   • " + Col(colEmail, "E-mail"));

        var sample = r.Contacts.FirstOrDefault();
        if (sample != null)
            sb.Append($"\n\n{r.Contacts.Count} ligne(s) lue(s). Exemple : {sample.Nom} {sample.Prenom} — {sample.Telephone} — {sample.Email}");

        return sb.ToString();
    }

    /// <summary>Détecte la ligne d'en-têtes et le mappage des colonnes (null si introuvable).</summary>
    public static ColumnMapping? DetectColumns(IReadOnlyList<string[]> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var candidate = MapHeader(rows[i]);
            if (candidate.ContainsKey("nom") || candidate.ContainsKey("email"))
                return new ColumnMapping(i, candidate);
        }
        return null;
    }

    /// <summary>
    /// Construit les adhérents à partir de lignes déjà découpées (CSV ou Excel).
    /// Détecte la ligne d'en-têtes et n'importe QUE les entrées ayant un e-mail.
    /// </summary>
    public static List<Adherent> FromRows(IReadOnlyList<string[]> rows)
    {
        var mapping = DetectColumns(rows);
        if (mapping == null)
            throw new InvalidDataException(
                "Impossible de trouver les colonnes attendues (NOM, PRENOM, TEL, e-mail) dans le fichier.");

        var headerIndex = mapping.HeaderRowIndex;
        var map = mapping.Columns;

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
                Telephone = Helpers.PhoneFormatter.Format(Get("tel"))
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
            // Un en-tête e-mail ne contient jamais « @ » (sinon c'est une donnée, pas un en-tête).
            else if ((n.Contains("MAIL") || n.Contains("COURRIEL") || n == "EMAIL") && !n.Contains('@'))
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
