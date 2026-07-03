using System.Collections.ObjectModel;
using System.Windows;
using BadmintonClub.Helpers;
using BadmintonClub.Models;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

public partial class ManageLabelsWindow : Window
{
    private readonly AppServices _services;
    private readonly Adherent _adherent;
    private readonly ObservableCollection<CheckOption> _options = new();
    private HashSet<string> _initial = new(StringComparer.Ordinal);

    public ManageLabelsWindow(AppServices services, Adherent adherent)
    {
        InitializeComponent();
        _services = services;
        _adherent = adherent;

        var name = $"{adherent.Prenom} {adherent.Nom}".Trim();
        TitleText.Text = $"Libellés de {(string.IsNullOrWhiteSpace(name) ? adherent.Email : name)}";

        LabelsList.ItemsSource = _options;
        LabelsList.Visibility = Visibility.Collapsed;

        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            IsEnabled = false;

            // Libellés depuis le cache (pas d'appel API si déjà chargés).
            var labels = await _services.GetLabelsAsync();

            // Appartenances du contact (appel léger via sa ressource), s'il est lié à Google.
            _initial = string.IsNullOrEmpty(_adherent.GoogleResourceName)
                ? new HashSet<string>(StringComparer.Ordinal)
                : await _services.Contacts.GetContactMembershipsAsync(_adherent.GoogleResourceName);

            _options.Clear();
            foreach (var label in labels)
                _options.Add(new CheckOption
                {
                    Text = label.Nom,
                    Tag = label.ResourceName,
                    IsSelected = _initial.Contains(label.ResourceName)
                });

            LoadingText.Visibility = Visibility.Collapsed;
            LabelsList.Visibility = Visibility.Visible;
        }
        catch (GoogleSyncException ex)
        {
            MessageBox.Show(this, ex.Message, "Libellés du contact",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            DialogResult = false;
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void Tout_Click(object sender, RoutedEventArgs e)
    {
        foreach (var o in _options) o.IsSelected = true;
    }

    private void Aucun_Click(object sender, RoutedEventArgs e)
    {
        foreach (var o in _options) o.IsSelected = false;
    }

    private async void Valider_Click(object sender, RoutedEventArgs e)
    {
        var current = _options.Where(o => o.IsSelected)
            .Select(o => (string)o.Tag!)
            .ToHashSet(StringComparer.Ordinal);

        var toAdd = current.Except(_initial).ToList();
        var toRemove = _initial.Except(current).ToList();

        if (toAdd.Count == 0 && toRemove.Count == 0)
        {
            DialogResult = true;
            return;
        }

        // On garantit le lien Google du contact avant de modifier ses libellés.
        string resource;
        try
        {
            resource = string.IsNullOrEmpty(_adherent.GoogleResourceName)
                ? await _services.Contacts.EnsureContactResourceAsync(_adherent)
                : _adherent.GoogleResourceName;
            _adherent.GoogleResourceName = resource;
            _services.SaveAdherents();
        }
        catch (GoogleSyncException ex)
        {
            MessageBox.Show(this, ex.Message, "Libellés du contact",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var ops = toAdd.Select(g => (Group: g, Add: true))
            .Concat(toRemove.Select(g => (Group: g, Add: false)))
            .ToList();

        var result = await ProgressRunner.RunAsync(this, "Mise à jour des libellés…", ops,
            op => _services.Contacts.SetMembershipAsync(resource, op.Group, op.Add));

        if (result.Failed > 0)
            MessageBox.Show(this,
                $"{result.Ok} modification(s) appliquée(s), {result.Failed} en erreur.\n\n{result.LastError}",
                "Libellés du contact", MessageBoxButton.OK, MessageBoxImage.Warning);

        DialogResult = true;
    }

    private void Annuler_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
