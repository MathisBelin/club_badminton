namespace BadmintonClub.Models;

/// <summary>
/// Une personne repérée dans un Google Sheet synchronisé qui a renseigné des informations
/// (nom / prénom / téléphone) mais dont l'e-mail est manquant ou au mauvais format (non
/// rattrapable automatiquement) : son inscription au libellé est incomplète.
/// </summary>
public class PendingPerson
{
    public string LabelResourceName { get; set; } = string.Empty;
    public string LabelName { get; set; } = string.Empty;
    public string Nom { get; set; } = string.Empty;
    public string Prenom { get; set; } = string.Empty;
    public string Telephone { get; set; } = string.Empty;

    /// <summary>E-mail brut lu dans le Sheet : vide (non renseigné) ou au format incorrect.</summary>
    public string Email { get; set; } = string.Empty;
}
