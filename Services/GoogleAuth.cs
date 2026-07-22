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
    /// <summary>
    /// Ensemble unique des permissions Google (Contacts + profil + Sheets + Drive).
    /// Partagé par tous les services → une seule autorisation, un seul écran de consentement.
    /// </summary>
    public static readonly string[] AllScopes =
    {
        "https://www.googleapis.com/auth/contacts",
        "https://www.googleapis.com/auth/userinfo.email",
        "https://www.googleapis.com/auth/userinfo.profile",
        "https://www.googleapis.com/auth/spreadsheets",
        "https://www.googleapis.com/auth/drive",
        // Préinscriptions : créer/éditer le formulaire Google Forms et lire ses réponses.
        "https://www.googleapis.com/auth/forms.body",
        "https://www.googleapis.com/auth/forms.responses.readonly"
    };

    /// <summary>Clé de jeton unique et partagée (un seul compte, un seul jeton pour toute l'appli).</summary>
    public const string SharedUser = "user-badminton";

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
        var credential = await new AuthorizationCodeInstalledApp(flow, receiver).AuthorizeAsync(user, ct);

        // La bibliothèque réutilise un jeton stocké tant qu'il est rafraîchissable, SANS
        // revérifier les scopes accordés. Un jeton créé avant l'ajout d'un scope (ou pour
        // lequel une case a été décochée au consentement) laisserait donc l'appli se
        // connecter silencieusement, puis échouer avec « insufficient authentication scopes ».
        // Si le jeton ne couvre pas les scopes critiques, on le supprime et on force un
        // consentement complet (au lieu de laisser l'utilisateur sur une session cassée).
        //
        // IMPORTANT : opération faite AU PLUS UNE FOIS par exécution, et UNIQUEMENT pour les
        // scopes critiques réellement configurés (contacts/sheets/drive). Sinon un scope que
        // Google n'accorde pas encore (ex. Forms non activé côté projet) provoquerait une
        // boucle qui supprimerait le jeton à chaque appel et casserait toute l'appli.
        if (!_reconsentAttempted && !TokenCoversRequiredScopes(credential.Token))
        {
            _reconsentAttempted = true;
            try
            {
                await flow.DeleteTokenAsync(user, ct);
                var consentReceiver = new LoopbackBrowserCodeReceiver(promptSelectAccount: true);
                credential = await new AuthorizationCodeInstalledApp(flow, consentReceiver).AuthorizeAsync(user, ct);
            }
            catch (OperationCanceledException)
            {
                throw; // annulation volontaire : on laisse remonter
            }
        }

        return credential;
    }

    /// <summary>Garantit qu'on ne relance le consentement (destructif) qu'une seule fois par exécution.</summary>
    private static bool _reconsentAttempted;

    /// <summary>
    /// Scopes indispensables aux fonctionnalités de l'appli, renvoyés tels quels par Google
    /// (contrairement à userinfo.email/profile que Google peut normaliser en openid/email/profile).
    /// Sert à détecter un jeton stocké aux permissions incomplètes.
    /// </summary>
    // NB : on n'y met PAS les scopes Forms. Tant que l'API Forms n'est pas activée / accordée,
    // le jeton ne les contient pas ; les exiger ici déclencherait un re-consentement destructif
    // en boucle. Les fonctionnalités Forms signalent elles-mêmes un scope manquant le cas échéant.
    private static readonly string[] RequiredScopes =
    {
        "https://www.googleapis.com/auth/contacts",
        "https://www.googleapis.com/auth/spreadsheets",
        "https://www.googleapis.com/auth/drive"
    };

    /// <summary>Vrai si le jeton accorde tous les scopes critiques (ou si l'info est absente).</summary>
    private static bool TokenCoversRequiredScopes(TokenResponse? token)
    {
        var granted = token?.Scope;
        // Scope inconnu (ancien jeton sans info) : on ne force pas de reconsentement.
        if (string.IsNullOrWhiteSpace(granted))
            return true;

        var set = granted.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return RequiredScopes.All(s => set.Contains(s, StringComparer.OrdinalIgnoreCase));
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
            ? "Connexion réussie ✅<br>Vous pouvez fermer cet onglet et revenir à l'application."
            : "En attente...";
        // On tente de fermer l'onglet automatiquement (le navigateur peut le refuser).
        var autoClose = success
            ? "<script>setTimeout(function(){window.open('','_self');window.close();},600);</script>"
            : "";
        var html = "<html><head><meta charset=\"utf-8\"></head>" +
                   "<body style=\"font-family:sans-serif;text-align:center;margin-top:60px;color:#233323\">" +
                   $"<h2>🏸 Club de Badminton</h2><p style=\"font-size:16px\">{message}</p>{autoClose}</body></html>";
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
