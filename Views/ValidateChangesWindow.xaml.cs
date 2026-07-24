using System.Windows;

namespace BadmintonClub.Views;

/// <summary>Choix appliqué en bloc aux personnes dont les infos diffèrent de leur contact.</summary>
public enum ChangesChoice { Cancel, Update, Ignore }

/// <summary>
/// Demande, lors d'une validation groupée, s'il faut <b>mettre à jour</b> les contacts avec les
/// informations des réponses ou <b>garder l'état actuel</b>, pour toutes les personnes concernées.
/// </summary>
public partial class ValidateChangesWindow : Window
{
    private ChangesChoice _choice = ChangesChoice.Cancel;

    private ValidateChangesWindow(int count)
    {
        InitializeComponent();
        MessageText.Text = $"{count} personne(s) sélectionnée(s) ont des informations différentes de " +
                           "leur contact.\n\nQue faire de ces changements pour toutes ces personnes ?";
    }

    /// <summary>Affiche le choix. Renvoie l'option retenue (Cancel si la fenêtre est fermée).</summary>
    public static ChangesChoice Ask(Window? owner, int count)
    {
        var win = new ValidateChangesWindow(count) { Owner = owner };
        return win.ShowDialog() == true ? win._choice : ChangesChoice.Cancel;
    }

    private void Update_Click(object sender, RoutedEventArgs e) { _choice = ChangesChoice.Update; DialogResult = true; }
    private void Ignore_Click(object sender, RoutedEventArgs e) { _choice = ChangesChoice.Ignore; DialogResult = true; }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
