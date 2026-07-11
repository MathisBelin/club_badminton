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

        // On compare uniquement les libellés utilisateur (les groupes système ne sont pas listés).
        var optionTags = _options.Select(o => (string)o.Tag!).ToHashSet(StringComparer.Ordinal);
        var initialUser = _initial.Where(optionTags.Contains).ToHashSet(StringComparer.Ordinal);

        if (current.SetEquals(initialUser))
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

        // Un seul appel : fixe l'ensemble des libellés voulus (évite la perte du dernier retrait).
        var result = await ProgressRunner.RunBusyAsync(this, "Mise à jour des libellés…",
            () => _services.Contacts.SetContactMembershipsAsync(resource, current));

        if (result.Failed > 0)
        {
            MessageBox.Show(this,
                $"{result.LastError}", "Libellés du contact", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            // Historique : associations/dissociations effectuées (instantané du contact figé).
            var names = _options.ToDictionary(o => (string)o.Tag!, o => o.Text, StringComparer.Ordinal);

            foreach (var g in current.Except(initialUser))
                _services.LogContactActivity(Models.ActivityAction.Association, _adherent, names.GetValueOrDefault(g, g));
            foreach (var g in initialUser.Except(current))
                _services.LogContactActivity(Models.ActivityAction.Dissociation, _adherent, names.GetValueOrDefault(g, g));
        }

        DialogResult = true;
    }

    private void Annuler_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
