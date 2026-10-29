namespace CodeSail.Services.Storage;

/// <summary>Speichert das GitHub-Token des Nutzers ausschließlich im Browser.</summary>
public interface ITokenStore
{
    ValueTask<string?> GetTokenAsync();
    ValueTask SetTokenAsync(string token);
    ValueTask ClearAsync();
}
