using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using BadmintonClub.Helpers;
using BadmintonClub.Models;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

public partial class FormConfigWindow : Window
{
    private readonly AppServices _services;
    private readonly FormRecord _form;
    private readonly ObservableCollection<ConfigQuestion> _questions = new();
    private bool _fieldMapReady;

    /// <summary>Choix de règle proposés par option (liés en XAML via RelativeSource).</summary>
    public IReadOnlyList<RuleChoice> Rules { get; } = new List<RuleChoice>
    {
        new(string.Empty, "Aucun"),
        new("waitlist", "Ajouter à la liste d'attente"),
        new("cancel", "Annuler l'inscription")
    };

    public FormConfigWindow(AppServices services, FormRecord form)
    {
        InitializeComponent();
        _services = services;
        _form = form;

        Title = $"Configuration — {form.Nom}";
        NameBox.Text = form.Nom;
        QuestionsList.ItemsSource = _questions;

        BuildLabelCombo();
        Loaded += async (_, _) => await LoadAsync();
    }

    private void BuildLabelCombo()
    {
        var choices = new List<LabelChoice> { new(string.Empty, "(aucun)") };
        choices.AddRange(_services.CachedLabels.Select(l => new LabelChoice(l.ResourceName, l.Nom)));
        LabelCombo.ItemsSource = choices;
        LabelCombo.SelectedValue = _form.LabelResourceName ?? string.Empty;
    }

    private async Task LoadAsync()
    {
        List<GoogleFormsService.FormQuestionDetail> detail = new();
        var result = await ProgressRunner.RunBusyAsync(this, "Lecture du formulaire…", async () =>
        {
            detail = await _services.Forms.GetFormDetailAsync(_form.FormId);
        });

        if (result.Failed > 0)
        {
            MessageBox.Show(this, result.LastError, "Configuration", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        BuildFieldMapCombos(detail);

        _questions.Clear();
        foreach (var q in detail)
        {
            var cq = new ConfigQuestion { Title = q.Title, IsSingleChoice = q.IsSingleChoice };
            if (q.IsSingleChoice)
                foreach (var opt in q.Options)
                    cq.Options.Add(new ConfigOption
                    {
                        QuestionId = q.QuestionId,
                        OptionValue = opt,
                        Rule = _form.AnswerRules.GetValueOrDefault(FormRecord.RuleKey(q.QuestionId, opt), string.Empty)
                    });
            _questions.Add(cq);
        }

        _fieldMapReady = true;

        EmptyHint.Text = _questions.Count == 0
            ? "Aucune question lue (le formulaire est-il vide ?)."
            : _questions.All(q => !q.IsSingleChoice)
                ? "Aucune question à choix unique à paramétrer."
                : string.Empty;
        EmptyHint.Visibility = string.IsNullOrEmpty(EmptyHint.Text) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Alimente les listes de correspondance champ → question (questions texte uniquement).</summary>
    private void BuildFieldMapCombos(IReadOnlyList<GoogleFormsService.FormQuestionDetail> detail)
    {
        var textQuestions = detail.Where(q => q.IsText).ToList();

        void Fill(System.Windows.Controls.ComboBox combo, string field)
        {
            var choices = new List<QuestionChoice> { new(string.Empty, "(aucune correspondance)") };
            choices.AddRange(textQuestions.Select(q => new QuestionChoice(q.QuestionId, q.Title)));
            combo.ItemsSource = choices;
            combo.SelectedValue = _form.FieldMap.GetValueOrDefault(field, string.Empty);
        }

        Fill(MapPrenom, "prenom");
        Fill(MapNom, "nom");
        Fill(MapTel, "tel");
        Fill(MapEmail, "email");
        Fill(MapSecondaryEmails, "secondaryEmails");
    }

    private async void Enregistrer_Click(object sender, RoutedEventArgs e)
    {
        var newName = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            MessageBox.Show(this, "Saisissez un nom de formulaire.", "Configuration",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (newName != _form.Nom)
        {
            var result = await ProgressRunner.RunBusyAsync(this, "Renommage…",
                () => _services.Forms.RenameFormAsync(_form.FormId, newName));
            if (result.Failed > 0)
            {
                MessageBox.Show(this, result.LastError, "Renommer", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _form.Nom = newName;
        }

        var lblRes = LabelCombo.SelectedValue as string ?? string.Empty;
        _form.LabelResourceName = lblRes;
        _form.LabelName = string.IsNullOrEmpty(lblRes)
            ? string.Empty
            : (LabelCombo.SelectedItem as LabelChoice)?.Nom ?? string.Empty;

        _form.AnswerRules.Clear();
        foreach (var q in _questions.Where(q => q.IsSingleChoice))
            foreach (var opt in q.Options.Where(o => !string.IsNullOrEmpty(o.Rule)))
                _form.AnswerRules[FormRecord.RuleKey(opt.QuestionId, opt.OptionValue)] = opt.Rule;

        // Correspondance manuelle des champs (ignorée si le formulaire n'a pas été lu).
        if (_fieldMapReady)
        {
            _form.FieldMap.Clear();
            void Save(System.Windows.Controls.ComboBox combo, string field)
            {
                if (combo.SelectedValue is string id && !string.IsNullOrEmpty(id))
                    _form.FieldMap[field] = id;
            }
            Save(MapPrenom, "prenom");
            Save(MapNom, "nom");
            Save(MapTel, "tel");
            Save(MapEmail, "email");
            Save(MapSecondaryEmails, "secondaryEmails");
        }

        DialogResult = true;
    }

    private async void SaveAsModel_Click(object sender, RoutedEventArgs e)
    {
        var proposed = string.IsNullOrWhiteSpace(NameBox.Text) ? _form.Nom : NameBox.Text.Trim();
        var dlg = new InputDialog("Enregistrer comme modèle", "Nom du modèle :", proposed) { Owner = this };
        if (dlg.ShowDialog() != true)
            return;
        var name = dlg.Value;
        if (string.IsNullOrWhiteSpace(name))
            return;

        FormTemplate? tpl = null;
        var result = await ProgressRunner.RunBusyAsync(this, "Lecture de la structure…", async () =>
        {
            tpl = await _services.Forms.ExportFormStructureAsync(_form.FormId, name.Trim());
        });

        if (result.Failed > 0 || tpl == null)
        {
            MessageBox.Show(this, result.LastError ?? "Structure illisible.", "Modèle",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (tpl.Items.Count == 0)
        {
            MessageBox.Show(this, "Aucune question exploitable dans ce formulaire.", "Modèle",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            new FormTemplateRepository().Save(tpl);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Échec de l'enregistrement du modèle : " + ex.Message, "Modèle",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBox.Show(this, $"Modèle « {tpl.Name} » enregistré ({tpl.Items.Count} question(s)).",
            "Modèle", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Annuler_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

public sealed record RuleChoice(string Value, string Label);
public sealed record LabelChoice(string ResourceName, string Nom);
public sealed record QuestionChoice(string QuestionId, string Title);

/// <summary>Question affichée dans la configuration (options paramétrables si choix unique).</summary>
internal sealed class ConfigQuestion
{
    public string Title { get; set; } = string.Empty;
    public bool IsSingleChoice { get; set; }
    public bool IsOther => !IsSingleChoice;
    public ObservableCollection<ConfigOption> Options { get; } = new();
}

/// <summary>Une option et sa règle (waitlist / cancel / aucune).</summary>
internal sealed class ConfigOption : INotifyPropertyChanged
{
    public string QuestionId { get; set; } = string.Empty;
    public string OptionValue { get; set; } = string.Empty;

    private string _rule = string.Empty;
    public string Rule
    {
        get => _rule;
        set { _rule = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
