using System.Text.Json.Serialization;

namespace BadmintonClub.Models;

/// <summary>Catégorie de l'objet concerné par une action (détermine le tableau d'historique).</summary>
public enum ActivityCategory { Utilisateur, Libelle, Sheet }

/// <summary>Type d'action journalisée.</summary>
public enum ActivityAction { Ajout, Modification, Suppression, Association, Dissociation }

/// <summary>Une entrée de l'historique des activités (persistée par compte).</summary>
public class ActivityEntry
{
    public DateTime Date { get; set; } = DateTime.Now;
    public ActivityCategory Category { get; set; }
    public ActivityAction Action { get; set; }

    /// <summary>Objet concerné (nom de la personne, du libellé ou du classeur).</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Précision (ex. libellé associé/dissocié, champ modifié).</summary>
    public string Details { get; set; } = string.Empty;

    /// <summary>Ancienne valeur (modifications).</summary>
    public string OldValue { get; set; } = string.Empty;

    /// <summary>Nouvelle valeur (modifications).</summary>
    public string NewValue { get; set; } = string.Empty;

    // ---- Instantané du contact AU MOMENT de l'action (figé, survit aux modifs/suppressions) ----
    public string TargetNom { get; set; } = string.Empty;
    public string TargetPrenom { get; set; } = string.Empty;
    public string TargetTelephone { get; set; } = string.Empty;
    public string TargetEmail { get; set; } = string.Empty;

    /// <summary>Vrai pour les actions concernant un contact (bouton « détails » proposé).</summary>
    [JsonIgnore]
    public bool IsUser => Category == ActivityCategory.Utilisateur;
}
