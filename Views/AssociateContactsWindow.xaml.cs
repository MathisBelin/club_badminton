using System.Windows;
using System.Windows.Controls;
using BadmintonClub.Helpers;
using BadmintonClub.Models;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

public partial class AssociateContactsWindow : Window
{
    private readonly GoogleContactsService _contacts;
    private readonly List<Adherent> _adherents;

    public AssociateContactsWindow(GoogleContactsService contacts, IEnumerable<Adherent> adherents)
    {
        InitializeComponent();
        _contacts = contacts;
        _adherents = adherents.ToList();

        AdherentsSelect.Placeholder = "Choisir des adhérents";
        AdherentsSelect.SetEmptyText("Sélectionnez d'abord un libellé.");

        Loaded += async (_, _) => await LoadLabelsAsync();
    }

    private async Task LoadLabelsAsync()
    {
        await RunBusyAsync(async () =>
        {
            var labels = await _contacts.ListLabelsAsync();
            LabelCombo.ItemsSource = labels;
            EmptyLabelsHint.Visibility = labels.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            if (labels.Count > 0)
                LabelCombo.SelectedIndex = 0; // déclenche le chargement des adhérents
        });
    }

    private async void LabelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LabelCombo.SelectedItem is not LabelItem label)
            return;

        await RunBusyAsync(async () =>
        {
            var memberEmails = await _contacts.GetLabelMemberEmailsAsync(label.ResourceName);

            // On ne propose que les adhérents ayant un e-mail et non déjà associés à ce libellé.
            var options = _adherents
                .Where(a => !string.IsNullOrWhiteSpace(a.Email) && !memberEmails.Contains(a.Email))
                .OrderBy(a => a.Nom, StringComparer.CurrentCultureIgnoreCase)
                .Select(a => new CheckOption
                {
                    Text = string.IsNullOrWhiteSpace(a.Nom) && string.IsNullOrWhiteSpace(a.Prenom)
                        ? a.Email
                        : $"{a.Prenom} {a.Nom} — {a.Email}",
                    Tag = a
                });

            AdherentsSelect.SetOptions(options);
            AdherentsSelect.SetEmptyText("Tous les adhérents (avec e-mail) sont déjà associés à ce libellé.");
        });
    }

    private async void Associer_Click(object sender, RoutedEventArgs e)
    {
        if (LabelCombo.SelectedItem is not LabelItem label)
        {
            Warn("Sélectionnez un libellé.");
            return;
        }

        var adherents = AdherentsSelect.SelectedTags.OfType<Adherent>().ToList();
        if (adherents.Count == 0)
        {
            Warn("Cochez au moins un adhérent.");
            return;
        }

        var groupResource = label.ResourceName;
        var result = await ProgressRunner.RunAsync(this, "Association des contacts…", adherents,
            async a =>
            {
                var resource = await _contacts.EnsureContactResourceAsync(a);
                await _contacts.SetMembershipAsync(resource, groupResource, add: true);
            });

        if (result.Failed > 0)
        {
            MessageBox.Show(this,
                $"{result.Ok} contact(s) associé(s), {result.Failed} en erreur.\n\n{result.LastError}",
                "Association", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            MessageBox.Show(this,
                $"{result.Ok} contact(s) associé(s) au libellé « {label.Nom} ».",
                "Association", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        DialogResult = true;
    }

    private void Annuler_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Warn(string message)
        => MessageBox.Show(this, message, "Association", MessageBoxButton.OK, MessageBoxImage.Warning);

    private async Task<bool> RunBusyAsync(Func<Task> action)
    {
        try
        {
            IsEnabled = false;
            Cursor = System.Windows.Input.Cursors.Wait;
            await action();
            return true;
        }
        catch (GoogleSyncException ex)
        {
            MessageBox.Show(this, ex.Message, "Association", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        finally
        {
            IsEnabled = true;
            Cursor = System.Windows.Input.Cursors.Arrow;
        }
    }
}
