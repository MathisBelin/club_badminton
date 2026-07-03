using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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
    private ICollectionView _view = null!;

    // Filtre par libellé (recherche avancée).
    private HashSet<string>? _labelFilterEmails;
    private readonly Dictionary<string, HashSet<string>> _labelMemberCache = new(StringComparer.Ordinal);
    private bool _labelFilterLoaded;

    public ContactsView(AppServices services)
    {
        InitializeComponent();
        _services = services;

        _view = CollectionViewSource.GetDefaultView(_services.Adherents);
        _view.SortDescriptions.Add(new SortDescription(nameof(Adherent.Nom), ListSortDirection.Ascending));
        _view.Filter = FilterAdherent;
        Grid.ItemsSource = _view;

        LabelFilter.Placeholder = "Choisir des libellés";
        LabelFilter.SelectionChanged += LabelFilter_SelectionChanged;

        foreach (var a in _services.Adherents)
            a.PropertyChanged += Adherent_PropertyChanged;
        _services.Adherents.CollectionChanged += Adherents_CollectionChanged;

        UpdateCount();
    }

    public void OnActivated()
    {
        _view.Refresh();
        UpdateCount();
        UpdateBulkBar();
    }

    /// <summary>Réinitialise l'affichage et les caches (changement de compte).</summary>
    public void ResetView()
    {
        SearchBox.Text = string.Empty;
        AdvancedToggle.IsChecked = false;
        AdvancedPanel.Visibility = Visibility.Collapsed;

        _labelFilterEmails = null;
        _labelMemberCache.Clear();
        _labelFilterLoaded = false;
        LabelFilter.SetOptions(Enumerable.Empty<CheckOption>());

        _view?.Refresh();
        UpdateCount();
    }

    /// <summary>Synchronisation deux sens avec Google au lancement. Silencieuse.</summary>
    public async Task AutoSyncContactsAsync()
    {
        try
        {
            await _services.SyncContactsAsync();
            _view.Refresh();
            UpdateCount();
        }
        catch (GoogleSyncException)
        {
            // Silencieux : l'appli reste utilisable hors ligne avec la liste locale.
        }
    }

    // ---- Filtrage / comptage ---------------------------------------------

    private bool FilterAdherent(object obj)
    {
        if (obj is not Adherent a)
            return false;

        // Filtre par libellé (recherche avancée).
        if (_labelFilterEmails != null &&
            (string.IsNullOrWhiteSpace(a.Email) || !_labelFilterEmails.Contains(a.Email)))
            return false;

        var term = SearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(term))
            return true;

        return Contains(a.Nom) || Contains(a.Prenom) || Contains(a.Telephone) || Contains(a.Email);

        bool Contains(string? v) => v != null && v.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => _view?.Refresh();

    // ---- Recherche avancée : filtre par libellé --------------------------

    private async void Advanced_Toggled(object sender, RoutedEventArgs e)
    {
        var open = AdvancedToggle.IsChecked == true;
        AdvancedPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;

        if (open && !_labelFilterLoaded)
        {
            _labelFilterLoaded = true;
            try
            {
                var labels = await _services.GetLabelsAsync();
                LabelFilter.SetOptions(labels.Select(l => new CheckOption { Text = l.Nom, Tag = l.ResourceName }));
            }
            catch (GoogleSyncException)
            {
                LabelFilter.SetEmptyText("Libellés indisponibles (hors ligne ?).");
            }
        }
    }

    private async void LabelFilter_SelectionChanged(object? sender, EventArgs e)
    {
        var selected = LabelFilter.SelectedTags.OfType<string>().ToList();
        if (selected.Count == 0)
        {
            _labelFilterEmails = null;
            _view.Refresh();
            return;
        }

        try
        {
            Cursor = System.Windows.Input.Cursors.Wait;
            var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var res in selected)
            {
                if (!_labelMemberCache.TryGetValue(res, out var set))
                {
                    set = await _services.Contacts.GetLabelMemberEmailsAsync(res);
                    _labelMemberCache[res] = set;
                }
                emails.UnionWith(set);
            }
            _labelFilterEmails = emails;
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

        _view.Refresh();
    }

    private void ResetFilter_Click(object sender, RoutedEventArgs e) => LabelFilter.ClearSelection();

    private void UpdateCount() => CountText.Text = $"{_services.Adherents.Count} adhérent(s)";

    // ---- Suivi de la sélection (cases à cocher) ---------------------------

    private void Adherents_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (Adherent a in e.OldItems)
                a.PropertyChanged -= Adherent_PropertyChanged;
        if (e.NewItems != null)
            foreach (Adherent a in e.NewItems)
                a.PropertyChanged += Adherent_PropertyChanged;

        UpdateCount();
        UpdateBulkBar();
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

        SelectAllBox.IsChecked = count == 0 ? false
            : count == _services.Adherents.Count ? true
            : null;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        var check = SelectAllBox.IsChecked == true;
        foreach (var a in _services.Adherents)
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

        // Dédoublonnage par e-mail (contacts déjà présents + doublons dans la source).
        var existingEmails = new HashSet<string>(
            _services.Adherents.Where(a => !string.IsNullOrWhiteSpace(a.Email)).Select(a => a.Email),
            StringComparer.OrdinalIgnoreCase);

        var newContacts = new List<Adherent>();
        var skipped = 0;
        foreach (var p in win.ParsedContacts)
        {
            if (!existingEmails.Add(p.Email))
            {
                skipped++;
                continue;
            }
            _services.Adherents.Add(p);
            newContacts.Add(p);
        }

        if (newContacts.Count > 0)
            _services.SaveAdherents();
        _view.Refresh();
        UpdateCount();

        // Envoi vers Google + association aux libellés cibles (avec barre de progression).
        BatchResult push = default;
        if (newContacts.Count > 0)
        {
            push = await ProgressRunner.RunAsync(owner, "Import des contacts…", newContacts,
                async a =>
                {
                    a.GoogleResourceName = await _services.Contacts.EnsureContactResourceAsync(a);
                    foreach (var groupResource in labelResources)
                        await _services.Contacts.SetMembershipAsync(a.GoogleResourceName, groupResource, add: true);
                });
            _services.SaveAdherents();
            _view.Refresh();
            UpdateCount();
        }

        var pushNote = push.Failed > 0
            ? $"\n• {push.Failed} non synchronisé(s) avec Google (repris au prochain lancement)"
            : string.Empty;

        MessageBox.Show(owner,
            $"Import terminé :\n• {newContacts.Count} contact(s) importé(s)\n• {skipped} ignoré(s) (e-mail déjà présent){pushNote}",
            "Import", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Exporter_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this)!;

        // Si des contacts sont cochés, on exporte la sélection ; sinon tous.
        var selected = _services.Adherents.Where(a => a.IsSelected).ToList();
        var toExport = selected.Count > 0 ? selected : _services.Adherents.ToList();

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

    private void Associer_Click(object sender, RoutedEventArgs e)
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
        modal.ShowDialog();
    }

    private async void Ajouter_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AdherentEditWindow(null, _services.Contacts) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true)
            return;

        var nouvel = dialog.Adherent;
        _services.Adherents.Add(nouvel);
        _services.SaveAdherents();
        _view.Refresh();

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
        if (Grid.SelectedItem is Adherent a)
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

        adherent.CopyFrom(dialog.Adherent);
        _services.SaveAdherents();
        _view.Refresh();

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

        if (!string.IsNullOrEmpty(a.GoogleResourceName))
            await _services.Contacts.DeleteContactAsync(a.GoogleResourceName);
    }

    // ---- Suppression unitaire + gestion des libellés (⋮) ------------------

    private async void Supprimer_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Adherent a)
            return;

        var confirm = MessageBox.Show(Window.GetWindow(this),
            $"Supprimer {a.Prenom} {a.Nom} ?", "Confirmation",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        var owner = Window.GetWindow(this)!;
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

        var confirm = MessageBox.Show(Window.GetWindow(this),
            $"Supprimer les {selected.Count} contact(s) sélectionné(s) ?",
            "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        var owner = Window.GetWindow(this)!;
        var result = await ProgressRunner.RunAsync(
            owner, "Suppression des contacts…", selected, DeleteContactCoreAsync);

        UpdateBulkBar();

        if (result.Failed > 0)
            MessageBox.Show(owner,
                $"{result.Ok} contact(s) supprimé(s), {result.Failed} en erreur.\n\n{result.LastError}",
                "Suppression", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
