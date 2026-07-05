using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace BadmintonClub.Services;

/// <summary>Un navigateur installé (nom affiché + chemin de l'exécutable).</summary>
public sealed record BrowserInfo(string Name, string ExecutablePath);

/// <summary>
/// Détecte les navigateurs installés sur la machine (via le registre Windows),
/// identifie le navigateur par défaut, et ouvre les URL avec le navigateur choisi.
/// </summary>
public static class BrowserService
{
    private const string StartMenuInternet = @"SOFTWARE\Clients\StartMenuInternet";
    private const string DefaultHttpsUserChoice =
        @"SOFTWARE\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice";

    /// <summary>
    /// Chemin de l'exe du navigateur choisi par l'utilisateur.
    /// Null ou vide = navigateur par défaut du système.
    /// </summary>
    public static string? SelectedBrowserPath { get; set; }

    /// <summary>Liste des navigateurs installés (dédoublonnés par exe), triés par nom.</summary>
    public static IReadOnlyList<BrowserInfo> GetInstalled()
    {
        var byExe = new Dictionary<string, BrowserInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var key = root.OpenSubKey(StartMenuInternet);
            if (key == null)
                continue;

            foreach (var subName in key.GetSubKeyNames())
            {
                using var sub = key.OpenSubKey(subName);
                if (sub == null)
                    continue;

                var display = sub.GetValue(null) as string;
                if (string.IsNullOrWhiteSpace(display))
                    display = subName;

                using var cmd = sub.OpenSubKey(@"shell\open\command");
                var exe = ExtractExePath(cmd?.GetValue(null) as string);

                if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                    continue;

                if (!byExe.ContainsKey(exe))
                    byExe[exe] = new BrowserInfo(display, exe);
            }
        }

        return byExe.Values.OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Chemin de l'exe du navigateur par défaut du système, ou null si indéterminable.</summary>
    public static string? GetDefaultExecutablePath()
    {
        try
        {
            using var choice = Registry.CurrentUser.OpenSubKey(DefaultHttpsUserChoice);
            var progId = choice?.GetValue("ProgId") as string;
            if (string.IsNullOrEmpty(progId))
                return null;

            using var cmd = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
            return ExtractExePath(cmd?.GetValue(null) as string);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Ouvre Gmail avec un nouveau message pré-adressé à <paramref name="email"/>.</summary>
    public static void OpenGmailCompose(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return;
        Open($"https://mail.google.com/mail/?view=cm&fs=1&to={Uri.EscapeDataString(email)}");
    }

    /// <summary>Ouvre Gmail avec un nouveau message adressé à plusieurs destinataires (objet/corps optionnels).</summary>
    public static void OpenGmailComposeMany(IEnumerable<string> emails, string? subject = null, string? body = null)
    {
        var list = emails
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (list.Count == 0)
            return;

        var to = string.Join(",", list.Select(Uri.EscapeDataString));
        var url = $"https://mail.google.com/mail/?view=cm&fs=1&to={to}";
        if (!string.IsNullOrWhiteSpace(subject))
            url += $"&su={Uri.EscapeDataString(subject)}";
        if (!string.IsNullOrWhiteSpace(body))
            url += $"&body={Uri.EscapeDataString(body)}";
        Open(url);
    }

    /// <summary>Ouvre une URL avec le navigateur sélectionné, sinon avec le navigateur par défaut.</summary>
    public static void Open(string url)
    {
        var path = SelectedBrowserPath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                // URL passée en argument (pas via le shell) : les « & » ne posent pas de problème.
                var psi = new ProcessStartInfo(path) { UseShellExecute = false };
                psi.ArgumentList.Add(url);
                Process.Start(psi);
                return;
            }
            catch
            {
                // Repli sur le navigateur par défaut ci-dessous.
            }
        }

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    /// <summary>Extrait le chemin de l'exe d'une commande du registre (gère les guillemets et arguments).</summary>
    private static string? ExtractExePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        command = command.Trim();

        if (command.StartsWith("\""))
        {
            var end = command.IndexOf('"', 1);
            if (end > 1)
                return command.Substring(1, end - 1);
        }

        var idx = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (idx > 0)
            return command.Substring(0, idx + 4);

        return command;
    }
}
