using System.Windows;
using System.Windows.Media;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

public partial class EditTemplateWindow : Window
{
    private readonly string? _originalName;

    private static readonly Brush OkBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
    private static readonly Brush ErrBrush = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));

    /// <param name="existing">null pour un nouveau modèle, sinon le modèle à modifier.</param>
    public EditTemplateWindow(MailTemplate? existing = null)
    {
        InitializeComponent();
        if (existing != null)
        {
            _originalName = existing.Name;
            NameBox.Text = existing.Name;
            SubjectBox.Text = existing.Subject;
            BodyBox.Text = existing.Body;
            Title = "Modifier le modèle";
        }
        else
        {
            Title = "Nouveau modèle";
        }
    }

    private void Drop_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Drop_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            ImportEml(files[0]);
    }

    private void Drop_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choisir un e-mail (.eml)",
            Filter = "E-mail (*.eml)|*.eml|Tous les fichiers (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) == true)
            ImportEml(dlg.FileName);
    }

    private void ImportEml(string path)
    {
        var name = System.IO.Path.GetFileName(path);

        // Mauvais format → dropzone rouge.
        if (!string.Equals(System.IO.Path.GetExtension(path), ".eml", StringComparison.OrdinalIgnoreCase))
        {
            SetDropState(false, "❌", $"Format non pris en charge : {name}\n(attendu : .eml)");
            return;
        }

        try
        {
            var (subject, body) = EmlParser.Parse(path);
            SubjectBox.Text = subject;
            BodyBox.Text = body;
            if (string.IsNullOrWhiteSpace(NameBox.Text))
                NameBox.Text = string.IsNullOrWhiteSpace(subject)
                    ? System.IO.Path.GetFileNameWithoutExtension(path)
                    : subject;
            SetDropState(true, "✅", name);
        }
        catch (Exception ex)
        {
            SetDropState(false, "❌", $"Fichier .eml illisible : {name}");
            MessageBox.Show(this, $"Impossible de lire le fichier .eml :\n{ex.Message}", "Modèle",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Colore la dropzone : true = vert (importé), false = rouge (erreur).</summary>
    private void SetDropState(bool ok, string icon, string hint)
    {
        DropRect.Stroke = ok ? OkBrush : ErrBrush;
        DropRect.Fill = new SolidColorBrush(ok ? Color.FromRgb(0xEA, 0xF5, 0xEA) : Color.FromRgb(0xFD, 0xEC, 0xEA));
        DropIcon.Text = icon;
        DropHint.Foreground = ok ? OkBrush : ErrBrush;
        DropHint.Text = hint;
    }

    private void Enregistrer_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "Donnez un nom au modèle.", "Modèle",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Renommage : on supprime l'ancien fichier si le nom a changé.
        if (_originalName != null && !string.Equals(_originalName, name, StringComparison.OrdinalIgnoreCase))
            MailTemplateStore.Delete(_originalName);

        MailTemplateStore.Save(new MailTemplate { Name = name, Subject = SubjectBox.Text, Body = BodyBox.Text });
        DialogResult = true;
    }

    private void Annuler_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
