namespace BadmintonClub.Models;

/// <summary>
/// Une personne associée au libellé cible lors de la dernière exécution d'une synchro.
/// <see cref="ViaKnown"/> est vrai si elle n'est passée que grâce à l'option « associer les
/// personnes connues » (aucun e-mail exploitable dans le Sheet, mais correspondance certaine
/// à un unique contact existant).
/// </summary>
public class SyncTraceEntry
{
    public string Nom { get; set; } = string.Empty;
    public string Prenom { get; set; } = string.Empty;
    public string Telephone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool ViaKnown { get; set; }
}
