using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using BadmintonClub.Models;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

public partial class PendingView : UserControl, IActivableView
{
    private readonly AppServices _services;
    private readonly ICollectionView _view;

    public PendingView(AppServices services)
    {
        InitializeComponent();
        _services = services;

        _view = CollectionViewSource.GetDefaultView(_services.Pending);
        _view.Filter = FilterPending;
        Grid.ItemsSource = _view;

        LabelFilter.Placeholder = "Tous les libellés";
        LabelFilter.SelectionChanged += (_, _) => { _view.Refresh(); UpdateCount(); };

        _services.Pending.CollectionChanged += Pending_CollectionChanged;
        RefreshFilterOptions();
        UpdateCount();
    }

    public void OnActivated()
    {
        RefreshFilterOptions();
        _view.Refresh();
        UpdateCount();
    }

    /// <summary>Ouvre la page pré-filtrée sur un libellé (depuis la page Association).</summary>
    public void ShowForLabel(string? labelResourceName)
    {
        RefreshFilterOptions(labelResourceName);
        _view.Refresh();
        UpdateCount();
    }

    private void Pending_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshFilterOptions(keepSelection: true);
        _view.Refresh();
        UpdateCount();
    }

    /// <summary>Remplit le filtre avec les libellés présents dans la liste d'attente.</summary>
    private void RefreshFilterOptions(string? selectResource = null, bool keepSelection = false)
    {
        var current = keepSelection
            ? LabelFilter.SelectedTags.OfType<string>().ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        if (selectResource != null)
            current = new HashSet<string>(StringComparer.Ordinal) { selectResource };

        var labels = _services.Pending
            .GroupBy(p => p.LabelResourceName)
            .Select(g => (Resource: g.Key, Name: g.First().LabelName))
            .OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        LabelFilter.SetOptions(labels.Select(l => new CheckOption
        {
            Text = string.IsNullOrWhiteSpace(l.Name) ? l.Resource : l.Name,
            Tag = l.Resource,
            IsSelected = current.Contains(l.Resource)
        }));
    }

    private bool FilterPending(object obj)
    {
        if (obj is not PendingPerson p)
            return false;
        var selected = LabelFilter.SelectedTags.OfType<string>().ToHashSet(StringComparer.Ordinal);
        return selected.Count == 0 || selected.Contains(p.LabelResourceName);
    }

    private void ClearFilter_Click(object sender, RoutedEventArgs e) => LabelFilter.ClearSelection();

    private void UpdateCount()
    {
        var total = _services.Pending.Count;
        var shown = _view.Cast<object>().Count();
        var hasFilter = LabelFilter.SelectedTags.OfType<string>().Any();
        ClearFilterBtn.Visibility = hasFilter ? Visibility.Visible : Visibility.Collapsed;

        CountText.Text = total == 0
            ? "Aucune personne en attente. 👍"
            : shown == total ? $"{total} personne(s) en attente"
                             : $"{shown} sur {total} personne(s) en attente";

        if (shown > 0)
            EmptyResults.Visibility = Visibility.Collapsed;
        else
        {
            EmptyResults.Text = total == 0
                ? "Aucune personne en attente : tout le monde a renseigné son e-mail. 👍"
                : "Aucun résultat pour ce libellé.";
            EmptyResults.Visibility = Visibility.Visible;
        }
    }
}
