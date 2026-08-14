using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public sealed record MobileHostedSyncDiagnosticSummary(
    int TotalOperationCount,
    int CurrentDeviceOperationCount,
    int UploadedCurrentDeviceOperationCount,
    IReadOnlyList<MobileHostedSyncDiagnosticOperation> PendingUploads,
    IReadOnlyList<MobileHostedSyncDiagnosticOperation> OtherDeviceHistory)
{
    public int PendingUploadCount => PendingUploads.Count;

    public static MobileHostedSyncDiagnosticSummary Create(
        PortableLogbookDocumentV2 document,
        BrowserHostedSyncState hostedSync)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(hostedSync);

        var uploadedRevisionIds = (hostedSync.UploadedRevisionIds ?? []).ToHashSet();
        var currentDeviceOperations = document.Operations
            .Where(operation => operation.DeviceId == hostedSync.DeviceId)
            .ToArray();
        var pendingUploads = currentDeviceOperations
            .Where(operation => !uploadedRevisionIds.Contains(operation.RevisionId))
            .OrderByDescending(operation => operation.CreatedAt)
            .ThenBy(operation => operation.RevisionId.Value, StringComparer.Ordinal)
            .Select(operation => MobileHostedSyncDiagnosticOperation.Create(operation))
            .ToArray();
        var otherDeviceHistory = document.Operations
            .Where(operation => operation.DeviceId != hostedSync.DeviceId)
            .OrderByDescending(operation => operation.CreatedAt)
            .ThenBy(operation => operation.RevisionId.Value, StringComparer.Ordinal)
            .Select(operation => MobileHostedSyncDiagnosticOperation.Create(operation))
            .ToArray();

        return new MobileHostedSyncDiagnosticSummary(
            document.Operations.Count,
            currentDeviceOperations.Length,
            currentDeviceOperations.Count(operation => uploadedRevisionIds.Contains(operation.RevisionId)),
            pendingUploads,
            otherDeviceHistory);
    }
}

public sealed record MobileHostedSyncDiagnosticOperation(
    string KindLabel,
    string FlightId,
    DateTimeOffset CreatedAt,
    string RevisionId,
    string DeviceId,
    string? Detail)
{
    internal static MobileHostedSyncDiagnosticOperation Create(PortableLogbookOperationV2 operation) =>
        new(
            operation.Kind switch
            {
                PortableOperationKind.Create => "Create",
                PortableOperationKind.Correction => "Correction",
                PortableOperationKind.Deletion => "Deletion",
                PortableOperationKind.ConflictResolution => "Conflict resolution",
                _ => operation.Kind.ToString()
            },
            string.IsNullOrWhiteSpace(operation.Entry?.FlightId)
                ? operation.EntryId.Value
                : operation.Entry.FlightId,
            operation.CreatedAt,
            operation.RevisionId.Value,
            operation.DeviceId.Value,
            FirstNonBlank(operation.Entry?.Remarks, operation.Reason, operation.ResolutionNote));

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
