using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookWorkbookProjectionTests
{
    [Fact]
    public void ReconcileProducesNoOperationsForUnchangedKnownRows()
    {
        var known = KnownEntry("ent_1", "rev_1", Entry("VH-ABC"));

        var result = Reconcile([known], [new PortableLogbookWorkbookRow(known.EntryId, known.CurrentRevisionId, Entry("VH-ABC"))]);

        Assert.Empty(result.Operations);
    }

    [Fact]
    public void ReconcileCreatesOperationsForRowsWithoutKnownIds()
    {
        var result = Reconcile(
            [],
            [new PortableLogbookWorkbookRow(null, null, Entry("VH-NEW"))],
            new PortableLogbookIdFactory(() => new EntryId("ent_new"), () => new RevisionId("rev_new")));

        var create = Assert.IsType<CreateEntryOperation>(Assert.Single(result.Operations));
        Assert.Equal(new EntryId("ent_new"), create.EntryId);
        Assert.Equal(new RevisionId("rev_new"), create.RevisionId);
        Assert.Equal("VH-NEW", create.Entry.Registration);
        Assert.Equal(1, result.CreateCount);
    }

    [Fact]
    public void ReconcileCorrectsChangedKnownRows()
    {
        var known = KnownEntry("ent_1", "rev_1", Entry("VH-ABC"));

        var result = Reconcile(
            [known],
            [new PortableLogbookWorkbookRow(known.EntryId, known.CurrentRevisionId, Entry("VH-XYZ"))],
            new PortableLogbookIdFactory(() => new EntryId("unused"), () => new RevisionId("rev_correct")));

        var correction = Assert.IsType<CorrectEntryOperation>(Assert.Single(result.Operations));
        Assert.Equal(known.EntryId, correction.EntryId);
        Assert.Equal(new RevisionId("rev_correct"), correction.RevisionId);
        Assert.Equal(known.CurrentRevisionId, Assert.Single(correction.ParentRevisionIds));
        Assert.Equal("VH-XYZ", correction.Entry.Registration);
        Assert.Equal(1, result.CorrectionCount);
    }

    [Fact]
    public void ReconcileDeletesKnownRowsMissingFromWorkbookProjection()
    {
        var known = KnownEntry("ent_1", "rev_1", Entry("VH-ABC"));

        var result = Reconcile(
            [known],
            [],
            new PortableLogbookIdFactory(() => new EntryId("unused"), () => new RevisionId("rev_delete")));

        var deletion = Assert.IsType<DeleteEntryOperation>(Assert.Single(result.Operations));
        Assert.Equal(known.EntryId, deletion.EntryId);
        Assert.Equal(known.CurrentRevisionId, Assert.Single(deletion.ParentRevisionIds));
        Assert.Equal(1, result.DeletionCount);
    }

    [Fact]
    public void ReconcileDoesNotDeleteAlreadyDeletedKnownRowsAgain()
    {
        var known = KnownEntry("ent_1", "rev_delete", null, isDeleted: true);

        var result = Reconcile([known], []);

        Assert.Empty(result.Operations);
    }

    [Fact]
    public void ReconcileRejectsRowsWithUnknownIds()
    {
        var known = KnownEntry("ent_1", "rev_1", Entry("VH-ABC"));

        var exception = Assert.Throws<PortableLogbookWorkbookProjectionException>(() => Reconcile(
            [known],
            [new PortableLogbookWorkbookRow(new EntryId("ent_unknown"), new RevisionId("rev_unknown"), Entry("VH-NEW"))],
            new PortableLogbookIdFactory(() => new EntryId("ent_created"), () => new RevisionId("rev_created"))));

        Assert.Equal(PortableLogbookWorkbookProjectionError.InvalidRowMetadata, exception.Error);
        Assert.Contains(exception.RowValidation.Errors, error => error.Code == PortableLogbookWorkbookRowValidationCode.UnknownEntryId);
    }

    private static PortableLogbookProjectionResult Reconcile(
        IEnumerable<PortableLogbookMaterializedEntry> knownEntries,
        IEnumerable<PortableLogbookWorkbookRow> currentRows,
        PortableLogbookIdFactory? idFactory = null) =>
        PortableLogbookWorkbookProjection.Reconcile(
            knownEntries,
            currentRows,
            new LogbookId("log_test"),
            new DeviceId("dev_excel"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            idFactory);

    private static PortableLogbookMaterializedEntry KnownEntry(
        string entryId,
        string revisionId,
        PortableLogbookEntry? entry,
        bool isDeleted = false) =>
        new(
            new EntryId(entryId),
            new RevisionId(revisionId),
            isDeleted,
            entry,
            [new RevisionId(revisionId)]);

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
