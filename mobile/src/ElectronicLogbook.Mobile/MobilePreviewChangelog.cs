namespace ElectronicLogbook.Mobile;

public static class MobilePreviewChangelog
{
    public static MobilePreviewChangelogRelease Current { get; } = new(
        "0.2.2",
        300000020,
        [
            new(
                "Improved Readability",
                "Ensured consistent UI items across devices, and display settings."),
            new(
                "Circling currency fixes",
                "Corrected how IPC and IFR OPC entries affect circling currency, and clarified the warning when an IPC has no circling approach.")
        ]);

    public static MobilePreviewChangelogDecision Evaluate(MobilePreviewInstallationInfo? installation)
    {
        if (installation is not { Enabled: true, VersionCode: > 0 })
        {
            return MobilePreviewChangelogDecision.None;
        }

        if (installation.AcknowledgedVersionCode == installation.VersionCode)
        {
            return MobilePreviewChangelogDecision.None;
        }

        return installation.WasUpdated
            ? MobilePreviewChangelogDecision.Show
            : MobilePreviewChangelogDecision.AcknowledgeSilently;
    }

    public static MobilePreviewChangelogRelease ReleaseFor(MobilePreviewInstallationInfo installation) =>
        installation.VersionCode == Current.VersionCode &&
        string.Equals(installation.VersionName, Current.VersionName, StringComparison.Ordinal)
            ? Current
            : new(
                installation.VersionName,
                installation.VersionCode,
                [
                    new(
                        "Preview improvements",
                        "This build includes the latest FlightLogX fixes and reliability improvements.")
                ]);
}

public enum MobilePreviewChangelogDecision
{
    None,
    AcknowledgeSilently,
    Show
}

public sealed record MobilePreviewChangelogRelease(
    string VersionName,
    long VersionCode,
    IReadOnlyList<MobilePreviewChangelogItem> Items);

public sealed record MobilePreviewChangelogItem(string Title, string Detail);
