namespace ElectronicLogbook.Portable;

public static class PortableLogbookEntryRules
{
    public static PortableLogbookEntryRuleResult Validate(
        PortableLogbookEntry entry,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var errors = new List<PortableLogbookEntryRuleViolation>();
        if (entry.Date is null || entry.Date > today)
        {
            errors.Add(new PortableLogbookEntryRuleViolation(
                PortableLogbookEntryRuleCode.InvalidDate,
                PortableLogbookEntryMessages.InvalidDate));
        }

        if (IsBlank(entry.AircraftType))
        {
            errors.Add(new PortableLogbookEntryRuleViolation(
                PortableLogbookEntryRuleCode.MissingAircraftType,
                PortableLogbookEntryMessages.MissingAircraftType));
        }

        var simulatorTime = Value(entry.InstrumentSimulated);
        var isSimulatorEntry = simulatorTime > 0;
        if (!isSimulatorEntry)
        {
            if (IsBlank(entry.Registration))
            {
                errors.Add(new PortableLogbookEntryRuleViolation(
                    PortableLogbookEntryRuleCode.MissingRegistration,
                    PortableLogbookEntryMessages.MissingRegistration));
            }

            if (IsBlank(entry.From))
            {
                errors.Add(new PortableLogbookEntryRuleViolation(
                    PortableLogbookEntryRuleCode.MissingDeparture,
                    PortableLogbookEntryMessages.MissingDeparture));
            }

            if (IsBlank(entry.To))
            {
                errors.Add(new PortableLogbookEntryRuleViolation(
                    PortableLogbookEntryRuleCode.MissingDestination,
                    PortableLogbookEntryMessages.MissingDestination));
            }
        }

        ValidateNonNegativeDurations(entry, errors);
        ValidateNonNegativeCounts(entry, errors);

        var flightTime = FlightTime(entry);
        if (flightTime + simulatorTime <= 0)
        {
            errors.Add(new PortableLogbookEntryRuleViolation(
                PortableLogbookEntryRuleCode.MissingLoggedTime,
                PortableLogbookEntryMessages.MissingLoggedTime));
        }

        if (Value(entry.InstrumentActual) > flightTime)
        {
            errors.Add(new PortableLogbookEntryRuleViolation(
                PortableLogbookEntryRuleCode.InstrumentActualExceedsFlightTime,
                PortableLogbookEntryMessages.InstrumentTimeExceedsFlightTime));
        }

        return new PortableLogbookEntryRuleResult(errors.Count == 0, errors);
    }

    public static decimal FlightTime(PortableLogbookEntry entry) =>
        Value(entry.MultiPilot) +
        Value(entry.PilotInCommand) +
        Value(entry.CoPilot) +
        Value(entry.Dual) +
        Value(entry.Instructor);

    public static decimal LoggedTime(PortableLogbookEntry entry) =>
        FlightTime(entry) + Value(entry.InstrumentSimulated);

    public static IReadOnlyList<PortableLogbookEntryRuleWarning> Warn(PortableLogbookEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var warnings = new List<PortableLogbookEntryRuleWarning>();
        var landingsDay = entry.LandingsDay.GetValueOrDefault();
        var landingsNight = entry.LandingsNight.GetValueOrDefault();
        var totalLandings = landingsDay + landingsNight;
        var hasCopilotFlightTime = Value(entry.CoPilot) > 0;
        var flightTime = FlightTime(entry);
        var dayNightTime = Value(entry.Day) + Value(entry.Night);
        if (flightTime > 0 && dayNightTime == 0)
        {
            warnings.Add(new PortableLogbookEntryRuleWarning(
                PortableLogbookEntryRuleWarningCode.FlightTimeWithoutDayOrNight,
                PortableLogbookEntryMessages.FlightTimeWithoutDayOrNight));
        }

        if (flightTime > 0 && dayNightTime > flightTime)
        {
            warnings.Add(new PortableLogbookEntryRuleWarning(
                PortableLogbookEntryRuleWarningCode.DayNightTimeExceedsFlightTime,
                PortableLogbookEntryMessages.DayNightTimeExceedsFlightTime));
        }

        if (flightTime > 0 && totalLandings == 0 && !hasCopilotFlightTime)
        {
            warnings.Add(new PortableLogbookEntryRuleWarning(
                PortableLogbookEntryRuleWarningCode.FlightTimeWithoutLanding,
                PortableLogbookEntryMessages.FlightTimeWithoutLanding));
        }

        if (Value(entry.Day) > 0 && landingsDay == 0 && !hasCopilotFlightTime)
        {
            warnings.Add(new PortableLogbookEntryRuleWarning(
                PortableLogbookEntryRuleWarningCode.DayTimeWithoutDayLanding,
                PortableLogbookEntryMessages.DayTimeWithoutDayLanding));
        }

        if (landingsDay > 0 && Value(entry.Day) == 0)
        {
            warnings.Add(new PortableLogbookEntryRuleWarning(
                PortableLogbookEntryRuleWarningCode.DayLandingWithoutDayTime,
                PortableLogbookEntryMessages.DayLandingWithoutDayTime));
        }

        if (Value(entry.Night) > 0 && landingsNight == 0 && !hasCopilotFlightTime)
        {
            warnings.Add(new PortableLogbookEntryRuleWarning(
                PortableLogbookEntryRuleWarningCode.NightTimeWithoutNightLanding,
                PortableLogbookEntryMessages.NightTimeWithoutNightLanding));
        }

        if (landingsNight > 0 && Value(entry.Night) == 0)
        {
            warnings.Add(new PortableLogbookEntryRuleWarning(
                PortableLogbookEntryRuleWarningCode.NightLandingWithoutNightTime,
                PortableLogbookEntryMessages.NightLandingWithoutNightTime));
        }

        var totalApproaches =
            entry.IfrApproaches.GetValueOrDefault() +
            entry.Holding.GetValueOrDefault() +
            entry.Rnav.GetValueOrDefault() +
            entry.Circling.GetValueOrDefault();
        var instrumentTime = Value(entry.InstrumentActual) + Value(entry.InstrumentSimulated);
        if (totalApproaches > 0 && instrumentTime == 0)
        {
            warnings.Add(new PortableLogbookEntryRuleWarning(
                PortableLogbookEntryRuleWarningCode.ApproachWithoutInstrumentTime,
                PortableLogbookEntryMessages.ApproachWithoutInstrumentTime));
        }

        if (instrumentTime > 0 && totalApproaches == 0)
        {
            warnings.Add(new PortableLogbookEntryRuleWarning(
                PortableLogbookEntryRuleWarningCode.InstrumentTimeWithoutApproach,
                PortableLogbookEntryMessages.InstrumentTimeWithoutApproach));
        }

        if (flightTime > 0 && totalLandings > flightTime * 6)
        {
            warnings.Add(new PortableLogbookEntryRuleWarning(
                PortableLogbookEntryRuleWarningCode.HighLandingsForFlightTime,
                PortableLogbookEntryMessages.HighLandingsForFlightTime));
        }

        if (flightTime > 0 && totalApproaches > flightTime * 3)
        {
            warnings.Add(new PortableLogbookEntryRuleWarning(
                PortableLogbookEntryRuleWarningCode.HighApproachesForFlightTime,
                PortableLogbookEntryMessages.HighApproachesForFlightTime));
        }

        return warnings;
    }

    private static void ValidateNonNegativeDurations(
        PortableLogbookEntry entry,
        List<PortableLogbookEntryRuleViolation> errors)
    {
        foreach (var value in Durations(entry))
        {
            if (value.Value >= 0)
            {
                continue;
            }

            errors.Add(new PortableLogbookEntryRuleViolation(
                PortableLogbookEntryRuleCode.NegativeDuration,
                PortableLogbookEntryMessages.NegativeValue(value.Label)));
        }
    }

    private static void ValidateNonNegativeCounts(
        PortableLogbookEntry entry,
        List<PortableLogbookEntryRuleViolation> errors)
    {
        foreach (var value in Counts(entry))
        {
            if (value.Value is null or >= 0)
            {
                continue;
            }

            errors.Add(new PortableLogbookEntryRuleViolation(
                PortableLogbookEntryRuleCode.NegativeCount,
                PortableLogbookEntryMessages.NegativeValue(value.Label)));
        }
    }

    private static IEnumerable<(string Label, decimal Value)> Durations(PortableLogbookEntry entry)
    {
        yield return ("Multi-pilot", Value(entry.MultiPilot));
        yield return ("PIC", Value(entry.PilotInCommand));
        yield return ("Co-pilot", Value(entry.CoPilot));
        yield return ("Dual", Value(entry.Dual));
        yield return ("Instructor", Value(entry.Instructor));
        yield return ("Day", Value(entry.Day));
        yield return ("Night", Value(entry.Night));
        yield return ("Instrument actual", Value(entry.InstrumentActual));
        yield return ("Instrument simulated", Value(entry.InstrumentSimulated));
    }

    private static IEnumerable<(string Label, int? Value)> Counts(PortableLogbookEntry entry)
    {
        yield return ("Takeoffs day", entry.TakeoffsDay);
        yield return ("Takeoffs night", entry.TakeoffsNight);
        yield return ("Landings day", entry.LandingsDay);
        yield return ("Landings night", entry.LandingsNight);
        yield return ("IFR approaches", entry.IfrApproaches);
        yield return ("Holding", entry.Holding);
        yield return ("RNP", entry.Rnav);
        yield return ("Circling", entry.Circling);
    }

    private static decimal Value(decimal? value) => value.GetValueOrDefault();

    private static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);
}

public sealed record PortableLogbookEntryRuleResult(
    bool IsValid,
    IReadOnlyList<PortableLogbookEntryRuleViolation> Errors);

public sealed record PortableLogbookEntryRuleViolation(
    PortableLogbookEntryRuleCode Code,
    string Message);

public sealed record PortableLogbookEntryRuleWarning(
    PortableLogbookEntryRuleWarningCode Code,
    string Message);

public enum PortableLogbookEntryRuleCode
{
    InvalidDate,
    MissingAircraftType,
    MissingRegistration,
    MissingDeparture,
    MissingDestination,
    MissingLoggedTime,
    InstrumentActualExceedsFlightTime,
    NegativeDuration,
    NegativeCount
}

public enum PortableLogbookEntryRuleWarningCode
{
    FlightTimeWithoutDayOrNight,
    DayNightTimeExceedsFlightTime,
    FlightTimeWithoutLanding,
    DayTimeWithoutDayLanding,
    DayLandingWithoutDayTime,
    NightTimeWithoutNightLanding,
    NightLandingWithoutNightTime,
    ApproachWithoutInstrumentTime,
    InstrumentTimeWithoutApproach,
    HighLandingsForFlightTime,
    HighApproachesForFlightTime
}
