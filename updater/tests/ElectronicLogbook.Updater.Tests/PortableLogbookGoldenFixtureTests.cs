using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookGoldenFixtureTests
{
    [Fact]
    public void PortableSchemaV1SerializesToGoldenFixture()
    {
        var expected = File.ReadAllText(Path.Combine("Fixtures", "portable-logbook-v1.json"));
        var document = CreateGoldenDocument();

        var actual = PortableLogbookJson.Serialize(document);

        Assert.Equal(Normalize(expected), Normalize(actual));
    }

    [Fact]
    public void PortableSchemaV1GoldenFixtureDeserializesAndValidates()
    {
        var json = File.ReadAllText(Path.Combine("Fixtures", "portable-logbook-v1.json"));

        var document = PortableLogbookJson.Deserialize(json);

        Assert.NotNull(document);
        var validation = PortableLogbookValidator.Validate(document);
        Assert.True(validation.IsValid);
        Assert.IsType<CreateEntryOperation>(Assert.Single(document.Operations));
    }

    [Fact]
    public void WorkbookStorageBridgeCanOpenGoldenFixtureDocument()
    {
        var document = PortableLogbookJson.Deserialize(
            File.ReadAllText(Path.Combine("Fixtures", "portable-logbook-v1.json")))
            ?? throw new InvalidOperationException("Golden fixture did not deserialize.");
        var key = PortableLogbookKey.FromBytes(Enumerable.Range(1, PortableLogbookPackage.KeySizeBytes).Select(value => (byte)value).ToArray());
        var encryptedPackage = PortableLogbookPackage.Write(document, key);
        var envelope = PortableLogbookWorkbookStorage.CreateEnvelope(document, encryptedPackage, []);

        var serializedEnvelope = PortableLogbookWorkbookStorage.Serialize(envelope);
        var reopened = PortableLogbookWorkbookStorage.OpenEnvelope(
            PortableLogbookWorkbookStorage.Deserialize(serializedEnvelope),
            key);

        var operation = Assert.IsType<CreateEntryOperation>(Assert.Single(reopened.Document.Operations));
        Assert.Equal(new EntryId("ent_fixture"), operation.EntryId);
        Assert.Equal(new RevisionId("rev_create"), operation.RevisionId);
        Assert.Equal("VH-ABC", operation.Entry.Registration);
    }

    private static PortableLogbookDocument CreateGoldenDocument()
    {
        var logbookId = new LogbookId("log_fixture");
        var customFieldId = new CustomFieldId("cf_training_kind");
        var entry = PortableLogbookEntry.Empty with
        {
            Date = new DateOnly(2026, 7, 18),
            AircraftType = "C172",
            Registration = "VH-ABC",
            FlightNumber = "AD123",
            From = "YSBK",
            To = "YSCN",
            Route = "BK CN",
            Details = "Training",
            PilotInCommand = 1.2m,
            TakeoffsDay = 1,
            LandingsDay = 1,
            CustomFields = new Dictionary<CustomFieldId, string?> { [customFieldId] = "Training" }
        };
        var create = new CreateEntryOperation(
            logbookId,
            new EntryId("ent_fixture"),
            new RevisionId("rev_create"),
            new DeviceId("dev_excel"),
            DateTimeOffset.Parse("2026-07-18T00:00:00+00:00"),
            entry);

        return PortableLogbookDocument.CreateAustraliaFirst(
            logbookId,
            [new CustomFieldDefinition(customFieldId, "Training kind", 1)],
            [create]);
    }

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
}
