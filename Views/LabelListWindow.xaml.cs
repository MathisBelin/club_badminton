using System.Windows;

namespace BadmintonClub.Views;

/// <summary>Affiche (joliment) la liste des libellés d'une personne, sous forme de puces colorées.</summary>
public partial class LabelListWindow : Window
{
    public LabelListWindow(string personName, IReadOnlyList<string> labels)
    {
        InitializeComponent();

        TitleText.Text = $"Libellés de {personName}";
        SubtitleText.Text = labels.Count switch
        {
            0 => "Aucun libellé",
            1 => "1 libellé",
            _ => $"{labels.Count} libellés"
        };

        if (labels.Count == 0)
        {
            EmptyBox.Visibility = Visibility.Visible;
            ChipsScroll.Visibility = Visibility.Collapsed;
        }
        else
        {
            ChipsList.ItemsSource = labels;
        }
    }

    private void Fermer_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
