using System.Globalization;
using System.Text;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public static class MobileLogbookFileExportWorkflow
{
    private const int FirstCustomFieldOrder = 1;
    private const int LastCustomFieldOrder = 4;

    public static async ValueTask<MobileLogbookFileExportResult> ExportAsync(
        IEnumerable<PortableLogbookMaterializedEntryV2> entries,
        IEnumerable<CustomFieldDefinition> customFields,
        MobileLogbookFileExportRequest request,
        BrowserFileStore fileStore,
        DateTimeOffset exportedAt)
    {
        ArgumentNullException.ThrowIfNull(fileStore);
        var file = Create(entries, customFields, request, exportedAt);
        var transfer = await fileStore.ShareLogbookExportOrDownloadAsync(
            file.FileName,
            file.Bytes,
            file.ContentType).ConfigureAwait(false);
        return new MobileLogbookFileExportResult(file, transfer);
    }

    public static MobileLogbookFileExportFile Create(
        IEnumerable<PortableLogbookMaterializedEntryV2> entries,
        IEnumerable<CustomFieldDefinition> customFields,
        MobileLogbookFileExportRequest request,
        DateTimeOffset exportedAt)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(customFields);
        ArgumentNullException.ThrowIfNull(request);
        if (request.StartDate is { } start && request.EndDate is { } end && start > end)
        {
            throw new InvalidOperationException("The start date cannot be later than the end date.");
        }
        if (!Enum.IsDefined(request.Layout))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Layout, "The export layout is not supported.");
        }

        var customFieldDefinitions = customFields.ToArray();
        var selected = entries
            .Where(materialized => !materialized.IsDeleted && materialized.Entry is not null)
            .Select(materialized =>
            {
                var date = materialized.Entry!.Date
                    ?? throw new InvalidOperationException("A logbook entry does not have a valid date.");
                return new { Materialized = materialized, Date = date };
            })
            .Where(item =>
                (request.StartDate is null || item.Date >= request.StartDate) &&
                (request.EndDate is null || item.Date <= request.EndDate))
            .OrderBy(item => item.Date)
            .ThenBy(item => item.Materialized.EntryId.Value, StringComparer.Ordinal)
            .Select(item => item.Materialized)
            .ToArray();
        if (selected.Length == 0)
        {
            throw new InvalidOperationException("No logbook entries match the selected date range.");
        }

        var utc = exportedAt.ToUniversalTime();
        var selectedEntries = selected.Select(materialized => materialized.Entry!).ToArray();
        var fields = CreateCustomFieldSlots(customFieldDefinitions);
        var table = CreateTable(selectedEntries, fields, request.Layout);
        var extension = request.Format == MobileLogbookFileExportFormat.Xlsx
            ? BrowserFileStore.ExcelWorkbookExtension
            : BrowserFileStore.CsvExtension;
        var contentType = request.Format == MobileLogbookFileExportFormat.Xlsx
            ? BrowserFileStore.ExcelWorkbookContentType
            : BrowserFileStore.CsvContentType;
        var fileName = $"flightlogx-logbook-{utc:yyyyMMdd'T'HHmmss'Z'}{extension}";
        var bytes = request.Format == MobileLogbookFileExportFormat.Xlsx
            ? MobileLogbookXlsxTemplateWriter.Write(
                selected,
                customFieldDefinitions,
                request.Layout,
                utc)
            : WriteCsv(table);
        return new MobileLogbookFileExportFile(fileName, contentType, utc, table, bytes);
    }

    private static MobileLogbookFileExportTable CreateTable(
        IReadOnlyList<PortableLogbookWorkbookEntry> entries,
        IReadOnlyList<CustomFieldDefinition> customFields,
        MobileLogbookFileExportLayout layout)
    {
        var columns = new List<MobileLogbookFileExportColumn>
        {
            Text("Date", entry => entry.Date),
            Text("Year", entry => entry.Year),
            Text("Month", entry => entry.Month),
            Text("Day", entry => entry.Day),
            Text("Type", entry => entry.Type),
            Text("Reg", entry => entry.Reg),
            Text("Flight ID", entry => entry.FlightId),
            Text("PIC", entry => entry.Pic),
            Text("Other Pilot or Crew", entry => entry.OtherPilotOrCrew)
        };

        if (layout == MobileLogbookFileExportLayout.Compact)
        {
            columns.Add(Text("Details", CombinedDetails));
        }
        else
        {
            columns.AddRange([
                Text("From", entry => entry.From),
                Text("To", entry => entry.To),
                Text("Via", entry => entry.Via),
                Text("Remarks", entry => entry.Remarks),
                Flag("FR", entry => entry.FlightReview),
                Flag("IPC", entry => entry.InstrumentProficiencyCheck),
                Flag("OPC", entry => entry.OperatorProficiencyCheck)
            ]);
        }

        columns.AddRange(customFields.Select(field =>
            Text(field.Label, entry => entry.CustomFields.TryGetValue(field.Id, out var value) ? value : null)));
        columns.AddRange([
            Hours("SeIcusDay", entry => entry.SeIcusDay),
            Hours("SeIcusNight", entry => entry.SeIcusNight),
            Hours("SeDualDay", entry => entry.SeDualDay),
            Hours("SeDualNight", entry => entry.SeDualNight),
            Hours("SeCommandDay", entry => entry.SeCommandDay),
            Hours("SeCommandNight", entry => entry.SeCommandNight),
            Hours("MeIcusDay", entry => entry.MeIcusDay),
            Hours("MeIcusNight", entry => entry.MeIcusNight),
            Hours("MeDualDay", entry => entry.MeDualDay),
            Hours("MeDualNight", entry => entry.MeDualNight),
            Hours("MeCommandDay", entry => entry.MeCommandDay),
            Hours("MeCommandNight", entry => entry.MeCommandNight),
            Hours("CopilotDay", entry => entry.CopilotDay),
            Hours("CopilotNight", entry => entry.CopilotNight),
            Hours("IfrIf", entry => entry.IfrIf),
            Hours("IfrSim", entry => entry.IfrSim),
            Count("LandingsDay", entry => entry.LandingsDay),
            Count("LandingsNight", entry => entry.LandingsNight),
            Count("ILS", entry => entry.Ils),
            Count("VOR", entry => entry.Vor),
            Count("RNP", entry => entry.Rnp),
            Count("NDB", entry => entry.Ndb),
            Count("DGA (CDI)", entry => entry.DgaCdi),
            Count("DGA (Azi)", entry => entry.DgaAzi),
            Count("Circling", entry => entry.Circling),
            Hours("TotalHours", entry => MobileLogbookSession.WorkbookFlightTime(entry)),
            Count("TotalApps", entry => WorkbookApproaches(entry))
        ]);

        var rows = entries
            .Select(entry => (IReadOnlyList<object?>)columns.Select(column => column.Value(entry)).ToArray())
            .ToArray();
        return new MobileLogbookFileExportTable(columns.Select(column => column.Header).ToArray(), rows);
    }

    private static IReadOnlyList<CustomFieldDefinition> CreateCustomFieldSlots(
        IReadOnlyList<CustomFieldDefinition> customFields)
    {
        var activeByOrder = customFields
            .Where(field => field.IsActive && field.Order is >= FirstCustomFieldOrder and <= LastCustomFieldOrder)
            .GroupBy(field => field.Order)
            .ToDictionary(group => group.Key, group => group.First());

        return Enumerable.Range(FirstCustomFieldOrder, LastCustomFieldOrder - FirstCustomFieldOrder + 1)
            .Select(order => activeByOrder.TryGetValue(order, out var field)
                ? field
                : new CustomFieldDefinition(new CustomFieldId($"cf_workbook_{order}"), $"Custom {order}", order))
            .ToArray();
    }

    private static MobileLogbookFileExportColumn Text(
        string header,
        Func<PortableLogbookWorkbookEntry, object?> value) => new(header, value);

    private static MobileLogbookFileExportColumn Flag(
        string header,
        Func<PortableLogbookWorkbookEntry, bool?> value) => new(header, entry => value(entry));

    private static MobileLogbookFileExportColumn Hours(
        string header,
        Func<PortableLogbookWorkbookEntry, decimal?> value) =>
        new(header, entry => NormalizeHours(value(entry)));

    internal static decimal? NormalizeHours(decimal? value) =>
        value is null ? null : decimal.Round(value.Value, 6, MidpointRounding.ToEven);

    private static MobileLogbookFileExportColumn Count(
        string header,
        Func<PortableLogbookWorkbookEntry, int?> value) => new(header, entry => value(entry));

    private static int WorkbookApproaches(PortableLogbookWorkbookEntry entry) =>
        (entry.Ils ?? 0) + (entry.Vor ?? 0) + (entry.Rnp ?? 0) + (entry.Ndb ?? 0) +
        (entry.DgaCdi ?? 0) + (entry.DgaAzi ?? 0);

    internal static string CombinedDetails(PortableLogbookWorkbookEntry entry)
    {
        var route = new[] { entry.From, entry.Via, entry.To }
            .Select(value => value?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value));
        var parts = new List<string>();
        var routeText = string.Join("-", route);
        if (routeText.Length > 0)
        {
            parts.Add(routeText);
        }

        if (!string.IsNullOrWhiteSpace(entry.Remarks))
        {
            parts.Add($"({entry.Remarks.Trim()})");
        }

        var flags = new List<string>();
        if (entry.FlightReview == true) flags.Add("Flight Review");
        if (entry.InstrumentProficiencyCheck == true) flags.Add("IPC");
        if (entry.OperatorProficiencyCheck == true) flags.Add("OPC");
        if (flags.Count > 0)
        {
            parts.Add($"({string.Join("/", flags)})");
        }

        return string.Join(" ", parts);
    }

    private static byte[] WriteCsv(MobileLogbookFileExportTable table)
    {
        var output = new StringBuilder();
        WriteCsvRow(output, table.Headers);
        foreach (var row in table.Rows)
        {
            WriteCsvRow(output, row.Select(FormatCsvValue));
        }

        return Encoding.UTF8.GetBytes(output.ToString());
    }

    private static void WriteCsvRow(StringBuilder output, IEnumerable<object?> values)
    {
        output.AppendJoin(',', values.Select(value => EscapeCsv(value?.ToString() ?? string.Empty)));
        output.Append("\r\n");
    }

    private static string FormatCsvValue(object? value) => value switch
    {
        null => string.Empty,
        bool flag => flag ? "TRUE" : "FALSE",
        DateOnly date => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        decimal number => number.ToString("G29", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static string EscapeCsv(string value)
    {
        if (!value.ContainsAny([',', '"', '\r', '\n']))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

}

public enum MobileLogbookFileExportFormat { Xlsx, Csv }

public enum MobileLogbookFileExportLayout { Full, Compact }

public sealed record MobileLogbookFileExportRequest(
    MobileLogbookFileExportFormat Format,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    MobileLogbookFileExportLayout Layout = MobileLogbookFileExportLayout.Full);

public sealed record MobileLogbookFileExportTable(
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<object?>> Rows);

public sealed record MobileLogbookFileExportFile(
    string FileName,
    string ContentType,
    DateTimeOffset ExportedAt,
    MobileLogbookFileExportTable Table,
    byte[] Bytes);

public sealed record MobileLogbookFileExportResult(
    MobileLogbookFileExportFile File,
    BrowserFileTransferResult Transfer);

internal sealed record MobileLogbookFileExportColumn(
    string Header,
    Func<PortableLogbookWorkbookEntry, object?> Value);
