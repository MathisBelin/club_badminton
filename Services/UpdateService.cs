using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace BadmintonClub.Services;

/// <summary>Une mise à jour disponible : version + URL de téléchargement.</summary>
public sealed record UpdateInfo(string Version, string Url);

/// <summary>
/// Vérifie s'il existe une version plus récente publiée en « Release » sur GitHub.
/// Silencieux en cas d'erreur (hors ligne, aucune release, etc.).
/// </summary>
public static class UpdateService
{
    private const string Repo = "MathisBelin/club_badminton";

    /// <summary>Version courante de l'application (ex. « 1.0.0 »).</summary>
    public static string CurrentVersion { get; } = ReadCurrentVersion();

    public static async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ClubBadminton-Updater");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            var json = await http.GetStringAsync(
                $"https://api.github.com/repos/{Repo}/releases/latest");

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            var url = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";

            // On privilégie un asset .exe (l'installeur) s'il est joint à la release.
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                        a.TryGetProperty("browser_download_url", out var d))
                    {
                        url = d.GetString() ?? url;
                        break;
                    }
                }
            }

            var latest = tag.TrimStart('v', 'V').Trim();
            if (Version.TryParse(latest, out var lv) &&
                Version.TryParse(CurrentVersion, out var cv) &&
                lv > cv)
            {
                return new UpdateInfo(latest, url);
            }

            return null;
        }
        catch
        {
            return null; // silencieux
        }
    }

    private static string ReadCurrentVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);
        return $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
