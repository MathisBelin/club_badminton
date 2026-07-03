namespace BadmintonClub.Services;

/// <summary>
/// Exception « propre » remontée à l'UI pour tout problème de synchro Google,
/// avec un message déjà en français et lisible par l'utilisateur.
/// </summary>
public class GoogleSyncException : Exception
{
    public GoogleSyncException(string message, Exception? inner = null)
        : base(message, inner) { }
}
