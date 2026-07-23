namespace ElectronicLogbook.Portable;

public static class PortableLogbookWorkingCopy
{
    public static PortableLogbookWorkingCopyState FromProjection(
        PortableLogbookProjectionResult projection,
        DateTimeOffset reconciledAt)
    {
        ArgumentNullException.ThrowIfNull(projection);

        var pendingRevisionIds = projection.Operations
            .Select(operation => operation.RevisionId)
            .OrderBy(revisionId => revisionId.Value, StringComparer.Ordinal)
            .ToArray();

        return new PortableLogbookWorkingCopyState(
            pendingRevisionIds.Length > 0,
            pendingRevisionIds.Length,
            projection.CreateCount,
            projection.CorrectionCount,
            projection.DeletionCount,
            pendingRevisionIds,
            reconciledAt);
    }
}

public sealed record PortableLogbookWorkingCopyState(
    bool HasUnexportedChanges,
    int PendingOperationCount,
    int PendingCreateCount,
    int PendingCorrectionCount,
    int PendingDeletionCount,
    IReadOnlyList<RevisionId> PendingRevisionIds,
    DateTimeOffset ReconciledAt)
{
    public bool ExportRequired => HasUnexportedChanges;
}
