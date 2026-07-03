using System.Windows;
using System.Windows.Threading;
using BadmintonClub.Services;
using BadmintonClub.Views;

namespace BadmintonClub.Helpers;

/// <summary>Résultat d'un traitement par lot.</summary>
public readonly record struct BatchResult(int Ok, int Failed, string? LastError);

/// <summary>
/// Exécute une action asynchrone sur une liste d'éléments en affichant une
/// fenêtre de progression (« X / N »). Les erreurs Google par élément sont
/// comptées sans interrompre le lot.
/// </summary>
public static class ProgressRunner
{
    /// <summary>Exécute une action unique en affichant un petit chargement indéterminé.</summary>
    public static async Task<BatchResult> RunBusyAsync(Window owner, string title, Func<Task> action)
    {
        var progress = new ProgressWindow { Owner = owner };
        progress.SetupIndeterminate(title);

        owner.IsEnabled = false;
        progress.Show();

        try
        {
            await action();
            return new BatchResult(1, 0, null);
        }
        catch (GoogleSyncException ex)
        {
            return new BatchResult(0, 1, ex.Message);
        }
        finally
        {
            owner.IsEnabled = true;
            progress.Close();
        }
    }

    public static async Task<BatchResult> RunAsync<T>(
        Window owner, string title, IReadOnlyList<T> items, Func<T, Task> action)
    {
        var progress = new ProgressWindow { Owner = owner };
        progress.Setup(title, items.Count);

        owner.IsEnabled = false;
        progress.Show();

        var ok = 0;
        var failed = 0;
        string? lastError = null;

        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                try
                {
                    await action(items[i]);
                    ok++;
                }
                catch (GoogleSyncException ex)
                {
                    failed++;
                    lastError = ex.Message;
                }

                progress.Report(i + 1);
                // Laisse l'UI se rafraîchir (utile pour les éléments traités très vite).
                await Dispatcher.Yield(DispatcherPriority.Background);
            }
        }
        finally
        {
            owner.IsEnabled = true;
            progress.Close();
        }

        return new BatchResult(ok, failed, lastError);
    }
}
