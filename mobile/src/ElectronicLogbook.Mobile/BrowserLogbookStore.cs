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
        var json = await jsRuntime.InvokeAsync<string?>("electronicLogbookStore.load", DocumentKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var parsed = JsonDocument.Parse(json);
        if (!parsed.RootElement.TryGetProperty("storeVersion", out _))
        {
            return ReadDocument(json);
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

        return document;
    }

    public async ValueTask SaveDocumentAsync(PortableLogbookDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != PortableLogbookDocument.CurrentSchemaVersion)
        {
            throw new BrowserLogbookStoreException(
                $"Portable schema version {document.SchemaVersion} cannot be saved by this app.");
        }

        var stored = new BrowserLogbookStoredDocument(
            CurrentStoreVersion,
            document.SchemaVersion,
            PortableLogbookJson.Serialize(document));
        await jsRuntime.InvokeVoidAsync(
            "electronicLogbookStore.save",
            DocumentKey,
            JsonSerializer.Serialize(stored, PortableLogbookJson.SerializerOptions));
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
}

public sealed record BrowserLogbookStoredDocument(
    int StoreVersion,
    int SchemaVersion,
    string DocumentJson);

public sealed class BrowserLogbookStoreException(string message) : Exception(message);
