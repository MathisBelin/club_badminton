using System.Windows;
using System.Windows.Threading;
using BadmintonClub.Services;

namespace BadmintonClub;

public partial class App : Application
{
    public App()
    {
        // Filet de sécurité : toute exception non gérée est journalisée dans log_error.txt.
        // On couvre les 3 sources : thread UI, autres threads (dont crashs au démarrage), et tâches.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        // Le handler DataGrid_LostKeyboardFocus est câblé par l'EventSetter du style DataGrid (App.xaml).
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ErrorLogger.Log("Interface (thread UI)", e.Exception);
        MessageBox.Show(
            $"Une erreur inattendue est survenue :\n\n{e.Exception.Message}\n\n" +
            $"Le détail a été enregistré dans :\n{ErrorLogger.LogPath}",
            "Erreur",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // Erreur fatale (souvent au démarrage) : on journalise avant que l'appli ne se ferme.
        if (e.ExceptionObject is Exception ex)
            ErrorLogger.Log(e.IsTerminating ? "Fatale (démarrage/thread)" : "Non gérée (thread)", ex);
        else
            ErrorLogger.Log("Fatale (objet non-Exception)", e.ExceptionObject?.ToString() ?? "inconnu");
    }

    private void OnUnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        ErrorLogger.Log("Tâche en arrière-plan", e.Exception);
        e.SetObserved(); // évite que l'app soit tuée par une tâche non observée
    }

    /// <summary>Efface la sélection de cellule quand le focus quitte le tableau (clic à l'extérieur).</summary>
    private void DataGrid_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.DataGrid grid)
            return;

        // On ne désélectionne pas si le focus reste dans le tableau (navigation entre cellules,
        // clic sur un bouton d'action d'une ligne…).
        if (e.NewFocus is DependencyObject d && IsInside(d, grid))
            return;

        grid.UnselectAllCells();
        grid.CurrentCell = default;
    }

    private static bool IsInside(DependencyObject? node, DependencyObject ancestor)
    {
        while (node != null)
        {
            if (ReferenceEquals(node, ancestor))
                return true;
            node = System.Windows.Media.VisualTreeHelper.GetParent(node)
                   ?? System.Windows.LogicalTreeHelper.GetParent(node);
        }
        return false;
    }
}
