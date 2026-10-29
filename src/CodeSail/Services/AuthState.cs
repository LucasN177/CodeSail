using CodeSail.Services.GitHub;
using CodeSail.Services.Storage;
using Octokit;

namespace CodeSail.Services;

/// <summary>Anmeldezustand der App. Wird beim Start einmal aus dem gespeicherten Token wiederhergestellt.</summary>
public sealed class AuthState(ITokenStore tokenStore, IGitHubService gitHub)
{
    public bool IsInitialized { get; private set; }

    public string? Login { get; private set; }

    public bool IsAuthenticated => Login is not null;

    public event Action? Changed;

    public async Task InitializeAsync()
    {
        if (IsInitialized)
            return;

        try
        {
            if (await tokenStore.GetTokenAsync() is not null)
                Login = await gitHub.GetCurrentUserLoginAsync();
        }
        catch (AuthorizationException)
        {
            await tokenStore.ClearAsync();
        }
        catch (Exception)
        {
            // Offline oder GitHub nicht erreichbar: Token behalten, Nutzer kann es erneut versuchen
        }

        IsInitialized = true;
        Changed?.Invoke();
    }

    /// <summary>Speichert das Token und prüft es gegen GitHub. Bei Fehlern wird es wieder verworfen.</summary>
    public async Task LoginAsync(string token, bool remember)
    {
        await tokenStore.SetTokenAsync(token.Trim(), remember);
        try
        {
            Login = await gitHub.GetCurrentUserLoginAsync();
        }
        catch
        {
            await tokenStore.ClearAsync();
            throw;
        }

        IsInitialized = true;
        Changed?.Invoke();
    }

    public async Task LogoutAsync()
    {
        await tokenStore.ClearAsync();
        Login = null;
        Changed?.Invoke();
    }
}
