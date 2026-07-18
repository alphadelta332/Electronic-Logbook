using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookConflictRoundTripTests
{
    [Fact]
    public void ConflictResolutionOperationMakesResolvedRevisionCurrent()
    {
        var create = new CreateEntryOperation(
            new LogbookId("log_conflict_roundtrip"),
            new EntryId("ent_1"),
            new RevisionId("rev_create"),
            new DeviceId("dev_excel"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            Entry("VH-BASE"));
        var excelCorrection = Correct(create, "rev_excel", "VH-EXCEL", "dev_excel", create.CreatedAt.AddMinutes(1));
        var mobileCorrection = Correct(create, "rev_mobile", "VH-MOBILE", "dev_mobile", create.CreatedAt.AddMinutes(2));
        var local = PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [create, excelCorrection]);
        var incoming = PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [create, mobileCorrection]);
        var preview = PortableLogbookExchange.PreviewImport(local, incoming);
        var conflict = Assert.Single(preview.Conflicts);
        var resolution = PortableLogbookConflictResolution.CreateResolution(
            conflict,
            create.LogbookId,
            new DeviceId("dev_excel"),
            new RevisionId("rev_resolved"),
            create.CreatedAt.AddMinutes(3),
            Entry("VH-FINAL"),
            "Resolved manually");
        var resolvedDocument = PortableLogbookDocument.CreateAustraliaFirst(
            create.LogbookId,
            [],
            local.Operations.Concat(preview.NewOperations).Concat([resolution]));

        var validation = PortableLogbookValidator.Validate(resolvedDocument);
        var merge = PortableLogbookMerger.Merge(resolvedDocument.Operations);

        Assert.True(validation.IsValid);
        Assert.Empty(merge.Conflicts);
        var current = Assert.Single(merge.Entries.Values);
        Assert.Equal(resolution.RevisionId, current.CurrentRevisionId);
        Assert.Equal("VH-FINAL", current.Entry?.Registration);
    }

    private static CorrectEntryOperation Correct(
        CreateEntryOperation create,
        string revisionId,
        string registration,
        string deviceId,
        DateTimeOffset createdAt) =>
        new(
            create.LogbookId,
            create.EntryId,
            new RevisionId(revisionId),
            new HashSet<RevisionId> { create.RevisionId },
            new DeviceId(deviceId),
            createdAt,
            Entry(registration));

    private static PortableLogbookEntry Entry(string registration) =>
        PortableLogbookEntry.Empty with
        {
            Date = new DateOnly(2026, 7, 18),
            AircraftType = "C172",
            Registration = registration,
            From = "YSBK",
            To = "YSBK",
            PilotInCommand = 1.2m
        };
}
