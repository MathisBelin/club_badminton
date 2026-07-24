using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using BadmintonClub.Models;

namespace BadmintonClub.Services;

/// <summary>
/// Lecture des formulaires d'inscription hébergés par l'<b>application web</b> (projet bad-web),
/// via son API d'intégration en lecture seule (en-tête <c>x-api-key</c>).
/// Remplace <see cref="GoogleFormsService"/> comme source des formulaires et des réponses :
/// les objets retournés sont volontairement identiques (<see cref="FormResponseRow"/>,
/// <see cref="GoogleFormsService.FormQuestionInfo"/>) pour que les écrans ne changent pas.
/// </summary>
public sealed class WebFormsService
{
    /// <summary>Adresse de l'application web des formulaires (déploiement de production).</summary>
    public const string DefaultBaseUrl = "https://bad-web-rho.vercel.app";

    // Pas de suivi automatique des redirections : une redirection vers /connexion signifie
    // que le site ne connaît pas (encore) l'API d'intégration — on veut le dire clairement
    // plutôt que de tenter d'analyser une page HTML.
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(30),
    };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly Func<AppSettings> _settings;

    /// <param name="settings">Accès aux paramètres courants (ils peuvent changer en cours de session).</param>
    public WebFormsService(Func<AppSettings> settings) => _settings = settings;

    private string BaseUrl
    {
        get
        {
            var url = _settings().WebFormsUrl;
            return string.IsNullOrWhiteSpace(url) ? DefaultBaseUrl : url.TrimEnd('/');
        }
    }

    private string ApiKey => _settings().WebFormsApiKey ?? string.Empty;

    /// <summary>La liaison est-elle configurée (clé d'API renseignée) ?</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>Adresse publique du site (page d'accueil), pour l'ouvrir dans le navigateur.</summary>
    public string SiteUrl => BaseUrl;

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new WebFormsException(
                "Liaison avec l'application web non configurée : renseignez la clé d'API " +
                "dans Paramètres.");

        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
        request.Headers.Add("x-api-key", ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new WebFormsException("Application web injoignable : vérifiez votre connexion.", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new WebFormsException(DescribeError(response.StatusCode, body));

            try
            {
                var value = JsonSerializer.Deserialize<T>(body, JsonOptions);
                if (value == null)
                    throw new WebFormsException("Réponse inattendue de l'application web.");
                return value;
            }
            catch (JsonException)
            {
                throw new WebFormsException(
                    "Réponse illisible : le site n'expose pas l'API d'intégration. " +
                    "Déployez la dernière version de l'application web.");
            }
        }
    }

    /// <summary>Écriture vers le site (PATCH/DELETE), avec corps JSON facultatif.</summary>
    private async Task SendAsync(HttpMethod method, string path, object? payload, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new WebFormsException(
                "Liaison avec l'application web non configurée : renseignez la clé d'API " +
                "dans Paramètres.");

        using var request = new HttpRequestMessage(method, BaseUrl + path);
        request.Headers.Add("x-api-key", ApiKey);
        if (payload != null)
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new WebFormsException("Application web injoignable : vérifiez votre connexion.", ex);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
                return;
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new WebFormsException(DescribeError(response.StatusCode, body));
        }
    }

    private static string DescribeError(System.Net.HttpStatusCode status, string body) => status switch
    {
        System.Net.HttpStatusCode.Unauthorized => "Clé d'API refusée par l'application web.",
        System.Net.HttpStatusCode.ServiceUnavailable =>
            "L'application web n'a pas de clé d'intégration configurée (INTEGRATION_API_KEY).",
        // Une page HTML (et non du JSON) signifie que la route n'existe pas sur le site :
        // c'est la version déployée qui est trop ancienne, pas la donnée qui manque.
        System.Net.HttpStatusCode.NotFound when IsHtml(body) =>
            "Cette fonction n'existe pas sur le site : déployez la dernière version de " +
            "l'application web (ou pointez le desktop sur votre site local dans Paramètres).",
        System.Net.HttpStatusCode.NotFound =>
            "Introuvable : formulaire ou réponse supprimé(e) sur le site.",
        // 405 : la route existe mais pas cette action (site déployé dans une version antérieure).
        System.Net.HttpStatusCode.MethodNotAllowed =>
            "Cette action n'existe pas encore sur le site : déployez la dernière version de " +
            "l'application web (ou pointez le desktop sur votre site local dans Paramètres).",
        // 3xx : le site redirige vers la page de connexion → l'API d'intégration n'y est pas encore.
        >= System.Net.HttpStatusCode.MultipleChoices and < System.Net.HttpStatusCode.BadRequest =>
            "Le site ne propose pas encore l'API d'intégration (déployez la dernière version " +
            "de l'application web).",
        _ => $"Erreur de l'application web ({(int)status}). {Truncate(body)}",
    };

    private static string Truncate(string s) => s.Length <= 200 ? s : s[..200] + "…";

    /// <summary>Le site a-t-il renvoyé une page HTML au lieu du JSON attendu ?</summary>
    private static bool IsHtml(string body)
        => body.TrimStart().StartsWith("<", StringComparison.Ordinal);

    // ---- Formulaires ------------------------------------------------------

    /// <summary>
    /// Formulaires créés par le compte Google donné (vide = tous), convertis en
    /// <see cref="FormRecord"/> pour être affichés comme les anciens Google Forms.
    /// La correspondance champ↔question et les règles de réponses sont déduites de la
    /// configuration faite dans l'application web (aucun réglage local à refaire).
    /// </summary>
    public async Task<List<FormRecord>> ListFormsAsync(string ownerEmail, CancellationToken ct = default)
    {
        var query = string.IsNullOrWhiteSpace(ownerEmail)
            ? string.Empty
            : "?owner=" + Uri.EscapeDataString(ownerEmail);
        var payload = await GetAsync<FormsPayload>("/api/integration/forms" + query, ct);

        return payload.Forms.Select(f => new FormRecord
        {
            FormId = f.Id,
            Nom = f.Title,
            EditUrl = $"{BaseUrl}/admin/forms/{f.Id}/edit",
            ResponderUri = $"{BaseUrl}/forms/{f.Id}",
            DateCreation = Local(f.CreatedAt),
            // Le libellé Contacts est choisi sur le SITE, à la création du formulaire.
            LabelName = f.LabelName,
            LabelResourceName = f.LabelResourceName,
        }).ToList();
    }

    /// <summary>
    /// Supprime la préinscription d'une personne sur le site : elle n'est plus inscrite
    /// et peut de nouveau remplir le formulaire.
    /// </summary>
    public Task DeleteResponseAsync(string formId, string responseId, CancellationToken ct = default)
        => SendAsync(HttpMethod.Delete,
            $"/api/integration/forms/{Uri.EscapeDataString(formId)}/responses/{Uri.EscapeDataString(responseId)}",
            payload: null, ct);

    /// <summary>
    /// Corrige la réponse d'une personne (« garder l'état actuel » : la valeur du contact
    /// remplace celle qu'elle avait saisie).
    /// </summary>
    public Task UpdateResponseAnswersAsync(string formId, string responseId,
        IReadOnlyDictionary<string, string> answers, CancellationToken ct = default)
        => SendAsync(HttpMethod.Patch,
            $"/api/integration/forms/{Uri.EscapeDataString(formId)}/responses/{Uri.EscapeDataString(responseId)}",
            new { answers }, ct);

    /// <summary>
    /// Questions d'un formulaire web + correspondances de contact et règles de réponses,
    /// appliquées au <see cref="FormRecord"/> fourni (FieldMap / AnswerRules).
    /// </summary>
    public async Task<List<GoogleFormsService.FormQuestionInfo>> GetFormQuestionsAsync(
        FormRecord form, CancellationToken ct = default)
    {
        var payload = await GetAsync<QuestionsPayload>(
            $"/api/integration/forms/{Uri.EscapeDataString(form.FormId)}/questions", ct);

        form.Nom = payload.Title;
        form.FieldMap = new Dictionary<string, string>();
        form.AnswerRules = new Dictionary<string, string>();

        var questions = new List<GoogleFormsService.FormQuestionInfo>();
        foreach (var q in payload.Questions)
        {
            questions.Add(new GoogleFormsService.FormQuestionInfo(q.QuestionId, q.Title));

            // Champ de contact associé côté web → correspondance attendue par les écrans.
            var field = ContactField(q.ContactField);
            if (field != null && !form.FieldMap.ContainsKey(field))
                form.FieldMap[field] = q.QuestionId;

            // Option marquée « liste d'attente » côté web → règle de réponse locale.
            var actions = q.OptionActions ?? Array.Empty<string>();
            for (var i = 0; i < (q.Options?.Length ?? 0); i++)
            {
                if (i < actions.Length && string.Equals(actions[i], "WAITLIST", StringComparison.OrdinalIgnoreCase))
                    form.AnswerRules[FormRecord.RuleKey(q.QuestionId, q.Options![i])] = "waitlist";
            }
        }
        return questions;
    }

    /// <summary>Réponses d'un formulaire web, regroupées par e-mail vérifié du répondant.</summary>
    public async Task<List<FormResponseRow>> ListResponsesAsync(string formId, CancellationToken ct = default)
    {
        var payload = await GetAsync<ResponsesPayload>(
            $"/api/integration/forms/{Uri.EscapeDataString(formId)}/responses", ct);

        var rows = new List<FormResponseRow>();
        foreach (var r in payload.Responses)
        {
            var row = new FormResponseRow
            {
                ResponseId = r.ResponseId,
                RespondentEmail = r.RespondentEmail,
                CreatedAt = Local(r.SubmittedAt),
                SubmittedAt = Local(r.LastSubmittedAt),
            };
            foreach (var kv in r.Fields)
                row.Fields[kv.Key] = kv.Value;
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>
    /// Le site renvoie ses dates en <b>UTC</b> (ISO 8601 « …Z ») : on les ramène à l'heure locale.
    /// Sans cela elles s'affichent décalées et se comparent mal aux dates locales de l'application
    /// (ex. la date de validation), ce qui empêchait une nouvelle réponse de faire réapparaître
    /// une personne dans la liste.
    /// </summary>
    private static DateTime Local(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;

    /// <summary>
    /// Champ de contact web → clé utilisée par <see cref="FormRecord.FieldMap"/>.
    /// Ces clés doivent rester identiques à celles de la configuration locale
    /// (« prenom », « nom », « tel », « email », « secondaryEmails ») : ce sont elles que
    /// la page des réponses interroge pour extraire les valeurs saisies.
    /// </summary>
    private static string? ContactField(string? field) => field switch
    {
        "FIRST_NAME" => "prenom",
        "LAST_NAME" => "nom",
        "PHONE" => "tel",
        "EMAIL" => "email",
        "SECONDARY_EMAIL" => "secondaryEmails",
        _ => null,
    };

    // ---- Formes JSON de l'API --------------------------------------------

    private sealed class FormsPayload
    {
        [JsonPropertyName("forms")] public FormDto[] Forms { get; set; } = Array.Empty<FormDto>();
    }

    private sealed class FormDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string OwnerEmail { get; set; } = string.Empty;
        public bool IsPublished { get; set; }
        public DateTime CreatedAt { get; set; }
        public int ResponseCount { get; set; }

        /// <summary>Libellé Contacts associé au formulaire (choisi sur le site).</summary>
        public string LabelResourceName { get; set; } = string.Empty;
        public string LabelName { get; set; } = string.Empty;
    }

    private sealed class QuestionsPayload
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public QuestionDto[] Questions { get; set; } = Array.Empty<QuestionDto>();
    }

    private sealed class QuestionDto
    {
        public string QuestionId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool Required { get; set; }
        public string[]? Options { get; set; }
        public string[]? OptionActions { get; set; }
        public string? Format { get; set; }
        public string? ContactField { get; set; }
    }

    private sealed class ResponsesPayload
    {
        public ResponseDto[] Responses { get; set; } = Array.Empty<ResponseDto>();
    }

    private sealed class ResponseDto
    {
        public string ResponseId { get; set; } = string.Empty;
        public string RespondentEmail { get; set; } = string.Empty;
        public string RespondentName { get; set; } = string.Empty;
        public DateTime SubmittedAt { get; set; }
        public DateTime LastSubmittedAt { get; set; }
        public DateTime? WaitlistedAt { get; set; }
        public Dictionary<string, string> Fields { get; set; } = new();
    }
}

/// <summary>Erreur de dialogue avec l'application web (message déjà en français).</summary>
public sealed class WebFormsException : Exception
{
    public WebFormsException(string message, Exception? inner = null) : base(message, inner) { }
}
