using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileLogbookAnalyticsTests
{
    private static readonly DateOnly Today = new(2024, 3, 1);
    private static readonly MobileAirport A = new("AAAA", "Alpha", "AAA", "AA", 0, 0);
    private static readonly MobileAirport B = new("BBBB", "Bravo", "BBB", "BB", 0, 1);
    private static readonly MobileAirport C = new("CCCC", "Charlie", "CCC", "CC", 0, 2);
    private static readonly MobileAirportCatalog Catalog = MobileAirportCatalog.Create([A, B, C]);

    [Fact]
    public void HandCalculatedFixtureCoversAllTwentyFourStatisticsAndSixChartDatasets()
    {
        var entries = new[]
        {
            Flight(new(2023, 1, 1), 5) with { Pic = "Alex", OtherPilotOrCrew = "Casey", IfrIf = 1, LandingsDay = 2 },
            Flight(new(2024, 1, 1), 1) with { Pic = "Self", OtherPilotOrCrew = "Casey", Via = "BBB CCC AA", To = "AAAA", IfrIf = .5m, LandingsDay = 1 },
            Flight(new(2024, 1, 7), 2) with { Pic = "Alex", OtherPilotOrCrew = "Self", From = "BBB", To = "AA", IfrIf = .5m, LandingsDay = 3 },
            Flight(new(2024, 1, 13), 3) with { Pic = "Bob", OtherPilotOrCrew = "Alex", From = "CCC", To = "AAA", IfrIf = 1, LandingsDay = 1 },
            Flight(new(2024, 2, 29), 4) with { Pic = "Bob", To = "CCC", IfrIf = 1, LandingsDay = 2, Ils = 1 },
            Flight(new(2024, 2, 29), 2) with { OtherPilotOrCrew = "Dana", IfrSim = 20, LandingsNight = 3, Rnp = 2 },
            Flight(new(2024, 2, 29), 0) with { IfrSim = 100, Ils = 10, Circling = 100, Pic = "Simulator instructor", From = "UNKNOWN" }
        };
        var result = Create(entries, bases: ["AA"]);
        var stats = result.Statistics;

        Assert.Equal(17m, result.FlyingHours);
        Assert.Equal(6, result.FlightCount);
        AssertRecord(stats.MostHoursInDay, 6, new(2024, 2, 29), new(2024, 2, 29));
        AssertRecord(stats.MostHoursInSevenDays, 6, new(2024, 2, 29), new(2024, 3, 6));
        AssertRecord(stats.MostHoursInMonth, 6, new(2024, 1, 1), new(2024, 1, 31));
        AssertRecord(stats.MostHoursInYear, 12, new(2024, 1, 1), new(2024, 12, 31));
        AssertRecord(stats.MostLandingsInDay, 5, new(2024, 2, 29), new(2024, 2, 29));
        AssertRecord(stats.MostApproachesInDay, 13, new(2024, 2, 29), new(2024, 2, 29));
        AssertRecord(stats.MostFlyingDaysInMonth, 3, new(2024, 1, 1), new(2024, 1, 31));
        Assert.Equal(5m, stats.LongestFlight!.Hours);
        Assert.Equal(new DateOnly(2023, 1, 1), stats.LongestFlight.Date);
        Assert.Equal("AAA → BBB", stats.LongestFlight.Route);
        Assert.Equal(1m, stats.ShortestFlight!.Hours);
        Assert.Equal(new DateOnly(2024, 1, 1), stats.ShortestFlight.Date);
        Assert.Equal(17m / 6m, stats.AverageHoursPerFlight);
        Assert.Equal(2m, stats.AverageFlyingDaysPerActiveMonthRecent);
        Assert.Equal(5m / 3m, stats.AverageFlyingDaysPerActiveMonth);
        Assert.Equal(25m, stats.IfrPercentageRecent);
        Assert.Equal(4m / 17m * 100m, stats.IfrPercentage);
        Assert.Equal(new MobileAnalyticsValue("Alex", 7m), stats.MostHoursWithAnotherPic);
        Assert.Equal(4, stats.DistinctCrew);
        Assert.Equal("AAAA", stats.MostVisitedAirport!.Airport.Icao);
        Assert.Equal(6, stats.MostVisitedAirport.Visits);
        Assert.Equal("BBBB", stats.MostVisitedAirportExcludingBases!.Airport.Icao);
        Assert.Equal(4, stats.MostVisitedAirportExcludingBases.Visits);
        Assert.Equal("CCCC", stats.MostRecentNewAirport!.Airport.Icao);
        Assert.Equal(new DateOnly(2024, 1, 1), stats.MostRecentNewAirport.FirstVisit);
        Assert.Equal(3, stats.AirportsVisited);
        Assert.Equal("CCCC", stats.LongestLeg!.From.Icao);
        Assert.Equal("AAAA", stats.LongestLeg.To.Icao);
        Assert.Equal(new DateOnly(2024, 1, 1), stats.LongestLeg.Date);
        Assert.InRange(stats.LongestLeg.NauticalMiles, 120.07, 120.09);
        Assert.Equal("AAAA", stats.ShortestLeg!.From.Icao);
        Assert.Equal("BBBB", stats.ShortestLeg.To.Icao);
        Assert.Equal(new DateOnly(2023, 1, 1), stats.ShortestLeg.Date);
        Assert.InRange(stats.ShortestLeg.NauticalMiles, 60.03, 60.05);
        Assert.Equal(2040m, stats.ApproximateNauticalMiles);
        Assert.Equal(2040m / 207600m * 100m, stats.PercentageOfDistanceToMoon);

        Assert.Equal(new[] { 2023, 2024 }, result.HoursByYear.Select(year => year.Year));
        Assert.Equal(new[] { 5m, 12m }, result.HoursByYear.Select(year => year.Categories.Sum(category => category.Value)));
        Assert.Equal(14, result.HoursByCategory.Count);
        Assert.Equal(17m, result.HoursByCategory.Sum(category => category.Value));
        Assert.Equal(new MobileAnalyticsValue("REG", 17m), Assert.Single(result.TopRegistrations));
        Assert.Equal(new MobileAnalyticsValue("TYPE", 17m), Assert.Single(result.TopTypes));
        Assert.Equal(new[] { 0m, 5m, 6m, 8m, 11m, 17m }, result.CumulativeHours.Select(point => point.Hours));
        Assert.Equal(new DateOnly(2022, 12, 31), result.CumulativeHours[0].Date);
        Assert.Equal(new[] { 6, 4, 3 }, result.TopAirports.Select(airport => airport.Visits));
        Assert.True(result.BasesConfigured);
        Assert.Empty(result.UnresolvedAirportCodes);
    }

    [Fact]
    public void SevenDayWindowCorrectsWorkbookExampleAndExcludesEighthDate()
    {
        var result = Create([Flight(new(2024, 1, 13), 1), Flight(new(2024, 1, 7), 1), Flight(new(2024, 1, 1), 1)]);
        AssertRecord(result.Statistics.MostHoursInSevenDays, 2, new(2024, 1, 1), new(2024, 1, 7));
        var boundary = Create([Flight(new(2024, 1, 1), 2), Flight(new(2024, 1, 8), 3)]);
        AssertRecord(boundary.Statistics.MostHoursInSevenDays, 3, new(2024, 1, 8), new(2024, 1, 14));
    }

    [Fact]
    public void FiltersApplyTogetherWithInclusiveDatesAndRecentPeriodIntersection()
    {
        var cutoff = Today.AddDays(-364);
        var entries = new[]
        {
            Flight(cutoff.AddDays(-1), 8), Flight(cutoff, 2) with { IfrIf = 1 },
            Flight(Today, 3), Flight(Today.AddDays(1), 64),
            Flight(Today, 16) with { Type = "OTHER" }, Flight(Today, 32) with { Reg = "OTHER" }
        };
        var all = Create(entries, new(AircraftType: " type ", Registration: "reg"));
        Assert.Equal(13m, all.FlyingHours);
        Assert.Equal(20m, all.Statistics.IfrPercentageRecent);
        Assert.Equal(1m, all.Statistics.AverageFlyingDaysPerActiveMonthRecent);
        var selected = Create(entries, new(Today, Today, "TYPE", "REG"));
        Assert.Equal(3m, selected.FlyingHours);
        Assert.Equal(0m, selected.Statistics.IfrPercentageRecent);
        Assert.Single(selected.TopTypes);
        Assert.Single(selected.TopRegistrations);
        Assert.Equal(2, selected.CumulativeHours.Count);
        Assert.Throws<ArgumentException>(() => Create(entries, new(Today, cutoff)));
    }

    [Fact]
    public void DeletesFutureAndMissingDatesNeverEnterTotals()
    {
        var valid = Flight(Today, 1);
        var result = MobileLogbookAnalytics.Create(
            [Materialized(valid), Materialized(valid with { Year = null }),
             Materialized(valid with { Month = 2, Day = 30 }), Materialized(Flight(Today.AddDays(1), 8)),
             Materialized(valid with { SeCommandDay = 16 }) with { IsDeleted = true },
             Materialized(valid) with { Entry = null }], new(), Today, [], Catalog);
        Assert.Equal(1m, result.FlyingHours);
        Assert.Equal(1, result.FlightCount);
        Assert.Equal(2, result.MissingDateEntries);
    }

    [Fact]
    public void EmptyAndZeroDataHaveUnavailableRecordsAndAveragesButZeroCounts()
    {
        foreach (var entries in new PortableLogbookWorkbookEntry[][] { [], [Flight(Today, 0)] })
        {
            var result = Create(entries);
            var stats = result.Statistics;
            Assert.Null(stats.MostHoursInDay);
            Assert.Null(stats.MostHoursInSevenDays);
            Assert.Null(stats.MostHoursInMonth);
            Assert.Null(stats.MostHoursInYear);
            Assert.Null(stats.MostLandingsInDay);
            Assert.Null(stats.MostApproachesInDay);
            Assert.Null(stats.MostFlyingDaysInMonth);
            Assert.Null(stats.LongestFlight);
            Assert.Null(stats.ShortestFlight);
            Assert.Null(stats.AverageHoursPerFlight);
            Assert.Null(stats.AverageFlyingDaysPerActiveMonth);
            Assert.Null(stats.AverageFlyingDaysPerActiveMonthRecent);
            Assert.Null(stats.IfrPercentage);
            Assert.Null(stats.IfrPercentageRecent);
            Assert.Null(stats.MostHoursWithAnotherPic);
            Assert.Equal(0, stats.DistinctCrew);
            Assert.Null(stats.MostVisitedAirport);
            Assert.Null(stats.MostVisitedAirportExcludingBases);
            Assert.Null(stats.MostRecentNewAirport);
            Assert.Equal(0, stats.AirportsVisited);
            Assert.Null(stats.LongestLeg);
            Assert.Null(stats.ShortestLeg);
            Assert.Equal(0m, stats.ApproximateNauticalMiles);
            Assert.Equal(0m, stats.PercentageOfDistanceToMoon);
            Assert.Empty(result.CumulativeHours);
            Assert.False(result.BasesConfigured);
        }
    }

    [Fact]
    public void SimulatorOnlyEntriesContributeApproachesButNoFlightStatistics()
    {
        var result = Create([Flight(Today, 0) with { IfrSim = 5, IfrIf = 3, Ils = 2, Rnp = 3,
            Vor = 4, Ndb = 5, DgaCdi = 6, DgaAzi = 7, Circling = 99, LandingsDay = 100, Pic = "Alex" }]);
        AssertRecord(result.Statistics.MostApproachesInDay, 27, Today, Today);
        Assert.Equal(0, result.FlightCount);
        Assert.Equal(0m, result.FlyingHours);
        Assert.Null(result.Statistics.MostLandingsInDay);
        Assert.Null(result.Statistics.AverageFlyingDaysPerActiveMonth);
        Assert.Null(result.Statistics.IfrPercentage);
        Assert.Equal(0, result.Statistics.AirportsVisited);
        Assert.Equal(0, result.Statistics.DistinctCrew);
    }

    [Fact]
    public void AliasesRemarksIgnoredTokensAndUnresolvedStopsPreserveRouteMeaning()
    {
        var result = Create([
            Flight(new(2024, 1, 1), 1) with { From = "AA", Via = "BBB UNKNOWN CCC", To = "AAAA", Remarks = "(AAA)-BBB, IPC OPC FR IR IFR VFR TEST CHECK CIRCLING SIM" },
            Flight(new(2024, 1, 2), 1) with { From = null, Via = null, To = null, Remarks = "AAA-BBB-CCC" },
            Flight(new(2024, 1, 3), 1) with { Via = "AAA", To = "AAAA" }
        ], bases: ["AAA", "BBBB", "CC"]);
        Assert.Equal(new[] { "UNKNOWN" }, result.UnresolvedAirportCodes);
        Assert.Equal(new[] { 3, 2, 2 }, result.TopAirports.Select(item => item.Visits));
        Assert.Null(result.Statistics.MostVisitedAirportExcludingBases);
        Assert.True(result.BasesConfigured);
        Assert.InRange(result.Statistics.ShortestLeg!.NauticalMiles, 60.03, 60.05);
        Assert.Equal("CCCC", result.Statistics.LongestLeg!.From.Icao);
        Assert.Equal("AAAA", result.Statistics.LongestLeg.To.Icao);
        var broken = Create([Flight(Today, 1) with { Via = "UNKNOWN" }]);
        Assert.Null(broken.Statistics.LongestLeg);
        Assert.Null(broken.Statistics.ShortestLeg);
        var same = Create([Flight(Today, 1) with { To = "AA" }]);
        Assert.Null(same.Statistics.ShortestLeg);
        Assert.Equal(1, same.Statistics.AirportsVisited);
    }

    [Fact]
    public void TiesUseEarliestDateAndAlphabeticalNamesRegardlessOfInputOrder()
    {
        var later = Flight(new(2024, 2, 1), 2) with { Type = "Zulu", Reg = "Z", Pic = "Zulu", OtherPilotOrCrew = "self" };
        var earlier = Flight(new(2024, 1, 1), 2) with { Type = "Alpha", Reg = "A", Pic = "Alpha", OtherPilotOrCrew = " alpha " };
        var result = Create([later, earlier]);
        Assert.Equal(earlier.Date, result.Statistics.MostHoursInDay!.From);
        Assert.Equal(earlier.Date, result.Statistics.LongestFlight!.Date);
        Assert.Equal(earlier.Date, result.Statistics.ShortestFlight!.Date);
        Assert.Equal("Alpha", result.TopTypes[0].Name);
        Assert.Equal("A", result.TopRegistrations[0].Name);
        Assert.Equal("Alpha", result.Statistics.MostHoursWithAnotherPic!.Name);
        Assert.Equal(2, result.Statistics.DistinctCrew);
        Assert.Equal("AAAA", result.Statistics.MostRecentNewAirport!.Airport.Icao);
        Assert.Equal(result.TopAirports, Create([earlier, later]).TopAirports);
    }

    [Fact]
    public void AllFourteenCategoriesReconcileAndRankingsAreLimited()
    {
        var entry = Flight(Today, 5) with { SeIcusDay = 1, SeIcusNight = 2, SeDualDay = 3, SeDualNight = 4,
            SeCommandNight = 6, MeIcusDay = 7, MeIcusNight = 8, MeDualDay = 9, MeDualNight = 10,
            MeCommandDay = 11, MeCommandNight = 12, CopilotDay = 13, CopilotNight = 14, IfrSim = 100 };
        var categories = Create([entry]);
        Assert.Equal(Enumerable.Range(1, 14).Select(value => (decimal)value), categories.HoursByCategory.Select(value => value.Value));
        Assert.Equal(105m, categories.FlyingHours);
        Assert.Equal(105m, Assert.Single(categories.HoursByYear).Categories.Sum(value => value.Value));
        var many = Create(Enumerable.Range(1, 7).Select(value => Flight(Today, value) with { Type = $"T{value}", Reg = $"R{value}" }));
        Assert.Equal(5, many.TopTypes.Count);
        Assert.Equal(5, many.TopRegistrations.Count);
        Assert.Equal(new[] { 7m, 6m, 5m, 4m, 3m }, many.TopTypes.Select(value => value.Value));
    }

    [Fact]
    public void QuietMonthsAndEmptyRecentIntersectionDoNotDiluteAverages()
    {
        var result = Create([Flight(new(2022, 1, 1), 1), Flight(new(2022, 1, 1), 2), Flight(new(2022, 12, 31), 3)],
            bases: [" "]);
        Assert.Equal(1m, result.Statistics.AverageFlyingDaysPerActiveMonth);
        Assert.Equal(2m, result.Statistics.AverageHoursPerFlight);
        Assert.Null(result.Statistics.AverageFlyingDaysPerActiveMonthRecent);
        Assert.Null(result.Statistics.IfrPercentageRecent);
        Assert.False(result.BasesConfigured);
    }

    [Fact]
    public void MissingAircraftLabelsAreExplicitAndAirportChartStopsAtTen()
    {
        var airports = Enumerable.Range(0, 12).Select(index => new MobileAirport($"X{index:D3}", $"Airport {index}", null, null, 0, index)).ToArray();
        var entries = airports.Select(airport => Materialized(Flight(Today, 1) with { From = airport.Icao, To = airport.Icao, Type = null, Reg = " " }));
        var result = MobileLogbookAnalytics.Create(entries, new(), Today, [], MobileAirportCatalog.Create(airports));
        Assert.Equal(new MobileAnalyticsValue("Unspecified", 12), Assert.Single(result.TopTypes));
        Assert.Equal(new MobileAnalyticsValue("Unspecified", 12), Assert.Single(result.TopRegistrations));
        Assert.Equal(12, result.Statistics.AirportsVisited);
        Assert.Equal(10, result.TopAirports.Count);
        Assert.Equal(airports.Take(10).Select(airport => airport.Icao), result.TopAirports.Select(item => item.Airport.Icao));
        Assert.Null(result.Statistics.ShortestLeg);
    }

    [Fact]
    public void SevenDayWindowMatchesIndependentExhaustiveCalendarCalculation()
    {
        var start = new DateOnly(2023, 12, 20);
        var random = new Random(42);
        var entries = Enumerable.Range(0, 80).Select(index => Flight(start.AddDays(index), random.Next(0, 10))).ToArray();
        var expected = Enumerable.Range(0, 80).Select(index =>
        {
            var from = start.AddDays(index);
            return new MobileAnalyticsRecord(entries.Where(entry => entry.Date >= from && entry.Date <= from.AddDays(6))
                .Sum(entry => entry.SeCommandDay!.Value), from, from.AddDays(6));
        }).OrderByDescending(record => record.Value).ThenBy(record => record.From).First();
        var result = MobileLogbookAnalytics.Create(entries.Select(Materialized), new(), start.AddDays(80), [], Catalog);
        Assert.Equal(expected, result.Statistics.MostHoursInSevenDays);
    }

    private static MobileLogbookAnalytics Create(IEnumerable<PortableLogbookWorkbookEntry> entries,
        MobileAnalyticsFilters? filters = null, IReadOnlyCollection<string>? bases = null) =>
        MobileLogbookAnalytics.Create(entries.Select(Materialized), filters ?? new(), Today, bases ?? [], Catalog);

    private static PortableLogbookWorkbookEntry Flight(DateOnly date, decimal hours) =>
        PortableLogbookWorkbookEntry.Empty with { Year = date.Year, Month = date.Month, Day = date.Day,
            SeCommandDay = hours, Type = "TYPE", Reg = "REG", From = "AAA", To = "BBB" };

    private static PortableLogbookMaterializedEntryV2 Materialized(PortableLogbookWorkbookEntry entry) =>
        new(EntryId.New(), RevisionId.New(), false, entry, []);

    private static void AssertRecord(MobileAnalyticsRecord? actual, decimal value, DateOnly from, DateOnly to) =>
        Assert.Equal(new MobileAnalyticsRecord(value, from, to), actual);
}
