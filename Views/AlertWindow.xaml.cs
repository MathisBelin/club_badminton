using System.Windows;

namespace BadmintonClub.Views;

/// <summary>
/// Fenêtre d'alerte stylée d'un répondant (préinscriptions). Liste les alertes et propose
/// « 👁 Voir les changements » pour ouvrir le détail de la réponse. <see cref="Window.ShowDialog"/>
/// renvoie <c>true</c> si l'utilisateur veut voir les changements.
/// </summary>
public partial class AlertWindow : Window
{
    public AlertWindow(string personName, IReadOnlyList<string> alerts)
    {
        InitializeComponent();
        TitleText.Text = string.IsNullOrWhiteSpace(personName) ? "Alertes" : $"Alertes — {personName}";
        List.ItemsSource = alerts;
    }

    private void ViewChanges_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
