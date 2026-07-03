using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using BadmintonClub.Models;

namespace BadmintonClub.Controls;

/// <summary>
/// Liste déroulante multi-sélection (type « select2 ») : bouton résumant la sélection,
/// popup avec recherche, boutons Tout / Aucun, et cases à cocher.
/// </summary>
public partial class MultiSelectComboBox : UserControl
{
    private readonly ObservableCollection<CheckOption> _options = new();
    private ICollectionView _view = null!;

    public string Placeholder { get; set; } = "Choisir…";

    /// <summary>Déclenché à chaque changement de sélection.</summary>
    public event EventHandler? SelectionChanged;

    public MultiSelectComboBox()
    {
        InitializeComponent();

        _view = CollectionViewSource.GetDefaultView(_options);
        _view.Filter = Filter;
        OptionsList.ItemsSource = _view;

        UpdateToggleText();
    }

    /// <summary>Remplace les options affichées.</summary>
    public void SetOptions(IEnumerable<CheckOption> options)
    {
        foreach (var o in _options)
            o.PropertyChanged -= Option_PropertyChanged;
        _options.Clear();

        foreach (var o in options)
        {
            o.PropertyChanged += Option_PropertyChanged;
            _options.Add(o);
        }

        EmptyText.Visibility = _options.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SearchBox.Clear();
        _view.Refresh();
        UpdateToggleText();
    }

    /// <summary>Texte affiché quand aucune option (ex. « Aucun libellé »).</summary>
    public void SetEmptyText(string text) => EmptyText.Text = text;

    /// <summary>Décoche toutes les options.</summary>
    public void ClearSelection()
    {
        foreach (var o in _options)
            o.IsSelected = false;
    }

    public IReadOnlyList<CheckOption> SelectedOptions => _options.Where(o => o.IsSelected).ToList();

    public IEnumerable<object?> SelectedTags => SelectedOptions.Select(o => o.Tag);

    private bool Filter(object obj)
    {
        if (obj is not CheckOption o)
            return false;
        var term = SearchBox.Text?.Trim();
        return string.IsNullOrEmpty(term) || o.Text.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private void Option_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CheckOption.IsSelected))
            return;
        UpdateToggleText();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateToggleText()
    {
        var count = _options.Count(o => o.IsSelected);
        ToggleText.Text = count == 0 ? Placeholder : $"{count} sélectionné(s)";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => _view?.Refresh();

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var o in _view.Cast<CheckOption>())
            o.IsSelected = true;
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var o in _view.Cast<CheckOption>())
            o.IsSelected = false;
    }
}
