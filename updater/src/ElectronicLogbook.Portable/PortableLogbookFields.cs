namespace ElectronicLogbook.Portable;

public static class PortableLogbookFields
{
    public const int CurrentFieldSchemaVersion = 1;

    public static IReadOnlyList<PortableLogbookFieldDefinition> RawFlightFields { get; } =
    [
        Text("date", "Date", PortableLogbookFieldKind.Date),
        Text("aircraftType", "Aircraft Type"),
        Text("registration", "Reg"),
        Text("flightNumber", "Flight Number"),
        Text("from", "From"),
        Text("to", "To"),
        Text("route", "Route"),
        Text("details", "Details"),
        Duration("multiPilot", "Multi-Pilot"),
        Duration("pilotInCommand", "PIC"),
        Duration("coPilot", "Co-Pilot"),
        Duration("dual", "Dual"),
        Duration("instructor", "Instructor"),
        Duration("day", "Day"),
        Duration("night", "Night"),
        Duration("instrumentActual", "Instrument Actual"),
        Duration("instrumentSimulated", "Instrument Simulated"),
        Count("takeoffsDay", "Takeoffs Day"),
        Count("takeoffsNight", "Takeoffs Night"),
        Count("landingsDay", "Landings Day"),
        Count("landingsNight", "Landings Night"),
        Count("ifrApproaches", "IFR Approaches"),
        Count("holding", "Holding"),
        Count("rnav", "RNP"),
        Count("circling", "Circling")
    ];

    public static IReadOnlyDictionary<string, PortableLogbookFieldDefinition> ById { get; } =
        RawFlightFields.ToDictionary(field => field.Id, StringComparer.Ordinal);

    private static PortableLogbookFieldDefinition Text(
        string id,
        string workbookColumnName,
        PortableLogbookFieldKind kind = PortableLogbookFieldKind.Text) =>
        new(id, workbookColumnName, kind);

    private static PortableLogbookFieldDefinition Duration(string id, string workbookColumnName) =>
        new(id, workbookColumnName, PortableLogbookFieldKind.DecimalHours);

    private static PortableLogbookFieldDefinition Count(string id, string workbookColumnName) =>
        new(id, workbookColumnName, PortableLogbookFieldKind.Count);
}

public sealed record PortableLogbookFieldDefinition(
    string Id,
    string WorkbookColumnName,
    PortableLogbookFieldKind Kind);

public enum PortableLogbookFieldKind
{
    Date,
    Text,
    DecimalHours,
    Count
}
