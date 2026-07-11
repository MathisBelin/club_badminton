using System.Windows;
using System.Windows.Controls;
using BadmintonClub.Helpers;
using BadmintonClub.Services;
using BadmintonClub.Views;

namespace BadmintonClub;

public partial class MainWindow : Window
{
    private readonly AppServices _services = new();

    private readonly ContactsView _contactsView;
    private readonly LabelsView _labelsView;
    private readonly AssociationView _associationView;
    private readonly PendingView _pendingView;
    private readonly EmailView _emailView;
    private readonly SheetsView _sheetsView;
    private readonly AutoSyncView _autoSyncView;
    private readonly HistoryView _historyView;
    private readonly SettingsView _settingsView;

    public MainWindow()
    {
        InitializeComponent();

        _contactsView = new ContactsView(_services);
        _labelsView = new LabelsView(_services);
        _associationView = new AssociationView(_services);
        _pendingView = new PendingView(_services);
        _emailView = new EmailView(_services);
        _sheetsView = new SheetsView(_services);
        _autoSyncView = new AutoSyncView(_services);
        _historyView = new HistoryView(_services);
        _settingsView = new SettingsView(_services);

        // Depuis la page Libellés : ouvrir la page Association filtrée sur le libellé cliqué.
        _labelsView.OpenAssociationRequested += resourceName =>
        {
            NavAssociation.IsChecked = true;
            _associationView.ShowForLabel(resourceName);
        };

        // Depuis la page Association : ouvrir « Personnes en attente » filtré sur le libellé courant.
        _associationView.OpenPendingRequested += resourceName =>
        {
            NavPending.IsChecked = true;
            _pendingView.ShowForLabel(resourceName);
        };

        // Depuis la modale d'alerte d'une synchro : idem, filtré sur le libellé de la synchro.
        _autoSyncView.OpenPendingRequested += resourceName =>
        {
            NavPending.IsChecked = true;
            _pendingView.ShowForLabel(resourceName);
        };

        // Sélection initiale : Contacts (le contenu est prêt derrière l'écran de connexion).
        NavContacts.IsChecked = true;

        var version = $"v{Services.UpdateService.CurrentVersion}";
        VersionText.Text = version;
        LoginVersionText.Text = $"Club de Badminton — {version}";

        _statusTimer.Tick += (_, _) => OnTick();
        _statusTimer.Start();

        Loaded += async (_, _) => await StartupAsync();
    }

    private readonly System.Windows.Threading.DispatcherTimer _statusTimer =
        new() { Interval = TimeSpan.FromSeconds(1) };

    private static bool IsOnline() => System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();

    /// <summary>Tick 1 s : rafraîchit l'affichage, lance les synchros dues, met à jour la cloche.</summary>
    private void OnTick()
    {
        var appVisible = AppRoot.Visibility == Visibility.Visible;
        var online = IsOnline();
        OfflineWarning.Visibility = appVisible && !online ? Visibility.Visible : Visibility.Collapsed;

        if (!appVisible)
        {
            NotifArea.Visibility = Visibility.Collapsed;
            return;
        }

        _services.TouchSyncs();               // rafraîchit minuteurs / états (bindings)
        if (online)
            _services.RunDueSyncs();           // lance les synchros échues (concurrentes)

        UpdateNotif();
    }

    /// <summary>Met à jour la pastille de la cloche (nombre de synchros en marche).</summary>
    private void UpdateNotif()
    {
        var count = _services.AutoSyncs.Count(s => s.Enabled);
        NotifArea.Visibility = Visibility.Visible;
        NotifBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NotifCount.Text = count.ToString();

        // Pastille jaune si au moins une synchro active a des inscriptions non finalisées.
        var hasIncomplete = _services.AutoSyncs.Any(s => s.Enabled && s.HasIncomplete);
        NotifWarnBadge.Visibility = hasIncomplete ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Ouvre la page « Inscriptions non finalisées » filtrée sur le libellé de la synchro.</summary>
    private void NotifPending_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.Documents.Hyperlink)?.DataContext is Models.AutoSyncConfig c)
        {
            NotifPopup.IsOpen = false;
            NavPending.IsChecked = true;
            _pendingView.ShowForLabel(c.LabelResourceName);
        }
    }

    /// <summary>Remplit la liste du popup (les minuteurs s'actualisent ensuite par binding).</summary>
    private void RefreshNotifList()
    {
        var running = _services.AutoSyncs.Where(s => s.Enabled).ToList();
        NotifList.ItemsSource = running;
        NotifEmpty.Visibility = running.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NotifButton_Click(object sender, RoutedEventArgs e)
    {
        NotifPopup.IsOpen = !NotifPopup.IsOpen;
        if (NotifPopup.IsOpen)
            RefreshNotifList();
    }

    private void Suspend_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Models.AutoSyncConfig c)
        {
            _services.StopSync(c);
            RefreshNotifList();
            UpdateNotif();
        }
    }

    /// <summary>Synchronise immédiatement une seule synchro (bouton logo de sa ligne).</summary>
    private void SyncOne_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Models.AutoSyncConfig c)
            return;
        if (!IsOnline())
        {
            MessageBox.Show(this, "Pas de connexion Internet : la synchronisation est en pause.",
                "Synchronisation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!c.IsImporting)
            _ = _services.RunSyncNowAsync(c);
    }

    private async Task CheckForUpdateAsync()
    {
        var info = await Services.UpdateService.CheckAsync();
        if (info == null)
            return;

        var choix = MessageBox.Show(this,
            $"Une nouvelle version ({info.Version}) est disponible.\n" +
            $"Vous utilisez la v{Services.UpdateService.CurrentVersion}.\n\n" +
            "Télécharger la mise à jour ?",
            "Mise à jour disponible", MessageBoxButton.YesNo, MessageBoxImage.Information);

        if (choix == MessageBoxResult.Yes)
            Services.BrowserService.Open(info.Url);
    }

    // ---- Connexion / déconnexion -----------------------------------------

    private async Task StartupAsync()
    {
        if (_services.HasStoredToken)
        {
            // Un jeton existe : on tente une connexion silencieuse.
            LoginStatus.Text = string.Empty;
            ConnexionButton.IsEnabled = false;
            try
            {
                var email = await _services.SignInAsync(switchAccount: false);
                await EnterAppAsync(email);
                return;
            }
            catch (Services.GoogleSyncException)
            {
                // Échec silencieux : on affiche l'écran de connexion.
            }
        }

        ShowLogin(null);
    }

    private System.Threading.CancellationTokenSource? _loginCts;

    private void ShowLogin(string? message)
    {
        AppRoot.Visibility = Visibility.Collapsed;
        LoginRoot.Visibility = Visibility.Visible;
        SetConnecting(false);
        LoginStatus.Text = message ?? string.Empty;
    }

    private void SetConnecting(bool connecting)
    {
        ConnexionButton.IsEnabled = !connecting;
        ConnexionButton.Visibility = connecting ? Visibility.Collapsed : Visibility.Visible;
        CancelLoginButton.Visibility = connecting ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Connexion_Click(object sender, RoutedEventArgs e)
    {
        _loginCts = new System.Threading.CancellationTokenSource();
        SetConnecting(true);
        LoginStatus.Foreground = System.Windows.Media.Brushes.Gray;
        LoginStatus.Text = "Connexion en cours… suivez les instructions dans le navigateur.";
        try
        {
            var email = await _services.SignInAsync(switchAccount: true, _loginCts.Token);
            await EnterAppAsync(email);
        }
        catch (Services.GoogleSyncException ex)
        {
            LoginStatus.Foreground = System.Windows.Media.Brushes.Firebrick;
            // Annulation volontaire : pas de message d'erreur.
            LoginStatus.Text = _loginCts.IsCancellationRequested ? string.Empty : ex.Message;
            SetConnecting(false);
        }
        finally
        {
            _loginCts?.Dispose();
            _loginCts = null;
        }
    }

    private void AnnulerConnexion_Click(object sender, RoutedEventArgs e)
    {
        _loginCts?.Cancel();
        LoginStatus.Foreground = System.Windows.Media.Brushes.Gray;
        LoginStatus.Text = "Annulation…";
    }

    private void Deconnexion_Click(object sender, RoutedEventArgs e)
    {
        _services.SignOut();
        ShowLogin(null);
    }

    private async Task EnterAppAsync(string email)
    {
        UpdateAccountUi(email);
        LoginRoot.Visibility = Visibility.Collapsed;
        AppRoot.Visibility = Visibility.Visible;
        await SyncAllAsync();

        // Réarme les synchros activées : elles s'exécutent tout de suite puis toutes les 5 min
        // (déclenchées par le tick de statut, tant que l'app est visible et en ligne).
        _services.ArmEnabledSyncs();

        _ = CheckForUpdateAsync(); // en arrière-plan, non bloquant
    }

    private async Task SyncAllAsync()
    {
        // Réinitialise l'affichage/les caches pour repartir du compte courant.
        _contactsView.ResetView();
        _labelsView.ResetView();
        _sheetsView.ResetView();

        await _contactsView.AutoSyncContactsAsync();
        await _labelsView.AutoLoadAsync();
        await _sheetsView.AutoSyncAsync();
    }

    private void UpdateAccountUi(string email)
        => AccountEmailText.Text = string.IsNullOrWhiteSpace(email) ? "Non connecté" : email;

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (ContentHost == null || sender is not RadioButton rb)
            return;

        UserControl? view = (rb.Tag as string) switch
        {
            "contacts" => _contactsView,
            "labels" => _labelsView,
            "association" => _associationView,
            "pending" => _pendingView,
            "email" => _emailView,
            "sheets" => _sheetsView,
            "autosync" => _autoSyncView,
            "history" => _historyView,
            "settings" => _settingsView,
            _ => null
        };

        if (view == null)
            return;

        // Chaque vue peut se rafraîchir à l'affichage.
        (view as IActivableView)?.OnActivated();

        ContentHost.Content = view;
        Animations.ContentIn(view);
    }
}

/// <summary>Vues qui souhaitent réagir quand elles deviennent visibles.</summary>
public interface IActivableView
{
    void OnActivated();
}
