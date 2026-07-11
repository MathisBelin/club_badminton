using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using BadmintonClub.Models;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

/// <summary>
/// Historique des activités : trois tableaux (Utilisateurs / Libellés / Sheets) choisis par radio,
/// filtrés par action et par période. Lecture seule.
/// </summary>
public partial class HistoryView : UserControl, IActivableView
{
    private readonly AppServices _services;
    private readonly ICollectionView _view;
    private bool _ready; // évite les handlers déclenchés pendant InitializeComponent

    public HistoryView(AppServices services)
    {
        InitializeComponent();
        _services = services;

        _view = CollectionViewSource.GetDefaultView(_services.Activities);
        _view.Filter = FilterRow;
        Grid.ItemsSource = _view;

        _services.Activities.CollectionChanged += (_, _) => { if (_ready) { _view.Refresh(); UpdateCount(); } };
        _ready = true;
        UpdateCount();
    }

    public void OnActivated()
    {
        _view.Refresh();
        UpdateCount();
    }

    private ActivityCategory SelectedCategory =>
        RadioLabels.IsChecked == true ? ActivityCategory.Libelle
        : RadioSheets.IsChecked == true ? ActivityCategory.Sheet
        : ActivityCategory.Utilisateur;

    private bool FilterRow(object obj)
    {
        if (obj is not ActivityEntry a)
            return false;

        if (a.Category != SelectedCategory)
            return false;

        if ((ActionCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() is string act && act != "Toutes"
            && !string.Equals(a.Action.ToString(), act, StringComparison.Ordinal))
            return false;

        if (FromDate?.SelectedDate is DateTime from && a.Date.Date < from.Date)
            return false;
        if (ToDate?.SelectedDate is DateTime to && a.Date.Date > to.Date)
            return false;

        var term = TargetSearch?.Text?.Trim();
        if (!string.IsNullOrEmpty(term) &&
            (a.Target == null || !a.Target.Contains(term, StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }

    private void Category_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready)
            return;
        _view.Refresh();
        UpdateCount();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready)
            return;
        _view.Refresh();
        UpdateCount();
    }

    private void TargetSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready)
            return;
        _view.Refresh();
        UpdateCount();
    }

    private void ClearDates_Click(object sender, RoutedEventArgs e)
    {
        FromDate.SelectedDate = null;
        ToDate.SelectedDate = null; // déclenche Filter_Changed
    }

    /// <summary>Affiche les détails du contact FIGÉS au moment de l'action (survit aux modifs/suppressions).</summary>
    private void Details_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ActivityEntry entry)
            return;
        new ContactDetailsWindow(entry) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private void UpdateCount()
    {
        var shown = _view?.Cast<object>().Count() ?? 0;
        CountText.Text = shown == 0
            ? "Aucune activité pour ces filtres."
            : $"{shown} activité(s)";
        EmptyResults.Text = "Aucune activité à afficher.";
        EmptyResults.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
