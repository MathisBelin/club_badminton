using System.Windows;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

/// <summary>
/// Rappelle à l'utilisateur d'activer, à la main dans Google Forms, les réglages non pilotables par
/// l'API : « Autoriser la modification des réponses » et « Limiter à une réponse ».
/// </summary>
public partial class FormSettingsReminderWindow : Window
{
    private readonly string _editUrl;

    public FormSettingsReminderWindow(string editUrl = "")
    {
        InitializeComponent();
        _editUrl = editUrl;
        if (string.IsNullOrWhiteSpace(editUrl))
            OpenBtn.Visibility = Visibility.Collapsed;
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_editUrl))
            BrowserService.Open(_editUrl);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
