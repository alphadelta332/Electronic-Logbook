using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookDocumentTests
{
    [Fact]
    public void CreateAustraliaFirstOrdersCustomFieldsAndOperations()
    {
        var logbookId = new LogbookId("log_schema");
        var firstOperation = CreateOperation(logbookId, "ent_1", "rev_1", DateTimeOffset.Parse("2026-07-18T00:00:00Z"));
        var secondOperation = CreateOperation(logbookId, "ent_2", "rev_2", DateTimeOffset.Parse("2026-07-18T00:01:00Z"));

        var document = PortableLogbookDocument.CreateAustraliaFirst(
            logbookId,
            [
                new CustomFieldDefinition(new CustomFieldId("cf_second"), "Second", 2),
                new CustomFieldDefinition(new CustomFieldId("cf_first"), "First", 1)
            ],
            [secondOperation, firstOperation]);

        Assert.Equal(PortableLogbookDocument.CurrentSchemaVersion, document.SchemaVersion);
        Assert.Equal(PortableLogbookDocument.AustraliaJurisdictionProfile, document.JurisdictionProfile);
        Assert.Equal(["cf_first", "cf_second"], document.CustomFieldDefinitions.Select(field => field.Id.Value));
        Assert.Equal(["rev_1", "rev_2"], document.Operations.Select(operation => operation.RevisionId.Value));
    }

    [Fact]
    public void JsonRoundTripPreservesOperationTypesAndStableCustomFieldIds()
    {
        var logbookId = new LogbookId("log_schema");
        var customFieldId = new CustomFieldId("cf_training_kind");
        var create = CreateOperation(logbookId, "ent_1", "rev_1", DateTimeOffset.Parse("2026-07-18T00:00:00Z"), customFieldId);
        var correction = new CorrectEntryOperation(
            logbookId,
            create.EntryId,
            new RevisionId("rev_2"),
            new HashSet<RevisionId> { create.RevisionId },
            new DeviceId("dev_mobile"),
            create.CreatedAt.AddMinutes(1),
            create.Entry with { Details = "Corrected" });
        var document = PortableLogbookDocument.CreateAustraliaFirst(
            logbookId,
            [new CustomFieldDefinition(customFieldId, "Training kind", 1)],
            [create, correction]);

        var json = PortableLogbookJson.Serialize(document);
        var roundTripped = PortableLogbookJson.Deserialize(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(document.SchemaVersion, roundTripped.SchemaVersion);
        Assert.Equal(customFieldId, Assert.Single(roundTripped.CustomFieldDefinitions).Id);
        Assert.IsType<CreateEntryOperation>(roundTripped.Operations[0]);
        var roundTrippedCorrection = Assert.IsType<CorrectEntryOperation>(roundTripped.Operations[1]);
        Assert.Equal(create.RevisionId, Assert.Single(roundTrippedCorrection.ParentRevisionIds));
        Assert.Equal("Training", Assert.Single(((CreateEntryOperation)roundTripped.Operations[0]).Entry.CustomFields).Value);
    }

    private static CreateEntryOperation CreateOperation(
        LogbookId logbookId,
        string entryId,
        string revisionId,
        DateTimeOffset createdAt,
        CustomFieldId? customFieldId = null) =>
        new(
            logbookId,
            new EntryId(entryId),
            new RevisionId(revisionId),
            new DeviceId("dev_excel"),
            createdAt,
            (PortableLogbookEntry.Empty with
            {
                Date = DateOnly.FromDateTime(createdAt.UtcDateTime),
                AircraftType = "C172",
                Registration = "VH-ABC",
                From = "YSBK",
                To = "YSBK",
                PilotInCommand = 1.2m,
                CustomFields = customFieldId is null
                    ? new Dictionary<CustomFieldId, string?>()
                    : new Dictionary<CustomFieldId, string?> { [customFieldId.Value] = "Training" }
            }));
}
