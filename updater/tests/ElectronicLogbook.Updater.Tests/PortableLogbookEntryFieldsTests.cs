using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookEntryFieldsTests
{
    [Fact]
    public void ToFieldValuesExportsStableRawFieldIds()
    {
        var entry = PortableLogbookEntry.Empty with
        {
            Date = new DateOnly(2026, 7, 18),
            AircraftType = "C172",
            Registration = "VH-ABC",
            PilotInCommand = 1.2m,
            LandingsDay = 1
        };

        var values = PortableLogbookEntryFields.ToFieldValues(entry);

        Assert.Equal(PortableLogbookFields.RawFlightFields.Select(field => field.Id), values.Keys);
        Assert.Equal(new DateOnly(2026, 7, 18), values["date"]);
        Assert.Equal("VH-ABC", values["registration"]);
        Assert.Equal(1.2m, values["pilotInCommand"]);
        Assert.Equal(1, values["landingsDay"]);
    }

    [Fact]
    public void FromFieldValuesBuildsEntryAndPreservesCustomFields()
    {
        var customFieldId = new CustomFieldId("cf_training_kind");
        var values = new Dictionary<string, object?>
        {
            ["date"] = new DateOnly(2026, 7, 18),
            ["aircraftType"] = "C172",
            ["registration"] = "VH-ABC",
            ["pilotInCommand"] = 1.2m,
            ["landingsDay"] = 1
        };

        var entry = PortableLogbookEntryFields.FromFieldValues(
            values,
            new Dictionary<CustomFieldId, string?> { [customFieldId] = "Training" });

        Assert.Equal(new DateOnly(2026, 7, 18), entry.Date);
        Assert.Equal("C172", entry.AircraftType);
        Assert.Equal("VH-ABC", entry.Registration);
        Assert.Equal(1.2m, entry.PilotInCommand);
        Assert.Equal(1, entry.LandingsDay);
        Assert.Equal("Training", Assert.Single(entry.CustomFields).Value);
    }

    [Fact]
    public void FromFieldValuesConvertsWorkbookLikeNumericValues()
    {
        var entry = PortableLogbookEntryFields.FromFieldValues(new Dictionary<string, object?>
        {
            ["pilotInCommand"] = 2,
            ["landingsDay"] = 3.0
        });

        Assert.Equal(2m, entry.PilotInCommand);
        Assert.Equal(3, entry.LandingsDay);
    }

    [Fact]
    public void FromFieldValuesRejectsUnknownFieldIds()
    {
        var exception = Assert.Throws<ArgumentException>(() => PortableLogbookEntryFields.FromFieldValues(
            new Dictionary<string, object?> { ["unknown"] = "value" }));

        Assert.Equal("values", exception.ParamName);
    }
}
