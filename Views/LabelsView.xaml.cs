using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using BadmintonClub.Helpers;
using BadmintonClub.Models;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

public partial class LabelsView : UserControl, IActivableView
{
    private readonly AppServices _services;
    private readonly ObservableCollection<LabelItem> _labels = new();
    private bool _loadedOnce;

    public LabelsView(AppServices services)
    {
        InitializeComponent();
        _services = services;
        Grid.ItemsSource = _labels;
        UpdateCount();
    }

    public void OnActivated()
    {
        // Affichage depuis le cache (pas d'appel réseau si déjà chargé).
        _ = LoadLabelsAsync(silent: true, forceRefresh: false);
    }

    /// <summary>Chargement automatique au lancement (silencieux, alimente le cache).</summary>
    public Task AutoLoadAsync() => LoadLabelsAsync(silent: true, forceRefresh: false);

    /// <summary>Vide l'affichage des libellés (changement de compte) avant rechargement.</summary>
    public void ResetView()
    {
        foreach (var l in _labels)
            l.PropertyChanged -= Label_PropertyChanged;
        _labels.Clear();
        _loadedOnce = false;
        UpdateCount();
        UpdateBulkBar();
    }

    // ---- Chargement -------------------------------------------------------

    private async Task LoadLabelsAsync(bool silent, bool forceRefresh)
    {
        try
        {
            var labels = await _services.GetLabelsAsync(forceRefresh);
            PopulateLabels(labels);
            _loadedOnce = true;
            UpdateCount();
            UpdateBulkBar();
        }
        catch (GoogleSyncException ex)
        {
            if (!silent)
                MessageBox.Show(Window.GetWindow(this), ex.Message, "Libellés Gmail",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PopulateLabels(IEnumerable<LabelItem> labels)
    {
        foreach (var l in _labels)
            l.PropertyChanged -= Label_PropertyChanged;
        _labels.Clear();
        foreach (var l in labels)
        {
            l.PropertyChanged += Label_PropertyChanged;
            _labels.Add(l);
        }
    }

    private void UpdateCount()
    {
        CountText.Text = _loadedOnce
            ? $"{_labels.Count} libellé(s)"
            : "Chargement des libellés…";

        if (_loadedOnce && _labels.Count == 0)
        {
            EmptyResults.Text = "Aucun libellé pour le moment.";
            EmptyResults.Visibility = Visibility.Visible;
        }
        else
        {
            EmptyResults.Visibility = Visibility.Collapsed;
        }
    }

    // ---- Sélection --------------------------------------------------------

    private void Label_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LabelItem.IsSelected))
            UpdateBulkBar();
    }

    private void UpdateBulkBar()
    {
        var count = _labels.Count(l => l.IsSelected);
        if (count > 0)
        {
            BulkCountText.Text = $"{count} libellé(s) sélectionné(s)";
            BulkDeleteButton.Content = $"🗑 Supprimer ({count})";
            Animations.SlideDownIn(BulkBar);
        }
        else
        {
            Animations.FadeOutCollapse(BulkBar);
        }

        SelectAllBox.IsChecked = count == 0 ? false : count == _labels.Count ? true : null;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        var check = SelectAllBox.IsChecked == true;
        foreach (var l in _labels)
            l.IsSelected = check;
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var l in _labels)
            l.IsSelected = false;
    }

    // ---- Créer / associer -------------------------------------------------

    private async void Creer_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new InputDialog("Créer un libellé", "Nom du nouveau libellé :")
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true)
            return;

        var owner = Window.GetWindow(this)!;
        var result = await ProgressRunner.RunBusyAsync(owner, "Création du libellé…", async () =>
        {
            await _services.Contacts.CreateLabelAsync(dialog.Value);
            await LoadLabelsAsync(silent: true, forceRefresh: true);
        });

        if (result.Failed > 0)
            Warn(result.LastError);
        else
            MessageBox.Show(owner, $"Libellé « {dialog.Value} » créé.", "Libellés",
                MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ---- Voir les membres (page Association) ------------------------------

    /// <summary>Demande d'ouvrir la page Association filtrée sur un libellé (resourceName).</summary>
    public event Action<string>? OpenAssociationRequested;

    private void Voir_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is LabelItem label)
            OpenAssociationRequested?.Invoke(label.ResourceName);
    }

    private void Grid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Grid.CurrentItem is LabelItem label)
            OpenAssociationRequested?.Invoke(label.ResourceName);
    }

    // ---- Renommer / supprimer --------------------------------------------

    private async void Renommer_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not LabelItem label)
            return;

        var dialog = new InputDialog("Renommer le libellé",
            "Nouveau nom du libellé :", label.Nom) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true)
            return;

        var owner = Window.GetWindow(this)!;
        var result = await ProgressRunner.RunBusyAsync(owner, "Renommage du libellé…", async () =>
        {
            await _services.Contacts.RenameLabelAsync(label.ResourceName, dialog.Value);
            await LoadLabelsAsync(silent: true, forceRefresh: true);
        });

        if (result.Failed > 0)
            Warn(result.LastError);
    }

    private async void Supprimer_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not LabelItem label)
            return;

        if (!ConfirmWindow.Ask(Window.GetWindow(this), "Supprimer le libellé",
                $"Supprimer le libellé « {label.Nom} » ?\nLes contacts eux-mêmes ne seront pas supprimés."))
            return;

        await DeleteLabelsAsync(new[] { label });
    }

    private async void BulkDelete_Click(object sender, RoutedEventArgs e)
    {
        var selected = _labels.Where(l => l.IsSelected).ToList();
        if (selected.Count == 0)
            return;

        if (!ConfirmWindow.Ask(Window.GetWindow(this), "Supprimer les libellés",
                $"Supprimer les {selected.Count} libellé(s) sélectionné(s) ?\nLes contacts eux-mêmes ne seront pas supprimés."))
            return;

        await DeleteLabelsAsync(selected);
    }

    private async Task DeleteLabelsAsync(IReadOnlyList<LabelItem> labels)
    {
        var owner = Window.GetWindow(this)!;
        var result = await ProgressRunner.RunAsync(owner, "Suppression des libellés…", labels,
            async l =>
            {
                await _services.Contacts.DeleteLabelAsync(l.ResourceName);
                l.PropertyChanged -= Label_PropertyChanged;
                _labels.Remove(l);
            });

        // Rafraîchit le cache partagé (utilisé par la modal « gérer les libellés »).
        await LoadLabelsAsync(silent: true, forceRefresh: true);
        UpdateBulkBar();

        if (result.Failed > 0)
            Warn($"{result.Ok} libellé(s) supprimé(s), {result.Failed} en erreur.\n\n{result.LastError}");
    }

    // ---- Utilitaire -------------------------------------------------------

    private void Warn(string? message)
        => MessageBox.Show(Window.GetWindow(this), message, "Libellés Gmail",
            MessageBoxButton.OK, MessageBoxImage.Warning);
}
