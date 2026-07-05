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

    /// <summary>Texte affiché quand la liste n'a aucune option (avant filtrage).</summary>
    private string _noOptionsText = "Aucune option.";

    private bool _suppress; // évite la ré-entrance lors des désélections en cascade (single-select)

    public string Placeholder { get; set; } = "Choisir…";

    private bool _singleSelect;

    /// <summary>Mode sélection unique (select2 non multiple) : une seule option, popup fermé au choix.</summary>
    public bool SingleSelect
    {
        get => _singleSelect;
        set
        {
            _singleSelect = value;
            if (BulkButtons != null)
                BulkButtons.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            if (OptionsList != null)
                OptionsList.ItemTemplate = (DataTemplate)Resources[value ? "SingleTemplate" : "MultiTemplate"];
            UpdateToggleText();
        }
    }

    /// <summary>Option sélectionnée (utile en mode single-select) ; null si aucune.</summary>
    public CheckOption? SelectedOption => _options.FirstOrDefault(o => o.IsSelected);

    /// <summary>Déclenché à chaque changement de sélection.</summary>
    public event EventHandler? SelectionChanged;

    public MultiSelectComboBox()
    {
        InitializeComponent();

        _view = CollectionViewSource.GetDefaultView(_options);
        _view.Filter = Filter;
        OptionsList.ItemsSource = _view;

        // À l'ouverture du popup, on place le curseur dans la recherche pour taper directement.
        Popup.Opened += (_, _) => Dispatcher.BeginInvoke(
            new Action(() =>
            {
                SearchBox.Focus();
                System.Windows.Input.Keyboard.Focus(SearchBox);
                SearchBox.SelectAll();
            }),
            System.Windows.Threading.DispatcherPriority.Input);

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

        SearchBox.Clear();
        _view.Refresh();
        UpdateEmptyState();
        UpdateToggleText();

        // En mode single, on surligne l'option sélectionnée dans la liste.
        if (_singleSelect)
        {
            _suppress = true;
            OptionsList.SelectedItem = SelectedOption;
            _suppress = false;
        }
    }

    private void OptionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_singleSelect || _suppress)
            return;
        if (OptionsList.SelectedItem is CheckOption o)
            o.IsSelected = true; // déclenche la désélection des autres + fermeture du popup
    }

    /// <summary>Texte affiché quand aucune option (ex. « Aucun libellé »).</summary>
    public void SetEmptyText(string text)
    {
        _noOptionsText = text;
        UpdateEmptyState();
    }

    /// <summary>Affiche un message adapté quand la liste (filtrée) est vide.</summary>
    private void UpdateEmptyState()
    {
        if (_options.Count == 0)
        {
            EmptyText.Text = _noOptionsText;
            EmptyText.Visibility = Visibility.Visible;
        }
        else if (!_view.Cast<CheckOption>().Any())
        {
            EmptyText.Text = "Aucun résultat.";
            EmptyText.Visibility = Visibility.Visible;
        }
        else
        {
            EmptyText.Visibility = Visibility.Collapsed;
        }
    }

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
        if (_suppress)
            return; // désélection en cascade : on ne renotifie pas

        if (_singleSelect && sender is CheckOption changed && changed.IsSelected)
        {
            _suppress = true;
            foreach (var o in _options)
                if (!ReferenceEquals(o, changed))
                    o.IsSelected = false;
            _suppress = false;
            Popup.IsOpen = false; // referme le popup dès qu'un choix est fait
        }

        UpdateToggleText();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateToggleText()
    {
        if (_singleSelect)
        {
            ToggleText.Text = SelectedOption?.Text ?? Placeholder;
            return;
        }
        var count = _options.Count(o => o.IsSelected);
        ToggleText.Text = count == 0 ? Placeholder : $"{count} sélectionné(s)";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _view?.Refresh();
        UpdateEmptyState();
    }

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
