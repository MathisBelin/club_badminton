using System.Windows;
using BadmintonClub.Models;

namespace BadmintonClub.Views;

/// <summary>
/// Liste (lecture seule) des personnes associées au libellé lors de la dernière exécution d'une
/// synchro. Les lignes en jaune ne sont passées que grâce à l'option « associer les personnes connues ».
/// </summary>
public partial class SyncTraceWindow : Window
{
    public SyncTraceWindow(AutoSyncConfig config)
    {
        InitializeComponent();

        HeaderText.Text = $"Synchronisation « {config.Name} »   —   libellé « {config.LabelName} »";

        var trace = config.Trace
            .OrderByDescending(t => t.ViaKnown)
            .ThenBy(t => t.Nom, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        Grid.ItemsSource = trace;

        var viaKnown = trace.Count(t => t.ViaKnown);
        HintText.Text = trace.Count == 0
            ? "Aucune personne associée lors de la dernière exécution."
            : $"{trace.Count} personne(s) associée(s) au libellé lors de la dernière exécution" +
              (viaKnown > 0 ? $", dont {viaKnown} via l'option « personnes connues »." : ".");
    }

    private void Fermer_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
