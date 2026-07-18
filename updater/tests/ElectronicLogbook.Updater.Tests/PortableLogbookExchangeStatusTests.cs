using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookExchangeStatusTests
{
    [Fact]
    public void CreateReportsUnexportedChangesAndLastExchangeWithoutFlightData()
    {
        var workingCopy = new PortableLogbookWorkingCopyState(
            true,
            PendingOperationCount: 2,
            PendingCreateCount: 1,
            PendingCorrectionCount: 1,
            PendingDeletionCount: 0,
            [new RevisionId("rev_pending")],
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"));
        var olderReceipt = Receipt(
            "AAAA",
            importedAt: "2026-07-18T00:01:00Z",
            packageCreatedAt: "2026-07-17T00:00:00Z");
        var newerReceipt = Receipt(
            "BBBB",
            importedAt: "2026-07-18T00:02:00Z",
            packageCreatedAt: "2026-07-18T00:00:00Z");

        var status = PortableLogbookExchangeStatus.Create(
            workingCopy,
            [olderReceipt, newerReceipt],
            DateTimeOffset.Parse("2026-07-18T00:03:00Z"));

        Assert.True(status.HasUnexportedChanges);
        Assert.Equal(2, status.PendingOperationCount);
        Assert.Equal(newerReceipt.ImportedAt, status.LastSuccessfulImportAt);
        Assert.Equal(newerReceipt.PackageCreatedAt, status.LastImportedPackageCreatedAt);
        Assert.Equal(DateTimeOffset.Parse("2026-07-18T00:03:00Z"), status.LastSuccessfulExportAt);
        Assert.Contains("not a backup", status.BackupNotice, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateHandlesNoPriorPackageExchange()
    {
        var workingCopy = new PortableLogbookWorkingCopyState(
            false,
            PendingOperationCount: 0,
            PendingCreateCount: 0,
            PendingCorrectionCount: 0,
            PendingDeletionCount: 0,
            [],
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"));

        var status = PortableLogbookExchangeStatus.Create(workingCopy, [], null);

        Assert.False(status.HasUnexportedChanges);
        Assert.Null(status.LastSuccessfulImportAt);
        Assert.Null(status.LastImportedPackageCreatedAt);
        Assert.Null(status.LastSuccessfulExportAt);
    }

    private static PortableLogbookPackageReceipt Receipt(
        string sha,
        string importedAt,
        string packageCreatedAt) =>
        new(
            sha,
            new LogbookId("log_status"),
            OperationCount: 1,
            DateTimeOffset.Parse(packageCreatedAt),
            DateTimeOffset.Parse(importedAt));
}
