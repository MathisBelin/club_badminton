using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Util.Store;

namespace BadmintonClub.Services;

/// <summary>
/// Logique d'authentification OAuth2 partagée par les services Google.
/// Chaque service passe ses propres scopes et une clé utilisateur distincte,
/// afin que les jetons (et donc les autorisations) restent indépendants.
/// </summary>
public static class GoogleAuth
{
    /// <summary>Indique s'il existe déjà un jeton stocké pour cet utilisateur (connexion possible sans navigateur).</summary>
    public static bool HasStoredToken(string user)
    {
        try
        {
            if (!Directory.Exists(AppPaths.TokenStoreFolder))
                return false;
            return Directory.GetFiles(AppPaths.TokenStoreFolder)
                .Any(f => System.IO.Path.GetFileName(f).Contains(user, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Supprime les jetons OAuth stockés (déconnexion / changement de compte).</summary>
    public static void SignOut()
    {
        try
        {
            if (Directory.Exists(AppPaths.TokenStoreFolder))
                Directory.Delete(AppPaths.TokenStoreFolder, recursive: true);
        }
        catch
        {
            // Non bloquant.
        }
    }

    public static async Task<UserCredential> AuthorizeAsync(
        string[] scopes, string user, CancellationToken ct, bool promptSelectAccount = false)
    {
        if (!File.Exists(AppPaths.ClientSecretFile))
        {
            throw new GoogleSyncException(
                "Fichier client_secret.json introuvable à côté de l'application.\n" +
                "Téléchargez vos identifiants OAuth2 (type « Application de bureau ») " +
                "depuis la console Google Cloud et placez le fichier client_secret.json " +
                "dans le dossier de l'exécutable.");
        }

        AppPaths.EnsureDataFolder();

        GoogleClientSecrets clientSecrets;
        await using (var stream = File.OpenRead(AppPaths.ClientSecretFile))
        {
            clientSecrets = await GoogleClientSecrets.FromStreamAsync(stream, ct);
        }

        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = clientSecrets.Secrets,
            Scopes = scopes,
            DataStore = new FileDataStore(AppPaths.TokenStoreFolder, fullPath: true)
        });

        // On utilise notre propre récepteur loopback : il ouvre le navigateur via
        // ShellExecute (et non « cmd start »), ce qui évite que l'URL d'autorisation
        // soit tronquée au premier « & » sur Windows (bug « response_type manquant »).
        var receiver = new LoopbackBrowserCodeReceiver(promptSelectAccount);
        return await new AuthorizationCodeInstalledApp(flow, receiver).AuthorizeAsync(user, ct);
    }
}

/// <summary>
/// Récepteur de code OAuth via un petit serveur HTTP local (loopback), avec ouverture
/// du navigateur robuste vis-à-vis des caractères « & » de l'URL d'autorisation.
/// </summary>
internal sealed class LoopbackBrowserCodeReceiver : ICodeReceiver
{
    private readonly string _redirectUri;
    private readonly bool _promptSelectAccount;

    public LoopbackBrowserCodeReceiver(bool promptSelectAccount = false)
    {
        _promptSelectAccount = promptSelectAccount;
        var port = GetRandomUnusedPort();
        _redirectUri = $"http://127.0.0.1:{port}/authorize/";
    }

    public string RedirectUri => _redirectUri;

    public async Task<AuthorizationCodeResponseUrl> ReceiveCodeAsync(
        AuthorizationCodeRequestUrl url, CancellationToken taskCancellationToken)
    {
        // Force le choix du compte ET l'écran de consentement complet (pour ré-accorder
        // toutes les permissions, sinon un accès décoché reste manquant).
        if (_promptSelectAccount && url is GoogleAuthorizationCodeRequestUrl google)
            google.Prompt = "consent select_account";

        var authorizationUrl = url.Build().AbsoluteUri;

        using var listener = new HttpListener();
        listener.Prefixes.Add(_redirectUri);
        listener.Start();

        // Permet d'annuler l'attente proprement.
        using var registration = taskCancellationToken.Register(() =>
        {
            try { listener.Stop(); } catch { /* ignore */ }
        });

        BrowserService.Open(authorizationUrl);

        try
        {
            // Ignore les requêtes parasites (favicon, etc.) jusqu'à obtenir code ou error.
            while (true)
            {
                var context = await listener.GetContextAsync();
                var query = context.Request.QueryString;

                var hasResult = query.AllKeys.Any(k => k is "code" or "error");
                WriteBrowserResponse(context, hasResult);

                if (!hasResult)
                    continue;

                var result = new Dictionary<string, string>();
                foreach (var key in query.AllKeys)
                {
                    if (key != null)
                        result[key] = query[key] ?? string.Empty;
                }
                return new AuthorizationCodeResponseUrl(result);
            }
        }
        catch (HttpListenerException) when (taskCancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(taskCancellationToken);
        }
    }

    private static void WriteBrowserResponse(HttpListenerContext context, bool success)
    {
        var message = success
            ? "Autorisation reçue. Vous pouvez fermer cet onglet et revenir à l'application."
            : "En attente...";
        var html = $"<html><head><meta charset=\"utf-8\"></head>" +
                   $"<body style=\"font-family:sans-serif;text-align:center;margin-top:60px\">" +
                   $"<h2>Club de Badminton</h2><p>{message}</p></body></html>";
        var buffer = Encoding.UTF8.GetBytes(html);

        try
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = buffer.Length;
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.OutputStream.Close();
        }
        catch
        {
            // Réponse au navigateur non critique.
        }
    }

    private static int GetRandomUnusedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
