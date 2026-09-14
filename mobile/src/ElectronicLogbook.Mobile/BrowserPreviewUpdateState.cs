using Microsoft.JSInterop;

namespace ElectronicLogbook.Mobile;

public sealed class BrowserPreviewUpdateState(IJSRuntime js)
{
    public ValueTask<MobilePreviewInstallationInfo?> LoadAsync() =>
        js.InvokeAsync<MobilePreviewInstallationInfo?>(
            "electronicLogbookPreviewUpdates.getInstallationInfo");

    public ValueTask AcknowledgeInstalledVersionAsync() =>
        js.InvokeVoidAsync("electronicLogbookPreviewUpdates.acknowledgeInstalledVersion");
}

public sealed record MobilePreviewInstallationInfo(
    bool Enabled,
    string VersionName,
    long VersionCode,
    bool WasUpdated,
    long AcknowledgedVersionCode);
