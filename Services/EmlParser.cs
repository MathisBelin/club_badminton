using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace BadmintonClub.Services;

/// <summary>
/// Analyse un fichier .eml (e-mail téléchargé) pour en extraire l'objet et le corps texte.
/// Gère l'encodage RFC 2047 des en-têtes, le multipart/alternative, quoted-printable et base64.
/// </summary>
public static partial class EmlParser
{
    public static (string Subject, string Body) Parse(string path)
    {
        var raw = File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n").Replace("\r", "\n");

        var sep = raw.IndexOf("\n\n", StringComparison.Ordinal);
        var headerText = sep >= 0 ? raw[..sep] : raw;
        var body = sep >= 0 ? raw[(sep + 2)..] : string.Empty;

        var headers = ParseHeaders(headerText);

        var subject = DecodeRfc2047(headers.GetValueOrDefault("subject", ""));
        var contentType = headers.GetValueOrDefault("content-type", "");
        var cte = headers.GetValueOrDefault("content-transfer-encoding", "");

        string text;
        if (contentType.Contains("multipart", StringComparison.OrdinalIgnoreCase))
        {
            var boundary = Regex.Match(contentType, @"boundary=""?([^"";\s]+)""?").Groups[1].Value;
            text = ExtractPlainFromMultipart(body, boundary);
        }
        else
        {
            text = Decode(body, cte, GetEncoding(contentType));
        }

        return (subject.Trim(), text.Trim());
    }

    // ---- En-têtes ---------------------------------------------------------

    private static Dictionary<string, string> ParseHeaders(string headerText)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? key = null;
        var value = new StringBuilder();

        foreach (var line in headerText.Split('\n'))
        {
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
            {
                value.Append(' ').Append(line.Trim());
                continue;
            }

            if (key != null)
                result[key] = value.ToString();

            var idx = line.IndexOf(':');
            if (idx > 0)
            {
                key = line[..idx].Trim();
                value.Clear();
                value.Append(line[(idx + 1)..].Trim());
            }
            else
            {
                key = null;
            }
        }
        if (key != null)
            result[key] = value.ToString();
        return result;
    }

    private static string ExtractPlainFromMultipart(string body, string boundary)
    {
        if (string.IsNullOrEmpty(boundary))
            return body;

        var parts = body.Split(new[] { "--" + boundary }, StringSplitOptions.None);
        string? htmlFallback = null;

        foreach (var part in parts)
        {
            var trimmed = part.TrimStart('\n');
            var sep = trimmed.IndexOf("\n\n", StringComparison.Ordinal);
            if (sep < 0) continue;

            var partHeaders = ParseHeaders(trimmed[..sep]);
            var ct = partHeaders.GetValueOrDefault("content-type", "");
            var cte = partHeaders.GetValueOrDefault("content-transfer-encoding", "");
            var content = trimmed[(sep + 2)..];

            if (ct.Contains("text/plain", StringComparison.OrdinalIgnoreCase))
                return Decode(content, cte, GetEncoding(ct));

            if (ct.Contains("text/html", StringComparison.OrdinalIgnoreCase))
                htmlFallback = HtmlToText(Decode(content, cte, GetEncoding(ct)));
        }

        return htmlFallback ?? string.Empty;
    }

    // ---- Décodage ---------------------------------------------------------

    private static string Decode(string content, string cte, Encoding encoding)
    {
        cte = cte.Trim().ToLowerInvariant();
        if (cte == "base64")
        {
            try
            {
                var clean = Regex.Replace(content, @"\s", "");
                return encoding.GetString(Convert.FromBase64String(clean));
            }
            catch { return content; }
        }
        if (cte == "quoted-printable")
            return DecodeQuotedPrintable(content, encoding);
        return content;
    }

    private static string DecodeQuotedPrintable(string input, Encoding encoding)
    {
        var bytes = new List<byte>();
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (c == '=' && i + 1 < input.Length)
            {
                if (input[i + 1] == '\n') { i++; continue; } // saut de ligne « souple »
                if (i + 2 < input.Length &&
                    Uri.IsHexDigit(input[i + 1]) && Uri.IsHexDigit(input[i + 2]))
                {
                    bytes.Add((byte)((Uri.FromHex(input[i + 1]) << 4) + Uri.FromHex(input[i + 2])));
                    i += 2;
                    continue;
                }
            }
            bytes.Add((byte)c);
        }
        return encoding.GetString(bytes.ToArray());
    }

    private static string DecodeRfc2047(string s)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;

        return Regex.Replace(s, @"=\?(?<cs>[^?]+)\?(?<enc>[BbQq])\?(?<txt>[^?]*)\?=", m =>
        {
            var encoding = GetEncoding("charset=" + m.Groups["cs"].Value);
            var txt = m.Groups["txt"].Value;
            if (string.Equals(m.Groups["enc"].Value, "B", StringComparison.OrdinalIgnoreCase))
            {
                try { return encoding.GetString(Convert.FromBase64String(txt)); }
                catch { return txt; }
            }
            return DecodeQuotedPrintable(txt.Replace('_', ' '), encoding);
        });
    }

    private static Encoding GetEncoding(string contentTypeOrCharset)
    {
        var m = Regex.Match(contentTypeOrCharset, @"charset=""?([^"";\s]+)""?", RegexOptions.IgnoreCase);
        var cs = m.Success ? m.Groups[1].Value.ToLowerInvariant() : "utf-8";
        if (cs.Contains("utf-8") || cs.Contains("utf8"))
            return Encoding.UTF8;
        return Encoding.Latin1; // ISO-8859-1 / windows-1252 approché
    }

    private static string HtmlToText(string html)
    {
        var text = Regex.Replace(html, @"<(br|/div|/p)\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", string.Empty);
        text = System.Net.WebUtility.HtmlDecode(text);
        return Regex.Replace(text, @"\n{3,}", "\n\n").Trim();
    }
}
