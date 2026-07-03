using System.Collections.ObjectModel;
using BadmintonClub.Models;

namespace BadmintonClub.Services;

/// <summary>
/// Conteneur des services et données partagés entre les différentes vues.
/// Évite de réinstancier repositories/services dans chaque écran.
/// </summary>
public class AppServices
{
    public AppSettings Settings { get; private set; }

    public SettingsService SettingsService { get; } = new();
    public GoogleContactsService Contacts { get; } = new();
    public GoogleSheetsService Sheets { get; } = new();
    public SheetRepository SheetRepository { get; private set; } = null!;

    public AdherentRepository AdherentRepository { get; private set; } = null!;
    public ObservableCollection<Adherent> Adherents { get; } = new();

    /// <summary>Compte Google connecté (données stockées par compte).</summary>
    public string CurrentAccount { get; private set; } = string.Empty;

    private List<LabelItem>? _labelsCache;

    public AppServices()
    {
        Settings = SettingsService.Load();
        BrowserService.SelectedBrowserPath = Settings.BrowserPath;

        BindAccount(Settings.CurrentAccount);
    }

    /// <summary>Déconnecte le compte Google (supprime les jetons, oublie la session).</summary>
    public void SignOut()
    {
        Contacts.Reset();
        GoogleAuth.SignOut();
    }

    /// <summary>Un jeton est-il déjà présent (connexion silencieuse possible au démarrage) ?</summary>
    public bool HasStoredToken => Contacts.HasStoredToken();

    /// <summary>Relie les dépôts locaux (adhérents, sheets) au compte donné et recharge les données.</summary>
    public void BindAccount(string account)
    {
        CurrentAccount = account ?? string.Empty;

        // Un chemin JSON personnalisé (Paramètres) prime ; sinon dossier propre au compte.
        var adherentsPath = string.IsNullOrWhiteSpace(Settings.AdherentsJsonPath)
            ? AppPaths.AdherentsFileFor(CurrentAccount)
            : Settings.AdherentsJsonPath;

        AdherentRepository = new AdherentRepository(adherentsPath);
        SheetRepository = new SheetRepository(AppPaths.WorksheetsFileFor(CurrentAccount));
        _labelsCache = null;
        ReloadAdherents();
    }

    /// <summary>
    /// Connexion Google : renvoie l'e-mail du compte. Si <paramref name="switchAccount"/>,
    /// déconnecte d'abord et force le choix du compte, puis relie les données à ce compte.
    /// </summary>
    public async Task<string> SignInAsync(bool switchAccount, CancellationToken ct = default)
    {
        if (switchAccount)
        {
            Contacts.Reset();
            GoogleAuth.SignOut();
        }

        var email = await Contacts.GetSignedInEmailAsync(promptSelectAccount: switchAccount, ct: ct);

        if (!string.IsNullOrEmpty(email) &&
            !string.Equals(email, CurrentAccount, StringComparison.OrdinalIgnoreCase))
        {
            // Migration UNIQUEMENT depuis le compte « par défaut » (aucun compte connu),
            // jamais entre deux comptes réels (sinon on copie les contacts de l'autre compte).
            if (string.IsNullOrEmpty(CurrentAccount))
                MigrateCurrentDataTo(email);

            Settings.CurrentAccount = email;
            SettingsService.Save(Settings);
            BindAccount(email);
        }

        return email;
    }

    /// <summary>
    /// Première liaison à un compte réel : reprend les données actuellement chargées
    /// (compte « par défaut ») dans le dossier du compte si celui-ci est vide.
    /// </summary>
    private void MigrateCurrentDataTo(string account)
    {
        try
        {
            var destAdh = AppPaths.AdherentsFileFor(account);
            var destWs = AppPaths.WorksheetsFileFor(account);

            var dir = System.IO.Path.GetDirectoryName(destAdh);
            if (!string.IsNullOrEmpty(dir))
                System.IO.Directory.CreateDirectory(dir);

            if (!System.IO.File.Exists(destAdh) && System.IO.File.Exists(AdherentRepository.Path)
                && !string.Equals(destAdh, AdherentRepository.Path, StringComparison.OrdinalIgnoreCase))
                System.IO.File.Copy(AdherentRepository.Path, destAdh);

            if (!System.IO.File.Exists(destWs) && System.IO.File.Exists(SheetRepository.Path)
                && !string.Equals(destWs, SheetRepository.Path, StringComparison.OrdinalIgnoreCase))
                System.IO.File.Copy(SheetRepository.Path, destWs);
        }
        catch
        {
            // Migration best-effort : en cas d'échec, la synchro reconstruit les données.
        }
    }

    public void ReloadAdherents()
    {
        Adherents.Clear();
        foreach (var a in AdherentRepository.Load())
            Adherents.Add(a);
    }

    public void SaveAdherents() => AdherentRepository.Save(Adherents);

    /// <summary>
    /// Liste des libellés, mise en cache. Ne rappelle l'API que si le cache est vide
    /// ou si <paramref name="forceRefresh"/> est demandé (après création/renommage/suppression).
    /// </summary>
    public async Task<IReadOnlyList<LabelItem>> GetLabelsAsync(bool forceRefresh = false)
    {
        if (_labelsCache == null || forceRefresh)
            _labelsCache = await Contacts.ListLabelsAsync();
        return _labelsCache;
    }

    /// <summary>Libellés déjà en cache (ou liste vide), sans appel réseau.</summary>
    public IReadOnlyList<LabelItem> CachedLabels => _labelsCache ?? new List<LabelItem>();

    /// <summary>
    /// Synchronisation deux sens avec Google Contacts :
    /// - contacts liés absents en ligne → supprimés localement (suppression Gmail répercutée) ;
    /// - contacts liés présents en ligne → champs mis à jour depuis Google (modif Gmail répercutée) ;
    /// - contacts locaux non liés → rapprochés par e-mail, sinon poussés vers Google ;
    /// - contacts Google non présents localement → ajoutés.
    /// </summary>
    public async Task SyncContactsAsync()
    {
        var pulled = await Contacts.ListContactsAsync();
        var byResource = pulled
            .Where(c => !string.IsNullOrEmpty(c.ResourceName))
            .GroupBy(c => c.ResourceName)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var consumed = new HashSet<string>(StringComparer.Ordinal);
        var changed = false;

        // On travaille sur une copie car on peut retirer des éléments.
        foreach (var a in Adherents.ToList())
        {
            if (!string.IsNullOrEmpty(a.GoogleResourceName))
            {
                if (byResource.TryGetValue(a.GoogleResourceName, out var gc))
                {
                    changed |= ApplyGoogleToLocal(a, gc);
                    consumed.Add(a.GoogleResourceName);
                }
                else
                {
                    // Supprimé côté Google → on retire localement.
                    Adherents.Remove(a);
                    changed = true;
                }
            }
            else
            {
                // Non lié : on tente un rapprochement par e-mail.
                var match = !string.IsNullOrWhiteSpace(a.Email)
                    ? pulled.FirstOrDefault(c => !consumed.Contains(c.ResourceName) &&
                        string.Equals(c.Email, a.Email, StringComparison.OrdinalIgnoreCase))
                    : null;

                if (match != null)
                {
                    a.GoogleResourceName = match.ResourceName;
                    ApplyGoogleToLocal(a, match);
                    consumed.Add(match.ResourceName);
                    changed = true;
                }
                else
                {
                    // Contact purement local → on le pousse vers Google.
                    a.GoogleResourceName = await Contacts.AddContactAsync(a);
                    changed = true;
                }
            }
        }

        // Contacts Google restants (non présents localement) → ajout local.
        foreach (var gc in pulled)
        {
            if (consumed.Contains(gc.ResourceName))
                continue;

            Adherents.Add(new Adherent
            {
                GoogleResourceName = gc.ResourceName,
                Prenom = gc.Prenom,
                Nom = gc.Nom,
                Email = gc.Email,
                Telephone = gc.Telephone
            });
            changed = true;
        }

        if (changed)
            SaveAdherents();
    }

    private static bool ApplyGoogleToLocal(Adherent a, GoogleContact gc)
    {
        var changed = false;
        if (!string.Equals(a.Prenom, gc.Prenom, StringComparison.Ordinal)) { a.Prenom = gc.Prenom; changed = true; }
        if (!string.Equals(a.Nom, gc.Nom, StringComparison.Ordinal)) { a.Nom = gc.Nom; changed = true; }
        if (!string.Equals(a.Email, gc.Email, StringComparison.Ordinal)) { a.Email = gc.Email; changed = true; }
        if (!string.Equals(a.Telephone, gc.Telephone, StringComparison.Ordinal)) { a.Telephone = gc.Telephone; changed = true; }
        return changed;
    }

    /// <summary>
    /// Applique de nouveaux paramètres : sauvegarde, met à jour le navigateur,
    /// et recharge les adhérents si le chemin du fichier a changé.
    /// </summary>
    public void ApplySettings(AppSettings newSettings)
    {
        var oldPath = AdherentRepository.Path;
        Settings = newSettings;
        SettingsService.Save(Settings);
        BrowserService.SelectedBrowserPath = Settings.BrowserPath;

        // Rebind (le chemin JSON personnalisé a pu changer).
        var newPath = string.IsNullOrWhiteSpace(Settings.AdherentsJsonPath)
            ? AppPaths.AdherentsFileFor(CurrentAccount)
            : Settings.AdherentsJsonPath;
        if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            AdherentRepository = new AdherentRepository(newPath);
            ReloadAdherents();
        }
    }
}
