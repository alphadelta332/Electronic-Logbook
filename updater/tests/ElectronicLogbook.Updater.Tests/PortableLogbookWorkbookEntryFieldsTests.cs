namespace ElectronicLogbook.Updater.Tests;

using ElectronicLogbook.Portable;

public sealed class PortableLogbookWorkbookEntryFieldsTests
{
    [Fact]
    public void ToFieldValuesExportsWorkbookFaithfulFieldIdsInCatalogOrder()
    {
        var customFieldId = new CustomFieldId("cf_workbook_2");
        var entry = PortableLogbookWorkbookEntry.Empty with
        {
            Year = 2026,
            Month = 7,
            Day = 24,
            Type = "DA40",
            Reg = "VH-ABC",
            FlightId = "ELB24",
            Pic = "A Delta",
            FlightReview = true,
            InstrumentProficiencyCheck = false,
            OperatorProficiencyCheck = true,
            CustomFields = new Dictionary<CustomFieldId, string?> { [customFieldId] = "Training" },
            SeCommandDay = 1.2m,
            MeDualNight = 0.4m,
            CopilotDay = 0.3m,
            IfrIf = 0.5m,
            IfrSim = 0.2m,
            LandingsDay = 1,
            Ils = 2,
            DgaCdi = 1
        };

        var values = PortableLogbookWorkbookEntryFields.ToFieldValues(entry);

        Assert.Equal(PortableLogbookWorkbookFieldCatalog.PilotEnteredFields.Select(field => field.Id), values.Keys);
        Assert.Equal(2026, values["dateYear"]);
        Assert.Equal("DA40", values["type"]);
        Assert.Equal("VH-ABC", values["reg"]);
        Assert.Equal("ELB24", values["flightId"]);
        Assert.Equal(true, values["fr"]);
        Assert.Equal(false, values["ipc"]);
        Assert.Equal(true, values["opc"]);
        Assert.Equal("Training", values["custom2"]);
        Assert.Equal(1.2m, values["seCommandDay"]);
        Assert.Equal(0.4m, values["meDualNight"]);
        Assert.Equal(0.3m, values["copilotDay"]);
        Assert.Equal(0.5m, values["ifrIf"]);
        Assert.Equal(0.2m, values["ifrSim"]);
        Assert.Equal(1, values["landingsDay"]);
        Assert.Equal(2, values["ils"]);
        Assert.Equal(1, values["dgaCdi"]);
    }

    [Fact]
    public void FromFieldValuesBuildsWorkbookFaithfulEntryAndPreservesCustomSlots()
    {
        var customFields = PortableLogbookCustomFieldSet.CreateWorkbookCustomFields(
            ["Duty", "Training", "Notes", "Billing"]);
        var values = new Dictionary<string, object?>
        {
            ["dateYear"] = 2026,
            ["dateMonth"] = 7,
            ["dateDay"] = 24,
            ["type"] = "DA40",
            ["reg"] = "VH-ABC",
            ["flightId"] = "ELB24",
            ["pic"] = "A Delta",
            ["fr"] = true,
            ["ipc"] = false,
            ["opc"] = true,
            ["custom2"] = "IFR",
            ["seCommandDay"] = 1.2,
            ["landingsDay"] = 1,
            ["ils"] = 2
        };

        var entry = PortableLogbookWorkbookEntryFields.FromFieldValues(values, customFields);

        Assert.Equal(new DateOnly(2026, 7, 24), entry.Date);
        Assert.Equal("DA40", entry.Type);
        Assert.Equal("VH-ABC", entry.Reg);
        Assert.Equal("ELB24", entry.FlightId);
        Assert.Equal("A Delta", entry.Pic);
        Assert.True(entry.FlightReview);
        Assert.False(entry.InstrumentProficiencyCheck);
        Assert.True(entry.OperatorProficiencyCheck);
        Assert.Equal("IFR", entry.CustomFields[new CustomFieldId("cf_workbook_2")]);
        Assert.Equal(1.2m, entry.SeCommandDay);
        Assert.Equal(1, entry.LandingsDay);
        Assert.Equal(2, entry.Ils);
    }

    [Fact]
    public void FromFieldValuesRejectsAbandonedV1CollapsedFieldIds()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            PortableLogbookWorkbookEntryFields.FromFieldValues(
                new Dictionary<string, object?> { ["pilotInCommand"] = 1.2m },
                PortableLogbookCustomFieldSet.CreateWorkbookCustomFields(["A", "B", "C", "D"])));

        Assert.Equal("values", exception.ParamName);
    }
}
