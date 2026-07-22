using Google.Apis.Drive.v3;
using Google.Apis.Forms.v1;
using Google.Apis.Forms.v1.Data;
using Google.Apis.Services;
using BadmintonClub.Models;

namespace BadmintonClub.Services;

/// <summary>Une réponse brute lue dans un formulaire (une soumission).</summary>
public sealed class FormResponseRow
{
    public string ResponseId { get; set; } = string.Empty;
    /// <summary>E-mail vérifié du répondant (identité Google), si la collecte est active.</summary>
    public string RespondentEmail { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime SubmittedAt { get; set; }
    /// <summary>Réponses par id de question (ou clé selon le mapping fourni à la lecture).</summary>
    public Dictionary<string, string> Fields { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Accès aux Google Forms : liste (via Drive), création (vierge / duplication de modèle), renommage,
/// suppression, lecture des questions et des réponses.
/// Scopes partagés : forms.body (créer/éditer) + forms.responses.readonly (lire les réponses) + drive.
/// </summary>
public class GoogleFormsService
{
    private static readonly string[] Scopes = GoogleAuth.AllScopes;
    private const string AppName = "BadmintonClub";
    private const string User = GoogleAuth.SharedUser;

    private FormsService CreateService(Google.Apis.Auth.OAuth2.UserCredential credential) =>
        new(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = AppName
        });

    /// <summary>
    /// Lit toutes les réponses d'un formulaire et les mappe selon le dictionnaire fourni
    /// (id de question → clé). Renseigne aussi l'e-mail vérifié du répondant s'il est collecté.
    /// </summary>
    public async Task<List<FormResponseRow>> ListResponsesAsync(
        string formId, IReadOnlyDictionary<string, string> questionIds, CancellationToken ct = default)
    {
        var credential = await GoogleAuth.AuthorizeAsync(Scopes, User, ct);
        try
        {
            using var service = CreateService(credential);

            // Id de question → clé de champ (mapping inverse).
            var keyByQid = questionIds
                .GroupBy(kv => kv.Value, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.Ordinal);

            var result = new List<FormResponseRow>();
            string? pageToken = null;
            do
            {
                var request = service.Forms.Responses.List(formId);
                request.PageSize = 5000;
                if (pageToken != null)
                    request.PageToken = pageToken;
                var page = await request.ExecuteAsync(ct);

                foreach (var r in page.Responses ?? new List<FormResponse>())
                {
                    var row = new FormResponseRow
                    {
                        ResponseId = r.ResponseId ?? string.Empty,
                        RespondentEmail = r.RespondentEmail ?? string.Empty,
                        CreatedAt = r.CreateTimeDateTimeOffset?.LocalDateTime ?? DateTime.Now,
                        SubmittedAt = r.LastSubmittedTimeDateTimeOffset?.LocalDateTime ?? DateTime.Now
                    };

                    foreach (var (qid, answer) in r.Answers ?? new Dictionary<string, Answer>())
                    {
                        if (!keyByQid.TryGetValue(qid, out var key))
                            continue;
                        var value = answer.TextAnswers?.Answers?.FirstOrDefault()?.Value ?? string.Empty;
                        row.Fields[key] = value;
                    }

                    result.Add(row);
                }

                pageToken = page.NextPageToken;
            } while (!string.IsNullOrEmpty(pageToken));

            return result;
        }
        catch (GoogleSyncException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw GoogleErrors.Translate(ex, "Impossible de lire les réponses du formulaire.");
        }
    }

    /// <summary>Une question d'un formulaire (pour l'affichage des réponses / le mapping).</summary>
    public sealed record FormQuestionInfo(string QuestionId, string Title);

    /// <summary>Détail d'une question (type + options) pour la configuration du formulaire.</summary>
    public sealed record FormQuestionDetail(string QuestionId, string Title, bool IsSingleChoice, bool IsText, IReadOnlyList<string> Options);

    /// <summary>Lit les questions détaillées (type + options) d'un formulaire.</summary>
    public async Task<List<FormQuestionDetail>> GetFormDetailAsync(string formId, CancellationToken ct = default)
    {
        var credential = await GoogleAuth.AuthorizeAsync(Scopes, User, ct);
        try
        {
            using var service = CreateService(credential);
            var form = await service.Forms.Get(formId).ExecuteAsync(ct);

            var result = new List<FormQuestionDetail>();
            foreach (var item in form.Items ?? new List<Item>())
            {
                var q = item.QuestionItem?.Question;
                var qid = q?.QuestionId;
                if (string.IsNullOrEmpty(qid))
                    continue;

                var single = string.Equals(q!.ChoiceQuestion?.Type, "RADIO", StringComparison.OrdinalIgnoreCase);
                var isText = q.TextQuestion != null;
                var options = q.ChoiceQuestion?.Options?
                    .Select(o => o.Value ?? string.Empty)
                    .Where(v => v.Length > 0).ToList() ?? new List<string>();

                result.Add(new FormQuestionDetail(qid, item.Title ?? string.Empty, single, isText, options));
            }
            return result;
        }
        catch (GoogleSyncException) { throw; }
        catch (Exception ex) { throw GoogleErrors.Translate(ex, "Impossible de lire la configuration du formulaire."); }
    }

    /// <summary>
    /// Active la collecte des adresses e-mail **vérifiées** sur un formulaire (best-effort, appelé à la
    /// création). Les répondants devront se connecter : les réponses portent alors un e-mail vérifié,
    /// ce qui fiabilise le regroupement par personne.
    /// </summary>
    private async Task TryEnableVerifiedEmailAsync(FormsService service, string formId, CancellationToken ct)
    {
        try
        {
            await service.Forms.BatchUpdate(new BatchUpdateFormRequest
            {
                Requests = new List<Request>
                {
                    new Request
                    {
                        UpdateSettings = new UpdateSettingsRequest
                        {
                            Settings = new FormSettings { EmailCollectionType = "VERIFIED" },
                            UpdateMask = "emailCollectionType"
                        }
                    }
                }
            }, formId).ExecuteAsync(ct);
        }
        catch (Google.GoogleApiException) { /* non bloquant : le formulaire est créé même si le réglage échoue */ }
    }

    /// <summary>Liste, dans l'ordre, les questions (id + intitulé) d'un formulaire.</summary>
    public async Task<List<FormQuestionInfo>> GetFormQuestionsAsync(string formId, CancellationToken ct = default)
    {
        var credential = await GoogleAuth.AuthorizeAsync(Scopes, User, ct);
        try
        {
            using var service = CreateService(credential);
            var form = await service.Forms.Get(formId).ExecuteAsync(ct);

            var result = new List<FormQuestionInfo>();
            foreach (var item in form.Items ?? new List<Item>())
            {
                var qid = item.QuestionItem?.Question?.QuestionId;
                if (!string.IsNullOrEmpty(qid))
                    result.Add(new FormQuestionInfo(qid, item.Title ?? string.Empty));
            }
            return result;
        }
        catch (GoogleSyncException) { throw; }
        catch (Exception ex) { throw GoogleErrors.Translate(ex, "Impossible de lire les questions du formulaire."); }
    }

    // ======================================================================
    // Gestion générale des Google Forms (page « Google Forms »)
    // ======================================================================

    private DriveService CreateDrive(Google.Apis.Auth.OAuth2.UserCredential credential) =>
        new(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = AppName
        });

    private static string EditUrlOf(string formId) => $"https://docs.google.com/forms/d/{formId}/edit";

    /// <summary>Liste les Google Forms accessibles au compte (via Drive), non supprimés.</summary>
    public async Task<List<FormRecord>> ListFormsAsync(CancellationToken ct = default)
    {
        var credential = await GoogleAuth.AuthorizeAsync(Scopes, User, ct);
        try
        {
            using var drive = CreateDrive(credential);

            var result = new List<FormRecord>();
            string? pageToken = null;
            do
            {
                var request = drive.Files.List();
                request.Q = "mimeType='application/vnd.google-apps.form' and trashed=false";
                request.Fields = "nextPageToken, files(id, name, createdTime)";
                request.Spaces = "drive";
                request.PageSize = 1000;
                request.OrderBy = "createdTime desc";
                request.IncludeItemsFromAllDrives = true;
                request.SupportsAllDrives = true;
                request.PageToken = pageToken;

                var response = await request.ExecuteAsync(ct);
                if (response.Files != null)
                    foreach (var f in response.Files)
                        result.Add(new FormRecord
                        {
                            FormId = f.Id,
                            Nom = f.Name,
                            EditUrl = EditUrlOf(f.Id),
                            DateCreation = f.CreatedTimeDateTimeOffset?.LocalDateTime ?? DateTime.Now
                        });

                pageToken = response.NextPageToken;
            } while (!string.IsNullOrEmpty(pageToken));

            return result;
        }
        catch (GoogleSyncException) { throw; }
        catch (Exception ex) { throw GoogleErrors.Translate(ex, "Impossible de récupérer la liste des Google Forms."); }
    }

    /// <summary>Crée un formulaire vierge et renvoie son enregistrement local.</summary>
    public async Task<FormRecord> CreateBlankFormAsync(string title, CancellationToken ct = default)
    {
        var credential = await GoogleAuth.AuthorizeAsync(Scopes, User, ct);
        try
        {
            using var service = CreateService(credential);
            var form = await service.Forms.Create(new Form
            {
                Info = new Info { Title = title, DocumentTitle = title }
            }).ExecuteAsync(ct);

            // Collecte d'e-mail vérifié activée dès la création (regroupement fiable des réponses).
            await TryEnableVerifiedEmailAsync(service, form.FormId, ct);

            return new FormRecord
            {
                FormId = form.FormId,
                Nom = title,
                EditUrl = EditUrlOf(form.FormId),
                ResponderUri = form.ResponderUri ?? string.Empty,
                DateCreation = DateTime.Now
            };
        }
        catch (GoogleSyncException) { throw; }
        catch (Exception ex) { throw GoogleErrors.Translate(ex, "Impossible de créer le Google Form."); }
    }

    /// <summary>
    /// Lit la structure d'un formulaire et la convertit en modèle réutilisable (titre + questions
    /// gérées : texte court/long, choix unique/multiple, liste déroulante, date). Lecture seule.
    /// </summary>
    public async Task<FormTemplate> ExportFormStructureAsync(string formId, string modelName, CancellationToken ct = default)
    {
        var credential = await GoogleAuth.AuthorizeAsync(Scopes, User, ct);
        try
        {
            using var service = CreateService(credential);
            var form = await service.Forms.Get(formId).ExecuteAsync(ct);

            var tpl = new FormTemplate { Name = modelName };
            foreach (var item in form.Items ?? new List<Item>())
            {
                var q = item.QuestionItem?.Question;
                if (q == null)
                    continue; // on ignore les éléments non-question (texte, image, saut de page…)

                string? type = null;
                var options = new List<string>();

                if (q.TextQuestion != null)
                    type = q.TextQuestion.Paragraph == true ? "PARAGRAPH" : "TEXT";
                else if (q.ChoiceQuestion != null)
                {
                    type = (q.ChoiceQuestion.Type ?? "RADIO").ToUpperInvariant() switch
                    {
                        "CHECKBOX" => "CHECKBOX",
                        "DROP_DOWN" => "DROP_DOWN",
                        _ => "RADIO"
                    };
                    options = q.ChoiceQuestion.Options?
                        .Select(o => o.Value ?? string.Empty)
                        .Where(v => v.Length > 0).ToList() ?? new List<string>();
                }
                else if (q.DateQuestion != null)
                    type = "DATE";

                if (type == null)
                    continue; // type non géré : ignoré

                tpl.Items.Add(new FormTemplateItem
                {
                    Title = item.Title ?? string.Empty,
                    Description = item.Description ?? string.Empty,
                    Type = type,
                    Required = q.Required ?? false,
                    Options = options
                });
            }

            return tpl;
        }
        catch (GoogleSyncException) { throw; }
        catch (Exception ex) { throw GoogleErrors.Translate(ex, "Impossible de lire la structure du formulaire."); }
    }

    /// <summary>Crée un nouveau Google Form à partir d'un modèle local (recrée les questions via l'API).</summary>
    public async Task<FormRecord> CreateFormFromTemplateAsync(FormTemplate template, string newTitle, CancellationToken ct = default)
    {
        var credential = await GoogleAuth.AuthorizeAsync(Scopes, User, ct);
        try
        {
            using var service = CreateService(credential);

            // 1) Formulaire vierge (l'API n'accepte que Info à la création).
            var form = await service.Forms.Create(new Form
            {
                Info = new Info { Title = newTitle, DocumentTitle = newTitle }
            }).ExecuteAsync(ct);

            // 2) Ajout des questions du modèle en un seul batchUpdate.
            var requests = new List<Request>();
            var index = 0;
            foreach (var it in template.Items)
            {
                var question = BuildQuestion(it);
                if (question == null)
                    continue;

                requests.Add(new Request
                {
                    CreateItem = new CreateItemRequest
                    {
                        Item = new Item
                        {
                            Title = it.Title,
                            Description = string.IsNullOrWhiteSpace(it.Description) ? null : it.Description,
                            QuestionItem = new QuestionItem { Question = question }
                        },
                        Location = new Location { Index = index++ }
                    }
                });
            }

            if (requests.Count > 0)
                await service.Forms.BatchUpdate(new BatchUpdateFormRequest { Requests = requests }, form.FormId)
                    .ExecuteAsync(ct);

            // Collecte d'e-mail vérifié activée dès la création (regroupement fiable des réponses).
            await TryEnableVerifiedEmailAsync(service, form.FormId, ct);

            var responder = string.Empty;
            try
            {
                var f = await service.Forms.Get(form.FormId).ExecuteAsync(ct);
                responder = f.ResponderUri ?? string.Empty;
            }
            catch (Google.GoogleApiException) { /* lien de réponse non critique */ }

            return new FormRecord
            {
                FormId = form.FormId,
                Nom = newTitle,
                EditUrl = EditUrlOf(form.FormId),
                ResponderUri = responder,
                DateCreation = DateTime.Now
            };
        }
        catch (GoogleSyncException) { throw; }
        catch (Exception ex) { throw GoogleErrors.Translate(ex, "Impossible de créer le formulaire depuis le modèle."); }
    }

    /// <summary>Construit la question API correspondant à un élément de modèle (null si type non géré).</summary>
    private static Question? BuildQuestion(FormTemplateItem it)
    {
        var q = new Question { Required = it.Required };
        switch (it.Type?.ToUpperInvariant())
        {
            case "TEXT":
                q.TextQuestion = new TextQuestion { Paragraph = false };
                return q;
            case "PARAGRAPH":
                q.TextQuestion = new TextQuestion { Paragraph = true };
                return q;
            case "RADIO":
            case "CHECKBOX":
            case "DROP_DOWN":
                q.ChoiceQuestion = new ChoiceQuestion
                {
                    Type = it.Type.ToUpperInvariant(),
                    Options = (it.Options ?? new List<string>())
                        .Where(o => !string.IsNullOrWhiteSpace(o))
                        .Select(o => new Option { Value = o }).ToList()
                };
                return q.ChoiceQuestion.Options.Count > 0 ? q : null;
            case "DATE":
                q.DateQuestion = new DateQuestion { IncludeYear = true };
                return q;
            default:
                return null;
        }
    }

    /// <summary>Duplique un formulaire modèle (copie Drive) et renomme la copie.</summary>
    public async Task<FormRecord> CopyFormAsync(string templateFormId, string newTitle, CancellationToken ct = default)
    {
        var credential = await GoogleAuth.AuthorizeAsync(Scopes, User, ct);
        try
        {
            using var drive = CreateDrive(credential);
            var copy = await drive.Files.Copy(
                new Google.Apis.Drive.v3.Data.File { Name = newTitle }, templateFormId).ExecuteAsync(ct);

            var newId = copy.Id;
            var responder = string.Empty;

            // Aligne le titre interne du formulaire et récupère le lien de réponse (best-effort).
            using var service = CreateService(credential);
            try
            {
                await service.Forms.BatchUpdate(new BatchUpdateFormRequest
                {
                    Requests = new List<Request>
                    {
                        new Request
                        {
                            UpdateFormInfo = new UpdateFormInfoRequest
                            {
                                Info = new Info { Title = newTitle },
                                UpdateMask = "title"
                            }
                        }
                    }
                }, newId).ExecuteAsync(ct);

                var f = await service.Forms.Get(newId).ExecuteAsync(ct);
                responder = f.ResponderUri ?? string.Empty;
            }
            catch (Google.GoogleApiException) { /* titre interne non critique */ }

            return new FormRecord
            {
                FormId = newId,
                Nom = newTitle,
                EditUrl = EditUrlOf(newId),
                ResponderUri = responder,
                DateCreation = DateTime.Now
            };
        }
        catch (GoogleSyncException) { throw; }
        catch (Exception ex) { throw GoogleErrors.Translate(ex, "Impossible de dupliquer le formulaire modèle."); }
    }

    /// <summary>Renomme un formulaire (nom Drive + titre interne).</summary>
    public async Task RenameFormAsync(string formId, string newName, CancellationToken ct = default)
    {
        var credential = await GoogleAuth.AuthorizeAsync(Scopes, User, ct);
        try
        {
            using var drive = CreateDrive(credential);
            await drive.Files.Update(new Google.Apis.Drive.v3.Data.File { Name = newName }, formId).ExecuteAsync(ct);

            using var service = CreateService(credential);
            try
            {
                await service.Forms.BatchUpdate(new BatchUpdateFormRequest
                {
                    Requests = new List<Request>
                    {
                        new Request
                        {
                            UpdateFormInfo = new UpdateFormInfoRequest
                            {
                                Info = new Info { Title = newName },
                                UpdateMask = "title"
                            }
                        }
                    }
                }, formId).ExecuteAsync(ct);
            }
            catch (Google.GoogleApiException) { /* titre interne non critique */ }
        }
        catch (GoogleSyncException) { throw; }
        catch (Exception ex) { throw GoogleErrors.Translate(ex, "Impossible de renommer le Google Form."); }
    }

    /// <summary>Supprime définitivement un Google Form (via Drive).</summary>
    public async Task DeleteFormAsync(string formId, CancellationToken ct = default)
    {
        var credential = await GoogleAuth.AuthorizeAsync(Scopes, User, ct);
        try
        {
            using var drive = CreateDrive(credential);
            await drive.Files.Delete(formId).ExecuteAsync(ct);
        }
        catch (Google.GoogleApiException apiEx)
            when (apiEx.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Déjà supprimé côté Google : on considère l'opération réussie.
        }
        catch (GoogleSyncException) { throw; }
        catch (Exception ex) { throw GoogleErrors.Translate(ex, "Impossible de supprimer le Google Form."); }
    }
}
