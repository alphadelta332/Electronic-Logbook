namespace ElectronicLogbook.Portable;

public static class PortableLogbookWorkbookProjection
{
    public static IReadOnlyList<PortableLogbookWorkbookRow> CreateCurrentRows(PortableLogbookDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return CreateCurrentRows(PortableLogbookMerger.Merge(document.Operations));
    }

    public static IReadOnlyList<PortableLogbookWorkbookRow> CreateCurrentRows(PortableLogbookMergeResult mergeResult)
    {
        ArgumentNullException.ThrowIfNull(mergeResult);

        return mergeResult
            .Entries
            .Values
            .Where(entry => !entry.IsDeleted && entry.Entry is not null)
            .OrderBy(entry => entry.Entry!.Date)
            .ThenBy(entry => entry.EntryId.Value, StringComparer.Ordinal)
            .Select(entry => new PortableLogbookWorkbookRow(entry.EntryId, entry.CurrentRevisionId, entry.Entry!))
            .ToArray();
    }

    public static PortableLogbookProjectionResult Reconcile(
        IEnumerable<PortableLogbookMaterializedEntry> knownEntries,
        IEnumerable<PortableLogbookWorkbookRow> currentRows,
        LogbookId logbookId,
        DeviceId deviceId,
        DateTimeOffset createdAt,
        PortableLogbookIdFactory? idFactory = null)
    {
        ArgumentNullException.ThrowIfNull(knownEntries);
        ArgumentNullException.ThrowIfNull(currentRows);

        idFactory ??= PortableLogbookIdFactory.Default;
        var knownByEntryId = knownEntries.ToDictionary(entry => entry.EntryId);
        var rows = currentRows.ToArray();
        var rowValidation = PortableLogbookWorkbookRowValidator.Validate(rows, knownByEntryId.Values);
        if (!rowValidation.IsValid)
        {
            throw new PortableLogbookWorkbookProjectionException(
                PortableLogbookWorkbookProjectionError.InvalidRowMetadata,
                "Workbook row metadata is invalid.",
                rowValidation);
        }

        var seenKnownIds = new HashSet<EntryId>();
        var allocatedIds = knownByEntryId.Keys.ToHashSet();
        var operations = new List<PortableLogbookOperation>();

        foreach (var row in rows)
        {
            if (row.EntryId is null || !knownByEntryId.TryGetValue(row.EntryId.Value, out var known))
            {
                var entryId = idFactory.NewEntryIdExcluding(allocatedIds);
                allocatedIds.Add(entryId);
                operations.Add(new CreateEntryOperation(
                    logbookId,
                    entryId,
                    idFactory.NewRevisionId(),
                    deviceId,
                    createdAt,
                    row.Entry));
                continue;
            }

            seenKnownIds.Add(known.EntryId);
            if (!known.IsDeleted &&
                known.Entry is not null &&
                EntriesEqual(known.Entry, row.Entry))
            {
                continue;
            }

            operations.Add(new CorrectEntryOperation(
                logbookId,
                known.EntryId,
                idFactory.NewRevisionId(),
                new HashSet<RevisionId> { known.CurrentRevisionId },
                deviceId,
                createdAt,
                row.Entry));
        }

        foreach (var missing in knownByEntryId.Values.Where(entry => !entry.IsDeleted && !seenKnownIds.Contains(entry.EntryId)))
        {
            operations.Add(new DeleteEntryOperation(
                logbookId,
                missing.EntryId,
                idFactory.NewRevisionId(),
                new HashSet<RevisionId> { missing.CurrentRevisionId },
                deviceId,
                createdAt,
                "Row missing from workbook projection."));
        }

        return new PortableLogbookProjectionResult(operations);
    }

    public static IReadOnlyList<PortableLogbookWorkbookRowV2> CreateCurrentRows(PortableLogbookDocumentV2 document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return CreateCurrentRows(MergeV2(document.Operations));
    }

    public static IReadOnlyList<PortableLogbookWorkbookRowV2> CreateCurrentRows(PortableLogbookMergeResultV2 mergeResult)
    {
        ArgumentNullException.ThrowIfNull(mergeResult);

        return mergeResult
            .Entries
            .Values
            .Where(entry => !entry.IsDeleted && entry.Entry is not null)
            .OrderBy(entry => entry.Entry!.Date)
            .ThenBy(entry => entry.EntryId.Value, StringComparer.Ordinal)
            .Select(entry => new PortableLogbookWorkbookRowV2(entry.EntryId, entry.CurrentRevisionId, entry.Entry!))
            .ToArray();
    }

    public static PortableLogbookProjectionResultV2 ReconcileV2(
        IEnumerable<PortableLogbookMaterializedEntryV2> knownEntries,
        IEnumerable<PortableLogbookWorkbookRowV2> currentRows,
        LogbookId logbookId,
        DeviceId deviceId,
        DateTimeOffset createdAt,
        PortableLogbookIdFactory? idFactory = null)
    {
        ArgumentNullException.ThrowIfNull(knownEntries);
        ArgumentNullException.ThrowIfNull(currentRows);

        idFactory ??= PortableLogbookIdFactory.Default;
        var knownByEntryId = knownEntries.ToDictionary(entry => entry.EntryId);
        var rows = currentRows.ToArray();
        var rowValidation = ValidateRowsV2(rows, knownByEntryId.Values);
        if (!rowValidation.IsValid)
        {
            throw new PortableLogbookWorkbookProjectionException(
                PortableLogbookWorkbookProjectionError.InvalidRowMetadata,
                "Workbook row metadata is invalid.",
                rowValidation);
        }

        var seenKnownIds = new HashSet<EntryId>();
        var allocatedIds = knownByEntryId.Keys.ToHashSet();
        var operations = new List<PortableLogbookOperationV2>();

        foreach (var row in rows)
        {
            if (row.EntryId is null || !knownByEntryId.TryGetValue(row.EntryId.Value, out var known))
            {
                var entryId = idFactory.NewEntryIdExcluding(allocatedIds);
                allocatedIds.Add(entryId);
                operations.Add(PortableLogbookOperationV2.Create(
                    logbookId,
                    entryId,
                    idFactory.NewRevisionId(),
                    deviceId,
                    createdAt,
                    row.Entry));
                continue;
            }

            seenKnownIds.Add(known.EntryId);
            if (!known.IsDeleted &&
                known.Entry is not null &&
                EntriesEqual(known.Entry, row.Entry))
            {
                continue;
            }

            operations.Add(PortableLogbookOperationV2.Correct(
                logbookId,
                known.EntryId,
                idFactory.NewRevisionId(),
                [known.CurrentRevisionId],
                deviceId,
                createdAt,
                row.Entry));
        }

        foreach (var missing in knownByEntryId.Values.Where(entry => !entry.IsDeleted && !seenKnownIds.Contains(entry.EntryId)))
        {
            operations.Add(PortableLogbookOperationV2.Delete(
                logbookId,
                missing.EntryId,
                idFactory.NewRevisionId(),
                [missing.CurrentRevisionId],
                deviceId,
                createdAt,
                "Row missing from workbook projection."));
        }

        return new PortableLogbookProjectionResultV2(operations);
    }

    public static PortableLogbookMergeResultV2 MergeV2(IEnumerable<PortableLogbookOperationV2> operations)
    {
        var uniqueOperations = operations
            .GroupBy(operation => operation.RevisionId)
            .Select(group => group.OrderBy(operation => operation.CreatedAt).First())
            .ToArray();

        var entries = new Dictionary<EntryId, PortableLogbookMaterializedEntryV2>();
        var conflicts = new List<PortableLogbookConflict>();

        foreach (var entryGroup in uniqueOperations.GroupBy(operation => operation.EntryId).OrderBy(group => group.Key.Value, StringComparer.Ordinal))
        {
            var operationsByRevision = entryGroup.ToDictionary(operation => operation.RevisionId);
            var childRevisionIds = entryGroup.SelectMany(operation => operation.ParentRevisionIds).ToHashSet();
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
            entries[entryGroup.Key] = new PortableLogbookMaterializedEntryV2(
                entryGroup.Key,
                head.RevisionId,
                head.Kind == PortableOperationKind.Deletion,
                head.Entry,
                BuildRevisionChainV2(head, operationsByRevision));
        }

        return new PortableLogbookMergeResultV2(entries, conflicts, uniqueOperations.Length);
    }

    private static IReadOnlyList<RevisionId> BuildRevisionChainV2(
        PortableLogbookOperationV2 head,
        IReadOnlyDictionary<RevisionId, PortableLogbookOperationV2> operationsByRevision)
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

    private static PortableLogbookWorkbookRowValidationResult ValidateRowsV2(
        IEnumerable<PortableLogbookWorkbookRowV2> rows,
        IEnumerable<PortableLogbookMaterializedEntryV2> knownEntries)
    {
        var knownByEntryId = knownEntries.ToDictionary(entry => entry.EntryId);
        var errors = new List<PortableLogbookWorkbookRowValidationError>();
        var seenEntryIds = new HashSet<EntryId>();
        var rowNumber = 0;
        foreach (var row in rows)
        {
            rowNumber++;
            if (row.Entry is null)
            {
                errors.Add(new PortableLogbookWorkbookRowValidationError(
                    rowNumber,
                    PortableLogbookWorkbookRowValidationCode.MissingEntryPayload,
                    "Workbook row is missing its portable entry payload."));
                continue;
            }

            if (row.EntryId is null && row.CurrentRevisionId is not null)
            {
                errors.Add(new PortableLogbookWorkbookRowValidationError(
                    rowNumber,
                    PortableLogbookWorkbookRowValidationCode.RevisionWithoutEntryId,
                    "Workbook row has a current revision ID without an entry ID."));
                continue;
            }

            if (row.EntryId is null)
            {
                continue;
            }

            if (!EntryId.IsValid(row.EntryId.Value))
            {
                errors.Add(new PortableLogbookWorkbookRowValidationError(
                    rowNumber,
                    PortableLogbookWorkbookRowValidationCode.InvalidEntryId,
                    $"Workbook row has an invalid entry ID '{row.EntryId}'."));
                continue;
            }

            if (!seenEntryIds.Add(row.EntryId.Value))
            {
                errors.Add(new PortableLogbookWorkbookRowValidationError(
                    rowNumber,
                    PortableLogbookWorkbookRowValidationCode.DuplicateEntryId,
                    $"Workbook row duplicates entry ID '{row.EntryId}'."));
                continue;
            }

            if (!knownByEntryId.TryGetValue(row.EntryId.Value, out var known))
            {
                errors.Add(new PortableLogbookWorkbookRowValidationError(
                    rowNumber,
                    PortableLogbookWorkbookRowValidationCode.UnknownEntryId,
                    $"Workbook row references unknown entry ID '{row.EntryId}'."));
                continue;
            }

            if (row.CurrentRevisionId is null)
            {
                errors.Add(new PortableLogbookWorkbookRowValidationError(
                    rowNumber,
                    PortableLogbookWorkbookRowValidationCode.MissingCurrentRevisionId,
                    $"Workbook row for entry '{row.EntryId}' is missing its current revision ID."));
                continue;
            }

            if (row.CurrentRevisionId.Value != known.CurrentRevisionId)
            {
                errors.Add(new PortableLogbookWorkbookRowValidationError(
                    rowNumber,
                    PortableLogbookWorkbookRowValidationCode.StaleCurrentRevisionId,
                    $"Workbook row for entry '{row.EntryId}' references stale revision '{row.CurrentRevisionId}'."));
            }
        }

        return new PortableLogbookWorkbookRowValidationResult(errors.Count == 0, errors);
    }

    private static bool EntriesEqual(PortableLogbookEntry left, PortableLogbookEntry right) =>
        left.Date == right.Date &&
        left.AircraftType == right.AircraftType &&
        left.Registration == right.Registration &&
        left.FlightNumber == right.FlightNumber &&
        left.From == right.From &&
        left.To == right.To &&
        left.Route == right.Route &&
        left.Details == right.Details &&
        left.MultiPilot == right.MultiPilot &&
        left.PilotInCommand == right.PilotInCommand &&
        left.CoPilot == right.CoPilot &&
        left.Dual == right.Dual &&
        left.Instructor == right.Instructor &&
        left.Day == right.Day &&
        left.Night == right.Night &&
        left.InstrumentActual == right.InstrumentActual &&
        left.InstrumentSimulated == right.InstrumentSimulated &&
        left.TakeoffsDay == right.TakeoffsDay &&
        left.TakeoffsNight == right.TakeoffsNight &&
        left.LandingsDay == right.LandingsDay &&
        left.LandingsNight == right.LandingsNight &&
        left.IfrApproaches == right.IfrApproaches &&
        left.Holding == right.Holding &&
        left.Rnav == right.Rnav &&
        left.Circling == right.Circling &&
        left.CustomFields.Count == right.CustomFields.Count &&
        left.CustomFields.All(pair => right.CustomFields.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private static bool EntriesEqual(PortableLogbookWorkbookEntry left, PortableLogbookWorkbookEntry right)
    {
        var leftValues = PortableLogbookWorkbookEntryFields.ToFieldValues(left);
        var rightValues = PortableLogbookWorkbookEntryFields.ToFieldValues(right);
        return leftValues.Count == rightValues.Count &&
            leftValues.All(pair => rightValues.TryGetValue(pair.Key, out var value) && Equals(pair.Value, value));
    }
}

public sealed record PortableLogbookWorkbookRow(
    EntryId? EntryId,
    RevisionId? CurrentRevisionId,
    PortableLogbookEntry Entry);

public sealed record PortableLogbookProjectionResult(
    IReadOnlyList<PortableLogbookOperation> Operations)
{
    public int CreateCount => Operations.Count(operation => operation.Kind == PortableOperationKind.Create);

    public int CorrectionCount => Operations.Count(operation => operation.Kind == PortableOperationKind.Correction);

    public int DeletionCount => Operations.Count(operation => operation.Kind == PortableOperationKind.Deletion);
}

public sealed record PortableLogbookWorkbookRowV2(
    EntryId? EntryId,
    RevisionId? CurrentRevisionId,
    PortableLogbookWorkbookEntry Entry);

public sealed record PortableLogbookMaterializedEntryV2(
    EntryId EntryId,
    RevisionId CurrentRevisionId,
    bool IsDeleted,
    PortableLogbookWorkbookEntry? Entry,
    IReadOnlyList<RevisionId> RevisionHistory);

public sealed record PortableLogbookMergeResultV2(
    IReadOnlyDictionary<EntryId, PortableLogbookMaterializedEntryV2> Entries,
    IReadOnlyList<PortableLogbookConflict> Conflicts,
    int OperationCount);

public sealed record PortableLogbookProjectionResultV2(
    IReadOnlyList<PortableLogbookOperationV2> Operations)
{
    public int CreateCount => Operations.Count(operation => operation.Kind == PortableOperationKind.Create);

    public int CorrectionCount => Operations.Count(operation => operation.Kind == PortableOperationKind.Correction);

    public int DeletionCount => Operations.Count(operation => operation.Kind == PortableOperationKind.Deletion);
}

public sealed class PortableLogbookWorkbookProjectionException : Exception
{
    public PortableLogbookWorkbookProjectionException(
        PortableLogbookWorkbookProjectionError error,
        string message,
        PortableLogbookWorkbookRowValidationResult rowValidation)
        : base(message)
    {
        Error = error;
        RowValidation = rowValidation;
    }

    public PortableLogbookWorkbookProjectionError Error { get; }

    public PortableLogbookWorkbookRowValidationResult RowValidation { get; }
}

public enum PortableLogbookWorkbookProjectionError
{
    InvalidRowMetadata
}
