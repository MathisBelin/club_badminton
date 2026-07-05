using System.Windows;
using System.Windows.Controls;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

public partial class ManageTemplatesWindow : Window
{
    public ManageTemplatesWindow()
    {
        InitializeComponent();
        Reload();
    }

    private void Reload() => Grid.ItemsSource = MailTemplateStore.LoadAll();

    private void Ajouter_Click(object sender, RoutedEventArgs e)
    {
        var win = new EditTemplateWindow { Owner = this };
        if (win.ShowDialog() == true)
            Reload();
    }

    private void Modifier_Click(object sender, RoutedEventArgs e) => OpenEditor(sender);

    private void Grid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Grid.SelectedItem is MailTemplate t)
            OpenEditor(t);
    }

    private void OpenEditor(object sender)
    {
        if ((sender as FrameworkElement)?.DataContext is MailTemplate t)
            OpenEditor(t);
    }

    private void OpenEditor(MailTemplate template)
    {
        var win = new EditTemplateWindow(template) { Owner = this };
        if (win.ShowDialog() == true)
            Reload();
    }

    private void Supprimer_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not MailTemplate t)
            return;
        if (!ConfirmWindow.Ask(this, "Supprimer le modèle", $"Supprimer le modèle « {t.Name} » ?"))
            return;
        MailTemplateStore.Delete(t.Name);
        Reload();
    }

    private void Fermer_Click(object sender, RoutedEventArgs e) => Close();
}
