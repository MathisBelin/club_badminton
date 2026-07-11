using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using BadmintonClub.Models;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

public partial class AutoSyncView : UserControl, IActivableView
{
    private readonly AppServices _services;
    private readonly ICollectionView _view;

    public AutoSyncView(AppServices services)
    {
        InitializeComponent();
        _services = services;

        _view = CollectionViewSource.GetDefaultView(_services.AutoSyncs);
        _view.Filter = FilterSync;
        Grid.ItemsSource = _view;

        _services.AutoSyncs.CollectionChanged += Syncs_CollectionChanged;
        UpdateCount();
    }

    public void OnActivated() => UpdateCount();

    private void Syncs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateCount();

    private bool FilterSync(object obj)
    {
        var term = SearchBox?.Text?.Trim();
        if (string.IsNullOrEmpty(term))
            return true;
        return obj is AutoSyncConfig c && (c.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        _view?.Refresh();
        UpdateCount();
    }

    private void UpdateCount()
    {
        var total = _services.AutoSyncs.Count;
        var shown = _view?.Cast<object>().Count() ?? total;
        CountText.Text = total == 0
            ? "Aucune synchronisation configurée."
            : shown == total ? $"{total} synchronisation(s)" : $"{shown} sur {total} synchronisation(s)";
        EmptyResults.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyResults.Text = total == 0
            ? "Aucune synchronisation. Cliquez sur « ➕ Ajouter »."
            : "Aucun résultat pour cette recherche.";
    }

    private void Ajouter_Click(object sender, RoutedEventArgs e)
    {
        var win = new AutoSyncEditWindow(_services) { Owner = Window.GetWindow(this) };
        win.ShowDialog();
        UpdateCount();
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AutoSyncConfig c)
            OpenEdit(c);
    }

    private void Grid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Grid.CurrentItem is AutoSyncConfig c)
            OpenEdit(c);
    }

    private void OpenEdit(AutoSyncConfig c)
    {
        if (c.IsImporting)
        {
            MessageBox.Show(Window.GetWindow(this),
                "Cette synchro est en cours d'exécution. Attendez qu'elle se termine pour la modifier.",
                "Synchronisation", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var win = new AutoSyncEditWindow(_services, c) { Owner = Window.GetWindow(this) };
        win.ShowDialog();
        UpdateCount();
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AutoSyncConfig c)
            return;
        if (c.Enabled)
        {
            _services.StopSync(c);
            return;
        }

        if (!c.IsComplete)
        {
            MessageBox.Show(Window.GetWindow(this),
                "Cette synchro est incomplète (brouillon). Renseignez le nom, le lien du Sheet, " +
                "le libellé cible et la colonne e-mail pour pouvoir la lancer.",
                "Synchronisation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _services.StartSync(c);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AutoSyncConfig c)
            return;
        if (ConfirmWindow.Ask(Window.GetWindow(this), "Supprimer la synchro",
                $"Supprimer la synchronisation « {c.Name} » ?\nLes contacts et le libellé ne sont pas supprimés.",
                confirmText: "Supprimer", icon: "🗑"))
        {
            _services.DeleteSync(c);
            UpdateCount();
        }
    }

    private void OpenLink_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Hyperlink)?.DataContext is AutoSyncConfig c && !string.IsNullOrWhiteSpace(c.SheetUrl))
            BrowserService.Open(c.SheetUrl);
    }

    /// <summary>Demande d'ouvrir la page « Inscriptions non finalisées » pré-filtrée sur un libellé.</summary>
    public event Action<string?>? OpenPendingRequested;

    private void Warning_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AutoSyncConfig c)
            return;

        var win = new SyncWarningWindow(_services, c) { Owner = Window.GetWindow(this) };
        win.ShowDialog();
        if (win.GoToPending)
            OpenPendingRequested?.Invoke(c.LabelResourceName);
    }

    private void Trace_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AutoSyncConfig c)
            return;
        new SyncTraceWindow(c) { Owner = Window.GetWindow(this) }.ShowDialog();
    }
}
