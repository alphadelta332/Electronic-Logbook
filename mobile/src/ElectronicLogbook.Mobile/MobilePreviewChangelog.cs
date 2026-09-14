namespace ElectronicLogbook.Mobile;

public static class MobilePreviewChangelog
{
    public static MobilePreviewChangelogRelease Current { get; } = new(
        "0.2.0",
        300000018,
        [
            new(
                "More advanced Filtering",
                "Filter Logbook Totals by dates, aircraft, airports, crew and numeric flight values. The included-flight count and totals update to match."),
            new(
                "Improved Custom Entry management",
                "Rename populated fields safely and choose Whole numbers or Decimals for each custom entry."),
            new(
                "Better Excel Export Formatting",
                "Choose Full or Compact Excel and CSV exports. Excel now follows the original Spreadsheet logbook layout.")
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
