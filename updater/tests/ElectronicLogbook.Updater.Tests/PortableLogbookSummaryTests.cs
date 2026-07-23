using System.Text.Json;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookSummaryTests
{
    [Fact]
    public void CreateReportsRedactedOperationCountsAndDateRange()
    {
        var create = CreateOperation("rev_create", PortableOperationKind.Create, DateTimeOffset.Parse("2026-07-18T00:00:00Z"));
        var correction = new CorrectEntryOperation(
            create.LogbookId,
            create.EntryId,
            new RevisionId("rev_correct"),
            new HashSet<RevisionId> { create.RevisionId },
            create.DeviceId,
            create.CreatedAt.AddMinutes(1),
            Entry("VH-SECRET"));
        var deletion = new DeleteEntryOperation(
            create.LogbookId,
            create.EntryId,
            new RevisionId("rev_delete"),
            new HashSet<RevisionId> { correction.RevisionId },
            create.DeviceId,
            create.CreatedAt.AddMinutes(2));
        var document = PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [deletion, correction, create]);

        var summary = PortableLogbookSummary.Create(document);

        Assert.Equal(3, summary.OperationCount);
        Assert.Equal(1, summary.CreateCount);
        Assert.Equal(1, summary.CorrectionCount);
        Assert.Equal(1, summary.DeletionCount);
        Assert.Equal(1, summary.DistinctDeviceCount);
        Assert.Equal(0, summary.CurrentRecordCount);
        Assert.Equal(1, summary.DeletedCurrentRecordCount);
        Assert.Equal(0, summary.UnresolvedConflictCount);
        Assert.Equal(create.CreatedAt, summary.FirstOperationAt);
        Assert.Equal(deletion.CreatedAt, summary.LastOperationAt);
    }

    [Fact]
    public void CreateReportsRedactedCurrentRecordAndConflictCounts()
    {
        var create = CreateOperation("rev_create", PortableOperationKind.Create, DateTimeOffset.Parse("2026-07-18T00:00:00Z"));
        var localCorrection = new CorrectEntryOperation(
            create.LogbookId,
            create.EntryId,
            new RevisionId("rev_local"),
            new HashSet<RevisionId> { create.RevisionId },
            create.DeviceId,
            create.CreatedAt.AddMinutes(1),
            Entry("VH-LOCAL"));
        var incomingCorrection = new CorrectEntryOperation(
            create.LogbookId,
            create.EntryId,
            new RevisionId("rev_incoming"),
            new HashSet<RevisionId> { create.RevisionId },
            create.DeviceId,
            create.CreatedAt.AddMinutes(2),
            Entry("VH-INCOMING"));
        var current = CreateOperation("rev_current", PortableOperationKind.Create, DateTimeOffset.Parse("2026-07-18T00:03:00Z")) with
        {
            EntryId = new EntryId("ent_current")
        };
        var document = PortableLogbookDocument.CreateAustraliaFirst(
            create.LogbookId,
            [],
            [create, localCorrection, incomingCorrection, current]);

        var summary = PortableLogbookSummary.Create(document);

        Assert.Equal(1, summary.CurrentRecordCount);
        Assert.Equal(0, summary.DeletedCurrentRecordCount);
        Assert.Equal(1, summary.UnresolvedConflictCount);
    }

    [Fact]
    public void SummarySerializationDoesNotIncludeFlightDetails()
    {
        var document = PortableLogbookDocument.CreateAustraliaFirst(
            new LogbookId("log_summary"),
            [],
            [CreateOperation("rev_create", PortableOperationKind.Create, DateTimeOffset.Parse("2026-07-18T00:00:00Z"))]);

        var json = JsonSerializer.Serialize(PortableLogbookSummary.Create(document), PortableLogbookJson.SerializerOptions);

        Assert.DoesNotContain("VH-SECRET", json, StringComparison.Ordinal);
        Assert.DoesNotContain("YSBK", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Training details", json, StringComparison.Ordinal);
        Assert.Contains("currentRecordCount", json, StringComparison.Ordinal);
        Assert.Contains("unresolvedConflictCount", json, StringComparison.Ordinal);
        Assert.Contains("distinctDeviceCount", json, StringComparison.Ordinal);
        Assert.DoesNotContain("dev_excel", json, StringComparison.Ordinal);
    }

    private static CreateEntryOperation CreateOperation(
        string revisionId,
        PortableOperationKind _,
        DateTimeOffset createdAt) =>
        new(
            new LogbookId("log_summary"),
            new EntryId("ent_1"),
            new RevisionId(revisionId),
            new DeviceId("dev_excel"),
            createdAt,
            Entry("VH-SECRET"));

    private static PortableLogbookEntry Entry(string registration) =>
        PortableLogbookEntry.Empty with
        {
            Date = new DateOnly(2026, 7, 18),
            AircraftType = "C172",
            Registration = registration,
            From = "YSBK",
            To = "YSCN",
            Details = "Training details",
            PilotInCommand = 1.2m
        };
}
