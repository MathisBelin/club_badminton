using System.Windows;
using BadmintonClub.Models;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

/// <summary>
/// Modale générique : sélection multiple de libellés (select2) + bouton de confirmation.
/// Sert à gérer les libellés d'un contact, ou à associer/dissocier une sélection de personnes.
/// </summary>
public partial class PickLabelsWindow : Window
{
    private readonly AppServices _services;
    private readonly HashSet<string> _preselected;

    /// <summary>Ressources des libellés cochés à la validation.</summary>
    public List<string> SelectedResources { get; private set; } = new();

    public PickLabelsWindow(AppServices services, string title, string hint, string confirmText,
        IEnumerable<string>? preselected = null)
    {
        InitializeComponent();
        _services = services;
        _preselected = new HashSet<string>(preselected ?? Enumerable.Empty<string>(), StringComparer.Ordinal);

        Title = title;
        TitleText.Text = title;
        HintText.Text = hint;
        ConfirmBtn.Content = confirmText;

        LabelsSelect.Placeholder = "Choisir des libellés";
        LabelsSelect.SetEmptyText("Aucun libellé disponible.");

        Loaded += async (_, _) => await LoadLabelsAsync();
    }

    private async Task LoadLabelsAsync()
    {
        try
        {
            var labels = await _services.GetLabelsAsync();
            LabelsSelect.SetOptions(labels.Select(l => new CheckOption
            {
                Text = l.Nom,
                Tag = l.ResourceName,
                IsSelected = _preselected.Contains(l.ResourceName)
            }));
        }
        catch (GoogleSyncException)
        {
            LabelsSelect.SetEmptyText("Libellés indisponibles (hors ligne ?).");
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        SelectedResources = LabelsSelect.SelectedTags.OfType<string>().ToList();
        DialogResult = true;
    }

    private void Annuler_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
