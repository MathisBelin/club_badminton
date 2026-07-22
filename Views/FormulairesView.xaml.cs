using System.Windows;
using System.Windows.Controls;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

/// <summary>
/// Page « Formulaires » : point d'accès à l'application WEB des formulaires d'inscription
/// (projet bad-web). Ouvre le site dans le navigateur configuré.
/// </summary>
public partial class FormulairesView : UserControl
{
    /// <summary>Adresse de l'application web des formulaires (déploiement de production).</summary>
    public const string AppUrl = "https://bad-web-rho.vercel.app";

    public FormulairesView()
    {
        InitializeComponent();
        UrlBox.Text = AppUrl;
    }

    private void Ouvrir_Click(object sender, RoutedEventArgs e) => BrowserService.Open(AppUrl);

    private void Copier_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(AppUrl);
            MessageBox.Show(Window.GetWindow(this), "Lien copié dans le presse-papiers.",
                "Formulaires", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            MessageBox.Show(Window.GetWindow(this), "Impossible d'accéder au presse-papiers.",
                "Formulaires", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
