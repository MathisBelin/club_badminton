using System.IO;
using System.Reflection;

namespace BadmintonClub.Services;

/// <summary>
/// Journalise les erreurs / plantages dans un fichier texte lisible, pour pouvoir
/// diagnostiquer un problème survenu sur un autre PC (l'appli n'affiche pas toujours
/// de message si elle plante au démarrage).
///
/// Emplacement : %LOCALAPPDATA%\BadmintonClub\log_error.txt
/// (repli : à côté de l'exe si le dossier n'est pas accessible).
/// Le journal est tronqué s'il dépasse ~1 Mo. La journalisation ne lève jamais d'exception.
/// </summary>
public static class ErrorLogger
{
    private static readonly object Sync = new();

    /// <summary>Chemin du fichier de journal (public pour l'afficher à l'utilisateur).</summary>
    public static string LogPath { get; } = ResolvePath();

    /// <summary>Enregistre une exception avec un contexte (ex. « Démarrage », « Synchro auto »).</summary>
    public static void Log(string context, Exception ex) => Write(context, ex.ToString());

    /// <summary>Enregistre un message simple.</summary>
    public static void Log(string context, string message) => Write(context, message);

    private static void Write(string context, string details)
    {
        try
        {
            lock (Sync)
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                // Tronque si le fichier devient trop gros.
                try
                {
                    if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1_000_000)
                        File.Delete(LogPath);
                }
                catch { /* ignore */ }

                var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";
                var entry =
                    $"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} — {context} =====" + Environment.NewLine +
                    $"App v{version} | OS {Environment.OSVersion} | .NET {Environment.Version} | 64-bit={Environment.Is64BitProcess}" + Environment.NewLine +
                    details + Environment.NewLine + Environment.NewLine;

                File.AppendAllText(LogPath, entry);
            }
        }
        catch
        {
            // La journalisation ne doit jamais faire planter l'application.
        }
    }

    private static string ResolvePath()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BadmintonClub");
            return Path.Combine(dir, "log_error.txt");
        }
        catch
        {
            // Repli : à côté de l'exécutable.
            return Path.Combine(AppContext.BaseDirectory, "log_error.txt");
        }
    }
}
