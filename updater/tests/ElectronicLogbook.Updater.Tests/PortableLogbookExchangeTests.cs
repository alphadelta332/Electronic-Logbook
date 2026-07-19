using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookExchangeTests
{
    [Fact]
    public void PreviewImportTreatsAlreadySeenPackageAsDuplicateReplay()
    {
        var create = CreateOperation();
        var local = Document(create.LogbookId, [create]);
        var incoming = Document(create.LogbookId, [create]);

        var preview = PortableLogbookExchange.PreviewImport(local, incoming);

        Assert.Empty(preview.NewOperations);
        Assert.Equal(1, preview.DuplicateOperationCount);
        var duplicate = Assert.Single(preview.DuplicateOperations);
        Assert.Equal(create.RevisionId, duplicate.RevisionId);
        var duplicateSummary = Assert.Single(preview.DuplicateOperationSummaries);
        Assert.Equal(create.EntryId, duplicateSummary.EntryId);
        Assert.Equal(create.RevisionId, duplicateSummary.RevisionId);
        Assert.Equal(PortableOperationKind.Create, duplicateSummary.Kind);
        Assert.Equal("VH-ABC", duplicateSummary.Registration);
        Assert.False(preview.HasConflicts);
    }

    [Fact]
    public void PreviewImportCountsIncomingCorrectionWithoutRollingBackLocalHistory()
    {
        var create = CreateOperation();
        var correction = CorrectOperation(create, "VH-XYZ", "rev_correct");
        var local = Document(create.LogbookId, [create]);
        var incoming = Document(create.LogbookId, [create, correction]);

        var preview = PortableLogbookExchange.PreviewImport(local, incoming);

        Assert.Equal(1, preview.DuplicateOperationCount);
        Assert.Equal(1, preview.CorrectionCount);
        Assert.Equal(correction.RevisionId, Assert.Single(preview.NewOperations).RevisionId);
        var summary = Assert.Single(preview.NewOperationSummaries);
        Assert.Equal(create.EntryId, summary.EntryId);
        Assert.Equal(correction.RevisionId, summary.RevisionId);
        Assert.Equal(PortableOperationKind.Correction, summary.Kind);
        Assert.Equal("VH-XYZ", summary.Registration);
        Assert.Equal(new DateOnly(2026, 7, 18), summary.Date);
        Assert.Equal("YSBK", summary.From);
        Assert.Equal("YSBK", summary.To);
        Assert.False(preview.HasConflicts);
    }

    [Fact]
    public void PreviewImportCountsIncomingDeletion()
    {
        var create = CreateOperation();
        var deletion = new DeleteEntryOperation(
            create.LogbookId,
            create.EntryId,
            new RevisionId("rev_delete"),
            new HashSet<RevisionId> { create.RevisionId },
            create.DeviceId,
            create.CreatedAt.AddMinutes(1));
        var local = Document(create.LogbookId, [create]);
        var incoming = Document(create.LogbookId, [create, deletion]);

        var preview = PortableLogbookExchange.PreviewImport(local, incoming);

        Assert.Equal(1, preview.DeletionCount);
        var summary = Assert.Single(preview.NewOperationSummaries);
        Assert.Equal(create.EntryId, summary.EntryId);
        Assert.Equal(deletion.RevisionId, summary.RevisionId);
        Assert.Equal(PortableOperationKind.Deletion, summary.Kind);
        Assert.Null(summary.Registration);
        Assert.False(preview.HasConflicts);
    }

    [Fact]
    public void PreviewImportReportsDivergentRevisionConflict()
    {
        var create = CreateOperation();
        var localCorrection = CorrectOperation(create, "VH-LOCAL", "rev_local");
        var incomingCorrection = CorrectOperation(create, "VH-INCOMING", "rev_incoming");
        var local = Document(create.LogbookId, [create, localCorrection]);
        var incoming = Document(create.LogbookId, [create, incomingCorrection]);

        var preview = PortableLogbookExchange.PreviewImport(local, incoming);

        Assert.True(preview.HasConflicts);
        var conflict = Assert.Single(preview.Conflicts);
        Assert.Equal(create.EntryId, conflict.EntryId);
        Assert.Equal([incomingCorrection.RevisionId, localCorrection.RevisionId], conflict.HeadRevisionIds);
    }

    [Fact]
    public void PreviewImportRejectsWrongLogbook()
    {
        var create = CreateOperation();
        var local = Document(create.LogbookId, [create]);
        var incoming = Document(new LogbookId("log_other"), [create with { LogbookId = new LogbookId("log_other") }]);

        var exception = Assert.Throws<ArgumentException>(() => PortableLogbookExchange.PreviewImport(local, incoming));

        Assert.Equal("incomingDocument", exception.ParamName);
    }

    [Fact]
    public void ApplyImportKeepsAlreadySeenPackageIdempotent()
    {
        var create = CreateOperation();
        var local = Document(create.LogbookId, [create]);
        var incoming = Document(create.LogbookId, [create]);

        var applied = PortableLogbookExchange.ApplyImport(local, incoming);

        Assert.Equal([create.RevisionId], applied.Operations.Select(operation => operation.RevisionId));
    }

    [Fact]
    public void ApplyImportAddsNewOperationsWhenPreviewHasNoConflicts()
    {
        var create = CreateOperation();
        var correction = CorrectOperation(create, "VH-XYZ", "rev_correct");
        var local = Document(create.LogbookId, [create]);
        var incoming = Document(create.LogbookId, [create, correction]);

        var applied = PortableLogbookExchange.ApplyImport(local, incoming);

        Assert.Equal([create.RevisionId, correction.RevisionId], applied.Operations.Select(operation => operation.RevisionId));
    }

    [Fact]
    public void ApplyImportAddsIncomingCustomFieldDefinitions()
    {
        var create = CreateOperation();
        var incomingField = new CustomFieldDefinition(new CustomFieldId("cf_incoming"), "Incoming", 1);
        var local = Document(create.LogbookId, [create]);
        var incoming = PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [incomingField], [create]);

        var applied = PortableLogbookExchange.ApplyImport(local, incoming);

        Assert.Equal(incomingField, Assert.Single(applied.CustomFieldDefinitions));
    }

    [Fact]
    public void ApplyImportBlocksUnresolvedConflicts()
    {
        var create = CreateOperation();
        var localCorrection = CorrectOperation(create, "VH-LOCAL", "rev_local");
        var incomingCorrection = CorrectOperation(create, "VH-INCOMING", "rev_incoming");
        var local = Document(create.LogbookId, [create, localCorrection]);
        var incoming = Document(create.LogbookId, [create, incomingCorrection]);

        var exception = Assert.Throws<PortableLogbookImportException>(() => PortableLogbookExchange.ApplyImport(local, incoming));

        Assert.Equal(PortableLogbookImportError.UnresolvedConflicts, exception.Error);
    }

    [Fact]
    public void PlanImportClassifiesDuplicateOnlyReadyAndConflictStates()
    {
        var create = CreateOperation();
        var correction = CorrectOperation(create, "VH-XYZ", "rev_correct");
        var conflictingCorrection = CorrectOperation(create, "VH-CONFLICT", "rev_conflict");
        var local = Document(create.LogbookId, [create]);
        var duplicatePlan = PortableLogbookExchange.PlanImport(local, Document(create.LogbookId, [create]));
        var readyPlan = PortableLogbookExchange.PlanImport(local, Document(create.LogbookId, [create, correction]));
        var conflictPlan = PortableLogbookExchange.PlanImport(
            Document(create.LogbookId, [create, correction]),
            Document(create.LogbookId, [create, conflictingCorrection]));

        Assert.Equal(PortableLogbookImportPlanStatus.DuplicateOnly, duplicatePlan.Status);
        Assert.Equal(PortableLogbookImportPlanStatus.ReadyToApply, readyPlan.Status);
        Assert.Equal(PortableLogbookImportPlanStatus.RequiresConflictResolution, conflictPlan.Status);
    }

    [Fact]
    public void PlanImportRequiresCustomFieldResolutionForConflictingDefinitions()
    {
        var create = CreateOperation();
        var fieldId = new CustomFieldId("cf_training_kind");
        var local = PortableLogbookDocument.CreateAustraliaFirst(
            create.LogbookId,
            [new CustomFieldDefinition(fieldId, "Training kind", 1)],
            [create]);
        var incoming = PortableLogbookDocument.CreateAustraliaFirst(
            create.LogbookId,
            [new CustomFieldDefinition(fieldId, "Training category", 1)],
            [create]);

        var plan = PortableLogbookExchange.PlanImport(local, incoming);

        Assert.Equal(PortableLogbookImportPlanStatus.RequiresCustomFieldResolution, plan.Status);
        Assert.True(plan.Preview.CustomFieldDefinitions.HasConflicts);
    }

    private static PortableLogbookDocument Document(
        LogbookId logbookId,
        IReadOnlyList<PortableLogbookOperation> operations) =>
        PortableLogbookDocument.CreateAustraliaFirst(logbookId, [], operations);

    private static CreateEntryOperation CreateOperation() =>
        new(
            new LogbookId("log_test"),
            new EntryId("ent_1"),
            new RevisionId("rev_create"),
            new DeviceId("dev_excel"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            Entry("VH-ABC"));

    private static CorrectEntryOperation CorrectOperation(
        CreateEntryOperation create,
        string registration,
        string revisionId) =>
        new(
            create.LogbookId,
            create.EntryId,
            new RevisionId(revisionId),
            new HashSet<RevisionId> { create.RevisionId },
            create.DeviceId,
            create.CreatedAt.AddMinutes(1),
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
