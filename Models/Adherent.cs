using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace BadmintonClub.Models;

/// <summary>
/// Un adhérent du club. La structure JSON sérialisée correspond exactement
/// aux champs demandés : Id, Nom, Prenom, Telephone, Email, DateInscription.
/// </summary>
public class Adherent : INotifyPropertyChanged
{
    private string _nom = string.Empty;
    private string _prenom = string.Empty;
    private string _telephone = string.Empty;
    private string _email = string.Empty;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Nom
    {
        get => _nom;
        set { _nom = value; OnPropertyChanged(); }
    }

    public string Prenom
    {
        get => _prenom;
        set { _prenom = value; OnPropertyChanged(); }
    }

    public string Telephone
    {
        get => _telephone;
        set { _telephone = value; OnPropertyChanged(); }
    }

    public string Email
    {
        get => _email;
        set { _email = value; OnPropertyChanged(); }
    }

    public DateTime DateInscription { get; set; } = DateTime.Now;

    /// <summary>E-mails secondaires (facultatifs) : ajoutés comme adresses supplémentaires du contact Google.</summary>
    public List<string> SecondaryEmails { get; set; } = new();

    /// <summary>Vrai si le contact a au moins un e-mail secondaire (pour l'affichage de la colonne).</summary>
    [JsonIgnore]
    public bool HasSecondaryEmails => SecondaryEmails is { Count: > 0 };

    /// <summary>Libellé compact de la colonne mails secondaires (« ✉ N » ou « N/A »).</summary>
    [JsonIgnore]
    public string SecondaryEmailsBadge => HasSecondaryEmails ? $"✉ {SecondaryEmails.Count}" : "N/A";

    /// <summary>Ressource Google du contact lié ("people/xxx"), vide si non synchronisé.</summary>
    public string GoogleResourceName { get; set; } = string.Empty;

    private bool _isSelected;

    /// <summary>Sélection (case à cocher) pour les actions groupées. Non sérialisé.</summary>
    [JsonIgnore]
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Copie superficielle, utilisée pour éditer sans toucher l'original tant qu'on n'a pas validé.</summary>
    public Adherent Clone() => new()
    {
        Id = Id,
        Nom = Nom,
        Prenom = Prenom,
        Telephone = Telephone,
        Email = Email,
        SecondaryEmails = new List<string>(SecondaryEmails),
        DateInscription = DateInscription,
        GoogleResourceName = GoogleResourceName
    };

    public void CopyFrom(Adherent other)
    {
        Nom = other.Nom;
        Prenom = other.Prenom;
        Telephone = other.Telephone;
        Email = other.Email;
        SecondaryEmails = new List<string>(other.SecondaryEmails);
        // Id et DateInscription ne changent pas lors d'une modification.
    }
}
