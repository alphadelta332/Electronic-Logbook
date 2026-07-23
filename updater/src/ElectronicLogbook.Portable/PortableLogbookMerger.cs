namespace ElectronicLogbook.Portable;

public static class PortableLogbookMerger
{
    public static PortableLogbookMergeResult Merge(IEnumerable<PortableLogbookOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);

        var uniqueOperations = operations
            .GroupBy(operation => operation.RevisionId)
            .Select(group => group.OrderBy(operation => operation.CreatedAt).First())
            .ToArray();

        var entries = new Dictionary<EntryId, PortableLogbookMaterializedEntry>();
        var conflicts = new List<PortableLogbookConflict>();

        foreach (var entryGroup in uniqueOperations.GroupBy(operation => operation.EntryId).OrderBy(group => group.Key.Value, StringComparer.Ordinal))
        {
            var operationsByRevision = entryGroup.ToDictionary(operation => operation.RevisionId);
            var childRevisionIds = entryGroup
                .SelectMany(operation => operation.ParentRevisionIds)
                .ToHashSet();
            var heads = entryGroup
                .Where(operation => !childRevisionIds.Contains(operation.RevisionId))
                .OrderBy(operation => operation.RevisionId.Value, StringComparer.Ordinal)
                .ToArray();

            if (heads.Length == 0)
            {
                continue;
            }

            if (heads.Length > 1)
            {
                conflicts.Add(new PortableLogbookConflict(entryGroup.Key, heads.Select(operation => operation.RevisionId).ToArray()));
                continue;
            }

            var head = heads[0];
            entries[entryGroup.Key] = new PortableLogbookMaterializedEntry(
                entryGroup.Key,
                head.RevisionId,
                head.Kind == PortableOperationKind.Deletion,
                GetEntryPayload(head),
                BuildRevisionChain(head, operationsByRevision));
        }

        return new PortableLogbookMergeResult(entries, conflicts, uniqueOperations.Length);
    }

    private static PortableLogbookEntry? GetEntryPayload(PortableLogbookOperation operation) =>
        operation switch
        {
            CreateEntryOperation create => create.Entry,
            CorrectEntryOperation correct => correct.Entry,
            ResolveConflictOperation resolution => resolution.Entry,
            DeleteEntryOperation => null,
            _ => throw new InvalidOperationException($"Unsupported operation type {operation.GetType().Name}.")
        };

    private static IReadOnlyList<RevisionId> BuildRevisionChain(
        PortableLogbookOperation head,
        IReadOnlyDictionary<RevisionId, PortableLogbookOperation> operationsByRevision)
    {
        var chain = new List<RevisionId>();
        var visited = new HashSet<RevisionId>();
        var stack = new Stack<RevisionId>();
        stack.Push(head.RevisionId);

        while (stack.Count > 0)
        {
            var revisionId = stack.Pop();
            if (!visited.Add(revisionId))
            {
                continue;
            }

            chain.Add(revisionId);
            if (!operationsByRevision.TryGetValue(revisionId, out var operation))
            {
                continue;
            }

            foreach (var parent in operation.ParentRevisionIds.OrderByDescending(id => id.Value, StringComparer.Ordinal))
            {
                stack.Push(parent);
            }
        }

        return chain;
    }
}

public sealed record PortableLogbookMergeResult(
    IReadOnlyDictionary<EntryId, PortableLogbookMaterializedEntry> Entries,
    IReadOnlyList<PortableLogbookConflict> Conflicts,
    int OperationCount);

public sealed record PortableLogbookMaterializedEntry(
    EntryId EntryId,
    RevisionId CurrentRevisionId,
    bool IsDeleted,
    PortableLogbookEntry? Entry,
    IReadOnlyList<RevisionId> RevisionHistory);

public sealed record PortableLogbookConflict(
    EntryId EntryId,
    IReadOnlyList<RevisionId> HeadRevisionIds);
