using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using BadmintonClub.Helpers;
using BadmintonClub.Models;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

public partial class AssociationView : UserControl, IActivableView
{
    private readonly AppServices _services;
    private readonly ObservableCollection<MemberRow> _members = new();
    private readonly ICollectionView _view;
    private int _loadGen; // jeton anti-course : seule la dernière invocation peuple le tableau

    public AssociationView(AppServices services)
    {
        InitializeComponent();
        _services = services;

        _view = CollectionViewSource.GetDefaultView(_members);
        _view.Filter = FilterMember;
        Grid.ItemsSource = _view;

        LabelSelect.Placeholder = "Choisir un libellé";
        LabelSelect.SelectionChanged += (_, _) => _ = LoadMembersAsync();

        _services.LabelsChanged += async () =>
        {
            await LoadLabelsAsync(keepSelection: true);
            await LoadMembersAsync();
        };
    }

    public async void OnActivated()
    {
        await LoadLabelsAsync(keepSelection: true);
        await LoadMembersAsync();
    }

    /// <summary>Ouvre la page pré-filtrée sur un libellé (depuis la page Libellés).</summary>
    public async void ShowForLabel(string labelResourceName)
    {
        await LoadLabelsAsync(keepSelection: false, selectResource: labelResourceName);
        await LoadMembersAsync();
    }

    private bool FilterMember(object obj)
    {
        if (obj is not MemberRow r)
            return false;
        var term = SearchBox?.Text?.Trim();
        if (string.IsNullOrEmpty(term))
            return true;
        var a = r.Adherent;
        return Contains(a.Nom) || Contains(a.Prenom) || Contains(a.Telephone) || Contains(a.Email);

        bool Contains(string? v) => v != null && v.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _view?.Refresh();
        UpdateCount();
    }

    // ---- Chargement -------------------------------------------------------

    private async Task LoadLabelsAsync(bool keepSelection, string? selectResource = null)
    {
        var current = keepSelection
            ? LabelSelect.SelectedTags.OfType<string>().ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        if (selectResource != null)
            current = new HashSet<string>(StringComparer.Ordinal) { selectResource };

        try
        {
            var labels = await _services.GetLabelsAsync();
            LabelSelect.SetOptions(labels.Select(l => new CheckOption
            {
                Text = l.Nom,
                Tag = l.ResourceName,
                IsSelected = current.Contains(l.ResourceName)
            }));
        }
        catch (GoogleSyncException)
        {
            LabelSelect.SetEmptyText("Libellés indisponibles (hors ligne ?).");
        }
    }

    private List<string> SelectedLabels() => LabelSelect.SelectedTags.OfType<string>().ToList();

    private async Task LoadMembersAsync()
    {
        var gen = ++_loadGen; // marque cette invocation ; une suivante l'invalidera

        foreach (var m in _members)
            m.PropertyChanged -= Member_PropertyChanged;
        _members.Clear();

        var labels = SelectedLabels();
        if (labels.Count == 0)
        {
            _view.Refresh();
            UpdateCount();
            UpdateBulkBar();
            return;
        }

        try
        {
            IsEnabled = false;
            Cursor = System.Windows.Input.Cursors.Wait;

            var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var res in labels)
                emails.UnionWith(await _services.Contacts.GetLabelMemberEmailsAsync(res));

            // Une invocation plus récente a pris le relais : on n'ajoute rien (évite le doublon).
            if (gen != _loadGen)
                return;

            foreach (var a in _services.Adherents
                         .Where(a => !string.IsNullOrWhiteSpace(a.Email) && emails.Contains(a.Email))
                         .OrderBy(a => a.Nom, StringComparer.CurrentCultureIgnoreCase))
            {
                var row = new MemberRow(a);
                row.PropertyChanged += Member_PropertyChanged;
                _members.Add(row);
            }
        }
        catch (GoogleSyncException ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Association",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsEnabled = true;
            Cursor = System.Windows.Input.Cursors.Arrow;
        }

        _view.Refresh();
        UpdateCount();
        UpdateBulkBar();
    }

    private List<MemberRow> Filtered() => _view.Cast<MemberRow>().ToList();

    private void UpdateCount()
    {
        if (SelectedLabels().Count == 0)
        {
            CountText.Text = "Choisissez un libellé pour voir ses membres.";
            EmptyResults.Text = "👆 Sélectionnez un libellé ci-dessus pour afficher ses membres.";
            EmptyResults.Visibility = Visibility.Visible;
            return;
        }
        var total = _members.Count;
        var shown = Filtered().Count;
        CountText.Text = shown == total
            ? $"{total} membre(s)"
            : $"{shown} sur {total} membre(s)";

        if (shown > 0)
            EmptyResults.Visibility = Visibility.Collapsed;
        else
        {
            EmptyResults.Text = total == 0
                ? "Aucun membre dans ce libellé."
                : "Aucun résultat pour cette recherche.";
            EmptyResults.Visibility = Visibility.Visible;
        }
    }

    // ---- Associer ---------------------------------------------------------

    private async void Associer_Click(object sender, RoutedEventArgs e)
    {
        if (_services.Adherents.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this),
                "Aucun adhérent à associer. Créez des contacts d'abord.", "Association",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var modal = new AssociateContactsWindow(_services.Contacts, _services.Adherents)
        {
            Owner = Window.GetWindow(this)
        };
        if (modal.ShowDialog() == true)
        {
            await _services.GetLabelsAsync(forceRefresh: true);
            await LoadLabelsAsync(keepSelection: true);
            await LoadMembersAsync();
        }
    }

    // ---- Sélection multiple ----------------------------------------------

    private void Member_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MemberRow.IsSelected))
            UpdateBulkBar();
    }

    private void UpdateBulkBar()
    {
        var count = _members.Count(m => m.IsSelected);
        if (count > 0)
        {
            BulkCountText.Text = $"{count} personne(s) sélectionnée(s)";
            BulkDissocierButton.Content = $"✂ Dissocier ({count})";
            Animations.SlideDownIn(BulkBar);
        }
        else
        {
            Animations.FadeOutCollapse(BulkBar);
        }

        var filtered = Filtered();
        var filteredSel = filtered.Count(m => m.IsSelected);
        SelectAllBox.IsChecked = filteredSel == 0 ? false
            : filteredSel == filtered.Count && filtered.Count > 0 ? true
            : null;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        var check = SelectAllBox.IsChecked == true;
        foreach (var m in Filtered())
            m.IsSelected = check;
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var m in _members)
            m.IsSelected = false;
    }

    // ---- Actions ----------------------------------------------------------

    private void Email_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Hyperlink)?.DataContext is MemberRow r)
            BrowserService.OpenGmailCompose(r.Adherent.Email);
    }

    private async void Dissocier_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not MemberRow r)
            return;

        var labelName = LabelSelect.SelectedOption?.Text ?? "ce libellé";
        var person = $"{r.Adherent.Prenom} {r.Adherent.Nom}".Trim();
        if (string.IsNullOrWhiteSpace(person))
            person = r.Adherent.Email;

        if (!ConfirmWindow.Ask(Window.GetWindow(this), "Dissocier du libellé",
                $"Dissocier {person} du libellé « {labelName} » ?\nLe contact n'est pas supprimé.",
                confirmText: "Dissocier", icon: "✂"))
            return;

        await DissocierAsync(new[] { r });
    }

    private async void BulkDissocier_Click(object sender, RoutedEventArgs e)
    {
        var selected = _members.Where(m => m.IsSelected).ToList();
        if (selected.Count == 0)
            return;

        var labelName = LabelSelect.SelectedOption?.Text ?? "ce libellé";
        if (!ConfirmWindow.Ask(Window.GetWindow(this), "Dissocier du libellé",
                $"Dissocier les {selected.Count} personne(s) sélectionnée(s) du libellé « {labelName} » ?\nLes contacts ne sont pas supprimés.",
                confirmText: "Dissocier", icon: "✂"))
            return;

        await DissocierAsync(selected);
    }

    private async Task DissocierAsync(IReadOnlyList<MemberRow> rows)
    {
        var labels = SelectedLabels();
        if (labels.Count == 0)
            return;

        var owner = Window.GetWindow(this)!;
        var result = await ProgressRunner.RunAsync(owner, "Dissociation…", rows, async r =>
        {
            var a = r.Adherent;
            var resource = string.IsNullOrEmpty(a.GoogleResourceName)
                ? await _services.Contacts.EnsureContactResourceAsync(a)
                : a.GoogleResourceName;
            a.GoogleResourceName = resource;
            _services.SaveAdherents();

            foreach (var group in labels)
                await _services.Contacts.SetMembershipAsync(resource, group, add: false);

            r.PropertyChanged -= Member_PropertyChanged;
            _members.Remove(r);
        });

        _view.Refresh();
        UpdateCount();
        UpdateBulkBar();

        if (result.Failed > 0)
            MessageBox.Show(owner,
                $"{result.Ok} dissocié(s), {result.Failed} en erreur.\n\n{result.LastError}",
                "Association", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}

/// <summary>Ligne membre avec sélection (isolée de la sélection de la page Contacts).</summary>
internal class MemberRow : INotifyPropertyChanged
{
    public Adherent Adherent { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public MemberRow(Adherent adherent) => Adherent = adherent;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
