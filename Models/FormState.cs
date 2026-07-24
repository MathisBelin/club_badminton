namespace BadmintonClub.Models;

/// <summary>
/// Décisions prises localement sur les réponses d'un formulaire d'inscription.
/// L'application web reste en lecture seule : tout ce qui suit est mémorisé côté desktop
/// (fichier <c>form_states.json</c> du compte), par identifiant de formulaire.
/// La personne est identifiée par son <b>e-mail de répondant</b> (insensible à la casse).
/// </summary>
public class FormState
{
    /// <summary>Inscriptions validées : la personne n'apparaît plus dans les réponses (voir « ✅ Inscrits »).</summary>
    public List<ValidatedEntry> Validated { get; set; } = new();

    /// <summary>Statut forcé à la main : « waitlist » (liste d'attente) ou « pre » (préinscription).</summary>
    public Dictionary<string, string> StatusOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Lien durable réponse ↔ contact : <b>e-mail vérifié du répondant → identifiant du contact</b>
    /// (<c>Adherent.Id</c>). Mémorisé au premier rapprochement par e-mail ; permet ensuite de
    /// retrouver le contact <b>même si son e-mail change</b> (le rapprochement ne dépend donc plus
    /// du seul e-mail courant du contact). La clé, elle, reste l'identité immuable de la réponse.
    /// </summary>
    public Dictionary<string, string> ContactLinks { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Une inscription validée, conservée pour l'historique du formulaire.</summary>
public class ValidatedEntry
{
    /// <summary>
    /// E-mail <b>vérifié du répondant</b> : identité immuable de la réponse. C'est cette valeur
    /// (et non l'e-mail du contact) qui masque l'inscrit de la liste des réponses ; elle ne doit
    /// PAS être réécrite quand on modifie l'e-mail d'un contact (voir <see cref="ContactId"/>).
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Identifiant stable du contact lié (<c>Adherent.Id</c>). Sert à retrouver le contact même
    /// si son e-mail change : le lien inscrit ↔ contact ne repose donc plus uniquement sur l'e-mail.
    /// Vide pour les entrées créées avant cette évolution (repli sur l'e-mail, puis complété).
    /// </summary>
    public string ContactId { get; set; } = string.Empty;

    public string Nom { get; set; } = string.Empty;
    public string Prenom { get; set; } = string.Empty;
    public string Telephone { get; set; } = string.Empty;

    /// <summary>Libellé Contacts auquel la personne a été associée à la validation.</summary>
    public string LabelName { get; set; } = string.Empty;

    /// <summary>
    /// Ressource du libellé Contacts (« contactGroups/... ») auquel la personne est associée.
    /// Permet de la dissocier de ce libellé si le formulaire change de libellé, ou lorsqu'on la
    /// remet en préinscription. Vide pour les entrées créées avant cette évolution (repli par nom).
    /// </summary>
    public string LabelResourceName { get; set; } = string.Empty;

    public DateTime ValidatedAt { get; set; } = DateTime.Now;

    /// <summary>Date de la réponse au formulaire (pour retrouver la chronologie).</summary>
    public DateTime SubmittedAt { get; set; }

    public string NomComplet => $"{Prenom} {Nom}".Trim();
    public string ValidatedText => ValidatedAt.ToString("dd/MM/yyyy HH:mm");
    public string SubmittedText => SubmittedAt == default ? "N/A" : SubmittedAt.ToString("dd/MM/yyyy HH:mm");
}
