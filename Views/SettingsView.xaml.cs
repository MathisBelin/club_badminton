using System.Windows;
using System.Windows.Controls;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

public partial class SettingsView : UserControl, IActivableView
{
    private readonly AppServices _services;

    public SettingsView(AppServices services)
    {
        InitializeComponent();
        _services = services;
        Load();
    }

    public void OnActivated() => Load();

    private void Load()
    {
        LoadBrowsers();
        WebUrlBox.Text = _services.Settings.WebFormsUrl;
        WebKeyBox.Password = _services.Settings.WebFormsApiKey;
    }

    private void LoadBrowsers()
    {
        var items = new List<BrowserInfo> { new("Navigateur par défaut du système", string.Empty) };
        items.AddRange(BrowserService.GetInstalled());
        BrowserCombo.ItemsSource = items;

        var target = _services.Settings.BrowserPath;
        if (string.IsNullOrWhiteSpace(target))
            target = BrowserService.GetDefaultExecutablePath() ?? string.Empty;

        BrowserCombo.SelectedItem = items.FirstOrDefault(b => PathEquals(b.ExecutablePath, target)) ?? items[0];
    }

    private static bool PathEquals(string a, string b)
        => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    private void Enregistrer_Click(object sender, RoutedEventArgs e)
    {
        _services.UpdateBrowser((BrowserCombo.SelectedItem as BrowserInfo)?.ExecutablePath ?? string.Empty);

        // Liaison avec l'application web des formulaires d'inscription.
        _services.Settings.WebFormsUrl = WebUrlBox.Text.Trim();
        _services.Settings.WebFormsApiKey = WebKeyBox.Password.Trim();
        _services.SettingsService.Save(_services.Settings);
        MessageBox.Show(Window.GetWindow(this), "Paramètres enregistrés.", "Paramètres",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
