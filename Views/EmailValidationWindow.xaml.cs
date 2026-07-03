using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using BadmintonClub.Models;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

public partial class EmailValidationWindow : Window
{
    private readonly List<EmailFix> _fixes;

    public EmailValidationWindow(IEnumerable<Adherent> invalidContacts)
    {
        InitializeComponent();
        _fixes = invalidContacts.Select(a => new EmailFix(a)).ToList();
        foreach (var f in _fixes)
            f.PropertyChanged += (_, _) => UpdateSummary();
        Grid.ItemsSource = _fixes;
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var ok = _fixes.Count(f => f.IsValid);
        SummaryText.Text = $"{ok} / {_fixes.Count} corrigé(s) et valide(s)";
    }

    private void Valider_Click(object sender, RoutedEventArgs e)
    {
        // On applique les corrections aux contacts (les invalides restants seront filtrés par l'appelant).
        foreach (var f in _fixes)
            f.Adherent.Email = f.Corrected.Trim();
        DialogResult = true;
    }

    private void Annuler_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

/// <summary>Ligne de correction d'un e-mail invalide.</summary>
internal class EmailFix : INotifyPropertyChanged
{
    public Adherent Adherent { get; }
    public string Original { get; }
    public string ContactLabel { get; }

    private string _corrected;
    public string Corrected
    {
        get => _corrected;
        set
        {
            _corrected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsValid));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public bool IsValid => EmailValidator.IsValid(Corrected);
    public string StatusText => IsValid ? "✓ valide" : "✗ à corriger";

    public EmailFix(Adherent adherent)
    {
        Adherent = adherent;
        Original = adherent.Email;
        _corrected = EmailValidator.Suggest(adherent.Email);

        var name = $"{adherent.Prenom} {adherent.Nom}".Trim();
        ContactLabel = string.IsNullOrWhiteSpace(name) ? "—" : name;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
