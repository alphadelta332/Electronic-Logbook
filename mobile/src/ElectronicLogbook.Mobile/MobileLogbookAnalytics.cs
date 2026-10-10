using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public sealed record MobileAnalyticsFilters(
    DateOnly? From = null, DateOnly? To = null, string? AircraftType = null, string? Registration = null);

public sealed record MobileAnalyticsValue(string Name, decimal Value);
public sealed record MobileAnalyticsYear(int Year, IReadOnlyList<MobileAnalyticsValue> Categories);
public sealed record MobileAnalyticsPoint(DateOnly Date, decimal Hours);
public sealed record MobileAnalyticsRecord(decimal Value, DateOnly From, DateOnly To);
public sealed record MobileAnalyticsFlight(decimal Hours, DateOnly Date, string Route, string? Type, string? Registration);
public sealed record MobileAnalyticsAirport(MobileAirport Airport, int Visits, DateOnly FirstVisit);
public sealed record MobileAnalyticsLeg(MobileAirport From, MobileAirport To, DateOnly Date, double NauticalMiles);

public sealed record MobileAnalyticsStatistics(
    MobileAnalyticsRecord? MostHoursInDay,
    MobileAnalyticsRecord? MostHoursInSevenDays,
    MobileAnalyticsRecord? MostHoursInMonth,
    MobileAnalyticsRecord? MostHoursInYear,
    MobileAnalyticsRecord? MostLandingsInDay,
    MobileAnalyticsRecord? MostApproachesInDay,
    MobileAnalyticsRecord? MostFlyingDaysInMonth,
    MobileAnalyticsFlight? LongestFlight,
    MobileAnalyticsFlight? ShortestFlight,
    decimal? AverageHoursPerFlight,
    decimal? AverageFlyingDaysPerActiveMonthRecent,
    decimal? AverageFlyingDaysPerActiveMonth,
    decimal? IfrPercentageRecent,
    decimal? IfrPercentage,
    MobileAnalyticsValue? MostHoursWithAnotherPic,
    int DistinctCrew,
    MobileAnalyticsAirport? MostVisitedAirport,
    MobileAnalyticsAirport? MostVisitedAirportExcludingBases,
    MobileAnalyticsAirport? MostRecentNewAirport,
    int AirportsVisited,
    MobileAnalyticsLeg? LongestLeg,
    MobileAnalyticsLeg? ShortestLeg,
    decimal ApproximateNauticalMiles,
    decimal PercentageOfDistanceToMoon);

public sealed record MobileLogbookAnalytics(
    IReadOnlyList<MobileAnalyticsYear> HoursByYear,
    IReadOnlyList<MobileAnalyticsValue> TopRegistrations,
    IReadOnlyList<MobileAnalyticsValue> TopTypes,
    IReadOnlyList<MobileAnalyticsPoint> CumulativeHours,
    IReadOnlyList<MobileAnalyticsValue> HoursByCategory,
    IReadOnlyList<MobileAnalyticsAirport> TopAirports,
    MobileAnalyticsStatistics Statistics,
    decimal FlyingHours,
    int FlightCount,
    int MissingDateEntries,
    IReadOnlyList<string> UnresolvedAirportCodes,
    bool BasesConfigured)
{
    private static readonly string[] CategoryNames =
    [
        "SE Day ICUS", "SE Night ICUS", "SE Day Dual", "SE Night Dual",
        "SE Day Command", "SE Night Command", "ME Day ICUS", "ME Night ICUS",
        "ME Day Dual", "ME Night Dual", "ME Day Command", "ME Night Command",
        "Day Copilot", "Night Copilot"
    ];

    public static MobileLogbookAnalytics Create(
        IEnumerable<PortableLogbookMaterializedEntryV2> entries,
        MobileAnalyticsFilters filters,
        DateOnly today,
        IReadOnlyCollection<string> baseAirports,
        MobileAirportCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(baseAirports);
        ArgumentNullException.ThrowIfNull(catalog);
        if (filters.From > filters.To)
        {
            throw new ArgumentException("The start date must be on or before the end date.", nameof(filters));
        }

        var active = entries.Where(item => !item.IsDeleted && item.Entry is not null)
            .Select(item => item.Entry!)
            .Where(entry => Matches(entry.Type, filters.AircraftType) && Matches(entry.Reg, filters.Registration))
            .ToArray();
        var selected = active.Where(entry => entry.Date is { } date && date <= today &&
                (filters.From is null || date >= filters.From) && (filters.To is null || date <= filters.To))
            .OrderBy(entry => entry.Date).ToArray();
        var flights = selected.Where(entry => MobileLogbookSession.WorkbookFlightTime(entry) > 0).ToArray();
        var daily = flights.GroupBy(entry => entry.Date!.Value)
            .Select(group => new MobileAnalyticsPoint(group.Key, group.Sum(MobileLogbookSession.WorkbookFlightTime)))
            .OrderBy(point => point.Date).ToArray();
        var monthly = daily.GroupBy(point => new DateOnly(point.Date.Year, point.Date.Month, 1)).ToArray();
        var yearly = flights.GroupBy(entry => entry.Date!.Value.Year).OrderBy(group => group.Key).ToArray();
        var recentStart = DateOnly.FromDayNumber(Math.Max(0, today.DayNumber - 364));
        var recent = flights.Where(entry => entry.Date >= recentStart).ToArray();
        var total = daily.Sum(point => point.Hours);
        var cumulative = new List<MobileAnalyticsPoint>();
        if (daily.Length > 0)
            cumulative.Add(new(DateOnly.FromDayNumber(Math.Max(0, daily[0].Date.DayNumber - 1)), 0));
        decimal running = 0;
        foreach (var point in daily)
        {
            running += point.Hours;
            cumulative.Add(point with { Hours = running });
        }

        var airports = new Dictionary<string, MobileAnalyticsAirport>(StringComparer.OrdinalIgnoreCase);
        var unresolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var legs = new List<MobileAnalyticsLeg>();
        foreach (var entry in flights)
        {
            var visited = new Dictionary<string, MobileAirport>(StringComparer.OrdinalIgnoreCase);
            foreach (var token in EndpointTokens(entry.From)
                         .Concat(MobileWorkbookEntryValidation.AirportTokens(entry.Via))
                         .Concat(EndpointTokens(entry.To))
                         .Concat(MobileWorkbookEntryValidation.AirportTokens(entry.Remarks)))
            {
                var airport = Resolve(token);
                if (airport is not null) visited.TryAdd(airport.Icao, airport);
            }

            foreach (var airport in visited.Values)
            {
                airports[airport.Icao] = airports.TryGetValue(airport.Icao, out var prior)
                    ? prior with { Visits = prior.Visits + 1 }
                    : new(airport, 1, entry.Date!.Value);
            }

            var route = string.IsNullOrWhiteSpace(entry.From) && string.IsNullOrWhiteSpace(entry.Via) &&
                        string.IsNullOrWhiteSpace(entry.To)
                ? MobileWorkbookEntryValidation.AirportTokens(entry.Remarks)
                : EndpointTokens(entry.From).Concat(MobileWorkbookEntryValidation.AirportTokens(entry.Via))
                    .Concat(EndpointTokens(entry.To));
            MobileAirport? previous = null;
            foreach (var token in route)
            {
                var airport = Resolve(token);
                if (previous is not null && airport is not null)
                {
                    var distance = MobileAirportCatalog.GreatCircleDistanceNm(previous, airport);
                    if (distance > 0) legs.Add(new(previous, airport, entry.Date!.Value, distance));
                }
                // An unresolved stop breaks the route; never invent a leg across it.
                previous = airport;
            }
        }

        var rankedAirports = airports.Values.OrderByDescending(item => item.Visits)
            .ThenBy(item => item.Airport.Icao, StringComparer.OrdinalIgnoreCase).ToArray();
        var bases = baseAirports.Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => catalog.TryFind(code, out var airport) ? airport.Icao : code.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var picHours = Rankings(flights.Where(entry => IsOtherPerson(entry.Pic)), entry => entry.Pic!);
        var flightRecords = flights.Select(entry => new MobileAnalyticsFlight(
            MobileLogbookSession.WorkbookFlightTime(entry), entry.Date!.Value,
            RouteLabel(entry), entry.Type, entry.Reg)).ToArray();
        var statistics = new MobileAnalyticsStatistics(
            Maximum(daily.Select(point => new MobileAnalyticsRecord(point.Hours, point.Date, point.Date))),
            SevenDays(daily),
            Maximum(monthly.Select(group => new MobileAnalyticsRecord(group.Sum(point => point.Hours), group.Key,
                group.Key.AddDays(DateTime.DaysInMonth(group.Key.Year, group.Key.Month) - 1)))),
            Maximum(yearly.Select(group => new MobileAnalyticsRecord(group.Sum(MobileLogbookSession.WorkbookFlightTime),
                new(group.Key, 1, 1), new(group.Key, 12, 31)))),
            Maximum(flights.GroupBy(entry => entry.Date!.Value).Select(group => new MobileAnalyticsRecord(
                group.Sum(entry => (decimal)entry.LandingsDay.GetValueOrDefault() + entry.LandingsNight.GetValueOrDefault()),
                group.Key, group.Key))),
            Maximum(selected.GroupBy(entry => entry.Date!.Value).Select(group => new MobileAnalyticsRecord(
                group.Sum(entry => (decimal)MobileLogbookSession.WorkbookApproaches(entry) - entry.Circling.GetValueOrDefault()),
                group.Key, group.Key))),
            Maximum(monthly.Select(group => new MobileAnalyticsRecord(group.Count(), group.Key,
                group.Key.AddDays(DateTime.DaysInMonth(group.Key.Year, group.Key.Month) - 1)))),
            flightRecords.OrderByDescending(item => item.Hours).ThenBy(item => item.Date)
                .ThenBy(item => item.Route, StringComparer.OrdinalIgnoreCase).FirstOrDefault(),
            flightRecords.OrderBy(item => item.Hours).ThenBy(item => item.Date)
                .ThenBy(item => item.Route, StringComparer.OrdinalIgnoreCase).FirstOrDefault(),
            flights.Length == 0 ? null : total / flights.Length,
            AverageDays(recent), AverageDays(flights), IfrPercent(recent), IfrPercent(flights),
            picHours.FirstOrDefault(),
            flights.SelectMany(entry => new[] { entry.Pic, entry.OtherPilotOrCrew }).Where(IsOtherPerson)
                .Select(name => name!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            rankedAirports.FirstOrDefault(), bases.Count == 0 ? null : rankedAirports.FirstOrDefault(item => !bases.Contains(item.Airport.Icao)),
            rankedAirports.OrderByDescending(item => item.FirstVisit)
                .ThenBy(item => item.Airport.Icao, StringComparer.OrdinalIgnoreCase).FirstOrDefault(),
            rankedAirports.Length,
            legs.OrderByDescending(item => item.NauticalMiles).ThenBy(item => item.Date)
                .ThenBy(item => item.From.Icao, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.To.Icao, StringComparer.OrdinalIgnoreCase).FirstOrDefault(),
            legs.OrderBy(item => item.NauticalMiles).ThenBy(item => item.Date)
                .ThenBy(item => item.From.Icao, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.To.Icao, StringComparer.OrdinalIgnoreCase).FirstOrDefault(),
            total * 120m, total * 120m / 207600m * 100m);
        return new(
            yearly.Select(group => new MobileAnalyticsYear(group.Key, Categories(group))).ToArray(),
            Rankings(flights, entry => entry.Reg).Take(5).ToArray(),
            Rankings(flights, entry => entry.Type).Take(5).ToArray(), cumulative, Categories(flights),
            rankedAirports.Take(10).ToArray(), statistics, total, flights.Length,
            active.Count(entry => entry.Date is null), unresolved.Order(StringComparer.OrdinalIgnoreCase).ToArray(), bases.Count > 0);

        MobileAirport? Resolve(string token)
        {
            if (catalog.TryFind(token, out var airport)) return airport;
            unresolved.Add(token.Trim().ToUpperInvariant());
            return null;
        }
    }

    private static bool Matches(string? value, string? filter) => string.IsNullOrWhiteSpace(filter) ||
        string.Equals(value?.Trim(), filter.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsOtherPerson(string? name) => !string.IsNullOrWhiteSpace(name) &&
        !string.Equals(name.Trim(), "Self", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> EndpointTokens(string? code) =>
        string.IsNullOrWhiteSpace(code) ? [] : new[] { code.Trim() };

    private static string RouteLabel(PortableLogbookWorkbookEntry entry) =>
        string.IsNullOrWhiteSpace(entry.From) && string.IsNullOrWhiteSpace(entry.Via) && string.IsNullOrWhiteSpace(entry.To)
            ? entry.Remarks?.Trim() ?? string.Empty
            : string.Join(" → ", new[] { entry.From, entry.Via, entry.To }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static IReadOnlyList<MobileAnalyticsValue> Rankings(
        IEnumerable<PortableLogbookWorkbookEntry> entries, Func<PortableLogbookWorkbookEntry, string?> name) =>
        entries.GroupBy(entry => string.IsNullOrWhiteSpace(name(entry)) ? "Unspecified" : name(entry)!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => new MobileAnalyticsValue(group.Select(name).Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim()).Order(StringComparer.Ordinal).FirstOrDefault() ?? "Unspecified",
                group.Sum(MobileLogbookSession.WorkbookFlightTime)))
            .OrderByDescending(value => value.Value).ThenBy(value => value.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    private static IReadOnlyList<MobileAnalyticsValue> Categories(IEnumerable<PortableLogbookWorkbookEntry> entries)
    {
        var totals = new decimal[14];
        foreach (var entry in entries)
        {
            decimal?[] values = [entry.SeIcusDay, entry.SeIcusNight, entry.SeDualDay, entry.SeDualNight,
                entry.SeCommandDay, entry.SeCommandNight, entry.MeIcusDay, entry.MeIcusNight, entry.MeDualDay,
                entry.MeDualNight, entry.MeCommandDay, entry.MeCommandNight, entry.CopilotDay, entry.CopilotNight];
            for (var index = 0; index < values.Length; index++) totals[index] += values[index].GetValueOrDefault();
        }
        return CategoryNames.Select((name, index) => new MobileAnalyticsValue(name, totals[index])).ToArray();
    }

    private static MobileAnalyticsRecord? Maximum(IEnumerable<MobileAnalyticsRecord> values) =>
        values.Where(value => value.Value > 0).OrderByDescending(value => value.Value).ThenBy(value => value.From).FirstOrDefault();

    private static MobileAnalyticsRecord? SevenDays(IReadOnlyList<MobileAnalyticsPoint> days)
    {
        MobileAnalyticsRecord? best = null;
        decimal sum = 0;
        var left = 0;
        for (var right = 0; right < days.Count; right++)
        {
            sum += days[right].Hours;
            while (days[right].Date.DayNumber - days[left].Date.DayNumber >= 7) sum -= days[left++].Hours;
            var start = days[left].Date;
            if (best is null || sum > best.Value)
                best = new(sum, start, DateOnly.FromDayNumber(Math.Min(DateOnly.MaxValue.DayNumber, start.DayNumber + 6)));
        }
        return best;
    }

    private static decimal? AverageDays(IEnumerable<PortableLogbookWorkbookEntry> entries)
    {
        var dates = entries.Select(entry => entry.Date!.Value).Distinct().ToArray();
        return dates.Length == 0 ? null : (decimal)dates.Length / dates.Select(date => (date.Year, date.Month)).Distinct().Count();
    }

    private static decimal? IfrPercent(IReadOnlyCollection<PortableLogbookWorkbookEntry> entries)
    {
        var hours = entries.Sum(MobileLogbookSession.WorkbookFlightTime);
        return hours == 0 ? null : entries.Sum(entry => entry.IfrIf.GetValueOrDefault()) / hours * 100m;
    }
}
