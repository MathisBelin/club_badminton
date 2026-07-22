using System.IO;
using System.Windows;
using BadmintonClub.Models;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

public partial class CreateFormWindow : Window
{
    private readonly List<FormModelFile> _models;
    private string? _importedPath;

    public string FormName { get; private set; } = string.Empty;

    /// <summary>Modèle choisi (liste locale ou fichier importé) ; null = formulaire vierge.</summary>
    public FormTemplate? SelectedModel { get; private set; }

    public CreateFormWindow(string defaultName, List<FormModelFile> models)
    {
        InitializeComponent();
        _models = models;
        FormNameBox.Text = defaultName;
        ModelCombo.ItemsSource = _models;
        if (_models.Count > 0)
            ModelCombo.SelectedIndex = 0;
        Loaded += (_, _) => { FormNameBox.SelectAll(); FormNameBox.Focus(); };
    }

    private void Type_Checked(object sender, RoutedEventArgs e)
    {
        if (TemplateZone == null)
            return;
        TemplateZone.Visibility = RadioModel.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        UpdateSourceZones();
    }

    private void Source_Checked(object sender, RoutedEventArgs e) => UpdateSourceZones();

    private void UpdateSourceZones()
    {
        if (ListZone == null)
            return;
        var fromList = RadioFromList.IsChecked == true;
        ListZone.Visibility = fromList ? Visibility.Visible : Visibility.Collapsed;
        ImportZone.Visibility = fromList ? Visibility.Collapsed : Visibility.Visible;

        ModelCombo.Visibility = fromList && _models.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NoModelHint.Visibility = fromList && _models.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            SetImportedFile(files[0]);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choisir un fichier modèle",
            Filter = "Modèle de formulaire (*.json)|*.json|Tous les fichiers (*.*)|*.*",
            InitialDirectory = Directory.Exists(AppPaths.FormModelsFolder)
                ? AppPaths.FormModelsFolder
                : AppPaths.DataFolder
        };
        if (dialog.ShowDialog(this) == true)
            SetImportedFile(dialog.FileName);
    }

    private void SetImportedFile(string path)
    {
        _importedPath = path;
        SelectedFileText.Text = Path.GetFileName(path);
        SelectedFileText.Visibility = Visibility.Visible;
        DropText.Text = "Fichier sélectionné :";
    }

    private void Creer_Click(object sender, RoutedEventArgs e)
    {
        var name = FormNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Warn("Saisissez un nom de formulaire.");
            return;
        }

        if (RadioModel.IsChecked == true)
        {
            string? path;
            if (RadioFromList.IsChecked == true)
            {
                if (ModelCombo.SelectedItem is not FormModelFile file)
                {
                    Warn("Choisissez un modèle, ou sélectionnez « Formulaire vierge ».");
                    return;
                }
                path = file.Path;
            }
            else
            {
                if (string.IsNullOrEmpty(_importedPath))
                {
                    Warn("Sélectionnez un fichier modèle à importer.");
                    return;
                }
                path = _importedPath;
            }

            var tpl = FormTemplateRepository.Load(path);
            if (tpl == null || tpl.Items.Count == 0)
            {
                Warn("Fichier modèle illisible ou sans question exploitable.");
                return;
            }
            SelectedModel = tpl;
        }

        FormName = name;
        DialogResult = true;
    }

    private void Warn(string message)
        => MessageBox.Show(this, message, "Créer un Form", MessageBoxButton.OK, MessageBoxImage.Warning);

    private void Annuler_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
