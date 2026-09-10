using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class WorkbookPreMigrationInspectorTests
{
    [Fact]
    public void CreateReportsFlightCountHoursDateRangeAndGroupedActionableWarnings()
    {
        var first = Flight(new DateOnly(2020, 1, 2), 1.2m);
        var second = Flight(new DateOnly(2026, 8, 28), 0.8m) with
        {
            Type = null
        };

        var summary = WorkbookPreMigrationInspector.Create(
            [first, second],
            today: new DateOnly(2026, 8, 29));

        Assert.Equal(2, summary.FlightCount);
        Assert.Equal(2.0m, summary.LoggedHours);
        Assert.Equal("2.0", summary.LoggedHoursDisplay);
        Assert.Equal(new DateOnly(2020, 1, 2), summary.FirstFlightDate);
        Assert.Equal(new DateOnly(2026, 8, 28), summary.LastFlightDate);
        Assert.Equal("2 Jan 2020 to 28 Aug 2026", summary.DateRangeDisplay);
        var warning = Assert.Single(summary.Warnings);
        Assert.Equal(PortableLogbookEntryMessages.MissingAircraftType, warning.Message);
        Assert.Equal(1, warning.AffectedFlightCount);
        Assert.Contains(
            $"1 flight: {PortableLogbookEntryMessages.MissingAircraftType}",
            WorkbookPreMigrationInspector.FormatWarnings(summary),
            StringComparison.Ordinal);
    }

    [Fact]
    public void CreateCallsOutUnrecognizedRowsWithoutExposingTechnicalIdentifiers()
    {
        var summary = WorkbookPreMigrationInspector.Create(
            [Flight(new DateOnly(2026, 8, 28), 1.0m)],
            unrecognizedUserDataRowCount: 2,
            today: new DateOnly(2026, 8, 29));

        var text = WorkbookPreMigrationInspector.FormatWarnings(summary);

        Assert.Contains("2 workbook rows are missing", text, StringComparison.Ordinal);
        Assert.DoesNotContain("fingerprint", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("logbook id", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("entry id", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateReportsAnEmptyWorkbookClearly()
    {
        var summary = WorkbookPreMigrationInspector.Create(
            [],
            today: new DateOnly(2026, 8, 29));

        Assert.Equal(0, summary.FlightCount);
        Assert.Equal(0m, summary.LoggedHours);
        Assert.Equal("No valid flight dates found", summary.DateRangeDisplay);
        Assert.Equal(
            "No flights were found. Check that this is the workbook you intend to move.",
            Assert.Single(summary.Warnings).Message);
    }

    [Fact]
    public void CreateCountsAllSingleAndMultiEngineIcusAsFlightTime()
    {
        var singleEngineIcus = Flight(new DateOnly(2019, 2, 27), 0m) with
        {
            SeCommandDay = null,
            SeIcusDay = 4.0m,
            IfrIf = 3.2m
        };
        var mixedIcusAndCommand = Flight(new DateOnly(2019, 9, 26), 0.5m) with
        {
            SeIcusDay = 6.8m
        };
        var multiEngineIcus = Flight(new DateOnly(2020, 1, 1), 0m) with
        {
            SeCommandDay = null,
            MeIcusNight = 2.5m,
            IfrIf = 2.5m,
            LandingsDay = null,
            LandingsNight = 1
        };

        var summary = WorkbookPreMigrationInspector.Create(
            [singleEngineIcus, mixedIcusAndCommand, multiEngineIcus],
            today: new DateOnly(2026, 8, 29));

        Assert.Equal(13.8m, summary.LoggedHours);
        Assert.DoesNotContain(summary.Warnings, warning =>
            warning.Message == PortableLogbookEntryMessages.MissingLoggedTime);
        Assert.DoesNotContain(summary.Warnings, warning =>
            warning.Message == PortableLogbookEntryMessages.InstrumentTimeExceedsFlightTime);
        Assert.DoesNotContain(summary.Warnings, warning =>
            warning.Message == PortableLogbookEntryMessages.DayNightTimeExceedsFlightTime);
    }

    [Fact]
    public void CreateReportsInstrumentTimeOnlyWhenItExceedsAllFlightTimeIncludingIcus()
    {
        var entry = Flight(new DateOnly(2019, 2, 27), 0m) with
        {
            SeCommandDay = null,
            SeIcusDay = 4.0m,
            IfrIf = 4.1m
        };

        var summary = WorkbookPreMigrationInspector.Create(
            [entry],
            today: new DateOnly(2026, 8, 29));

        var warning = Assert.Single(summary.Warnings, warning =>
            warning.Message == PortableLogbookEntryMessages.InstrumentTimeExceedsFlightTime);
        Assert.Equal(1, warning.AffectedFlightCount);
    }

    private static PortableLogbookWorkbookEntry Flight(DateOnly date, decimal commandDay) =>
        PortableLogbookWorkbookEntry.Empty with
        {
            Year = date.Year,
            Month = date.Month,
            Day = date.Day,
            Type = "C172",
            Reg = "VH-ABC",
            From = "YSBK",
            To = "YSCN",
            SeCommandDay = commandDay,
            LandingsDay = 1
        };
}
