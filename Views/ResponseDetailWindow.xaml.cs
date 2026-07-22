using System.ComponentModel;
using System.Windows;
using BadmintonClub.Helpers;
using BadmintonClub.Models;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

public partial class ResponseDetailWindow : Window
{
    private readonly Adherent? _existing;
    private readonly AppServices? _services;
    private readonly List<QAItem> _items;

    /// <summary>Vrai si le contact a été mis à jour depuis cette fenêtre (pour rafraîchir l'appelant).</summary>
    public bool ContactUpdated { get; private set; }

    public ResponseDetailWindow(
        List<GoogleFormsService.FormQuestionInfo> questions, FormResponseRow response, string personName,
        Adherent? existing = null, IReadOnlyDictionary<string, string>? qidToField = null, AppServices? services = null)
    {
        InitializeComponent();
        _existing = existing;
        _services = services;

        HeaderText.Text = string.IsNullOrWhiteSpace(personName) ? "Réponses" : $"Réponses — {personName}";
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(response.RespondentEmail))
            parts.Add($"Répondant : {response.RespondentEmail}");
        parts.Add($"Envoyé le {response.SubmittedAt:dd/MM/yyyy HH:mm}");
        SubText.Text = string.Join("  ·  ", parts);

        _items = questions.Select(q =>
        {
            var raw = response.Fields.TryGetValue(q.QuestionId, out var a) ? a : string.Empty;
            var answer = string.IsNullOrWhiteSpace(raw) ? "(sans réponse)" : raw;

            // Comparaison au contact existant si cette question est mappée à un champ contact.
            if (existing != null && qidToField != null &&
                qidToField.TryGetValue(q.QuestionId, out var field))
            {
                var (current, newValue, differs) = Compare(field, raw, existing);
                if (differs)
                    return new QAItem(q.Title, answer, true, field, newValue,
                        $"Actuel : {(string.IsNullOrWhiteSpace(current) ? "(vide)" : current)}");
            }
            return new QAItem(q.Title, answer, false, null, null, string.Empty);
        }).ToList();

        List.ItemsSource = _items;

        UpdateBtn.Visibility = existing != null && services != null && _items.Any(i => i.IsDifferent)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Compare la réponse au champ correspondant du contact. Renvoie (valeur actuelle, nouvelle valeur, diffère).</summary>
    private static (string current, string newValue, bool differs) Compare(string field, string answer, Adherent a)
    {
        answer = answer?.Trim() ?? string.Empty;
        switch (field)
        {
            case "nom":
                return (a.Nom, answer, answer.Length > 0 &&
                    !string.Equals(answer, a.Nom, StringComparison.CurrentCultureIgnoreCase));
            case "prenom":
                return (a.Prenom, answer, answer.Length > 0 &&
                    !string.Equals(answer, a.Prenom, StringComparison.CurrentCultureIgnoreCase));
            case "tel":
                var formatted = PhoneFormatter.Format(answer);
                return (a.Telephone, formatted, Digits(answer).Length > 0 &&
                    Digits(formatted) != Digits(a.Telephone));
            case "email":
                var email = EmailValidator.IsValid(answer) || !EmailValidator.IsValidOrFixable(answer)
                    ? answer : EmailValidator.Suggest(answer);
                return (a.Email, email, email.Length > 0 &&
                    !string.Equals(email, a.Email, StringComparison.OrdinalIgnoreCase));
            default:
                return (string.Empty, answer, false);
        }
    }

    private static string Digits(string? s) => new((s ?? string.Empty).Where(char.IsDigit).ToArray());

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        var diffs = _items.Where(i => i.IsDifferent && i.Field != null).ToList();
        if (await ApplyDiffsAsync(diffs))
            Close();
    }

    private async void UpdateField_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not QAItem item ||
            !item.IsDifferent || item.Field == null)
            return;

        if (await ApplyDiffsAsync(new[] { item }))
            UpdateBtn.Visibility = _items.Any(i => i.IsDifferent) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Applique au contact les champs indiqués et pousse la modif vers Google. Renvoie vrai si réussi.</summary>
    private async Task<bool> ApplyDiffsAsync(IReadOnlyList<QAItem> diffs)
    {
        if (_existing == null || _services == null || diffs.Count == 0)
            return false;

        var old = _existing.Clone();
        foreach (var it in diffs)
        {
            switch (it.Field)
            {
                case "nom": _existing.Nom = it.NewValue ?? _existing.Nom; break;
                case "prenom": _existing.Prenom = it.NewValue ?? _existing.Prenom; break;
                case "tel": _existing.Telephone = it.NewValue ?? _existing.Telephone; break;
                case "email": _existing.Email = it.NewValue ?? _existing.Email; break;
            }
        }

        var result = await ProgressRunner.RunBusyAsync(this, "Mise à jour du contact…", async () =>
        {
            _existing.GoogleResourceName = await _services.Contacts.EnsureContactResourceAsync(_existing);
            await _services.Contacts.UpdateContactAsync(_existing.GoogleResourceName, _existing);
        });

        if (result.Failed > 0)
        {
            // Restaure les valeurs en cas d'échec de la synchro Google.
            _existing.CopyFrom(old);
            MessageBox.Show(this, result.LastError, "Mise à jour",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        _services.LogContactActivity(ActivityAction.Modification, _existing, "Mise à jour depuis préinscription",
            $"{old.Prenom} {old.Nom} · {old.Telephone} · {old.Email}".Trim(),
            $"{_existing.Prenom} {_existing.Nom} · {_existing.Telephone} · {_existing.Email}".Trim());
        _services.SaveAdherents();

        ContactUpdated = true;
        foreach (var it in diffs)
            it.IsDifferent = false;
        return true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed class QAItem : INotifyPropertyChanged
    {
        public string Question { get; }
        public string Answer { get; }
        public string? Field { get; }
        public string? NewValue { get; }
        public string CurrentText { get; }

        private bool _isDifferent;
        public bool IsDifferent
        {
            get => _isDifferent;
            set { if (_isDifferent != value) { _isDifferent = value; OnPropertyChanged(nameof(IsDifferent)); } }
        }

        public QAItem(string question, string answer, bool isDifferent, string? field, string? newValue, string currentText)
        {
            Question = question;
            Answer = answer;
            _isDifferent = isDifferent;
            Field = field;
            NewValue = newValue;
            CurrentText = currentText;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
