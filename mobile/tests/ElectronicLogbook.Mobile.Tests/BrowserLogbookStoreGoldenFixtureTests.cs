using System.Text.Json;
using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;
using Microsoft.JSInterop;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class BrowserLogbookStoreGoldenFixtureTests
{
    [Fact]
    public async Task LoadDocumentAsyncCanReadGoldenFixtureFromVersionedBrowserEnvelope()
    {
        var fixtureJson = ReadGoldenFixture();
        var stored = new BrowserLogbookStoredDocument(
            1,
            PortableLogbookDocument.CurrentSchemaVersion,
            fixtureJson);
        var jsRuntime = new MemoryJsRuntime
        {
            StoredJson = JsonSerializer.Serialize(stored, PortableLogbookJson.SerializerOptions)
        };
        var store = new BrowserLogbookStore(jsRuntime);

        var document = await store.LoadDocumentAsync();

        Assert.NotNull(document);
        AssertGoldenFixtureDocument(document);
    }

    [Fact]
    public async Task SaveDocumentAsyncPersistsGoldenFixtureInVersionedBrowserEnvelope()
    {
        var fixtureJson = ReadGoldenFixture();
        var document = PortableLogbookJson.Deserialize(fixtureJson)
            ?? throw new InvalidOperationException("Golden fixture did not deserialize.");
        var jsRuntime = new MemoryJsRuntime();
        var store = new BrowserLogbookStore(jsRuntime);

        await store.SaveDocumentAsync(document);

        var storedJson = Assert.IsType<string>(jsRuntime.StoredJson);
        var stored = JsonSerializer.Deserialize<BrowserLogbookStoredDocument>(
            storedJson,
            PortableLogbookJson.SerializerOptions);
        Assert.NotNull(stored);
        Assert.Equal(1, stored.StoreVersion);
        Assert.Equal(PortableLogbookDocument.CurrentSchemaVersion, stored.SchemaVersion);
        Assert.Equal(Normalize(PortableLogbookJson.Serialize(document)), Normalize(stored.DocumentJson));

        jsRuntime.StoredJson = JsonSerializer.Serialize(stored, PortableLogbookJson.SerializerOptions);
        AssertGoldenFixtureDocument(await store.LoadDocumentAsync());
    }

    [Fact]
    public async Task SaveAndLoadStateRoundTripsExchangeReceiptsAndLastExport()
    {
        var document = PortableLogbookJson.Deserialize(ReadGoldenFixture())
            ?? throw new InvalidOperationException("Golden fixture did not deserialize.");
        var receipt = new PortableLogbookPackageReceipt(
            "ABC123",
            document.LogbookId,
            document.Operations.Count,
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-19T00:00:00Z"));
        var lastExport = DateTimeOffset.Parse("2026-07-19T01:00:00Z");
        var jsRuntime = new MemoryJsRuntime();
        var store = new BrowserLogbookStore(jsRuntime);

        await store.SaveStateAsync(new BrowserLogbookState(document, [receipt], lastExport));

        var state = await store.LoadStateAsync();

        Assert.NotNull(state);
        AssertGoldenFixtureDocument(state.Document);
        Assert.Equal([receipt], state.ImportReceipts);
        Assert.Equal(lastExport, state.LastSuccessfulExportAt);
    }

    [Fact]
    public async Task SaveDocumentAsyncPreservesExistingExchangeMetadata()
    {
        var document = PortableLogbookJson.Deserialize(ReadGoldenFixture())
            ?? throw new InvalidOperationException("Golden fixture did not deserialize.");
        var receipt = new PortableLogbookPackageReceipt(
            "ABC123",
            document.LogbookId,
            document.Operations.Count,
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-19T00:00:00Z"));
        var lastExport = DateTimeOffset.Parse("2026-07-19T01:00:00Z");
        var jsRuntime = new MemoryJsRuntime();
        var store = new BrowserLogbookStore(jsRuntime);
        await store.SaveStateAsync(new BrowserLogbookState(document, [receipt], lastExport));

        await store.SaveDocumentAsync(document with
        {
            Operations = document.Operations.Concat([
                new CorrectEntryOperation(
                    document.LogbookId,
                    new EntryId("ent_fixture"),
                    new RevisionId("rev_mobile"),
                    new HashSet<RevisionId> { new("rev_create") },
                    new DeviceId("dev_mobile"),
                    DateTimeOffset.Parse("2026-07-19T02:00:00Z"),
                    ((CreateEntryOperation)Assert.Single(document.Operations)).Entry with { Details = "Mobile correction" })
            ]).ToArray()
        });

        var state = await store.LoadStateAsync();

        Assert.NotNull(state);
        Assert.Equal([receipt], state.ImportReceipts);
        Assert.Equal(lastExport, state.LastSuccessfulExportAt);
        Assert.Equal(2, state.Document.Operations.Count);
    }

    [Fact]
    public async Task RecordImportReceiptAsyncDeduplicatesByPackageHash()
    {
        var document = PortableLogbookJson.Deserialize(ReadGoldenFixture())
            ?? throw new InvalidOperationException("Golden fixture did not deserialize.");
        var first = new PortableLogbookPackageReceipt(
            "ABC123",
            document.LogbookId,
            document.Operations.Count,
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-19T00:00:00Z"));
        var replacement = first with
        {
            PackageSha256 = "abc123",
            ImportedAt = DateTimeOffset.Parse("2026-07-19T01:00:00Z")
        };
        var store = new BrowserLogbookStore(new MemoryJsRuntime());
        await store.SaveStateAsync(new BrowserLogbookState(document, [first], null));

        await store.RecordImportReceiptAsync(replacement);

        var state = await store.LoadStateAsync();

        Assert.NotNull(state);
        var receipt = Assert.Single(state.ImportReceipts);
        Assert.Equal(replacement, receipt);
    }

    [Fact]
    public async Task RecordSuccessfulExportAsyncPersistsTimestampWithoutChangingDocument()
    {
        var document = PortableLogbookJson.Deserialize(ReadGoldenFixture())
            ?? throw new InvalidOperationException("Golden fixture did not deserialize.");
        var exportedAt = DateTimeOffset.Parse("2026-07-19T03:00:00Z");
        var store = new BrowserLogbookStore(new MemoryJsRuntime());
        await store.SaveStateAsync(new BrowserLogbookState(document, [], null));

        await store.RecordSuccessfulExportAsync(exportedAt);

        var state = await store.LoadStateAsync();

        Assert.NotNull(state);
        Assert.Equal(exportedAt, state.LastSuccessfulExportAt);
        Assert.Equal(Normalize(PortableLogbookJson.Serialize(document)), Normalize(PortableLogbookJson.Serialize(state.Document)));
    }

    [Fact]
    public async Task LoadDocumentAsyncCanReadLegacyRawGoldenFixtureJson()
    {
        var store = new BrowserLogbookStore(new MemoryJsRuntime { StoredJson = ReadGoldenFixture() });

        var document = await store.LoadDocumentAsync();

        Assert.NotNull(document);
        AssertGoldenFixtureDocument(document);
    }

    [Fact]
    public async Task LoadStateAsyncCanReadLegacyRawGoldenFixtureJsonAsDocumentOnlyState()
    {
        var store = new BrowserLogbookStore(new MemoryJsRuntime { StoredJson = ReadGoldenFixture() });

        var state = await store.LoadStateAsync();

        Assert.NotNull(state);
        AssertGoldenFixtureDocument(state.Document);
        Assert.Empty(state.ImportReceipts);
        Assert.Null(state.LastSuccessfulExportAt);
    }

    [Fact]
    public async Task LoadDocumentAsyncRejectsUnsupportedStoreVersionWithoutOverwritingState()
    {
        var originalJson = JsonSerializer.Serialize(
            new BrowserLogbookStoredDocument(
                2,
                PortableLogbookDocument.CurrentSchemaVersion,
                ReadGoldenFixture()),
            PortableLogbookJson.SerializerOptions);
        var jsRuntime = new MemoryJsRuntime { StoredJson = originalJson };
        var store = new BrowserLogbookStore(jsRuntime);

        var error = await Assert.ThrowsAsync<BrowserLogbookStoreException>(async () =>
            await store.LoadDocumentAsync());

        Assert.Contains("version 2", error.Message, StringComparison.Ordinal);
        Assert.Equal(originalJson, jsRuntime.StoredJson);
        Assert.False(jsRuntime.SaveWasCalled);
    }

    [Fact]
    public async Task LoadDocumentAsyncRejectsSchemaEnvelopeMismatchWithoutOverwritingState()
    {
        var originalJson = JsonSerializer.Serialize(
            new BrowserLogbookStoredDocument(
                1,
                0,
                ReadGoldenFixture()),
            PortableLogbookJson.SerializerOptions);
        var jsRuntime = new MemoryJsRuntime { StoredJson = originalJson };
        var store = new BrowserLogbookStore(jsRuntime);

        var error = await Assert.ThrowsAsync<BrowserLogbookStoreException>(async () =>
            await store.LoadDocumentAsync());

        Assert.Contains("schema metadata does not match", error.Message, StringComparison.Ordinal);
        Assert.Equal(originalJson, jsRuntime.StoredJson);
        Assert.False(jsRuntime.SaveWasCalled);
    }

    [Fact]
    public async Task SaveDocumentAsyncRejectsUnsupportedSchemaWithoutOverwritingState()
    {
        var originalJson = "existing-browser-state";
        var jsRuntime = new MemoryJsRuntime { StoredJson = originalJson };
        var store = new BrowserLogbookStore(jsRuntime);
        var document = PortableLogbookJson.Deserialize(ReadGoldenFixture())
            ?? throw new InvalidOperationException("Golden fixture did not deserialize.");

        var error = await Assert.ThrowsAsync<BrowserLogbookStoreException>(async () =>
            await store.SaveDocumentAsync(document with { SchemaVersion = PortableLogbookDocument.CurrentSchemaVersion + 1 }));

        Assert.Contains("cannot be saved", error.Message, StringComparison.Ordinal);
        Assert.Equal(originalJson, jsRuntime.StoredJson);
        Assert.False(jsRuntime.SaveWasCalled);
    }

    private static void AssertGoldenFixtureDocument(PortableLogbookDocument? document)
    {
        Assert.NotNull(document);
        Assert.Equal(new LogbookId("log_fixture"), document.LogbookId);
        Assert.True(PortableLogbookValidator.Validate(document).IsValid);

        var operation = Assert.IsType<CreateEntryOperation>(Assert.Single(document.Operations));
        Assert.Equal(new EntryId("ent_fixture"), operation.EntryId);
        Assert.Equal(new RevisionId("rev_create"), operation.RevisionId);
        Assert.Equal("VH-ABC", operation.Entry.Registration);
        Assert.Equal("YSBK", operation.Entry.From);
        Assert.Equal("YSCN", operation.Entry.To);
        Assert.Equal("Training", operation.Entry.CustomFields[new CustomFieldId("cf_training_kind")]);
    }

    private static string ReadGoldenFixture() =>
        File.ReadAllText(Path.Combine("Fixtures", "portable-logbook-v1.json"));

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private sealed class MemoryJsRuntime : IJSRuntime
    {
        public string? StoredJson { get; set; }

        public bool SaveWasCalled { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            return InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return identifier switch
            {
                "electronicLogbookStore.load" => new ValueTask<TValue>((TValue)(object?)StoredJson!),
                "electronicLogbookStore.save" => Save<TValue>(args),
                _ => throw new JSException($"Unexpected JS call: {identifier}")
            };
        }

        private ValueTask<TValue> Save<TValue>(object?[]? args)
        {
            Assert.NotNull(args);
            Assert.Equal("portable-document", Assert.IsType<string>(args[0]));
            SaveWasCalled = true;
            StoredJson = Assert.IsType<string>(args[1]);
            return new ValueTask<TValue>(default(TValue)!);
        }
    }
}
