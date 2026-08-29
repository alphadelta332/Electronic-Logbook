using System.Globalization;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater;

internal sealed record WorkbookLogbookRowsInspectionV2(
    IReadOnlyList<PortableLogbookWorkbookRowV2> Rows,
    int UserDataRowCount,
    int UnrecognizedUserDataRowCount);

public sealed record WorkbookPreMigrationWarning(
    string Message,
    int AffectedFlightCount);

public sealed record WorkbookPreMigrationSummary(
    int FlightCount,
    decimal LoggedHours,
    DateOnly? FirstFlightDate,
    DateOnly? LastFlightDate,
    IReadOnlyList<WorkbookPreMigrationWarning> Warnings)
{
    public string LoggedHoursDisplay => LoggedHours.ToString("0.0#", CultureInfo.InvariantCulture);

    public string DateRangeDisplay => (FirstFlightDate, LastFlightDate) switch
    {
        (null, _) or (_, null) => "No valid flight dates found",
        ({ } first, { } last) when first == last => first.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
        ({ } first, { } last) =>
            $"{first.ToString("d MMM yyyy", CultureInfo.InvariantCulture)} to " +
            last.ToString("d MMM yyyy", CultureInfo.InvariantCulture)
    };
}

public static class WorkbookPreMigrationInspector
{
    public static WorkbookPreMigrationSummary Inspect(
        string workbookPath,
        DateOnly? today = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);

        WorkbookPackageValidator.ValidateWorkbookPackage(workbookPath);
        var customFields = PortableLogbookWorkbookPackageStorage
            .ReadWorkbookCustomFieldDefinitions(workbookPath);
        var inspection = PortableLogbookWorkbookPackageStorage
            .ReadCurrentRowsForInspectionV2(workbookPath, customFields);

        return Create(
            inspection.Rows.Select(row => row.Entry),
            inspection.UnrecognizedUserDataRowCount,
            today ?? DateOnly.FromDateTime(DateTime.Today));
    }

    public static WorkbookPreMigrationSummary Create(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        int unrecognizedUserDataRowCount = 0,
        DateOnly? today = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentOutOfRangeException.ThrowIfNegative(unrecognizedUserDataRowCount);

        var entryArray = entries.ToArray();
        var effectiveToday = today ?? DateOnly.FromDateTime(DateTime.Today);
        var warningCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var entry in entryArray)
        {
            var presentationEntry = ToPresentationEntry(entry);
            var messages = PortableLogbookEntryRules
                .Validate(presentationEntry, effectiveToday)
                .Errors
                .Select(error => error.Message)
                .Concat(PortableLogbookEntryRules
                    .Warn(presentationEntry)
                    .Select(warning => warning.Message))
                .Distinct(StringComparer.Ordinal);

            foreach (var message in messages)
            {
                warningCounts[message] = warningCounts.GetValueOrDefault(message) + 1;
            }
        }

        var warnings = warningCounts
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new WorkbookPreMigrationWarning(pair.Key, pair.Value))
            .ToList();

        if (unrecognizedUserDataRowCount > 0)
        {
            warnings.Insert(
                0,
                new WorkbookPreMigrationWarning(
                    $"{unrecognizedUserDataRowCount} workbook " +
                    $"{(unrecognizedUserDataRowCount == 1 ? "row is" : "rows are")} missing a usable date, identifying flight details, or logged time. " +
                    "Fix them in Excel, save the workbook, and run the check again.",
                    0));
        }

        if (entryArray.Length == 0 && unrecognizedUserDataRowCount == 0)
        {
            warnings.Insert(
                0,
                new WorkbookPreMigrationWarning(
                    "No flights were found. Check that this is the workbook you intend to move.",
                    0));
        }

        var dates = entryArray
            .Select(entry => entry.Date)
            .OfType<DateOnly>()
            .ToArray();

        return new WorkbookPreMigrationSummary(
            entryArray.Length,
            entryArray.Sum(FlightHours),
            dates.Length == 0 ? null : dates.Min(),
            dates.Length == 0 ? null : dates.Max(),
            warnings);
    }

    public static string FormatWarnings(WorkbookPreMigrationSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        if (summary.Warnings.Count == 0)
        {
            return "No workbook warnings found.";
        }

        return string.Join(
            Environment.NewLine,
            summary.Warnings.Select(warning =>
                warning.AffectedFlightCount == 1
                    ? $"• 1 flight: {warning.Message}"
                    : warning.AffectedFlightCount > 1
                        ? $"• {warning.AffectedFlightCount} flights: {warning.Message}"
                        : $"• {warning.Message}"));
    }

    private static decimal FlightHours(PortableLogbookWorkbookEntry entry) =>
        Value(entry.SeIcusDay) +
        Value(entry.SeIcusNight) +
        Value(entry.SeDualDay) +
        Value(entry.SeDualNight) +
        Value(entry.SeCommandDay) +
        Value(entry.SeCommandNight) +
        Value(entry.MeIcusDay) +
        Value(entry.MeIcusNight) +
        Value(entry.MeDualDay) +
        Value(entry.MeDualNight) +
        Value(entry.MeCommandDay) +
        Value(entry.MeCommandNight) +
        Value(entry.CopilotDay) +
        Value(entry.CopilotNight);

    private static PortableLogbookEntry ToPresentationEntry(PortableLogbookWorkbookEntry entry) => new(
        entry.Date,
        entry.Type,
        entry.Reg,
        entry.FlightId,
        entry.From,
        entry.To,
        entry.Via,
        entry.Remarks,
        Sum(entry.MeIcusDay, entry.MeIcusNight),
        Sum(entry.SeCommandDay, entry.SeCommandNight, entry.MeCommandDay, entry.MeCommandNight),
        Sum(entry.CopilotDay, entry.CopilotNight),
        Sum(entry.SeDualDay, entry.SeDualNight, entry.MeDualDay, entry.MeDualNight),
        null,
        Sum(
            entry.SeIcusDay,
            entry.SeDualDay,
            entry.SeCommandDay,
            entry.MeIcusDay,
            entry.MeDualDay,
            entry.MeCommandDay,
            entry.CopilotDay),
        Sum(
            entry.SeIcusNight,
            entry.SeDualNight,
            entry.SeCommandNight,
            entry.MeIcusNight,
            entry.MeDualNight,
            entry.MeCommandNight,
            entry.CopilotNight),
        entry.IfrIf,
        entry.IfrSim,
        null,
        null,
        entry.LandingsDay,
        entry.LandingsNight,
        Sum(entry.Ils, entry.Vor, entry.Rnp, entry.Ndb, entry.DgaCdi, entry.DgaAzi, entry.Circling),
        null,
        entry.Rnp,
        entry.Circling,
        entry.CustomFields);

    private static decimal? Sum(params decimal?[] values)
    {
        var present = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return present.Length == 0 ? null : present.Sum();
    }

    private static int? Sum(params int?[] values)
    {
        var present = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return present.Length == 0 ? null : present.Sum();
    }

    private static decimal Value(decimal? value) => value.GetValueOrDefault();
}
