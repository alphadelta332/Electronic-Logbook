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

        var localRevisionIds = localDocument.Operations.Select(operation => operation.RevisionId).ToHashSet();
        var newOperations = incomingDocument.Operations
            .Where(operation => !localRevisionIds.Contains(operation.RevisionId))
            .OrderBy(operation => operation.CreatedAt)
            .ThenBy(operation => operation.RevisionId.Value, StringComparer.Ordinal)
            .ToArray();
        var duplicateCount = incomingDocument.Operations.Count - newOperations.Length;
        var merged = PortableLogbookMerger.Merge(localDocument.Operations.Concat(newOperations));

        return new PortableLogbookImportPreview(
            newOperations,
            duplicateCount,
            newOperations.Count(operation => operation.Kind == PortableOperationKind.Create),
            newOperations.Count(operation => operation.Kind == PortableOperationKind.Correction),
            newOperations.Count(operation => operation.Kind == PortableOperationKind.Deletion),
            merged.Conflicts);
    }

    public static PortableLogbookDocument ApplyImport(
        PortableLogbookDocument localDocument,
        PortableLogbookDocument incomingDocument)
    {
        var preview = PreviewImport(localDocument, incomingDocument);
        if (preview.HasConflicts)
        {
            throw new PortableLogbookImportException(
                PortableLogbookImportError.UnresolvedConflicts,
                "Incoming operations produce unresolved conflicts.");
        }

        return PortableLogbookDocument.CreateAustraliaFirst(
            localDocument.LogbookId,
            localDocument.CustomFieldDefinitions,
            localDocument.Operations.Concat(preview.NewOperations));
    }

    private static void EnsureValid(PortableLogbookDocument document, string parameterName)
    {
        var validation = PortableLogbookValidator.Validate(document);
        if (!validation.IsValid)
        {
            throw new ArgumentException("Portable logbook document is invalid.", parameterName);
        }
    }
}

public sealed record PortableLogbookImportPreview(
    IReadOnlyList<PortableLogbookOperation> NewOperations,
    int DuplicateOperationCount,
    int CreateCount,
    int CorrectionCount,
    int DeletionCount,
    IReadOnlyList<PortableLogbookConflict> Conflicts)
{
    public bool HasConflicts => Conflicts.Count > 0;
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
