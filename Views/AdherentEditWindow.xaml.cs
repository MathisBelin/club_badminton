using System.Collections.ObjectModel;
using System.Windows;
using BadmintonClub.Models;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

public partial class AdherentEditWindow : Window
{
    private readonly GoogleContactsService? _contacts;

    /// <summary>Champs e-mails secondaires (un par ligne, ajoutables/supprimables comme Google Contacts).</summary>
    private readonly ObservableCollection<EmailField> _secondaryEmails = new();

    /// <summary>L'adhérent en cours d'édition (un clone, validé seulement à l'enregistrement).</summary>
    public Adherent Adherent { get; }

    /// <summary>Ressources des libellés choisis à la création (vide en modification).</summary>
    public IReadOnlyList<string> SelectedLabelResourceNames { get; private set; } = new List<string>();

    /// <param name="adherent">null pour une création, sinon l'adhérent à modifier.</param>
    /// <param name="contacts">Service Contacts : si fourni en création, propose le choix des libellés.</param>
    public AdherentEditWindow(Adherent? adherent = null, GoogleContactsService? contacts = null)
    {
        InitializeComponent();
        _contacts = contacts;

        if (adherent == null)
        {
            Adherent = new Adherent();
            Title = "Ajouter un adhérent";
        }
        else
        {
            Adherent = adherent.Clone();
            Title = "Modifier un adhérent";
        }

        NomBox.Text = Adherent.Nom;
        PrenomBox.Text = Adherent.Prenom;
        TelephoneBox.Text = Adherent.Telephone;
        EmailBox.Text = Adherent.Email;

        // Un champ par e-mail secondaire existant, plus un champ vide s'il n'y en a aucun.
        foreach (var se in Adherent.SecondaryEmails)
            _secondaryEmails.Add(new EmailField(se));
        if (_secondaryEmails.Count == 0)
            _secondaryEmails.Add(new EmailField());
        SecondaryEmailsList.ItemsSource = _secondaryEmails;
        Loaded += (_, _) => UpdateAddButtonState();

        // Choix des libellés seulement à la création et si le service est disponible.
        if (adherent == null && _contacts != null)
        {
            LabelsSection.Visibility = Visibility.Visible;
            LabelsSelect.Placeholder = "Choisir des libellés";
            LabelsSelect.SetEmptyText("Aucun libellé disponible.");
            Loaded += async (_, _) => await LoadLabelsAsync();
        }

        Loaded += (_, _) => NomBox.Focus();
    }

    private async Task LoadLabelsAsync()
    {
        try
        {
            var labels = await _contacts!.ListLabelsAsync();
            LabelsSelect.SetOptions(labels.Select(l => new CheckOption
            {
                Text = l.Nom,
                Tag = l.ResourceName
            }));
        }
        catch (GoogleSyncException)
        {
            // Silencieux : la création reste possible sans association.
            LabelsSelect.SetEmptyText("Libellés indisponibles (hors ligne ?).");
        }
    }

    private void Enregistrer_Click(object sender, RoutedEventArgs e)
    {
        var prenom = PrenomBox.Text.Trim();
        var nom = NomBox.Text.Trim();
        var email = EmailBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(prenom) || string.IsNullOrWhiteSpace(nom))
        {
            MessageBox.Show(this, "Le prénom et le nom sont obligatoires.",
                "Champs manquants", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!string.IsNullOrWhiteSpace(email) && !IsValidEmail(email))
        {
            MessageBox.Show(this, "L'adresse e-mail n'est pas valide.",
                "E-mail invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Mails secondaires : un champ par adresse (les champs vides sont ignorés).
        var secondaries = _secondaryEmails
            .Select(f => f.Value.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var invalid = secondaries.Where(s => !IsValidEmail(s)).ToList();
        if (invalid.Count > 0)
        {
            MessageBox.Show(this, "Adresse(s) secondaire(s) invalide(s) :\n• " + string.Join("\n• ", invalid),
                "Mails secondaires", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Adherent.Nom = nom;
        Adherent.Prenom = prenom;
        Adherent.Telephone = Helpers.PhoneFormatter.Format(TelephoneBox.Text);
        Adherent.Email = email;
        // L'e-mail principal ne doit pas figurer aussi dans les secondaires.
        Adherent.SecondaryEmails = secondaries
            .Where(s => !string.Equals(s, email, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (LabelsSection.Visibility == Visibility.Visible)
            SelectedLabelResourceNames = LabelsSelect.SelectedTags.OfType<string>().ToList();

        DialogResult = true;
    }

    private void Annuler_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>Ajoute un champ e-mail secondaire vide (seulement si les précédents sont valides).</summary>
    private void AddEmail_Click(object sender, RoutedEventArgs e)
    {
        if (!AllSecondaryValid())
            return;
        _secondaryEmails.Add(new EmailField());
        UpdateAddButtonState();
    }

    /// <summary>Retire le champ e-mail secondaire correspondant (garde toujours au moins un champ).</summary>
    private void RemoveEmail_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not EmailField field)
            return;
        _secondaryEmails.Remove(field);
        if (_secondaryEmails.Count == 0)
            _secondaryEmails.Add(new EmailField());
        UpdateAddButtonState();
    }

    private void SecondaryEmail_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => UpdateAddButtonState();

    /// <summary>Vrai si tous les champs e-mails secondaires sont renseignés et au bon format.</summary>
    private bool AllSecondaryValid()
        => _secondaryEmails.All(f => !string.IsNullOrWhiteSpace(f.Value) && IsValidEmail(f.Value.Trim()));

    /// <summary>On ne peut ajouter une adresse que si toutes les précédentes sont valides.</summary>
    private void UpdateAddButtonState()
    {
        if (AddEmailBtn != null)
            AddEmailBtn.IsEnabled = AllSecondaryValid();
    }

    /// <summary>Un champ e-mail secondaire éditable (lié en deux sens à son TextBox).</summary>
    private sealed class EmailField
    {
        public EmailField(string value = "") => Value = value;
        public string Value { get; set; }
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }
}
