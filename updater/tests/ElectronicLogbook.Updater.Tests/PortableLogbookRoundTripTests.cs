using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookRoundTripTests
{
    [Fact]
    public void PackageToPackageRoundTripPreservesFieldsCustomFieldsIdsAndRevisions()
    {
        var customFieldId = new CustomFieldId("cf_training_kind");
        var localCreate = CreateOperation(
            "ent_1",
            "rev_1",
            "VH-AAA",
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            customFieldId,
            "Local");
        var incomingCorrection = new CorrectEntryOperation(
            localCreate.LogbookId,
            localCreate.EntryId,
            new RevisionId("rev_2"),
            new HashSet<RevisionId> { localCreate.RevisionId },
            new DeviceId("dev_mobile"),
            localCreate.CreatedAt.AddMinutes(1),
            Entry("VH-AAB", customFieldId, "Mobile"));
        var local = PortableLogbookDocument.CreateAustraliaFirst(
            localCreate.LogbookId,
            [new CustomFieldDefinition(customFieldId, "Training kind", 1)],
            [localCreate]);
        var incoming = PortableLogbookDocument.CreateAustraliaFirst(
            localCreate.LogbookId,
            [new CustomFieldDefinition(customFieldId, "Training kind", 1)],
            [localCreate, incomingCorrection]);
        var key = PortableLogbookKey.Generate();

        var incomingPackage = PortableLogbookPackage.Write(incoming, key);
        var readIncoming = PortableLogbookPackage.Read(incomingPackage, key, local.LogbookId);
        var applied = PortableLogbookExchange.ApplyImport(local, readIncoming.Document);
        var exportedPackage = PortableLogbookPackage.Write(applied, key);
        var readExported = PortableLogbookPackage.Read(exportedPackage, key, local.LogbookId);

        Assert.Equal([localCreate.RevisionId, incomingCorrection.RevisionId], readExported.Document.Operations.Select(operation => operation.RevisionId));
        var merge = PortableLogbookMerger.Merge(readExported.Document.Operations);
        var current = Assert.Single(merge.Entries.Values);
        Assert.Equal(incomingCorrection.RevisionId, current.CurrentRevisionId);
        Assert.Equal("VH-AAB", current.Entry?.Registration);
        Assert.Equal("Mobile", Assert.Single(current.Entry!.CustomFields).Value);
    }

    private static CreateEntryOperation CreateOperation(
        string entryId,
        string revisionId,
        string registration,
        DateTimeOffset createdAt,
        CustomFieldId customFieldId,
        string customFieldValue) =>
        new(
            new LogbookId("log_roundtrip"),
            new EntryId(entryId),
            new RevisionId(revisionId),
            new DeviceId("dev_excel"),
            createdAt,
            Entry(registration, customFieldId, customFieldValue));

    private static PortableLogbookEntry Entry(string registration, CustomFieldId customFieldId, string customFieldValue) =>
        PortableLogbookEntry.Empty with
        {
            Date = new DateOnly(2026, 7, 18),
            AircraftType = "C172",
            Registration = registration,
            From = "YSBK",
            To = "YSCN",
            PilotInCommand = 1.2m,
            CustomFields = new Dictionary<CustomFieldId, string?> { [customFieldId] = customFieldValue }
        };
}
