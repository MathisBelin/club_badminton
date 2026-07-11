using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using BadmintonClub.Helpers;
using BadmintonClub.Models;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

public partial class ContactsView : UserControl, IActivableView
{
    private readonly AppServices _services;

    // Pagination : la grille n'affiche qu'une page ; tri/filtre s'appliquent sur la liste complète.
    private readonly ObservableCollection<Adherent> _pageItems = new();
    private List<Adherent> _filtered = new();      // adhérents filtrés + triés (toutes pages)
    private int _pageSize = 20;
    private int _pageIndex;                          // page courante (0-based)
    private string _sortMember = nameof(Adherent.Nom);
    private ListSortDirection _sortDir = ListSortDirection.Ascending;
    private bool _rebuildQueued;
    private bool _ready; // évite que les handlers agissent pendant InitializeComponent

    // Filtre par libellé (recherche avancée).
    private const string NoLabelTag = "__none__";
    private HashSet<string>? _labelFilterEmails;
    private bool _filterNoLabel;
    private HashSet<string>? _allLabelEmails; // union des membres de tous les libellés (pour « sans libellé »)
    private readonly Dictionary<string, HashSet<string>> _labelMemberCache = new(StringComparer.Ordinal);
    private bool _hideIncomplete; // masque les contacts sans nom, ni prénom, ni téléphone
    private bool _duplicatesOnly; // n'affiche que les homonymes (même nom + prénom, ≥ 2 personnes)
    private HashSet<string>? _duplicateKeys; // clés « nom|prénom » en doublon (recalculées à chaque rebuild)
    private DateTime? _dateFrom, _dateTo; // filtre par période d'ajout (DateInscription)

    public ContactsView(AppServices services)
    {
        InitializeComponent();
        _services = services;

        Grid.ItemsSource = _pageItems;

        LabelFilter.Placeholder = "Choisir des libellés";
        LabelFilter.SelectionChanged += LabelFilter_SelectionChanged;

        // Mise à jour en direct du filtre quand les libellés changent (création/suppression).
        _services.LabelsChanged += OnLabelsChanged;

        foreach (var a in _services.Adherents)
            a.PropertyChanged += Adherent_PropertyChanged;
        _services.Adherents.CollectionChanged += Adherents_CollectionChanged;

        _ready = true;
        RebuildAll();
    }

    public void OnActivated()
    {
        RefreshView();
        _ = PopulateLabelFilterAsync();
    }

    /// <summary>Réinitialise l'affichage et les caches (changement de compte).</summary>
    public void ResetView()
    {
        SearchBox.Text = string.Empty;
        _labelFilterEmails = null;
        _filterNoLabel = false;
        _allLabelEmails = null;
        _labelMemberCache.Clear();
        LabelFilter.ClearSelection();

        RebuildAll();
    }

    /// <summary>Synchronisation deux sens avec Google au lancement. Silencieuse.</summary>
    public async Task AutoSyncContactsAsync()
    {
        try
        {
            await _services.SyncContactsAsync();
            RefreshView();
        }
        catch (GoogleSyncException)
        {
            // Silencieux : l'appli reste utilisable hors ligne avec la liste locale.
        }
    }

    // ---- Filtrage / pagination -------------------------------------------

    /// <summary>Un adhérent passe-t-il le filtre (recherche + libellé + complétude) ?</summary>
    private bool PassesFilter(Adherent a)
    {
        // Masque les contacts sans nom NI prénom NI téléphone (souvent juste un e-mail importé).
        if (_hideIncomplete &&
            string.IsNullOrWhiteSpace(a.Nom) &&
            string.IsNullOrWhiteSpace(a.Prenom) &&
            string.IsNullOrWhiteSpace(a.Telephone))
            return false;

        // Filtre « doublons » : uniquement les personnes ayant un homonyme (même nom + prénom).
        if (_duplicatesOnly && (!HasName(a) || _duplicateKeys == null || !_duplicateKeys.Contains(DupKey(a))))
            return false;

        // Filtre par période d'ajout (DateInscription).
        if (_dateFrom is DateTime df && a.DateInscription.Date < df.Date)
            return false;
        if (_dateTo is DateTime dt && a.DateInscription.Date > dt.Date)
            return false;

        // Filtre par libellé (recherche avancée).
        if (_labelFilterEmails != null || _filterNoLabel)
        {
            var inSelectedLabel = _labelFilterEmails != null &&
                !string.IsNullOrWhiteSpace(a.Email) && _labelFilterEmails.Contains(a.Email);
            var isWithoutLabel = _filterNoLabel &&
                (string.IsNullOrWhiteSpace(a.Email) ||
                 (_allLabelEmails != null && !_allLabelEmails.Contains(a.Email)));
            if (!inSelectedLabel && !isWithoutLabel)
                return false;
        }

        var term = SearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(term))
            return true;

        return Contains(a.Nom) || Contains(a.Prenom) || Contains(a.Telephone) || Contains(a.Email);

        bool Contains(string? v) => v != null && v.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RebuildAll();

    private void HideIncomplete_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready)
            return;
        _hideIncomplete = HideIncompleteCheck.IsChecked == true;
        RebuildAll();
    }

    private void Duplicates_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready)
            return;
        _duplicatesOnly = DuplicatesToggle.IsChecked == true;
        RebuildAll();
    }

    private void AdvancedToggle_Click(object sender, RoutedEventArgs e)
        => AdvancedPanel.Visibility = AdvancedPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;

    private void DateFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready)
            return;
        _dateFrom = FromDate.SelectedDate;
        _dateTo = ToDate.SelectedDate;
        RebuildAll();
    }

    private void ClearDates_Click(object sender, RoutedEventArgs e)
    {
        FromDate.SelectedDate = null;
        ToDate.SelectedDate = null; // déclenche DateFilter_Changed → RebuildAll
    }

    // ---- Pagination : construction de la page affichée -------------------

    /// <summary>Recompute la liste filtrée+triée, revient à la 1re page, puis affiche.</summary>
    private void RebuildAll()
    {
        _pageIndex = 0;
        Rebuild();
    }

    /// <summary>Recompute en conservant la page courante (clampée), puis affiche.</summary>
    private void RefreshView() => Rebuild();

    private void Rebuild()
    {
        // Détection des homonymes (sur toute la liste) quand le filtre « doublons » est actif.
        _duplicateKeys = _duplicatesOnly
            ? _services.Adherents.Where(HasName)
                .GroupBy(DupKey)
                .Where(g => g.Count() >= 2)
                .Select(g => g.Key)
                .ToHashSet(StringComparer.Ordinal)
            : null;

        _filtered = _services.Adherents.Where(PassesFilter).ToList();
        SortFiltered();
        RefreshPage();
    }

    /// <summary>Vrai si la personne a un nom ou un prénom (sinon on l'ignore pour les doublons).</summary>
    private static bool HasName(Adherent a)
        => !string.IsNullOrWhiteSpace(a.Nom) || !string.IsNullOrWhiteSpace(a.Prenom);

    /// <summary>Clé de comparaison des homonymes : nom + prénom normalisés (casse/espaces ignorés).</summary>
    private static string DupKey(Adherent a)
        => $"{(a.Nom ?? string.Empty).Trim().ToLowerInvariant()}|{(a.Prenom ?? string.Empty).Trim().ToLowerInvariant()}";

    private void SortFiltered()
    {
        // Tri par date (colonne « Ajouté le ») : comparaison chronologique.
        if (_sortMember == nameof(Adherent.DateInscription))
        {
            _filtered = (_sortDir == ListSortDirection.Ascending
                ? _filtered.OrderBy(a => a.DateInscription)
                : _filtered.OrderByDescending(a => a.DateInscription)).ToList();
            return;
        }

        Func<Adherent, string> key = _sortMember switch
        {
            nameof(Adherent.Prenom) => a => a.Prenom ?? string.Empty,
            nameof(Adherent.Telephone) => a => a.Telephone ?? string.Empty,
            nameof(Adherent.Email) => a => a.Email ?? string.Empty,
            _ => a => a.Nom ?? string.Empty
        };
        _filtered = (_sortDir == ListSortDirection.Ascending
            ? _filtered.OrderBy(key, StringComparer.CurrentCultureIgnoreCase)
            : _filtered.OrderByDescending(key, StringComparer.CurrentCultureIgnoreCase)).ToList();
    }

    /// <summary>Remplit la grille avec la page courante et met à jour la barre de pagination.</summary>
    private void RefreshPage()
    {
        var total = _filtered.Count;
        var pageCount = Math.Max(1, (total + _pageSize - 1) / _pageSize);
        _pageIndex = Math.Clamp(_pageIndex, 0, pageCount - 1);

        _pageItems.Clear();
        foreach (var a in _filtered.Skip(_pageIndex * _pageSize).Take(_pageSize))
            _pageItems.Add(a);

        PageInfo.Text = $"Page {_pageIndex + 1} / {pageCount}";
        PrevBtn.IsEnabled = _pageIndex > 0;
        NextBtn.IsEnabled = _pageIndex < pageCount - 1;

        UpdateCount();
        UpdateBulkBar();
    }

    private void Grid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        var member = e.Column.SortMemberPath;
        if (string.IsNullOrEmpty(member))
            return;

        // Bascule ascendant/descendant sur la même colonne, sinon nouvelle colonne ascendante.
        _sortDir = _sortMember == member && _sortDir == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
        _sortMember = member;

        foreach (var c in Grid.Columns)
            c.SortDirection = null;
        e.Column.SortDirection = _sortDir;

        e.Handled = true; // on trie la liste complète nous-mêmes (pas seulement la page)
        SortFiltered();
        RefreshPage();
    }

    private void PageSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready)
            return;
        if (PageSizeCombo?.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Content?.ToString(), out var size) && size > 0)
        {
            _pageSize = size;
            _pageIndex = 0;
            RefreshPage();
        }
    }

    private void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_pageIndex > 0) { _pageIndex--; RefreshPage(); }
    }

    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        _pageIndex++;
        RefreshPage();
    }

    private void Email_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.Documents.Hyperlink)?.DataContext is Adherent a)
            BrowserService.OpenGmailCompose(a.Email);
    }

    // ---- Recherche avancée : filtre par libellé --------------------------

    /// <summary>Charge/rafraîchit les options du filtre par libellé.</summary>
    private async Task PopulateLabelFilterAsync()
    {
        try
        {
            var labels = await _services.GetLabelsAsync();
            RefreshLabelFilterOptions(labels);
        }
        catch (GoogleSyncException)
        {
            LabelFilter.SetEmptyText("Libellés indisponibles (hors ligne ?).");
        }
    }

    private void OnLabelsChanged()
    {
        _allLabelEmails = null; // les libellés ont changé : union à recalculer
        RefreshLabelFilterOptions(_services.CachedLabels);
    }

    private void RefreshLabelFilterOptions(IEnumerable<LabelItem> labels)
    {
        var list = labels.ToList();
        var selected = LabelFilter.SelectedTags.OfType<string>().ToHashSet(StringComparer.Ordinal);

        // Option « sans libellé » + libellés (un supprimé disparaît, un nouveau apparaît),
        // en conservant les sélections encore valides.
        var options = new List<CheckOption>
        {
            new() { Text = "(Sans libellé)", Tag = NoLabelTag, IsSelected = _filterNoLabel }
        };
        options.AddRange(list.Select(l => new CheckOption
        {
            Text = l.Nom,
            Tag = l.ResourceName,
            IsSelected = selected.Contains(l.ResourceName)
        }));
        LabelFilter.SetOptions(options);

        // Nettoie le cache d'appartenance des libellés disparus.
        var valid = list.Select(l => l.ResourceName).ToHashSet(StringComparer.Ordinal);
        foreach (var key in _labelMemberCache.Keys.ToList())
            if (!valid.Contains(key))
                _labelMemberCache.Remove(key);

        // Si un libellé actuellement filtré a été supprimé, on réinitialise le filtre.
        if (_labelFilterEmails != null && selected.Any(s => !valid.Contains(s)))
        {
            _labelFilterEmails = null;
            RebuildAll();
        }
    }

    private async void LabelFilter_SelectionChanged(object? sender, EventArgs e)
    {
        var selected = LabelFilter.SelectedTags.OfType<string>().ToList();
        ClearLabelBtn.Visibility = selected.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        _filterNoLabel = selected.Contains(NoLabelTag);
        var realLabels = selected.Where(t => t != NoLabelTag).ToList();

        if (realLabels.Count == 0 && !_filterNoLabel)
        {
            _labelFilterEmails = null;
            RebuildAll();
            return;
        }

        try
        {
            Cursor = System.Windows.Input.Cursors.Wait;

            if (realLabels.Count > 0)
            {
                var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var res in realLabels)
                    emails.UnionWith(await MemberEmailsAsync(res));
                _labelFilterEmails = emails;
            }
            else
            {
                _labelFilterEmails = null;
            }

            // « Sans libellé » nécessite l'union des membres de TOUS les libellés.
            if (_filterNoLabel && _allLabelEmails == null)
            {
                var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var l in _services.CachedLabels)
                    all.UnionWith(await MemberEmailsAsync(l.ResourceName));
                _allLabelEmails = all;
            }
        }
        catch (GoogleSyncException ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Filtre par libellé",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Cursor = System.Windows.Input.Cursors.Arrow;
        }

        RebuildAll();
    }

    private async Task<HashSet<string>> MemberEmailsAsync(string resourceName)
    {
        if (!_labelMemberCache.TryGetValue(resourceName, out var set))
        {
            set = await _services.Contacts.GetLabelMemberEmailsAsync(resourceName);
            _labelMemberCache[resourceName] = set;
        }
        return set;
    }

    private void ResetFilter_Click(object sender, RoutedEventArgs e) => LabelFilter.ClearSelection();

    private void UpdateCount()
    {
        var total = _services.Adherents.Count;
        var shown = _filtered.Count;
        CountText.Text = shown == total
            ? $"{total} adhérent(s)"
            : $"{shown} sur {total} adhérent(s)";

        if (shown > 0)
            EmptyResults.Visibility = Visibility.Collapsed;
        else
        {
            EmptyResults.Text = total == 0
                ? "Aucun adhérent pour le moment."
                : "Aucun résultat pour cette recherche.";
            EmptyResults.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Adhérents filtrés (toutes pages) — pour l'export.</summary>
    private List<Adherent> FilteredAdherents() => _filtered;

    // ---- Suivi de la sélection (cases à cocher) ---------------------------

    private void Adherents_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (Adherent a in e.OldItems)
                a.PropertyChanged -= Adherent_PropertyChanged;
        if (e.NewItems != null)
            foreach (Adherent a in e.NewItems)
                a.PropertyChanged += Adherent_PropertyChanged;

        // Coalescé : plusieurs ajouts rapprochés (import, synchro) → une seule reconstruction.
        if (_rebuildQueued)
            return;
        _rebuildQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            _rebuildQueued = false;
            RefreshView();
        }));
    }

    private void Adherent_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Adherent.IsSelected))
            UpdateBulkBar();
    }

    private void UpdateBulkBar()
    {
        var count = _services.Adherents.Count(a => a.IsSelected);
        if (count > 0)
        {
            BulkCountText.Text = $"{count} contact(s) sélectionné(s)";
            BulkDeleteButton.Content = $"🗑 Supprimer ({count})";
            Animations.SlideDownIn(BulkBar);
        }
        else
        {
            Animations.FadeOutCollapse(BulkBar);
        }

        // La case d'en-tête reflète la sélection de la PAGE affichée.
        var pageSel = _pageItems.Count(a => a.IsSelected);
        SelectAllBox.IsChecked = pageSel == 0 ? false
            : pageSel == _pageItems.Count && _pageItems.Count > 0 ? true
            : null;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        // Sélectionne / désélectionne les adhérents de la page affichée.
        var check = SelectAllBox.IsChecked == true;
        foreach (var a in _pageItems.ToList())
            a.IsSelected = check;
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var a in _services.Adherents)
            a.IsSelected = false;
    }

    // ---- CRUD -------------------------------------------------------------

    private async void Importer_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this)!;

        var win = new ImportWindow(_services.Contacts) { Owner = owner };
        if (win.ShowDialog() != true)
            return;

        var labelResources = win.SelectedLabelResourceNames;
        var corrections = win.Corrections;
        const string ImportDetails = "Import manuel";

        var toPush = new List<Adherent>();   // nouveaux + modifiés → à envoyer à Google
        int added = 0, updated = 0, unchanged = 0;

        foreach (var p in win.ParsedContacts)
        {
            // 1. E-mail déjà présent → on met à jour les infos (on garde l'e-mail).
            var existing = FindByEmail(p.Email);
            if (existing != null)
            {
                var (oNom, oPre, oTel, oMail) = (existing.Nom, existing.Prenom, existing.Telephone, existing.Email);
                if (ApplyImportedFields(existing, p))
                {
                    updated++;
                    toPush.Add(existing);
                    _services.LogContactModification(existing, oNom, oPre, oTel, oMail, ImportDetails);
                }
                else unchanged++;
                continue;
            }

            // 2. E-mail corrigé : si l'e-mail d'origine (mal écrit) existe déjà localement,
            //    on corrige cet e-mail au lieu de créer un doublon.
            if (corrections.TryGetValue(p.Email, out var original))
            {
                var origContact = FindByEmail(original);
                if (origContact != null)
                {
                    var (oNom, oPre, oTel, oMail) = (origContact.Nom, origContact.Prenom, origContact.Telephone, origContact.Email);
                    origContact.Email = p.Email;      // remplace l'e-mail par sa correction
                    ApplyImportedFields(origContact, p);
                    updated++;
                    toPush.Add(origContact);
                    _services.LogContactModification(origContact, oNom, oPre, oTel, oMail, ImportDetails);
                    continue;
                }
            }

            // 3. Nouveau contact.
            _services.Adherents.Add(p);
            added++;
            _services.LogContactActivity(Models.ActivityAction.Ajout, p, ImportDetails);
            toPush.Add(p);
        }

        _services.SaveAdherents();
        RefreshView();
        UpdateCount();

        // Envoi vers Google (création ou mise à jour) + libellés cibles.
        BatchResult push = default;
        if (toPush.Count > 0)
        {
            push = await ProgressRunner.RunAsync(owner, "Import des contacts…", toPush,
                async a =>
                {
                    if (string.IsNullOrEmpty(a.GoogleResourceName))
                        a.GoogleResourceName = await _services.Contacts.EnsureContactResourceAsync(a);
                    else
                        await _services.Contacts.UpdateContactAsync(a.GoogleResourceName, a);

                    foreach (var groupResource in labelResources)
                        await _services.Contacts.SetMembershipAsync(a.GoogleResourceName, groupResource, add: true);
                });
            _services.SaveAdherents();
            RefreshView();
            UpdateCount();

            // Historique : associations aux libellés cibles de l'import.
            if (labelResources.Count > 0)
            {
                var labelNames = _services.CachedLabels
                    .GroupBy(l => l.ResourceName, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First().Nom, StringComparer.Ordinal);
                foreach (var a in toPush)
                    foreach (var res in labelResources)
                        _services.LogContactActivity(Models.ActivityAction.Association, a,
                            $"{labelNames.GetValueOrDefault(res, res)} ({ImportDetails})");
            }
        }

        var pushNote = push.Failed > 0
            ? $"\n• {push.Failed} non synchronisé(s) avec Google (repris au prochain lancement)"
            : string.Empty;

        var msg = new StringBuilder();
        msg.Append($"Import terminé :\n• {added} ajouté(s)\n• {updated} mis à jour\n• {unchanged} inchangé(s){pushNote}");

        var hasIssues = win.DuplicateEmails.Count > 0 || win.IncompletePeople.Count > 0;
        if (hasIssues)
        {
            AppendList(msg, $"⚠ {win.DuplicateEmails.Count} e-mail(s) en double", win.DuplicateEmails);
            AppendList(msg, $"⚠ {win.IncompletePeople.Count} personne(s) aux informations incomplètes (non importée(s))",
                win.IncompletePeople);
        }
        else
        {
            msg.Append("\n\n✔ Tout s'est bien passé.");
        }

        MessageBox.Show(owner, msg.ToString(), "Import", MessageBoxButton.OK,
            hasIssues ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    /// <summary>Ajoute une section « titre » + liste à puces (tronquée au-delà de 15 éléments).</summary>
    private static void AppendList(StringBuilder sb, string title, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
            return;

        sb.Append($"\n\n{title} :");
        const int max = 15;
        foreach (var item in items.Take(max))
            sb.Append($"\n   • {item}");
        if (items.Count > max)
            sb.Append($"\n   … et {items.Count - max} autre(s)");
    }

    private Adherent? FindByEmail(string email)
        => string.IsNullOrWhiteSpace(email)
            ? null
            : _services.Adherents.FirstOrDefault(a =>
                string.Equals(a.Email, email, StringComparison.OrdinalIgnoreCase));

    /// <summary>Met à jour nom/prénom/téléphone depuis l'import (sans écraser par du vide). Renvoie true si modifié.</summary>
    private static bool ApplyImportedFields(Adherent target, Adherent source)
    {
        var changed = false;
        if (!string.IsNullOrWhiteSpace(source.Nom) && !string.Equals(target.Nom, source.Nom, StringComparison.Ordinal))
        { target.Nom = source.Nom; changed = true; }
        if (!string.IsNullOrWhiteSpace(source.Prenom) && !string.Equals(target.Prenom, source.Prenom, StringComparison.Ordinal))
        { target.Prenom = source.Prenom; changed = true; }
        if (!string.IsNullOrWhiteSpace(source.Telephone) && !string.Equals(target.Telephone, source.Telephone, StringComparison.Ordinal))
        { target.Telephone = source.Telephone; changed = true; }
        return changed;
    }

    private void Exporter_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this)!;

        // Si des contacts sont cochés, on exporte la sélection ; sinon le filtrage affiché.
        var selected = _services.Adherents.Where(a => a.IsSelected).ToList();
        var toExport = selected.Count > 0 ? selected : FilteredAdherents();

        if (toExport.Count == 0)
        {
            MessageBox.Show(owner, "Aucun contact à exporter.", "Export",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exporter les contacts en CSV",
            Filter = "Fichier CSV (*.csv)|*.csv",
            FileName = $"contacts_{DateTime.Now:yyyy-MM-dd}.csv",
            DefaultExt = ".csv"
        };
        if (dlg.ShowDialog(owner) != true)
            return;

        try
        {
            CsvContactExporter.Export(dlg.FileName, toExport);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, $"Impossible d'écrire le fichier :\n{ex.Message}", "Export",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var portee = selected.Count > 0 ? "sélectionné(s)" : "au total";
        MessageBox.Show(owner,
            $"{toExport.Count} contact(s) {portee} exporté(s) vers :\n{dlg.FileName}",
            "Export", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void Ajouter_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AdherentEditWindow(null, _services.Contacts) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true)
            return;

        var nouvel = dialog.Adherent;
        _services.Adherents.Add(nouvel);
        _services.SaveAdherents();
        _services.LogContactActivity(Models.ActivityAction.Ajout, nouvel);
        RefreshView();

        var owner = Window.GetWindow(this)!;
        var result = await ProgressRunner.RunBusyAsync(owner, "Ajout du contact…",
            () => PushNewContactCoreAsync(nouvel, dialog.SelectedLabelResourceNames));
        ReportSingle(owner, result);
    }

    private async Task PushNewContactCoreAsync(Adherent adherent, IReadOnlyList<string> labelResourceNames)
    {
        // Création (ou rapprochement) du contact Google + mémorisation de sa ressource.
        var resource = await _services.Contacts.EnsureContactResourceAsync(adherent);
        adherent.GoogleResourceName = resource;
        _services.SaveAdherents();

        foreach (var groupResource in labelResourceNames)
            await _services.Contacts.SetMembershipAsync(resource, groupResource, add: true);

        // Libellé par défaut si la synchro auto est activée dans les Paramètres.
        if (_services.Settings.SyncGoogleEnabled &&
            !string.IsNullOrWhiteSpace(_services.Settings.GmailLabel))
            await _services.Contacts.AddToLabelAsync(adherent, _services.Settings.GmailLabel);
    }

    /// <summary>Affiche une éventuelle erreur Google après une action unitaire.</summary>
    private static void ReportSingle(Window owner, BatchResult result)
    {
        if (result.Failed > 0)
            MessageBox.Show(owner, result.LastError, "Synchronisation Google",
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void Grid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Grid.CurrentItem is Adherent a)
            Modifier(a);
    }

    private void Modifier_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Adherent a)
            Modifier(a);
    }

    private async void Modifier(Adherent adherent)
    {
        var dialog = new AdherentEditWindow(adherent) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true)
            return;

        // Capture des valeurs avant modification pour l'historique.
        var (oNom, oPre, oTel, oMail) = (adherent.Nom, adherent.Prenom, adherent.Telephone, adherent.Email);

        adherent.CopyFrom(dialog.Adherent);
        _services.SaveAdherents();
        _services.LogContactModification(adherent, oNom, oPre, oTel, oMail, "Modification");
        RefreshView();

        var owner = Window.GetWindow(this)!;
        var result = await ProgressRunner.RunBusyAsync(owner, "Mise à jour du contact…",
            () => PushUpdateCoreAsync(adherent));
        ReportSingle(owner, result);
    }

    /// <summary>Répercute une modification locale vers Google Contacts. Peut lever GoogleSyncException.</summary>
    private async Task PushUpdateCoreAsync(Adherent adherent)
    {
        if (string.IsNullOrEmpty(adherent.GoogleResourceName))
        {
            // Pas encore lié : on le crée dans Google.
            adherent.GoogleResourceName = await _services.Contacts.EnsureContactResourceAsync(adherent);
            _services.SaveAdherents();
        }
        else
        {
            await _services.Contacts.UpdateContactAsync(adherent.GoogleResourceName, adherent);
        }
    }

    /// <summary>Supprime le contact localement ET dans Google Contacts (s'il y est lié). Peut lever GoogleSyncException.</summary>
    private async Task DeleteContactCoreAsync(Adherent a)
    {
        _services.Adherents.Remove(a);
        _services.SaveAdherents();
        _services.LogContactActivity(Models.ActivityAction.Suppression, a);

        if (!string.IsNullOrEmpty(a.GoogleResourceName))
            await _services.Contacts.DeleteContactAsync(a.GoogleResourceName);
    }

    // ---- Suppression unitaire + gestion des libellés (⋮) ------------------

    private async void Supprimer_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Adherent a)
            return;

        var owner = Window.GetWindow(this)!;
        if (!ConfirmWindow.Ask(owner, "Supprimer le contact",
                $"Supprimer définitivement {a.Prenom} {a.Nom} ?\nLe contact sera aussi retiré de Google Contacts."))
            return;

        var result = await ProgressRunner.RunBusyAsync(owner, "Suppression du contact…",
            () => DeleteContactCoreAsync(a));
        ReportSingle(owner, result);
    }

    private void PlusOptions_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Adherent a)
            return;

        var window = new ManageLabelsWindow(_services, a) { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    private async void BulkDelete_Click(object sender, RoutedEventArgs e)
    {
        var selected = _services.Adherents.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0)
            return;

        var owner = Window.GetWindow(this)!;
        if (!ConfirmWindow.Ask(owner, "Supprimer les contacts",
                $"Supprimer définitivement les {selected.Count} contact(s) sélectionné(s) ?\nIls seront aussi retirés de Google Contacts."))
            return;

        var result = await ProgressRunner.RunAsync(
            owner, "Suppression des contacts…", selected, DeleteContactCoreAsync);

        UpdateBulkBar();

        if (result.Failed > 0)
            MessageBox.Show(owner,
                $"{result.Ok} contact(s) supprimé(s), {result.Failed} en erreur.\n\n{result.LastError}",
                "Suppression", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
