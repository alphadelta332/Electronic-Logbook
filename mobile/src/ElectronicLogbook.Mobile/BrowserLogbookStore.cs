using System.Text.Json;
using ElectronicLogbook.Portable;
using Microsoft.JSInterop;

namespace ElectronicLogbook.Mobile;

public sealed class BrowserLogbookStore(IJSRuntime jsRuntime)
{
    private const int CurrentStoreVersion = 1;
    private const string DocumentKey = "portable-document";

    public async ValueTask<PortableLogbookDocument?> LoadDocumentAsync()
    {
        var state = await LoadStateAsync();
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
                exportCheckpoint);
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
            existingExportCheckpoint));
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

        var stored = new BrowserLogbookStoredDocument(
            CurrentStoreVersion,
            document.SchemaVersion,
            PortableLogbookJson.Serialize(document),
            state.ImportReceipts,
            state.LastSuccessfulExportAt ?? exportCheckpoint?.ExportedAt,
            exportCheckpoint);
        await jsRuntime.InvokeVoidAsync(
            "electronicLogbookStore.save",
            DocumentKey,
            JsonSerializer.Serialize(stored, PortableLogbookJson.SerializerOptions));
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

    private static void EnsureSaveableDocument(PortableLogbookDocument document)
    {
        if (document.SchemaVersion != PortableLogbookDocument.CurrentSchemaVersion)
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
}

public sealed record BrowserLogbookStoredDocument(
    int StoreVersion,
    int SchemaVersion,
    string DocumentJson,
    IReadOnlyList<PortableLogbookPackageReceipt>? ImportReceipts = null,
    DateTimeOffset? LastSuccessfulExportAt = null,
    BrowserLogbookExportCheckpoint? LastSuccessfulExport = null);

public sealed record BrowserLogbookState(
    PortableLogbookDocument Document,
    IReadOnlyList<PortableLogbookPackageReceipt> ImportReceipts,
    DateTimeOffset? LastSuccessfulExportAt,
    BrowserLogbookExportCheckpoint? LastSuccessfulExport = null);

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

    public bool Covers(PortableLogbookDocument document) =>
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
}

public sealed class BrowserLogbookStoreException(string message, Exception? innerException = null)
    : Exception(message, innerException);
