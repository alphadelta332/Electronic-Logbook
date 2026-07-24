namespace ElectronicLogbook.Mobile;

public sealed class MobileUiPreferenceState(BrowserUiPreferencesStore store)
{
    public event Action? Changed;

    public string ThemeMode { get; private set; } = "System";

    public bool SystemIsDark { get; private set; }

    public bool IsDarkMode => ThemeMode == "Dark" || (ThemeMode == "System" && SystemIsDark);

    public async Task EnsureLoadedAsync()
    {
        var preferences = await store.LoadAsync();
        ThemeMode = preferences.ThemeMode;
        SystemIsDark = await store.IsSystemDarkAsync();
        await store.ApplyThemeAsync(preferences);
        Changed?.Invoke();
    }

    public async Task SetThemeModeAsync(string themeMode)
    {
        var preferences = MobileUiPreferences.Parse(themeMode);
        ThemeMode = preferences.ThemeMode;
        if (ThemeMode == "System")
        {
            SystemIsDark = await store.IsSystemDarkAsync();
        }

        await store.SaveAsync(preferences);
        await store.ApplyThemeAsync(preferences);
        Changed?.Invoke();
    }
}
