using System.Globalization;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public sealed record MobileLogbookTotalsFilterField(
    string Id,
    string Label,
    MobileLogbookTotalsFilterFieldKind Kind,
    Func<PortableLogbookWorkbookEntry, object?> ReadValue)
{
    public bool SupportsNumericComparison =>
        Kind is MobileLogbookTotalsFilterFieldKind.DecimalHours or MobileLogbookTotalsFilterFieldKind.Count ||
        Id.StartsWith("custom:", StringComparison.Ordinal);
}

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

public sealed record MobileLogbookNumericFilter(string Comparison, decimal? Value, decimal? UpperValue = null)
{
    public string Label => Comparison switch
    {
        "eq" => "Equals",
        "ne" => "Does not equal",
        "gt" => "Greater than",
        "ge" => "Greater than or equal to",
        "lt" => "Less than",
        "le" => "Less than or equal to",
        "between" => "Between",
        _ => string.Empty
    };

    public string? Error => Value is null ? "Enter a value."
        : Comparison == "between" && UpperValue is null ? "Enter an upper value."
        : Comparison == "between" && UpperValue < Value ? "Upper value must be at least the lower value."
        : null;

    public string Summary => Error ?? (Comparison == "between"
        ? $"{Label} {Value} and {UpperValue}"
        : $"{Label} {Value}");

    public bool Matches(decimal value) => Error is null && (Comparison switch
    {
        "eq" => value == Value,
        "ne" => value != Value,
        "gt" => value > Value,
        "ge" => value >= Value,
        "lt" => value < Value,
        "le" => value <= Value,
        "between" => value >= Value && value <= UpperValue,
        _ => false
    });
}

public static class MobileLogbookTotalsFilters
{
    private const string BlankKey = "blank:";
    private const string NonZeroKey = "numeric:non-zero";
    private const string ZeroOrBlankKey = "numeric:zero-or-blank";

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
            Hours("seIcusDay", "Single engine ICUS - day", entry => entry.SeIcusDay),
            Hours("seIcusNight", "Single engine ICUS - night", entry => entry.SeIcusNight),
            Hours("seDualDay", "Single engine dual - day", entry => entry.SeDualDay),
            Hours("seDualNight", "Single engine dual - night", entry => entry.SeDualNight),
            Hours("seCommandDay", "Single engine command - day", entry => entry.SeCommandDay),
            Hours("seCommandNight", "Single engine command - night", entry => entry.SeCommandNight),
            Hours("meIcusDay", "Multi engine ICUS - day", entry => entry.MeIcusDay),
            Hours("meIcusNight", "Multi engine ICUS - night", entry => entry.MeIcusNight),
            Hours("meDualDay", "Multi engine dual - day", entry => entry.MeDualDay),
            Hours("meDualNight", "Multi engine dual - night", entry => entry.MeDualNight),
            Hours("meCommandDay", "Multi engine command - day", entry => entry.MeCommandDay),
            Hours("meCommandNight", "Multi engine command - night", entry => entry.MeCommandNight),
            Hours("copilotDay", "Copilot - day", entry => entry.CopilotDay),
            Hours("copilotNight", "Copilot - night", entry => entry.CopilotNight),
            Hours("ifrIf", "Instrument - actual", entry => entry.IfrIf),
            Hours("ifrSim", "Instrument - simulator", entry => entry.IfrSim),
            Count("landingsDay", "Landings - day", entry => entry.LandingsDay),
            Count("landingsNight", "Landings - night", entry => entry.LandingsNight),
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
                .OrderBy(value => value.Key != NonZeroKey)
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
        DateOnly? endDate = null,
        IReadOnlyDictionary<string, MobileLogbookNumericFilter>? numericFilters = null)
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
        if (numericFilters is not null)
        {
            foreach (var filter in numericFilters)
            {
                if (!fieldsById.TryGetValue(filter.Key, out var field) ||
                    !field.SupportsNumericComparison)
                {
                    continue;
                }

                decimal? number = field.ReadValue(entry) switch
                {
                    decimal hours => hours,
                    int count => (decimal)count,
                    null => 0m,
                    string text when string.IsNullOrWhiteSpace(text) => 0m,
                    string text when decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
                    _ => null
                };
                if (number is null || !filter.Value.Matches(number.Value))
                {
                    return false;
                }
            }
        }

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
        if (rawValue is null)
        {
            return field.Kind is MobileLogbookTotalsFilterFieldKind.DecimalHours or MobileLogbookTotalsFilterFieldKind.Count
                ? new NormalizedFilterValue(ZeroOrBlankKey, "Zero or blank")
                : new NormalizedFilterValue(BlankKey, "(Blanks)");
        }

        if (rawValue is string text && string.IsNullOrWhiteSpace(text))
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
                NormalizeNumeric(hours),
            MobileLogbookTotalsFilterFieldKind.Count when rawValue is int count =>
                NormalizeNumeric(count),
            _ => NormalizeText(rawValue.ToString() ?? string.Empty)
        };
    }

    private static NormalizedFilterValue NormalizeNumeric(decimal value) =>
        value == 0m
            ? new NormalizedFilterValue(ZeroOrBlankKey, "Zero or blank")
            : new NormalizedFilterValue(NonZeroKey, "Non-zero");

    private static NormalizedFilterValue NormalizeText(string value)
    {
        var label = value.Trim();
        return label.Length == 0
            ? new NormalizedFilterValue(BlankKey, "(Blanks)")
            : new NormalizedFilterValue($"text:{label.ToUpperInvariant()}", label);
    }

    private sealed record NormalizedFilterValue(string Key, string Label);
}
