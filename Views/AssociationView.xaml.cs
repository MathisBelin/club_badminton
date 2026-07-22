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

    private List<LabelItem> _allLabels = new();
    // Cache des e-mails membres par libellé (évite de re-questionner l'API à chaque coche de filtre).
    private readonly Dictionary<string, HashSet<string>> _memberEmailsCache = new(StringComparer.Ordinal);

    public AssociationView(AppServices services)
    {
        InitializeComponent();
        _services = services;

        _view = CollectionViewSource.GetDefaultView(_members);
        _view.Filter = FilterMember;
        Grid.ItemsSource = _view;

        LabelSelect.Placeholder = "Tous les libellés";
        LabelAll.Placeholder = "Aucun";
        LabelNone.Placeholder = "Aucun";

        LabelSelect.SelectionChanged += (_, _) => OnIncludeChanged();
        LabelAll.SelectionChanged += (_, _) => OnIncludeChanged();
        LabelNone.SelectionChanged += (_, _) => OnNoneChanged();

        _services.LabelsChanged += async () =>
        {
            _memberEmailsCache.Clear();
            await LoadLabelsAsync(keepSelection: true);
            await LoadMembersAsync();
        };
    }

    public async void OnActivated()
    {
        _memberEmailsCache.Clear();
        await LoadLabelsAsync(keepSelection: true);
        await LoadMembersAsync();
    }

    /// <summary>Un filtre « inclusion » (OU/ET) a changé → adapte la liste d'exclusion et recharge.</summary>
    private void OnIncludeChanged()
    {
        RebuildNoneOptions();
        _ = LoadMembersAsync();
    }

    /// <summary>Le filtre « exclusion » a changé → adapte les listes d'inclusion et recharge.</summary>
    private void OnNoneChanged()
    {
        RebuildIncludeOptions();
        _ = LoadMembersAsync();
    }

    private void AdvancedToggle_Click(object sender, RoutedEventArgs e)
        => AdvancedPanel.Visibility = AdvancedPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;

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
        var orSel = keepSelection ? TagSet(LabelSelect) : new HashSet<string>(StringComparer.Ordinal);
        var andSel = keepSelection ? TagSet(LabelAll) : new HashSet<string>(StringComparer.Ordinal);
        var noneSel = keepSelection ? TagSet(LabelNone) : new HashSet<string>(StringComparer.Ordinal);
        if (selectResource != null)
        {
            orSel = new HashSet<string>(StringComparer.Ordinal) { selectResource };
            andSel.Clear();
            noneSel.Clear();
        }

        try
        {
            _allLabels = (await _services.GetLabelsAsync()).ToList();
        }
        catch (GoogleSyncException)
        {
            LabelSelect.SetEmptyText("Libellés indisponibles (hors ligne ?).");
            LabelAll.SetEmptyText("Libellés indisponibles (hors ligne ?).");
            LabelNone.SetEmptyText("Libellés indisponibles (hors ligne ?).");
            return;
        }

        var include = new HashSet<string>(orSel, StringComparer.Ordinal);
        include.UnionWith(andSel);

        // Un libellé exclu n'est plus proposé en inclusion, et inversement.
        LabelSelect.SetOptions(Opts(_allLabels.Where(l => !noneSel.Contains(l.ResourceName)), orSel));
        LabelAll.SetOptions(Opts(_allLabels.Where(l => !noneSel.Contains(l.ResourceName)), andSel));
        LabelNone.SetOptions(Opts(_allLabels.Where(l => !include.Contains(l.ResourceName)), noneSel));
    }

    // ---- Adaptation des listes (inclusion ↔ exclusion) --------------------

    private static IEnumerable<CheckOption> Opts(IEnumerable<LabelItem> labels, ISet<string> selected)
        => labels.Select(l => new CheckOption
        {
            Text = l.Nom,
            Tag = l.ResourceName,
            IsSelected = selected.Contains(l.ResourceName)
        });

    private static HashSet<string> TagSet(BadmintonClub.Controls.MultiSelectComboBox c)
        => c.SelectedTags.OfType<string>().ToHashSet(StringComparer.Ordinal);

    /// <summary>Libellés cochés en inclusion (OU ∪ ET).</summary>
    private HashSet<string> IncludeSet()
    {
        var s = TagSet(LabelSelect);
        s.UnionWith(TagSet(LabelAll));
        return s;
    }

    /// <summary>Reconstruit la liste d'exclusion sans les libellés déjà utilisés en inclusion.</summary>
    private void RebuildNoneOptions()
    {
        var include = IncludeSet();
        var noneSel = TagSet(LabelNone);
        LabelNone.SetOptions(Opts(_allLabels.Where(l => !include.Contains(l.ResourceName)), noneSel));
    }

    /// <summary>Reconstruit les listes d'inclusion sans les libellés déjà utilisés en exclusion.</summary>
    private void RebuildIncludeOptions()
    {
        var noneSel = TagSet(LabelNone);
        LabelSelect.SetOptions(Opts(_allLabels.Where(l => !noneSel.Contains(l.ResourceName)), TagSet(LabelSelect)));
        LabelAll.SetOptions(Opts(_allLabels.Where(l => !noneSel.Contains(l.ResourceName)), TagSet(LabelAll)));
    }

    /// <summary>E-mails membres d'un libellé (mis en cache pour la session d'affichage).</summary>
    private async Task<HashSet<string>> MembersOfAsync(string labelResource)
    {
        if (!_memberEmailsCache.TryGetValue(labelResource, out var set))
        {
            set = await _services.Contacts.GetLabelMemberEmailsAsync(labelResource);
            _memberEmailsCache[labelResource] = set;
        }
        return set;
    }

    private List<string> SelectedLabels() => LabelSelect.SelectedTags.OfType<string>().ToList();

    private async Task LoadMembersAsync()
    {
        var gen = ++_loadGen; // marque cette invocation ; une suivante l'invalidera

        var orLabels = SelectedLabels();                                   // OU : au moins un
        var andLabels = LabelAll.SelectedTags.OfType<string>().ToList();   // ET : tous
        var noneLabels = LabelNone.SelectedTags.OfType<string>().ToList(); // EXCLUSION : aucun

        foreach (var m in _members)
            m.PropertyChanged -= Member_PropertyChanged;
        _members.Clear();

        try
        {
            IsEnabled = false;
            Cursor = System.Windows.Input.Cursors.Wait;

            // Base : toutes les personnes, ou l'union des membres du filtre OU.
            List<Adherent> people;
            if (orLabels.Count == 0)
            {
                people = _services.Adherents.ToList();
            }
            else
            {
                var union = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var res in orLabels)
                {
                    union.UnionWith(await MembersOfAsync(res));
                    if (gen != _loadGen) return;
                }
                people = _services.Adherents
                    .Where(a => !string.IsNullOrWhiteSpace(a.Email) && union.Contains(a.Email)).ToList();
            }

            // ET : ne garder que les personnes présentes dans CHAQUE libellé requis.
            foreach (var res in andLabels)
            {
                var members = await MembersOfAsync(res);
                if (gen != _loadGen) return;
                people = people.Where(a => !string.IsNullOrWhiteSpace(a.Email) && members.Contains(a.Email)).ToList();
            }

            // EXCLUSION : retirer les personnes présentes dans l'un des libellés exclus.
            foreach (var res in noneLabels)
            {
                var members = await MembersOfAsync(res);
                if (gen != _loadGen) return;
                people = people.Where(a => string.IsNullOrWhiteSpace(a.Email) || !members.Contains(a.Email)).ToList();
            }

            foreach (var a in people.OrderBy(a => a.Nom, StringComparer.CurrentCultureIgnoreCase))
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
        var filterActive = SelectedLabels().Count > 0
            || LabelAll.SelectedTags.Any() || LabelNone.SelectedTags.Any();
        var total = _members.Count;
        var shown = Filtered().Count;
        CountText.Text = shown == total
            ? $"{total} personne(s)"
            : $"{shown} sur {total} personne(s)";

        if (shown > 0)
            EmptyResults.Visibility = Visibility.Collapsed;
        else
        {
            EmptyResults.Text = total == 0
                ? (filterActive ? "Aucune personne pour ce(s) libellé(s)." : "Aucune personne.")
                : "Aucun résultat pour cette recherche.";
            EmptyResults.Visibility = Visibility.Visible;
        }
    }

    // ---- Préinscriptions --------------------------------------------------

    /// <summary>Demande d'ouvrir la page « Préinscriptions ».</summary>
    public event Action? OpenPreinscriptionsRequested;

    private void Preinscriptions_Click(object sender, RoutedEventArgs e)
        => OpenPreinscriptionsRequested?.Invoke();

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
            BulkAssocierButton.Content = $"👥 Associer ({count})";
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

    /// <summary>Gérer les libellés d'une personne (select2 multi pré-coché → diff appliqué).</summary>
    private async void Gerer_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not MemberRow r)
            return;

        var a = r.Adherent;
        var owner = Window.GetWindow(this)!;

        HashSet<string> current;
        string resource;
        try
        {
            resource = await EnsureResourceAsync(a);
            current = await _services.Contacts.GetContactMembershipsAsync(resource);
        }
        catch (GoogleSyncException ex) { Warn(ex.Message); return; }

        // On ne raisonne que sur les libellés utilisateur : les groupes système (myContacts…) ne
        // doivent jamais être proposés ni retirés.
        var names = LabelNames();
        var currentUser = current.Where(res => names.ContainsKey(res)).ToHashSet(StringComparer.Ordinal);

        var win = new PickLabelsWindow(_services, $"Libellés de {PersonName(a)}",
            "Cochez les libellés à associer à cette personne.", "✅ Valider", currentUser) { Owner = owner };
        if (win.ShowDialog() != true)
            return;

        var chosen = win.SelectedResources.ToHashSet(StringComparer.Ordinal);
        var toAdd = chosen.Except(currentUser).ToList();
        var toRemove = currentUser.Except(chosen).ToList();
        if (toAdd.Count == 0 && toRemove.Count == 0)
            return;

        // Un seul appel : fixe l'ensemble des libellés voulus (évite la perte du dernier retrait).
        var result = await ProgressRunner.RunBusyAsync(owner, "Mise à jour des libellés…",
            () => _services.Contacts.SetContactMembershipsAsync(resource, chosen));

        if (result.Failed > 0)
            MessageBox.Show(owner, result.LastError, "Association",
                MessageBoxButton.OK, MessageBoxImage.Warning);

        foreach (var g in toAdd)
            _services.LogContactActivity(Models.ActivityAction.Association, a, names.GetValueOrDefault(g, g));
        foreach (var g in toRemove)
            _services.LogContactActivity(Models.ActivityAction.Dissociation, a, names.GetValueOrDefault(g, g));

        _memberEmailsCache.Clear(); // les appartenances ont changé
        await LoadMembersAsync();
    }

    /// <summary>Affiche les libellés d'une personne dans un message.</summary>
    private async void Voir_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not MemberRow r)
            return;

        var a = r.Adherent;
        var owner = Window.GetWindow(this)!;

        HashSet<string> current;
        try
        {
            current = string.IsNullOrEmpty(a.GoogleResourceName)
                ? new HashSet<string>(StringComparer.Ordinal)
                : await _services.Contacts.GetContactMembershipsAsync(a.GoogleResourceName);
        }
        catch (GoogleSyncException ex) { Warn(ex.Message); return; }

        var names = LabelNames();
        var labels = current
            .Where(res => names.ContainsKey(res)) // on ignore les groupes système (myContacts, etc.)
            .Select(res => names[res])
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        new LabelListWindow(PersonName(a), labels) { Owner = owner }.ShowDialog();
    }

    private async void BulkAssocier_Click(object sender, RoutedEventArgs e)
    {
        var selected = _members.Where(m => m.IsSelected).ToList();
        if (selected.Count == 0)
            return;

        var win = new PickLabelsWindow(_services, $"Associer {selected.Count} personne(s)",
            "Choisissez le(s) libellé(s) auxquels associer les personnes sélectionnées.", "👥 Associer")
        { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() != true || win.SelectedResources.Count == 0)
            return;

        await ApplyMembershipAsync(selected, win.SelectedResources, add: true, "Association…");
    }

    private async void BulkDissocier_Click(object sender, RoutedEventArgs e)
    {
        var selected = _members.Where(m => m.IsSelected).ToList();
        if (selected.Count == 0)
            return;

        var win = new PickLabelsWindow(_services, $"Dissocier {selected.Count} personne(s)",
            "Choisissez le(s) libellé(s) dont retirer les personnes sélectionnées.", "✂ Dissocier")
        { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() != true || win.SelectedResources.Count == 0)
            return;

        await ApplyMembershipAsync(selected, win.SelectedResources, add: false, "Dissociation…");
    }

    /// <summary>Applique (ajout/retrait) chaque libellé choisi à chaque personne sélectionnée.</summary>
    private async Task ApplyMembershipAsync(
        IReadOnlyList<MemberRow> rows, IReadOnlyList<string> labels, bool add, string title)
    {
        var owner = Window.GetWindow(this)!;

        // On garantit d'abord le lien Google de chaque personne (une seule fois).
        try
        {
            foreach (var r in rows)
                await EnsureResourceAsync(r.Adherent);
        }
        catch (GoogleSyncException ex) { Warn(ex.Message); return; }

        var ops = new List<(string Resource, string Group)>();
        foreach (var r in rows)
            foreach (var g in labels)
                ops.Add((r.Adherent.GoogleResourceName, g));

        var result = await ProgressRunner.RunAsync(owner, title, ops,
            op => _services.Contacts.SetMembershipAsync(op.Resource, op.Group, add));

        var names = LabelNames();
        var action = add ? Models.ActivityAction.Association : Models.ActivityAction.Dissociation;
        foreach (var r in rows)
            foreach (var g in labels)
                _services.LogContactActivity(action, r.Adherent, names.GetValueOrDefault(g, g));

        _memberEmailsCache.Clear(); // les appartenances ont changé
        await LoadMembersAsync();

        if (result.Failed > 0)
            MessageBox.Show(owner,
                $"{result.Ok} opération(s) appliquée(s), {result.Failed} en erreur.\n\n{result.LastError}",
                "Association", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ---- Utilitaires ------------------------------------------------------

    private void Warn(string message)
        => MessageBox.Show(Window.GetWindow(this), message, "Association",
            MessageBoxButton.OK, MessageBoxImage.Warning);

    private static string PersonName(Adherent a)
    {
        var n = $"{a.Prenom} {a.Nom}".Trim();
        return string.IsNullOrWhiteSpace(n) ? a.Email : n;
    }

    /// <summary>Ressource → nom de libellé (depuis le cache).</summary>
    private Dictionary<string, string> LabelNames()
        => _services.CachedLabels
            .GroupBy(l => l.ResourceName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Nom, StringComparer.Ordinal);

    /// <summary>Garantit le lien Google du contact (crée si besoin) et persiste.</summary>
    private async Task<string> EnsureResourceAsync(Adherent a)
    {
        if (string.IsNullOrEmpty(a.GoogleResourceName))
        {
            a.GoogleResourceName = await _services.Contacts.EnsureContactResourceAsync(a);
            _services.SaveAdherents();
        }
        return a.GoogleResourceName;
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
