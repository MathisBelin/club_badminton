using System.Windows;
using System.Windows.Controls;
using BadmintonClub.Helpers;
using BadmintonClub.Models;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

public partial class EmailView : UserControl, IActivableView
{
    private readonly AppServices _services;

    public EmailView(AppServices services)
    {
        InitializeComponent();
        _services = services;
        LabelSelect.Placeholder = "Choisir des libellés";
        LabelSelect.SetEmptyText("Aucun libellé disponible.");

        _services.LabelsChanged += () => _ = LoadLabelsAsync();
    }

    public void OnActivated() => _ = LoadLabelsAsync();

    private void Gerer_Click(object sender, RoutedEventArgs e)
    {
        var win = new ManageTemplatesWindow { Owner = Window.GetWindow(this) };
        win.ShowDialog();
    }

    private async Task LoadLabelsAsync()
    {
        try
        {
            var selected = LabelSelect.SelectedTags.OfType<string>().ToHashSet(StringComparer.Ordinal);
            var labels = await _services.GetLabelsAsync();
            LabelSelect.SetOptions(labels.Select(l => new CheckOption
            {
                Text = l.Nom,
                Tag = l.ResourceName,
                IsSelected = selected.Contains(l.ResourceName)
            }));
        }
        catch (GoogleSyncException)
        {
            LabelSelect.SetEmptyText("Libellés indisponibles (hors ligne ?).");
        }
    }

    private async void Ecrire_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this)!;
        var selected = LabelSelect.SelectedTags.OfType<string>().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(owner, "Sélectionnez au moins un libellé.", "E-mail",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Choix : mail vierge ou à partir d'un modèle.
        var choice = new ComposeChoiceWindow { Owner = owner };
        if (choice.ShowDialog() != true)
            return;

        HashSet<string> destinataires = new(StringComparer.OrdinalIgnoreCase);
        var result = await ProgressRunner.RunBusyAsync(owner, "Récupération des destinataires…", async () =>
        {
            foreach (var res in selected)
                destinataires.UnionWith(await _services.Contacts.GetLabelMemberEmailsAsync(res));
        });

        if (result.Failed > 0)
        {
            MessageBox.Show(owner, result.LastError, "E-mail", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (destinataires.Count == 0)
        {
            ResultText.Text = string.Empty;
            MessageBox.Show(owner, "Aucun destinataire trouvé pour ces libellés.", "E-mail",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        BrowserService.OpenGmailComposeMany(destinataires, choice.Subject, choice.Body);
        ResultText.Text = $"✓ Gmail ouvert avec {destinataires.Count} destinataire(s).";
    }
}
