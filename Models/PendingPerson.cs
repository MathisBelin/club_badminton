namespace BadmintonClub.Models;

/// <summary>
/// Une personne repérée dans un Google Sheet synchronisé qui a renseigné des informations
/// (nom / prénom / téléphone) mais PAS d'e-mail : son inscription au libellé est incomplète.
/// </summary>
public class PendingPerson
{
    public string LabelResourceName { get; set; } = string.Empty;
    public string LabelName { get; set; } = string.Empty;
    public string Nom { get; set; } = string.Empty;
    public string Prenom { get; set; } = string.Empty;
    public string Telephone { get; set; } = string.Empty;
}
