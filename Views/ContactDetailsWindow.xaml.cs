using System.Windows;
using BadmintonClub.Models;

namespace BadmintonClub.Views;

/// <summary>Détails (figés) d'un contact au moment d'une action de l'historique.</summary>
public partial class ContactDetailsWindow : Window
{
    public ContactDetailsWindow(ActivityEntry entry)
    {
        InitializeComponent();

        var name = $"{entry.TargetPrenom} {entry.TargetNom}".Trim();
        if (string.IsNullOrWhiteSpace(name))
            name = string.IsNullOrWhiteSpace(entry.TargetEmail) ? entry.Target : entry.TargetEmail;

        TitleText.Text = string.IsNullOrWhiteSpace(name) ? "Contact" : name;
        SubtitleText.Text = $"{entry.Action} — {entry.Date:dd/MM/yyyy HH:mm}";

        NomText.Text = Val(entry.TargetNom);
        PrenomText.Text = Val(entry.TargetPrenom);
        TelText.Text = Val(entry.TargetTelephone);
        MailText.Text = Val(entry.TargetEmail);
    }

    private static string Val(string s) => string.IsNullOrWhiteSpace(s) ? "—" : s;

    private void Fermer_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
