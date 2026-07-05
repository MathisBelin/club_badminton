using System.Windows;
using System.Windows.Controls;

namespace BadmintonClub.Controls;

/// <summary>Champ de recherche arrondi avec bouton « ✕ » pour effacer.</summary>
public partial class SearchBox : UserControl
{
    public event TextChangedEventHandler? TextChanged;

    public SearchBox()
    {
        InitializeComponent();
    }

    public string Text
    {
        get => Box.Text;
        set => Box.Text = value ?? string.Empty;
    }

    public string PlaceholderText
    {
        get => Watermark.Text;
        set => Watermark.Text = value;
    }

    public void Clear() => Box.Clear();

    private void Box_TextChanged(object sender, TextChangedEventArgs e)
    {
        var empty = string.IsNullOrEmpty(Box.Text);
        Watermark.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ClearBtn.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        TextChanged?.Invoke(this, e);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        Box.Clear();
        Box.Focus();
    }
}
