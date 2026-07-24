namespace ElectronicLogbook.Updater.Tests;

using ElectronicLogbook.Portable;

public sealed class PortableLogbookDocumentV2Tests
{
    [Fact]
    public void CreateAustraliaFirstUsesWorkbookFaithfulSchemaAndOrdersCustomFieldsAndOperations()
    {
        var logbookId = new LogbookId("log_v2");
        var first = CreateOperation(logbookId, "ent_1", "rev_1", DateTimeOffset.Parse("2026-07-23T00:00:00Z"));
        var second = CreateOperation(logbookId, "ent_2", "rev_2", DateTimeOffset.Parse("2026-07-23T00:01:00Z"));
        var overrides = new PortableLogbookCurrencyOverrideDates(
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 2),
            new DateOnly(2026, 7, 3));

        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(
            logbookId,
            [
                new CustomFieldDefinition(new CustomFieldId("cf_workbook_2"), "Custom 2", 2),
                new CustomFieldDefinition(new CustomFieldId("cf_workbook_1"), "Custom 1", 1)
            ],
            overrides,
            [second, first]);

        Assert.Equal(2, document.SchemaVersion);
        Assert.Equal(PortableLogbookDocumentV2.AustraliaJurisdictionProfile, document.JurisdictionProfile);
        Assert.Equal(PortableLogbookDocumentV2.AustraliaJurisdictionProfileVersion, document.JurisdictionProfileVersion);
        Assert.Equal(["cf_workbook_1", "cf_workbook_2"], document.CustomFieldDefinitions.Select(field => field.Id.Value));
        Assert.Equal(["rev_1", "rev_2"], document.Operations.Select(operation => operation.RevisionId.Value));
        Assert.Equal(overrides, document.CurrencyOverrideDates);
    }

    [Fact]
    public void JsonRoundTripPreservesWorkbookFaithfulEntryAndCurrencyOverrides()
    {
        var logbookId = new LogbookId("log_v2");
        var customFieldId = new CustomFieldId("cf_workbook_1");
        var operation = CreateOperation(logbookId, "ent_1", "rev_1", DateTimeOffset.Parse("2026-07-24T00:00:00Z"));
        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(
            logbookId,
            [new CustomFieldDefinition(customFieldId, "Custom 1", 1)],
            new PortableLogbookCurrencyOverrideDates(new DateOnly(2026, 6, 1), null, null),
            [operation]);

        var json = PortableLogbookJson.SerializeV2(document);
        var roundTripped = PortableLogbookJson.DeserializeV2(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(2, roundTripped.SchemaVersion);
        Assert.Equal(new DateOnly(2026, 6, 1), roundTripped.CurrencyOverrideDates.FlightReview);
        var roundTrippedOperation = Assert.Single(roundTripped.Operations);
        Assert.Equal(PortableOperationKind.Create, roundTrippedOperation.Kind);
        Assert.NotNull(roundTrippedOperation.Entry);
        Assert.Equal(2026, roundTrippedOperation.Entry.Year);
        Assert.True(roundTrippedOperation.Entry.FlightReview);
        Assert.Equal("Alpha", roundTrippedOperation.Entry.CustomFields[customFieldId]);
        Assert.Equal(1.2m, roundTrippedOperation.Entry.SeCommandDay);
        Assert.Equal(2, roundTrippedOperation.Entry.Ils);
    }

    [Fact]
    public void SerializedV2EntryUsesWorkbookFieldNamesNotAbandonedCollapsedV1Names()
    {
        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(
            new LogbookId("log_v2"),
            PortableLogbookCustomFieldSet.CreateWorkbookCustomFields(["Custom 1", "Custom 2", "Custom 3", "Custom 4"]),
            PortableLogbookCurrencyOverrideDates.Empty,
            [CreateOperation(new LogbookId("log_v2"), "ent_1", "rev_1", DateTimeOffset.Parse("2026-07-24T00:00:00Z"))]);

        var json = PortableLogbookJson.SerializeV2(document);

        Assert.Contains("\"seCommandDay\"", json, StringComparison.Ordinal);
        Assert.Contains("\"ifrIf\"", json, StringComparison.Ordinal);
        Assert.Contains("\"ils\"", json, StringComparison.Ordinal);
        foreach (var abandonedProperty in new[]
        {
            "aircraftType",
            "registration",
            "flightNumber",
            "multiPilot",
            "pilotInCommand",
            "coPilot",
            "takeoffsDay",
            "ifrApproaches",
            "holding"
        })
        {
            Assert.DoesNotContain($"\"{abandonedProperty}\"", json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ValidateAcceptsWorkbookFaithfulV2Document()
    {
        var logbookId = new LogbookId("log_v2");
        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(
            logbookId,
            [new CustomFieldDefinition(new CustomFieldId("cf_workbook_1"), "Custom 1", 1)],
            PortableLogbookCurrencyOverrideDates.Empty,
            [CreateOperation(logbookId, "ent_1", "rev_1", DateTimeOffset.Parse("2026-07-24T00:00:00Z"))]);

        var result = PortableLogbookValidatorV2.Validate(document, new DateOnly(2026, 7, 24));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateRejectsV1SchemaWithReimportMessage()
    {
        var logbookId = new LogbookId("log_v2");
        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(
            logbookId,
            [new CustomFieldDefinition(new CustomFieldId("cf_workbook_1"), "Custom 1", 1)],
            PortableLogbookCurrencyOverrideDates.Empty,
            [CreateOperation(logbookId, "ent_1", "rev_1", DateTimeOffset.Parse("2026-07-24T00:00:00Z"))])
            with
            {
                SchemaVersion = 1
            };

        var result = PortableLogbookValidatorV2.Validate(document, new DateOnly(2026, 7, 24));

        var error = Assert.Single(result.Errors, error => error.Code == PortableLogbookValidationCode.UnsupportedSchemaVersion);
        Assert.Contains("Re-import the authoritative workbook", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRejectsUnknownCustomFieldAndNegativeWorkbookFacts()
    {
        var logbookId = new LogbookId("log_v2");
        var operation = CreateOperation(logbookId, "ent_1", "rev_1", DateTimeOffset.Parse("2026-07-24T00:00:00Z")) with
        {
            Entry = CreateOperation(logbookId, "ent_1", "rev_1", DateTimeOffset.Parse("2026-07-24T00:00:00Z")).Entry! with
            {
                CustomFields = new Dictionary<CustomFieldId, string?> { [new("cf_unknown")] = "Unexpected" },
                SeCommandDay = -0.1m,
                Ils = -1
            }
        };
        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(
            logbookId,
            [new CustomFieldDefinition(new CustomFieldId("cf_workbook_1"), "Custom 1", 1)],
            PortableLogbookCurrencyOverrideDates.Empty,
            [operation]);

        var result = PortableLogbookValidatorV2.Validate(document, new DateOnly(2026, 7, 24));

        Assert.Contains(result.Errors, error => error.Code == PortableLogbookValidationCode.UnknownCustomFieldId);
        Assert.Contains(result.Errors, error => error.Code == PortableLogbookValidationCode.InvalidEntryField && error.Message.Contains("SeCommandDay", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Code == PortableLogbookValidationCode.InvalidEntryField && error.Message.Contains("ILS", StringComparison.Ordinal));
    }

    private static PortableLogbookOperationV2 CreateOperation(
        LogbookId logbookId,
        string entryId,
        string revisionId,
        DateTimeOffset createdAt)
    {
        var customFieldId = new CustomFieldId("cf_workbook_1");
        var entry = PortableLogbookWorkbookEntry.Empty with
        {
            Year = 2026,
            Month = 7,
            Day = 24,
            Type = "DA40",
            Reg = "VH-ABC",
            FlightId = "ELB24",
            Pic = "A Delta",
            From = "YSBK",
            To = "YSCN",
            Via = "BK CN",
            Remarks = "Training",
            FlightReview = true,
            InstrumentProficiencyCheck = false,
            OperatorProficiencyCheck = true,
            CustomFields = new Dictionary<CustomFieldId, string?> { [customFieldId] = "Alpha" },
            SeCommandDay = 1.2m,
            LandingsDay = 1,
            Ils = 2
        };

        return PortableLogbookOperationV2.Create(
            logbookId,
            new EntryId(entryId),
            new RevisionId(revisionId),
            new DeviceId("dev_mobile"),
            createdAt,
            entry);
    }
}
