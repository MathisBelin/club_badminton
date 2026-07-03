using System.Windows;

namespace BadmintonClub.Views;

public partial class ProgressWindow : Window
{
    public ProgressWindow()
    {
        InitializeComponent();
    }

    public void Setup(string title, int max)
    {
        TitleText.Text = title;
        Bar.Maximum = Math.Max(1, max);
        Bar.Value = 0;
        StatusText.Text = $"0 / {max}";
    }

    public void Report(int done)
    {
        Bar.Value = done;
        StatusText.Text = $"{done} / {(int)Bar.Maximum}";
    }

    public void SetupIndeterminate(string title)
    {
        TitleText.Text = title;
        Bar.IsIndeterminate = true;
        StatusText.Text = "Veuillez patienter…";
    }
}
