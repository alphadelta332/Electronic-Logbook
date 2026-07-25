namespace ElectronicLogbook.Updater.Tests;

using ElectronicLogbook.Portable;

public sealed class PortableLogbookWorkbookProjectionV2Tests
{
    [Fact]
    public void CreateCurrentRowsReturnsWorkbookFaithfulCurrentRowsWithStableMetadata()
    {
        var create = Create("ent_1", "rev_create", Entry("VH-OLD"));
        var correction = PortableLogbookOperationV2.Correct(
            create.LogbookId,
            create.EntryId,
            new RevisionId("rev_correct"),
            [create.RevisionId],
            create.DeviceId,
            create.CreatedAt.AddMinutes(1),
            Entry("VH-NEW"));
        var deleted = Create("ent_2", "rev_deleted_create", Entry("VH-DEL"));
        var deletion = PortableLogbookOperationV2.Delete(
            deleted.LogbookId,
            deleted.EntryId,
            new RevisionId("rev_deleted"),
            [deleted.RevisionId],
            deleted.DeviceId,
            deleted.CreatedAt.AddMinutes(1),
            "Removed");
        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(
            create.LogbookId,
            PortableLogbookCustomFieldSet.CreateWorkbookCustomFields(["Custom 1", "Custom 2", "Custom 3", "Custom 4"]),
            PortableLogbookCurrencyOverrideDates.Empty,
            [create, correction, deleted, deletion]);

        var rows = PortableLogbookWorkbookProjection.CreateCurrentRows(document);

        var row = Assert.Single(rows);
        Assert.Equal(create.EntryId, row.EntryId);
        Assert.Equal(correction.RevisionId, row.CurrentRevisionId);
        Assert.Equal("VH-NEW", row.Entry.Reg);
    }

    [Fact]
    public void ReconcileV2CreatesInsertedWorkbookRowsAndRetriesGeneratedEntryIdCollisions()
    {
        var known = KnownEntry("ent_existing", "rev_existing", Entry("VH-ABC"));
        var generatedIds = new Queue<EntryId>([
            known.EntryId,
            new EntryId("ent_new")
        ]);

        var result = Reconcile(
            [known],
            [
                new PortableLogbookWorkbookRowV2(null, null, Entry("VH-NEW")),
                new PortableLogbookWorkbookRowV2(known.EntryId, known.CurrentRevisionId, Entry("VH-ABC"))
            ],
            new PortableLogbookIdFactory(() => generatedIds.Dequeue(), () => new RevisionId("rev_new")));

        var create = Assert.Single(result.Operations);
        Assert.Equal(PortableOperationKind.Create, create.Kind);
        Assert.Equal(new EntryId("ent_new"), create.EntryId);
        Assert.Equal(new RevisionId("rev_new"), create.RevisionId);
        Assert.Equal("VH-NEW", create.Entry!.Reg);
        Assert.Equal(1, result.CreateCount);
    }

    [Fact]
    public void ReconcileV2CorrectsAnyChangedWorkbookFaithfulFieldForSameEntryId()
    {
        var customFieldId = new CustomFieldId("cf_workbook_1");
        var knownEntry = Entry("VH-ABC") with
        {
            FlightId = "ABC123",
            Via = "BK",
            Remarks = "Original details",
            CustomFields = new Dictionary<CustomFieldId, string?>
            {
                [customFieldId] = "Original"
            },
            SeCommandDay = 1.2m
        };
        var editedRow = knownEntry with
        {
            FlightId = "ABC124",
            Via = "BK CN",
            Remarks = "Edited directly in workbook",
            CustomFields = new Dictionary<CustomFieldId, string?>
            {
                [customFieldId] = "Edited"
            },
            SeCommandNight = 0.4m,
            IfrIf = 0.3m,
            Ils = 2
        };
        var known = KnownEntry("ent_1", "rev_1", knownEntry);

        var result = Reconcile(
            [known],
            [new PortableLogbookWorkbookRowV2(known.EntryId, known.CurrentRevisionId, editedRow)],
            new PortableLogbookIdFactory(() => new EntryId("unused"), () => new RevisionId("rev_cell_edit")));

        var correction = Assert.Single(result.Operations);
        Assert.Equal(PortableOperationKind.Correction, correction.Kind);
        Assert.Equal(known.EntryId, correction.EntryId);
        Assert.Equal(new RevisionId("rev_cell_edit"), correction.RevisionId);
        Assert.Equal(known.CurrentRevisionId, Assert.Single(correction.ParentRevisionIds));
        Assert.Equal("ABC124", correction.Entry!.FlightId);
        Assert.Equal("BK CN", correction.Entry.Via);
        Assert.Equal("Edited directly in workbook", correction.Entry.Remarks);
        Assert.Equal("Edited", correction.Entry.CustomFields[customFieldId]);
        Assert.Equal(0.4m, correction.Entry.SeCommandNight);
        Assert.Equal(0.3m, correction.Entry.IfrIf);
        Assert.Equal(2, correction.Entry.Ils);
        Assert.Equal(1, result.CorrectionCount);
    }

    [Fact]
    public void ReconcileV2CorrectsKnownRowsWithoutAllocatingReplacementEntryId()
    {
        var known = KnownEntry("ent_1", "rev_1", Entry("VH-ABC"));

        var result = Reconcile(
            [known],
            [new PortableLogbookWorkbookRowV2(known.EntryId, known.CurrentRevisionId, Entry("VH-XYZ"))],
            EntryIdThrowingFactory("rev_correct"));

        var correction = Assert.Single(result.Operations);
        Assert.Equal(PortableOperationKind.Correction, correction.Kind);
        Assert.Equal(known.EntryId, correction.EntryId);
        Assert.Equal(new RevisionId("rev_correct"), correction.RevisionId);
        Assert.Equal(1, result.CorrectionCount);
    }

    [Fact]
    public void ReconcileV2DeletesKnownRowsMissingFromWorkbookProjection()
    {
        var known = KnownEntry("ent_1", "rev_1", Entry("VH-ABC"));

        var result = Reconcile(
            [known],
            [],
            new PortableLogbookIdFactory(() => new EntryId("unused"), () => new RevisionId("rev_delete")));

        var deletion = Assert.Single(result.Operations);
        Assert.Equal(PortableOperationKind.Deletion, deletion.Kind);
        Assert.Equal(known.EntryId, deletion.EntryId);
        Assert.Equal(known.CurrentRevisionId, Assert.Single(deletion.ParentRevisionIds));
        Assert.Null(deletion.Entry);
        Assert.Equal(1, result.DeletionCount);
    }

    [Fact]
    public void ReconcileV2DeletesKnownRowsWithoutAllocatingReplacementEntryId()
    {
        var known = KnownEntry("ent_1", "rev_1", Entry("VH-ABC"));

        var result = Reconcile(
            [known],
            [],
            EntryIdThrowingFactory("rev_delete"));

        var deletion = Assert.Single(result.Operations);
        Assert.Equal(PortableOperationKind.Deletion, deletion.Kind);
        Assert.Equal(known.EntryId, deletion.EntryId);
        Assert.Equal(new RevisionId("rev_delete"), deletion.RevisionId);
        Assert.Equal(1, result.DeletionCount);
    }

    [Fact]
    public void ReconcileV2RejectsDuplicateKnownEntryIdRows()
    {
        var known = KnownEntry("ent_1", "rev_1", Entry("VH-ABC"));

        var exception = Assert.Throws<PortableLogbookWorkbookProjectionException>(() => Reconcile(
            [known],
            [
                new PortableLogbookWorkbookRowV2(known.EntryId, known.CurrentRevisionId, Entry("VH-ABC")),
                new PortableLogbookWorkbookRowV2(known.EntryId, known.CurrentRevisionId, Entry("VH-EDITED"))
            ],
            new PortableLogbookIdFactory(() => new EntryId("ent_created"), () => new RevisionId("rev_created"))));

        Assert.Equal(PortableLogbookWorkbookProjectionError.InvalidRowMetadata, exception.Error);
        Assert.Contains(exception.RowValidation.Errors, error => error.Code == PortableLogbookWorkbookRowValidationCode.DuplicateEntryId);
    }

    private static PortableLogbookProjectionResultV2 Reconcile(
        IEnumerable<PortableLogbookMaterializedEntryV2> knownEntries,
        IEnumerable<PortableLogbookWorkbookRowV2> currentRows,
        PortableLogbookIdFactory? idFactory = null) =>
        PortableLogbookWorkbookProjection.ReconcileV2(
            knownEntries,
            currentRows,
            new LogbookId("log_test"),
            new DeviceId("dev_excel"),
            DateTimeOffset.Parse("2026-07-24T00:00:00Z"),
            idFactory);

    private static PortableLogbookMaterializedEntryV2 KnownEntry(
        string entryId,
        string revisionId,
        PortableLogbookWorkbookEntry? entry,
        bool isDeleted = false) =>
        new(
            new EntryId(entryId),
            new RevisionId(revisionId),
            isDeleted,
            entry,
            [new RevisionId(revisionId)]);

    private static PortableLogbookOperationV2 Create(
        string entryId,
        string revisionId,
        PortableLogbookWorkbookEntry entry) =>
        PortableLogbookOperationV2.Create(
            new LogbookId("log_test"),
            new EntryId(entryId),
            new RevisionId(revisionId),
            new DeviceId("dev_test"),
            DateTimeOffset.Parse("2026-07-24T00:00:00Z"),
            entry);

    private static PortableLogbookWorkbookEntry Entry(string registration) =>
        PortableLogbookWorkbookEntry.Empty with
        {
            Year = 2026,
            Month = 7,
            Day = 24,
            Type = "DA40",
            Reg = registration,
            From = "YSBK",
            To = "YSCN",
            Pic = "A Delta",
            SeCommandDay = 1.2m
        };

    private static PortableLogbookIdFactory EntryIdThrowingFactory(string revisionId) =>
        new(
            () => throw new InvalidOperationException("EntryID allocation was not expected."),
            () => new RevisionId(revisionId));
}
