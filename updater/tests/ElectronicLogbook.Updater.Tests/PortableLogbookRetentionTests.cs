using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookRetentionTests
{
    [Fact]
    public void EvaluateReportsOperationsInsideAndOutsideSevenYearWindow()
    {
        var logbookId = new LogbookId("log_retention");
        var oldOperation = CreateOperation(logbookId, "ent_old", "rev_old", DateTimeOffset.Parse("2018-07-17T00:00:00Z"));
        var retainedBoundaryOperation = CreateOperation(logbookId, "ent_boundary", "rev_boundary", DateTimeOffset.Parse("2019-07-18T00:00:00Z"));
        var recentOperation = CreateOperation(logbookId, "ent_recent", "rev_recent", DateTimeOffset.Parse("2026-07-18T00:00:00Z"));
        var document = PortableLogbookDocument.CreateAustraliaFirst(logbookId, [], [oldOperation, retainedBoundaryOperation, recentOperation]);

        var snapshot = PortableLogbookRetention.Evaluate(document, new DateOnly(2026, 7, 18));

        Assert.Equal(new DateOnly(2019, 7, 18), snapshot.RetainAfter);
        Assert.Equal(3, snapshot.TotalOperationCount);
        Assert.Equal(1, snapshot.OlderThanMinimumRetentionCount);
        Assert.Equal(2, snapshot.MinimumRetainedOperationCount);
    }

    [Fact]
    public void EvaluateRejectsRetentionShorterThanSevenYears()
    {
        var document = PortableLogbookDocument.CreateAustraliaFirst(new LogbookId("log_retention"), [], []);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => PortableLogbookRetention.Evaluate(document, new DateOnly(2026, 7, 18), minimumRetentionYears: 6));

        Assert.Equal("minimumRetentionYears", exception.ParamName);
    }

    private static CreateEntryOperation CreateOperation(
        LogbookId logbookId,
        string entryId,
        string revisionId,
        DateTimeOffset createdAt) =>
        new(
            logbookId,
            new EntryId(entryId),
            new RevisionId(revisionId),
            new DeviceId("dev_excel"),
            createdAt,
            PortableLogbookEntry.Empty with
            {
                Date = DateOnly.FromDateTime(createdAt.UtcDateTime),
                AircraftType = "C172",
                Registration = "VH-ABC",
                From = "YSBK",
                To = "YSBK",
                PilotInCommand = 1.2m
            });
}
