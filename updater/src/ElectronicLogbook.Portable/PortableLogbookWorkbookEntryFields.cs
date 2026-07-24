namespace ElectronicLogbook.Portable;

public static class PortableLogbookWorkbookEntryFields
{
    public static IReadOnlyDictionary<string, object?> ToFieldValues(PortableLogbookWorkbookEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["dateYear"] = entry.Year,
            ["dateMonth"] = entry.Month,
            ["dateDay"] = entry.Day,
            ["type"] = entry.Type,
            ["reg"] = entry.Reg,
            ["flightId"] = entry.FlightId,
            ["pic"] = entry.Pic,
            ["otherPilotOrCrew"] = entry.OtherPilotOrCrew,
            ["from"] = entry.From,
            ["to"] = entry.To,
            ["via"] = entry.Via,
            ["remarks"] = entry.Remarks,
            ["fr"] = entry.FlightReview,
            ["ipc"] = entry.InstrumentProficiencyCheck,
            ["opc"] = entry.OperatorProficiencyCheck,
            ["seIcusDay"] = entry.SeIcusDay,
            ["seIcusNight"] = entry.SeIcusNight,
            ["seDualDay"] = entry.SeDualDay,
            ["seDualNight"] = entry.SeDualNight,
            ["seCommandDay"] = entry.SeCommandDay,
            ["seCommandNight"] = entry.SeCommandNight,
            ["meIcusDay"] = entry.MeIcusDay,
            ["meIcusNight"] = entry.MeIcusNight,
            ["meDualDay"] = entry.MeDualDay,
            ["meDualNight"] = entry.MeDualNight,
            ["meCommandDay"] = entry.MeCommandDay,
            ["meCommandNight"] = entry.MeCommandNight,
            ["copilotDay"] = entry.CopilotDay,
            ["copilotNight"] = entry.CopilotNight,
            ["ifrIf"] = entry.IfrIf,
            ["ifrSim"] = entry.IfrSim,
            ["landingsDay"] = entry.LandingsDay,
            ["landingsNight"] = entry.LandingsNight,
            ["ils"] = entry.Ils,
            ["vor"] = entry.Vor,
            ["rnp"] = entry.Rnp,
            ["ndb"] = entry.Ndb,
            ["dgaCdi"] = entry.DgaCdi,
            ["dgaAzi"] = entry.DgaAzi,
            ["circling"] = entry.Circling
        };

        AddCustomField(values, entry, "custom1", 1);
        AddCustomField(values, entry, "custom2", 2);
        AddCustomField(values, entry, "custom3", 3);
        AddCustomField(values, entry, "custom4", 4);

        return PortableLogbookWorkbookFieldCatalog.PilotEnteredFields
            .ToDictionary(field => field.Id, field => values[field.Id], StringComparer.Ordinal);
    }

    public static PortableLogbookWorkbookEntry FromFieldValues(
        IReadOnlyDictionary<string, object?> values,
        IReadOnlyList<CustomFieldDefinition> customFieldDefinitions)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(customFieldDefinitions);

        foreach (var key in values.Keys.Where(key => !PortableLogbookWorkbookFieldCatalog.ById.ContainsKey(key)))
        {
            throw new ArgumentException($"Unknown portable logbook workbook field '{key}'.", nameof(values));
        }

        var customFields = new Dictionary<CustomFieldId, string?>();
        foreach (var definition in customFieldDefinitions.Where(definition =>
            definition.Order is >= 1 and <= PortableLogbookCustomFieldSet.WorkbookCustomFieldCount))
        {
            customFields[definition.Id] = GetString(values, $"custom{definition.Order}");
        }

        return new PortableLogbookWorkbookEntry(
            Get<int>(values, "dateYear"),
            Get<int>(values, "dateMonth"),
            Get<int>(values, "dateDay"),
            GetString(values, "type"),
            GetString(values, "reg"),
            GetString(values, "flightId"),
            GetString(values, "pic"),
            GetString(values, "otherPilotOrCrew"),
            GetString(values, "from"),
            GetString(values, "to"),
            GetString(values, "via"),
            GetString(values, "remarks"),
            Get<bool>(values, "fr"),
            Get<bool>(values, "ipc"),
            Get<bool>(values, "opc"),
            customFields,
            Get<decimal>(values, "seIcusDay"),
            Get<decimal>(values, "seIcusNight"),
            Get<decimal>(values, "seDualDay"),
            Get<decimal>(values, "seDualNight"),
            Get<decimal>(values, "seCommandDay"),
            Get<decimal>(values, "seCommandNight"),
            Get<decimal>(values, "meIcusDay"),
            Get<decimal>(values, "meIcusNight"),
            Get<decimal>(values, "meDualDay"),
            Get<decimal>(values, "meDualNight"),
            Get<decimal>(values, "meCommandDay"),
            Get<decimal>(values, "meCommandNight"),
            Get<decimal>(values, "copilotDay"),
            Get<decimal>(values, "copilotNight"),
            Get<decimal>(values, "ifrIf"),
            Get<decimal>(values, "ifrSim"),
            Get<int>(values, "landingsDay"),
            Get<int>(values, "landingsNight"),
            Get<int>(values, "ils"),
            Get<int>(values, "vor"),
            Get<int>(values, "rnp"),
            Get<int>(values, "ndb"),
            Get<int>(values, "dgaCdi"),
            Get<int>(values, "dgaAzi"),
            Get<int>(values, "circling"));
    }

    private static void AddCustomField(
        Dictionary<string, object?> values,
        PortableLogbookWorkbookEntry entry,
        string fieldId,
        int order)
    {
        var customValue = entry.CustomFields
            .Where(pair => pair.Key.Value == $"cf_workbook_{order}")
            .Select(pair => pair.Value)
            .FirstOrDefault();
        values[fieldId] = customValue;
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
