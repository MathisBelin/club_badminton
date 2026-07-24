using System.Windows;
using System.Windows.Controls;
using BadmintonClub.Models;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

/// <summary>
/// Historique des activités : trois tableaux (Utilisateurs / Libellés / Sheets) choisis par radio,
/// filtrés par action et par période, <b>paginés</b> (1000 par page). Lecture seule.
/// </summary>
public partial class HistoryView : UserControl, IActivableView
{
    /// <summary>Nombre maximum de lignes affichées par page.</summary>
    private const int PageSize = 1000;

    private readonly AppServices _services;
    private bool _ready; // évite les handlers déclenchés pendant InitializeComponent
    private List<ActivityEntry> _filtered = new();
    private int _page;

    public HistoryView(AppServices services)
    {
        InitializeComponent();
        _services = services;

        _services.Activities.CollectionChanged += (_, _) => { if (_ready) Apply(); };
        _ready = true;
        Apply();
    }

    public void OnActivated() => Apply();

    private ActivityCategory SelectedCategory =>
        RadioLabels.IsChecked == true ? ActivityCategory.Libelle
        : RadioSheets.IsChecked == true ? ActivityCategory.Sheet
        : ActivityCategory.Utilisateur;

    private bool Matches(ActivityEntry a)
    {
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

    /// <summary>Recalcule le filtrage et affiche la page courante (1000 lignes max).</summary>
    private void Apply(bool resetPage = true)
    {
        if (!_ready)
            return;
        if (resetPage)
            _page = 0;

        // Activities est déjà trié (le plus récent en tête).
        _filtered = _services.Activities.Where(Matches).ToList();

        var pageCount = Math.Max(1, (int)Math.Ceiling(_filtered.Count / (double)PageSize));
        _page = Math.Clamp(_page, 0, pageCount - 1);

        Grid.ItemsSource = _filtered.Skip(_page * PageSize).Take(PageSize).ToList();
        UpdateCount(pageCount);
    }

    private void UpdateCount(int pageCount)
    {
        var total = _filtered.Count;
        CountText.Text = total == 0 ? "Aucune activité pour ces filtres." : $"{total} activité(s)";
        EmptyResults.Text = "Aucune activité à afficher.";
        EmptyResults.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;

        PageText.Text = $"Page {_page + 1} / {pageCount}";
        PrevBtn.IsEnabled = _page > 0;
        NextBtn.IsEnabled = _page < pageCount - 1;
    }

    private void Category_Changed(object sender, RoutedEventArgs e) => Apply();
    private void Filter_Changed(object sender, RoutedEventArgs e) => Apply();
    private void TargetSearch_Changed(object sender, TextChangedEventArgs e) => Apply();

    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        if (_page <= 0)
            return;
        _page--;
        Apply(resetPage: false);
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        _page++;
        Apply(resetPage: false);
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
}
