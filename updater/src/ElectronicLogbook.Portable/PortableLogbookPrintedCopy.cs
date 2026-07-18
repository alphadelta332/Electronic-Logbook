namespace ElectronicLogbook.Portable;

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
        return new PortableLogbookPrintedCopyRequest(
            audit,
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
                request.AuditSnapshot.CurrentRecords.Count,
                request.AuditSnapshot.RevisionHistory.Sum(history => history.Revisions.Count),
                request.AuditSnapshot.Conflicts.Count),
            new PortableLogbookPrintedCopyCertificationBlock(
                request.HolderFullName,
                request.HolderDateOfBirth,
                request.CertifiedOn,
                request.ComplianceNotice));
    }
}

public sealed record PortableLogbookPrintedCopyRequest(
    PortableLogbookAuditSnapshot AuditSnapshot,
    string HolderFullName,
    DateOnly HolderDateOfBirth,
    DateOnly CertifiedOn,
    string ComplianceNotice);

public sealed record PortableLogbookPrintedCopyPagePlan(
    IReadOnlyList<PortableLogbookPrintedCopyPage> Pages,
    PortableLogbookPrintedCopyAuditSummary AuditSummary,
    PortableLogbookPrintedCopyCertificationBlock CertificationBlock);

public sealed record PortableLogbookPrintedCopyPage(
    int PageNumber,
    int TotalPages,
    IReadOnlyList<PortableLogbookCurrentRecord> Records);

public sealed record PortableLogbookPrintedCopyAuditSummary(
    int CurrentRecordCount,
    int RevisionCount,
    int ConflictCount);

public sealed record PortableLogbookPrintedCopyCertificationBlock(
    string HolderFullName,
    DateOnly HolderDateOfBirth,
    DateOnly CertifiedOn,
    string ComplianceNotice);
