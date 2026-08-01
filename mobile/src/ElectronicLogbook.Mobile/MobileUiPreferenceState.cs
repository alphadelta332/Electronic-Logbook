namespace ElectronicLogbook.Mobile;

public sealed class MobileUiPreferenceState(BrowserUiPreferencesStore store)
{
    public event Action? Changed;

    public string ThemeMode { get; private set; } = "System";

    public string Accent { get; private set; } = MobileUiPreferences.DefaultAccent;

    public bool SystemIsDark { get; private set; }

    public bool IsDarkMode => ThemeMode == "Dark" || (ThemeMode == "System" && SystemIsDark);

    public async Task EnsureLoadedAsync()
    {
        var preferences = await store.LoadAsync();
        ThemeMode = preferences.ThemeMode;
        Accent = preferences.Accent;
        SystemIsDark = await store.IsSystemDarkAsync();
        await store.ApplyThemeAsync(preferences);
        Changed?.Invoke();
    }

    public async Task SetThemeModeAsync(string themeMode)
    {
        var preferences = MobileUiPreferences.Parse($"{themeMode}|{Accent}");
        ThemeMode = preferences.ThemeMode;
        if (ThemeMode == "System")
        {
            SystemIsDark = await store.IsSystemDarkAsync();
        }

        await store.SaveAsync(preferences);
        await store.ApplyThemeAsync(preferences);
        Changed?.Invoke();
    }

    public async Task SetAccentAsync(string accent)
    {
        var preferences = MobileUiPreferences.Parse($"{ThemeMode}|{accent}");
        Accent = preferences.Accent;
        await store.SaveAsync(preferences);
        await store.ApplyThemeAsync(preferences);
        Changed?.Invoke();
    }
}
