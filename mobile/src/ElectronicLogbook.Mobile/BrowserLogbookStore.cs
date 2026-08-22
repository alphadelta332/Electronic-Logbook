using System.Text.Json;
using ElectronicLogbook.Portable;
using Microsoft.JSInterop;

namespace ElectronicLogbook.Mobile;

public sealed class BrowserLogbookStore(IJSRuntime jsRuntime)
{
    internal const int CurrentStoreVersion = 1;
    private const string DocumentKey = "portable-document";
    private const string ConnectionDiagnosticsKey = "connection-diagnostics";
    private const string DiagnosticProbeKeyPrefix = "diagnostic-probe:";

    public async ValueTask<PortableLogbookDocument?> LoadDocumentAsync()
    {
        var state = await LoadStateAsync();
        return state?.Document;
    }

    public async ValueTask<PortableLogbookDocumentV2?> LoadDocumentV2Async()
    {
        var state = await LoadStateV2Async();
        return state?.Document;
    }

    public async ValueTask<BrowserLogbookState?> LoadStateAsync()
    {
        var json = await jsRuntime.InvokeAsync<string?>("electronicLogbookStore.load", DocumentKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var parsed = JsonDocument.Parse(json);
            if (!parsed.RootElement.TryGetProperty("storeVersion", out _))
            {
                return new BrowserLogbookState(ReadDocument(json), [], null);
            }

            var stored = JsonSerializer.Deserialize<BrowserLogbookStoredDocument>(json, PortableLogbookJson.SerializerOptions)
                ?? throw new BrowserLogbookStoreException("Stored logbook state is invalid.");
            if (stored.StoreVersion != CurrentStoreVersion)
            {
                throw new BrowserLogbookStoreException(
                    $"Stored logbook state version {stored.StoreVersion} is not supported by this app.");
            }

            if (stored.SchemaVersion > PortableLogbookDocument.CurrentSchemaVersion)
            {
                throw new BrowserLogbookStoreException(
                    $"Stored portable schema version {stored.SchemaVersion} is newer than this app supports.");
            }

            var document = ReadDocument(stored.DocumentJson);
            if (document.SchemaVersion != stored.SchemaVersion)
            {
                throw new BrowserLogbookStoreException("Stored logbook state schema metadata does not match the document.");
            }

            var exportCheckpoint = stored.LastSuccessfulExport?.Covers(document) == true
                ? stored.LastSuccessfulExport
                : null;
            return new BrowserLogbookState(
                document,
                stored.ImportReceipts ?? [],
                stored.LastSuccessfulExportAt ?? exportCheckpoint?.ExportedAt,
                exportCheckpoint,
                stored.HostedSync);
        }
        catch (JsonException ex)
        {
            throw new BrowserLogbookStoreException("Stored logbook state is not valid JSON.", ex);
        }
    }

    public async ValueTask<BrowserLogbookStateV2?> LoadStateV2Async()
    {
        var json = await jsRuntime.InvokeAsync<string?>("electronicLogbookStore.load", DocumentKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var parsed = JsonDocument.Parse(json);
            if (!parsed.RootElement.TryGetProperty("storeVersion", out _))
            {
                return new BrowserLogbookStateV2(ReadDocumentV2(json), [], null);
            }

            var stored = JsonSerializer.Deserialize<BrowserLogbookStoredDocument>(json, PortableLogbookJson.SerializerOptions)
                ?? throw new BrowserLogbookStoreException("Stored logbook state is invalid.");
            if (stored.StoreVersion != CurrentStoreVersion)
            {
                throw new BrowserLogbookStoreException(
                    $"Stored logbook state version {stored.StoreVersion} is not supported by this app.");
            }

            if (stored.SchemaVersion < PortableLogbookDocumentV2.CurrentSchemaVersion)
            {
                throw new BrowserLogbookStoreException(
                    "Stored portable schema version is from the legacy mobile format. " +
                    "Re-import the authoritative workbook to create a workbook-faithful mobile logbook.");
            }

            if (stored.SchemaVersion > PortableLogbookDocumentV2.CurrentSchemaVersion)
            {
                throw new BrowserLogbookStoreException(
                    $"Stored portable schema version {stored.SchemaVersion} is newer than this app supports.");
            }

            var document = ReadDocumentV2(stored.DocumentJson);
            if (document.SchemaVersion != stored.SchemaVersion)
            {
                throw new BrowserLogbookStoreException("Stored logbook state schema metadata does not match the document.");
            }

            var exportCheckpoint = stored.LastSuccessfulExport?.Covers(document) == true
                ? stored.LastSuccessfulExport
                : null;
            return new BrowserLogbookStateV2(
                document,
                stored.ImportReceipts ?? [],
                stored.LastSuccessfulExportAt ?? exportCheckpoint?.ExportedAt,
                exportCheckpoint,
                stored.HostedSync);
        }
        catch (JsonException ex)
        {
            throw new BrowserLogbookStoreException("Stored logbook state is not valid JSON.", ex);
        }
    }

    public async ValueTask SaveDocumentAsync(PortableLogbookDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        EnsureSaveableDocument(document);
        var existing = await LoadStateAsync();
        var existingExportCheckpoint = existing?.LastSuccessfulExport?.Covers(document) == true
            ? existing.LastSuccessfulExport
            : null;
        await SaveStateAsync(new BrowserLogbookState(
            document,
            existing?.ImportReceipts ?? [],
            existing?.LastSuccessfulExportAt,
            existingExportCheckpoint,
            existing?.HostedSync));
    }

    public async ValueTask SaveDocumentAsync(PortableLogbookDocumentV2 document)
    {
        ArgumentNullException.ThrowIfNull(document);
        EnsureSaveableDocument(document);
        var existing = await LoadStateV2Async();
        var existingExportCheckpoint = existing?.LastSuccessfulExport?.Covers(document) == true
            ? existing.LastSuccessfulExport
            : null;
        await SaveStateAsync(new BrowserLogbookStateV2(
            document,
            existing?.ImportReceipts ?? [],
            existing?.LastSuccessfulExportAt,
            existingExportCheckpoint,
            existing?.HostedSync));
    }

    public async ValueTask SaveStateAsync(BrowserLogbookState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var document = state.Document;
        ArgumentNullException.ThrowIfNull(document);
        EnsureSaveableDocument(document);
        var existing = await LoadStateAsync();
        EnsureSchemaUpgradeHasBackup(existing, document);
        var exportCheckpoint = state.LastSuccessfulExport?.Covers(document) == true
            ? state.LastSuccessfulExport
            : null;
        var hostedSync = state.HostedSync ?? existing?.HostedSync;

        var stored = new BrowserLogbookStoredDocument(
            CurrentStoreVersion,
            document.SchemaVersion,
            PortableLogbookJson.Serialize(document),
            state.ImportReceipts,
            state.LastSuccessfulExportAt ?? exportCheckpoint?.ExportedAt,
            exportCheckpoint,
            hostedSync);
        await jsRuntime.InvokeVoidAsync(
            "electronicLogbookStore.save",
            DocumentKey,
            JsonSerializer.Serialize(stored, PortableLogbookJson.SerializerOptions));
    }

    public async ValueTask SaveStateAsync(BrowserLogbookStateV2 state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var document = state.Document;
        ArgumentNullException.ThrowIfNull(document);
        EnsureSaveableDocument(document);
        var existing = await LoadStateV2Async();
        EnsureSchemaUpgradeHasBackup(existing, document);
        var exportCheckpoint = state.LastSuccessfulExport?.Covers(document) == true
            ? state.LastSuccessfulExport
            : null;
        var hostedSync = state.HostedSync ?? existing?.HostedSync;

        var stored = new BrowserLogbookStoredDocument(
            CurrentStoreVersion,
            document.SchemaVersion,
            PortableLogbookJson.SerializeV2(document),
            state.ImportReceipts,
            state.LastSuccessfulExportAt ?? exportCheckpoint?.ExportedAt,
            exportCheckpoint,
            hostedSync);
        await jsRuntime.InvokeVoidAsync(
            "electronicLogbookStore.save",
            DocumentKey,
            JsonSerializer.Serialize(stored, PortableLogbookJson.SerializerOptions));
    }

    public async ValueTask RunDisposableProbeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = DiagnosticProbeKeyPrefix + Guid.NewGuid().ToString("N");
        var value = Guid.NewGuid().ToString("N");
        try
        {
            await jsRuntime.InvokeVoidAsync("electronicLogbookStore.save", cancellationToken, key, value);
            var reloaded = await jsRuntime.InvokeAsync<string?>("electronicLogbookStore.load", cancellationToken, key);
            if (!string.Equals(value, reloaded, StringComparison.Ordinal))
            {
                throw new MobileHostedDiagnosticException("INDEXEDDB_READBACK_MISMATCH", "The disposable IndexedDB value did not match on readback.");
            }
        }
        finally
        {
            await jsRuntime.InvokeVoidAsync("electronicLogbookStore.delete", CancellationToken.None, key);
        }
    }

    public async ValueTask RestoreStateV2Async(BrowserLogbookStateV2? previous)
    {
        if (previous is null)
        {
            await jsRuntime.InvokeVoidAsync("electronicLogbookStore.delete", DocumentKey);
            return;
        }

        var document = previous.Document;
        var exportCheckpoint = previous.LastSuccessfulExport?.Covers(document) == true
            ? previous.LastSuccessfulExport
            : null;
        var stored = new BrowserLogbookStoredDocument(
            CurrentStoreVersion,
            document.SchemaVersion,
            PortableLogbookJson.SerializeV2(document),
            previous.ImportReceipts,
            previous.LastSuccessfulExportAt ?? exportCheckpoint?.ExportedAt,
            exportCheckpoint,
            previous.HostedSync);
        await jsRuntime.InvokeVoidAsync(
            "electronicLogbookStore.save",
            DocumentKey,
            JsonSerializer.Serialize(stored, PortableLogbookJson.SerializerOptions));
    }

    public async ValueTask<IReadOnlyList<MobileConnectionDiagnosticReport>> LoadConnectionDiagnosticsAsync()
    {
        var json = await jsRuntime.InvokeAsync<string?>("electronicLogbookStore.load", ConnectionDiagnosticsKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<MobileConnectionDiagnosticReport[]>(json, PortableLogbookJson.SerializerOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public async ValueTask AppendConnectionDiagnosticsAsync(MobileConnectionDiagnosticReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var history = (await LoadConnectionDiagnosticsAsync())
            .Where(existing => !string.Equals(existing.AttemptId, report.AttemptId, StringComparison.Ordinal))
            .Append(report)
            .OrderByDescending(existing => existing.AttemptedAt)
            .Take(20)
            .ToArray();
        await jsRuntime.InvokeVoidAsync(
            "electronicLogbookStore.save",
            ConnectionDiagnosticsKey,
            JsonSerializer.Serialize(history, PortableLogbookJson.SerializerOptions));
    }

    public async ValueTask RecordImportReceiptAsync(PortableLogbookPackageReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var state = await LoadStateAsync()
            ?? throw new BrowserLogbookStoreException("Cannot record an import receipt before a portable document exists.");
        var receipts = state.ImportReceipts
            .Where(existing => !string.Equals(existing.PackageSha256, receipt.PackageSha256, StringComparison.OrdinalIgnoreCase))
            .Concat([receipt])
            .ToArray();
        await SaveStateAsync(state with { ImportReceipts = receipts });
    }

    public async ValueTask RecordSuccessfulExportAsync(DateTimeOffset exportedAt)
    {
        var state = await LoadStateAsync()
            ?? throw new BrowserLogbookStoreException("Cannot record an export before a portable document exists.");
        await SaveStateAsync(state with { LastSuccessfulExportAt = exportedAt });
    }

    public async ValueTask RecordSuccessfulExportAsync(MobilePackageExportWorkflowResult export)
    {
        ArgumentNullException.ThrowIfNull(export);
        var state = await LoadStateAsync()
            ?? throw new BrowserLogbookStoreException("Cannot record an export before a portable document exists.");
        await SaveStateAsync(state with
        {
            LastSuccessfulExportAt = export.ExportedAt,
            LastSuccessfulExport = BrowserLogbookExportCheckpoint.Create(state.Document, export)
        });
    }

    private static PortableLogbookDocument ReadDocument(string json)
    {
        var document = PortableLogbookJson.Deserialize(json)
            ?? throw new BrowserLogbookStoreException("Stored portable logbook document is invalid.");
        if (document.SchemaVersion > PortableLogbookDocument.CurrentSchemaVersion)
        {
            throw new BrowserLogbookStoreException(
                $"Stored portable schema version {document.SchemaVersion} is newer than this app supports.");
        }

        return document;
    }

    private static PortableLogbookDocumentV2 ReadDocumentV2(string json)
    {
        var document = PortableLogbookJson.DeserializeV2(json)
            ?? throw new BrowserLogbookStoreException("Stored portable logbook document is invalid.");
        if (document.SchemaVersion < PortableLogbookDocumentV2.CurrentSchemaVersion)
        {
            throw new BrowserLogbookStoreException(
                "Stored portable schema version is from the legacy mobile format. " +
                "Re-import the authoritative workbook to create a workbook-faithful mobile logbook.");
        }

        if (document.SchemaVersion > PortableLogbookDocumentV2.CurrentSchemaVersion)
        {
            throw new BrowserLogbookStoreException(
                $"Stored portable schema version {document.SchemaVersion} is newer than this app supports.");
        }

        return document;
    }

    private static void EnsureSaveableDocument(PortableLogbookDocument document)
    {
        if (document.SchemaVersion != PortableLogbookDocument.CurrentSchemaVersion)
        {
            throw new BrowserLogbookStoreException(
                $"Portable schema version {document.SchemaVersion} cannot be saved by this app.");
        }
    }

    private static void EnsureSaveableDocument(PortableLogbookDocumentV2 document)
    {
        if (document.SchemaVersion != PortableLogbookDocumentV2.CurrentSchemaVersion)
        {
            throw new BrowserLogbookStoreException(
                $"Portable schema version {document.SchemaVersion} cannot be saved by this app.");
        }
    }

    private static void EnsureSchemaUpgradeHasBackup(BrowserLogbookState? existing, PortableLogbookDocument document)
    {
        if (existing is null || existing.Document.SchemaVersion >= document.SchemaVersion)
        {
            return;
        }

        if (existing.LastSuccessfulExport is null ||
            !existing.LastSuccessfulExport.Covers(existing.Document))
        {
            throw new BrowserLogbookStoreException(
                "Stored logbook state must be exported as a valid backup package before this app can upgrade its local schema.");
        }
    }

    private static void EnsureSchemaUpgradeHasBackup(BrowserLogbookStateV2? existing, PortableLogbookDocumentV2 document)
    {
        if (existing is null || existing.Document.SchemaVersion >= document.SchemaVersion)
        {
            return;
        }

        if (existing.LastSuccessfulExport is null ||
            !existing.LastSuccessfulExport.Covers(existing.Document))
        {
            throw new BrowserLogbookStoreException(
                "Stored logbook state must be exported as a valid backup package before this app can upgrade its local schema.");
        }
    }
}

public sealed record BrowserLogbookStoredDocument(
    int StoreVersion,
    int SchemaVersion,
    string DocumentJson,
    IReadOnlyList<PortableLogbookPackageReceipt>? ImportReceipts = null,
    DateTimeOffset? LastSuccessfulExportAt = null,
    BrowserLogbookExportCheckpoint? LastSuccessfulExport = null,
    BrowserHostedSyncState? HostedSync = null);

public sealed record BrowserLogbookState(
    PortableLogbookDocument Document,
    IReadOnlyList<PortableLogbookPackageReceipt> ImportReceipts,
    DateTimeOffset? LastSuccessfulExportAt,
    BrowserLogbookExportCheckpoint? LastSuccessfulExport = null,
    BrowserHostedSyncState? HostedSync = null);

public sealed record BrowserLogbookStateV2(
    PortableLogbookDocumentV2 Document,
    IReadOnlyList<PortableLogbookPackageReceipt> ImportReceipts,
    DateTimeOffset? LastSuccessfulExportAt,
    BrowserLogbookExportCheckpoint? LastSuccessfulExport = null,
    BrowserHostedSyncState? HostedSync = null);

public sealed record BrowserHostedSyncState(
    HostedAccountId AccountId,
    LogbookId LogbookId,
    DeviceId DeviceId,
    long LastAcknowledgedHostedRevision,
    PortableHostedSyncStatus LastStatus,
    DateTimeOffset? LastAttemptedAt = null,
    DateTimeOffset? LastSyncedAt = null,
    string? AttentionRequiredReason = null,
    int PendingLocalOperationCount = 0,
    IReadOnlyList<RevisionId>? UploadedRevisionIds = null,
    int LedgerCursorVersion = 0)
{
    public const int CurrentLedgerCursorVersion = 1;

    public BrowserHostedSyncState WithResult(PortableHostedSyncResult result, DateTimeOffset attemptedAt)
    {
        ArgumentNullException.ThrowIfNull(result);
        var uploadedRevisionIds = (UploadedRevisionIds ?? [])
            .Concat(result.UploadedRevisionIds ?? [])
            .Distinct()
            .OrderBy(revisionId => revisionId.Value, StringComparer.Ordinal)
            .ToArray();
        return this with
        {
            LastAcknowledgedHostedRevision = result.LastAcknowledgedHostedRevision,
            LastStatus = result.Status,
            LastAttemptedAt = attemptedAt,
            LastSyncedAt = result.Status == PortableHostedSyncStatus.Synced ? attemptedAt : LastSyncedAt,
            AttentionRequiredReason = result.AttentionRequiredReason,
            PendingLocalOperationCount = result.PendingLocalOperationCount,
            UploadedRevisionIds = uploadedRevisionIds,
            LedgerCursorVersion = result.Status is PortableHostedSyncStatus.Synced or PortableHostedSyncStatus.Waiting
                ? CurrentLedgerCursorVersion
                : LedgerCursorVersion
        };
    }
}

public sealed record BrowserLogbookExportCheckpoint(
    DateTimeOffset ExportedAt,
    int SchemaVersion,
    LogbookId LogbookId,
    int OperationCount,
    DateTimeOffset? LatestOperationCreatedAt,
    string PackageSha256)
{
    public static BrowserLogbookExportCheckpoint Create(
        PortableLogbookDocument document,
        MobilePackageExportWorkflowResult export)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(export);
        return new BrowserLogbookExportCheckpoint(
            export.ExportedAt,
            document.SchemaVersion,
            document.LogbookId,
            document.Operations.Count,
            LatestOperationCreatedAtFor(document),
            export.PackageSha256);
    }

    public static BrowserLogbookExportCheckpoint Create(
        PortableLogbookDocumentV2 document,
        MobilePackageExportWorkflowResult export)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(export);
        return new BrowserLogbookExportCheckpoint(
            export.ExportedAt,
            document.SchemaVersion,
            document.LogbookId,
            document.Operations.Count,
            LatestOperationCreatedAtFor(document),
            export.PackageSha256);
    }

    public bool Covers(PortableLogbookDocument document) =>
        SchemaVersion == document.SchemaVersion &&
        LogbookId == document.LogbookId &&
        OperationCount >= document.Operations.Count &&
        LatestOperationCreatedAt == LatestOperationCreatedAtFor(document) &&
        PackageSha256.Length == 64 &&
        PackageSha256.All(Uri.IsHexDigit);

    public bool Covers(PortableLogbookDocumentV2 document) =>
        SchemaVersion == document.SchemaVersion &&
        LogbookId == document.LogbookId &&
        OperationCount >= document.Operations.Count &&
        LatestOperationCreatedAt == LatestOperationCreatedAtFor(document) &&
        PackageSha256.Length == 64 &&
        PackageSha256.All(Uri.IsHexDigit);

    private static DateTimeOffset? LatestOperationCreatedAtFor(PortableLogbookDocument document) =>
        document.Operations.Count == 0
            ? null
            : document.Operations.Max(operation => operation.CreatedAt);

    private static DateTimeOffset? LatestOperationCreatedAtFor(PortableLogbookDocumentV2 document) =>
        document.Operations.Count == 0
            ? null
            : document.Operations.Max(operation => operation.CreatedAt);
}

public sealed class BrowserLogbookStoreException(string message, Exception? innerException = null)
    : Exception(message, innerException);
