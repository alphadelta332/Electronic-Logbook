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
        js.InvokeVoidAsync("electronicLogbookUiPreferences.save", StorageKey, preferences.ToStorageValue());

    public ValueTask ApplyThemeAsync(MobileUiPreferences preferences) =>
        js.InvokeVoidAsync("electronicLogbookUiPreferences.applyTheme", preferences.ToStorageValue());

    public ValueTask<bool> IsSystemDarkAsync() =>
        js.InvokeAsync<bool>("electronicLogbookUiPreferences.isSystemDark");
}

public sealed record MobileUiPreferences(string ThemeMode, string Accent)
{
    public const string DefaultAccent = "Forest";

    public static MobileUiPreferences System { get; } = new("System", DefaultAccent);

    public static IReadOnlyList<string> AccentOptions { get; } =
    [
        "Forest", "Ocean", "Sky", "Indigo", "Violet", "Plum",
        "Rose", "Red", "Orange", "Amber", "Gold", "Teal"
    ];

    public string ToStorageValue() => $"{ThemeMode}|{Accent}";

    public static MobileUiPreferences Parse(string? value)
    {
        var parts = value?.Split('|', 2) ?? [];
        var themeMode = parts.ElementAtOrDefault(0);
        var accent = parts.ElementAtOrDefault(1);
        return new(
            themeMode is "Light" or "Dark" or "System" ? themeMode : System.ThemeMode,
            AccentOptions.Contains(accent, StringComparer.Ordinal) ? accent! : DefaultAccent);
    }
}
