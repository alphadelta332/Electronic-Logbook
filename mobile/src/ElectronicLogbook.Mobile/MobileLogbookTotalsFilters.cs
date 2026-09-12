using System.Globalization;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public sealed record MobileLogbookTotalsFilterField(
    string Id,
    string Label,
    MobileLogbookTotalsFilterFieldKind Kind,
    Func<PortableLogbookWorkbookEntry, object?> ReadValue);

public sealed record MobileLogbookTotalsFilterValue(
    string Key,
    string Label,
    int EntryCount);

public enum MobileLogbookTotalsFilterFieldKind
{
    Text,
    Date,
    Boolean,
    DecimalHours,
    Count
}

public static class MobileLogbookTotalsFilters
{
    private const string BlankKey = "blank:";

    public static IReadOnlyList<MobileLogbookTotalsFilterField> CreateFields(
        IEnumerable<CustomFieldDefinition> customFieldDefinitions)
    {
        ArgumentNullException.ThrowIfNull(customFieldDefinitions);

        var fields = new List<MobileLogbookTotalsFilterField>
        {
            Date("date", "Date", entry => entry.Date),
            Text("type", "Aircraft type", entry => entry.Type),
            Text("reg", "Registration", entry => entry.Reg),
            Text("flightId", "Flight ID", entry => entry.FlightId),
            Text("pic", "Pilot in command", entry => entry.Pic),
            Text("otherPilotOrCrew", "Other pilot or crew", entry => entry.OtherPilotOrCrew),
            Text("from", "From", entry => entry.From),
            Text("to", "To", entry => entry.To),
            Text("via", "Via", entry => entry.Via),
            Text("remarks", "Remarks", entry => entry.Remarks),
            Boolean("fr", "Flight review", entry => entry.FlightReview),
            Boolean("ipc", "Instrument proficiency check", entry => entry.InstrumentProficiencyCheck),
            Boolean("opc", "Operator proficiency check", entry => entry.OperatorProficiencyCheck)
        };

        fields.AddRange(customFieldDefinitions
            .Where(definition => definition.IsActive)
            .OrderBy(definition => definition.Order)
            .Select(definition => Text(
                $"custom:{definition.Id.Value}",
                definition.Label,
                entry => entry.CustomFields.GetValueOrDefault(definition.Id))));

        fields.AddRange(
        [
            Hours("seIcusDay", "Single engine ICUS — day", entry => entry.SeIcusDay),
            Hours("seIcusNight", "Single engine ICUS — night", entry => entry.SeIcusNight),
            Hours("seDualDay", "Single engine dual — day", entry => entry.SeDualDay),
            Hours("seDualNight", "Single engine dual — night", entry => entry.SeDualNight),
            Hours("seCommandDay", "Single engine command — day", entry => entry.SeCommandDay),
            Hours("seCommandNight", "Single engine command — night", entry => entry.SeCommandNight),
            Hours("meIcusDay", "Multi engine ICUS — day", entry => entry.MeIcusDay),
            Hours("meIcusNight", "Multi engine ICUS — night", entry => entry.MeIcusNight),
            Hours("meDualDay", "Multi engine dual — day", entry => entry.MeDualDay),
            Hours("meDualNight", "Multi engine dual — night", entry => entry.MeDualNight),
            Hours("meCommandDay", "Multi engine command — day", entry => entry.MeCommandDay),
            Hours("meCommandNight", "Multi engine command — night", entry => entry.MeCommandNight),
            Hours("copilotDay", "Copilot — day", entry => entry.CopilotDay),
            Hours("copilotNight", "Copilot — night", entry => entry.CopilotNight),
            Hours("ifrIf", "Instrument — actual", entry => entry.IfrIf),
            Hours("ifrSim", "Instrument — simulator", entry => entry.IfrSim),
            Count("landingsDay", "Landings — day", entry => entry.LandingsDay),
            Count("landingsNight", "Landings — night", entry => entry.LandingsNight),
            Count("ils", "ILS approaches", entry => entry.Ils),
            Count("vor", "VOR approaches", entry => entry.Vor),
            Count("rnp", "RNP approaches", entry => entry.Rnp),
            Count("ndb", "NDB approaches", entry => entry.Ndb),
            Count("dgaCdi", "DGA (CDI) approaches", entry => entry.DgaCdi),
            Count("dgaAzi", "DGA (azimuth) approaches", entry => entry.DgaAzi),
            Count("circling", "Circling approaches", entry => entry.Circling)
        ]);

        return fields;
    }

    public static IReadOnlyList<MobileLogbookTotalsFilterValue> Values(
        MobileLogbookTotalsFilterField field,
        IEnumerable<PortableLogbookWorkbookEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(entries);

        var values = entries
            .Select(entry => Normalize(field, field.ReadValue(entry)))
            .GroupBy(value => value.Key, StringComparer.Ordinal)
            .Select(group => new MobileLogbookTotalsFilterValue(
                group.Key,
                group.First().Label,
                group.Count()));

        return field.Kind switch
        {
            MobileLogbookTotalsFilterFieldKind.Date => values
                .OrderBy(value => value.Key == BlankKey)
                .ThenBy(value => value.Key, StringComparer.Ordinal)
                .ToArray(),
            MobileLogbookTotalsFilterFieldKind.DecimalHours or MobileLogbookTotalsFilterFieldKind.Count => values
                .OrderBy(value => value.Key == BlankKey)
                .ThenBy(value => NumericSortValue(value.Key))
                .ToArray(),
            _ => values
                .OrderBy(value => value.Key == BlankKey)
                .ThenBy(value => value.Label, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    public static bool Matches(
        PortableLogbookWorkbookEntry entry,
        IReadOnlyDictionary<string, HashSet<string>> selectedValuesByField,
        IEnumerable<MobileLogbookTotalsFilterField> fields,
        DateOnly? startDate = null,
        DateOnly? endDate = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(selectedValuesByField);
        ArgumentNullException.ThrowIfNull(fields);

        if (startDate is { } start && endDate is { } end && start > end)
        {
            throw new ArgumentException("The start date cannot be later than the end date.", nameof(startDate));
        }

        if (startDate is not null || endDate is not null)
        {
            if (entry.Date is not { } entryDate ||
                startDate is { } minimumDate && entryDate < minimumDate ||
                endDate is { } maximumDate && entryDate > maximumDate)
            {
                return false;
            }
        }

        var fieldsById = fields.ToDictionary(field => field.Id, StringComparer.Ordinal);
        foreach (var selection in selectedValuesByField)
        {
            if (!fieldsById.TryGetValue(selection.Key, out var field))
            {
                continue;
            }

            var value = Normalize(field, field.ReadValue(entry));
            if (!selection.Value.Contains(value.Key))
            {
                return false;
            }
        }

        return true;
    }

    private static MobileLogbookTotalsFilterField Text(
        string id,
        string label,
        Func<PortableLogbookWorkbookEntry, object?> readValue) =>
        new(id, label, MobileLogbookTotalsFilterFieldKind.Text, readValue);

    private static MobileLogbookTotalsFilterField Date(
        string id,
        string label,
        Func<PortableLogbookWorkbookEntry, object?> readValue) =>
        new(id, label, MobileLogbookTotalsFilterFieldKind.Date, readValue);

    private static MobileLogbookTotalsFilterField Boolean(
        string id,
        string label,
        Func<PortableLogbookWorkbookEntry, object?> readValue) =>
        new(id, label, MobileLogbookTotalsFilterFieldKind.Boolean, readValue);

    private static MobileLogbookTotalsFilterField Hours(
        string id,
        string label,
        Func<PortableLogbookWorkbookEntry, object?> readValue) =>
        new(id, label, MobileLogbookTotalsFilterFieldKind.DecimalHours, readValue);

    private static MobileLogbookTotalsFilterField Count(
        string id,
        string label,
        Func<PortableLogbookWorkbookEntry, object?> readValue) =>
        new(id, label, MobileLogbookTotalsFilterFieldKind.Count, readValue);

    private static NormalizedFilterValue Normalize(
        MobileLogbookTotalsFilterField field,
        object? rawValue)
    {
        if (rawValue is null || rawValue is string text && string.IsNullOrWhiteSpace(text))
        {
            return new NormalizedFilterValue(BlankKey, "(Blanks)");
        }

        return field.Kind switch
        {
            MobileLogbookTotalsFilterFieldKind.Date when rawValue is DateOnly date =>
                new NormalizedFilterValue($"date:{date:yyyy-MM-dd}", date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)),
            MobileLogbookTotalsFilterFieldKind.Boolean when rawValue is bool flag =>
                new NormalizedFilterValue($"boolean:{flag.ToString().ToLowerInvariant()}", flag ? "Yes" : "No"),
            MobileLogbookTotalsFilterFieldKind.DecimalHours when rawValue is decimal hours =>
                new NormalizedFilterValue($"number:{hours.ToString("G29", CultureInfo.InvariantCulture)}", hours.ToString("0.0###", CultureInfo.InvariantCulture)),
            MobileLogbookTotalsFilterFieldKind.Count when rawValue is int count =>
                new NormalizedFilterValue($"number:{count.ToString(CultureInfo.InvariantCulture)}", count.ToString(CultureInfo.InvariantCulture)),
            _ => NormalizeText(rawValue.ToString() ?? string.Empty)
        };
    }

    private static NormalizedFilterValue NormalizeText(string value)
    {
        var label = value.Trim();
        return label.Length == 0
            ? new NormalizedFilterValue(BlankKey, "(Blanks)")
            : new NormalizedFilterValue($"text:{label.ToUpperInvariant()}", label);
    }

    private static decimal NumericSortValue(string key) =>
        key.StartsWith("number:", StringComparison.Ordinal) &&
        decimal.TryParse(key["number:".Length..], NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : decimal.MaxValue;

    private sealed record NormalizedFilterValue(string Key, string Label);
}
