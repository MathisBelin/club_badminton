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

    /// <summary>Synchronisations automatiques configurées.</summary>
    public ObservableCollection<AutoSyncConfig> AutoSyncs { get; } = new();

    /// <summary>Personnes avec des infos mais sans e-mail (inscriptions incomplètes), par libellé.</summary>
    public ObservableCollection<PendingPerson> Pending { get; } = new();

    private List<LabelItem>? _labelsCache;

    public AppServices()
    {
        Settings = SettingsService.Load();
        BrowserService.SelectedBrowserPath = Settings.BrowserPath;

        foreach (var s in Settings.AutoSyncs)
            AutoSyncs.Add(s);

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

    /// <summary>Met à jour le navigateur choisi (seul paramètre édité par l'écran Paramètres).</summary>
    public void UpdateBrowser(string browserPath)
    {
        Settings.BrowserPath = browserPath ?? string.Empty;
        SettingsService.Save(Settings);
        BrowserService.SelectedBrowserPath = Settings.BrowserPath;
    }

    /// <summary>
    /// Liste des libellés, mise en cache. Ne rappelle l'API que si le cache est vide
    /// ou si <paramref name="forceRefresh"/> est demandé (après création/renommage/suppression).
    /// </summary>
    /// <summary>Déclenché quand la liste des libellés (cache) est (re)chargée.</summary>
    public event Action? LabelsChanged;

    public async Task<IReadOnlyList<LabelItem>> GetLabelsAsync(bool forceRefresh = false)
    {
        if (_labelsCache == null || forceRefresh)
        {
            _labelsCache = await Contacts.ListLabelsAsync();
            LabelsChanged?.Invoke();
        }
        return OrderLabels(_labelsCache);
    }

    /// <summary>Libellés déjà en cache (ou liste vide), triés par ordre alphabétique, sans appel réseau.</summary>
    public IReadOnlyList<LabelItem> CachedLabels => OrderLabels(_labelsCache ?? new List<LabelItem>());

    /// <summary>Trie les libellés par ordre alphabétique inverse (Z→A), utilisé pour toutes les listes déroulantes.</summary>
    private static IReadOnlyList<LabelItem> OrderLabels(IReadOnlyList<LabelItem> src)
        => src.OrderByDescending(l => l.Nom, StringComparer.CurrentCultureIgnoreCase).ToList();

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

    /// <summary>Intervalle entre deux exécutions d'une même synchro.</summary>
    public static readonly TimeSpan AutoSyncInterval = TimeSpan.FromMinutes(5);

    // ---- Gestion des synchros --------------------------------------------

    /// <summary>Persiste la liste des synchros dans settings.json.</summary>
    public void SaveSyncs()
    {
        Settings.AutoSyncs = AutoSyncs.ToList();
        SettingsService.Save(Settings);
    }

    /// <summary>Ajoute (ou remplace, par Id) une synchro puis sauvegarde.</summary>
    public void AddOrUpdateSync(AutoSyncConfig config)
    {
        var existing = AutoSyncs.FirstOrDefault(s => s.Id == config.Id);
        if (existing != null)
            AutoSyncs[AutoSyncs.IndexOf(existing)] = config;
        else
            AutoSyncs.Add(config);
        SaveSyncs();
    }

    public void DeleteSync(AutoSyncConfig config)
    {
        config.Enabled = false;
        AutoSyncs.Remove(config);
        // Retire les personnes en attente rattachées à cette synchro.
        for (var i = Pending.Count - 1; i >= 0; i--)
            if (string.Equals(Pending[i].LabelResourceName, config.LabelResourceName, StringComparison.Ordinal))
                Pending.RemoveAt(i);
        SaveSyncs();
    }

    /// <summary>Remplace les personnes en attente d'un libellé par la liste fraîchement lue.</summary>
    private void UpdatePending(AutoSyncConfig config, List<(string Nom, string Prenom, string Tel)> incompletes)
    {
        for (var i = Pending.Count - 1; i >= 0; i--)
            if (string.Equals(Pending[i].LabelResourceName, config.LabelResourceName, StringComparison.Ordinal))
                Pending.RemoveAt(i);

        foreach (var (nom, prenom, tel) in incompletes)
            Pending.Add(new PendingPerson
            {
                LabelResourceName = config.LabelResourceName,
                LabelName = config.LabelName,
                Nom = nom,
                Prenom = prenom,
                Telephone = tel
            });
    }

    /// <summary>Vrai si un libellé est déjà ciblé par une autre synchro (unicité).</summary>
    public bool IsLabelInUse(string labelResourceName, Guid exceptId)
        => AutoSyncs.Any(s => s.Id != exceptId
            && string.Equals(s.LabelResourceName, labelResourceName, StringComparison.Ordinal));

    public void StartSync(AutoSyncConfig config)
    {
        config.Enabled = true;
        config.NextRun = DateTime.Now; // exécution immédiate
        SaveSyncs();
    }

    public void StopSync(AutoSyncConfig config)
    {
        config.Enabled = false;
        config.NextRun = null;
        SaveSyncs();
    }

    /// <summary>Réarme les synchros activées au démarrage (exécution immédiate).</summary>
    public void ArmEnabledSyncs()
    {
        foreach (var s in AutoSyncs.Where(s => s.Enabled))
            s.NextRun = DateTime.Now;
    }

    /// <summary>Rafraîchit l'état affiché (minuteur) de toutes les synchros.</summary>
    public void TouchSyncs()
    {
        foreach (var s in AutoSyncs)
            s.Touch();
    }

    /// <summary>Lance les synchros dues (activées, non en cours, échéance atteinte). Concurrent.</summary>
    public void RunDueSyncs()
    {
        var now = DateTime.Now;
        foreach (var s in AutoSyncs)
            if (s.Enabled && !s.IsImporting && (s.NextRun == null || s.NextRun <= now))
                _ = RunSyncNowAsync(s);
    }

    /// <summary>Exécute une synchro immédiatement (silencieuse). Ne fait rien si déjà en cours.</summary>
    public async Task RunSyncNowAsync(AutoSyncConfig config)
    {
        if (config.IsImporting)
            return;

        config.Progress = 0;
        config.IsImporting = true;
        try
        {
            await ImportConfigAsync(config);
        }
        catch
        {
            // Silencieux : repris au prochain cycle.
        }
        finally
        {
            config.IsImporting = false;
            config.Progress = 0;
            config.NextRun = DateTime.Now + AutoSyncInterval;
        }
    }

    /// <summary>
    /// Importe les adhérents d'un Sheet vers son libellé cible : ajout / mise à jour par e-mail,
    /// association au libellé, et dissociation des membres absents du fichier.
    /// </summary>
    private async Task<(int Added, int Updated)> ImportConfigAsync(AutoSyncConfig config)
    {
        var id = ExtractSheetId(config.SheetUrl);
        if (id == null)
            return (0, 0);

        var rows = await Sheets.ReadRowsAsync(id, BuildDataRange(config.StartRow, config.EndRow));

        int? colNom = CsvContactImporter.ColIndex(config.ColNom);
        int? colPrenom = CsvContactImporter.ColIndex(config.ColPrenom);
        int? colTel = CsvContactImporter.ColIndex(config.ColTel);
        int? colEmail = CsvContactImporter.ColIndex(config.ColEmail);

        var parsed = CsvContactImporter.BuildFromColumns(rows, colNom, colPrenom, colTel, colEmail)
            .Select(c =>
            {
                // E-mail avec faute de frappe (ex. virgule à la place du point) : on tente une
                // correction automatique ; si le format devient valide, on l'utilise.
                if (!EmailValidator.IsValid(c.Email))
                {
                    var corrected = EmailValidator.Suggest(c.Email);
                    if (EmailValidator.IsValid(corrected))
                        c.Email = corrected;
                }
                return c;
            })
            .Where(c => EmailValidator.IsValid(c.Email)).ToList();

        // Personnes ayant renseigné des infos (nom/prénom/tél) mais SANS e-mail : inscription
        // incomplète → on les mémorise pour la page « Personnes en attente » (par libellé).
        UpdatePending(config, CsvContactImporter.BuildIncompleteFromColumns(rows, colNom, colPrenom, colTel, colEmail));

        // Adhérent local correspondant à chaque ligne du fichier (existant fusionné ou nouveau).
        var sheetContacts = new List<Adherent>();
        int added = 0, updated = 0;
        foreach (var p in parsed)
        {
            var existing = Adherents.FirstOrDefault(a =>
                string.Equals(a.Email, p.Email, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                if (MergeFields(existing, p)) updated++;
                sheetContacts.Add(existing);
            }
            else
            {
                Adherents.Add(p);
                added++;
                sheetContacts.Add(p);
            }
        }

        if (added > 0 || updated > 0)
            SaveAdherents();

        config.Progress = 10;

        // État actuel côté Google pour ne pousser que les vraies différences de champs.
        Dictionary<string, GoogleContact> googleByEmail;
        try
        {
            googleByEmail = (await Contacts.ListContactsAsync())
                .Where(c => !string.IsNullOrWhiteSpace(c.Email))
                .GroupBy(c => c.Email, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }
        catch (GoogleSyncException)
        {
            googleByEmail = new Dictionary<string, GoogleContact>(StringComparer.OrdinalIgnoreCase);
        }

        var pushed = false;
        var pushIndex = 0;
        var pushTotal = Math.Max(1, sheetContacts.Count);
        foreach (var a in sheetContacts)
        {
            try
            {
                googleByEmail.TryGetValue(a.Email, out var gc);

                if (string.IsNullOrEmpty(a.GoogleResourceName))
                {
                    a.GoogleResourceName = gc?.ResourceName ?? await Contacts.EnsureContactResourceAsync(a);
                    pushed = true;
                }

                if (gc == null || FieldsDiffer(a, gc))
                {
                    await Contacts.UpdateContactAsync(a.GoogleResourceName, a);
                    pushed = true;
                }
            }
            catch (GoogleSyncException)
            {
                // Silencieux : repris au prochain cycle.
            }

            // La poussée vers Google est le gros du travail : 10 % → 85 %.
            pushIndex++;
            config.Progress = 10 + (int)(pushIndex * 75.0 / pushTotal);
        }
        if (pushed)
            SaveAdherents();

        config.Progress = 90;

        // Association au libellé cible (unique) : ajout des manquants, retrait des absents.
        var group = config.LabelResourceName;
        if (!string.IsNullOrWhiteSpace(group))
        {
            var sheetEmails = new HashSet<string>(parsed.Select(p => p.Email), StringComparer.OrdinalIgnoreCase);
            var byEmail = Adherents
                .Where(a => !string.IsNullOrWhiteSpace(a.Email) && sheetEmails.Contains(a.Email))
                .GroupBy(a => a.Email, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            List<(string ResourceName, string Email)>? members = null;
            try { members = await Contacts.GetLabelMembersAsync(group); }
            catch (GoogleSyncException) { /* on retentera au prochain cycle */ }

            if (members != null)
            {
                var alreadyIn = new HashSet<string>(members.Select(m => m.Email), StringComparer.OrdinalIgnoreCase);
                var resourceResolved = false;

                foreach (var (email, a) in byEmail)
                {
                    if (alreadyIn.Contains(email))
                        continue;
                    try
                    {
                        if (string.IsNullOrEmpty(a.GoogleResourceName))
                        {
                            a.GoogleResourceName = await Contacts.EnsureContactResourceAsync(a);
                            resourceResolved = true;
                        }
                        await Contacts.SetMembershipAsync(a.GoogleResourceName, group, add: true);
                    }
                    catch (GoogleSyncException) { /* repris au prochain cycle */ }
                }

                foreach (var (resourceName, email) in members)
                {
                    if (sheetEmails.Contains(email))
                        continue;
                    try { await Contacts.SetMembershipAsync(resourceName, group, add: false); }
                    catch (GoogleSyncException) { /* repris au prochain cycle */ }
                }

                if (resourceResolved)
                    SaveAdherents();
            }
        }

        config.Progress = 100;
        return (added, updated);
    }

    private static bool MergeFields(Adherent target, Adherent source)
    {
        var changed = false;
        if (!string.IsNullOrWhiteSpace(source.Nom) && target.Nom != source.Nom) { target.Nom = source.Nom; changed = true; }
        if (!string.IsNullOrWhiteSpace(source.Prenom) && target.Prenom != source.Prenom) { target.Prenom = source.Prenom; changed = true; }
        if (!string.IsNullOrWhiteSpace(source.Telephone) && target.Telephone != source.Telephone) { target.Telephone = source.Telephone; changed = true; }
        return changed;
    }

    /// <summary>Construit la plage A→Z pour les lignes de données configurées (début → fin).</summary>
    private static string BuildDataRange(int startRow, int endRow)
    {
        var start = startRow > 0 ? startRow : 1;
        return endRow >= start ? $"A{start}:Z{endRow}" : $"A{start}:Z";
    }

    /// <summary>Colonnes détectées automatiquement (lettres ; null si non trouvée).</summary>
    public sealed record DetectedColumns(string? Nom, string? Prenom, string? Tel, string? Email);

    /// <summary>
    /// Détecte automatiquement les colonnes d'un Sheet par leurs en-têtes (lecture depuis la 1re
    /// ligne pour inclure l'en-tête). Renvoie les lettres trouvées (null si non détectée).
    /// </summary>
    public async Task<DetectedColumns> DetectColumnsAsync(string sheetUrl, int endRow)
    {
        var id = ExtractSheetId(sheetUrl);
        if (id == null)
            return new DetectedColumns(null, null, null, null);

        var rows = await Sheets.ReadRowsAsync(id, endRow > 0 ? $"A1:Z{endRow}" : "A1:Z");

        var mapping = CsvContactImporter.DetectColumns(rows);
        if (mapping == null)
            return new DetectedColumns(null, null, null, null);

        string? L(string key) => mapping.Columns.TryGetValue(key, out var i) ? CsvContactImporter.ColumnLetter(i) : null;
        return new DetectedColumns(L("nom"), L("prenom"), L("tel"), L("email"));
    }

    /// <summary>Résultat d'un test de colonnes : validé ou non + message affichable.</summary>
    public sealed record ColumnCheck(bool Ok, string Message);

    /// <summary>
    /// Lit les lignes d'un Sheet avec les colonnes indiquées et vérifie que les 4 informations
    /// (Nom / Prénom / Téléphone / E-mail) ressortent bien. Renvoie un statut + un message.
    /// </summary>
    public async Task<ColumnCheck> CheckColumnsAsync(
        string sheetUrl, int startRow, int endRow,
        string colNom, string colPrenom, string colTel, string colEmail)
    {
        var id = ExtractSheetId(sheetUrl);
        if (id == null)
            return new ColumnCheck(false, "Lien du Google Sheet invalide ou manquant.");

        List<string[]> rows;
        try
        {
            rows = await Sheets.ReadRowsAsync(id, BuildDataRange(startRow, endRow));
        }
        catch (GoogleSyncException ex)
        {
            return new ColumnCheck(false, ex.Message);
        }

        var result = CsvContactImporter.CheckColumns(rows, colNom, colPrenom, colTel, colEmail);
        var message = CsvContactImporter.BuildCheckMessage(result, colNom, colPrenom, colTel, colEmail);
        return new ColumnCheck(result.Ok, message);
    }

    /// <summary>Vrai si le contact Google diffère de l'adhérent (nom, prénom ou téléphone).</summary>
    private static bool FieldsDiffer(Adherent a, GoogleContact gc)
    {
        static string Digits(string? s) => new string((s ?? string.Empty).Where(char.IsDigit).ToArray());
        return !string.Equals(a.Nom, gc.Nom, StringComparison.Ordinal)
            || !string.Equals(a.Prenom, gc.Prenom, StringComparison.Ordinal)
            || Digits(a.Telephone) != Digits(gc.Telephone);
    }

    /// <summary>Extrait l'ID d'un Google Sheet depuis une URL (…/spreadsheets/d/&lt;id&gt;/…).</summary>
    public static string? ExtractSheetId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        var m = System.Text.RegularExpressions.Regex.Match(url, @"/spreadsheets/d/([a-zA-Z0-9\-_]+)");
        if (m.Success)
            return m.Groups[1].Value;
        // Peut-être un ID collé directement.
        return System.Text.RegularExpressions.Regex.IsMatch(url.Trim(), @"^[a-zA-Z0-9\-_]{20,}$")
            ? url.Trim()
            : null;
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

}
