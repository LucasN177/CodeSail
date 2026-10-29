using Microsoft.JSInterop;

namespace CodeSail.Services.Storage;

/// <summary>
/// Hält das Token standardmäßig im sessionStorage, damit es beim Schließen des Tabs verfällt.
/// Mit "Angemeldet bleiben" landet es im localStorage. Der Login-Name liegt immer neben dem Token.
/// </summary>
public sealed class BrowserTokenStore(IJSRuntime js) : ITokenStore
{
    private const string TokenKey = "codesail.github.token";
    private const string LoginKey = "codesail.github.login";

    public ValueTask<string?> GetTokenAsync() => GetAsync(TokenKey);

    public async ValueTask SetTokenAsync(string token, bool persistent)
    {
        await ClearAsync();
        await js.InvokeVoidAsync(persistent ? "localStorage.setItem" : "sessionStorage.setItem", TokenKey, token);
    }

    public ValueTask<string?> GetLoginAsync() => GetAsync(LoginKey);

    public async ValueTask SetLoginAsync(string login)
    {
        // Im selben Speicher wie das Token, damit beide gemeinsam verfallen
        var persistent = await js.InvokeAsync<string?>("localStorage.getItem", TokenKey) is not null;
        await js.InvokeVoidAsync(persistent ? "localStorage.setItem" : "sessionStorage.setItem", LoginKey, login);
    }

    public async ValueTask ClearAsync()
    {
        foreach (var key in new[] { TokenKey, LoginKey })
        {
            await js.InvokeVoidAsync("sessionStorage.removeItem", key);
            await js.InvokeVoidAsync("localStorage.removeItem", key);
        }
    }

    private async ValueTask<string?> GetAsync(string key) =>
        await js.InvokeAsync<string?>("sessionStorage.getItem", key)
        ?? await js.InvokeAsync<string?>("localStorage.getItem", key);
}
