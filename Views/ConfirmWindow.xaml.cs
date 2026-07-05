using System.Windows;
using System.Windows.Media;

namespace BadmintonClub.Views;

public partial class ConfirmWindow : Window
{
    private ConfirmWindow(string title, string message, string confirmText, string icon, bool danger)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmBtn.Content = confirmText;
        IconText.Text = icon;

        if (!danger)
        {
            IconBadge.Background = new SolidColorBrush(Color.FromRgb(0xEA, 0xF2, 0xEA));
            ConfirmBtn.Style = (Style)Application.Current.FindResource("PrimaryButton");
        }
    }

    /// <summary>Affiche une confirmation stylée. Renvoie true si l'utilisateur confirme.</summary>
    public static bool Ask(Window? owner, string title, string message,
        string confirmText = "Supprimer", string icon = "🗑", bool danger = true)
    {
        var win = new ConfirmWindow(title, message, confirmText, icon, danger) { Owner = owner };
        return win.ShowDialog() == true;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
