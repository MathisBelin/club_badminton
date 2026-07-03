using System.Windows;
using System.Windows.Threading;

namespace BadmintonClub;

public partial class App : Application
{
    public App()
    {
        // Filet de sécurité : aucune exception non gérée ne doit fermer l'appli silencieusement.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Une erreur inattendue est survenue :\n\n{e.Exception.Message}",
            "Erreur",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
