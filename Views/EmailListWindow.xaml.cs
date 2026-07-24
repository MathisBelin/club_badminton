using System.Windows;

namespace BadmintonClub.Views;

/// <summary>Fenêtre stylée affichant la liste des e-mails secondaires d'une personne (lecture seule).</summary>
public partial class EmailListWindow : Window
{
    public EmailListWindow(string personName, IReadOnlyList<string> emails)
    {
        InitializeComponent();
        SubtitleText.Text = string.IsNullOrWhiteSpace(personName)
            ? $"{emails.Count} adresse(s)"
            : $"{personName} — {emails.Count} adresse(s)";
        EmailsList.ItemsSource = emails;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
