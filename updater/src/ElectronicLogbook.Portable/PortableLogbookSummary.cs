namespace ElectronicLogbook.Portable;

public static class PortableLogbookSummary
{
    public static PortableLogbookRedactedSummary Create(PortableLogbookDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var operations = document.Operations;
        var merge = PortableLogbookMerger.Merge(operations);
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
            operations.Select(operation => operation.DeviceId).Distinct().Count(),
            merge.Entries.Values.Count(entry => !entry.IsDeleted),
            merge.Entries.Values.Count(entry => entry.IsDeleted),
            merge.Conflicts.Count,
            operations.Count == 0 ? null : operations.Min(operation => operation.CreatedAt),
            operations.Count == 0 ? null : operations.Max(operation => operation.CreatedAt));
    }

    public static PortableLogbookRedactedSummary Create(PortableLogbookDocumentV2 document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var operations = document.Operations;
        var merge = PortableLogbookWorkbookProjection.MergeV2(operations);
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
            operations.Select(operation => operation.DeviceId).Distinct().Count(),
            merge.Entries.Values.Count(entry => !entry.IsDeleted),
            merge.Entries.Values.Count(entry => entry.IsDeleted),
            merge.Conflicts.Count,
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
    int DistinctDeviceCount,
    int CurrentRecordCount,
    int DeletedCurrentRecordCount,
    int UnresolvedConflictCount,
    DateTimeOffset? FirstOperationAt,
    DateTimeOffset? LastOperationAt);
