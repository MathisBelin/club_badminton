using System.ComponentModel;
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
    private readonly List<PreRow> _allRows = new();
    private List<PreRow> _displayed = new();
    private bool _waitlistMode;

    // Libellés de statut d'une préinscription.
    private const string StatusPre = "En préinscription";
    private const string StatusWait = "En attente";
    private const string StatusCancel = "Annulée";

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
        PickerPanel.Visibility = Visibility.Visible;

        var forms = _services.FormRepository.Load()
            .OrderByDescending(f => f.DateCreation).ToList();
        FormsGrid.ItemsSource = forms;
        PickerEmpty.Visibility = forms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

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
        ResponsesPanel.Visibility = Visibility.Visible;

        await LoadAsync();
    }

    // ---- Chargement -------------------------------------------------------

    private async Task LoadAsync()
    {
        if (_form == null)
            return;

        var owner = Window.GetWindow(this)!;
        var result = await ProgressRunner.RunBusyAsync(owner, "Lecture des réponses…", async () =>
        {
            _questions = await _services.Forms.GetFormQuestionsAsync(_form.FormId);
            var idMap = _questions.GroupBy(q => q.QuestionId).ToDictionary(g => g.Key, g => g.Key);
            _responses = await _services.Forms.ListResponsesAsync(_form.FormId, idMap);

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

        foreach (var g in _responses.GroupBy(Key, StringComparer.OrdinalIgnoreCase)
                                    .Where(g => !string.IsNullOrWhiteSpace(g.Key)))
        {
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
                Nom = Ans(latest, nomId),
                Prenom = Ans(latest, prenomId),
                Telephone = PhoneFormatter.Format(Ans(latest, telId)),
                Email = email,
                IsEmailValid = string.IsNullOrWhiteSpace(email) || EmailValidator.IsValid(email),
                MappedSecondaryEmails = ParseEmails(Ans(latest, secondaryId), email),
                SubmittedAt = originalAt,
                ModifiedAt = modifiedAt,
                Status = StatusOf(latest),
                AlreadyMember = !string.IsNullOrWhiteSpace(email) && _labelMemberEmails.Contains(email)
            };

            ApplyExistingMatch(row);

            row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PreRow.IsSelected)) UpdateBar(); };
            _allRows.Add(row);
        }
    }

    /// <summary>
    /// Rapproche la réponse d'un contact existant (par e-mail, à défaut nom+prénom) : récupère ses
    /// e-mails secondaires et surligne (jaune) si le répondant est nouveau ou si ses infos ont changé.
    /// </summary>
    private void ApplyExistingMatch(PreRow row)
    {
        var existing = _services.Adherents.FirstOrDefault(a =>
            !string.IsNullOrWhiteSpace(row.Email) && !string.IsNullOrWhiteSpace(a.Email) &&
            string.Equals(a.Email, row.Email, StringComparison.OrdinalIgnoreCase));

        if (existing == null && string.IsNullOrWhiteSpace(row.Email))
            existing = _services.Adherents.FirstOrDefault(a =>
                !string.IsNullOrWhiteSpace(row.Nom) && !string.IsNullOrWhiteSpace(row.Prenom) &&
                string.Equals(a.Nom, row.Nom, StringComparison.CurrentCultureIgnoreCase) &&
                string.Equals(a.Prenom, row.Prenom, StringComparison.CurrentCultureIgnoreCase));

        row.Existing = existing;

        if (existing == null)
        {
            // Répondant absent de mes contacts → à ajouter (ligne rouge).
            row.SecondaryEmails = new List<string>(row.MappedSecondaryEmails);
            row.NotInContacts = true;
            return;
        }

        // Mails secondaires affichés = union de ceux du contact et de ceux fournis dans la réponse.
        row.SecondaryEmails = existing.SecondaryEmails
            .Concat(row.MappedSecondaryEmails)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Comparaison des champs renseignés dans la réponse (seuls les champs fournis comptent).
        var diffs = new List<string>();
        if (!string.IsNullOrWhiteSpace(row.Nom) &&
            !string.Equals(row.Nom, existing.Nom, StringComparison.CurrentCultureIgnoreCase))
            diffs.Add("nom");
        if (!string.IsNullOrWhiteSpace(row.Prenom) &&
            !string.Equals(row.Prenom, existing.Prenom, StringComparison.CurrentCultureIgnoreCase))
            diffs.Add("prénom");
        if (!string.IsNullOrWhiteSpace(row.Telephone) &&
            Digits(row.Telephone) != Digits(existing.Telephone))
            diffs.Add("téléphone");

        row.DiffFields = diffs;
        row.Highlight = diffs.Count > 0;
    }

    private static string Digits(string? s)
        => new string((s ?? string.Empty).Where(char.IsDigit).ToArray());

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

    // ---- Affichage / filtres ---------------------------------------------

    private void Display()
    {
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
        return C(r.Nom) || C(r.Prenom) || C(r.Telephone) || C(r.Email);
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
        WaitlistBtn.Content = _waitlistMode ? "← Toutes les réponses" : "⏳ Liste d'attente";
        Display();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        var check = (sender as System.Windows.Controls.Primitives.ToggleButton)?.IsChecked == true;
        foreach (var r in _displayed)
            r.IsSelected = check;
    }

    private void UpdateBar()
    {
        var count = _displayed.Count(r => r.IsSelected);
        ValidateBtn.Content = count > 0 ? $"✔ Valider ({count})" : "✔ Valider";
        HeaderSelectAllBox.IsChecked = count == 0 ? false
            : count == _displayed.Count && _displayed.Count > 0 ? true : (bool?)null;
    }

    // ---- Détail (réponses d'une personne) --------------------------------

    private void Detail_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PreRow r)
            return;

        var win = new ResponseDetailWindow(_questions, r.Resp, $"{r.Prenom} {r.Nom}".Trim(),
            r.Existing, FieldQidMap(), _services)
        {
            Owner = Window.GetWindow(this)
        };
        win.ShowDialog();

        if (win.ContactUpdated)
        {
            ApplyExistingMatch(r);
            Display();
        }
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
        if (string.IsNullOrWhiteSpace(r.Email) && string.IsNullOrWhiteSpace(r.Nom) && string.IsNullOrWhiteSpace(r.Prenom))
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
            Nom = r.Nom,
            Prenom = r.Prenom,
            Telephone = r.Telephone,
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
        var text = alerts.Count > 0 ? string.Join("\n", alerts.Select(a => "• " + a)) : "(N/A)";
        MessageBox.Show(Window.GetWindow(this), text,
            $"Alertes — {r.Prenom} {r.Nom}".Trim(), MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void SecondaryEmails_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PreRow r || !r.HasSecondaryEmails)
            return;

        var list = string.Join("\n", r.SecondaryEmails.Select(m => "• " + m));
        MessageBox.Show(Window.GetWindow(this), list,
            $"E-mails secondaires — {r.Prenom} {r.Nom}".Trim(),
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ---- Validation groupée ----------------------------------------------

    private async void Validate_Click(object sender, RoutedEventArgs e)
    {
        if (_form == null)
            return;
        if (string.IsNullOrWhiteSpace(_form.LabelResourceName))
        {
            Warn("Associez d'abord un libellé au formulaire (page Google Forms → ⚙ Configuration).");
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

        if (!ConfirmWindow.Ask(Window.GetWindow(this), "Valider les inscriptions",
                $"Créer/associer {toProcess.Count} adhérent(s) au libellé « {_form.LabelName} » ?",
                "Valider", "✔", danger: false))
            return;

        var owner = Window.GetWindow(this)!;
        var details = $"Formulaire « {_form.Nom} »";
        var result = await ProgressRunner.RunAsync(owner, "Validation des inscriptions…", toProcess, async r =>
        {
            var existing = _services.Adherents.FirstOrDefault(a =>
                !string.IsNullOrWhiteSpace(a.Email) &&
                string.Equals(a.Email, r.Email, StringComparison.OrdinalIgnoreCase));

            var a = existing ?? new Adherent();
            a.Nom = r.Nom;
            a.Prenom = r.Prenom;
            a.Telephone = r.Telephone;
            a.Email = r.Email;

            // Mails secondaires fournis dans la réponse : fusionnés avec les existants (hors e-mail principal).
            if (r.MappedSecondaryEmails.Count > 0)
                a.SecondaryEmails = a.SecondaryEmails
                    .Concat(r.MappedSecondaryEmails)
                    .Where(m => !string.Equals(m, a.Email, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            a.GoogleResourceName = await _services.Contacts.EnsureContactResourceAsync(a);
            await _services.Contacts.UpdateContactAsync(a.GoogleResourceName, a);
            await _services.Contacts.SetMembershipAsync(a.GoogleResourceName, _form!.LabelResourceName, add: true);

            if (existing == null)
            {
                _services.Adherents.Add(a);
                _services.LogContactActivity(ActivityAction.Ajout, a, details);
            }
            _services.LogContactActivity(ActivityAction.Association, a, $"{_form.LabelName} ({details})");
        });

        _services.SaveAdherents();

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

    private void Warn(string message)
        => MessageBox.Show(Window.GetWindow(this), message, "Préinscriptions",
            MessageBoxButton.OK, MessageBoxImage.Warning);
}

/// <summary>Ligne du tableau des réponses (contact + réponse brute pour le détail).</summary>
internal sealed class PreRow : INotifyPropertyChanged
{
    public FormResponseRow Resp { get; set; } = null!;
    public string Nom { get; set; } = string.Empty;
    public string Prenom { get; set; } = string.Empty;
    public string Telephone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>Contact existant correspondant (null si le répondant n'est pas dans les contacts).</summary>
    public Adherent? Existing { get; set; }

    /// <summary>E-mail au bon format (après correction automatique) ; sinon affiché en rouge.</summary>
    public bool IsEmailValid { get; set; } = true;
    public System.Windows.Media.Brush EmailBrush => IsEmailValid ? OkBrush : ErrorBrush;

    /// <summary>Répondant absent de mes contacts (ligne rouge + bouton d'ajout).</summary>
    public bool NotInContacts { get; set; }

    /// <summary>Répondant déjà membre du libellé du formulaire (⚠).</summary>
    public bool AlreadyMember { get; set; }

    /// <summary>Champs de la réponse qui diffèrent du contact existant (nom/prénom/téléphone).</summary>
    public List<string> DiffFields { get; set; } = new();

    /// <summary>Vrai si au moins une alerte s'applique à ce répondant (bouton ⚠ ; sinon « N/A »).</summary>
    public bool HasAlert => NotInContacts || AlreadyMember || DiffFields.Count > 0 || !IsEmailValid;

    /// <summary>Construit la liste lisible des alertes du répondant.</summary>
    public List<string> BuildAlerts(string? labelName)
    {
        var list = new List<string>();
        if (NotInContacts)
            list.Add("N'est pas dans vos contacts.");
        if (AlreadyMember)
            list.Add(string.IsNullOrWhiteSpace(labelName)
                ? "Déjà associé au libellé du formulaire."
                : $"Déjà associé au libellé « {labelName} ».");
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

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
