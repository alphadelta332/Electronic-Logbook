namespace ElectronicLogbook.Portable;

public static class PortableLogbookSummary
{
    public static PortableLogbookRedactedSummary Create(PortableLogbookDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var operations = document.Operations;
        return new PortableLogbookRedactedSummary(
            document.LogbookId,
            document.SchemaVersion,
            document.JurisdictionProfile,
            document.JurisdictionProfileVersion,
            document.CustomFieldDefinitions.Count,
            operations.Count,
            operations.Count(operation => operation.Kind == PortableOperationKind.Create),
            operations.Count(operation => operation.Kind == PortableOperationKind.Correction),
            operations.Count(operation => operation.Kind == PortableOperationKind.Deletion),
            operations.Count(operation => operation.Kind == PortableOperationKind.ConflictResolution),
            operations.Count == 0 ? null : operations.Min(operation => operation.CreatedAt),
            operations.Count == 0 ? null : operations.Max(operation => operation.CreatedAt));
    }
}

public sealed record PortableLogbookRedactedSummary(
    LogbookId LogbookId,
    int SchemaVersion,
    string JurisdictionProfile,
    int JurisdictionProfileVersion,
    int CustomFieldDefinitionCount,
    int OperationCount,
    int CreateCount,
    int CorrectionCount,
    int DeletionCount,
    int ConflictResolutionCount,
    DateTimeOffset? FirstOperationAt,
    DateTimeOffset? LastOperationAt);
