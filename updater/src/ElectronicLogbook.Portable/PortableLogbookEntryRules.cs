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
                "The Date field is not valid or is in the future."));
        }

        if (IsBlank(entry.AircraftType))
        {
            errors.Add(new PortableLogbookEntryRuleViolation(
                PortableLogbookEntryRuleCode.MissingAircraftType,
                "Aircraft type is required before this entry can be added."));
        }

        var simulatorTime = Value(entry.InstrumentSimulated);
        var isSimulatorEntry = simulatorTime > 0;
        if (!isSimulatorEntry)
        {
            if (IsBlank(entry.Registration))
            {
                errors.Add(new PortableLogbookEntryRuleViolation(
                    PortableLogbookEntryRuleCode.MissingRegistration,
                    "Registration is required for a flight entry."));
            }

            if (IsBlank(entry.From))
            {
                errors.Add(new PortableLogbookEntryRuleViolation(
                    PortableLogbookEntryRuleCode.MissingDeparture,
                    "Departure airport is required for a flight entry."));
            }

            if (IsBlank(entry.To))
            {
                errors.Add(new PortableLogbookEntryRuleViolation(
                    PortableLogbookEntryRuleCode.MissingDestination,
                    "Destination airport is required for a flight entry."));
            }
        }

        ValidateNonNegativeDurations(entry, errors);
        ValidateNonNegativeCounts(entry, errors);

        var flightTime = FlightTime(entry);
        if (flightTime + simulatorTime <= 0)
        {
            errors.Add(new PortableLogbookEntryRuleViolation(
                PortableLogbookEntryRuleCode.MissingLoggedTime,
                "Total flight or simulator time cannot be zero."));
        }

        if (Value(entry.InstrumentActual) > flightTime)
        {
            errors.Add(new PortableLogbookEntryRuleViolation(
                PortableLogbookEntryRuleCode.InstrumentActualExceedsFlightTime,
                "In-flight instrument time cannot be greater than the total flight time for this entry."));
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
        if (FlightTime(entry) > 0 && totalLandings == 0 && !hasCopilotFlightTime)
        {
            warnings.Add(new PortableLogbookEntryRuleWarning(
                PortableLogbookEntryRuleWarningCode.FlightTimeWithoutLanding,
                "This entry has flight time but no landings."));
        }

        if (Value(entry.Day) > 0 && landingsDay == 0 && !hasCopilotFlightTime)
        {
            warnings.Add(new PortableLogbookEntryRuleWarning(
                PortableLogbookEntryRuleWarningCode.DayTimeWithoutDayLanding,
                "This entry has day time but no day landing."));
        }

        if (landingsDay > 0 && Value(entry.Day) == 0)
        {
            warnings.Add(new PortableLogbookEntryRuleWarning(
                PortableLogbookEntryRuleWarningCode.DayLandingWithoutDayTime,
                "This entry has a day landing but no day time."));
        }

        if (Value(entry.Night) > 0 && landingsNight == 0 && !hasCopilotFlightTime)
        {
            warnings.Add(new PortableLogbookEntryRuleWarning(
                PortableLogbookEntryRuleWarningCode.NightTimeWithoutNightLanding,
                "This entry has night time but no night landing."));
        }

        if (landingsNight > 0 && Value(entry.Night) == 0)
        {
            warnings.Add(new PortableLogbookEntryRuleWarning(
                PortableLogbookEntryRuleWarningCode.NightLandingWithoutNightTime,
                "This entry has a night landing but no night time."));
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
                "This entry has approach activity but no instrument time."));
        }

        if (instrumentTime > 0 && totalApproaches == 0)
        {
            warnings.Add(new PortableLogbookEntryRuleWarning(
                PortableLogbookEntryRuleWarningCode.InstrumentTimeWithoutApproach,
                "This entry has instrument time but no approach activity."));
        }

        var flightTime = FlightTime(entry);
        if (flightTime > 0 && totalLandings > flightTime * 6)
        {
            warnings.Add(new PortableLogbookEntryRuleWarning(
                PortableLogbookEntryRuleWarningCode.HighLandingsForFlightTime,
                "The number of landings seems high compared with the total flight time."));
        }

        if (flightTime > 0 && totalApproaches > flightTime * 3)
        {
            warnings.Add(new PortableLogbookEntryRuleWarning(
                PortableLogbookEntryRuleWarningCode.HighApproachesForFlightTime,
                "The number of approaches seems high compared with the total flight time."));
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
                $"{value.Label} cannot be negative."));
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
                $"{value.Label} cannot be negative."));
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
        yield return ("RNAV", entry.Rnav);
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
