using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookAuditTests
{
    [Fact]
    public void CreateSeparatesCurrentRecordsFromCompleteRevisionHistory()
    {
        var createA = CreateOperation("ent_a", "rev_a_create", "VH-AAA", DateTimeOffset.Parse("2026-07-18T00:00:00Z"));
        var correctionA = new CorrectEntryOperation(
            createA.LogbookId,
            createA.EntryId,
            new RevisionId("rev_a_correct"),
            new HashSet<RevisionId> { createA.RevisionId },
            createA.DeviceId,
            createA.CreatedAt.AddMinutes(1),
            Entry("VH-AAB"));
        var createB = CreateOperation("ent_b", "rev_b_create", "VH-BBB", DateTimeOffset.Parse("2026-07-18T00:02:00Z"));
        var deleteB = new DeleteEntryOperation(
            createB.LogbookId,
            createB.EntryId,
            new RevisionId("rev_b_delete"),
            new HashSet<RevisionId> { createB.RevisionId },
            createB.DeviceId,
            createB.CreatedAt.AddMinutes(1));
        var document = PortableLogbookDocument.CreateAustraliaFirst(createA.LogbookId, [], [createB, createA, deleteB, correctionA]);

        var audit = PortableLogbookAudit.Create(document);

        var current = Assert.Single(audit.CurrentRecords);
        Assert.Equal(createA.EntryId, current.EntryId);
        Assert.Equal(correctionA.RevisionId, current.CurrentRevisionId);
        Assert.Equal("VH-AAB", current.Entry.Registration);
        Assert.Equal(2, audit.RevisionHistory.Count);
        Assert.Contains(audit.RevisionHistory, history => history.EntryId == createB.EntryId && history.Revisions.Any(revision => revision.Kind == PortableOperationKind.Deletion));
    }

    [Fact]
    public void CreatePreservesVerifiedParentRevisionLinksForAuditReview()
    {
        var create = CreateOperation("ent_a", "rev_create", "VH-AAA", DateTimeOffset.Parse("2026-07-18T00:00:00Z"));
        var correction = new CorrectEntryOperation(
            create.LogbookId,
            create.EntryId,
            new RevisionId("rev_correct"),
            new HashSet<RevisionId> { create.RevisionId },
            create.DeviceId,
            create.CreatedAt.AddMinutes(1),
            Entry("VH-AAB"));
        var document = PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [correction, create]);

        var audit = PortableLogbookAudit.Create(document);

        var history = Assert.Single(audit.RevisionHistory);
        var auditedCorrection = Assert.Single(history.Revisions, revision => revision.RevisionId == correction.RevisionId);
        Assert.Equal([create.RevisionId], auditedCorrection.ParentRevisionIds);
        Assert.Equal([create.RevisionId], auditedCorrection.VerifiedParentRevisionIds);
    }

    [Fact]
    public void CreatePreservesCustomFieldDefinitionsForAuditOutput()
    {
        var field = new CustomFieldDefinition(new CustomFieldId("cf_role"), "Role", 1);
        var create = CreateOperation("ent_a", "rev_create", "VH-AAA", DateTimeOffset.Parse("2026-07-18T00:00:00Z"));
        var document = PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [field], [create]);

        var audit = PortableLogbookAudit.Create(document);

        Assert.Equal([field], audit.CustomFieldDefinitions);
    }

    [Fact]
    public void CreateRejectsInvalidDocuments()
    {
        var create = CreateOperation("ent_a", "rev_create", "VH-AAA", DateTimeOffset.Parse("2026-07-18T00:00:00Z"));
        var invalid = PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [create with { LogbookId = new LogbookId("log_other") }]);

        var exception = Assert.Throws<ArgumentException>(() => PortableLogbookAudit.Create(invalid));

        Assert.Equal("document", exception.ParamName);
    }

    private static CreateEntryOperation CreateOperation(
        string entryId,
        string revisionId,
        string registration,
        DateTimeOffset createdAt) =>
        new(
            new LogbookId("log_audit"),
            new EntryId(entryId),
            new RevisionId(revisionId),
            new DeviceId("dev_excel"),
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
