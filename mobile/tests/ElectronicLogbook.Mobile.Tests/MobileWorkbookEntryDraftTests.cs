using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileWorkbookEntryDraftTests
{
    private static readonly CustomFieldDefinition[] CustomFields =
    [
        new(new CustomFieldId("cf_workbook_1"), "Custom 1", 1),
        new(new CustomFieldId("cf_workbook_2"), "Custom 2", 2),
        new(new CustomFieldId("cf_workbook_3"), "Custom 3", 3),
        new(new CustomFieldId("cf_workbook_4"), "Custom 4", 4)
    ];

    [Fact]
    public void ToEntryWritesWorkbookCanonicalFieldsWithoutCollapsedMobileFields()
    {
        var draft = MobileWorkbookEntryDraft.Create(CustomFields);
        draft.Date = new DateOnly(2026, 7, 24);
        draft.Type = " C172 ";
        draft.Reg = " VH-ABC ";
        draft.FlightId = " AD123 ";
        draft.Pic = " Alex ";
        draft.OtherPilotOrCrew = " Jamie ";
        draft.From = " YSBK ";
        draft.To = " YSCN ";
        draft.Via = " YWOL ";
        draft.Remarks = " Training ";
        draft.FlightReview = true;
        draft.InstrumentProficiencyCheck = true;
        draft.OperatorProficiencyCheck = false;
        draft.CustomValues[CustomFields[0].Id] = "Client";
        draft.CustomValues[CustomFields[1].Id] = "  ";
        draft.SeIcusDay = 0.1m;
        draft.SeIcusNight = 0.2m;
        draft.SeDualDay = 0.3m;
        draft.SeDualNight = 0.4m;
        draft.SeCommandDay = 0.5m;
        draft.SeCommandNight = 0.6m;
        draft.MeIcusDay = 0.7m;
        draft.MeIcusNight = 0.8m;
        draft.MeDualDay = 0.9m;
        draft.MeDualNight = 1.0m;
        draft.MeCommandDay = 1.1m;
        draft.MeCommandNight = 1.2m;
        draft.CopilotDay = 1.3m;
        draft.CopilotNight = 1.4m;
        draft.IfrIf = 1.5m;
        draft.IfrSim = 1.6m;
        draft.LandingsDay = 2;
        draft.LandingsNight = 3;
        draft.Ils = 4;
        draft.Vor = 5;
        draft.Rnp = 6;
        draft.Ndb = 7;
        draft.DgaCdi = 8;
        draft.DgaAzi = 9;
        draft.Circling = 10;

        var entry = draft.ToEntry(CustomFields);
        var values = PortableLogbookWorkbookEntryFields.ToFieldValues(entry);

        Assert.Equal(44, values.Count);
        Assert.Equal(new DateOnly(2026, 7, 24), entry.Date);
        Assert.Equal("C172", entry.Type);
        Assert.Equal("VH-ABC", entry.Reg);
        Assert.Equal("AD123", entry.FlightId);
        Assert.Equal("Alex", entry.Pic);
        Assert.Equal("Jamie", entry.OtherPilotOrCrew);
        Assert.Equal("YSBK", entry.From);
        Assert.Equal("YSCN", entry.To);
        Assert.Equal("YWOL", entry.Via);
        Assert.Equal("Training", entry.Remarks);
        Assert.True(entry.FlightReview);
        Assert.True(entry.InstrumentProficiencyCheck);
        Assert.False(entry.OperatorProficiencyCheck);
        Assert.Equal("Client", entry.CustomFields[CustomFields[0].Id]);
        Assert.DoesNotContain(CustomFields[1].Id, entry.CustomFields.Keys);
        Assert.Equal(10.5m, draft.TotalHours);
        Assert.Equal(49, draft.TotalApproaches);
        Assert.Equal(1.5m, entry.IfrIf);
        Assert.Equal(1.6m, entry.IfrSim);
        Assert.Equal(10, entry.Circling);
        Assert.DoesNotContain(values.Keys, key => key is "aircraftType" or "pilotInCommand" or "dual" or "holding");
    }

    [Fact]
    public void FromEntryPreservesEveryWorkbookEditableField()
    {
        var source = WorkbookEntry();

        var draft = MobileWorkbookEntryDraft.FromEntry(source, CustomFields, preserveDate: true);
        var roundTripped = draft.ToEntry(CustomFields);

        Assert.Equal(
            PortableLogbookWorkbookEntryFields.ToFieldValues(source),
            PortableLogbookWorkbookEntryFields.ToFieldValues(roundTripped));
    }

    private static PortableLogbookWorkbookEntry WorkbookEntry() =>
        PortableLogbookWorkbookEntry.Empty with
        {
            Year = 2026,
            Month = 7,
            Day = 24,
            Type = "C172",
            Reg = "VH-ABC",
            FlightId = "AD123",
            Pic = "Alex",
            OtherPilotOrCrew = "Jamie",
            From = "YSBK",
            To = "YSCN",
            Via = "YWOL",
            Remarks = "Training",
            FlightReview = true,
            InstrumentProficiencyCheck = false,
            OperatorProficiencyCheck = true,
            CustomFields = new Dictionary<CustomFieldId, string?> { [CustomFields[2].Id] = "Survey" },
            SeIcusDay = 0.1m,
            SeIcusNight = 0.2m,
            SeDualDay = 0.3m,
            SeDualNight = 0.4m,
            SeCommandDay = 0.5m,
            SeCommandNight = 0.6m,
            MeIcusDay = 0.7m,
            MeIcusNight = 0.8m,
            MeDualDay = 0.9m,
            MeDualNight = 1.0m,
            MeCommandDay = 1.1m,
            MeCommandNight = 1.2m,
            CopilotDay = 1.3m,
            CopilotNight = 1.4m,
            IfrIf = 1.5m,
            IfrSim = 1.6m,
            LandingsDay = 2,
            LandingsNight = 3,
            Ils = 4,
            Vor = 5,
            Rnp = 6,
            Ndb = 7,
            DgaCdi = 8,
            DgaAzi = 9,
            Circling = 10
        };
}
