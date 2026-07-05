using System.IO;
using System.Windows;
using System.Windows.Media;
using BadmintonClub.Models;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

public partial class ImportWindow : Window
{
    private readonly GoogleContactsService _contacts;
    private string? _selectedFilePath;
    private List<string[]>? _fileRows; // lignes du fichier chargé (null si aucun / illisible)

    /// <summary>Contacts issus de l'import (tous ont un e-mail).</summary>
    public List<Adherent> ParsedContacts { get; private set; } = new();

    /// <summary>Ressources des libellés cibles choisis.</summary>
    public IReadOnlyList<string> SelectedLabelResourceNames { get; private set; } = new List<string>();

    /// <summary>E-mails corrigés → e-mail d'origine (mal écrit), pour fusion à l'import.</summary>
    public IReadOnlyDictionary<string, string> Corrections { get; private set; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static readonly Brush OkBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
    private static readonly Brush ErrBrush = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
    private static readonly Brush WarnBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0x86, 0x0B));

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
        if (FileArea == null || TextPanel == null)
            return;
        var fileMode = ModeFile.IsChecked == true;
        FileArea.Visibility = fileMode ? Visibility.Visible : Visibility.Collapsed;
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

    private async void SetFile(string path)
    {
        _selectedFilePath = path;
        _fileRows = null;
        var name = Path.GetFileName(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();

        // Format non pris en charge → dropzone rouge.
        if (ext != ".xlsx" && ext != ".csv")
        {
            SetDropState(false, "❌", $"Format non pris en charge : {name}\n(attendu : .xlsx ou .csv)");
            ColumnConfigPanel.Visibility = Visibility.Collapsed;
            return;
        }

        // État de chargement (lecture en arrière-plan pour ne pas figer l'interface).
        ColumnConfigPanel.Visibility = Visibility.Collapsed;
        FilePanel.IsEnabled = false;
        DropProgress.Visibility = Visibility.Visible;
        SetDropState(null, "⏳", $"Chargement de {name}…");

        List<string[]>? rows;
        try
        {
            rows = await Task.Run(() =>
                ext == ".xlsx" ? ExcelContactImporter.ReadRows(path) : CsvContactImporter.ReadRows(path));
        }
        catch
        {
            rows = null;
        }
        finally
        {
            DropProgress.Visibility = Visibility.Collapsed;
            FilePanel.IsEnabled = true;
        }

        _fileRows = rows;
        if (_fileRows == null || _fileRows.Count == 0)
        {
            SetDropState(false, "❌", $"Fichier illisible ou vide : {name}");
            ColumnConfigPanel.Visibility = Visibility.Collapsed;
            return;
        }

        // Fichier valide → dropzone verte + panneau de colonnes.
        SetDropState(true, "✅", name);
        ColumnConfigPanel.Visibility = Visibility.Visible;
        StartRowBox.Text = "1";
        EndRowBox.Text = string.Empty;
        TestBorder.Visibility = Visibility.Collapsed;
        AutoDetectAndFill(showMessage: false); // pré-remplissage silencieux
    }

    /// <summary>Colore la dropzone : true = vert (ok), false = rouge (erreur), null = neutre (chargement).</summary>
    private void SetDropState(bool? ok, string icon, string hint)
    {
        DropRect.Stroke = ok == true ? OkBrush : ok == false ? ErrBrush
            : new SolidColorBrush(Color.FromRgb(0x9C, 0xB8, 0x9C));
        DropRect.Fill = new SolidColorBrush(ok == true ? Color.FromRgb(0xEA, 0xF5, 0xEA)
            : ok == false ? Color.FromRgb(0xFD, 0xEC, 0xEA) : Color.FromRgb(0xF7, 0xFA, 0xF7));
        DropIcon.Text = icon;
        DropHint.Foreground = ok == true ? OkBrush : ok == false ? ErrBrush
            : new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
        DropHint.Text = hint;
    }

    // ---- Colonnes : détection auto + test --------------------------------

    private void AutoFill_Click(object sender, RoutedEventArgs e) => AutoDetectAndFill(showMessage: true);

    private void AutoDetectAndFill(bool showMessage)
    {
        if (_fileRows == null)
            return;

        var m = CsvContactImporter.DetectColumns(_fileRows);
        string? L(string k) => m != null && m.Columns.TryGetValue(k, out var i) ? CsvContactImporter.ColumnLetter(i) : null;
        var nom = L("nom");
        var prenom = L("prenom");
        var tel = L("tel");
        var mail = L("email");

        if (nom != null) ColNomBox.Text = nom;
        if (prenom != null) ColPrenomBox.Text = prenom;
        if (tel != null) ColTelBox.Text = tel;
        if (mail != null) ColEmailBox.Text = mail;

        if (!showMessage)
            return;

        var found = new List<string>();
        var missing = new List<string>();
        (nom != null ? found : missing).Add("Nom");
        (prenom != null ? found : missing).Add("Prénom");
        (tel != null ? found : missing).Add("Téléphone");
        (mail != null ? found : missing).Add("E-mail");

        if (found.Count == 4)
            ShowTest("✔ Les 4 colonnes ont été détectées et renseignées.", OkBrush);
        else if (found.Count == 0)
            ShowTest("✘ Aucune colonne détectée (en-tête introuvable dans le fichier).", ErrBrush);
        else
            ShowTest($"⚠ Détectées : {string.Join(", ", found)}.\nÀ renseigner à la main : {string.Join(", ", missing)}.", WarnBrush);
    }

    private void Test_Click(object sender, RoutedEventArgs e)
    {
        if (_fileRows == null)
        {
            ShowTest("Aucun fichier chargé.", ErrBrush);
            return;
        }

        var (start, end, nom, prenom, tel, mail) = ReadColumnInputs();
        var rows = CsvContactImporter.SliceRows(_fileRows, start, end);
        var result = CsvContactImporter.CheckColumns(rows, nom, prenom, tel, mail);
        ShowTest(CsvContactImporter.BuildCheckMessage(result, nom, prenom, tel, mail),
            result.Ok ? OkBrush : ErrBrush);
    }

    private (int start, int end, string nom, string prenom, string tel, string mail) ReadColumnInputs()
    {
        var start = int.TryParse(StartRowBox.Text.Trim(), out var s) && s > 0 ? s : 1;
        var end = int.TryParse(EndRowBox.Text.Trim(), out var en) && en > 0 ? en : 0;
        return (start, end,
            ColNomBox.Text.Trim().ToUpperInvariant(),
            ColPrenomBox.Text.Trim().ToUpperInvariant(),
            ColTelBox.Text.Trim().ToUpperInvariant(),
            ColEmailBox.Text.Trim().ToUpperInvariant());
    }

    private void ShowTest(string text, Brush brush)
    {
        TestBorder.Visibility = Visibility.Visible;
        TestResult.Text = text;
        TestResult.Foreground = brush;
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

        // Capture des entrées (lues sur le thread UI avant le traitement).
        var filePath = _selectedFilePath;
        var emailsText = EmailsBox.Text;
        var rowsSnapshot = _fileRows;
        var (start, end, colNom, colPrenom, colTel, colEmail) = fileMode
            ? ReadColumnInputs()
            : (1, 0, string.Empty, string.Empty, string.Empty, string.Empty);

        List<Adherent> contacts;
        List<Adherent> invalid;

        var progress = new ProgressWindow { Owner = this };
        progress.SetupIndeterminate("Vérification des e-mails…");
        IsEnabled = false;
        progress.Show();
        try
        {
            (contacts, invalid) = await Task.Run(() =>
            {
                List<Adherent> list;
                if (!fileMode)
                {
                    list = ParseEmails(emailsText);
                }
                else if (rowsSnapshot != null && !string.IsNullOrWhiteSpace(colEmail))
                {
                    // Colonnes indiquées : on lit les lignes voulues et on mappe par lettre.
                    var sliced = CsvContactImporter.SliceRows(rowsSnapshot, start, end);
                    list = CsvContactImporter.BuildFromColumns(sliced,
                        CsvContactImporter.ColIndex(colNom),
                        CsvContactImporter.ColIndex(colPrenom),
                        CsvContactImporter.ColIndex(colTel),
                        CsvContactImporter.ColIndex(colEmail));
                }
                else
                {
                    // Repli : détection automatique par en-têtes.
                    list = Path.GetExtension(filePath!).ToLowerInvariant() == ".xlsx"
                        ? ExcelContactImporter.Parse(filePath!)
                        : CsvContactImporter.Parse(filePath!);
                }

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
            var originals = invalid.ToDictionary(a => a, a => a.Email);

            var validation = new EmailValidationWindow(invalid) { Owner = this };
            if (validation.ShowDialog() != true)
                return; // Import annulé : on reste sur la fenêtre d'import.

            var corr = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in invalid)
            {
                var original = originals[a];
                if (EmailValidator.IsValid(a.Email) &&
                    !string.Equals(a.Email, original, StringComparison.OrdinalIgnoreCase))
                    corr[a.Email] = original;
            }
            Corrections = corr;

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

            var lt = token.IndexOf('<');
            var gt = token.IndexOf('>');
            if (lt >= 0 && gt > lt)
                token = token.Substring(lt + 1, gt - lt - 1).Trim();

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
