using System.Globalization;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public static class MobileWorkbookEntryValidation
{
    private const double RemoteAirportWarningThresholdNm = 3000;
    private const double HighSpeedRouteWarningThresholdKt = 700;
    private static readonly char[] AirportTokenDelimiters = ['-', ' ', ',', '(', ')'];
    private static readonly HashSet<string> IgnoredAirportTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "IPC", "OPC", "FR", "IR", "IFR", "VFR", "TEST", "CHECK", "CIRCLING", "SIM"
    };
    private static readonly string[] DefaultKeywordPhrases =
    [
        "FLIGHTREVIEW", "IPC", "OPC", "FR", "IRTEST", "PPLTEST", "IFRTEST", "CPLTEST",
        "MEARATING", "MEACLASSRATING", "FLIGHTTEST"
    ];

    public static IReadOnlyList<MobileWorkbookEntryIssue> Validate(
        PortableLogbookWorkbookEntry entry,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var errors = new List<MobileWorkbookEntryIssue>();
        if (entry.Date is null || entry.Date > today)
        {
            errors.Add(Error(
                "NEWENTRY-E001",
                PortableLogbookEntryMessages.InvalidWorkbookDate,
                nameof(entry.Year), nameof(entry.Month), nameof(entry.Day)));
        }

        var simulatorTime = Value(entry.IfrSim);
        if (simulatorTime == 0 && IsBlank(entry.Reg))
        {
            errors.Add(Error(
                "NEWENTRY-E002",
                PortableLogbookEntryMessages.MissingWorkbookRegistration,
                nameof(entry.Reg)));
        }

        if (!FlightHourValues(entry).Any(value => value > 0))
        {
            errors.Add(Error(
                "NEWENTRY-E003",
                PortableLogbookEntryMessages.MissingWorkbookLoggedTime,
                FlightHourFieldNames));
        }

        if (Value(entry.IfrIf) > FlightTime(entry))
        {
            errors.Add(Error(
                "NEWENTRY-E004",
                PortableLogbookEntryMessages.InstrumentTimeExceedsFlightTime,
                [nameof(entry.IfrIf), .. FlightTimeFieldNames]));
        }

        if (IsBlank(entry.Type))
        {
            errors.Add(Error("NEWENTRY-E005", PortableLogbookEntryMessages.MissingType, nameof(entry.Type)));
        }
        if (IsBlank(entry.Pic))
        {
            errors.Add(Error("NEWENTRY-E005", PortableLogbookEntryMessages.MissingPic, nameof(entry.Pic)));
        }

        if (simulatorTime == 0 && IsBlank(entry.From))
        {
            errors.Add(Error("NEWENTRY-E006", PortableLogbookEntryMessages.MissingDeparture, nameof(entry.From)));
        }
        if (simulatorTime == 0 && IsBlank(entry.To))
        {
            errors.Add(Error("NEWENTRY-E007", PortableLogbookEntryMessages.MissingDestination, nameof(entry.To)));
        }

        if (entry.CustomFields.Values.Any(value =>
                !string.IsNullOrWhiteSpace(value) && !IsNumeric(value)))
        {
            errors.Add(Error(
                "NEWENTRY-E008",
                PortableLogbookEntryMessages.InvalidNumericValue,
                entry.CustomFields.Keys.Select(field => field.Value).ToArray()));
        }

        return errors;
    }

    public static IReadOnlyList<MobileWorkbookEntryIssue> Warn(
        PortableLogbookWorkbookEntry entry,
        IEnumerable<PortableLogbookMaterializedEntryV2> currentEntries,
        EntryId? editingEntryId = null,
        MobileAirportCatalog? airportCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(currentEntries);

        airportCatalog ??= MobileAirportCatalog.Default;
        var comparableEntries = currentEntries
            .Where(existing => !existing.IsDeleted && existing.Entry is not null && existing.EntryId != editingEntryId)
            .ToArray();
        var warnings = new List<MobileWorkbookEntryIssue>();
        var flightTime = FlightTime(entry);
        var simulatorTime = Value(entry.IfrSim);
        var inFlightInstrumentTime = Value(entry.IfrIf);
        var approachValues = ApproachValues(entry).ToArray();
        var hasPositiveApproach = approachValues.Any(value => value > 0);
        var totalApproaches = approachValues.Sum();
        var dayLandings = entry.LandingsDay.GetValueOrDefault();
        var nightLandings = entry.LandingsNight.GetValueOrDefault();
        var totalLandings = dayLandings + nightLandings;
        var copilotHoursRecorded = Value(entry.CopilotDay) > 0 || Value(entry.CopilotNight) > 0;
        var dayHours = DayFlightTimeValues(entry).Sum();
        var nightHours = NightFlightTimeValues(entry).Sum();

        if (entry.OperatorProficiencyCheck == true &&
            (inFlightInstrumentTime > 0 || simulatorTime > 0) &&
            entry.InstrumentProficiencyCheck != true)
        {
            warnings.Add(Warning("NEWENTRY-W001", PortableLogbookEntryMessages.OpcWithoutIpc,
                nameof(entry.InstrumentProficiencyCheck), nameof(entry.OperatorProficiencyCheck), nameof(entry.IfrIf), nameof(entry.IfrSim)));
        }
        if (entry.InstrumentProficiencyCheck == true && entry.FlightReview != true)
        {
            warnings.Add(Warning("NEWENTRY-W002", PortableLogbookEntryMessages.IpcWithoutFlightReview,
                nameof(entry.FlightReview), nameof(entry.InstrumentProficiencyCheck)));
        }
        if (entry.InstrumentProficiencyCheck == true && entry.Circling.GetValueOrDefault() == 0)
        {
            warnings.Add(Warning("NEWENTRY-W003", PortableLogbookEntryMessages.IpcWithoutCircling,
                nameof(entry.Circling), nameof(entry.InstrumentProficiencyCheck)));
        }

        AddAirportWarnings(entry, comparableEntries, airportCatalog, flightTime, warnings);

        if (simulatorTime == 0 && !copilotHoursRecorded && dayLandings == 0 && nightLandings == 0)
        {
            warnings.Add(Warning("NEWENTRY-W007", PortableLogbookEntryMessages.WorkbookFlightWithoutLanding,
                nameof(entry.LandingsDay), nameof(entry.LandingsNight)));
        }

        var latestDate = comparableEntries
            .Select(existing => existing.Entry!.Date)
            .Where(date => date is not null)
            .Max();
        if (entry.Date is not null && latestDate is not null && entry.Date < latestDate)
        {
            warnings.Add(Warning("NEWENTRY-W008", PortableLogbookEntryMessages.BeforeLatestWorkbookEntry(latestDate.Value),
                nameof(entry.Year), nameof(entry.Month), nameof(entry.Day)));
        }

        if (dayHours > 0 && !copilotHoursRecorded && entry.LandingsDay.GetValueOrDefault() == 0)
        {
            warnings.Add(Warning("NEWENTRY-W009", PortableLogbookEntryMessages.WorkbookDayTimeWithoutLanding,
                nameof(entry.LandingsDay), DayFlightTimeFieldNames));
        }
        if (entry.LandingsDay.GetValueOrDefault() > 0 && dayHours == 0)
        {
            warnings.Add(Warning("NEWENTRY-W010", PortableLogbookEntryMessages.WorkbookDayLandingWithoutTime,
                nameof(entry.LandingsDay), DayFlightTimeFieldNames));
        }
        if (nightHours > 0 && !copilotHoursRecorded && entry.LandingsNight.GetValueOrDefault() == 0)
        {
            warnings.Add(Warning("NEWENTRY-W011", PortableLogbookEntryMessages.WorkbookNightTimeWithoutLanding,
                nameof(entry.LandingsNight), NightFlightTimeFieldNames));
        }
        if (entry.LandingsNight.GetValueOrDefault() > 0 && nightHours == 0)
        {
            warnings.Add(Warning("NEWENTRY-W012", PortableLogbookEntryMessages.WorkbookNightLandingWithoutTime,
                nameof(entry.LandingsNight), NightFlightTimeFieldNames));
        }
        if (entry.OperatorProficiencyCheck == true &&
            inFlightInstrumentTime == 0 && simulatorTime == 0 && !hasPositiveApproach)
        {
            warnings.Add(Warning("NEWENTRY-W013", PortableLogbookEntryMessages.OpcWithoutInstrumentActivity,
                nameof(entry.OperatorProficiencyCheck), [nameof(entry.IfrIf), nameof(entry.IfrSim), .. ApproachFieldNames]));
        }
        if (hasPositiveApproach && inFlightInstrumentTime == 0 && simulatorTime == 0)
        {
            warnings.Add(Warning("NEWENTRY-W014", PortableLogbookEntryMessages.WorkbookApproachWithoutInstrumentTime,
                nameof(entry.IfrIf), [.. ApproachFieldNames, nameof(entry.IfrSim)]));
        }
        if (totalLandings > 6 * flightTime)
        {
            warnings.Add(Warning("NEWENTRY-W015", PortableLogbookEntryMessages.WorkbookHighLandings,
                dayLandings >= nightLandings ? nameof(entry.LandingsDay) : nameof(entry.LandingsNight),
                [nameof(entry.LandingsDay), nameof(entry.LandingsNight), .. FlightTimeFieldNames]));
        }
        if (totalApproaches > 3 * flightTime)
        {
            warnings.Add(Warning("NEWENTRY-W016", PortableLogbookEntryMessages.WorkbookHighApproaches,
                MostUsedApproachField(entry), [.. ApproachFieldNames, .. FlightTimeFieldNames]));
        }

        var otherCrewHours =
            Value(entry.SeIcusDay) + Value(entry.SeIcusNight) +
            Value(entry.SeDualDay) + Value(entry.SeDualNight) +
            Value(entry.MeIcusDay) + Value(entry.MeIcusNight) +
            Value(entry.MeDualDay) + Value(entry.MeDualNight) +
            Value(entry.CopilotDay) + Value(entry.CopilotNight);
        if (IsBlank(entry.OtherPilotOrCrew) && otherCrewHours > 0)
        {
            warnings.Add(Warning("NEWENTRY-W017", PortableLogbookEntryMessages.CrewHoursWithoutCrew,
                nameof(entry.OtherPilotOrCrew), OtherCrewHourFieldNames));
        }

        var singleEngineHours = SingleEngineValues(entry).Sum();
        var multiEngineHours = MultiEngineValues(entry).Sum();
        if (singleEngineHours > 0 && multiEngineHours > 0)
        {
            warnings.Add(Warning("NEWENTRY-W018", PortableLogbookEntryMessages.MixedEngineHours,
                nameof(entry.Type), [.. SingleEngineFieldNames, .. MultiEngineFieldNames]));
        }

        if (singleEngineHours > 0 && multiEngineHours == 0 && HasAircraftTypeHours(comparableEntries, entry.Type, MultiEngineValues))
        {
            warnings.Add(Warning("NEWENTRY-W019", PortableLogbookEntryMessages.ExpectedMultiEngineHours,
                nameof(entry.Type), SingleEngineFieldNames));
        }
        else if (multiEngineHours > 0 && singleEngineHours == 0 && HasAircraftTypeHours(comparableEntries, entry.Type, SingleEngineValues))
        {
            warnings.Add(Warning("NEWENTRY-W020", PortableLogbookEntryMessages.ExpectedSingleEngineHours,
                nameof(entry.Type), MultiEngineFieldNames));
        }

        if (!IsBlank(entry.Type) && !IsBlank(entry.Reg) && comparableEntries.Any(existing =>
                Same(entry.Reg, existing.Entry!.Reg) &&
                !IsBlank(existing.Entry.Type) &&
                !Same(entry.Type, existing.Entry.Type)))
        {
            warnings.Add(Warning("NEWENTRY-W021", PortableLogbookEntryMessages.RegistrationTypeMismatch,
                nameof(entry.Type), nameof(entry.Reg)));
        }

        if (comparableEntries.Any(existing => IsDuplicate(entry, existing.Entry!)))
        {
            warnings.Add(Warning("NEWENTRY-W022", PortableLogbookEntryMessages.PossibleWorkbookDuplicate,
                nameof(entry.Year), nameof(entry.Month), nameof(entry.Day), nameof(entry.Type), nameof(entry.Reg), nameof(entry.Remarks)));
        }

        return warnings;
    }

    private static void AddAirportWarnings(
        PortableLogbookWorkbookEntry entry,
        IReadOnlyList<PortableLogbookMaterializedEntryV2> comparableEntries,
        MobileAirportCatalog catalog,
        decimal flightTime,
        ICollection<MobileWorkbookEntryIssue> warnings)
    {
        var fromRecognised = catalog.TryFind(entry.From, out var fromAirport);
        var toRecognised = catalog.TryFind(entry.To, out var toAirport);
        if (!IsBlank(entry.From) && !fromRecognised || !IsBlank(entry.To) && !toRecognised)
        {
            var unrecognisedFrom = !IsBlank(entry.From) && !fromRecognised;
            var unrecognisedTo = !IsBlank(entry.To) && !toRecognised;
            if (unrecognisedFrom)
            {
                warnings.Add(Warning(
                    "NEWENTRY-W004",
                    PortableLogbookEntryMessages.UnrecognisedDeparture,
                    nameof(entry.From)));
            }
            if (unrecognisedTo)
            {
                warnings.Add(Warning(
                    "NEWENTRY-W004",
                    PortableLogbookEntryMessages.UnrecognisedDestination,
                    nameof(entry.To)));
            }
        }

        var distantLines = new List<string>();
        MobileWorkbookEntryIssue? highSpeedWarning = null;
        var visitedAirports = VisitedAirports(comparableEntries, catalog);
        AddDistantAirportLine("Departure", fromRecognised ? fromAirport : null, visitedAirports, distantLines);
        AddDistantAirportLine("Destination", toRecognised ? toAirport : null, visitedAirports, distantLines);
        if (fromRecognised && toRecognised && !Same(fromAirport.Icao, toAirport.Icao))
        {
            var routeDistance = MobileAirportCatalog.GreatCircleDistanceNm(fromAirport, toAirport);
            if (routeDistance >= RemoteAirportWarningThresholdNm)
            {
                distantLines.Add(PortableLogbookEntryMessages.RouteDistance(
                    AirportLabel(fromAirport), AirportLabel(toAirport), Rounded(routeDistance)));
            }

            if (flightTime > 0)
            {
                var impliedSpeed = routeDistance / (double)flightTime;
                if (impliedSpeed > HighSpeedRouteWarningThresholdKt)
                {
                    highSpeedWarning = Warning(
                        "NEWENTRY-W006",
                        PortableLogbookEntryMessages.RouteSpeed(
                            AirportLabel(fromAirport), AirportLabel(toAirport), Rounded(routeDistance), flightTime, Rounded(impliedSpeed)),
                        nameof(entry.To), [nameof(entry.From), .. FlightTimeFieldNames]);
                }
            }
        }

        if (distantLines.Count > 0)
        {
            warnings.Add(Warning(
                "NEWENTRY-W005",
                PortableLogbookEntryMessages.UnusualRouteDistances(distantLines),
                nameof(entry.To), nameof(entry.From)));
        }
        if (highSpeedWarning is not null)
        {
            warnings.Add(highSpeedWarning);
        }
    }

    private static void AddDistantAirportLine(
        string fieldLabel,
        MobileAirport? airport,
        IReadOnlyCollection<MobileAirport> visitedAirports,
        ICollection<string> lines)
    {
        if (airport is null || visitedAirports.Count == 0)
        {
            return;
        }

        var nearest = visitedAirports
            .Select(visited => new
            {
                Airport = visited,
                Distance = MobileAirportCatalog.GreatCircleDistanceNm(airport, visited)
            })
            .OrderBy(candidate => candidate.Distance)
            .First();
        if (nearest.Distance >= RemoteAirportWarningThresholdNm)
        {
            lines.Add(PortableLogbookEntryMessages.AirportDistance(
                fieldLabel, AirportLabel(airport), Rounded(nearest.Distance), AirportLabel(nearest.Airport)));
        }
    }

    private static IReadOnlyCollection<MobileAirport> VisitedAirports(
        IEnumerable<PortableLogbookMaterializedEntryV2> entries,
        MobileAirportCatalog catalog)
    {
        var visited = new Dictionary<string, MobileAirport>(StringComparer.OrdinalIgnoreCase);
        foreach (var materialized in entries)
        {
            var entry = materialized.Entry!;
            if (Value(entry.IfrSim) > 0 && FlightTime(entry) == 0)
            {
                continue;
            }

            AddVisitedAirport(entry.From, catalog, visited);
            AddVisitedAirport(entry.To, catalog, visited);
            AddVisitedAirportTokens(entry.Via, catalog, visited);
            AddVisitedAirportTokens(entry.Remarks, catalog, visited);
        }

        return visited.Values.ToArray();
    }

    private static void AddVisitedAirport(
        string? code,
        MobileAirportCatalog catalog,
        IDictionary<string, MobileAirport> visited)
    {
        if (catalog.TryFind(code, out var airport))
        {
            visited.TryAdd(airport.Icao, airport);
        }
    }

    private static void AddVisitedAirportTokens(
        string? text,
        MobileAirportCatalog catalog,
        IDictionary<string, MobileAirport> visited)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        foreach (var token in text.Replace("|", string.Empty, StringComparison.Ordinal)
                     .Split(AirportTokenDelimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!AirportTokenIsIgnored(token))
            {
                AddVisitedAirport(token, catalog, visited);
            }
        }
    }

    private static bool AirportTokenIsIgnored(string token)
    {
        var normalised = new string(token.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return IgnoredAirportTokens.Contains(token) ||
            DefaultKeywordPhrases.Any(keyword => keyword.Contains(normalised, StringComparison.Ordinal));
    }

    private static bool HasAircraftTypeHours(
        IEnumerable<PortableLogbookMaterializedEntryV2> entries,
        string? aircraftType,
        Func<PortableLogbookWorkbookEntry, IEnumerable<decimal>> values) =>
        !IsBlank(aircraftType) && entries.Any(existing =>
            Same(aircraftType, existing.Entry!.Type) && values(existing.Entry).Sum() > 0);

    private static bool IsDuplicate(PortableLogbookWorkbookEntry draft, PortableLogbookWorkbookEntry existing) =>
        draft.Date == existing.Date &&
        Same(draft.Type, existing.Type) &&
        Same(draft.Reg, existing.Reg) &&
        Same(draft.Remarks, existing.Remarks);

    private static MobileWorkbookEntryIssue Error(string code, string message, params string[] fields) =>
        new(code, message, fields, null);

    private static MobileWorkbookEntryIssue Warning(
        string code,
        string message,
        string affectedField,
        params string[] relatedFields) =>
        new(code, message, [affectedField, .. relatedFields], affectedField);

    private static string AirportLabel(MobileAirport airport) =>
        string.IsNullOrWhiteSpace(airport.Name) ? airport.Icao : $"{airport.Icao} ({airport.Name})";

    private static string Rounded(double value) =>
        value.ToString("#,##0", CultureInfo.InvariantCulture);

    private static bool IsNumeric(string value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out _) ||
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _);

    private static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);

    private static bool Same(string? first, string? second) =>
        string.Equals(first?.Trim(), second?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static decimal Value(decimal? value) => value.GetValueOrDefault();

    private static decimal FlightTime(PortableLogbookWorkbookEntry entry) => FlightTimeValues(entry).Sum();

    private static IEnumerable<decimal> FlightHourValues(PortableLogbookWorkbookEntry entry) =>
        FlightTimeValues(entry).Append(Value(entry.IfrSim));

    private static IEnumerable<decimal> FlightTimeValues(PortableLogbookWorkbookEntry entry) =>
        SingleEngineValues(entry)
            .Concat(MultiEngineValues(entry))
            .Concat([Value(entry.CopilotDay), Value(entry.CopilotNight)]);

    private static IEnumerable<decimal> DayFlightTimeValues(PortableLogbookWorkbookEntry entry) =>
    [
        Value(entry.SeIcusDay), Value(entry.SeDualDay), Value(entry.SeCommandDay),
        Value(entry.MeIcusDay), Value(entry.MeDualDay), Value(entry.MeCommandDay),
        Value(entry.CopilotDay)
    ];

    private static IEnumerable<decimal> NightFlightTimeValues(PortableLogbookWorkbookEntry entry) =>
    [
        Value(entry.SeIcusNight), Value(entry.SeDualNight), Value(entry.SeCommandNight),
        Value(entry.MeIcusNight), Value(entry.MeDualNight), Value(entry.MeCommandNight),
        Value(entry.CopilotNight)
    ];

    private static IEnumerable<decimal> SingleEngineValues(PortableLogbookWorkbookEntry entry) =>
    [
        Value(entry.SeIcusDay), Value(entry.SeIcusNight),
        Value(entry.SeDualDay), Value(entry.SeDualNight),
        Value(entry.SeCommandDay), Value(entry.SeCommandNight)
    ];

    private static IEnumerable<decimal> MultiEngineValues(PortableLogbookWorkbookEntry entry) =>
    [
        Value(entry.MeIcusDay), Value(entry.MeIcusNight),
        Value(entry.MeDualDay), Value(entry.MeDualNight),
        Value(entry.MeCommandDay), Value(entry.MeCommandNight)
    ];

    private static IEnumerable<int> ApproachValues(PortableLogbookWorkbookEntry entry) =>
    [
        entry.Ils.GetValueOrDefault(), entry.Vor.GetValueOrDefault(), entry.Rnp.GetValueOrDefault(),
        entry.Ndb.GetValueOrDefault(), entry.DgaCdi.GetValueOrDefault(), entry.DgaAzi.GetValueOrDefault(),
        entry.Circling.GetValueOrDefault()
    ];

    private static string MostUsedApproachField(PortableLogbookWorkbookEntry entry)
    {
        var values = ApproachValues(entry).ToArray();
        var largestIndex = Array.IndexOf(values, values.Max());
        return ApproachFieldNames[largestIndex];
    }

    private static readonly string[] FlightTimeFieldNames =
    [
        nameof(PortableLogbookWorkbookEntry.SeIcusDay), nameof(PortableLogbookWorkbookEntry.SeIcusNight),
        nameof(PortableLogbookWorkbookEntry.SeDualDay), nameof(PortableLogbookWorkbookEntry.SeDualNight),
        nameof(PortableLogbookWorkbookEntry.SeCommandDay), nameof(PortableLogbookWorkbookEntry.SeCommandNight),
        nameof(PortableLogbookWorkbookEntry.MeIcusDay), nameof(PortableLogbookWorkbookEntry.MeIcusNight),
        nameof(PortableLogbookWorkbookEntry.MeDualDay), nameof(PortableLogbookWorkbookEntry.MeDualNight),
        nameof(PortableLogbookWorkbookEntry.MeCommandDay), nameof(PortableLogbookWorkbookEntry.MeCommandNight),
        nameof(PortableLogbookWorkbookEntry.CopilotDay), nameof(PortableLogbookWorkbookEntry.CopilotNight)
    ];

    private static readonly string[] FlightHourFieldNames = [.. FlightTimeFieldNames, nameof(PortableLogbookWorkbookEntry.IfrSim)];

    private static readonly string[] DayFlightTimeFieldNames =
    [
        nameof(PortableLogbookWorkbookEntry.SeIcusDay), nameof(PortableLogbookWorkbookEntry.SeDualDay),
        nameof(PortableLogbookWorkbookEntry.SeCommandDay), nameof(PortableLogbookWorkbookEntry.MeIcusDay),
        nameof(PortableLogbookWorkbookEntry.MeDualDay), nameof(PortableLogbookWorkbookEntry.MeCommandDay),
        nameof(PortableLogbookWorkbookEntry.CopilotDay)
    ];

    private static readonly string[] NightFlightTimeFieldNames =
    [
        nameof(PortableLogbookWorkbookEntry.SeIcusNight), nameof(PortableLogbookWorkbookEntry.SeDualNight),
        nameof(PortableLogbookWorkbookEntry.SeCommandNight), nameof(PortableLogbookWorkbookEntry.MeIcusNight),
        nameof(PortableLogbookWorkbookEntry.MeDualNight), nameof(PortableLogbookWorkbookEntry.MeCommandNight),
        nameof(PortableLogbookWorkbookEntry.CopilotNight)
    ];

    private static readonly string[] SingleEngineFieldNames = FlightTimeFieldNames.Where(field => field.StartsWith("Se", StringComparison.Ordinal)).ToArray();
    private static readonly string[] MultiEngineFieldNames = FlightTimeFieldNames.Where(field => field.StartsWith("Me", StringComparison.Ordinal)).ToArray();

    private static readonly string[] OtherCrewHourFieldNames =
    [
        nameof(PortableLogbookWorkbookEntry.SeIcusDay), nameof(PortableLogbookWorkbookEntry.SeIcusNight),
        nameof(PortableLogbookWorkbookEntry.SeDualDay), nameof(PortableLogbookWorkbookEntry.SeDualNight),
        nameof(PortableLogbookWorkbookEntry.MeIcusDay), nameof(PortableLogbookWorkbookEntry.MeIcusNight),
        nameof(PortableLogbookWorkbookEntry.MeDualDay), nameof(PortableLogbookWorkbookEntry.MeDualNight),
        nameof(PortableLogbookWorkbookEntry.CopilotDay), nameof(PortableLogbookWorkbookEntry.CopilotNight)
    ];

    private static readonly string[] ApproachFieldNames =
    [
        nameof(PortableLogbookWorkbookEntry.Ils), nameof(PortableLogbookWorkbookEntry.Vor),
        nameof(PortableLogbookWorkbookEntry.Rnp), nameof(PortableLogbookWorkbookEntry.Ndb),
        nameof(PortableLogbookWorkbookEntry.DgaCdi), nameof(PortableLogbookWorkbookEntry.DgaAzi),
        nameof(PortableLogbookWorkbookEntry.Circling)
    ];
}

public sealed record MobileWorkbookEntryIssue(
    string Code,
    string Message,
    IReadOnlyList<string> Fields,
    string? AffectedField);
