using System.Text.Json;
using Microsoft.JSInterop;

namespace CodeSail.Services;

public enum AppTheme
{
    Dark,
    Light,
    System
}

public enum TouchToolbarMode
{
    /// <summary>Nur auf Geräten mit Touchscreen (iPad).</summary>
    Auto,
    On,
    Off
}

public sealed record EditorSettings(
    int FontSize = 14,
    bool WordWrap = false,
    bool Minimap = false,
    AppTheme Theme = AppTheme.Dark,
    TouchToolbarMode TouchToolbar = TouchToolbarMode.Auto)
{
    public const int MinFontSize = 10;
    public const int MaxFontSize = 28;

    public EditorSettings Normalized() => this with { FontSize = Math.Clamp(FontSize, MinFontSize, MaxFontSize) };
}

/// <summary>Einstellungen der App, gespeichert im localStorage dieses Geräts.</summary>
public sealed class SettingsService(IJSRuntime js)
{
    private const string Key = "codesail.settings";

    public EditorSettings Settings { get; private set; } = new();

    /// <summary>Ergebnis von prefers-color-scheme, für das Design "System".</summary>
    public bool SystemPrefersDark { get; private set; } = true;

    public bool IsDark => Settings.Theme switch
    {
        AppTheme.Light => false,
        AppTheme.System => SystemPrefersDark,
        _ => true
    };

    public event Action? Changed;

    public async Task LoadAsync(bool systemPrefersDark)
    {
        SystemPrefersDark = systemPrefersDark;
        try
        {
            if (await js.InvokeAsync<string?>("localStorage.getItem", Key) is { } json)
                Settings = Parse(json);
        }
        catch (JSException)
        {
            // Ohne lesbaren Speicher gelten die Standardwerte
        }
    }

    public async Task SaveAsync(EditorSettings settings)
    {
        Settings = settings.Normalized();
        Changed?.Invoke();
        await js.InvokeVoidAsync("localStorage.setItem", Key, JsonSerializer.Serialize(Settings));
    }

    /// <summary>Liest gespeicherte Einstellungen; unbekannte oder kaputte Werte fallen auf den Standard zurück.</summary>
    public static EditorSettings Parse(string json)
    {
        try
        {
            return (JsonSerializer.Deserialize<EditorSettings>(json) ?? new EditorSettings()).Normalized();
        }
        catch (JsonException)
        {
            return new EditorSettings();
        }
    }
}
