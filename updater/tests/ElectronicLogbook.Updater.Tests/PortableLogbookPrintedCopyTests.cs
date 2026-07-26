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
        Assert.Equal(document.LogbookId, plan.AuditSummary.LogbookId);
        Assert.Equal(new DateOnly(2019, 7, 18), plan.AuditSummary.Retention.RetainAfter);
        Assert.Equal(3, plan.RevisionHistory.Count);
        Assert.Equal("Alex Pilot", plan.CertificationBlock.HolderFullName);
    }

    [Fact]
    public void CreatePagePlanIncludesSevenYearRetentionSnapshotInAuditSummary()
    {
        var logbookId = new LogbookId("log_print");
        var oldOperation = CreateOperation(logbookId, "ent_old", "rev_old", DateTimeOffset.Parse("2018-07-17T00:00:00Z"));
        var recentOperation = CreateOperation(logbookId, "ent_recent", "rev_recent", DateTimeOffset.Parse("2026-07-18T00:00:00Z"));
        var document = PortableLogbookDocument.CreateAustraliaFirst(logbookId, [], [oldOperation, recentOperation]);
        var request = PortableLogbookPrintedCopy.CreateRequest(
            document,
            "Alex Pilot",
            new DateOnly(1990, 1, 2),
            new DateOnly(2026, 7, 18));

        var plan = PortableLogbookPrintedCopy.CreatePagePlan(request, recordsPerPage: 10);

        Assert.Equal(new DateOnly(2019, 7, 18), plan.AuditSummary.Retention.RetainAfter);
        Assert.Equal(2, plan.AuditSummary.Retention.TotalOperationCount);
        Assert.Equal(1, plan.AuditSummary.Retention.MinimumRetainedOperationCount);
        Assert.Equal(1, plan.AuditSummary.Retention.OlderThanMinimumRetentionCount);
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
            plan.RevisionHistory,
            history => history.EntryId == createDeleted.EntryId &&
                history.Revisions.Last().Kind == PortableOperationKind.Deletion);
        Assert.Contains(
            request.AuditSnapshot.RevisionHistory,
            history => history.EntryId == createDeleted.EntryId &&
                history.Revisions.Last().Kind == PortableOperationKind.Deletion);
    }

    [Fact]
    public void RenderHtmlIncludesAuditCertificationPageNumbersAndCurrentRecords()
    {
        var document = CreateDocumentWithRecords(3);
        var request = PortableLogbookPrintedCopy.CreateRequest(
            document,
            "Alex Pilot",
            new DateOnly(1990, 1, 2),
            new DateOnly(2026, 7, 18));
        var plan = PortableLogbookPrintedCopy.CreatePagePlan(request, recordsPerPage: 2);

        var html = PortableLogbookPrintedCopy.RenderHtml(plan);

        Assert.Contains("<!doctype html>", html, StringComparison.Ordinal);
        Assert.Contains("Page 1 of 2", html, StringComparison.Ordinal);
        Assert.Contains("Page 2 of 2", html, StringComparison.Ordinal);
        Assert.Contains("Audit summary", html, StringComparison.Ordinal);
        Assert.Contains("Logbook ID", html, StringComparison.Ordinal);
        Assert.Contains("log_print", html, StringComparison.Ordinal);
        Assert.Contains("Current records", html, StringComparison.Ordinal);
        Assert.Contains("Complete revision history", html, StringComparison.Ordinal);
        Assert.Contains("@page{size:A4;margin:0;}", html, StringComparison.Ordinal);
        Assert.Contains("thead{display:table-header-group;}", html, StringComparison.Ordinal);
        Assert.Contains("Revision history records", html, StringComparison.Ordinal);
        Assert.Contains("Minimum retained operations", html, StringComparison.Ordinal);
        Assert.Contains("2019-07-18", html, StringComparison.Ordinal);
        Assert.Contains("Alex Pilot", html, StringComparison.Ordinal);
        Assert.Contains("1990-01-02", html, StringComparison.Ordinal);
        Assert.Contains("current regulatory review are required", html, StringComparison.Ordinal);
        Assert.Contains("VH-AB1", html, StringComparison.Ordinal);
        Assert.DoesNotContain("ent_1", html, StringComparison.Ordinal);
        Assert.Contains("rev_1", html, StringComparison.Ordinal);
        Assert.Contains("Create", html, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderHtmlIncludesDeletedEntriesInCompleteRevisionHistory()
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

        var html = PortableLogbookPrintedCopy.RenderHtml(plan);

        Assert.DoesNotContain("VH-DEL</td><td>YSBK</td><td>YSBK", html, StringComparison.Ordinal);
        Assert.DoesNotContain("ent_deleted", html, StringComparison.Ordinal);
        Assert.Contains("rev_deleted_create", html, StringComparison.Ordinal);
        Assert.Contains("rev_deleted_tombstone", html, StringComparison.Ordinal);
        Assert.Contains("Deletion", html, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderHtmlIncludesUnresolvedConflictEntryAndHeadRevisions()
    {
        var create = CreateOperation("ent_conflict", "rev_create", "VH-OLD", DateTimeOffset.Parse("2026-07-18T00:00:00Z"));
        var localCorrection = new CorrectEntryOperation(
            create.LogbookId,
            create.EntryId,
            new RevisionId("rev_local_head"),
            new HashSet<RevisionId> { create.RevisionId },
            create.DeviceId,
            create.CreatedAt.AddMinutes(1),
            create.Entry with { Registration = "VH-LOC" });
        var incomingCorrection = new CorrectEntryOperation(
            create.LogbookId,
            create.EntryId,
            new RevisionId("rev_incoming_head"),
            new HashSet<RevisionId> { create.RevisionId },
            create.DeviceId,
            create.CreatedAt.AddMinutes(2),
            create.Entry with { Registration = "VH-INC" });
        var document = PortableLogbookDocument.CreateAustraliaFirst(
            create.LogbookId,
            [],
            [create, localCorrection, incomingCorrection]);
        var request = PortableLogbookPrintedCopy.CreateRequest(
            document,
            "Alex Pilot",
            new DateOnly(1990, 1, 2),
            new DateOnly(2026, 7, 18));
        var plan = PortableLogbookPrintedCopy.CreatePagePlan(request, recordsPerPage: 10);

        var html = PortableLogbookPrintedCopy.RenderHtml(plan);

        var conflict = Assert.Single(plan.Conflicts);
        Assert.Equal(create.EntryId, conflict.EntryId);
        Assert.Equal(1, plan.AuditSummary.ConflictCount);
        Assert.Contains("Unresolved conflict details", html, StringComparison.Ordinal);
        Assert.DoesNotContain("ent_conflict", html, StringComparison.Ordinal);
        Assert.Contains("rev_incoming_head, rev_local_head", html, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderHtmlIncludesFullCurrentRecordPayloadAndCustomFieldLabels()
    {
        var roleField = new CustomFieldDefinition(new CustomFieldId("cf_role"), "Role", 1);
        var missionField = new CustomFieldDefinition(new CustomFieldId("cf_mission"), "Mission", 2);
        var create = new CreateEntryOperation(
            new LogbookId("log_print"),
            new EntryId("ent_full"),
            new RevisionId("rev_full"),
            new DeviceId("dev_excel"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            PortableLogbookEntry.Empty with
            {
                Date = new DateOnly(2026, 7, 18),
                AircraftType = "C172",
                Registration = "VH-ABC",
                FlightNumber = "EL123",
                From = "YSBK",
                To = "YSCN",
                Route = "BK CN",
                Details = "Training detail",
                MultiPilot = 0.1m,
                PilotInCommand = 1.2m,
                CoPilot = 0.3m,
                Dual = 0.4m,
                Instructor = 0.5m,
                Day = 1.0m,
                Night = 0.2m,
                InstrumentActual = 0.1m,
                InstrumentSimulated = 0.2m,
                TakeoffsDay = 1,
                TakeoffsNight = 2,
                LandingsDay = 3,
                LandingsNight = 4,
                IfrApproaches = 5,
                Holding = 6,
                Rnav = 7,
                Circling = 8,
                CustomFields = new Dictionary<CustomFieldId, string?>
                {
                    [roleField.Id] = "PICUS",
                    [missionField.Id] = "Check"
                }
            });
        var document = PortableLogbookDocument.CreateAustraliaFirst(
            create.LogbookId,
            [roleField, missionField],
            [create]);
        var request = PortableLogbookPrintedCopy.CreateRequest(
            document,
            "Alex Pilot",
            new DateOnly(1990, 1, 2),
            new DateOnly(2026, 7, 18));
        var plan = PortableLogbookPrintedCopy.CreatePagePlan(request, recordsPerPage: 10);

        var html = PortableLogbookPrintedCopy.RenderHtml(plan);

        Assert.Equal([roleField, missionField], plan.CustomFieldDefinitions);
        foreach (var expected in new[]
        {
            "Flight number",
            "Route",
            "Remarks",
            "Multi-pilot",
            "Co-pilot",
            "Dual",
            "Instructor",
            "Instrument actual",
            "Instrument sim",
            "Takeoffs day",
            "Takeoffs night",
            "Landings day",
            "Landings night",
            "IFR approaches",
            "Holding",
            "RNP",
            "Circling",
            "Role",
            "Mission",
            "EL123",
            "BK CN",
            "Training detail",
            "0.1",
            "1.2",
            "0.3",
            "0.4",
            "0.5",
            "1.0",
            "0.2",
            "5",
            "6",
            "7",
            "8",
            "PICUS",
            "Check"
        })
        {
            Assert.Contains(expected, html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RenderHtmlEscapesHolderAndEntryText()
    {
        var create = CreateOperation(
            "ent_unsafe",
            "rev_unsafe",
            "VH-&1",
            DateTimeOffset.Parse("2026-07-18T00:00:00Z")) with
        {
            Entry = PortableLogbookEntry.Empty with
            {
                Date = new DateOnly(2026, 7, 18),
                AircraftType = "C<172>",
                Registration = "VH-&1",
                From = "YS<BK",
                To = "YWOL>",
                PilotInCommand = 1.2m
            }
        };
        var document = PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [create]);
        var request = PortableLogbookPrintedCopy.CreateRequest(
            document,
            "Alex <Pilot>",
            new DateOnly(1990, 1, 2),
            new DateOnly(2026, 7, 18));
        var plan = PortableLogbookPrintedCopy.CreatePagePlan(request, recordsPerPage: 10);

        var html = PortableLogbookPrintedCopy.RenderHtml(plan);

        Assert.Contains("Alex &lt;Pilot&gt;", html, StringComparison.Ordinal);
        Assert.Contains("C&lt;172&gt;", html, StringComparison.Ordinal);
        Assert.Contains("VH-&amp;1", html, StringComparison.Ordinal);
        Assert.Contains("YS&lt;BK", html, StringComparison.Ordinal);
        Assert.Contains("YWOL&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Alex <Pilot>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("C<172>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderHtmlEscapesCustomFieldLabelsAndValues()
    {
        var field = new CustomFieldDefinition(new CustomFieldId("cf_unsafe"), "Role <script>", 1);
        var create = CreateOperation(
            "ent_custom_unsafe",
            "rev_custom_unsafe",
            "VH-ABC",
            DateTimeOffset.Parse("2026-07-18T00:00:00Z")) with
        {
            Entry = PortableLogbookEntry.Empty with
            {
                Date = new DateOnly(2026, 7, 18),
                AircraftType = "C172",
                Registration = "VH-ABC",
                From = "YSBK",
                To = "YSBK",
                PilotInCommand = 1.2m,
                CustomFields = new Dictionary<CustomFieldId, string?>
                {
                    [field.Id] = "PIC <unsafe>"
                }
            }
        };
        var document = PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [field], [create]);
        var request = PortableLogbookPrintedCopy.CreateRequest(
            document,
            "Alex Pilot",
            new DateOnly(1990, 1, 2),
            new DateOnly(2026, 7, 18));
        var plan = PortableLogbookPrintedCopy.CreatePagePlan(request, recordsPerPage: 10);

        var html = PortableLogbookPrintedCopy.RenderHtml(plan);

        Assert.Contains("Role &lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("PIC &lt;unsafe&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Role <script>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("PIC <unsafe>", html, StringComparison.Ordinal);
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
                    Date = new DateOnly(2026, 7, 18).AddDays(1 - index),
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
        CreateOperation(
            new LogbookId("log_print"),
            entryId,
            revisionId,
            createdAt,
            registration);

    private static CreateEntryOperation CreateOperation(
        LogbookId logbookId,
        string entryId,
        string revisionId,
        DateTimeOffset createdAt,
        string registration = "VH-ABC") =>
        new(
            logbookId,
            new EntryId(entryId),
            new RevisionId(revisionId),
            new DeviceId("dev_excel"),
            createdAt,
            PortableLogbookEntry.Empty with
            {
                Date = DateOnly.FromDateTime(createdAt.UtcDateTime),
                AircraftType = "C172",
                Registration = registration,
                From = "YSBK",
                To = "YSBK",
                PilotInCommand = 1.2m
            });
}
