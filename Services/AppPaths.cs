using System.IO;

namespace BadmintonClub.Services;

/// <summary>
/// Centralise les chemins de fichiers utilisés par l'application.
/// On stocke les données dans %LOCALAPPDATA%\BadmintonClub pour éviter
/// les problèmes de droits si l'exe est installé dans Program Files.
/// </summary>
public static class AppPaths
{
    public static string DataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BadmintonClub");

    public static string SettingsFile => Path.Combine(DataFolder, "settings.json");

    public static string DefaultAdherentsFile => Path.Combine(DataFolder, "adherents.json");

    /// <summary>Registre local des Google Sheets créés par l'appli.</summary>
    public static string WorksheetsFile => Path.Combine(DataFolder, "worksheets.json");

    /// <summary>Dossier de stockage des modèles de Sheets (Excel/CSV).</summary>
    public static string ModelsFolder => Path.Combine(DataFolder, "modeles");

    public static void EnsureModelsFolder() => Directory.CreateDirectory(ModelsFolder);

    /// <summary>Dossier de stockage des modèles de mails.</summary>
    public static string MailTemplatesFolder => Path.Combine(DataFolder, "mails");

    public static void EnsureMailTemplatesFolder() => Directory.CreateDirectory(MailTemplatesFolder);

    /// <summary>Dossier des données par compte Google.</summary>
    public static string AccountsFolder => Path.Combine(DataFolder, "accounts");

    public static string AccountFolder(string account) => Path.Combine(AccountsFolder, SanitizeAccount(account));

    // Le compte « par défaut » (aucun compte connu) conserve les anciens fichiers à la racine
    // du dossier de données, pour rester rétro-compatible avec les données existantes.
    public static string AdherentsFileFor(string account) =>
        SanitizeAccount(account) == "default"
            ? DefaultAdherentsFile
            : Path.Combine(AccountFolder(account), "adherents.json");

    public static string WorksheetsFileFor(string account) =>
        SanitizeAccount(account) == "default"
            ? WorksheetsFile
            : Path.Combine(AccountFolder(account), "worksheets.json");

    private static string SanitizeAccount(string account)
    {
        if (string.IsNullOrWhiteSpace(account))
            return "default";
        foreach (var c in Path.GetInvalidFileNameChars())
            account = account.Replace(c, '_');
        return account;
    }

    /// <summary>Dossier où l'OAuth stocke le token rafraîchissable.</summary>
    public static string TokenStoreFolder => Path.Combine(DataFolder, "google_token");

    /// <summary>client_secret.json attendu à côté de l'exe.</summary>
    public static string ClientSecretFile => Path.Combine(AppContext.BaseDirectory, "client_secret.json");

    /// <summary>config.json optionnel à côté de l'exe : pré-remplit les paramètres au 1er lancement.</summary>
    public static string SeedConfigFile => Path.Combine(AppContext.BaseDirectory, "config.json");

    public static void EnsureDataFolder() => Directory.CreateDirectory(DataFolder);
}
