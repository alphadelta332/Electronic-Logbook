namespace ElectronicLogbook.Portable;

public static class PortableLogbookRevisionHistory
{
    public static PortableLogbookRevisionHistoryView ForEntry(
        PortableLogbookDocument document,
        EntryId entryId)
    {
        ArgumentNullException.ThrowIfNull(document);

        var validation = PortableLogbookValidator.Validate(document);
        if (!validation.IsValid)
        {
            throw new ArgumentException("Portable logbook document is invalid.", nameof(document));
        }

        var entryOperations = document.Operations
            .Where(operation => operation.EntryId == entryId)
            .OrderBy(operation => operation.CreatedAt)
            .ThenBy(operation => operation.RevisionId.Value, StringComparer.Ordinal)
            .ToArray();
        if (entryOperations.Length == 0)
        {
            throw new KeyNotFoundException($"Entry '{entryId}' was not found.");
        }

        var merge = PortableLogbookMerger.Merge(document.Operations);
        var conflict = merge.Conflicts.FirstOrDefault(item => item.EntryId == entryId);
        merge.Entries.TryGetValue(entryId, out var current);

        return new PortableLogbookRevisionHistoryView(
            entryId,
            current?.CurrentRevisionId,
            current?.IsDeleted ?? false,
            conflict?.HeadRevisionIds ?? [],
            entryOperations.Select(ToRevisionHistoryItem).ToArray());
    }

    private static PortableLogbookRevisionHistoryItem ToRevisionHistoryItem(PortableLogbookOperation operation) =>
        new(
            operation.RevisionId,
            operation.Kind,
            operation.ParentRevisionIds.OrderBy(parent => parent.Value, StringComparer.Ordinal).ToArray(),
            operation.DeviceId,
            operation.CreatedAt,
            GetEntryPayload(operation));

    private static PortableLogbookEntry? GetEntryPayload(PortableLogbookOperation operation) =>
        operation switch
        {
            CreateEntryOperation create => create.Entry,
            CorrectEntryOperation correction => correction.Entry,
            ResolveConflictOperation resolution => resolution.Entry,
            DeleteEntryOperation => null,
            _ => throw new InvalidOperationException($"Unsupported operation type {operation.GetType().Name}.")
        };
}

public sealed record PortableLogbookRevisionHistoryView(
    EntryId EntryId,
    RevisionId? CurrentRevisionId,
    bool IsDeleted,
    IReadOnlyList<RevisionId> ConflictHeadRevisionIds,
    IReadOnlyList<PortableLogbookRevisionHistoryItem> Revisions)
{
    public bool HasConflict => ConflictHeadRevisionIds.Count > 0;
}

public sealed record PortableLogbookRevisionHistoryItem(
    RevisionId RevisionId,
    PortableOperationKind Kind,
    IReadOnlyList<RevisionId> ParentRevisionIds,
    DeviceId DeviceId,
    DateTimeOffset CreatedAt,
    PortableLogbookEntry? Entry);
