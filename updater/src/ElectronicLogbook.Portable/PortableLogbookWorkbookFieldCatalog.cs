namespace ElectronicLogbook.Portable;

public static class PortableLogbookWorkbookFieldCatalog
{
    public const int SchemaVersion = 2;
    public const string EntryIdColumnName = "EntryID";

    public static IReadOnlyList<PortableLogbookWorkbookFieldDefinition> PilotEnteredFields { get; } =
    [
        Text("dateYear", "Year"),
        Text("dateMonth", "Month"),
        Text("dateDay", "Day"),
        Text("type", "Type"),
        Text("reg", "Reg"),
        Text("flightId", "Flight ID"),
        Text("pic", "PIC"),
        Text("otherPilotOrCrew", "Other Pilot or Crew"),
        Text("from", "From"),
        Text("to", "To"),
        Text("via", "Via"),
        Text("remarks", "Remarks"),
        Flag("fr", "FR"),
        Flag("ipc", "IPC"),
        Flag("opc", "OPC"),
        Text("custom1", "Custom 1"),
        Text("custom2", "Custom 2"),
        Text("custom3", "Custom 3"),
        Text("custom4", "Custom 4"),
        Hours("seIcusDay", "SeIcusDay"),
        Hours("seIcusNight", "SeIcusNight"),
        Hours("seDualDay", "SeDualDay"),
        Hours("seDualNight", "SeDualNight"),
        Hours("seCommandDay", "SeCommandDay"),
        Hours("seCommandNight", "SeCommandNight"),
        Hours("meIcusDay", "MeIcusDay"),
        Hours("meIcusNight", "MeIcusNight"),
        Hours("meDualDay", "MeDualDay"),
        Hours("meDualNight", "MeDualNight"),
        Hours("meCommandDay", "MeCommandDay"),
        Hours("meCommandNight", "MeCommandNight"),
        Hours("copilotDay", "CopilotDay"),
        Hours("copilotNight", "CopilotNight"),
        Hours("ifrIf", "IfrIf"),
        Hours("ifrSim", "IfrSim"),
        Count("landingsDay", "LandingsDay"),
        Count("landingsNight", "LandingsNight"),
        Count("ils", "ILS"),
        Count("vor", "VOR"),
        Count("rnp", "RNP"),
        Count("ndb", "NDB"),
        Count("dgaCdi", "DGA (CDI)"),
        Count("dgaAzi", "DGA (Azi)"),
        Count("circling", "Circling")
    ];

    public static IReadOnlyList<string> CalculatedProjectionColumnNames { get; } =
    [
        "Date",
        "TotalHours",
        "TotalApps",
        "CumLandingsDay",
        "CumLandingsNight",
        "CumILS",
        "CumVOR",
        "CumRNP",
        "CumNDB",
        "CumDgaCdi",
        "CumDgaAzi",
        "CumCirc",
        "CumTotalApps",
        "CumTotalHours",
        "Cum2D",
        "Cum3D",
        "CumCDI",
        "CumAzi"
    ];

    public static IReadOnlyList<PortableLogbookSyncMetadataFieldDefinition> SyncMetadataFields { get; } =
    [
        new("entryId", EntryIdColumnName, PortableLogbookSyncMetadataKind.EntryId),
        new("currentRevisionId", "Portable Current Revision ID", PortableLogbookSyncMetadataKind.CurrentRevisionId),
        new("logbookId", PortableLogbookWorkbookMetadata.LogbookIdName, PortableLogbookSyncMetadataKind.LogbookId),
        new("deviceId", PortableLogbookWorkbookMetadata.DeviceIdName, PortableLogbookSyncMetadataKind.DeviceId)
    ];

    public static IReadOnlyDictionary<string, PortableLogbookWorkbookFieldDefinition> ByWorkbookColumnName { get; } =
        PilotEnteredFields.ToDictionary(field => field.WorkbookColumnName, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<string, PortableLogbookWorkbookFieldDefinition> ById { get; } =
        PilotEnteredFields.ToDictionary(field => field.Id, StringComparer.Ordinal);

    public static IReadOnlyList<string> PilotEnteredColumnNames { get; } =
        PilotEnteredFields.Select(field => field.WorkbookColumnName).ToArray();

    private static PortableLogbookWorkbookFieldDefinition Text(string id, string workbookColumnName) =>
        new(id, workbookColumnName, PortableLogbookWorkbookFieldKind.Text);

    private static PortableLogbookWorkbookFieldDefinition Flag(string id, string workbookColumnName) =>
        new(id, workbookColumnName, PortableLogbookWorkbookFieldKind.Boolean);

    private static PortableLogbookWorkbookFieldDefinition Hours(string id, string workbookColumnName) =>
        new(id, workbookColumnName, PortableLogbookWorkbookFieldKind.DecimalHours);

    private static PortableLogbookWorkbookFieldDefinition Count(string id, string workbookColumnName) =>
        new(id, workbookColumnName, PortableLogbookWorkbookFieldKind.Count);
}

public sealed record PortableLogbookWorkbookFieldDefinition(
    string Id,
    string WorkbookColumnName,
    PortableLogbookWorkbookFieldKind Kind);

public enum PortableLogbookWorkbookFieldKind
{
    Text,
    Boolean,
    DecimalHours,
    Count
}

public sealed record PortableLogbookSyncMetadataFieldDefinition(
    string Id,
    string StorageName,
    PortableLogbookSyncMetadataKind Kind);

public enum PortableLogbookSyncMetadataKind
{
    EntryId,
    CurrentRevisionId,
    LogbookId,
    DeviceId
}
