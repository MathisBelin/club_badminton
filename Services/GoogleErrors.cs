using System.Net.Http;
using Google;
using Google.Apis.Auth.OAuth2.Responses;

namespace BadmintonClub.Services;

/// <summary>
/// Convertit les exceptions techniques Google en <see cref="GoogleSyncException"/>
/// avec un message français lisible par l'utilisateur.
/// </summary>
public static class GoogleErrors
{
    public static GoogleSyncException Translate(Exception ex, string context)
    {
        switch (ex)
        {
            case TokenResponseException tokenEx:
                return new GoogleSyncException(
                    $"{context}\nVotre autorisation Google a expiré ou a été révoquée. " +
                    $"Réessayez pour vous reconnecter. (détail : {tokenEx.Error?.Error})", tokenEx);

            case GoogleApiException apiEx
                when apiEx.Message.Contains("insufficient authentication scopes", StringComparison.OrdinalIgnoreCase):
                return new GoogleSyncException(
                    $"{context}\nLes permissions accordées à Google sont incomplètes " +
                    "(l'accès aux Contacts n'a pas été autorisé).\n\n" +
                    "Déconnectez-vous puis reconnectez-vous, et à l'écran de consentement " +
                    "Google, laissez TOUTES les cases cochées (accès aux Contacts inclus).", apiEx);

            case GoogleApiException apiEx:
                return new GoogleSyncException(
                    $"{context}\nErreur de l'API Google ({(int)apiEx.HttpStatusCode}) : {apiEx.Message}", apiEx);

            case HttpRequestException:
            case System.Net.Sockets.SocketException:
                return new GoogleSyncException(
                    $"{context}\nImpossible de joindre Google. Vérifiez votre connexion Internet.", ex);

            case TaskCanceledException:
            case OperationCanceledException:
                return new GoogleSyncException(
                    $"{context}\nL'opération a expiré ou a été annulée.", ex);

            default:
                return new GoogleSyncException($"{context}\n{ex.Message}", ex);
        }
    }
}
