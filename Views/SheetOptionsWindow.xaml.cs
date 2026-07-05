using System.Windows;
using System.Windows.Controls;
using BadmintonClub.Helpers;
using BadmintonClub.Models;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

public partial class SheetOptionsWindow : Window
{
    private readonly GoogleSheetsService _sheets;
    private readonly SheetRecord _record;

    public SheetOptionsWindow(GoogleSheetsService sheets, SheetRecord record)
    {
        InitializeComponent();
        _sheets = sheets;
        _record = record;
        TitleText.Text = $"Classeur : {record.Nom}";
        Loaded += async (_, _) => await LoadSharingAsync();
    }

    private async Task LoadSharingAsync()
    {
        try
        {
            IsEnabled = false;
            var sharing = await _sheets.GetSharingAsync(_record.SpreadsheetId);
            LinkAccessCheck.IsChecked = sharing.LinkAccess;
            SelectRole(sharing.Role);
            LoadingText.Visibility = Visibility.Collapsed;
        }
        catch (GoogleSyncException ex)
        {
            LoadingText.Text = ex.Message;
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void SelectRole(string role)
    {
        foreach (var item in RoleCombo.Items.OfType<ComboBoxItem>())
        {
            if ((item.Tag as string) == role)
            {
                RoleCombo.SelectedItem = item;
                return;
            }
        }
        RoleCombo.SelectedIndex = 2; // Éditeur par défaut
    }

    private async void Appliquer_Click(object sender, RoutedEventArgs e)
    {
        var linkAccess = LinkAccessCheck.IsChecked == true;
        var role = (RoleCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "writer";

        var result = await ProgressRunner.RunBusyAsync(this, "Application du partage…",
            () => _sheets.SetSharingAsync(_record.SpreadsheetId, linkAccess, role));

        if (result.Failed > 0)
            MessageBox.Show(this, result.LastError, "Partage", MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            MessageBox.Show(this, "Partage mis à jour.", "Partage", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void Telecharger_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Télécharger le classeur en CSV",
            Filter = "Fichier CSV (*.csv)|*.csv",
            FileName = SanitizeFileName(_record.Nom) + ".csv",
            DefaultExt = ".csv"
        };
        if (dlg.ShowDialog(this) != true)
            return;

        var result = await ProgressRunner.RunBusyAsync(this, "Téléchargement du CSV…",
            () => _sheets.DownloadCsvAsync(_record.SpreadsheetId, dlg.FileName));

        if (result.Failed > 0)
            MessageBox.Show(this, result.LastError, "Téléchargement", MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            MessageBox.Show(this, $"CSV enregistré :\n{dlg.FileName}", "Téléchargement",
                MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void Modele_Click(object sender, RoutedEventArgs e)
    {
        AppPaths.EnsureModelsFolder();
        var dest = System.IO.Path.Combine(AppPaths.ModelsFolder, SanitizeFileName(_record.Nom) + ".xlsx");

        var result = await ProgressRunner.RunBusyAsync(this, "Enregistrement du modèle…",
            () => _sheets.DownloadXlsxAsync(_record.SpreadsheetId, dest));

        if (result.Failed > 0)
            MessageBox.Show(this, result.LastError, "Modèle", MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            MessageBox.Show(this, $"Modèle enregistré :\n{dest}\n\nIl apparaîtra dans « À partir d'un modèle » à la création d'un Sheet.",
                "Modèle", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Ouvrir_Click(object sender, RoutedEventArgs e) => BrowserService.Open(_record.Url);

    private void Fermer_Click(object sender, RoutedEventArgs e) => Close();

    private static string SanitizeFileName(string name)
    {
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
