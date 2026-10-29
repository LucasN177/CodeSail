using CodeSail.Services.GitHub;
using CodeSail.Services.Storage;
using Octokit;

namespace CodeSail.Services;

/// <summary>Anmeldezustand der App. Wird beim Start einmal aus dem gespeicherten Token wiederhergestellt.</summary>
public sealed class AuthState
{
    private readonly ITokenStore _tokenStore;
    private readonly IGitHubService _gitHub;

    public AuthState(ITokenStore tokenStore, IGitHubService gitHub)
    {
        _tokenStore = tokenStore;
        _gitHub = gitHub;
        _gitHub.Unauthorized += () => _ = ExpireAsync();
    }

    public bool IsInitialized { get; private set; }

    public string? Login { get; private set; }

    public bool IsAuthenticated => Login is not null;

    /// <summary>true, wenn GitHub das gespeicherte Token abgelehnt hat (abgelaufen oder widerrufen).</summary>
    public bool SessionExpired { get; private set; }

    /// <summary>Seite, zu der nach dem erneuten Anmelden zurückgekehrt wird.</summary>
    public string? ReturnUrl { get; set; }

    public event Action? Changed;

    public async Task InitializeAsync()
    {
        if (IsInitialized)
            return;

        try
        {
            if (await _tokenStore.GetTokenAsync() is not null)
                Login = await _gitHub.GetCurrentUserLoginAsync();
        }
        catch (AuthorizationException)
        {
            await _tokenStore.ClearAsync();
            SessionExpired = true;
        }
        catch (Exception)
        {
            // Offline oder GitHub nicht erreichbar: mit dem zuletzt bekannten Login weiterarbeiten,
            // damit die Offline-Kopien erreichbar bleiben. Das Token wird beim nächsten Abruf geprüft.
            Login = await _tokenStore.GetLoginAsync();
        }

        IsInitialized = true;
        Changed?.Invoke();
    }

    /// <summary>Speichert das Token und prüft es gegen GitHub. Bei Fehlern wird es wieder verworfen.</summary>
    public async Task LoginAsync(string token, bool remember)
    {
        await _tokenStore.SetTokenAsync(token.Trim(), remember);
        try
        {
            Login = await _gitHub.GetCurrentUserLoginAsync();
            await _tokenStore.SetLoginAsync(Login);
        }
        catch
        {
            await _tokenStore.ClearAsync();
            throw;
        }

        IsInitialized = true;
        SessionExpired = false;
        Changed?.Invoke();
    }

    public async Task LogoutAsync()
    {
        await _tokenStore.ClearAsync();
        Login = null;
        Changed?.Invoke();
    }

    /// <summary>
    /// Reagiert auf ein abgelehntes Token: abmelden, aber den Offline-Cache behalten,
    /// damit lokale Änderungen nach dem erneuten Anmelden noch da sind.
    /// </summary>
    private async Task ExpireAsync()
    {
        if (!IsAuthenticated)
            return;

        SessionExpired = true;
        await LogoutAsync();
    }
}
