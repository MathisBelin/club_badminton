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
}
