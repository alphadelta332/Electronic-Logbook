using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookPrintedCopyTests
{
    [Fact]
    public void CreateRequestCarriesHolderIdentityOnlyInEphemeralRequest()
    {
        var document = CreateDocument();

        var request = PortableLogbookPrintedCopy.CreateRequest(
            document,
            "  Alex Pilot  ",
            new DateOnly(1990, 1, 2),
            new DateOnly(2026, 7, 18));

        Assert.Equal("Alex Pilot", request.HolderFullName);
        Assert.Equal(new DateOnly(1990, 1, 2), request.HolderDateOfBirth);
        Assert.Equal(new DateOnly(2026, 7, 18), request.CertifiedOn);
        Assert.Contains("Australia-first", request.ComplianceNotice, StringComparison.Ordinal);
        Assert.Single(request.AuditSnapshot.CurrentRecords);
    }

    [Fact]
    public void PortableDocumentSerializationDoesNotPersistPrintedCopyIdentity()
    {
        var document = CreateDocument();
        _ = PortableLogbookPrintedCopy.CreateRequest(
            document,
            "Alex Pilot",
            new DateOnly(1990, 1, 2),
            new DateOnly(2026, 7, 18));

        var json = PortableLogbookJson.Serialize(document);

        Assert.DoesNotContain("Alex Pilot", json, StringComparison.Ordinal);
        Assert.DoesNotContain("1990", json, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateRequestRejectsBlankHolderName()
    {
        var exception = Assert.Throws<ArgumentException>(() => PortableLogbookPrintedCopy.CreateRequest(
            CreateDocument(),
            " ",
            new DateOnly(1990, 1, 2),
            new DateOnly(2026, 7, 18)));

        Assert.Equal("holderFullName", exception.ParamName);
    }

    [Fact]
    public void CreatePagePlanAssignsStablePageNumbersAndAuditSummary()
    {
        var document = CreateDocumentWithRecords(3);
        var request = PortableLogbookPrintedCopy.CreateRequest(
            document,
            "Alex Pilot",
            new DateOnly(1990, 1, 2),
            new DateOnly(2026, 7, 18));

        var plan = PortableLogbookPrintedCopy.CreatePagePlan(request, recordsPerPage: 2);

        Assert.Equal(2, plan.Pages.Count);
        Assert.Equal([(1, 2), (2, 2)], plan.Pages.Select(page => (page.PageNumber, page.TotalPages)));
        Assert.Equal(2, plan.Pages[0].Records.Count);
        Assert.Single(plan.Pages[1].Records);
        Assert.Equal(3, plan.AuditSummary.CurrentRecordCount);
        Assert.Equal(3, plan.AuditSummary.RevisionCount);
        Assert.Equal("Alex Pilot", plan.CertificationBlock.HolderFullName);
    }

    [Fact]
    public void CreatePagePlanStillCreatesCertificationPageForEmptyLogbook()
    {
        var request = PortableLogbookPrintedCopy.CreateRequest(
            PortableLogbookDocument.CreateAustraliaFirst(new LogbookId("log_empty"), [], []),
            "Alex Pilot",
            new DateOnly(1990, 1, 2),
            new DateOnly(2026, 7, 18));

        var plan = PortableLogbookPrintedCopy.CreatePagePlan(request, recordsPerPage: 10);

        var page = Assert.Single(plan.Pages);
        Assert.Equal(1, page.PageNumber);
        Assert.Equal(1, page.TotalPages);
        Assert.Empty(page.Records);
        Assert.Equal(0, plan.AuditSummary.CurrentRecordCount);
    }

    [Fact]
    public void CreatePagePlanRejectsInvalidPageSize()
    {
        var request = PortableLogbookPrintedCopy.CreateRequest(
            CreateDocument(),
            "Alex Pilot",
            new DateOnly(1990, 1, 2),
            new DateOnly(2026, 7, 18));

        Assert.Throws<ArgumentOutOfRangeException>(() => PortableLogbookPrintedCopy.CreatePagePlan(request, recordsPerPage: 0));
    }

    [Fact]
    public void CreatePagePlanExcludesDeletedRecordsButPreservesTheirAuditHistory()
    {
        var createCurrent = CreateOperation("ent_current", "rev_current", "VH-CUR", DateTimeOffset.Parse("2026-07-18T00:00:00Z"));
        var createDeleted = CreateOperation("ent_deleted", "rev_deleted_create", "VH-DEL", DateTimeOffset.Parse("2026-07-18T00:01:00Z"));
        var delete = new DeleteEntryOperation(
            createDeleted.LogbookId,
            createDeleted.EntryId,
            new RevisionId("rev_deleted_tombstone"),
            new HashSet<RevisionId> { createDeleted.RevisionId },
            createDeleted.DeviceId,
            createDeleted.CreatedAt.AddMinutes(1));
        var document = PortableLogbookDocument.CreateAustraliaFirst(
            createCurrent.LogbookId,
            [],
            [createCurrent, createDeleted, delete]);
        var request = PortableLogbookPrintedCopy.CreateRequest(
            document,
            "Alex Pilot",
            new DateOnly(1990, 1, 2),
            new DateOnly(2026, 7, 18));

        var plan = PortableLogbookPrintedCopy.CreatePagePlan(request, recordsPerPage: 10);

        var page = Assert.Single(plan.Pages);
        var currentRecord = Assert.Single(page.Records);
        Assert.Equal(createCurrent.EntryId, currentRecord.EntryId);
        Assert.Equal(1, plan.AuditSummary.CurrentRecordCount);
        Assert.Equal(3, plan.AuditSummary.RevisionCount);
        Assert.Contains(
            request.AuditSnapshot.RevisionHistory,
            history => history.EntryId == createDeleted.EntryId &&
                history.Revisions.Last().Kind == PortableOperationKind.Deletion);
    }

    private static PortableLogbookDocument CreateDocument()
    {
        var create = new CreateEntryOperation(
            new LogbookId("log_print"),
            new EntryId("ent_1"),
            new RevisionId("rev_1"),
            new DeviceId("dev_excel"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            PortableLogbookEntry.Empty with
            {
                Date = new DateOnly(2026, 7, 18),
                AircraftType = "C172",
                Registration = "VH-ABC",
                From = "YSBK",
                To = "YSBK",
                PilotInCommand = 1.2m
            });

        return PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [create]);
    }

    private static PortableLogbookDocument CreateDocumentWithRecords(int count)
    {
        var logbookId = new LogbookId("log_print");
        var operations = Enumerable.Range(1, count)
            .Select(index => new CreateEntryOperation(
                logbookId,
                new EntryId($"ent_{index}"),
                new RevisionId($"rev_{index}"),
                new DeviceId("dev_excel"),
                DateTimeOffset.Parse("2026-07-18T00:00:00Z").AddMinutes(index),
                PortableLogbookEntry.Empty with
                {
                    Date = new DateOnly(2026, 7, 18).AddDays(index),
                    AircraftType = "C172",
                    Registration = $"VH-AB{index}",
                    From = "YSBK",
                    To = "YSBK",
                    PilotInCommand = 1.2m
                }))
            .ToArray();

        return PortableLogbookDocument.CreateAustraliaFirst(logbookId, [], operations);
    }

    private static CreateEntryOperation CreateOperation(
        string entryId,
        string revisionId,
        string registration,
        DateTimeOffset createdAt) =>
        new(
            new LogbookId("log_print"),
            new EntryId(entryId),
            new RevisionId(revisionId),
            new DeviceId("dev_excel"),
            createdAt,
            PortableLogbookEntry.Empty with
            {
                Date = new DateOnly(2026, 7, 18),
                AircraftType = "C172",
                Registration = registration,
                From = "YSBK",
                To = "YSBK",
                PilotInCommand = 1.2m
            });
}
