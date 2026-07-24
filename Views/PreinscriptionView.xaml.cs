using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using BadmintonClub.Helpers;
using BadmintonClub.Models;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

public partial class PreinscriptionView : UserControl, IActivableView
{
    private readonly AppServices _services;
    private FormRecord? _form;

    private List<GoogleFormsService.FormQuestionInfo> _questions = new();
    private List<FormResponseRow> _responses = new();
    private HashSet<string> _labelMemberEmails = new(StringComparer.OrdinalIgnoreCase);
    private List<FormRecord> _allForms = new();
    private readonly List<PreRow> _allRows = new();
    private List<PreRow> _displayed = new();
    private bool _waitlistMode;

    // Page « Personnes inscrites » (inline) : lignes construites depuis _state.Validated.
    private readonly List<RegRow> _registeredAll = new();
    private List<RegRow> _registeredDisplayed = new();
    private bool _registeredRestored;

    /// <summary>Des liens réponse↔contact ont été ajoutés et restent à enregistrer (évite N écritures).</summary>
    private bool _linksDirty;

    /// <summary>Décisions locales sur ce formulaire (inscriptions validées, statuts forcés).</summary>
    private FormState _state = new();

    // Libellés de statut d'une préinscription.
    private const string StatusPre = PreRow.StatusPre;
    private const string StatusWait = PreRow.StatusWait;
    private const string StatusCancel = PreRow.StatusCancel;

    public PreinscriptionView(AppServices services)
    {
        InitializeComponent();
        _services = services;
        ShowPicker();
    }

    public void OnActivated() => ShowPicker();

    // ---- Sélecteur de formulaire -----------------------------------------

    /// <summary>Affiche la liste des formulaires (état par défaut de la page).</summary>
    public void ShowPicker()
    {
        _form = null;
        ResponsesPanel.Visibility = Visibility.Collapsed;
        RegisteredPanel.Visibility = Visibility.Collapsed;
        PickerPanel.Visibility = Visibility.Visible;
        _ = LoadFormsAsync();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = LoadFormsAsync();

    private void OpenWebApp_Click(object sender, RoutedEventArgs e)
        => BrowserService.Open(_services.WebForms.SiteUrl);

    /// <summary>
    /// Charge les formulaires du compte connecté depuis l'APPLICATION WEB, en conservant
    /// les informations locales (libellé associé) mémorisées dans forms.json.
    /// </summary>
    private async Task LoadFormsAsync()
    {
        List<FormRecord> forms;
        try
        {
            forms = await _services.WebForms.ListFormsAsync(_services.CurrentAccount);
        }
        catch (WebFormsException ex)
        {
            _allForms = new List<FormRecord>();
            FormsGrid.ItemsSource = null;
            FormCountText.Text = string.Empty;
            PickerEmpty.Text = ex.Message;
            PickerEmpty.Visibility = Visibility.Visible;
            return;
        }

        // Le libellé vient désormais du SITE (choisi à la création). Pour les formulaires
        // créés avant cette évolution, on retombe sur le libellé mémorisé localement.
        var local = _services.FormRepository.Load().ToDictionary(f => f.FormId, StringComparer.Ordinal);
        foreach (var f in forms)
        {
            if (!string.IsNullOrWhiteSpace(f.LabelResourceName) ||
                !local.TryGetValue(f.FormId, out var saved))
                continue;
            f.LabelName = saved.LabelName;
            f.LabelResourceName = saved.LabelResourceName;
        }
        _services.FormRepository.Save(forms);

        _allForms = forms.OrderByDescending(f => f.DateCreation).ToList();
        DisplayForms();
    }

    /// <summary>Applique le filtre de recherche à la liste des formulaires (nom ou libellé).</summary>
    private void DisplayForms()
    {
        var term = FormSearchBox.Text?.Trim();
        var shown = string.IsNullOrEmpty(term)
            ? _allForms
            : _allForms.Where(f =>
                (f.Nom?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (f.LabelName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();

        FormsGrid.ItemsSource = shown;
        FormCountText.Text = string.IsNullOrEmpty(term)
            ? $"{shown.Count} formulaire(s)"
            : $"{shown.Count} sur {_allForms.Count}";

        PickerEmpty.Text = _allForms.Count == 0
            ? "Aucun formulaire pour ce compte. Créez-en un depuis l'application web."
            : "Aucun formulaire ne correspond à cette recherche.";
        PickerEmpty.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FormSearch_Changed(object sender, TextChangedEventArgs e) => DisplayForms();


    private void Form_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (FormsGrid.SelectedItem is FormRecord f)
            ShowForForm(f);
    }

    private void OpenForm_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FormRecord f)
            ShowForForm(f);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ShowPicker();

    /// <summary>Affiche les réponses du formulaire donné (depuis la page Google Forms ou le sélecteur).</summary>
    public async void ShowForForm(FormRecord form)
    {
        _form = form;
        _waitlistMode = false;
        WaitlistBtn.Content = "⏳ Liste d'attente";
        FormTitle.Text = form.Nom;

        PickerPanel.Visibility = Visibility.Collapsed;
        RegisteredPanel.Visibility = Visibility.Collapsed;
        ResponsesPanel.Visibility = Visibility.Visible;

        await LoadAsync();
    }

    // ---- Chargement -------------------------------------------------------

    private async Task LoadAsync()
    {
        if (_form == null)
            return;

        _state = _services.FormStates.Get(_form.FormId);

        var owner = Window.GetWindow(this)!;
        var result = await ProgressRunner.RunBusyAsync(owner, "Lecture des réponses…", async () =>
        {
            // Source : application web (les correspondances de contact et les règles de
            // réponses sont celles configurées dans le formulaire web).
            _questions = await _services.WebForms.GetFormQuestionsAsync(_form);
            _responses = await _services.WebForms.ListResponsesAsync(_form.FormId);

            // Membres déjà associés au libellé du formulaire (pour l'alerte ⚠) — best-effort.
            _labelMemberEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(_form.LabelResourceName))
            {
                try { _labelMemberEmails = await _services.Contacts.GetLabelMemberEmailsAsync(_form.LabelResourceName); }
                catch (GoogleSyncException) { /* alerte ⚠ indisponible, non bloquant */ }
            }
        });

        if (result.Failed > 0)
        {
            ShowEmpty(result.LastError ?? "Impossible de lire les réponses.");
            return;
        }

        BuildRows();
        Display();

        // Le formulaire a pu changer de libellé « en cours de route » : on aligne les inscrits.
        await ReconcileValidatedLabelsAsync();
    }

    /// <summary>
    /// Aligne le libellé des personnes déjà inscrites sur le libellé ACTUEL du formulaire : si le
    /// formulaire a changé de libellé depuis leur validation, chacune est dissociée de l'ancien
    /// libellé et associée au nouveau (Google Contacts). Sans effet si le libellé n'a pas changé.
    /// </summary>
    private async Task ReconcileValidatedLabelsAsync()
    {
        if (_form == null || string.IsNullOrWhiteSpace(_form.LabelResourceName) || _state.Validated.Count == 0)
            return;

        // Personnes dont le libellé enregistré diffère du libellé actuel du formulaire.
        var toMove = new List<(ValidatedEntry Entry, Adherent Contact, string? OldLabelRes)>();
        var backfilled = false;

        foreach (var v in _state.Validated)
        {
            var changed = !string.IsNullOrEmpty(v.LabelResourceName)
                ? !string.Equals(v.LabelResourceName, _form.LabelResourceName, StringComparison.Ordinal)
                : !string.Equals(v.LabelName, _form.LabelName, StringComparison.CurrentCultureIgnoreCase);

            if (!changed)
            {
                // Entrée créée avant cette évolution mais même libellé : on complète juste la
                // ressource (aucune écriture côté Google).
                if (string.IsNullOrEmpty(v.LabelResourceName))
                {
                    v.LabelResourceName = _form.LabelResourceName;
                    backfilled = true;
                }
                continue;
            }

            var contact = FindContactForEntry(v);
            if (contact == null || string.IsNullOrWhiteSpace(contact.GoogleResourceName))
                continue; // pas dans mes contacts : rien à déplacer (retenté au prochain chargement).

            var oldRes = !string.IsNullOrEmpty(v.LabelResourceName)
                ? v.LabelResourceName
                : ResolveLabelResourceByName(v.LabelName);
            toMove.Add((v, contact, oldRes));
        }

        if (toMove.Count == 0)
        {
            if (backfilled)
                _services.FormStates.Save();
            return;
        }

        var owner = Window.GetWindow(this)!;
        var moved = 0;
        string? error = null;
        await ProgressRunner.RunBusyAsync(owner, "Mise à jour des libellés des inscrits…", async () =>
        {
            foreach (var (entry, contact, oldRes) in toMove)
            {
                try
                {
                    await _services.Contacts.SetMembershipAsync(contact.GoogleResourceName, _form!.LabelResourceName, add: true);
                    if (!string.IsNullOrEmpty(oldRes) &&
                        !string.Equals(oldRes, _form.LabelResourceName, StringComparison.Ordinal))
                    {
                        await _services.Contacts.SetMembershipAsync(contact.GoogleResourceName, oldRes, add: false);
                        _services.LogContactActivity(ActivityAction.Dissociation, contact,
                            $"{entry.LabelName} (changement de libellé — {_form.Nom})");
                    }
                    _services.LogContactActivity(ActivityAction.Association, contact,
                        $"{_form.LabelName} (changement de libellé — {_form.Nom})");

                    entry.LabelName = _form.LabelName;
                    entry.LabelResourceName = _form.LabelResourceName;
                    moved++;
                }
                catch (GoogleSyncException ex)
                {
                    error = ex.Message;
                }
            }
        });

        _services.FormStates.Save();

        if (moved > 0 || error != null)
        {
            var msg = moved > 0
                ? $"{moved} personne(s) déjà inscrite(s) déplacée(s) vers le libellé « {_form.LabelName} »."
                : "Aucune personne déplacée.";
            if (error != null)
                msg += $"\nErreur : {error}";
            MessageBox.Show(owner, msg, "Libellé du formulaire",
                MessageBoxButton.OK, error != null ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
    }

    /// <summary>Résout un nom de libellé en ressource Google via le cache (null si introuvable).</summary>
    private string? ResolveLabelResourceByName(string? labelName)
    {
        if (string.IsNullOrWhiteSpace(labelName))
            return null;
        return _services.CachedLabels
            .FirstOrDefault(l => string.Equals(l.Nom, labelName, StringComparison.CurrentCultureIgnoreCase))
            ?.ResourceName;
    }

    private string FieldId(string field)
    {
        if (_form != null && _form.FieldMap.TryGetValue(field, out var id) && !string.IsNullOrEmpty(id))
            return id;
        // Repli sur l'auto-détection uniquement si AUCUNE correspondance n'a été configurée (rétro-compat).
        if (_form != null && _form.FieldMap.Count == 0)
        {
            var q = _questions.FirstOrDefault(q => CsvContactImporter.DetectContactField(q.Title) == field);
            return q?.QuestionId ?? string.Empty;
        }
        return string.Empty;
    }

    private void BuildRows()
    {
        _allRows.Clear();
        if (_form == null)
            return;

        var prenomId = FieldId("prenom");
        var nomId = FieldId("nom");
        var telId = FieldId("tel");
        var emailId = FieldId("email");
        var secondaryId = FieldId("secondaryEmails");

        // Identité = e-mail vérifié du répondant (à défaut e-mail saisi, à défaut id de réponse).
        // Normalisé (trim + minuscules via le comparateur) pour regrouper les soumissions multiples
        // d'une même personne en une seule ligne (la plus récente fait foi).
        string Key(FormResponseRow r) => !string.IsNullOrWhiteSpace(r.RespondentEmail)
            ? r.RespondentEmail.Trim()
            : (!string.IsNullOrWhiteSpace(Ans(r, emailId)) ? Ans(r, emailId).Trim() : r.ResponseId);

        // Les inscriptions déjà validées quittent la liste : elles vivent dans « ✅ Inscrits ».
        // Mais une personne dissociée qui REMPLIT DE NOUVEAU le formulaire revient dans la liste :
        // une réponse postérieure à la validation annule celle-ci (et sort des inscrits).
        var validated = new Dictionary<string, ValidatedEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in _state.Validated)
            validated[v.Email] = v;
        var stateChanged = false;

        foreach (var g in _responses.GroupBy(Key, StringComparer.OrdinalIgnoreCase)
                                    .Where(g => !string.IsNullOrWhiteSpace(g.Key)))
        {
            if (validated.TryGetValue(g.Key, out var entry))
            {
                // Réponse (ou modification) postérieure à la validation = nouvelle inscription.
                // Les dates du site sont en UTC et la validation en heure locale : on ramène les
                // deux à l'UTC, sinon le décalage horaire crée un angle mort de plusieurs heures.
                if (Utc(g.Max(r => r.SubmittedAt)) <= Utc(entry.ValidatedAt))
                    continue;
                _state.Validated.Remove(entry);
                stateChanged = true;
            }

            var latest = g.OrderByDescending(r => r.SubmittedAt).First();

            var rawEmail = !string.IsNullOrWhiteSpace(latest.RespondentEmail)
                ? latest.RespondentEmail
                : Ans(latest, emailId);
            var email = NormalizeEmail(rawEmail);

            // 1re soumission = place dans la file ; dernière soumission = éventuelle modification.
            var originalAt = g.Min(r => r.CreatedAt);
            var lastSubmit = g.Max(r => r.SubmittedAt);
            DateTime? modifiedAt = lastSubmit > originalAt.AddSeconds(2) ? lastSubmit : null;

            var row = new PreRow
            {
                Resp = latest,
                RespNom = Ans(latest, nomId),
                RespPrenom = Ans(latest, prenomId),
                RespTelephone = PhoneFormatter.Format(Ans(latest, telId)),
                Email = email,
                IsEmailValid = string.IsNullOrWhiteSpace(email) || EmailValidator.IsValid(email),
                MappedSecondaryEmails = ParseEmails(Ans(latest, secondaryId), email),
                SubmittedAt = originalAt,
                ModifiedAt = modifiedAt,
                Status = EffectiveStatus(latest, email),
                AlreadyMember = !string.IsNullOrWhiteSpace(email) && _labelMemberEmails.Contains(email)
            };

            row.StatusForced = !string.IsNullOrWhiteSpace(email) && _state.StatusOverrides.ContainsKey(email);

            ApplyExistingMatch(row);

            row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PreRow.IsSelected)) UpdateBar(); };
            _allRows.Add(row);
        }

        if (stateChanged)
            _services.FormStates.Save();
    }

    /// <summary>
    /// Rapproche la réponse d'un contact existant (par e-mail, à défaut nom+prénom) : les colonnes
    /// affichent alors les informations DU CONTACT, et les champs que la réponse contredit sont
    /// marqués en jaune (cellule + ligne). Sans contact, on retombe sur les valeurs de la réponse.
    /// </summary>
    private void ApplyExistingMatch(PreRow row)
    {
        Adherent? existing = null;

        // 1) Lien durable déjà mémorisé (identifiant du contact) : résiste à un changement d'e-mail.
        if (!string.IsNullOrWhiteSpace(row.Email) &&
            _state.ContactLinks.TryGetValue(row.Email, out var linkedId) &&
            Guid.TryParse(linkedId, out var lid))
            existing = _services.Adherents.FirstOrDefault(a => a.Id == lid);

        // 2) Rapprochement par e-mail vérifié → on mémorise le lien pour la suite.
        if (existing == null && !string.IsNullOrWhiteSpace(row.Email))
        {
            existing = _services.Adherents.FirstOrDefault(a =>
                !string.IsNullOrWhiteSpace(a.Email) &&
                string.Equals(a.Email, row.Email, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                RememberContactLink(row.Email, existing.Id);
        }

        // 3) Repli nom+prénom quand la réponse n'a pas d'e-mail.
        if (existing == null && string.IsNullOrWhiteSpace(row.Email))
            existing = _services.Adherents.FirstOrDefault(a =>
                !string.IsNullOrWhiteSpace(row.RespNom) && !string.IsNullOrWhiteSpace(row.RespPrenom) &&
                string.Equals(a.Nom, row.RespNom, StringComparison.CurrentCultureIgnoreCase) &&
                string.Equals(a.Prenom, row.RespPrenom, StringComparison.CurrentCultureIgnoreCase));

        row.Existing = existing;
        row.NomDiff = row.PrenomDiff = row.TelDiff = row.EmailDiff = false;

        if (existing == null)
        {
            // Répondant absent de mes contacts → à ajouter (ligne rouge + mention dans la colonne Nom).
            row.SecondaryEmails = new List<string>(row.MappedSecondaryEmails);
            row.NotInContacts = true;
            row.DiffFields = new List<string>();
            row.Highlight = false;
            row.NotifyDisplayChanged();
            return;
        }

        row.NotInContacts = false;

        // Mails secondaires affichés : ceux de la réponse s'il y en a (ils remplaceront ceux du
        // contact à la validation), sinon ceux du contact.
        row.SecondaryEmails = row.MappedSecondaryEmails.Count > 0
            ? row.MappedSecondaryEmails.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string>(existing.SecondaryEmails);

        // Comparaison des champs renseignés dans la réponse (seuls les champs fournis comptent),
        // en écartant les différences que j'ai choisi d'ignorer (« garder l'état actuel »).
        var diffs = new List<string>();
        if (!string.IsNullOrWhiteSpace(row.RespNom) &&
            !string.Equals(row.RespNom, existing.Nom, StringComparison.CurrentCultureIgnoreCase))
        {
            row.NomDiff = true;
            diffs.Add("nom");
        }
        if (!string.IsNullOrWhiteSpace(row.RespPrenom) &&
            !string.Equals(row.RespPrenom, existing.Prenom, StringComparison.CurrentCultureIgnoreCase))
        {
            row.PrenomDiff = true;
            diffs.Add("prénom");
        }
        if (!string.IsNullOrWhiteSpace(row.RespTelephone) &&
            Digits(row.RespTelephone) != Digits(existing.Telephone))
        {
            row.TelDiff = true;
            diffs.Add("téléphone");
        }
        if (!string.IsNullOrWhiteSpace(row.Email) &&
            !string.Equals(row.Email, existing.Email, StringComparison.OrdinalIgnoreCase))
        {
            row.EmailDiff = true;
            diffs.Add("e-mail");
        }

        row.DiffFields = diffs;
        row.Highlight = diffs.Count > 0;
        row.NotifyDisplayChanged();
    }

    private static string Digits(string? s)
        => new string((s ?? string.Empty).Where(char.IsDigit).ToArray());

    /// <summary>
    /// Ramène une date à l'UTC pour pouvoir comparer une date du site (UTC) et une date
    /// enregistrée localement. Une date sans fuseau connu est supposée locale.
    /// </summary>
    private static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime(),
    };


    /// <summary>Normalise un e-mail : corrige les fautes rattrapables (virgule→point, espaces) si possible.</summary>
    private static string NormalizeEmail(string? email)
    {
        email = email?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(email) || EmailValidator.IsValid(email))
            return email;
        return EmailValidator.IsValidOrFixable(email) ? EmailValidator.Suggest(email) : email;
    }

    /// <summary>Découpe une réponse en liste d'e-mails (séparés par , ; espace ou saut de ligne), sans l'e-mail principal.</summary>
    private static List<string> ParseEmails(string? value, string? primary)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new List<string>();
        return value.Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0 && !string.Equals(s, primary, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string Ans(FormResponseRow r, string? qid)
        => string.IsNullOrEmpty(qid) ? string.Empty : r.Fields.GetValueOrDefault(qid, string.Empty);

    private string StatusOf(FormResponseRow r)
    {
        if (_form == null)
            return StatusPre;
        var waitlist = false;
        foreach (var (qid, answer) in r.Fields)
        {
            if (!_form.AnswerRules.TryGetValue(FormRecord.RuleKey(qid, answer), out var rule))
                continue;
            if (rule == "cancel")
                return StatusCancel;
            if (rule == "waitlist")
                waitlist = true;
        }
        return waitlist ? StatusWait : StatusPre;
    }

    /// <summary>Statut du répondant : celui forcé à la main s'il existe, sinon celui déduit des règles.</summary>
    private string EffectiveStatus(FormResponseRow r, string email)
    {
        if (!string.IsNullOrWhiteSpace(email) &&
            _state.StatusOverrides.TryGetValue(email, out var forced))
            return forced == "waitlist" ? StatusWait : StatusPre;
        return StatusOf(r);
    }

    /// <summary>Force le statut d'un répondant (⏳ mettre en attente / ↩ remettre en préinscription).</summary>
    private void SetStatus_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PreRow r || _form == null)
            return;
        if (string.IsNullOrWhiteSpace(r.Email))
        {
            Warn("Réponse sans e-mail : impossible de mémoriser un statut pour cette personne.");
            return;
        }

        var toWaitlist = !r.IsWaiting;
        _state.StatusOverrides[r.Email] = toWaitlist ? "waitlist" : "pre";
        _services.FormStates.Save();

        r.Status = toWaitlist ? StatusWait : StatusPre;
        r.StatusForced = true;
        Display();
    }

    // ---- Affichage / filtres ---------------------------------------------

    private void Display()
    {
        // Les rapprochements par e-mail effectués pendant la construction ont pu ajouter des liens durables.
        FlushLinks();

        var term = SearchBox.Text?.Trim();

        IEnumerable<PreRow> src = _allRows;
        if (!string.IsNullOrEmpty(term))
            src = src.Where(r => Match(r, term));

        if (_waitlistMode)
        {
            // Liste d'attente : uniquement les personnes « En attente », par date de réponse.
            _displayed = src.Where(r => r.Status == StatusWait)
                            .OrderBy(r => r.SubmittedAt).ToList();
            var rang = 0;
            foreach (var r in _displayed)
                r.Rang = ++rang;
        }
        else
        {
            // Vue par défaut : uniquement les préinscrits (ni en attente, ni annulés).
            _displayed = src.Where(r => r.Status == StatusPre)
                            .OrderBy(r => r.Nom, StringComparer.CurrentCultureIgnoreCase).ToList();
            foreach (var r in _displayed)
                r.Rang = 0;
        }

        // La colonne « Rang » n'a de sens que dans la liste d'attente.
        RangCol.Visibility = _waitlistMode ? Visibility.Visible : Visibility.Collapsed;

        Grid.ItemsSource = _displayed;
        Grid.Visibility = Visibility.Visible;

        CountText.Text = _waitlistMode
            ? $"{_displayed.Count} en liste d'attente"
            : $"{_displayed.Count} préinscrit(s)";

        if (_displayed.Count == 0)
            ShowEmpty(_waitlistMode ? "Aucune personne en liste d'attente." : "Aucun préinscrit.");
        else
            EmptyResults.Visibility = Visibility.Collapsed;

        UpdateBar();
    }

    private static bool Match(PreRow r, string term)
    {
        bool C(string? v) => v != null && v.Contains(term, StringComparison.OrdinalIgnoreCase);
        // On cherche aussi bien dans les infos affichées (contact) que dans celles de la réponse.
        return C(r.Nom) || C(r.Prenom) || C(r.Telephone) || C(r.Email)
            || C(r.RespNom) || C(r.RespPrenom) || C(r.RespTelephone);
    }

    private void ShowEmpty(string message)
    {
        _displayed = new List<PreRow>();
        Grid.ItemsSource = _displayed;
        EmptyResults.Text = message;
        EmptyResults.Visibility = Visibility.Visible;
        CountText.Text = string.Empty;
        UpdateBar();
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) => Display();

    private void Waitlist_Click(object sender, RoutedEventArgs e)
    {
        _waitlistMode = !_waitlistMode;
        WaitlistBtn.Content = _waitlistMode ? "← Liste des préinscrits" : "⏳ Liste d'attente";
        Display();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        var check = (sender as System.Windows.Controls.Primitives.ToggleButton)?.IsChecked == true;
        foreach (var r in _displayed)
            r.IsSelected = check;
    }

    /// <summary>
    /// Barre d'actions groupées : elle n'apparaît que si des lignes sont cochées, et les
    /// actions dépendent du mode — préinscrits : valider / mettre en attente / supprimer ;
    /// liste d'attente : remettre en préinscription / supprimer (pas de validation directe).
    /// </summary>
    private void UpdateBar()
    {
        var count = _displayed.Count(r => r.IsSelected);

        SelectionBar.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SelectionText.Text = $"{count} sélectionné(e)s";

        // Valider est réservé aux préinscrits : on ne valide pas depuis la liste d'attente.
        ValidateBtn.Visibility = _waitlistMode ? Visibility.Collapsed : Visibility.Visible;
        ValidateBtn.Content = $"✔ Valider ({count})";
        ToWaitlistBtn.Visibility = _waitlistMode ? Visibility.Collapsed : Visibility.Visible;
        ToWaitlistBtn.Content = $"⏳ Mettre en liste d'attente ({count})";
        ToPreBtn.Visibility = _waitlistMode ? Visibility.Visible : Visibility.Collapsed;
        ToPreBtn.Content = $"↩ Remettre en préinscription ({count})";
        DeleteBtn.Content = $"🗑 Supprimer ({count})";

        HeaderSelectAllBox.IsChecked = count == 0 ? false
            : count == _displayed.Count && _displayed.Count > 0 ? true : (bool?)null;
    }

    /// <summary>Change en bloc le statut des lignes cochées (⏳ en attente / ↩ préinscription).</summary>
    private void BulkStatus_Click(object sender, RoutedEventArgs e)
    {
        var toWaitlist = ReferenceEquals(sender, ToWaitlistBtn);
        var selected = _displayed.Where(r => r.IsSelected).ToList();
        var noEmail = selected.Count(r => string.IsNullOrWhiteSpace(r.Email));

        foreach (var r in selected.Where(r => !string.IsNullOrWhiteSpace(r.Email)))
        {
            _state.StatusOverrides[r.Email] = toWaitlist ? "waitlist" : "pre";
            r.Status = toWaitlist ? StatusWait : StatusPre;
            r.StatusForced = true;
            r.IsSelected = false;
        }
        _services.FormStates.Save();
        Display();

        if (noEmail > 0)
            Warn($"{noEmail} réponse(s) sans e-mail ignorée(s) : impossible d'y mémoriser un statut.");
    }

    /// <summary>Supprime la préinscription d'une seule personne (bouton 🗑 de la ligne).</summary>
    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PreRow r)
            _ = DeleteAsync(new List<PreRow> { r }, $"{r.Prenom} {r.Nom}".Trim());
    }

    /// <summary>Supprime les préinscriptions cochées sur le site (les personnes pourront se réinscrire).</summary>
    private void BulkDelete_Click(object sender, RoutedEventArgs e)
    {
        var selected = _displayed.Where(r => r.IsSelected).ToList();
        if (selected.Count > 0)
            _ = DeleteAsync(selected, null);
    }

    /// <summary>
    /// Supprime des préinscriptions sur le SITE : la personne n'est plus inscrite et peut
    /// de nouveau remplir le formulaire. Le contact et l'adhérent ne sont pas touchés.
    /// </summary>
    private async Task DeleteAsync(IReadOnlyList<PreRow> selected, string? personName)
    {
        if (_form == null || selected.Count == 0)
            return;

        var question = personName != null
            ? $"Supprimer la préinscription de « {personName} » ?"
            : $"Supprimer définitivement {selected.Count} préinscription(s) du formulaire ?";
        if (!ConfirmWindow.Ask(Window.GetWindow(this), "Supprimer les préinscriptions",
                question + "\nLes personnes concernées pourront de nouveau remplir le formulaire.",
                "Supprimer", "🗑", danger: true))
            return;

        var owner = Window.GetWindow(this)!;
        var deleted = new List<PreRow>();
        string? error = null;

        await ProgressRunner.RunBusyAsync(owner, "Suppression des préinscriptions…", async () =>
        {
            foreach (var r in selected)
            {
                try
                {
                    await _services.WebForms.DeleteResponseAsync(_form.FormId, r.Resp.ResponseId);
                    deleted.Add(r);
                }
                catch (WebFormsException ex)
                {
                    error = ex.Message;
                }
            }
        });

        foreach (var r in deleted)
        {
            _state.StatusOverrides.Remove(r.Email);
            _allRows.Remove(r);
        }
        _services.FormStates.Save();
        Display();

        var msg = $"{deleted.Count} préinscription(s) supprimée(s).";
        if (error != null)
            msg += $"\n{selected.Count - deleted.Count} en erreur : {error}";
        MessageBox.Show(owner, msg, "Suppression", MessageBoxButton.OK,
            error != null ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    // ---- Détail (réponses d'une personne) --------------------------------

    private void Detail_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PreRow r)
            _ = OpenResponseDetailAsync(r);
    }

    /// <summary>Ouvre le détail des réponses d'une personne (question par question, diffs en couleur).</summary>
    private async Task OpenResponseDetailAsync(PreRow r)
    {
        var win = new ResponseDetailWindow(_questions, r.Resp, $"{r.Prenom} {r.Nom}".Trim(),
            r.Existing, FieldQidMap(), _services)
        {
            Owner = Window.GetWindow(this)
        };
        win.ShowDialog();

        // « Garder l'état actuel » : la valeur du contact remplace celle saisie, DANS LA RÉPONSE
        // sur le site (seule écriture, avec l'envoi des libellés).
        if (win.IgnoredFields.Count > 0)
        {
            await ApplyIgnoredToWebAsync(r, win.IgnoredFields);
            return;
        }

        if (win.ContactUpdated)
        {
            ApplyExistingMatch(r);
            Display();
        }
    }

    /// <summary>Réécrit sur le site les réponses ignorées avec la valeur actuelle du contact.</summary>
    private async Task ApplyIgnoredToWebAsync(PreRow row, IReadOnlyList<(string Field, string ContactValue)> ignored)
    {
        if (_form == null)
            return;

        var answers = new Dictionary<string, string>(StringComparer.Ordinal);
        var unmapped = new List<string>();
        foreach (var (field, value) in ignored)
        {
            var qid = FieldId(field);
            if (string.IsNullOrEmpty(qid))
                unmapped.Add(field);
            else
                answers[qid] = value;
        }

        if (answers.Count > 0)
        {
            var owner = Window.GetWindow(this)!;
            string? error = null;
            await ProgressRunner.RunBusyAsync(owner, "Correction de la réponse…", async () =>
            {
                try
                {
                    await _services.WebForms.UpdateResponseAnswersAsync(
                        _form.FormId, row.Resp.ResponseId, answers);
                }
                catch (WebFormsException ex)
                {
                    error = ex.Message;
                }
            });

            if (error != null)
            {
                Warn(error);
                return;
            }
        }

        if (unmapped.Count > 0)
            Warn("Champ(s) sans question associée sur le site, non corrigé(s) : " +
                 string.Join(", ", unmapped) + ".");

        // On relit les réponses : la valeur corrigée fait foi et la différence disparaît.
        await LoadAsync();
    }

    /// <summary>Map id de question → champ contact (pour comparer les réponses au contact existant).</summary>
    private Dictionary<string, string> FieldQidMap()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(string field)
        {
            var id = FieldId(field);
            if (!string.IsNullOrEmpty(id))
                map[id] = field;
        }
        Add("prenom");
        Add("nom");
        Add("tel");
        Add("email");
        return map;
    }

    private async void AddToContacts_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PreRow r)
            return;
        if (string.IsNullOrWhiteSpace(r.Email) && string.IsNullOrWhiteSpace(r.RespNom) && string.IsNullOrWhiteSpace(r.RespPrenom))
        {
            Warn("Réponse sans nom, prénom ni e-mail : impossible de créer un contact.");
            return;
        }
        if (!r.IsEmailValid)
        {
            Warn("L'adresse e-mail semble invalide. Corrigez-la avant d'ajouter le contact.");
            return;
        }

        var owner = Window.GetWindow(this)!;
        var a = new Adherent
        {
            Nom = r.RespNom,
            Prenom = r.RespPrenom,
            Telephone = r.RespTelephone,
            Email = r.Email,
            SecondaryEmails = new List<string>(r.MappedSecondaryEmails)
        };

        var result = await ProgressRunner.RunBusyAsync(owner, "Ajout du contact…", async () =>
        {
            a.GoogleResourceName = await _services.Contacts.EnsureContactResourceAsync(a);
            await _services.Contacts.UpdateContactAsync(a.GoogleResourceName, a);
        });

        if (result.Failed > 0)
        {
            MessageBox.Show(owner, result.LastError, "Ajouter aux contacts",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _services.Adherents.Add(a);
        _services.LogContactActivity(ActivityAction.Ajout, a, $"Formulaire « {_form?.Nom} »");
        _services.SaveAdherents();

        ApplyExistingMatch(r);
        Display();
    }

    private void Alert_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PreRow r)
            return;

        var alerts = r.BuildAlerts(_form?.LabelName);
        if (alerts.Count == 0)
            return;

        var win = new AlertWindow($"{r.Prenom} {r.Nom}".Trim(), alerts)
        {
            Owner = Window.GetWindow(this)
        };
        // « 👁 Voir les changements » → ouvre le détail de la réponse.
        if (win.ShowDialog() == true)
            _ = OpenResponseDetailAsync(r);
    }

    private void SecondaryEmails_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PreRow r || !r.HasSecondaryEmails)
            return;

        new EmailListWindow($"{r.Prenom} {r.Nom}".Trim(), r.SecondaryEmails)
        {
            Owner = Window.GetWindow(this)
        }.ShowDialog();
    }

    // ---- Validation groupée ----------------------------------------------

    private async void Validate_Click(object sender, RoutedEventArgs e)
    {
        if (_form == null)
            return;
        if (string.IsNullOrWhiteSpace(_form.LabelResourceName))
        {
            Warn("Associez d'abord un libellé au formulaire (bouton 🏷 Libellé dans la liste).");
            return;
        }

        var selected = _displayed.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0)
            return;

        var cancelled = selected.Count(r => r.Status == "Annulée");
        var toProcess = selected.Where(r => r.Status != "Annulée" && !string.IsNullOrWhiteSpace(r.Email)).ToList();
        var noEmail = selected.Count(r => r.Status != "Annulée" && string.IsNullOrWhiteSpace(r.Email));
        if (toProcess.Count == 0)
        {
            Warn("Aucune réponse validable sélectionnée (annulées ou sans e-mail).");
            return;
        }

        // Personnes dont les infos diffèrent de leur contact : on demande un choix appliqué à toutes
        // (mettre à jour les contacts avec les réponses, ou garder l'état actuel). Ce choix tient
        // lieu de confirmation ; sans différence, on garde la confirmation simple.
        var changedCount = toProcess.Count(r => r.DiffFields.Count > 0);
        var applyChanges = true;
        if (changedCount > 0)
        {
            var choice = ValidateChangesWindow.Ask(Window.GetWindow(this), changedCount);
            if (choice == ChangesChoice.Cancel)
                return;
            applyChanges = choice == ChangesChoice.Update;
        }
        else if (!ConfirmWindow.Ask(Window.GetWindow(this), "Valider les inscriptions",
                     $"Créer/associer {toProcess.Count} adhérent(s) au libellé « {_form.LabelName} » ?",
                     "Valider", "✔", danger: false))
        {
            return;
        }

        var owner = Window.GetWindow(this)!;
        var details = $"Formulaire « {_form.Nom} »";
        // Seules les personnes réellement validées quittent la liste (une erreur Google les y laisse).
        var succeeded = new List<PreRow>();
        var contactIds = new Dictionary<PreRow, string>();
        var result = await ProgressRunner.RunAsync(owner, "Validation des inscriptions…", toProcess, async r =>
        {
            // Contact déjà rapproché (lien durable / e-mail) : évite un doublon si l'e-mail a changé.
            var existing = r.Existing != null && _services.Adherents.Contains(r.Existing)
                ? r.Existing
                : FindContactForResponse(r.Email);

            var a = existing ?? new Adherent();
            // Nouveau contact : on pose l'e-mail vérifié comme principal. Contact existant : on
            // n'écrase pas son e-mail (il a pu être modifié à la main), le lien durable suffit à l'identifier.
            if (existing == null)
                a.Email = r.Email;

            // Nouveau contact : on applique toujours les valeurs de la réponse. Contact existant : on
            // n'applique les changements que si l'admin a choisi « Mettre à jour » (sinon état actuel
            // conservé). Un champ vide dans la réponse ne remplace jamais une valeur existante.
            if (existing == null || applyChanges)
            {
                if (!string.IsNullOrWhiteSpace(r.RespNom)) a.Nom = r.RespNom;
                if (!string.IsNullOrWhiteSpace(r.RespPrenom)) a.Prenom = r.RespPrenom;
                if (!string.IsNullOrWhiteSpace(r.RespTelephone)) a.Telephone = r.RespTelephone;

                // Mails secondaires fournis dans la réponse : ils REMPLACENT ceux du contact (hors
                // e-mail principal). Si la réponse n'en fournit aucun, on garde ceux du contact.
                if (r.MappedSecondaryEmails.Count > 0)
                    a.SecondaryEmails = r.MappedSecondaryEmails
                        .Where(m => !string.Equals(m, a.Email, StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
            }

            // Contact déjà lié → mise à jour de SA ressource (ne pas rechercher par e-mail, qui a pu
            // changer, sinon création d'un doublon). Recherche/création seulement s'il n'est pas lié.
            if (string.IsNullOrEmpty(a.GoogleResourceName))
                a.GoogleResourceName = await _services.Contacts.EnsureContactResourceAsync(a);
            await _services.Contacts.UpdateContactAsync(a.GoogleResourceName, a);
            await _services.Contacts.SetMembershipAsync(a.GoogleResourceName, _form!.LabelResourceName, add: true);

            if (existing == null)
            {
                _services.Adherents.Add(a);
                _services.LogContactActivity(ActivityAction.Ajout, a, details);
            }
            _services.LogContactActivity(ActivityAction.Association, a, $"{_form.LabelName} ({details})");
            RememberContactLink(r.Email, a.Id);
            contactIds[r] = a.Id.ToString();
            succeeded.Add(r);
        });

        _services.SaveAdherents();

        // Une inscription validée quitte la liste des réponses et rejoint « ✅ Inscrits ».
        foreach (var r in succeeded)
        {
            _state.Validated.RemoveAll(v => string.Equals(v.Email, r.Email, StringComparison.OrdinalIgnoreCase));
            _state.Validated.Add(new ValidatedEntry
            {
                Email = r.Email,
                ContactId = contactIds.GetValueOrDefault(r, string.Empty),
                Nom = r.RespNom,
                Prenom = r.RespPrenom,
                Telephone = r.RespTelephone,
                LabelName = _form.LabelName,
                LabelResourceName = _form.LabelResourceName,
                SubmittedAt = r.SubmittedAt,
            });
            _state.StatusOverrides.Remove(r.Email);
            _allRows.Remove(r);
        }
        _services.FormStates.Save();
        Display();

        var msg = $"{result.Ok} inscription(s) validée(s).";
        if (result.Failed > 0)
            msg += $"\n{result.Failed} en erreur : {result.LastError}";
        if (noEmail > 0)
            msg += $"\n{noEmail} ignorée(s) (aucun e-mail).";
        if (cancelled > 0)
            msg += $"\n{cancelled} annulée(s) ignorée(s).";
        MessageBox.Show(owner, msg, "Validation", MessageBoxButton.OK,
            result.Failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    // ---- Personnes inscrites (page inline) & export ----------------------

    /// <summary>Affiche la page inline des personnes inscrites (validées) de ce formulaire.</summary>
    private void RegisteredPeople_Click(object sender, RoutedEventArgs e)
    {
        if (_form == null)
            return;

        _registeredRestored = false;
        RegisteredSearchBox.Text = string.Empty;
        RegisteredTitle.Text = $"Personnes inscrites — {_form.Nom}";

        ResponsesPanel.Visibility = Visibility.Collapsed;
        RegisteredPanel.Visibility = Visibility.Visible;

        BuildRegistered();
        DisplayRegistered();
    }

    /// <summary>Va aux préinscrits (depuis la page inscrits).</summary>
    private void ToPreinscrits_Click(object sender, RoutedEventArgs e) => ShowResponses(waitlist: false);

    /// <summary>Va à la liste d'attente (depuis la page inscrits).</summary>
    private void RegisteredWaitlist_Click(object sender, RoutedEventArgs e) => ShowResponses(waitlist: true);

    /// <summary>
    /// Bascule de la page inscrits vers les réponses (préinscrits ou liste d'attente) ; recharge si
    /// des personnes ont été remises en préinscription pour qu'elles réapparaissent.
    /// </summary>
    private void ShowResponses(bool waitlist)
    {
        RegisteredPanel.Visibility = Visibility.Collapsed;
        ResponsesPanel.Visibility = Visibility.Visible;

        _waitlistMode = waitlist;
        WaitlistBtn.Content = waitlist ? "← Liste des préinscrits" : "⏳ Liste d'attente";

        if (_registeredRestored)
        {
            _registeredRestored = false;
            _ = LoadAsync();
        }
        else
        {
            Display();
        }
    }

    /// <summary>Exporte en CSV les inscrits affichés (recherche comprise).</summary>
    private void ExportRegistered_Click(object sender, RoutedEventArgs e)
    {
        if (_registeredDisplayed.Count == 0)
        {
            Warn("Rien à exporter : aucun inscrit affiché.");
            return;
        }

        var name = string.Join("_", (_form?.Nom ?? "inscrits").Split(Path.GetInvalidFileNameChars()));
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exporter les inscrits affichés",
            Filter = "Fichier CSV (*.csv)|*.csv",
            FileName = $"{name}_inscrits.csv",
            InitialDirectory = AppPaths.DownloadsFolder,
            AddExtension = true,
        };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            CsvRegisteredExporter.Export(dlg.FileName, _registeredDisplayed);
        }
        catch (IOException ex) { Warn("Export impossible : " + ex.Message); return; }
        catch (UnauthorizedAccessException ex) { Warn("Export impossible : " + ex.Message); return; }

        MessageBox.Show(Window.GetWindow(this),
            $"{_registeredDisplayed.Count} inscrit(s) exporté(s) vers :\n{dlg.FileName}", "Export CSV",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>Construit les lignes « inscrits » : le contact d'aujourd'hui, à défaut l'instantané figé.</summary>
    private void BuildRegistered()
    {
        _registeredAll.Clear();
        var changed = false;
        foreach (var v in _state.Validated)
        {
            var contact = FindContactForEntry(v);
            // Entrée d'avant cette évolution retrouvée par e-mail : on mémorise le lien stable.
            if (contact != null && string.IsNullOrEmpty(v.ContactId))
            {
                v.ContactId = contact.Id.ToString();
                changed = true;
            }
            _registeredAll.Add(new RegRow(v, contact));
        }
        if (changed)
            _services.FormStates.Save();
    }

    /// <summary>Contact correspondant (par e-mail), null s'il a été supprimé depuis.</summary>
    private Adherent? FindContactByEmail(string? email)
        => string.IsNullOrWhiteSpace(email)
            ? null
            : _services.Adherents.FirstOrDefault(a =>
                !string.IsNullOrWhiteSpace(a.Email) &&
                string.Equals(a.Email, email, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Contact lié à une inscription : d'abord par <b>identifiant stable</b> de l'entrée, puis par
    /// le <b>lien durable</b> réponse↔contact, puis repli sur l'e-mail. Tous ces chemins (sauf le
    /// dernier) résistent à un changement d'e-mail. Renvoie null si le contact a été supprimé.
    /// </summary>
    private Adherent? FindContactForEntry(ValidatedEntry v)
    {
        if (!string.IsNullOrWhiteSpace(v.ContactId) && Guid.TryParse(v.ContactId, out var id))
        {
            var byId = _services.Adherents.FirstOrDefault(a => a.Id == id);
            if (byId != null)
                return byId;
        }
        if (!string.IsNullOrWhiteSpace(v.Email) &&
            _state.ContactLinks.TryGetValue(v.Email, out var linkedId) &&
            Guid.TryParse(linkedId, out var lid))
        {
            var byLink = _services.Adherents.FirstOrDefault(a => a.Id == lid);
            if (byLink != null)
                return byLink;
        }
        return FindContactByEmail(v.Email);
    }

    /// <summary>Contact d'une réponse : lien durable mémorisé, puis repli sur l'e-mail. Null si nouveau.</summary>
    private Adherent? FindContactForResponse(string verifiedEmail)
    {
        if (!string.IsNullOrWhiteSpace(verifiedEmail) &&
            _state.ContactLinks.TryGetValue(verifiedEmail, out var linkedId) &&
            Guid.TryParse(linkedId, out var lid))
        {
            var byLink = _services.Adherents.FirstOrDefault(a => a.Id == lid);
            if (byLink != null)
                return byLink;
        }
        return FindContactByEmail(verifiedEmail);
    }

    /// <summary>Mémorise (en mémoire) le lien e-mail vérifié → identifiant du contact ; enregistré via <see cref="FlushLinks"/>.</summary>
    private void RememberContactLink(string verifiedEmail, Guid contactId)
    {
        var id = contactId.ToString();
        if (_state.ContactLinks.TryGetValue(verifiedEmail, out var cur) &&
            string.Equals(cur, id, StringComparison.Ordinal))
            return;
        _state.ContactLinks[verifiedEmail] = id;
        _linksDirty = true;
    }

    /// <summary>Enregistre les liens réponse↔contact ajoutés depuis le dernier appel (au plus une écriture).</summary>
    private void FlushLinks()
    {
        if (!_linksDirty)
            return;
        _linksDirty = false;
        _services.FormStates.Save();
    }

    /// <summary>Applique le filtre de recherche et affiche la page des inscrits.</summary>
    private void DisplayRegistered()
    {
        var term = RegisteredSearchBox.Text?.Trim();
        IEnumerable<RegRow> src = _registeredAll;
        if (!string.IsNullOrEmpty(term))
        {
            bool C(string? v) => v != null && v.Contains(term, StringComparison.OrdinalIgnoreCase);
            src = src.Where(r => C(r.Nom) || C(r.Prenom) || C(r.Telephone) || C(r.Email));
        }

        _registeredDisplayed = src
            .OrderBy(r => r.Nom, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        RegisteredGrid.ItemsSource = _registeredDisplayed;
        RegisteredCountText.Text = $"{_registeredDisplayed.Count} inscrit(s)";

        RegisteredEmpty.Text = _registeredAll.Count == 0
            ? "Aucune personne inscrite pour ce formulaire."
            : "Aucun inscrit ne correspond à cette recherche.";
        RegisteredEmpty.Visibility = _registeredDisplayed.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RegisteredSearch_Changed(object sender, TextChangedEventArgs e) => DisplayRegistered();

    /// <summary>
    /// Remet une personne dans la liste des préinscrits : elle redevient une réponse à traiter et
    /// est <b>dissociée du libellé</b> du formulaire (l'adhérent créé, lui, reste).
    /// </summary>
    private async void RegisteredRestore_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RegRow row)
            return;

        if (!ConfirmWindow.Ask(Window.GetWindow(this), "Remettre en préinscription",
                $"Remettre « {row.NomComplet} » dans la liste des préinscrits ?\n" +
                "La personne sera dissociée du libellé du formulaire (l'adhérent, lui, reste).",
                "Remettre", "↩", danger: false))
            return;

        // Libellé à retirer : celui enregistré sur l'entrée, à défaut celui résolu par nom, à défaut celui du formulaire.
        var labelRes = !string.IsNullOrEmpty(row.Entry.LabelResourceName)
            ? row.Entry.LabelResourceName
            : ResolveLabelResourceByName(row.Entry.LabelName) ?? _form?.LabelResourceName;
        var contact = FindContactForEntry(row.Entry);

        if (contact != null && !string.IsNullOrWhiteSpace(contact.GoogleResourceName) &&
            !string.IsNullOrWhiteSpace(labelRes))
        {
            var owner = Window.GetWindow(this)!;
            var result = await ProgressRunner.RunBusyAsync(owner, "Dissociation du libellé…", async () =>
            {
                await _services.Contacts.SetMembershipAsync(contact.GoogleResourceName, labelRes!, add: false);
            });

            if (result.Failed > 0)
            {
                // On ne remet pas en préinscription si la dissociation a échoué (état cohérent).
                MessageBox.Show(owner, result.LastError, "Remettre en préinscription",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _services.LogContactActivity(ActivityAction.Dissociation, contact,
                $"{row.Entry.LabelName} (remise en préinscription — {_form?.Nom})");
        }

        _state.Validated.Remove(row.Entry);
        _services.FormStates.Save();
        _registeredRestored = true;

        _registeredAll.Remove(row);
        DisplayRegistered();
    }

    private void RegisteredSecondaryEmails_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RegRow row || !row.HasSecondaryEmails)
            return;

        new EmailListWindow(row.NomComplet, row.SecondaryEmails)
        {
            Owner = Window.GetWindow(this)
        }.ShowDialog();
    }

    /// <summary>Exporte en CSV les lignes affichées (filtre de recherche et mode liste d'attente compris).</summary>
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_displayed.Count == 0)
        {
            Warn("Rien à exporter : aucune ligne affichée.");
            return;
        }

        var name = string.Join("_", (_form?.Nom ?? "reponses").Split(Path.GetInvalidFileNameChars()));
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exporter les réponses affichées",
            Filter = "Fichier CSV (*.csv)|*.csv",
            FileName = $"{name}{(_waitlistMode ? "_liste_attente" : string.Empty)}.csv",
            InitialDirectory = AppPaths.DownloadsFolder,
            AddExtension = true,
        };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            CsvResponseExporter.Export(dlg.FileName, _displayed, _waitlistMode);
        }
        catch (IOException ex)
        {
            Warn("Export impossible : " + ex.Message);
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            Warn("Export impossible : " + ex.Message);
            return;
        }

        MessageBox.Show(Window.GetWindow(this),
            $"{_displayed.Count} ligne(s) exportée(s) vers :\n{dlg.FileName}", "Export CSV",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Warn(string message)
        => MessageBox.Show(Window.GetWindow(this), message, "Formulaires d'inscription",
            MessageBoxButton.OK, MessageBoxImage.Warning);
}

/// <summary>
/// Ligne du tableau des réponses. Les colonnes affichent les informations DU CONTACT rapproché
/// (<see cref="Existing"/>) ; à défaut, celles de la réponse (Resp*) ; à défaut encore, seul
/// l'e-mail du répondant avec la mention « pas dans mes contacts ».
/// </summary>
internal sealed class PreRow : INotifyPropertyChanged, CsvResponseExporter.IResponseLine
{
    // Libellés de statut (partagés avec la vue).
    public const string StatusPre = "En préinscription";
    public const string StatusWait = "En attente";
    public const string StatusCancel = "Annulée";

    public FormResponseRow Resp { get; set; } = null!;

    // ---- Valeurs issues de la réponse (servent à l'ajout, à la validation et aux comparaisons) ----
    public string RespNom { get; set; } = string.Empty;
    public string RespPrenom { get; set; } = string.Empty;
    public string RespTelephone { get; set; } = string.Empty;

    /// <summary>E-mail identifiant le répondant (e-mail vérifié, à défaut e-mail saisi).</summary>
    public string Email { get; set; } = string.Empty;

    public DateTime SubmittedAt { get; set; }

    private string _status = string.Empty;
    public string Status
    {
        get => _status;
        set
        {
            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsWaiting));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    /// <summary>Vrai si la personne est en liste d'attente (bouton ↩ au lieu de ⏳).</summary>
    public bool IsWaiting => Status == StatusWait;

    /// <summary>Statut forcé à la main (et non déduit des réponses) : signalé par un astérisque.</summary>
    public bool StatusForced { get; set; }
    public string StatusText => StatusForced ? Status + " *" : Status;

    /// <summary>Contact existant correspondant (null si le répondant n'est pas dans les contacts).</summary>
    public Adherent? Existing { get; set; }

    // ---- Valeurs affichées : le contact s'il existe, sinon la réponse ----
    public string Nom => Existing?.Nom ?? RespNom;
    public string Prenom => Existing?.Prenom ?? RespPrenom;
    public string Telephone => Existing?.Telephone ?? RespTelephone;
    public string EmailText => !string.IsNullOrWhiteSpace(Existing?.Email) ? Existing!.Email : Email;

    // ---- Champs que la réponse contredit (cellule jaune + info-bulle « Réponse : … ») ----
    public bool NomDiff { get; set; }
    public bool PrenomDiff { get; set; }
    public bool TelDiff { get; set; }
    public bool EmailDiff { get; set; }

    public string? NomTip => NomDiff ? $"Réponse : {RespNom}" : null;
    public string? PrenomTip => PrenomDiff ? $"Réponse : {RespPrenom}" : null;
    public string? TelTip => TelDiff ? $"Réponse : {RespTelephone}" : null;
    public string? EmailTip => EmailDiff ? $"Réponse : {Email}" : null;

    /// <summary>E-mail au bon format (après correction automatique) ; sinon affiché en rouge.</summary>
    public bool IsEmailValid { get; set; } = true;
    public System.Windows.Media.Brush EmailBrush => IsEmailValid ? OkBrush : ErrorBrush;

    /// <summary>Répondant absent de mes contacts (ligne rouge + mention + bouton d'ajout).</summary>
    public bool NotInContacts { get; set; }

    /// <summary>Répondant déjà membre du libellé du formulaire (⚠).</summary>
    public bool AlreadyMember { get; set; }

    /// <summary>
    /// Incohérence : la personne est <b>membre du libellé</b> du formulaire (côté Google) mais
    /// <b>absente de mes contacts</b>. Surlignée en jaune avec un avertissement dédié (au lieu du
    /// rouge « pas dans mes contacts »), car il faut l'ajouter/resynchroniser, pas la traiter comme
    /// une personne inconnue.
    /// </summary>
    public bool MemberNotInContacts => NotInContacts && AlreadyMember;

    /// <summary>Champs de la réponse qui diffèrent du contact existant (nom/prénom/téléphone/e-mail).</summary>
    public List<string> DiffFields { get; set; } = new();

    /// <summary>Vrai si au moins une alerte s'applique à ce répondant (bouton ⚠ ; sinon « N/A »).</summary>
    public bool HasAlert => NotInContacts || AlreadyMember || DiffFields.Count > 0 || !IsEmailValid;

    /// <summary>Construit la liste lisible des alertes du répondant.</summary>
    public List<string> BuildAlerts(string? labelName)
    {
        var list = new List<string>();
        var label = string.IsNullOrWhiteSpace(labelName) ? "du formulaire" : $"« {labelName} »";
        if (MemberNotInContacts)
        {
            // Cas incohérent : on remplace les deux messages génériques par un seul, explicite.
            list.Add($"Associé au libellé {label} mais absent de vos contacts.");
        }
        else
        {
            if (NotInContacts)
                list.Add("N'est pas dans vos contacts.");
            if (AlreadyMember)
                list.Add($"Déjà associé au libellé {label}.");
        }
        if (DiffFields.Count > 0)
            list.Add("Informations différentes du contact existant : " + string.Join(", ", DiffFields) + ".");
        if (!IsEmailValid)
            list.Add("Adresse e-mail au format invalide.");
        return list;
    }

    private static readonly System.Windows.Media.Brush OkBrush =
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x33, 0x33));
    private static readonly System.Windows.Media.Brush ErrorBrush =
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC6, 0x28, 0x28));

    /// <summary>E-mails secondaires fournis dans la réponse (champ « secondaryEmails » mappé).</summary>
    public List<string> MappedSecondaryEmails { get; set; } = new();

    /// <summary>E-mails secondaires affichés (union réponse + contact existant).</summary>
    public List<string> SecondaryEmails { get; set; } = new();
    public bool HasSecondaryEmails => SecondaryEmails.Count > 0;
    public string SecondaryEmailsBadge => HasSecondaryEmails ? $"✉ {SecondaryEmails.Count}" : "N/A";

    /// <summary>Date de dernière modification du formulaire par le répondant (null s'il n'a pas modifié).</summary>
    public DateTime? ModifiedAt { get; set; }
    public string ModifiedText => ModifiedAt.HasValue ? ModifiedAt.Value.ToString("dd/MM/yyyy HH:mm") : "N/A";

    /// <summary>Vrai si le répondant est nouveau ou si ses infos diffèrent du contact existant (surlignage jaune).</summary>
    public bool Highlight { get; set; }

    private int _rang;
    public int Rang
    {
        get => _rang;
        set { _rang = value; OnPropertyChanged(); OnPropertyChanged(nameof(RangText)); }
    }
    public string RangText => _rang > 0 ? $"#{_rang}" : string.Empty;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    // Vues en lecture seule pour l'export CSV (les listes internes restent modifiables).
    IReadOnlyList<string> CsvResponseExporter.IResponseLine.SecondaryEmails => SecondaryEmails;
    IReadOnlyList<string> CsvResponseExporter.IResponseLine.DiffFields => DiffFields;

    /// <summary>Signale que toutes les valeurs affichées ont pu changer (contact ajouté ou mis à jour).</summary>
    public void NotifyDisplayChanged() => OnPropertyChanged(string.Empty);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>
/// Ligne de la page « Personnes inscrites » : l'entrée figée à la validation + le contact
/// d'aujourd'hui s'il existe encore. Les colonnes affichent le contact « tel quel » ; à défaut
/// de contact (supprimé depuis), les valeurs figées à la validation avec la mention idoine.
/// </summary>
internal sealed class RegRow : CsvRegisteredExporter.IRegisteredLine
{
    public ValidatedEntry Entry { get; }

    /// <summary>Contact actuel (null s'il a été supprimé des contacts depuis la validation).</summary>
    private readonly Adherent? _contact;

    public RegRow(ValidatedEntry entry, Adherent? contact)
    {
        Entry = entry;
        _contact = contact;
    }

    public string Nom => _contact?.Nom ?? Entry.Nom;
    public string Prenom => _contact?.Prenom ?? Entry.Prenom;
    public string Telephone => _contact?.Telephone ?? Entry.Telephone;
    public string Email => _contact?.Email ?? Entry.Email;
    public string NomComplet => $"{Prenom} {Nom}".Trim();

    /// <summary>E-mails secondaires du contact (aucun si le contact n'existe plus).</summary>
    public IReadOnlyList<string> SecondaryEmails => _contact?.SecondaryEmails ?? (IReadOnlyList<string>)Array.Empty<string>();
    public bool HasSecondaryEmails => SecondaryEmails.Count > 0;
    public string SecondaryEmailsBadge => HasSecondaryEmails ? $"✉ {SecondaryEmails.Count}" : "N/A";

    /// <summary>Date d'ajout aux contacts (« N/A » si le contact n'existe plus).</summary>
    public string AddedText => _contact != null ? _contact.DateInscription.ToString("dd/MM/yyyy") : "N/A";

    /// <summary>Signalé dans la colonne Nom quand la personne n'est plus dans les contacts.</summary>
    public bool NotInContacts => _contact == null;
}
