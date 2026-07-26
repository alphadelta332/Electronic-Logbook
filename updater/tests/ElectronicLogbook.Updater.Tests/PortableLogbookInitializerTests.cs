using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookInitializerTests
{
    [Fact]
    public void CreateInitialDocumentAssignsEntryAndRevisionIdsToExistingRows()
    {
        var entryIndex = 0;
        var revisionIndex = 0;
        var idFactory = new PortableLogbookIdFactory(
            () => new EntryId($"ent_{++entryIndex}"),
            () => new RevisionId($"rev_{++revisionIndex}"));
        var logbookId = new LogbookId("log_initial");
        var deviceId = new DeviceId("dev_excel");
        var createdAt = DateTimeOffset.Parse("2026-07-18T00:00:00Z");

        var document = PortableLogbookInitializer.CreateInitialDocument(
            [Entry("VH-ABC"), Entry("VH-XYZ")],
            [],
            logbookId,
            deviceId,
            createdAt,
            idFactory);

        Assert.Equal([new EntryId("ent_1"), new EntryId("ent_2")], document.Operations.Select(operation => operation.EntryId));
        Assert.Equal([new RevisionId("rev_1"), new RevisionId("rev_2")], document.Operations.Select(operation => operation.RevisionId));
        Assert.All(document.Operations, operation =>
        {
            Assert.IsType<CreateEntryOperation>(operation);
            Assert.Equal(logbookId, operation.LogbookId);
            Assert.Equal(deviceId, operation.DeviceId);
            Assert.Equal(createdAt, operation.CreatedAt);
            Assert.Empty(operation.ParentRevisionIds);
        });
    }

    [Fact]
    public void EntryIdFactoryRetriesCollisionAgainstAllocatedIds()
    {
        var candidates = new Queue<EntryId>([new EntryId("ent_1"), new EntryId("ent_2")]);
        var idFactory = new PortableLogbookIdFactory(candidates.Dequeue, RevisionId.New);

        var entryId = idFactory.NewEntryIdExcluding(new HashSet<EntryId> { new("ent_1") });

        Assert.Equal(new EntryId("ent_2"), entryId);
    }

    [Fact]
    public void EntryIdFactoryRejectsMalformedCandidatesBeforeAllocation()
    {
        var candidates = new Queue<EntryId>([new EntryId("not_an_entry_id"), new EntryId("ent_2")]);
        var idFactory = new PortableLogbookIdFactory(candidates.Dequeue, RevisionId.New);

        var entryId = idFactory.NewEntryIdExcluding(new HashSet<EntryId>());

        Assert.Equal(new EntryId("ent_2"), entryId);
    }

    [Fact]
    public void CreateInitialDocumentOrdersCustomFieldDefinitionsAndValidates()
    {
        var fieldId = new CustomFieldId("cf_training_kind");
        var document = PortableLogbookInitializer.CreateInitialDocument(
            [Entry("VH-ABC", new Dictionary<CustomFieldId, string?> { [fieldId] = "Training" })],
            [
                new CustomFieldDefinition(new CustomFieldId("cf_second"), "Second", 2),
                new CustomFieldDefinition(fieldId, "Training kind", 1)
            ],
            new LogbookId("log_initial"),
            new DeviceId("dev_excel"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            new PortableLogbookIdFactory(() => new EntryId("ent_1"), () => new RevisionId("rev_1")));

        var validation = PortableLogbookValidator.Validate(document);
        var merge = PortableLogbookMerger.Merge(document.Operations);

        Assert.True(validation.IsValid);
        Assert.Equal(["cf_training_kind", "cf_second"], document.CustomFieldDefinitions.Select(field => field.Id.Value));
        Assert.Empty(merge.Conflicts);
        Assert.Equal("Training", Assert.Single(Assert.Single(merge.Entries.Values).Entry!.CustomFields).Value);
    }

    private static PortableLogbookEntry Entry(
        string registration,
        IReadOnlyDictionary<CustomFieldId, string?>? customFields = null) =>
        PortableLogbookEntry.Empty with
        {
            Date = new DateOnly(2026, 7, 18),
            AircraftType = "C172",
            Registration = registration,
            From = "YSBK",
            To = "YSBK",
            PilotInCommand = 1.2m,
            CustomFields = customFields ?? new Dictionary<CustomFieldId, string?>()
        };
}
