using System.IO;
using System.Windows;
using BadmintonClub.Models;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

public partial class ImportWindow : Window
{
    private readonly GoogleContactsService _contacts;
    private string? _selectedFilePath;

    /// <summary>Contacts issus de l'import (tous ont un e-mail).</summary>
    public List<Adherent> ParsedContacts { get; private set; } = new();

    /// <summary>Ressources des libellés cibles choisis.</summary>
    public IReadOnlyList<string> SelectedLabelResourceNames { get; private set; } = new List<string>();

    public ImportWindow(GoogleContactsService contacts)
    {
        InitializeComponent();
        _contacts = contacts;

        LabelsSelect.Placeholder = "Choisir des libellés";
        LabelsSelect.SetEmptyText("Aucun libellé disponible.");

        Loaded += async (_, _) => await LoadLabelsAsync();
    }

    private async Task LoadLabelsAsync()
    {
        try
        {
            var labels = await _contacts.ListLabelsAsync();
            LabelsSelect.SetOptions(labels.Select(l => new CheckOption
            {
                Text = l.Nom,
                Tag = l.ResourceName
            }));
        }
        catch (GoogleSyncException)
        {
            LabelsSelect.SetEmptyText("Libellés indisponibles (hors ligne ?).");
        }
    }

    // ---- Bascule de mode --------------------------------------------------

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (FilePanel == null || TextPanel == null)
            return;
        var fileMode = ModeFile.IsChecked == true;
        FilePanel.Visibility = fileMode ? Visibility.Visible : Visibility.Collapsed;
        TextPanel.Visibility = fileMode ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---- Zone de dépôt de fichier ----------------------------------------

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            SetFile(files[0]);
    }

    private void DropZone_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choisir un fichier",
            Filter = "Fichiers Excel/CSV (*.xlsx;*.csv)|*.xlsx;*.csv|Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv|Tous les fichiers (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) == true)
            SetFile(dlg.FileName);
    }

    private void SetFile(string path)
    {
        _selectedFilePath = path;
        DropHint.Text = $"Fichier sélectionné :\n{Path.GetFileName(path)}";
    }

    // ---- Import -----------------------------------------------------------

    private async void Importer_Click(object sender, RoutedEventArgs e)
    {
        var fileMode = ModeFile.IsChecked == true;
        if (fileMode && string.IsNullOrEmpty(_selectedFilePath))
        {
            Warn("Choisissez d'abord un fichier (Excel ou CSV).");
            return;
        }

        // Capture des entrées (les contrôles sont lus sur le thread UI avant le traitement).
        var filePath = _selectedFilePath;
        var emailsText = EmailsBox.Text;

        List<Adherent> contacts;
        List<Adherent> invalid;

        // Analyse + vérification des e-mails sous un petit chargement (traitement en arrière-plan).
        var progress = new ProgressWindow { Owner = this };
        progress.SetupIndeterminate("Vérification des e-mails…");
        IsEnabled = false;
        progress.Show();
        try
        {
            (contacts, invalid) = await Task.Run(() =>
            {
                var list = fileMode
                    ? (Path.GetExtension(filePath!).ToLowerInvariant() == ".xlsx"
                        ? ExcelContactImporter.Parse(filePath!)
                        : CsvContactImporter.Parse(filePath!))
                    : ParseEmails(emailsText);
                var inv = list.Where(c => !EmailValidator.IsValid(c.Email)).ToList();
                return (list, inv);
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Impossible de lire la source :\n{ex.Message}", "Import",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally
        {
            IsEnabled = true;
            progress.Close();
        }

        if (contacts.Count == 0)
        {
            Warn("Aucun contact avec e-mail à importer.");
            return;
        }

        // Correction des adresses douteuses.
        if (invalid.Count > 0)
        {
            var validation = new EmailValidationWindow(invalid) { Owner = this };
            if (validation.ShowDialog() != true)
                return; // Import annulé : on reste sur la fenêtre d'import.

            // Corrections appliquées ; on écarte les adresses encore invalides.
            contacts = contacts.Where(c => EmailValidator.IsValid(c.Email)).ToList();
            if (contacts.Count == 0)
            {
                Warn("Aucune adresse e-mail valide après correction.");
                return;
            }
        }

        ParsedContacts = contacts;
        SelectedLabelResourceNames = LabelsSelect.SelectedTags.OfType<string>().ToList();
        DialogResult = true;
    }

    /// <summary>
    /// Extrait les e-mails d'un texte collé, une adresse PAR LIGNE (les virgules éventuelles
    /// sont conservées car ce sont souvent des fautes de frappe à corriger).
    /// </summary>
    private static List<Adherent> ParseEmails(string text)
    {
        var result = new List<Adherent>();
        if (string.IsNullOrWhiteSpace(text))
            return result;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        foreach (var raw in lines)
        {
            var token = raw.Trim().Trim('"', '\'');
            if (token.Length == 0)
                continue;

            // Format « Nom <email> » éventuel.
            var lt = token.IndexOf('<');
            var gt = token.IndexOf('>');
            if (lt >= 0 && gt > lt)
                token = token.Substring(lt + 1, gt - lt - 1).Trim();

            // On ne garde que les lignes qui ressemblent à une tentative d'e-mail.
            if (!token.Contains('@'))
                continue;
            if (!seen.Add(token))
                continue;

            result.Add(new Adherent { Email = token });
        }

        return result;
    }

    private void Annuler_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Warn(string message)
        => MessageBox.Show(this, message, "Import", MessageBoxButton.OK, MessageBoxImage.Warning);
}
