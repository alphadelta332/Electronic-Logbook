namespace ElectronicLogbook.Portable;

public static class PortableLogbookAudit
{
    public static PortableLogbookAuditSnapshot Create(PortableLogbookDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var validation = PortableLogbookValidator.Validate(document);
        if (!validation.IsValid)
        {
            throw new ArgumentException("Portable logbook document is invalid.", nameof(document));
        }

        var merge = PortableLogbookMerger.Merge(document.Operations);
        var operationsByRevision = document.Operations.ToDictionary(operation => operation.RevisionId);
        var currentRecords = merge.Entries.Values
            .Where(entry => !entry.IsDeleted && entry.Entry is not null)
            .OrderBy(entry => entry.Entry!.Date)
            .ThenBy(entry => entry.EntryId.Value, StringComparer.Ordinal)
            .Select(entry => new PortableLogbookCurrentRecord(entry.EntryId, entry.CurrentRevisionId, entry.Entry!))
            .ToArray();
        var revisionHistory = document.Operations
            .GroupBy(operation => operation.EntryId)
            .OrderBy(group => group.Key.Value, StringComparer.Ordinal)
            .Select(group => new PortableLogbookEntryRevisionHistory(
                group.Key,
                group.OrderBy(operation => operation.CreatedAt)
                    .ThenBy(operation => operation.RevisionId.Value, StringComparer.Ordinal)
                    .Select(operation => ToAuditRevision(operation, operationsByRevision))
                    .ToArray()))
            .ToArray();

        return new PortableLogbookAuditSnapshot(
            document.LogbookId,
            document.SchemaVersion,
            document.CustomFieldDefinitions,
            currentRecords,
            revisionHistory,
            merge.Conflicts);
    }

    private static PortableLogbookAuditRevision ToAuditRevision(
        PortableLogbookOperation operation,
        IReadOnlyDictionary<RevisionId, PortableLogbookOperation> operationsByRevision) =>
        new(
            operation.RevisionId,
            operation.Kind,
            operation.ParentRevisionIds.OrderBy(parent => parent.Value, StringComparer.Ordinal).ToArray(),
            operation.DeviceId,
            operation.CreatedAt,
            operation.ParentRevisionIds
                .Where(parent => operationsByRevision.TryGetValue(parent, out var parentOperation) && parentOperation.EntryId == operation.EntryId)
                .OrderBy(parent => parent.Value, StringComparer.Ordinal)
                .ToArray());
}

public sealed record PortableLogbookAuditSnapshot(
    LogbookId LogbookId,
    int SchemaVersion,
    IReadOnlyList<CustomFieldDefinition> CustomFieldDefinitions,
    IReadOnlyList<PortableLogbookCurrentRecord> CurrentRecords,
    IReadOnlyList<PortableLogbookEntryRevisionHistory> RevisionHistory,
    IReadOnlyList<PortableLogbookConflict> Conflicts);

public sealed record PortableLogbookCurrentRecord(
    EntryId EntryId,
    RevisionId CurrentRevisionId,
    PortableLogbookEntry Entry);

public sealed record PortableLogbookEntryRevisionHistory(
    EntryId EntryId,
    IReadOnlyList<PortableLogbookAuditRevision> Revisions);

public sealed record PortableLogbookAuditRevision(
    RevisionId RevisionId,
    PortableOperationKind Kind,
    IReadOnlyList<RevisionId> ParentRevisionIds,
    DeviceId DeviceId,
    DateTimeOffset CreatedAt,
    IReadOnlyList<RevisionId> VerifiedParentRevisionIds);
