namespace ElectronicLogbook.Mobile;

public static class MobilePreviewChangelog
{
    public static MobilePreviewChangelogRelease Current { get; } = new(
        "0.2.1",
        300000019,
        [
            new(
                "Matching filters everywhere",
                "Logbook Entries and Totals now use the same filters, including dates, aircraft, airports, crew and numeric flight values."),
            new(
                "Clearer Custom Entry settings",
                "Rename fields, see their logged hours, choose whole numbers or decimals, and jump straight to flights that prevent removal."),
            new(
                "Save exports to your device",
                "Choose Download to device from Android's share options to save Excel and CSV exports where you want them.")
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
