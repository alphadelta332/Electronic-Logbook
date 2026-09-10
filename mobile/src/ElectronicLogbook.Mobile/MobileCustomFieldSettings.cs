using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public static class MobileCustomFieldSettings
{
    public const int MaximumActiveFields = PortableLogbookCustomFieldSet.WorkbookCustomFieldCount;
    public const int MaximumLabelLength = 60;

    public static IReadOnlyList<MobileCustomFieldTotal> Summarize(
        IEnumerable<CustomFieldDefinition> definitions,
        IEnumerable<PortableLogbookWorkbookEntry> entries) =>
        MobileCustomFieldTotals.Calculate(
            definitions.Where(definition => definition.IsActive),
            entries);

    public static IReadOnlyList<CustomFieldDefinition> Rename(
        IEnumerable<CustomFieldDefinition> definitions,
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        CustomFieldId fieldId,
        string label)
    {
        var definitionArray = definitions.ToArray();
        var field = ActiveField(definitionArray, fieldId);
        EnsureZeroTotal(field, entries);
        var normalizedLabel = NormalizeLabel(label, definitionArray, fieldId);
        return definitionArray
            .Select(definition => definition.Id == fieldId
                ? definition with { Label = normalizedLabel }
                : definition)
            .ToArray();
    }

    public static IReadOnlyList<CustomFieldDefinition> Remove(
        IEnumerable<CustomFieldDefinition> definitions,
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        CustomFieldId fieldId)
    {
        var definitionArray = definitions.ToArray();
        var field = ActiveField(definitionArray, fieldId);
        EnsureZeroTotal(field, entries);
        return definitionArray
            .Select(definition => definition.Id == fieldId
                ? definition with { IsActive = false }
                : definition)
            .ToArray();
    }

    public static IReadOnlyList<CustomFieldDefinition> Add(
        IEnumerable<CustomFieldDefinition> definitions,
        string label,
        CustomFieldId? fieldId = null)
    {
        var definitionArray = definitions.ToArray();
        var active = definitionArray.Where(definition => definition.IsActive).ToArray();
        if (active.Length >= MaximumActiveFields)
        {
            throw new InvalidOperationException($"A maximum of {MaximumActiveFields} custom entries can be active.");
        }

        var normalizedLabel = NormalizeLabel(label, definitionArray, excludedFieldId: null);
        var order = Enumerable.Range(1, MaximumActiveFields)
            .First(candidate => active.All(definition => definition.Order != candidate));
        var resolvedId = fieldId ?? CustomFieldId.New();
        if (string.IsNullOrWhiteSpace(resolvedId.Value)
            || definitionArray.Any(definition => definition.Id == resolvedId))
        {
            throw new InvalidOperationException("The new custom entry could not be assigned a unique identifier.");
        }

        return definitionArray
            .Append(new CustomFieldDefinition(resolvedId, normalizedLabel, order))
            .OrderBy(definition => definition.Order)
            .ThenBy(definition => definition.IsActive ? 0 : 1)
            .ThenBy(definition => definition.Id.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private static CustomFieldDefinition ActiveField(
        IReadOnlyList<CustomFieldDefinition> definitions,
        CustomFieldId fieldId) =>
        definitions.FirstOrDefault(definition => definition.Id == fieldId && definition.IsActive)
        ?? throw new InvalidOperationException("The custom entry is no longer active.");

    private static void EnsureZeroTotal(
        CustomFieldDefinition field,
        IEnumerable<PortableLogbookWorkbookEntry> entries)
    {
        var total = MobileCustomFieldTotals.Calculate([field], entries).Single().Total;
        if (total != 0m)
        {
            throw new InvalidOperationException(
                $"{field.Label} cannot be changed because its current total is not zero.");
        }
    }

    private static string NormalizeLabel(
        string label,
        IReadOnlyList<CustomFieldDefinition> definitions,
        CustomFieldId? excludedFieldId)
    {
        var normalized = label?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new InvalidOperationException("Enter a name for the custom entry.");
        }
        if (normalized.Length > MaximumLabelLength)
        {
            throw new InvalidOperationException(
                $"Custom entry names must be {MaximumLabelLength} characters or fewer.");
        }
        if (definitions.Any(definition =>
                definition.IsActive
                && definition.Id != excludedFieldId
                && string.Equals(definition.Label.Trim(), normalized, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Each active custom entry needs a different name.");
        }

        return normalized;
    }
}
