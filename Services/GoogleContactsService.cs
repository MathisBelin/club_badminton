using BadmintonClub.Models;
using Google.Apis.PeopleService.v1;
using Google.Apis.PeopleService.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util;

namespace BadmintonClub.Services;

/// <summary>État des libellés d'un contact : sa ressource Google + les groupes dont il est membre.</summary>
public class ContactLabelState
{
    public string? ResourceName { get; set; }
    public HashSet<string> GroupResourceNames { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Un contact Google (pour l'import/synchro avec la liste locale).</summary>
public sealed record GoogleContact(string ResourceName, string Prenom, string Nom, string Email, string Telephone);

/// <summary>
/// Intégration Google Contacts (People API).
/// Une « étiquette Gmail » correspond à un groupe de contacts (contactGroup).
/// On gère l'ajout d'un adhérent à ce groupe et son retrait.
///
/// Toutes les erreurs (token expiré, pas de réseau, config manquante) sont
/// converties en <see cref="GoogleSyncException"/> avec un message en français.
/// </summary>
public class GoogleContactsService
{
    // Autorisation unique partagée (Contacts + profil + Sheets + Drive) → un seul consentement.
    private static readonly string[] Scopes = GoogleAuth.AllScopes;
    private const string AppName = "BadmintonClub";
    private const string User = GoogleAuth.SharedUser;

    private PeopleServiceService? _service;

    /// <summary>
    /// Authentifie l'utilisateur (ouvre le navigateur la première fois) et prépare le service.
    /// Réutilise le token stocké tant qu'il est valide.
    /// </summary>
    public async Task EnsureAuthenticatedAsync(CancellationToken ct = default, bool promptSelectAccount = false)
    {
        if (_service != null)
            return;

        try
        {
            var credential = await GoogleAuth.AuthorizeAsync(Scopes, User, ct, promptSelectAccount);
            _service = new PeopleServiceService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = AppName
            });
        }
        catch (GoogleSyncException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw GoogleErrors.Translate(ex, "Échec de l'authentification Google.");
        }
    }

    /// <summary>Oublie la session en mémoire (à appeler avant un changement de compte).</summary>
    public void Reset() => _service = null;

    /// <summary>Un jeton est-il déjà stocké (connexion silencieuse possible) ?</summary>
    public bool HasStoredToken() => GoogleAuth.HasStoredToken(User);

    /// <summary>Authentifie et renvoie l'adresse e-mail du compte Google connecté.</summary>
    public async Task<string> GetSignedInEmailAsync(bool promptSelectAccount = false, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct, promptSelectAccount);

        try
        {
            var get = _service!.People.Get("people/me");
            get.PersonFields = "emailAddresses";
            var me = await get.ExecuteAsync(ct);

            var emails = me.EmailAddresses;
            var primary = emails?.FirstOrDefault(e => e.Metadata?.Primary == true)?.Value;
            return primary ?? emails?.FirstOrDefault()?.Value ?? string.Empty;
        }
        catch (GoogleSyncException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw GoogleErrors.Translate(ex, "Impossible de lire le compte Google connecté.");
        }
    }

    /// <summary>
    /// Ajoute (ou met à jour) l'adhérent dans Google Contacts et l'ajoute à l'étiquette.
    /// </summary>
    public async Task AddToLabelAsync(Adherent adherent, string labelName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(labelName))
            throw new GoogleSyncException("Aucune étiquette Gmail n'est configurée dans les paramètres.");

        await EnsureAuthenticatedAsync(ct);

        try
        {
            var group = await GetOrCreateGroupAsync(labelName, ct);
            var resourceName = await FindContactResourceNameAsync(adherent.Email, ct)
                               ?? await CreateContactAsync(adherent, ct);

            var body = new ModifyContactGroupMembersRequest
            {
                ResourceNamesToAdd = new List<string> { resourceName }
            };
            await _service!.ContactGroups.Members.Modify(body, group.ResourceName).ExecuteAsync(ct);
        }
        catch (GoogleSyncException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw GoogleErrors.Translate(ex, "Impossible d'ajouter le contact à l'étiquette Google.");
        }
    }

    /// <summary>
    /// Retire l'adhérent de l'étiquette (le contact lui-même n'est pas supprimé de Google Contacts).
    /// </summary>
    public async Task RemoveFromLabelAsync(Adherent adherent, string labelName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(labelName))
            throw new GoogleSyncException("Aucune étiquette Gmail n'est configurée dans les paramètres.");

        await EnsureAuthenticatedAsync(ct);

        try
        {
            var group = await FindGroupAsync(labelName, ct);
            if (group == null)
                return; // L'étiquette n'existe pas : rien à retirer.

            var resourceName = await FindContactResourceNameAsync(adherent.Email, ct);
            if (resourceName == null)
                return; // Contact introuvable : rien à retirer.

            var body = new ModifyContactGroupMembersRequest
            {
                ResourceNamesToRemove = new List<string> { resourceName }
            };
            await _service!.ContactGroups.Members.Modify(body, group.ResourceName).ExecuteAsync(ct);
        }
        catch (GoogleSyncException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw GoogleErrors.Translate(ex, "Impossible de retirer le contact de l'étiquette Google.");
        }
    }

    /// <summary>Liste les noms des libellés (groupes de contacts) créés par l'utilisateur.</summary>
    public async Task<List<string>> ListLabelNamesAsync(CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        try
        {
            var request = _service!.ContactGroups.List();
            request.PageSize = 1000;
            var response = await request.ExecuteAsync(ct);

            return response.ContactGroups?
                       .Where(g => g.GroupType == "USER_CONTACT_GROUP" && !string.IsNullOrEmpty(g.Name))
                       .Select(g => g.Name)
                       .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                       .ToList()
                   ?? new List<string>();
        }
        catch (GoogleSyncException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw GoogleErrors.Translate(ex, "Impossible de récupérer la liste des libellés Google.");
        }
    }

    /// <summary>Liste les libellés utilisateur sous forme d'éléments affichables.</summary>
    public async Task<List<LabelItem>> ListLabelsAsync(CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        try
        {
            var request = _service!.ContactGroups.List();
            request.PageSize = 1000;
            var response = await request.ExecuteAsync(ct);

            return response.ContactGroups?
                       .Where(g => g.GroupType == "USER_CONTACT_GROUP" && !string.IsNullOrEmpty(g.Name))
                       .Select(g => new LabelItem
                       {
                           ResourceName = g.ResourceName,
                           Nom = g.Name,
                           NombreMembres = g.MemberCount ?? 0
                       })
                       .OrderByDescending(l => l.Nom, StringComparer.CurrentCultureIgnoreCase)
                       .ToList()
                   ?? new List<LabelItem>();
        }
        catch (GoogleSyncException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw GoogleErrors.Translate(ex, "Impossible de récupérer la liste des libellés Google.");
        }
    }

    /// <summary>Supprime un libellé (le groupe ; les contacts eux-mêmes ne sont pas supprimés).</summary>
    public async Task DeleteLabelAsync(string resourceName, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        try
        {
            var request = _service!.ContactGroups.Delete(resourceName);
            request.DeleteContacts = false;
            await request.ExecuteAsync(ct);
        }
        catch (GoogleSyncException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw GoogleErrors.Translate(ex, "Impossible de supprimer le libellé Google.");
        }
    }

    /// <summary>Renomme un libellé existant.</summary>
    public async Task RenameLabelAsync(string resourceName, string newName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new GoogleSyncException("Le nouveau nom du libellé ne peut pas être vide.");

        await EnsureAuthenticatedAsync(ct);

        try
        {
            var group = await _service!.ContactGroups.Get(resourceName).ExecuteAsync(ct);
            group.Name = newName;

            var body = new UpdateContactGroupRequest { ContactGroup = group };
            await _service!.ContactGroups.Update(body, resourceName).ExecuteAsync(ct);
        }
        catch (GoogleSyncException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw GoogleErrors.Translate(ex, "Impossible de renommer le libellé Google.");
        }
    }

    /// <summary>Crée un nouveau libellé (groupe de contacts). Échoue s'il existe déjà.</summary>
    public async Task CreateLabelAsync(string labelName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(labelName))
            throw new GoogleSyncException("Le nom du libellé ne peut pas être vide.");

        await EnsureAuthenticatedAsync(ct);

        try
        {
            var existing = await FindGroupAsync(labelName, ct);
            if (existing != null)
                throw new GoogleSyncException($"Le libellé « {labelName} » existe déjà.");

            var createRequest = new CreateContactGroupRequest
            {
                ContactGroup = new ContactGroup { Name = labelName }
            };
            await _service!.ContactGroups.Create(createRequest).ExecuteAsync(ct);
        }
        catch (GoogleSyncException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw GoogleErrors.Translate(ex, "Impossible de créer le libellé Google.");
        }
    }

    /// <summary>
    /// Ajoute plusieurs adhérents à un libellé (créé s'il n'existe pas).
    /// Renvoie le nombre de contacts ajoutés.
    /// </summary>
    public async Task<int> AddContactsToLabelAsync(
        IEnumerable<Adherent> adherents, string labelName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(labelName))
            throw new GoogleSyncException("Aucun libellé n'est indiqué.");

        await EnsureAuthenticatedAsync(ct);

        try
        {
            var group = await GetOrCreateGroupAsync(labelName, ct);

            var resourceNames = new List<string>();
            foreach (var adherent in adherents)
            {
                var rn = await FindContactResourceNameAsync(adherent.Email, ct)
                         ?? await CreateContactAsync(adherent, ct);
                resourceNames.Add(rn);
            }

            // L'API limite le nombre de membres par requête : on découpe par paquets.
            const int chunkSize = 100;
            for (var i = 0; i < resourceNames.Count; i += chunkSize)
            {
                var chunk = resourceNames.Skip(i).Take(chunkSize).ToList();
                var body = new ModifyContactGroupMembersRequest { ResourceNamesToAdd = chunk };
                await _service!.ContactGroups.Members.Modify(body, group.ResourceName).ExecuteAsync(ct);
            }

            return resourceNames.Count;
        }
        catch (GoogleSyncException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw GoogleErrors.Translate(ex, "Impossible d'ajouter les contacts au libellé Google.");
        }
    }

    /// <summary>Liste tous les contacts Google de l'utilisateur (pour l'import local).</summary>
    public async Task<List<GoogleContact>> ListContactsAsync(CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        try
        {
            var result = new List<GoogleContact>();
            var request = _service!.People.Connections.List("people/me");
            request.PersonFields = "names,emailAddresses,phoneNumbers";
            request.PageSize = 1000;

            string? pageToken = null;
            do
            {
                request.PageToken = pageToken;
                var response = await request.ExecuteAsync(ct);

                if (response.Connections != null)
                {
                    foreach (var p in response.Connections)
                    {
                        var name = p.Names?.FirstOrDefault();
                        var prenom = name?.GivenName ?? string.Empty;
                        var nom = name?.FamilyName ?? string.Empty;
                        var email = p.EmailAddresses?.FirstOrDefault()?.Value ?? string.Empty;
                        var tel = p.PhoneNumbers?.FirstOrDefault()?.Value ?? string.Empty;

                        // On ignore les entrées totalement vides.
                        if (string.IsNullOrWhiteSpace(prenom) && string.IsNullOrWhiteSpace(nom) &&
                            string.IsNullOrWhiteSpace(email))
                            continue;

                        result.Add(new GoogleContact(p.ResourceName, prenom, nom, email, tel));
                    }
                }

                pageToken = response.NextPageToken;
            }
            while (!string.IsNullOrEmpty(pageToken));

            return result;
        }
        catch (GoogleSyncException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw GoogleErrors.Translate(ex, "Impossible de récupérer les contacts Google.");
        }
    }

    /// <summary>Renvoie les membres d'un libellé sous forme (ressource, e-mail).</summary>
    public async Task<List<(string ResourceName, string Email)>> GetLabelMembersAsync(
        string groupResourceName, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);
        try
        {
            var getGroup = _service!.ContactGroups.Get(groupResourceName);
            getGroup.MaxMembers = 10000;
            var group = await getGroup.ExecuteAsync(ct);

            var result = new List<(string, string)>();
            var members = group.MemberResourceNames;
            if (members == null || members.Count == 0)
                return result;

            const int chunkSize = 200;
            for (var i = 0; i < members.Count; i += chunkSize)
            {
                var chunk = members.Skip(i).Take(chunkSize).ToList();
                var request = _service.People.GetBatchGet();
                request.ResourceNames = new Repeatable<string>(chunk);
                request.PersonFields = "emailAddresses";
                var response = await request.ExecuteAsync(ct);

                if (response.Responses == null) continue;
                foreach (var r in response.Responses)
                {
                    var rn = r.Person?.ResourceName ?? r.RequestedResourceName;
                    var email = r.Person?.EmailAddresses?.FirstOrDefault(e => !string.IsNullOrEmpty(e.Value))?.Value;
                    if (!string.IsNullOrEmpty(rn) && !string.IsNullOrEmpty(email))
                        result.Add((rn, email));
                }
            }
            return result;
        }
        catch (GoogleSyncException) { throw; }
        catch (Exception ex) { throw GoogleErrors.Translate(ex, "Impossible de lire les membres du libellé."); }
    }

    /// <summary>Renvoie l'ensemble des e-mails des contacts membres d'un libellé.</summary>
    public async Task<HashSet<string>> GetLabelMemberEmailsAsync(
        string groupResourceName, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        try
        {
            var getGroup = _service!.ContactGroups.Get(groupResourceName);
            getGroup.MaxMembers = 10000;
            var group = await getGroup.ExecuteAsync(ct);

            var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var members = group.MemberResourceNames;
            if (members == null || members.Count == 0)
                return emails;

            // getBatchGet : max 200 ressources par appel.
            const int chunkSize = 200;
            for (var i = 0; i < members.Count; i += chunkSize)
            {
                var chunk = members.Skip(i).Take(chunkSize).ToList();
                var request = _service.People.GetBatchGet();
                request.ResourceNames = new Repeatable<string>(chunk);
                request.PersonFields = "emailAddresses";
                var response = await request.ExecuteAsync(ct);

                if (response.Responses == null)
                    continue;
                foreach (var r in response.Responses)
                {
                    if (r.Person?.EmailAddresses == null)
                        continue;
                    foreach (var e in r.Person.EmailAddresses)
                        if (!string.IsNullOrEmpty(e.Value))
                            emails.Add(e.Value);
                }
            }

            return emails;
        }
        catch (GoogleSyncException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw GoogleErrors.Translate(ex, "Impossible de lire les membres du libellé.");
        }
    }

    /// <summary>Renvoie la ressource du contact (par e-mail) et les libellés dont il est membre.</summary>
    public async Task<ContactLabelState> GetContactLabelStateAsync(string email, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        var state = new ContactLabelState();
        if (string.IsNullOrWhiteSpace(email))
            return state;

        try
        {
            var request = _service!.People.Connections.List("people/me");
            request.PersonFields = "emailAddresses,memberships";
            request.PageSize = 1000;

            string? pageToken = null;
            do
            {
                request.PageToken = pageToken;
                var response = await request.ExecuteAsync(ct);

                var match = response.Connections?.FirstOrDefault(p =>
                    p.EmailAddresses != null &&
                    p.EmailAddresses.Any(e => string.Equals(e.Value, email, StringComparison.OrdinalIgnoreCase)));

                if (match != null)
                {
                    state.ResourceName = match.ResourceName;
                    if (match.Memberships != null)
                        foreach (var m in match.Memberships)
                            if (m.ContactGroupMembership?.ContactGroupResourceName != null)
                                state.GroupResourceNames.Add(m.ContactGroupMembership.ContactGroupResourceName);
                    return state;
                }

                pageToken = response.NextPageToken;
            }
            while (!string.IsNullOrEmpty(pageToken));

            return state;
        }
        catch (GoogleSyncException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw GoogleErrors.Translate(ex, "Impossible de lire les libellés du contact.");
        }
    }

    /// <summary>Crée un contact dans Google et renvoie sa ressource.</summary>
    public async Task<string> AddContactAsync(Adherent adherent, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        try
        {
            return await CreateContactAsync(adherent, ct);
        }
        catch (GoogleSyncException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw GoogleErrors.Translate(ex, "Impossible de créer le contact Google.");
        }
    }

    /// <summary>Met à jour un contact Google existant (nom, e-mail, téléphone).</summary>
    public async Task UpdateContactAsync(string resourceName, Adherent adherent, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        try
        {
            // On récupère l'etag courant (obligatoire pour la mise à jour).
            var get = _service!.People.Get(resourceName);
            get.PersonFields = "names,emailAddresses,phoneNumbers,metadata";
            var person = await get.ExecuteAsync(ct);

            person.Names = new List<Name>
            {
                new Name { GivenName = adherent.Prenom, FamilyName = adherent.Nom }
            };
            person.EmailAddresses = string.IsNullOrWhiteSpace(adherent.Email)
                ? null
                : new List<EmailAddress> { new EmailAddress { Value = adherent.Email } };
            person.PhoneNumbers = string.IsNullOrWhiteSpace(adherent.Telephone)
                ? null
                : new List<PhoneNumber> { new PhoneNumber { Value = adherent.Telephone } };

            var update = _service.People.UpdateContact(person, resourceName);
            update.UpdatePersonFields = "names,emailAddresses,phoneNumbers";
            await update.ExecuteAsync(ct);
        }
        catch (GoogleSyncException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw GoogleErrors.Translate(ex, "Impossible de mettre à jour le contact Google.");
        }
    }

    /// <summary>Supprime définitivement un contact de Google Contacts.</summary>
    public async Task DeleteContactAsync(string resourceName, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        try
        {
            await _service!.People.DeleteContact(resourceName).ExecuteAsync(ct);
        }
        catch (Google.GoogleApiException apiEx)
            when (apiEx.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Déjà supprimé côté Google : on considère l'opération réussie.
        }
        catch (GoogleSyncException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw GoogleErrors.Translate(ex, "Impossible de supprimer le contact Google.");
        }
    }

    /// <summary>Libellés (groupes) dont un contact est membre — appel léger via sa ressource.</summary>
    public async Task<HashSet<string>> GetContactMembershipsAsync(string resourceName, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        try
        {
            var get = _service!.People.Get(resourceName);
            get.PersonFields = "memberships";
            var person = await get.ExecuteAsync(ct);

            var set = new HashSet<string>(StringComparer.Ordinal);
            if (person.Memberships != null)
                foreach (var m in person.Memberships)
                    if (m.ContactGroupMembership?.ContactGroupResourceName != null)
                        set.Add(m.ContactGroupMembership.ContactGroupResourceName);
            return set;
        }
        catch (GoogleSyncException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw GoogleErrors.Translate(ex, "Impossible de lire les libellés du contact.");
        }
    }

    /// <summary>Garantit l'existence du contact dans Google (le crée si besoin) et renvoie sa ressource.</summary>
    public async Task<string> EnsureContactResourceAsync(Adherent adherent, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        try
        {
            return await FindContactResourceNameAsync(adherent.Email, ct)
                   ?? await CreateContactAsync(adherent, ct);
        }
        catch (GoogleSyncException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw GoogleErrors.Translate(ex, "Impossible de retrouver ou créer le contact Google.");
        }
    }

    /// <summary>Ajoute ou retire un contact (par ressource) d'un libellé.</summary>
    public async Task SetMembershipAsync(
        string contactResourceName, string groupResourceName, bool add, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        try
        {
            var body = new ModifyContactGroupMembersRequest();
            if (add)
                body.ResourceNamesToAdd = new List<string> { contactResourceName };
            else
                body.ResourceNamesToRemove = new List<string> { contactResourceName };

            await _service!.ContactGroups.Members.Modify(body, groupResourceName).ExecuteAsync(ct);
        }
        catch (GoogleSyncException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw GoogleErrors.Translate(ex, "Impossible de modifier l'association du contact.");
        }
    }

    // ---- Helpers internes -------------------------------------------------

    private async Task<ContactGroup?> FindGroupAsync(string labelName, CancellationToken ct)
    {
        var request = _service!.ContactGroups.List();
        request.PageSize = 1000;
        var response = await request.ExecuteAsync(ct);

        return response.ContactGroups?
            .FirstOrDefault(g => string.Equals(g.Name, labelName, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<ContactGroup> GetOrCreateGroupAsync(string labelName, CancellationToken ct)
    {
        var existing = await FindGroupAsync(labelName, ct);
        if (existing != null)
            return existing;

        var createRequest = new CreateContactGroupRequest
        {
            ContactGroup = new ContactGroup { Name = labelName }
        };
        return await _service!.ContactGroups.Create(createRequest).ExecuteAsync(ct);
    }

    /// <summary>Cherche un contact existant par e-mail parmi les connexions. Renvoie son resourceName ou null.</summary>
    private async Task<string?> FindContactResourceNameAsync(string email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var request = _service!.People.Connections.List("people/me");
        request.PersonFields = "emailAddresses";
        request.PageSize = 1000;

        string? pageToken = null;
        do
        {
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(ct);

            var match = response.Connections?.FirstOrDefault(p =>
                p.EmailAddresses != null &&
                p.EmailAddresses.Any(e =>
                    string.Equals(e.Value, email, StringComparison.OrdinalIgnoreCase)));

            if (match != null)
                return match.ResourceName;

            pageToken = response.NextPageToken;
        }
        while (!string.IsNullOrEmpty(pageToken));

        return null;
    }

    private async Task<string> CreateContactAsync(Adherent adherent, CancellationToken ct)
    {
        var person = new Person
        {
            Names = new List<Name>
            {
                new Name { GivenName = adherent.Prenom, FamilyName = adherent.Nom }
            }
        };

        if (!string.IsNullOrWhiteSpace(adherent.Email))
            person.EmailAddresses = new List<EmailAddress> { new EmailAddress { Value = adherent.Email } };

        if (!string.IsNullOrWhiteSpace(adherent.Telephone))
            person.PhoneNumbers = new List<PhoneNumber> { new PhoneNumber { Value = adherent.Telephone } };

        var created = await _service!.People.CreateContact(person).ExecuteAsync(ct);
        return created.ResourceName;
    }
}
