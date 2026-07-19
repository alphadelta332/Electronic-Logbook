using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookValidatorTests
{
    [Fact]
    public void ValidateAcceptsLinearHistoryWithDeclaredCustomFields()
    {
        var fieldId = new CustomFieldId("cf_training_kind");
        var create = CreateOperation(customFields: new Dictionary<CustomFieldId, string?> { [fieldId] = "Training" });
        var correction = new CorrectEntryOperation(
            create.LogbookId,
            create.EntryId,
            new RevisionId("rev_correct"),
            new HashSet<RevisionId> { create.RevisionId },
            create.DeviceId,
            create.CreatedAt.AddMinutes(1),
            create.Entry with { Details = "Corrected details" });
        var document = PortableLogbookDocument.CreateAustraliaFirst(
            create.LogbookId,
            [new CustomFieldDefinition(fieldId, "Training kind", 1)],
            [correction, create]);

        var result = PortableLogbookValidator.Validate(document);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(2, PortableLogbookValidationCode.UnsupportedSchemaVersion)]
    [InlineData(1, PortableLogbookValidationCode.MissingJurisdictionProfile)]
    [InlineData(0, PortableLogbookValidationCode.InvalidJurisdictionProfileVersion)]
    public void ValidateRejectsInvalidDocumentEnvelope(int schemaVersion, PortableLogbookValidationCode expectedCode)
    {
        var create = CreateOperation();
        var document = PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [create]) with
        {
            SchemaVersion = schemaVersion,
            JurisdictionProfile = schemaVersion == 1 ? "" : PortableLogbookDocument.AustraliaJurisdictionProfile,
            JurisdictionProfileVersion = schemaVersion == 0 ? 0 : PortableLogbookDocument.AustraliaJurisdictionProfileVersion
        };

        var result = PortableLogbookValidator.Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == expectedCode);
    }

    [Fact]
    public void ValidateRejectsDuplicateCustomFieldDefinitions()
    {
        var create = CreateOperation();
        var fieldId = new CustomFieldId("cf_duplicate");
        var document = PortableLogbookDocument.CreateAustraliaFirst(
            create.LogbookId,
            [
                new CustomFieldDefinition(fieldId, "First", 1),
                new CustomFieldDefinition(fieldId, "Second", 2)
            ],
            [create]);

        var result = PortableLogbookValidator.Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == PortableLogbookValidationCode.DuplicateCustomFieldId);
    }

    [Fact]
    public void ValidateRejectsBlankDocumentAndCustomFieldIdentifiers()
    {
        var document = PortableLogbookDocument.CreateAustraliaFirst(
            new LogbookId(" "),
            [new CustomFieldDefinition(new CustomFieldId(" "), "Training", 1)],
            []);

        var result = PortableLogbookValidator.Validate(document);

        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count(error => error.Code == PortableLogbookValidationCode.InvalidIdentifier) >= 2);
    }

    [Fact]
    public void ValidateRejectsBlankOperationIdentifiers()
    {
        var create = CreateOperation() with
        {
            EntryId = new EntryId(" "),
            RevisionId = new RevisionId(" "),
            DeviceId = new DeviceId(" ")
        };
        var document = PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [create]);

        var result = PortableLogbookValidator.Validate(document);

        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count(error => error.Code == PortableLogbookValidationCode.InvalidIdentifier) >= 3);
    }

    [Fact]
    public void ValidateRejectsBlankParentRevisionIdentifiers()
    {
        var create = CreateOperation();
        var correction = new CorrectEntryOperation(
            create.LogbookId,
            create.EntryId,
            new RevisionId("rev_correction"),
            new HashSet<RevisionId> { new(" ") },
            create.DeviceId,
            create.CreatedAt.AddMinutes(1),
            create.Entry);
        var document = PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [create, correction]);

        var result = PortableLogbookValidator.Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == PortableLogbookValidationCode.InvalidIdentifier);
    }

    [Fact]
    public void ValidateRejectsConflictingDuplicateRevisionPayloads()
    {
        var create = CreateOperation();
        var conflictingDuplicate = create with
        {
            DeviceId = new DeviceId("dev_other"),
            Entry = create.Entry with { Registration = "VH-XYZ" }
        };
        var document = PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [create, conflictingDuplicate]);

        var result = PortableLogbookValidator.Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == PortableLogbookValidationCode.DuplicateRevisionId);
    }

    [Fact]
    public void ValidateRejectsOperationForDifferentLogbook()
    {
        var create = CreateOperation();
        var document = PortableLogbookDocument.CreateAustraliaFirst(new LogbookId("log_other"), [], [create]);

        var result = PortableLogbookValidator.Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == PortableLogbookValidationCode.OperationLogbookMismatch);
    }

    [Fact]
    public void ValidateRejectsMissingAndSelfParentReferences()
    {
        var create = CreateOperation();
        var correction = new CorrectEntryOperation(
            create.LogbookId,
            create.EntryId,
            new RevisionId("rev_missing_parent"),
            new HashSet<RevisionId> { new("rev_absent"), new("rev_missing_parent") },
            create.DeviceId,
            create.CreatedAt.AddMinutes(1),
            create.Entry);
        var document = PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [create, correction]);

        var result = PortableLogbookValidator.Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == PortableLogbookValidationCode.MissingParentRevision);
        Assert.Contains(result.Errors, error => error.Code == PortableLogbookValidationCode.SelfParentRevision);
    }

    [Fact]
    public void ValidateRequiresConflictResolutionToReferenceAtLeastTwoParents()
    {
        var create = CreateOperation();
        var resolution = new ResolveConflictOperation(
            create.LogbookId,
            create.EntryId,
            new RevisionId("rev_resolution"),
            new HashSet<RevisionId> { create.RevisionId },
            create.DeviceId,
            create.CreatedAt.AddMinutes(1),
            create.Entry);
        var document = PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [create, resolution]);

        var result = PortableLogbookValidator.Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == PortableLogbookValidationCode.InvalidParentCount);
    }

    [Fact]
    public void ValidateRejectsUndefinedCustomFieldReferences()
    {
        var create = CreateOperation(customFields: new Dictionary<CustomFieldId, string?>
        {
            [new CustomFieldId("cf_unknown")] = "Training"
        });
        var document = PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [create]);

        var result = PortableLogbookValidator.Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == PortableLogbookValidationCode.UnknownCustomFieldId);
    }

    [Fact]
    public void ValidateRejectsInvalidEntryPayloads()
    {
        var create = CreateOperation() with
        {
            Entry = CreateOperation().Entry with
            {
                Registration = "",
                PilotInCommand = 0
            }
        };
        var document = PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [create]);

        var result = PortableLogbookValidator.Validate(document, new DateOnly(2026, 7, 19));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == PortableLogbookValidationCode.InvalidEntryField);
        Assert.Contains(result.Errors, error => error.Message.Contains("Registration is required", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Message.Contains("cannot be zero", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateUsesSuppliedTodayForFutureDateChecks()
    {
        var create = CreateOperation() with
        {
            Entry = CreateOperation().Entry with { Date = new DateOnly(2026, 7, 20) }
        };
        var document = PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [create]);

        var futureForSuppliedDate = PortableLogbookValidator.Validate(document, new DateOnly(2026, 7, 19));
        var validOnSuppliedDate = PortableLogbookValidator.Validate(document, new DateOnly(2026, 7, 20));

        Assert.Contains(futureForSuppliedDate.Errors, error => error.Code == PortableLogbookValidationCode.InvalidEntryField);
        Assert.True(validOnSuppliedDate.IsValid);
    }

    [Fact]
    public void ValidateRejectsCyclicRevisionHistory()
    {
        var create = CreateOperation();
        var revisionA = new CorrectEntryOperation(
            create.LogbookId,
            create.EntryId,
            new RevisionId("rev_a"),
            new HashSet<RevisionId> { new("rev_b") },
            create.DeviceId,
            create.CreatedAt.AddMinutes(1),
            create.Entry);
        var revisionB = new CorrectEntryOperation(
            create.LogbookId,
            create.EntryId,
            new RevisionId("rev_b"),
            new HashSet<RevisionId> { revisionA.RevisionId },
            create.DeviceId,
            create.CreatedAt.AddMinutes(2),
            create.Entry);
        var document = PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [create, revisionA, revisionB]);

        var result = PortableLogbookValidator.Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == PortableLogbookValidationCode.CyclicRevisionHistory);
    }

    private static CreateEntryOperation CreateOperation(IReadOnlyDictionary<CustomFieldId, string?>? customFields = null) =>
        new(
            new LogbookId("log_test"),
            new EntryId("ent_1"),
            new RevisionId("rev_create"),
            new DeviceId("dev_excel"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            PortableLogbookEntry.Empty with
            {
                Date = new DateOnly(2026, 7, 18),
                AircraftType = "C172",
                Registration = "VH-ABC",
                From = "YSBK",
                To = "YSBK",
                PilotInCommand = 1.2m,
                CustomFields = customFields ?? new Dictionary<CustomFieldId, string?>()
            });
}
