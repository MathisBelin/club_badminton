using System.Windows;
using BadmintonClub.Models;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

public partial class AdherentEditWindow : Window
{
    private readonly GoogleContactsService? _contacts;

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

        PrenomBox.Text = Adherent.Prenom;
        NomBox.Text = Adherent.Nom;
        TelephoneBox.Text = Adherent.Telephone;
        EmailBox.Text = Adherent.Email;

        // Choix des libellés seulement à la création et si le service est disponible.
        if (adherent == null && _contacts != null)
        {
            LabelsSection.Visibility = Visibility.Visible;
            LabelsSelect.Placeholder = "Choisir des libellés";
            LabelsSelect.SetEmptyText("Aucun libellé disponible.");
            Loaded += async (_, _) => await LoadLabelsAsync();
        }

        Loaded += (_, _) => PrenomBox.Focus();
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

        Adherent.Prenom = prenom;
        Adherent.Nom = nom;
        Adherent.Telephone = Helpers.PhoneFormatter.Format(TelephoneBox.Text);
        Adherent.Email = email;

        if (LabelsSection.Visibility == Visibility.Visible)
            SelectedLabelResourceNames = LabelsSelect.SelectedTags.OfType<string>().ToList();

        DialogResult = true;
    }

    private void Annuler_Click(object sender, RoutedEventArgs e) => DialogResult = false;

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
