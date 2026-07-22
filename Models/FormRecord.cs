using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace BadmintonClub.Models;

/// <summary>
/// Un Google Form créé/connu par l'application, mémorisé localement (registre synchronisé
/// depuis Drive). Peut être marqué comme **modèle** pour servir de base à de nouveaux formulaires.
/// </summary>
public class FormRecord : INotifyPropertyChanged
{
    public string FormId { get; set; } = string.Empty;

    private string _nom = string.Empty;
    public string Nom
    {
        get => _nom;
        set { _nom = value; OnPropertyChanged(); }
    }

    /// <summary>Lien d'édition du formulaire (gestion). Dérivé de l'id si absent.</summary>
    public string EditUrl { get; set; } = string.Empty;

    /// <summary>Lien public de réponse (connu seulement pour les formulaires créés par l'appli).</summary>
    public string ResponderUri { get; set; } = string.Empty;

    public DateTime DateCreation { get; set; } = DateTime.Now;

    /// <summary>Libellé (groupe Google Contacts) associé au formulaire pour la validation des réponses.</summary>
    private string _labelName = string.Empty;
    public string LabelResourceName { get; set; } = string.Empty;
    public string LabelName
    {
        get => _labelName;
        set { _labelName = value; OnPropertyChanged(); OnPropertyChanged(nameof(LabelDisplay)); }
    }

    /// <summary>Correspondance manuelle champ contact → id de question (vide = auto-détection).</summary>
    public Dictionary<string, string> FieldMap { get; set; } = new();

    /// <summary>
    /// Règles par réponse (questions à choix unique) : clé = id question + option, valeur = « waitlist »
    /// (ajouter à la liste d'attente) ou « cancel » (annuler l'inscription). Définies dans la configuration.
    /// </summary>
    public Dictionary<string, string> AnswerRules { get; set; } = new();

    /// <summary>Construit la clé d'une règle de réponse (id de question + valeur d'option).</summary>
    public static string RuleKey(string questionId, string optionValue) => questionId + "|@|" + optionValue;

    /// <summary>Libellé affichable (ou tiret si non associé).</summary>
    [JsonIgnore]
    public string LabelDisplay => string.IsNullOrWhiteSpace(LabelName) ? "—" : LabelName;

    private bool _isTemplate;
    /// <summary>Marqué comme modèle → proposé dans le menu déroulant « à partir d'un modèle ».</summary>
    public bool IsTemplate
    {
        get => _isTemplate;
        set { _isTemplate = value; OnPropertyChanged(); OnPropertyChanged(nameof(TemplateMark)); }
    }

    /// <summary>Étoile affichée dans la liste pour les modèles.</summary>
    [JsonIgnore]
    public string TemplateMark => _isTemplate ? "⭐" : string.Empty;

    /// <summary>Lien d'édition effectif (dérivé de l'id si non renseigné).</summary>
    [JsonIgnore]
    public string EditLink => string.IsNullOrWhiteSpace(EditUrl)
        ? $"https://docs.google.com/forms/d/{FormId}/edit"
        : EditUrl;

    private bool _isSelected;
    [JsonIgnore]
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
