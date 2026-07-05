using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using BadmintonClub.Helpers;
using BadmintonClub.Models;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

public partial class SheetsView : UserControl, IActivableView
{
    private readonly AppServices _services;
    private readonly ObservableCollection<SheetRecord> _sheets = new();
    private ICollectionView _view = null!;

    public SheetsView(AppServices services)
    {
        InitializeComponent();
        _services = services;

        _view = CollectionViewSource.GetDefaultView(_sheets);
        _view.Filter = FilterSheet;
        Grid.ItemsSource = _view;

        LoadFromRepository();
    }

    public void OnActivated() => LoadFromRepository();

    private void LoadFromRepository()
    {
        foreach (var s in _sheets)
            s.PropertyChanged -= Sheet_PropertyChanged;
        _sheets.Clear();

        foreach (var s in _services.SheetRepository.Load().OrderByDescending(s => s.DateCreation))
        {
            s.PropertyChanged += Sheet_PropertyChanged;
            _sheets.Add(s);
        }

        UpdateCount();
        UpdateBulkBar();
    }

    private void UpdateCount()
    {
        var total = _sheets.Count;
        var shown = _view?.Cast<object>().Count() ?? total;
        CountText.Text = shown == total
            ? $"{total} classeur(s)"
            : $"{shown} sur {total} classeur(s)";

        if (shown > 0)
            EmptyResults.Visibility = Visibility.Collapsed;
        else
        {
            EmptyResults.Text = total == 0
                ? "Aucun classeur pour le moment."
                : "Aucun résultat pour cette recherche.";
            EmptyResults.Visibility = Visibility.Visible;
        }
    }

    // ---- Filtres (nom + période) -----------------------------------------

    private bool FilterSheet(object obj)
    {
        if (obj is not SheetRecord s)
            return false;

        var term = SearchBox?.Text?.Trim();
        if (!string.IsNullOrEmpty(term) &&
            !(s.Nom?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
            return false;

        if (FromDate?.SelectedDate is DateTime from && s.DateCreation.Date < from.Date)
            return false;
        if (ToDate?.SelectedDate is DateTime to && s.DateCreation.Date > to.Date)
            return false;

        return true;
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) { _view?.Refresh(); UpdateCount(); }
    private void Date_Changed(object sender, SelectionChangedEventArgs e) { _view?.Refresh(); UpdateCount(); }

    // ---- Sélection --------------------------------------------------------

    private void Sheet_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SheetRecord.IsSelected))
            UpdateBulkBar();
    }

    private void UpdateBulkBar()
    {
        var count = _sheets.Count(s => s.IsSelected);
        if (count > 0)
        {
            BulkCountText.Text = $"{count} classeur(s) sélectionné(s)";
            BulkDeleteButton.Content = $"🗑 Supprimer ({count})";
            Animations.SlideDownIn(BulkBar);
        }
        else
        {
            Animations.FadeOutCollapse(BulkBar);
        }

        SelectAllBox.IsChecked = count == 0 ? false : count == _sheets.Count ? true : null;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        var check = SelectAllBox.IsChecked == true;
        foreach (var s in _sheets)
            s.IsSelected = check;
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var s in _sheets)
            s.IsSelected = false;
    }

    // ---- Actions par ligne ------------------------------------------------

    private void Grid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Grid.SelectedItem is SheetRecord s)
            BrowserService.Open(s.Url);
    }

    private void Ouvrir_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SheetRecord s)
            BrowserService.Open(s.Url);
    }

    private void Copier_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SheetRecord s)
            return;
        try { Clipboard.SetText(s.Url); } catch { /* non bloquant */ }
    }

    private void Options_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SheetRecord s)
            return;

        var window = new SheetOptionsWindow(_services.Sheets, s) { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    private async void Supprimer_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SheetRecord s)
            return;

        if (!ConfirmWindow.Ask(Window.GetWindow(this), "Supprimer le classeur",
                $"Supprimer définitivement « {s.Nom} » ?\nIl sera supprimé de Google Drive et de cette liste."))
            return;

        await DeleteSheetsAsync(new[] { s });
    }

    private async void BulkDelete_Click(object sender, RoutedEventArgs e)
    {
        var selected = _sheets.Where(s => s.IsSelected).ToList();
        if (selected.Count == 0)
            return;

        if (!ConfirmWindow.Ask(Window.GetWindow(this), "Supprimer les classeurs",
                $"Supprimer définitivement les {selected.Count} classeur(s) sélectionné(s) ?\nIls seront supprimés de Google Drive et de cette liste."))
            return;

        await DeleteSheetsAsync(selected);
    }

    private async Task DeleteSheetsAsync(IReadOnlyList<SheetRecord> sheets)
    {
        var owner = Window.GetWindow(this)!;
        var result = await ProgressRunner.RunAsync(owner, "Suppression des classeurs…", sheets,
            async s =>
            {
                await _services.Sheets.DeleteSpreadsheetAsync(s.SpreadsheetId);
                s.PropertyChanged -= Sheet_PropertyChanged;
                _sheets.Remove(s);
            });

        _services.SheetRepository.Save(_sheets);
        _view.Refresh();
        UpdateCount();
        UpdateBulkBar();

        if (result.Failed > 0)
            MessageBox.Show(owner,
                $"{result.Ok} classeur(s) supprimé(s), {result.Failed} en erreur.\n\n{result.LastError}",
                "Suppression", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ---- Créer ------------------------------------------------------------

    private async void Creer_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this)!;

        var defaultName = $"Événements - Club Badminton ({DateTime.Now:yyyy-MM-dd})";
        var dialog = new CreateSheetWindow(defaultName) { Owner = owner };
        if (dialog.ShowDialog() != true)
            return;

        var options = dialog.Options;
        options.ShareWithEmail = _services.Settings.ClubEmail;

        CreatedSheet? created = null;
        var result = await ProgressRunner.RunBusyAsync(owner, "Création du classeur…", async () =>
        {
            created = await _services.Sheets.CreateSharedSpreadsheetAsync(options);
        });

        if (result.Failed > 0)
        {
            MessageBox.Show(owner, result.LastError, "Création du Google Sheet",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (created == null)
            return;

        _services.Settings.GoogleSheetUrl = created.Url;
        _services.SettingsService.Save(_services.Settings);

        var record = new SheetRecord
        {
            SpreadsheetId = created.SpreadsheetId,
            Nom = options.Title,
            Url = created.Url
        };
        _services.SheetRepository.Add(record);
        record.PropertyChanged += Sheet_PropertyChanged;
        _sheets.Insert(0, record);
        _view.Refresh();
        UpdateCount();

        try { Clipboard.SetText(created.Url); } catch { /* non bloquant */ }
        BrowserService.Open(created.Url);

        MessageBox.Show(owner,
            $"Classeur « {options.Title} » créé.\n\nLien (copié dans le presse-papiers) :\n{created.Url}",
            "Google Sheet créé", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>Réinitialise les filtres (changement de compte).</summary>
    public void ResetView()
    {
        SearchBox.Clear();
        FromDate.SelectedDate = null;
        ToDate.SelectedDate = null;
        _view?.Refresh();
    }

    /// <summary>
    /// Synchronisation automatique (au lancement) : récupère les Sheets depuis Google Drive.
    /// Silencieuse : en cas d'erreur (hors ligne, non autorisé), on garde la liste locale.
    /// </summary>
    public async Task AutoSyncAsync()
    {
        try
        {
            var online = await _services.Sheets.ListSpreadsheetsAsync();
            _services.SheetRepository.Save(online);
            LoadFromRepository();
        }
        catch (GoogleSyncException)
        {
            // Silencieux : l'appli reste utilisable hors ligne avec la liste locale.
        }
    }
}
