using System.IO;
using System.Windows;
using System.Windows.Media;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

public partial class CreateSheetWindow : Window
{
    private string? _templatePath;
    private bool _linkAccess = true;
    private string _linkRole = "writer";

    private static readonly Brush OkBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
    private static readonly Brush ErrBrush = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));

    public SheetCreateOptions Options { get; private set; } = new();

    public CreateSheetWindow(string defaultName)
    {
        InitializeComponent();
        SheetNameBox.Text = defaultName;
        Loaded += (_, _) => { SheetNameBox.SelectAll(); SheetNameBox.Focus(); };
    }

    private void Type_Checked(object sender, RoutedEventArgs e)
    {
        if (TemplateZone == null)
            return;
        TemplateZone.Visibility = RadioModel.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- Zone modèle ------------------------------------------------------

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
        AppPaths.EnsureModelsFolder();
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choisir un modèle (Excel ou CSV)",
            InitialDirectory = AppPaths.ModelsFolder,
            Filter = "Excel/CSV (*.xlsx;*.csv)|*.xlsx;*.csv|Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv|Tous (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) == true)
            SetTemplate(dlg.FileName);
    }

    private void SetTemplate(string path)
    {
        var name = Path.GetFileName(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();

        // Mauvais format → dropzone rouge, on ne retient pas le fichier.
        if (ext != ".xlsx" && ext != ".csv")
        {
            _templatePath = null;
            SetDropState(false, "❌", $"Format non pris en charge : {name}\n(attendu : .xlsx ou .csv)");
            ClearTemplateButton.Visibility = Visibility.Collapsed;
            return;
        }

        _templatePath = path;
        SetDropState(true, "✅", name);
        ClearTemplateButton.Visibility = Visibility.Visible;
    }

    private void ClearTemplate_Click(object sender, RoutedEventArgs e)
    {
        _templatePath = null;
        SetDropState(null, "📄", "Glissez un modèle Excel/CSV ici,\nou cliquez pour choisir dans vos modèles.");
        ClearTemplateButton.Visibility = Visibility.Collapsed;
    }

    /// <summary>Colore la dropzone : true = vert, false = rouge, null = neutre (défaut).</summary>
    private void SetDropState(bool? ok, string icon, string hint)
    {
        DropRect.Stroke = ok == true ? OkBrush : ok == false ? ErrBrush
            : new SolidColorBrush(Color.FromRgb(0x9C, 0xB8, 0x9C));
        DropRect.Fill = new SolidColorBrush(ok == true ? Color.FromRgb(0xEA, 0xF5, 0xEA)
            : ok == false ? Color.FromRgb(0xFD, 0xEC, 0xEA) : Color.FromRgb(0xF7, 0xFA, 0xF7));
        DropIcon.Text = icon;
        TemplateHint.Foreground = ok == true ? OkBrush : ok == false ? ErrBrush
            : new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
        TemplateHint.Text = hint;
    }

    // ---- Partage ----------------------------------------------------------

    private void Partage_Click(object sender, RoutedEventArgs e)
    {
        var win = new SheetShareSettingsWindow(_linkAccess, _linkRole) { Owner = this };
        if (win.ShowDialog() == true)
        {
            _linkAccess = win.LinkAccess;
            _linkRole = win.LinkRole;
        }
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

        if (RadioModel.IsChecked == true && string.IsNullOrEmpty(_templatePath))
        {
            MessageBox.Show(this, "Choisissez un fichier modèle, ou sélectionnez « Classeur vierge ».",
                "Modèle", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Options = new SheetCreateOptions
        {
            Title = name,
            LinkAccess = _linkAccess,
            LinkRole = _linkRole,
            TemplateFilePath = RadioModel.IsChecked == true ? _templatePath : null
        };
        DialogResult = true;
    }

    private void Annuler_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
