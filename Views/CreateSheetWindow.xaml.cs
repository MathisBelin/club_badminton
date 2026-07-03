using System.IO;
using System.Windows;
using System.Windows.Controls;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

public partial class CreateSheetWindow : Window
{
    private string? _templatePath;

    public SheetCreateOptions Options { get; private set; } = new();

    public CreateSheetWindow(string defaultName)
    {
        InitializeComponent();
        SheetNameBox.Text = defaultName;
        Loaded += (_, _) => { SheetNameBox.SelectAll(); SheetNameBox.Focus(); };
    }

    // ---- Zone de dépôt du modèle -----------------------------------------

    private void Drop_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Drop_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            SetTemplate(files[0]);
    }

    private void Drop_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choisir un modèle (Excel ou CSV)",
            Filter = "Excel/CSV (*.xlsx;*.csv)|*.xlsx;*.csv|Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv|Tous les fichiers (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) == true)
            SetTemplate(dlg.FileName);
    }

    private void SetTemplate(string path)
    {
        _templatePath = path;
        TemplateHint.Text = $"Modèle : {Path.GetFileName(path)}";
        ClearTemplateButton.Visibility = Visibility.Visible;
    }

    private void ClearTemplate_Click(object sender, RoutedEventArgs e)
    {
        _templatePath = null;
        TemplateHint.Text = "Aucun modèle — classeur vierge.\nGlissez un Excel/CSV ici ou cliquez pour parcourir.";
        ClearTemplateButton.Visibility = Visibility.Collapsed;
    }

    // ---- Validation -------------------------------------------------------

    private void Creer_Click(object sender, RoutedEventArgs e)
    {
        var name = SheetNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "Saisissez un nom de classeur.", "Créer un Sheet",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var role = (RoleCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "writer";

        Options = new SheetCreateOptions
        {
            Title = name,
            LinkAccess = LinkAccessCheck.IsChecked == true,
            LinkRole = role,
            TemplateFilePath = _templatePath
        };
        DialogResult = true;
    }

    private void Annuler_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
