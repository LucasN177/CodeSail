namespace CodeSail.Services.Storage;

/// <summary>Speichert das GitHub-Token des Nutzers ausschließlich im Browser.</summary>
public interface ITokenStore
{
    ValueTask<string?> GetTokenAsync();

    /// <param name="persistent">true: bleibt nach dem Schließen des Tabs erhalten ("Angemeldet bleiben").</param>
    ValueTask SetTokenAsync(string token, bool persistent);

    ValueTask ClearAsync();
}
