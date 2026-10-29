using Microsoft.JSInterop;

namespace CodeSail.Services.Storage;

/// <summary>
/// Hält das Token im sessionStorage, damit es beim Schließen des Tabs verfällt.
/// Eine optionale "Angemeldet bleiben"-Variante (localStorage) folgt in Phase 1.
/// </summary>
public sealed class BrowserTokenStore(IJSRuntime js) : ITokenStore
{
    private const string Key = "codesail.github.token";

    public ValueTask<string?> GetTokenAsync() =>
        js.InvokeAsync<string?>("sessionStorage.getItem", Key);

    public ValueTask SetTokenAsync(string token) =>
        js.InvokeVoidAsync("sessionStorage.setItem", Key, token);

    public ValueTask ClearAsync() =>
        js.InvokeVoidAsync("sessionStorage.removeItem", Key);
}
