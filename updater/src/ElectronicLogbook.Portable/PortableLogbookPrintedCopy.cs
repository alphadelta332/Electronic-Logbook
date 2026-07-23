namespace ElectronicLogbook.Portable;

using System.Globalization;
using System.Net;
using System.Text;

public static class PortableLogbookPrintedCopy
{
    public static PortableLogbookPrintedCopyRequest CreateRequest(
        PortableLogbookDocument document,
        string holderFullName,
        DateOnly holderDateOfBirth,
        DateOnly certifiedOn)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(holderFullName);

        var audit = PortableLogbookAudit.Create(document);
        var retention = PortableLogbookRetention.Evaluate(document, certifiedOn);
        return new PortableLogbookPrintedCopyRequest(
            audit,
            retention,
            holderFullName.Trim(),
            holderDateOfBirth,
            certifiedOn,
            "Australia-first portable logbook audit output. User certification and current regulatory review are required before use as an official record.");
    }

    public static PortableLogbookPrintedCopyPagePlan CreatePagePlan(
        PortableLogbookPrintedCopyRequest request,
        int recordsPerPage)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (recordsPerPage < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(recordsPerPage), "Records per page must be at least 1.");
        }

        var pages = request.AuditSnapshot.CurrentRecords
            .Chunk(recordsPerPage)
            .Select((records, index) => new PortableLogbookPrintedCopyPage(
                index + 1,
                0,
                records))
            .ToArray();
        var totalPages = Math.Max(1, pages.Length);
        if (pages.Length == 0)
        {
            pages = [new PortableLogbookPrintedCopyPage(1, totalPages, [])];
        }
        else
        {
            pages = pages.Select(page => page with { TotalPages = totalPages }).ToArray();
        }

        return new PortableLogbookPrintedCopyPagePlan(
            pages,
            new PortableLogbookPrintedCopyAuditSummary(
                request.AuditSnapshot.LogbookId,
                request.AuditSnapshot.CurrentRecords.Count,
                request.AuditSnapshot.RevisionHistory.Sum(history => history.Revisions.Count),
                request.AuditSnapshot.Conflicts.Count,
                request.RetentionSnapshot),
            request.AuditSnapshot.CustomFieldDefinitions,
            request.AuditSnapshot.RevisionHistory,
            request.AuditSnapshot.Conflicts,
            new PortableLogbookPrintedCopyCertificationBlock(
                request.HolderFullName,
                request.HolderDateOfBirth,
                request.CertifiedOn,
                request.ComplianceNotice));
    }

    public static string RenderHtml(PortableLogbookPrintedCopyPagePlan pagePlan)
    {
        ArgumentNullException.ThrowIfNull(pagePlan);

        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("<meta charset=\"utf-8\">");
        builder.AppendLine("<title>Certified Electronic Logbook Printed Copy</title>");
        builder.AppendLine("<style>");
        builder.AppendLine("body{font-family:Arial,sans-serif;color:#111;margin:0;}");
        builder.AppendLine(".page{box-sizing:border-box;min-height:270mm;padding:14mm 12mm;page-break-after:always;}");
        builder.AppendLine(".page:last-child{page-break-after:auto;}");
        builder.AppendLine("@page{size:A4;margin:0;}");
        builder.AppendLine("tr{page-break-inside:avoid;}");
        builder.AppendLine("thead{display:table-header-group;}");
        builder.AppendLine("h1{font-size:20px;margin:0 0 8px;}");
        builder.AppendLine("h2{font-size:15px;margin:16px 0 6px;}");
        builder.AppendLine("p{margin:4px 0;}");
        builder.AppendLine("table{border-collapse:collapse;width:100%;font-size:10px;}");
        builder.AppendLine("th,td{border:1px solid #444;padding:4px;text-align:left;vertical-align:top;}");
        builder.AppendLine("th{background:#eee;}");
        builder.AppendLine(".summary{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:4px 18px;font-size:11px;margin:10px 0;}");
        builder.AppendLine(".footer{margin-top:12px;font-size:10px;color:#333;}");
        builder.AppendLine("</style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");

        foreach (var page in pagePlan.Pages)
        {
            builder.AppendLine("<section class=\"page\">");
            builder.AppendLine("<h1>Certified Electronic Logbook Printed Copy</h1>");
            builder.Append("<p>Page ");
            builder.Append(page.PageNumber.ToString(CultureInfo.InvariantCulture));
            builder.Append(" of ");
            builder.Append(page.TotalPages.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("</p>");

            if (page.PageNumber == 1)
            {
                AppendAuditSummary(builder, pagePlan.AuditSummary);
                AppendCertificationBlock(builder, pagePlan.CertificationBlock);
                AppendRevisionHistory(builder, pagePlan.RevisionHistory);
                AppendConflicts(builder, pagePlan.Conflicts);
            }

            AppendRecords(builder, page.Records, pagePlan.CustomFieldDefinitions);
            builder.Append("<p class=\"footer\">Generated from immutable portable-logbook audit data. Current records exclude deleted entries; revision-history counts include corrections and tombstones.</p>");
            builder.AppendLine("</section>");
        }

        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    private static void AppendAuditSummary(StringBuilder builder, PortableLogbookPrintedCopyAuditSummary summary)
    {
        builder.AppendLine("<h2>Audit summary</h2>");
        builder.AppendLine("<dl class=\"summary\">");
        AppendSummaryItem(builder, "Logbook ID", summary.LogbookId.Value);
        AppendSummaryItem(builder, "Current records", summary.CurrentRecordCount);
        AppendSummaryItem(builder, "Revision history records", summary.RevisionCount);
        AppendSummaryItem(builder, "Unresolved conflicts", summary.ConflictCount);
        AppendSummaryItem(builder, "Total operations", summary.Retention.TotalOperationCount);
        AppendSummaryItem(builder, "Minimum retained operations", summary.Retention.MinimumRetainedOperationCount);
        AppendSummaryItem(builder, "Operations older than retention floor", summary.Retention.OlderThanMinimumRetentionCount);
        AppendSummaryItem(builder, "Retain after", summary.Retention.RetainAfter);
        builder.AppendLine("</dl>");
    }

    private static void AppendCertificationBlock(StringBuilder builder, PortableLogbookPrintedCopyCertificationBlock certification)
    {
        builder.AppendLine("<h2>Certification</h2>");
        builder.Append("<p><strong>Holder:</strong> ");
        builder.Append(Escape(certification.HolderFullName));
        builder.AppendLine("</p>");
        builder.Append("<p><strong>Date of birth:</strong> ");
        builder.Append(FormatDate(certification.HolderDateOfBirth));
        builder.AppendLine("</p>");
        builder.Append("<p><strong>Certified on:</strong> ");
        builder.Append(FormatDate(certification.CertifiedOn));
        builder.AppendLine("</p>");
        builder.Append("<p>");
        builder.Append(Escape(certification.ComplianceNotice));
        builder.AppendLine("</p>");
    }

    private static void AppendRecords(
        StringBuilder builder,
        IReadOnlyList<PortableLogbookCurrentRecord> records,
        IReadOnlyList<CustomFieldDefinition> customFieldDefinitions)
    {
        builder.AppendLine("<h2>Current records</h2>");
        if (records.Count == 0)
        {
            builder.AppendLine("<p>No current records.</p>");
            return;
        }

        var orderedCustomFields = customFieldDefinitions
            .OrderBy(field => field.Order)
            .ThenBy(field => field.Id.Value, StringComparer.Ordinal)
            .ToArray();

        builder.AppendLine("<table>");
        builder.Append("<thead><tr>");
        foreach (var header in CurrentRecordHeaders().Concat(orderedCustomFields.Select(field => field.Label)))
        {
            AppendHeader(builder, header);
        }
        builder.AppendLine("</tr></thead>");
        builder.AppendLine("<tbody>");
        foreach (var record in records)
        {
            var entry = record.Entry;
            builder.Append("<tr>");
            AppendCell(builder, FormatDate(entry.Date));
            AppendCell(builder, entry.AircraftType);
            AppendCell(builder, entry.Registration);
            AppendCell(builder, entry.FlightNumber);
            AppendCell(builder, entry.From);
            AppendCell(builder, entry.To);
            AppendCell(builder, entry.Route);
            AppendCell(builder, entry.Details);
            AppendCell(builder, FormatDecimal(entry.MultiPilot));
            AppendCell(builder, FormatDecimal(entry.PilotInCommand));
            AppendCell(builder, FormatDecimal(entry.CoPilot));
            AppendCell(builder, FormatDecimal(entry.Dual));
            AppendCell(builder, FormatDecimal(entry.Instructor));
            AppendCell(builder, FormatDecimal(entry.Day));
            AppendCell(builder, FormatDecimal(entry.Night));
            AppendCell(builder, FormatDecimal(entry.InstrumentActual));
            AppendCell(builder, FormatDecimal(entry.InstrumentSimulated));
            AppendCell(builder, FormatInt(entry.TakeoffsDay));
            AppendCell(builder, FormatInt(entry.TakeoffsNight));
            AppendCell(builder, FormatInt(entry.LandingsDay));
            AppendCell(builder, FormatInt(entry.LandingsNight));
            AppendCell(builder, FormatInt(entry.IfrApproaches));
            AppendCell(builder, FormatInt(entry.Holding));
            AppendCell(builder, FormatInt(entry.Rnav));
            AppendCell(builder, FormatInt(entry.Circling));
            AppendCell(builder, record.EntryId.Value);
            AppendCell(builder, record.CurrentRevisionId.Value);
            foreach (var field in orderedCustomFields)
            {
                entry.CustomFields.TryGetValue(field.Id, out var value);
                AppendCell(builder, value);
            }

            builder.AppendLine("</tr>");
        }

        builder.AppendLine("</tbody>");
        builder.AppendLine("</table>");
    }

    private static IEnumerable<string> CurrentRecordHeaders()
    {
        yield return "Date";
        yield return "Aircraft";
        yield return "Registration";
        yield return "Flight number";
        yield return "From";
        yield return "To";
        yield return "Route";
        yield return "Remarks";
        yield return "Multi-pilot";
        yield return "PIC";
        yield return "Co-pilot";
        yield return "Dual";
        yield return "Instructor";
        yield return "Day";
        yield return "Night";
        yield return "Instrument actual";
        yield return "Instrument sim";
        yield return "Takeoffs day";
        yield return "Takeoffs night";
        yield return "Landings day";
        yield return "Landings night";
        yield return "IFR approaches";
        yield return "Holding";
        yield return "RNP";
        yield return "Circling";
        yield return "Entry ID";
        yield return "Revision ID";
    }

    private static void AppendRevisionHistory(StringBuilder builder, IReadOnlyList<PortableLogbookEntryRevisionHistory> histories)
    {
        builder.AppendLine("<h2>Complete revision history</h2>");
        if (histories.Count == 0)
        {
            builder.AppendLine("<p>No revision history.</p>");
            return;
        }

        builder.AppendLine("<table>");
        builder.AppendLine("<thead><tr><th>Entry ID</th><th>Revision ID</th><th>Kind</th><th>Created</th><th>Device ID</th><th>Verified parents</th></tr></thead>");
        builder.AppendLine("<tbody>");
        foreach (var history in histories)
        {
            foreach (var revision in history.Revisions)
            {
                builder.Append("<tr><td>");
                builder.Append(Escape(history.EntryId.Value));
                builder.Append("</td><td>");
                builder.Append(Escape(revision.RevisionId.Value));
                builder.Append("</td><td>");
                builder.Append(Escape(revision.Kind.ToString()));
                builder.Append("</td><td>");
                builder.Append(Escape(revision.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)));
                builder.Append("</td><td>");
                builder.Append(Escape(revision.DeviceId.Value));
                builder.Append("</td><td>");
                builder.Append(Escape(string.Join(", ", revision.VerifiedParentRevisionIds.Select(parent => parent.Value))));
                builder.AppendLine("</td></tr>");
            }
        }

        builder.AppendLine("</tbody>");
        builder.AppendLine("</table>");
    }

    private static void AppendConflicts(StringBuilder builder, IReadOnlyList<PortableLogbookConflict> conflicts)
    {
        builder.AppendLine("<h2>Unresolved conflict details</h2>");
        if (conflicts.Count == 0)
        {
            builder.AppendLine("<p>No unresolved conflicts.</p>");
            return;
        }

        builder.AppendLine("<table>");
        builder.AppendLine("<thead><tr><th>Entry ID</th><th>Head revision IDs</th></tr></thead>");
        builder.AppendLine("<tbody>");
        foreach (var conflict in conflicts)
        {
            builder.Append("<tr><td>");
            builder.Append(Escape(conflict.EntryId.Value));
            builder.Append("</td><td>");
            builder.Append(Escape(string.Join(", ", conflict.HeadRevisionIds.Select(revision => revision.Value))));
            builder.AppendLine("</td></tr>");
        }

        builder.AppendLine("</tbody>");
        builder.AppendLine("</table>");
    }

    private static void AppendSummaryItem(StringBuilder builder, string label, int value) =>
        AppendSummaryItem(builder, label, value.ToString(CultureInfo.InvariantCulture));

    private static void AppendSummaryItem(StringBuilder builder, string label, DateOnly value) =>
        AppendSummaryItem(builder, label, FormatDate(value));

    private static void AppendSummaryItem(StringBuilder builder, string label, string value)
    {
        builder.Append("<div><dt>");
        builder.Append(Escape(label));
        builder.Append("</dt><dd>");
        builder.Append(Escape(value));
        builder.AppendLine("</dd></div>");
    }

    private static void AppendHeader(StringBuilder builder, string value)
    {
        builder.Append("<th>");
        builder.Append(Escape(value));
        builder.Append("</th>");
    }

    private static void AppendCell(StringBuilder builder, string? value)
    {
        builder.Append("<td>");
        builder.Append(Escape(value));
        builder.Append("</td>");
    }

    private static string FormatDate(DateOnly? value) => value is null
        ? string.Empty
        : FormatDate(value.Value);

    private static string FormatDate(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatDecimal(decimal? value) => value?.ToString("0.0#", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string FormatInt(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Escape(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

public sealed record PortableLogbookPrintedCopyRequest(
    PortableLogbookAuditSnapshot AuditSnapshot,
    PortableLogbookRetentionSnapshot RetentionSnapshot,
    string HolderFullName,
    DateOnly HolderDateOfBirth,
    DateOnly CertifiedOn,
    string ComplianceNotice);

public sealed record PortableLogbookPrintedCopyPagePlan(
    IReadOnlyList<PortableLogbookPrintedCopyPage> Pages,
    PortableLogbookPrintedCopyAuditSummary AuditSummary,
    IReadOnlyList<CustomFieldDefinition> CustomFieldDefinitions,
    IReadOnlyList<PortableLogbookEntryRevisionHistory> RevisionHistory,
    IReadOnlyList<PortableLogbookConflict> Conflicts,
    PortableLogbookPrintedCopyCertificationBlock CertificationBlock);

public sealed record PortableLogbookPrintedCopyPage(
    int PageNumber,
    int TotalPages,
    IReadOnlyList<PortableLogbookCurrentRecord> Records);

public sealed record PortableLogbookPrintedCopyAuditSummary(
    LogbookId LogbookId,
    int CurrentRecordCount,
    int RevisionCount,
    int ConflictCount,
    PortableLogbookRetentionSnapshot Retention);

public sealed record PortableLogbookPrintedCopyCertificationBlock(
    string HolderFullName,
    DateOnly HolderDateOfBirth,
    DateOnly CertifiedOn,
    string ComplianceNotice);
