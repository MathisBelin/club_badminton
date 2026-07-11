using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using BadmintonClub.Helpers;
using BadmintonClub.Models;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

public partial class PendingView : UserControl, IActivableView
{
    private readonly AppServices _services;
    private readonly ObservableCollection<PendingRow> _rows = new();
    private readonly ICollectionView _view;
    private bool _ready;

    public PendingView(AppServices services)
    {
        InitializeComponent();
        _services = services;

        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = FilterRow;
        Grid.ItemsSource = _view;

        LabelFilter.Placeholder = "Tous les libellés";
        LabelFilter.SelectionChanged += (_, _) => { _view.Refresh(); UpdateCount(); };

        _services.Pending.CollectionChanged += Pending_CollectionChanged;
        _services.LabelsChanged += OnLabelsChanged;
        _ready = true;
        RebuildRows();
    }

    /// <summary>Un renommage de libellé a été répercuté : on reconstruit (nom + filtre à jour).</summary>
    private void OnLabelsChanged() => RebuildRows(keepFilter: true);

    public void OnActivated() => RebuildRows();

    /// <summary>Ouvre la page pré-filtrée sur un libellé (depuis Association).</summary>
    public void ShowForLabel(string? labelResourceName) => RebuildRows(labelResourceName);

    private void Pending_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RebuildRows(keepFilter: true);

    /// <summary>Recompute les lignes (correspondances) et rafraîchit filtres + affichage.</summary>
    private void RebuildRows(string? selectLabel = null, bool keepFilter = false)
    {
        foreach (var r in _rows)
            r.PropertyChanged -= Row_PropertyChanged;
        _rows.Clear();

        foreach (var p in _services.Pending)
        {
            var match = PendingMatcher.Match(p, _services.Adherents);
            var row = new PendingRow(p, match.Level, match.Candidates);
            row.PropertyChanged += Row_PropertyChanged;
            _rows.Add(row);
        }

        RefreshLabelOptions(selectLabel, keepFilter);
        _view.Refresh();
        UpdateCount();
        UpdateBulkBar();
    }

    private void RefreshLabelOptions(string? selectLabel, bool keepFilter)
    {
        var current = keepFilter
            ? LabelFilter.SelectedTags.OfType<string>().ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        if (selectLabel != null)
            current = new HashSet<string>(StringComparer.Ordinal) { selectLabel };

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

    // ---- Filtres ----------------------------------------------------------

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _view.Refresh();
        UpdateCount();
    }

    private void LevelFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready)
            return;
        _view.Refresh();
        UpdateCount();
    }

    private bool FilterRow(object obj)
    {
        if (obj is not PendingRow r)
            return false;

        var labels = LabelFilter.SelectedTags.OfType<string>().ToHashSet(StringComparer.Ordinal);
        if (labels.Count > 0 && !labels.Contains(r.Person.LabelResourceName))
            return false;

        if ((LevelCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() is string lvl && lvl != "Tous")
        {
            if (!string.Equals(r.Level.ToString(), lvl, StringComparison.Ordinal))
                return false;
        }

        var term = SearchBox?.Text?.Trim();
        if (!string.IsNullOrEmpty(term))
            return Contains(r.Nom) || Contains(r.Prenom) || Contains(r.Telephone) || Contains(r.Email);

        return true;

        bool Contains(string? v) => v != null && v.Contains(term!, StringComparison.OrdinalIgnoreCase);
    }

    private List<PendingRow> Filtered() => _view.Cast<PendingRow>().ToList();

    private void UpdateCount()
    {
        var total = _rows.Count;
        var shown = Filtered().Count;

        CountText.Text = total == 0
            ? "Aucune inscription non finalisée. 👍"
            : shown == total ? $"{total} inscription(s) non finalisée(s)"
                             : $"{shown} sur {total} inscription(s) non finalisée(s)";

        if (shown > 0)
            EmptyResults.Visibility = Visibility.Collapsed;
        else
        {
            EmptyResults.Text = total == 0
                ? "Aucune inscription non finalisée : tout le monde a renseigné son e-mail. 👍"
                : "Aucun résultat pour ces filtres.";
            EmptyResults.Visibility = Visibility.Visible;
        }
    }

    // ---- Sélection / suppression -----------------------------------------

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PendingRow.IsSelected))
            UpdateBulkBar();
    }

    private void UpdateBulkBar()
    {
        var count = _rows.Count(r => r.IsSelected);
        if (count > 0)
        {
            BulkCountText.Text = $"{count} inscription(s) sélectionnée(s)";
            BulkDeleteButton.Content = $"✔ Valider ({count})";
            Animations.SlideDownIn(BulkBar);
        }
        else
        {
            Animations.FadeOutCollapse(BulkBar);
        }

        var filtered = Filtered();
        var sel = filtered.Count(r => r.IsSelected);
        SelectAllBox.IsChecked = sel == 0 ? false
            : sel == filtered.Count && filtered.Count > 0 ? true
            : null;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        var check = SelectAllBox.IsChecked == true;
        foreach (var r in Filtered())
            r.IsSelected = check;
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _rows)
            r.IsSelected = false;
    }

    /// <summary>Message de confirmation commun (valider = retirer de la liste).</summary>
    private static string ValidationMessage(int count) =>
        (count == 1
            ? "Valider cette inscription et la retirer de la liste ?\n\n"
            : $"Valider les {count} inscription(s) sélectionnée(s) et les retirer de la liste ?\n\n") +
        "⚠ Vérifiez d'abord que les informations manquantes (e-mail) ont bien été renseignées " +
        "dans le fichier Excel d'import. Sinon, la personne réapparaîtra dans cette liste à la " +
        "prochaine synchronisation ou import manuelle.";

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PendingRow r)
            return;

        if (!ConfirmWindow.Ask(Window.GetWindow(this), "Valider l'inscription",
                ValidationMessage(1), confirmText: "Valider", icon: "✔", danger: false))
            return;

        _services.Pending.Remove(r.Person); // déclenche RebuildRows via CollectionChanged
    }

    private void BulkDelete_Click(object sender, RoutedEventArgs e)
    {
        var selected = _rows.Where(r => r.IsSelected).Select(r => r.Person).ToList();
        if (selected.Count == 0)
            return;

        if (!ConfirmWindow.Ask(Window.GetWindow(this), "Valider les inscriptions",
                ValidationMessage(selected.Count), confirmText: "Valider", icon: "✔", danger: false))
            return;

        foreach (var p in selected)
            _services.Pending.Remove(p);
    }

    // ---- Correspondances (lecture seule) ---------------------------------

    private void Matches_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PendingRow r)
            return;
        var win = new PendingMatchWindow(r.Person, r.Person.LabelName, r.Candidates)
        {
            Owner = Window.GetWindow(this)
        };
        win.ShowDialog();
    }
}

/// <summary>Ligne de la table « inscriptions non finalisées » (avec niveau de correspondance et sélection).</summary>
internal sealed class PendingRow : INotifyPropertyChanged
{
    public PendingPerson Person { get; }
    public MatchLevel Level { get; }
    public List<Adherent> Candidates { get; }

    public PendingRow(PendingPerson person, MatchLevel level, List<Adherent> candidates)
    {
        Person = person;
        Level = level;
        Candidates = candidates;
    }

    public string LabelName => Person.LabelName;
    public string Nom => Person.Nom;
    public string Prenom => Person.Prenom;
    public string Telephone => Person.Telephone;
    public string Email => Person.Email;

    private bool HasEmail => !string.IsNullOrWhiteSpace(Person.Email);

    /// <summary>E-mail affiché : l'adresse (mauvais format) ou « mail non renseigné » si vide.</summary>
    public string EmailDisplay => HasEmail ? Person.Email : "mail non renseigné";

    /// <summary>Jaune si e-mail au mauvais format, rouge si e-mail manquant.</summary>
    public Brush EmailBrush => HasEmail ? WarnBrush : ErrBrush;

    /// <summary>Le libellé « (format incorrect) » n'apparaît que si un e-mail est présent.</summary>
    public Visibility EmailNoteVisibility => HasEmail ? Visibility.Visible : Visibility.Collapsed;

    private static readonly Brush WarnBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0x86, 0x0B));
    private static readonly Brush ErrBrush = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
