namespace ElectronicLogbook.Mobile;

public static class MobilePreviewChangelog
{
    public static MobilePreviewChangelogRelease Current { get; } = new(
        "0.3.0",
        300000021,
        [
            new("Charts & Stats", IsNew: true),
            new("Smarter airport suggestions"),
            new("Automatic airport database updates")
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
                    new("Preview improvements")
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

public sealed record MobilePreviewChangelogItem(string Title, bool IsNew = false);
