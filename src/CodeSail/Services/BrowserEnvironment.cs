using Microsoft.JSInterop;

namespace CodeSail.Services;

/// <summary>Online-Status, Bildschirmbreite und App-Updates des Browsers.</summary>
public sealed class BrowserEnvironment(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private DotNetObjectReference<BrowserEnvironment>? _selfRef;

    public bool IsOnline { get; private set; } = true;

    /// <summary>Eine neue App-Version ist geladen und wartet auf einen Neustart.</summary>
    public bool UpdateAvailable { get; private set; }

    public event Action? ConnectivityChanged;

    public event Action? UpdateReady;

    public async Task InitializeAsync()
    {
        if (_module is not null)
            return;

        _module = await js.InvokeAsync<IJSObjectReference>("import", "./js/codesail.js");
        _selfRef = DotNetObjectReference.Create(this);
        IsOnline = await _module.InvokeAsync<bool>("watchConnectivity", _selfRef);
        if (await _module.InvokeAsync<bool>("watchUpdates", _selfRef))
            OnUpdateAvailable();
    }

    /// <summary>Aktiviert die neue Version und lädt die App neu.</summary>
    public async Task ApplyUpdateAsync()
    {
        if (_module is not null)
            await _module.InvokeVoidAsync("applyUpdate");
    }

    [JSInvokable]
    public void OnUpdateAvailable()
    {
        UpdateAvailable = true;
        UpdateReady?.Invoke();
    }

    /// <summary>true, wenn der Explorer den Editor überdeckt (iPad hochkant).</summary>
    public async Task<bool> IsNarrowAsync() =>
        _module is not null && await _module.InvokeAsync<bool>("isNarrow");

    [JSInvokable]
    public void OnConnectivityChanged(bool isOnline)
    {
        IsOnline = isOnline;
        ConnectivityChanged?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        _selfRef?.Dispose();
        if (_module is null)
            return;
        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }
    }
}
