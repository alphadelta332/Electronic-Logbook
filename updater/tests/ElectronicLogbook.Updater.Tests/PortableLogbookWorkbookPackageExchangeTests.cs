using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookWorkbookPackageExchangeTests
{
    [Fact]
    public void PureWorkbookPackageWorkbookRoundTripPreservesFieldsAndStableRowIds()
    {
        var customField = new CustomFieldDefinition(new CustomFieldId("cf_workbook_1"), "Role", 1);
        var setup = PortableLogbookSetup.CreateInitialSetupPlan(
            [Entry("VH-ABC", customField.Id, "PICUS")],
            [customField],
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            new LogbookId("log_workbook_exchange"),
            new DeviceId("dev_workbook_a"),
            PortableLogbookKey.Generate(),
            new PortableLogbookIdFactory(
                () => new EntryId("ent_1"),
                () => new RevisionId("rev_1")));

        var replacementWorkbook = PortableLogbookDocument.CreateAustraliaFirst(setup.LogbookId, [customField], []);
        var imported = PortableLogbookPackageImport.ImportPackage(
            replacementWorkbook,
            setup.InitialPackageBytes,
            setup.Key,
            [],
            DateTimeOffset.Parse("2026-07-18T00:10:00Z"));

        Assert.Equal(PortableLogbookPackageImportStatus.Applied, imported.Status);
        var importedRow = Assert.Single(imported.WorkbookRows!);
        Assert.Equal(new EntryId("ent_1"), importedRow.EntryId);
        Assert.Equal(new RevisionId("rev_1"), importedRow.CurrentRevisionId);
        Assert.Equal("VH-ABC", importedRow.Entry.Registration);
        Assert.Equal("PICUS", importedRow.Entry.CustomFields[customField.Id]);

        var exported = PortableLogbookPackageExport.ExportPackage(
            imported.Document,
            PortableLogbookMerger.Merge(imported.Document.Operations).Entries.Values,
            imported.WorkbookRows!,
            new DeviceId("dev_workbook_b"),
            setup.Key,
            imported.ImportReceipts,
            DateTimeOffset.Parse("2026-07-18T00:20:00Z"));
        var readBack = PortableLogbookPackage.Read(exported.PackageBytes, setup.Key, setup.LogbookId);
        var final = PortableLogbookMerger.Merge(readBack.Document.Operations);
        var current = Assert.Single(final.Entries.Values);

        Assert.Equal(imported.Document.Operations.Select(operation => operation.RevisionId), readBack.Document.Operations.Select(operation => operation.RevisionId));
        Assert.Equal(new EntryId("ent_1"), current.EntryId);
        Assert.Equal(new RevisionId("rev_1"), current.CurrentRevisionId);
        Assert.False(current.IsDeleted);
        AssertEntriesEqual(Entry("VH-ABC", customField.Id, "PICUS"), current.Entry!);
        var exportedRow = Assert.Single(exported.WorkbookRows);
        Assert.Equal(importedRow.EntryId, exportedRow.EntryId);
        Assert.Equal(importedRow.CurrentRevisionId, exportedRow.CurrentRevisionId);
    }

    private static void AssertEntriesEqual(PortableLogbookEntry expected, PortableLogbookEntry actual)
    {
        Assert.Equal(expected.Date, actual.Date);
        Assert.Equal(expected.AircraftType, actual.AircraftType);
        Assert.Equal(expected.Registration, actual.Registration);
        Assert.Equal(expected.FlightNumber, actual.FlightNumber);
        Assert.Equal(expected.From, actual.From);
        Assert.Equal(expected.To, actual.To);
        Assert.Equal(expected.Route, actual.Route);
        Assert.Equal(expected.Details, actual.Details);
        Assert.Equal(expected.MultiPilot, actual.MultiPilot);
        Assert.Equal(expected.PilotInCommand, actual.PilotInCommand);
        Assert.Equal(expected.CoPilot, actual.CoPilot);
        Assert.Equal(expected.Dual, actual.Dual);
        Assert.Equal(expected.Instructor, actual.Instructor);
        Assert.Equal(expected.Day, actual.Day);
        Assert.Equal(expected.Night, actual.Night);
        Assert.Equal(expected.InstrumentActual, actual.InstrumentActual);
        Assert.Equal(expected.InstrumentSimulated, actual.InstrumentSimulated);
        Assert.Equal(expected.TakeoffsDay, actual.TakeoffsDay);
        Assert.Equal(expected.TakeoffsNight, actual.TakeoffsNight);
        Assert.Equal(expected.LandingsDay, actual.LandingsDay);
        Assert.Equal(expected.LandingsNight, actual.LandingsNight);
        Assert.Equal(expected.IfrApproaches, actual.IfrApproaches);
        Assert.Equal(expected.Holding, actual.Holding);
        Assert.Equal(expected.Rnav, actual.Rnav);
        Assert.Equal(expected.Circling, actual.Circling);
        Assert.Equal(expected.CustomFields, actual.CustomFields);
    }

    private static PortableLogbookEntry Entry(
        string registration,
        CustomFieldId customFieldId,
        string customFieldValue) =>
        PortableLogbookEntry.Empty with
        {
            Date = new DateOnly(2026, 7, 18),
            AircraftType = "C172",
            Registration = registration,
            FlightNumber = "EL123",
            From = "YSBK",
            To = "YSCN",
            Route = "YSBK YSCN",
            Details = "Training details",
            MultiPilot = 0.1m,
            PilotInCommand = 1.2m,
            CoPilot = 0.2m,
            Dual = 0.3m,
            Instructor = 0.4m,
            Day = 1.0m,
            Night = 0.2m,
            InstrumentActual = 0.1m,
            InstrumentSimulated = 0.2m,
            TakeoffsDay = 1,
            TakeoffsNight = 2,
            LandingsDay = 3,
            LandingsNight = 4,
            IfrApproaches = 5,
            Holding = 6,
            Rnav = 7,
            Circling = 8,
            CustomFields = new Dictionary<CustomFieldId, string?>
            {
                [customFieldId] = customFieldValue
            }
        };
}
