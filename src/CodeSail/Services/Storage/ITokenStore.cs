namespace CodeSail.Services.Storage;

/// <summary>Speichert das GitHub-Token des Nutzers ausschließlich im Browser.</summary>
public interface ITokenStore
{
    ValueTask<string?> GetTokenAsync();

    /// <param name="persistent">true: bleibt nach dem Schließen des Tabs erhalten ("Angemeldet bleiben").</param>
    ValueTask SetTokenAsync(string token, bool persistent);

    /// <summary>Zuletzt angemeldeter GitHub-Login, damit die App auch offline startet.</summary>
    ValueTask<string?> GetLoginAsync();

    ValueTask SetLoginAsync(string login);

    /// <summary>Entfernt Token und Login.</summary>
    ValueTask ClearAsync();
}
