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
