namespace ElectronicLogbook.Portable;

public static class PortableLogbookEntryFields
{
    public static IReadOnlyDictionary<string, object?> ToFieldValues(PortableLogbookEntry entry) =>
        new Dictionary<string, object?>
        {
            ["date"] = entry.Date,
            ["aircraftType"] = entry.AircraftType,
            ["registration"] = entry.Registration,
            ["flightNumber"] = entry.FlightNumber,
            ["from"] = entry.From,
            ["to"] = entry.To,
            ["route"] = entry.Route,
            ["details"] = entry.Details,
            ["multiPilot"] = entry.MultiPilot,
            ["pilotInCommand"] = entry.PilotInCommand,
            ["coPilot"] = entry.CoPilot,
            ["dual"] = entry.Dual,
            ["instructor"] = entry.Instructor,
            ["day"] = entry.Day,
            ["night"] = entry.Night,
            ["instrumentActual"] = entry.InstrumentActual,
            ["instrumentSimulated"] = entry.InstrumentSimulated,
            ["takeoffsDay"] = entry.TakeoffsDay,
            ["takeoffsNight"] = entry.TakeoffsNight,
            ["landingsDay"] = entry.LandingsDay,
            ["landingsNight"] = entry.LandingsNight,
            ["ifrApproaches"] = entry.IfrApproaches,
            ["holding"] = entry.Holding,
            ["rnav"] = entry.Rnav,
            ["circling"] = entry.Circling
        };

    public static PortableLogbookEntry FromFieldValues(
        IReadOnlyDictionary<string, object?> values,
        IReadOnlyDictionary<CustomFieldId, string?>? customFields = null)
    {
        ArgumentNullException.ThrowIfNull(values);

        foreach (var key in values.Keys.Where(key => !PortableLogbookFields.ById.ContainsKey(key)))
        {
            throw new ArgumentException($"Unknown portable logbook field '{key}'.", nameof(values));
        }

        return new PortableLogbookEntry(
            Get<DateOnly>(values, "date"),
            GetString(values, "aircraftType"),
            GetString(values, "registration"),
            GetString(values, "flightNumber"),
            GetString(values, "from"),
            GetString(values, "to"),
            GetString(values, "route"),
            GetString(values, "details"),
            Get<decimal>(values, "multiPilot"),
            Get<decimal>(values, "pilotInCommand"),
            Get<decimal>(values, "coPilot"),
            Get<decimal>(values, "dual"),
            Get<decimal>(values, "instructor"),
            Get<decimal>(values, "day"),
            Get<decimal>(values, "night"),
            Get<decimal>(values, "instrumentActual"),
            Get<decimal>(values, "instrumentSimulated"),
            Get<int>(values, "takeoffsDay"),
            Get<int>(values, "takeoffsNight"),
            Get<int>(values, "landingsDay"),
            Get<int>(values, "landingsNight"),
            Get<int>(values, "ifrApproaches"),
            Get<int>(values, "holding"),
            Get<int>(values, "rnav"),
            Get<int>(values, "circling"),
            customFields ?? new Dictionary<CustomFieldId, string?>());
    }

    private static T? Get<T>(IReadOnlyDictionary<string, object?> values, string key)
        where T : struct
    {
        if (!values.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            T typed => typed,
            IConvertible convertible => (T)Convert.ChangeType(convertible, typeof(T), System.Globalization.CultureInfo.InvariantCulture),
            _ => throw new ArgumentException($"Field '{key}' cannot be converted to {typeof(T).Name}.", nameof(values))
        };
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value.ToString();
    }
}
