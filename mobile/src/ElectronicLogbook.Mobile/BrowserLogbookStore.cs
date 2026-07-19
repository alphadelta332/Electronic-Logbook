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

        return new BrowserLogbookState(
            document,
            stored.ImportReceipts ?? [],
            stored.LastSuccessfulExportAt);
    }

    public async ValueTask SaveDocumentAsync(PortableLogbookDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        EnsureSaveableDocument(document);
        var existing = await LoadStateAsync();
        await SaveStateAsync(new BrowserLogbookState(
            document,
            existing?.ImportReceipts ?? [],
            existing?.LastSuccessfulExportAt));
    }

    public async ValueTask SaveStateAsync(BrowserLogbookState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var document = state.Document;
        ArgumentNullException.ThrowIfNull(document);
        EnsureSaveableDocument(document);

        var stored = new BrowserLogbookStoredDocument(
            CurrentStoreVersion,
            document.SchemaVersion,
            PortableLogbookJson.Serialize(document),
            state.ImportReceipts,
            state.LastSuccessfulExportAt);
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
}

public sealed record BrowserLogbookStoredDocument(
    int StoreVersion,
    int SchemaVersion,
    string DocumentJson,
    IReadOnlyList<PortableLogbookPackageReceipt>? ImportReceipts = null,
    DateTimeOffset? LastSuccessfulExportAt = null);

public sealed record BrowserLogbookState(
    PortableLogbookDocument Document,
    IReadOnlyList<PortableLogbookPackageReceipt> ImportReceipts,
    DateTimeOffset? LastSuccessfulExportAt);

public sealed class BrowserLogbookStoreException(string message) : Exception(message);
