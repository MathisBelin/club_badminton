using System.Windows;
using System.Windows.Controls;

namespace BadmintonClub.Views;

public partial class SheetShareSettingsWindow : Window
{
    public bool LinkAccess { get; private set; }
    public string LinkRole { get; private set; } = "writer";

    public SheetShareSettingsWindow(bool linkAccess, string linkRole)
    {
        InitializeComponent();
        LinkAccessCheck.IsChecked = linkAccess;
        foreach (var item in RoleCombo.Items.OfType<ComboBoxItem>())
            if ((item.Tag as string) == linkRole)
                RoleCombo.SelectedItem = item;
    }

    private void Valider_Click(object sender, RoutedEventArgs e)
    {
        LinkAccess = LinkAccessCheck.IsChecked == true;
        LinkRole = (RoleCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "writer";
        DialogResult = true;
    }

    private void Annuler_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
