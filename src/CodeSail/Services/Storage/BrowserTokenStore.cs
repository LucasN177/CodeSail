using Microsoft.JSInterop;

namespace CodeSail.Services.Storage;

/// <summary>
/// Hält das Token standardmäßig im sessionStorage, damit es beim Schließen des Tabs verfällt.
/// Mit "Angemeldet bleiben" landet es im localStorage.
/// </summary>
public sealed class BrowserTokenStore(IJSRuntime js) : ITokenStore
{
    private const string Key = "codesail.github.token";

    public async ValueTask<string?> GetTokenAsync() =>
        await js.InvokeAsync<string?>("sessionStorage.getItem", Key)
        ?? await js.InvokeAsync<string?>("localStorage.getItem", Key);

    public async ValueTask SetTokenAsync(string token, bool persistent)
    {
        await ClearAsync();
        await js.InvokeVoidAsync(persistent ? "localStorage.setItem" : "sessionStorage.setItem", Key, token);
    }

    public async ValueTask ClearAsync()
    {
        await js.InvokeVoidAsync("sessionStorage.removeItem", Key);
        await js.InvokeVoidAsync("localStorage.removeItem", Key);
    }
}
