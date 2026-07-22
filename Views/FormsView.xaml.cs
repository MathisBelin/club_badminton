using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using BadmintonClub.Helpers;
using BadmintonClub.Models;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

public partial class FormsView : UserControl, IActivableView
{
    private readonly AppServices _services;
    private readonly ObservableCollection<FormRecord> _forms = new();
    private ICollectionView _view = null!;

    public FormsView(AppServices services)
    {
        InitializeComponent();
        _services = services;

        _view = CollectionViewSource.GetDefaultView(_forms);
        _view.Filter = FilterForm;
        Grid.ItemsSource = _view;

        LoadFromRepository();
    }

    public void OnActivated() => LoadFromRepository();

    private void LoadFromRepository()
    {
        foreach (var f in _forms)
            f.PropertyChanged -= Form_PropertyChanged;
        _forms.Clear();

        foreach (var f in _services.FormRepository.Load().OrderByDescending(f => f.DateCreation))
        {
            f.PropertyChanged += Form_PropertyChanged;
            _forms.Add(f);
        }

        UpdateCount();
        UpdateBulkBar();
    }

    private void UpdateCount()
    {
        var total = _forms.Count;
        var shown = _view?.Cast<object>().Count() ?? total;
        CountText.Text = shown == total
            ? $"{total} formulaire(s)"
            : $"{shown} sur {total} formulaire(s)";

        if (shown > 0)
            EmptyResults.Visibility = Visibility.Collapsed;
        else
        {
            EmptyResults.Text = total == 0
                ? "Aucun formulaire pour le moment."
                : "Aucun résultat pour cette recherche.";
            EmptyResults.Visibility = Visibility.Visible;
        }
    }

    // ---- Filtres (nom + période) -----------------------------------------

    private bool FilterForm(object obj)
    {
        if (obj is not FormRecord f)
            return false;

        var term = SearchBox?.Text?.Trim();
        if (!string.IsNullOrEmpty(term) &&
            !(f.Nom?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
            return false;

        if (FromDate?.SelectedDate is DateTime from && f.DateCreation.Date < from.Date)
            return false;
        if (ToDate?.SelectedDate is DateTime to && f.DateCreation.Date > to.Date)
            return false;

        return true;
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) { _view?.Refresh(); UpdateCount(); }
    private void Date_Changed(object sender, SelectionChangedEventArgs e) { _view?.Refresh(); UpdateCount(); }

    private void ClearDates_Click(object sender, RoutedEventArgs e)
    {
        FromDate.SelectedDate = null;
        ToDate.SelectedDate = null;
    }

    // ---- Sélection --------------------------------------------------------

    private void Form_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FormRecord.IsSelected))
            UpdateBulkBar();
    }

    private void UpdateBulkBar()
    {
        var count = _forms.Count(f => f.IsSelected);
        if (count > 0)
        {
            BulkCountText.Text = $"{count} formulaire(s) sélectionné(s)";
            BulkDeleteButton.Content = $"🗑 Supprimer ({count})";
            Animations.SlideDownIn(BulkBar);
        }
        else
        {
            Animations.FadeOutCollapse(BulkBar);
        }

        SelectAllBox.IsChecked = count == 0 ? false : count == _forms.Count ? true : null;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        var check = SelectAllBox.IsChecked == true;
        foreach (var f in _forms)
            f.IsSelected = check;
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var f in _forms)
            f.IsSelected = false;
    }

    // ---- Actions par ligne ------------------------------------------------

    /// <summary>Demande d'ouvrir la page des réponses (page Préinscriptions) pour un formulaire.</summary>
    public event Action<FormRecord>? OpenResponsesRequested;

    private void Grid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Grid.CurrentItem is FormRecord f)
            OpenResponsesRequested?.Invoke(f);
    }

    private void Reponses_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FormRecord f)
            OpenResponsesRequested?.Invoke(f);
    }

    private void Link_Click(object sender, RoutedEventArgs e)
    {
        // Un Hyperlink est un FrameworkContentElement (pas un FrameworkElement) : caster au bon type.
        if ((sender as System.Windows.Documents.Hyperlink)?.DataContext is FormRecord f)
            BrowserService.Open(f.EditLink);
    }

    private void ShowReminder_Click(object sender, RoutedEventArgs e)
        => new FormSettingsReminderWindow { Owner = Window.GetWindow(this) }.ShowDialog();

    private void Config_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FormRecord f)
            OpenConfig(f);
    }

    private async void OpenConfig(FormRecord f)
    {
        try { await _services.GetLabelsAsync(); } catch (GoogleSyncException) { /* cache éventuel */ }

        var win = new FormConfigWindow(_services, f) { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() == true)
        {
            _services.FormRepository.Save(_forms);
            _view.Refresh(); // nom / libellé mis à jour
            UpdateCount();
        }
    }

    private async void Supprimer_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not FormRecord f)
            return;

        if (!ConfirmWindow.Ask(Window.GetWindow(this), "Supprimer le formulaire",
                $"Supprimer définitivement « {f.Nom} » ?\nIl sera supprimé de Google Drive et de cette liste."))
            return;

        await DeleteFormsAsync(new[] { f });
    }

    private async void BulkDelete_Click(object sender, RoutedEventArgs e)
    {
        var selected = _forms.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0)
            return;

        if (!ConfirmWindow.Ask(Window.GetWindow(this), "Supprimer les formulaires",
                $"Supprimer définitivement les {selected.Count} formulaire(s) sélectionné(s) ?\nIls seront supprimés de Google Drive et de cette liste."))
            return;

        await DeleteFormsAsync(selected);
    }

    private async Task DeleteFormsAsync(IReadOnlyList<FormRecord> forms)
    {
        var owner = Window.GetWindow(this)!;
        var result = await ProgressRunner.RunAsync(owner, "Suppression des formulaires…", forms,
            async f =>
            {
                await _services.Forms.DeleteFormAsync(f.FormId);
                f.PropertyChanged -= Form_PropertyChanged;
                _forms.Remove(f);
            });

        _services.FormRepository.Save(_forms);
        _view.Refresh();
        UpdateCount();
        UpdateBulkBar();

        if (result.Failed > 0)
            MessageBox.Show(owner,
                $"{result.Ok} formulaire(s) supprimé(s), {result.Failed} en erreur.\n\n{result.LastError}",
                "Suppression", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ---- Créer ------------------------------------------------------------

    private async void Creer_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this)!;

        var models = new FormTemplateRepository().List();
        var defaultName = $"Formulaire - Club Badminton ({DateTime.Now:yyyy-MM-dd})";
        var dialog = new CreateFormWindow(defaultName, models) { Owner = owner };
        if (dialog.ShowDialog() != true)
            return;

        FormRecord? created = null;
        var result = await ProgressRunner.RunBusyAsync(owner, "Création du formulaire…", async () =>
        {
            created = dialog.SelectedModel != null
                ? await _services.Forms.CreateFormFromTemplateAsync(dialog.SelectedModel, dialog.FormName)
                : await _services.Forms.CreateBlankFormAsync(dialog.FormName);
        });

        if (result.Failed > 0)
        {
            MessageBox.Show(owner, result.LastError, "Créer un Google Form",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (created == null)
            return;

        _services.FormRepository.Add(created);
        created.PropertyChanged += Form_PropertyChanged;
        _forms.Insert(0, created);
        _view.Refresh();
        UpdateCount();

        try { Clipboard.SetText(created.EditLink); } catch { /* non bloquant */ }
        BrowserService.Open(created.EditLink);
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
    /// Synchronisation automatique (au lancement) : récupère les Forms depuis Google Drive
    /// en préservant les indicateurs locaux (modèle, lien de réponse). Silencieuse.
    /// </summary>
    public async Task AutoSyncAsync()
    {
        try
        {
            await _services.SyncFormsAsync();
            LoadFromRepository();
        }
        catch (GoogleSyncException)
        {
            // Silencieux : l'appli reste utilisable hors ligne avec la liste locale.
        }
    }
}
