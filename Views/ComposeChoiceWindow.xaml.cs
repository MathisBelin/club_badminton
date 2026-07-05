using System.Windows;
using BadmintonClub.Models;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

public partial class ComposeChoiceWindow : Window
{
    public string? Subject { get; private set; }
    public string? Body { get; private set; }

    public ComposeChoiceWindow()
    {
        InitializeComponent();

        var templates = MailTemplateStore.LoadAll();
        TemplateSelect.Placeholder = "Choisir un modèle";
        TemplateSelect.SetEmptyText("Aucun modèle enregistré.");
        TemplateSelect.SetOptions(templates.Select((t, i) => new CheckOption
        {
            Text = t.Name,
            Tag = t,
            IsSelected = i == 0 // pré-sélection du 1er modèle
        }));
    }

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        // Le select2 n'apparaît que si « à partir d'un modèle » est coché.
        if (TemplatePanel != null)
            TemplatePanel.Visibility = RadioModel.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Continuer_Click(object sender, RoutedEventArgs e)
    {
        if (RadioModel.IsChecked == true)
        {
            if (TemplateSelect.SelectedOption?.Tag is not MailTemplate t)
            {
                MessageBox.Show(this, "Choisissez un modèle, ou sélectionnez « Mail vierge ».",
                    "Écrire un mail", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Subject = t.Subject;
            Body = t.Body;
        }
        DialogResult = true;
    }

    private void Annuler_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
