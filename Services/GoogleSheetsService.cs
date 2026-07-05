using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

namespace BadmintonClub.Services;

/// <summary>Résultat de la création d'un Google Sheet.</summary>
public sealed record CreatedSheet(string SpreadsheetId, string Url);

/// <summary>Options de création d'un Google Sheet.</summary>
public sealed class SheetCreateOptions
{
    public string Title { get; set; } = "Nouveau classeur";
    public string? ShareWithEmail { get; set; }

    /// <summary>Accessible par toute personne disposant du lien.</summary>
    public bool LinkAccess { get; set; } = true;

    /// <summary>Rôle du lien : "reader", "commenter" ou "writer".</summary>
    public string LinkRole { get; set; } = "writer";

    /// <summary>
    /// Chemin d'un fichier modèle (Excel/CSV) : s'il est renseigné, le classeur est créé
    /// en clonant ce fichier (téléversé vers Drive et converti en Google Sheet). Null = vierge.
    /// </summary>
    public string? TemplateFilePath { get; set; }
}

/// <summary>État de partage « lien » d'un classeur.</summary>
public sealed record SheetSharing(bool LinkAccess, string Role);

/// <summary>
/// Création de Google Sheets partagés.
/// Scopes : création/édition de classeurs (spreadsheets) + gestion des fichiers
/// créés par l'application (drive.file, suffisant pour partager ses propres fichiers).
/// </summary>
public class GoogleSheetsService
{
    // Autorisation unique partagée avec les Contacts → un seul jeton, un seul consentement.
    private static readonly string[] Scopes = GoogleAuth.AllScopes;
    private const string AppName = "BadmintonClub";
    private const string User = GoogleAuth.SharedUser;

    /// <summary>
    /// Crée un Google Sheet vierge, le partage en « tout le monde avec le lien = Éditeur »,
    /// le partage aussi directement (en éditeur, avec e-mail d'invitation) avec l'adresse
    /// <paramref name="shareWithEmail"/> si elle est renseignée, et renvoie son URL.
    /// </summary>
    public async Task<CreatedSheet> CreateSharedSpreadsheetAsync(
        SheetCreateOptions options, CancellationToken ct = default)
    {
        var credential = await GoogleAuth.AuthorizeAsync(Scopes, User, ct);

        try
        {
            using var sheets = new SheetsService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = AppName
            });

            using var drive = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = AppName
            });

            string spreadsheetId;
            string? webViewLink;

            if (!string.IsNullOrEmpty(options.TemplateFilePath))
            {
                // Clone : le fichier modèle est téléversé et converti en Google Sheet.
                (spreadsheetId, webViewLink) =
                    await UploadAsSpreadsheetAsync(drive, options.Title, options.TemplateFilePath!, ct);
            }
            else
            {
                var spreadsheet = new Spreadsheet
                {
                    Properties = new SpreadsheetProperties { Title = options.Title }
                };
                var created = await sheets.Spreadsheets.Create(spreadsheet).ExecuteAsync(ct);
                spreadsheetId = created.SpreadsheetId;
                webViewLink = created.SpreadsheetUrl;
            }

            // Partage par lien selon le rôle choisi (accessible uniquement via le lien).
            if (options.LinkAccess)
            {
                var permission = new Permission
                {
                    Type = "anyone",
                    Role = options.LinkRole,
                    AllowFileDiscovery = false
                };
                await drive.Permissions.Create(permission, spreadsheetId).ExecuteAsync(ct);
            }

            // Partage nominatif avec l'e-mail du club (invitation par e-mail).
            if (!string.IsNullOrWhiteSpace(options.ShareWithEmail))
            {
                var clubPermission = new Permission
                {
                    Type = "user",
                    Role = "writer",
                    EmailAddress = options.ShareWithEmail
                };
                var clubRequest = drive.Permissions.Create(clubPermission, spreadsheetId);
                clubRequest.SendNotificationEmail = true;
                await clubRequest.ExecuteAsync(ct);
            }

            var url = webViewLink
                      ?? $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/edit";
            return new CreatedSheet(spreadsheetId, url);
        }
        catch (GoogleSyncException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw GoogleErrors.Translate(ex, "Impossible de créer le Google Sheet.");
        }
    }

    /// <summary>
    /// Liste les Google Sheets créés par l'application (visibles via le scope drive.file),
    /// non supprimés. Sert à synchroniser le registre local avec l'état réel en ligne.
    /// </summary>
    public async Task<List<Models.SheetRecord>> ListSpreadsheetsAsync(CancellationToken ct = default)
    {
        var credential = await GoogleAuth.AuthorizeAsync(Scopes, User, ct);

        try
        {
            using var drive = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = AppName
            });

            var result = new List<Models.SheetRecord>();
            string? pageToken = null;

            do
            {
                var request = drive.Files.List();
                // Tous les classeurs accessibles au compte (les tiens + ceux partagés avec toi).
                request.Q = "mimeType='application/vnd.google-apps.spreadsheet' and trashed=false";
                request.Fields = "nextPageToken, files(id, name, createdTime, webViewLink)";
                request.Spaces = "drive";
                request.PageSize = 1000;
                request.OrderBy = "createdTime desc";
                request.IncludeItemsFromAllDrives = true;
                request.SupportsAllDrives = true;
                request.PageToken = pageToken;

                var response = await request.ExecuteAsync(ct);

                if (response.Files != null)
                {
                    foreach (var f in response.Files)
                    {
                        result.Add(new Models.SheetRecord
                        {
                            SpreadsheetId = f.Id,
                            Nom = f.Name,
                            Url = f.WebViewLink
                                  ?? $"https://docs.google.com/spreadsheets/d/{f.Id}/edit",
                            DateCreation = f.CreatedTimeDateTimeOffset?.LocalDateTime ?? DateTime.Now
                        });
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
            throw GoogleErrors.Translate(ex, "Impossible de récupérer la liste des Google Sheets.");
        }
    }

    /// <summary>Téléverse un fichier (Excel/CSV) et le convertit en Google Sheet ; renvoie (id, lien).</summary>
    private static async Task<(string Id, string? Link)> UploadAsSpreadsheetAsync(
        DriveService drive, string title, string path, CancellationToken ct)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        var sourceMime = ext switch
        {
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".xls" => "application/vnd.ms-excel",
            ".csv" => "text/csv",
            _ => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        };

        var metadata = new Google.Apis.Drive.v3.Data.File
        {
            Name = title,
            MimeType = "application/vnd.google-apps.spreadsheet" // déclenche la conversion
        };

        await using var stream = System.IO.File.OpenRead(path);
        var request = drive.Files.Create(metadata, stream, sourceMime);
        request.Fields = "id, webViewLink";

        var progress = await request.UploadAsync(ct);
        if (progress.Status != Google.Apis.Upload.UploadStatus.Completed)
            throw progress.Exception ?? new Exception("Échec du téléversement du modèle.");

        var body = request.ResponseBody;
        return (body.Id, body.WebViewLink);
    }

    /// <summary>Lit l'état de partage « lien » d'un classeur.</summary>
    public async Task<SheetSharing> GetSharingAsync(string spreadsheetId, CancellationToken ct = default)
    {
        var credential = await GoogleAuth.AuthorizeAsync(Scopes, User, ct);
        try
        {
            using var drive = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = AppName
            });

            var list = drive.Permissions.List(spreadsheetId);
            list.Fields = "permissions(id,type,role)";
            var response = await list.ExecuteAsync(ct);

            var anyone = response.Permissions?.FirstOrDefault(p => p.Type == "anyone");
            return new SheetSharing(anyone != null, anyone?.Role ?? "writer");
        }
        catch (GoogleSyncException) { throw; }
        catch (Exception ex) { throw GoogleErrors.Translate(ex, "Impossible de lire le partage du classeur."); }
    }

    /// <summary>Définit le partage « lien » (activé/désactivé + rôle reader/commenter/writer).</summary>
    public async Task SetSharingAsync(string spreadsheetId, bool linkAccess, string role, CancellationToken ct = default)
    {
        var credential = await GoogleAuth.AuthorizeAsync(Scopes, User, ct);
        try
        {
            using var drive = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = AppName
            });

            var list = drive.Permissions.List(spreadsheetId);
            list.Fields = "permissions(id,type,role)";
            var response = await list.ExecuteAsync(ct);
            var anyone = response.Permissions?.FirstOrDefault(p => p.Type == "anyone");

            if (linkAccess)
            {
                if (anyone == null)
                {
                    var permission = new Permission { Type = "anyone", Role = role, AllowFileDiscovery = false };
                    await drive.Permissions.Create(permission, spreadsheetId).ExecuteAsync(ct);
                }
                else if (anyone.Role != role)
                {
                    await drive.Permissions.Update(new Permission { Role = role }, spreadsheetId, anyone.Id).ExecuteAsync(ct);
                }
            }
            else if (anyone != null)
            {
                await drive.Permissions.Delete(spreadsheetId, anyone.Id).ExecuteAsync(ct);
            }
        }
        catch (GoogleSyncException) { throw; }
        catch (Exception ex) { throw GoogleErrors.Translate(ex, "Impossible de modifier le partage du classeur."); }
    }

    /// <summary>Lit une plage de cellules (ex. « A1:F110 ») et renvoie les lignes de texte.</summary>
    public async Task<List<string[]>> ReadRowsAsync(string spreadsheetId, string range, CancellationToken ct = default)
    {
        var credential = await GoogleAuth.AuthorizeAsync(Scopes, User, ct);
        try
        {
            using var sheets = new SheetsService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = AppName
            });

            var response = await sheets.Spreadsheets.Values.Get(spreadsheetId, range).ExecuteAsync(ct);
            var rows = new List<string[]>();
            if (response.Values != null)
                foreach (var row in response.Values)
                    rows.Add(row.Select(c => c?.ToString() ?? string.Empty).ToArray());
            return rows;
        }
        catch (GoogleSyncException) { throw; }
        catch (Exception ex) { throw GoogleErrors.Translate(ex, "Impossible de lire la plage du Sheet."); }
    }

    /// <summary>Exporte le classeur en CSV (première feuille) vers un fichier local.</summary>
    public async Task DownloadCsvAsync(string spreadsheetId, string destinationPath, CancellationToken ct = default)
    {
        var credential = await GoogleAuth.AuthorizeAsync(Scopes, User, ct);
        try
        {
            using var drive = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = AppName
            });

            var request = drive.Files.Export(spreadsheetId, "text/csv");
            await using var fs = System.IO.File.Create(destinationPath);
            await request.DownloadAsync(fs, ct);
        }
        catch (GoogleSyncException) { throw; }
        catch (Exception ex) { throw GoogleErrors.Translate(ex, "Impossible d'exporter le classeur en CSV."); }
    }

    /// <summary>Exporte le classeur au format Excel (.xlsx) vers un fichier local.</summary>
    public async Task DownloadXlsxAsync(string spreadsheetId, string destinationPath, CancellationToken ct = default)
    {
        var credential = await GoogleAuth.AuthorizeAsync(Scopes, User, ct);
        try
        {
            using var drive = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = AppName
            });

            var request = drive.Files.Export(
                spreadsheetId, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            await using var fs = System.IO.File.Create(destinationPath);
            await request.DownloadAsync(fs, ct);
        }
        catch (GoogleSyncException) { throw; }
        catch (Exception ex) { throw GoogleErrors.Translate(ex, "Impossible d'exporter le classeur en Excel."); }
    }

    /// <summary>Renomme le classeur (titre du fichier Drive).</summary>
    public async Task RenameAsync(string spreadsheetId, string newName, CancellationToken ct = default)
    {
        var credential = await GoogleAuth.AuthorizeAsync(Scopes, User, ct);
        try
        {
            using var drive = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = AppName
            });

            var body = new Google.Apis.Drive.v3.Data.File { Name = newName };
            var request = drive.Files.Update(body, spreadsheetId);
            request.Fields = "id,name";
            await request.ExecuteAsync(ct);
        }
        catch (GoogleSyncException) { throw; }
        catch (Exception ex) { throw GoogleErrors.Translate(ex, "Impossible de renommer le classeur."); }
    }

    /// <summary>Supprime définitivement un Google Sheet créé par l'application.</summary>
    public async Task DeleteSpreadsheetAsync(string spreadsheetId, CancellationToken ct = default)
    {
        var credential = await GoogleAuth.AuthorizeAsync(Scopes, User, ct);

        try
        {
            using var drive = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = AppName
            });

            await drive.Files.Delete(spreadsheetId).ExecuteAsync(ct);
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
            throw GoogleErrors.Translate(ex, "Impossible de supprimer le Google Sheet.");
        }
    }
}
