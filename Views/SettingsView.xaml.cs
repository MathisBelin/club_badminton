using System.Windows;
using System.Windows.Controls;
using BadmintonClub.Models;
using BadmintonClub.Services;
using Microsoft.Win32;

namespace BadmintonClub.Views;

public partial class SettingsView : UserControl, IActivableView
{
    private readonly AppServices _services;

    public SettingsView(AppServices services)
    {
        InitializeComponent();
        _services = services;
        LoadFromSettings();
    }

    public void OnActivated() => LoadFromSettings();

    private void LoadFromSettings()
    {
        var s = _services.Settings;
        LabelBox.Text = s.GmailLabel;
        ClubEmailBox.Text = s.ClubEmail;
        SheetUrlBox.Text = s.GoogleSheetUrl;
        JsonPathBox.Text = s.AdherentsJsonPath;
        SyncCheck.IsChecked = s.SyncGoogleEnabled;
        DefaultPathHint.Text = $"Si vide : {AppPaths.DefaultAdherentsFile}";
        LoadBrowsers();
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

    private void Parcourir_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Fichier des adhérents",
            Filter = "Fichier JSON (*.json)|*.json",
            FileName = string.IsNullOrWhiteSpace(JsonPathBox.Text) ? "adherents.json" : JsonPathBox.Text,
            OverwritePrompt = false,
            CheckPathExists = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            JsonPathBox.Text = dialog.FileName;
    }

    private void Enregistrer_Click(object sender, RoutedEventArgs e)
    {
        var clubEmail = ClubEmailBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(clubEmail) && !IsValidEmail(clubEmail))
        {
            MessageBox.Show(Window.GetWindow(this), "L'adresse e-mail du club n'est pas valide.",
                "E-mail invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var updated = new AppSettings
        {
            GmailLabel = LabelBox.Text.Trim(),
            ClubEmail = clubEmail,
            GoogleSheetUrl = SheetUrlBox.Text.Trim(),
            AdherentsJsonPath = JsonPathBox.Text.Trim(),
            SyncGoogleEnabled = SyncCheck.IsChecked == true,
            BrowserPath = (BrowserCombo.SelectedItem as BrowserInfo)?.ExecutablePath ?? string.Empty
        };

        _services.ApplySettings(updated);

        MessageBox.Show(Window.GetWindow(this), "Paramètres enregistrés.", "Paramètres",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }
}
