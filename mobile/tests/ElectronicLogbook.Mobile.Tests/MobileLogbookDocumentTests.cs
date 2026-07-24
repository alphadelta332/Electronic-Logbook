using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileLogbookDocumentTests
{
    [Fact]
    public void AppendOperationV2PreservesCustomFieldsAndCurrencyOverrides()
    {
        var existingField = new CustomFieldDefinition(new CustomFieldId("cf_training_kind"), "Training kind", 1);
        var requiredField = new CustomFieldDefinition(new CustomFieldId("cf_workbook_1"), "Custom 1", 2);
        var create = PortableLogbookOperationV2.Create(
            new LogbookId("log_mobile"),
            new EntryId("ent_00000000000000000000000000000001"),
            new RevisionId("rev_create"),
            new DeviceId("dev_mobile"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            WorkbookEntry() with
            {
                CustomFields = new Dictionary<CustomFieldId, string?> { [existingField.Id] = "Training" }
            });
        var overrides = new PortableLogbookCurrencyOverrideDates(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 2),
            new DateOnly(2026, 6, 3));
        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(
            create.LogbookId,
            [existingField],
            overrides,
            [create]);
        var correction = PortableLogbookOperationV2.Correct(
            create.LogbookId,
            create.EntryId,
            new RevisionId("rev_correct"),
            [create.RevisionId],
            create.DeviceId,
            create.CreatedAt.AddMinutes(1),
            WorkbookEntry() with { Remarks = "Corrected" });

        var updated = MobileLogbookDocument.AppendOperation(document, [requiredField], correction);

        Assert.Contains(updated.CustomFieldDefinitions, field => field == existingField);
        Assert.Contains(updated.CustomFieldDefinitions, field => field == requiredField);
        Assert.Equal(overrides, updated.CurrencyOverrideDates);
        Assert.Equal([create.RevisionId, correction.RevisionId], updated.Operations.Select(operation => operation.RevisionId));
        Assert.True(PortableLogbookValidatorV2.Validate(updated, new DateOnly(2026, 7, 19)).IsValid);
    }

    [Fact]
    public void AppendOperationPreservesExistingCustomFieldDefinitionsAndAddsRequiredFields()
    {
        var existingField = new CustomFieldDefinition(new CustomFieldId("cf_training_kind"), "Training kind", 1);
        var requiredField = new CustomFieldDefinition(new CustomFieldId("cf_workbook_1"), "Custom 1", 2);
        var create = CreateOperation(existingField.Id);
        var document = PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [existingField], [create]);
        var correction = new CorrectEntryOperation(
            create.LogbookId,
            create.EntryId,
            new RevisionId("rev_correct"),
            new HashSet<RevisionId> { create.RevisionId },
            create.DeviceId,
            create.CreatedAt.AddMinutes(1),
            create.Entry with { Details = "Corrected" });

        var updated = MobileLogbookDocument.AppendOperation(document, [requiredField], correction);

        Assert.Contains(updated.CustomFieldDefinitions, field => field == existingField);
        Assert.Contains(updated.CustomFieldDefinitions, field => field == requiredField);
        Assert.Equal([create.RevisionId, correction.RevisionId], updated.Operations.Select(operation => operation.RevisionId));
        Assert.True(PortableLogbookValidator.Validate(updated, new DateOnly(2026, 7, 19)).IsValid);
    }

    private static CreateEntryOperation CreateOperation(CustomFieldId customFieldId) =>
        new(
            new LogbookId("log_mobile"),
            new EntryId("ent_1"),
            new RevisionId("rev_create"),
            new DeviceId("dev_mobile"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            PortableLogbookEntry.Empty with
            {
                Date = new DateOnly(2026, 7, 18),
                AircraftType = "C172",
                Registration = "VH-ABC",
                From = "YSBK",
                To = "YSCN",
                PilotInCommand = 1.2m,
                CustomFields = new Dictionary<CustomFieldId, string?> { [customFieldId] = "Training" }
            });

    private static PortableLogbookWorkbookEntry WorkbookEntry() =>
        PortableLogbookWorkbookEntry.Empty with
        {
            Year = 2026,
            Month = 7,
            Day = 18,
            Type = "C172",
            Reg = "VH-ABC",
            From = "YSBK",
            To = "YSCN",
            Pic = "Pilot",
            SeCommandDay = 1.2m
        };
}
