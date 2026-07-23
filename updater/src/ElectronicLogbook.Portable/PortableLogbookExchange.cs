namespace ElectronicLogbook.Portable;

public static class PortableLogbookExchange
{
    public static PortableLogbookImportPreview PreviewImport(
        PortableLogbookDocument localDocument,
        PortableLogbookDocument incomingDocument)
    {
        ArgumentNullException.ThrowIfNull(localDocument);
        ArgumentNullException.ThrowIfNull(incomingDocument);

        if (localDocument.LogbookId != incomingDocument.LogbookId)
        {
            throw new ArgumentException("Incoming document belongs to a different logbook.", nameof(incomingDocument));
        }

        EnsureValid(localDocument, nameof(localDocument));
        EnsureValid(incomingDocument, nameof(incomingDocument));
        var customFieldMerge = PortableLogbookCustomFieldDefinitions.Merge(
            localDocument.CustomFieldDefinitions,
            incomingDocument.CustomFieldDefinitions);

        var localRevisionIds = localDocument.Operations.Select(operation => operation.RevisionId).ToHashSet();
        var duplicateOperations = incomingDocument.Operations
            .Where(operation => localRevisionIds.Contains(operation.RevisionId))
            .OrderBy(operation => operation.CreatedAt)
            .ThenBy(operation => operation.RevisionId.Value, StringComparer.Ordinal)
            .ToArray();
        var newOperations = incomingDocument.Operations
            .Where(operation => !localRevisionIds.Contains(operation.RevisionId))
            .OrderBy(operation => operation.CreatedAt)
            .ThenBy(operation => operation.RevisionId.Value, StringComparer.Ordinal)
            .ToArray();
        var merged = PortableLogbookMerger.Merge(localDocument.Operations.Concat(newOperations));

        return new PortableLogbookImportPreview(
            newOperations,
            duplicateOperations,
            newOperations.Select(CreateChangeSummary).ToArray(),
            duplicateOperations.Select(CreateChangeSummary).ToArray(),
            newOperations.Count(operation => operation.Kind == PortableOperationKind.Create),
            newOperations.Count(operation => operation.Kind == PortableOperationKind.Correction),
            newOperations.Count(operation => operation.Kind == PortableOperationKind.Deletion),
            merged.Conflicts,
            customFieldMerge);
    }

    public static PortableLogbookDocument ApplyImport(
        PortableLogbookDocument localDocument,
        PortableLogbookDocument incomingDocument)
    {
        var preview = PreviewImport(localDocument, incomingDocument);
        if (preview.HasConflicts || preview.CustomFieldDefinitions.HasConflicts)
        {
            throw new PortableLogbookImportException(
                PortableLogbookImportError.UnresolvedConflicts,
                "Incoming operations produce unresolved conflicts.");
        }

        return PortableLogbookDocument.CreateAustraliaFirst(
            localDocument.LogbookId,
            preview.CustomFieldDefinitions.Definitions,
            localDocument.Operations.Concat(preview.NewOperations));
    }

    public static PortableLogbookImportPlan PlanImport(
        PortableLogbookDocument localDocument,
        PortableLogbookDocument incomingDocument)
    {
        var preview = PreviewImport(localDocument, incomingDocument);
        var status = preview.CustomFieldDefinitions.HasConflicts
            ? PortableLogbookImportPlanStatus.RequiresCustomFieldResolution
            : preview.HasConflicts
            ? PortableLogbookImportPlanStatus.RequiresConflictResolution
            : preview.NewOperations.Count == 0
                ? PortableLogbookImportPlanStatus.DuplicateOnly
                : PortableLogbookImportPlanStatus.ReadyToApply;
        return new PortableLogbookImportPlan(status, preview);
    }

    private static void EnsureValid(PortableLogbookDocument document, string parameterName)
    {
        var validation = PortableLogbookValidator.Validate(document);
        if (!validation.IsValid)
        {
            throw new ArgumentException("Portable logbook document is invalid.", parameterName);
        }
    }

    private static PortableLogbookImportChangeSummary CreateChangeSummary(PortableLogbookOperation operation)
    {
        var entry = EntryPayload(operation);
        return new PortableLogbookImportChangeSummary(
            operation.EntryId,
            operation.RevisionId,
            operation.Kind,
            entry?.Date,
            entry?.AircraftType,
            entry?.Registration,
            entry?.From,
            entry?.To,
            entry?.Details,
            operation is DeleteEntryOperation delete ? delete.Reason : null);
    }

    private static PortableLogbookEntry? EntryPayload(PortableLogbookOperation operation) =>
        operation switch
        {
            CreateEntryOperation create => create.Entry,
            CorrectEntryOperation correction => correction.Entry,
            ResolveConflictOperation resolution => resolution.Entry,
            DeleteEntryOperation => null,
            _ => throw new InvalidOperationException($"Unsupported operation type {operation.GetType().Name}.")
        };
}

public sealed record PortableLogbookImportPreview(
    IReadOnlyList<PortableLogbookOperation> NewOperations,
    IReadOnlyList<PortableLogbookOperation> DuplicateOperations,
    IReadOnlyList<PortableLogbookImportChangeSummary> NewOperationSummaries,
    IReadOnlyList<PortableLogbookImportChangeSummary> DuplicateOperationSummaries,
    int CreateCount,
    int CorrectionCount,
    int DeletionCount,
    IReadOnlyList<PortableLogbookConflict> Conflicts,
    PortableLogbookCustomFieldDefinitionMergeResult CustomFieldDefinitions)
{
    public int DuplicateOperationCount => DuplicateOperations.Count;

    public bool HasConflicts => Conflicts.Count > 0;
}

public sealed record PortableLogbookImportChangeSummary(
    EntryId EntryId,
    RevisionId RevisionId,
    PortableOperationKind Kind,
    DateOnly? Date,
    string? AircraftType,
    string? Registration,
    string? From,
    string? To,
    string? Details,
    string? DeletionReason);

public sealed record PortableLogbookImportPlan(
    PortableLogbookImportPlanStatus Status,
    PortableLogbookImportPreview Preview);

public enum PortableLogbookImportPlanStatus
{
    DuplicateOnly,
    ReadyToApply,
    RequiresConflictResolution,
    RequiresCustomFieldResolution
}

public sealed class PortableLogbookImportException : Exception
{
    public PortableLogbookImportException(PortableLogbookImportError error, string message)
        : base(message)
    {
        Error = error;
    }

    public PortableLogbookImportError Error { get; }
}

public enum PortableLogbookImportError
{
    UnresolvedConflicts
}
