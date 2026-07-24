using Microsoft.JSInterop;

namespace ElectronicLogbook.Mobile;

public sealed class BrowserUiPreferencesStore(IJSRuntime js)
{
    private const string StorageKey = "electronic-logbook.ui-preferences";

    public async ValueTask<MobileUiPreferences> LoadAsync()
    {
        var value = await js.InvokeAsync<string?>("electronicLogbookUiPreferences.load", StorageKey);
        return MobileUiPreferences.Parse(value);
    }

    public ValueTask SaveAsync(MobileUiPreferences preferences) =>
        js.InvokeVoidAsync("electronicLogbookUiPreferences.save", StorageKey, preferences.ThemeMode);

    public ValueTask ApplyThemeAsync(MobileUiPreferences preferences) =>
        js.InvokeVoidAsync("electronicLogbookUiPreferences.applyTheme", preferences.ThemeMode);

    public ValueTask<bool> IsSystemDarkAsync() =>
        js.InvokeAsync<bool>("electronicLogbookUiPreferences.isSystemDark");
}

public sealed record MobileUiPreferences(string ThemeMode)
{
    public static MobileUiPreferences System { get; } = new("System");

    public static MobileUiPreferences Parse(string? themeMode) =>
        themeMode is "Light" or "Dark" or "System"
            ? new MobileUiPreferences(themeMode)
            : System;
}
