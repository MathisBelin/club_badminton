namespace BadmintonClub.Models;

/// <summary>
/// Paramètres de l'application, sérialisés dans settings.json.
/// </summary>
public class AppSettings
{
    /// <summary>Nom de l'étiquette / groupe de contacts Google cible.</summary>
    public string GmailLabel { get; set; } = "Club Badminton";

    /// <summary>Adresse e-mail du club. Les Sheets créés sont aussi partagés avec elle (éditeur).</summary>
    public string ClubEmail { get; set; } = string.Empty;

    /// <summary>URL du Google Sheet des événements du club.</summary>
    public string GoogleSheetUrl { get; set; } = string.Empty;

    /// <summary>
    /// Chemin du fichier JSON des adhérents. Optionnel : si vide, on utilise
    /// adherents.json dans le dossier de l'application.
    /// </summary>
    public string AdherentsJsonPath { get; set; } = string.Empty;

    /// <summary>Active la synchronisation avec Google Contacts lors des ajouts/suppressions.</summary>
    public bool SyncGoogleEnabled { get; set; } = false;

    /// <summary>
    /// Chemin de l'exécutable du navigateur choisi pour ouvrir les liens.
    /// Vide = navigateur par défaut du système.
    /// </summary>
    public string BrowserPath { get; set; } = string.Empty;

    /// <summary>Adresse du compte Google actuellement connecté (données stockées par compte).</summary>
    public string CurrentAccount { get; set; } = string.Empty;

    /// <summary>
    /// Adresse de l'application web des formulaires d'inscription (projet bad-web).
    /// Vide = valeur par défaut (<see cref="Services.WebFormsService.DefaultBaseUrl"/>).
    /// </summary>
    public string WebFormsUrl { get; set; } = string.Empty;

    /// <summary>
    /// Clé de l'API d'intégration de l'application web (en-tête x-api-key).
    /// SECRET : renseignée via config.json ou l'écran Paramètres, jamais versionnée.
    /// </summary>
    public string WebFormsApiKey { get; set; } = string.Empty;
}
