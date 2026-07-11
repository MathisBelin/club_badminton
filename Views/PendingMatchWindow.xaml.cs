using System.Windows;
using BadmintonClub.Models;

namespace BadmintonClub.Views;

/// <summary>Affiche (en lecture seule) les contacts existants qui pourraient correspondre.</summary>
public partial class PendingMatchWindow : Window
{
    public PendingMatchWindow(PendingPerson person, string labelName, List<Adherent> candidates)
    {
        InitializeComponent();

        var infos = new List<string>();
        if (!string.IsNullOrWhiteSpace(person.Nom)) infos.Add(person.Nom);
        if (!string.IsNullOrWhiteSpace(person.Prenom)) infos.Add(person.Prenom);
        if (!string.IsNullOrWhiteSpace(person.Telephone)) infos.Add(person.Telephone);
        PersonText.Text = $"Personne : {string.Join("  ·  ", infos)}   —   libellé « {labelName} »";

        Grid.ItemsSource = candidates;
        HintText.Text = candidates.Count == 0
            ? "Aucun contact existant ne correspond."
            : $"{candidates.Count} contact(s) pourraient correspondre :";
    }

    private void Fermer_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
