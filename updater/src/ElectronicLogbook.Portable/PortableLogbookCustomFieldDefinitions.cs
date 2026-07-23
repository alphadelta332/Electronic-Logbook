namespace ElectronicLogbook.Portable;

public static class PortableLogbookCustomFieldDefinitions
{
    public static PortableLogbookCustomFieldDefinitionMergeResult Merge(
        IEnumerable<CustomFieldDefinition> localDefinitions,
        IEnumerable<CustomFieldDefinition> incomingDefinitions)
    {
        ArgumentNullException.ThrowIfNull(localDefinitions);
        ArgumentNullException.ThrowIfNull(incomingDefinitions);

        var byId = localDefinitions.ToDictionary(definition => definition.Id);
        var conflicts = new List<PortableLogbookCustomFieldDefinitionConflict>();
        foreach (var incoming in incomingDefinitions)
        {
            if (!byId.TryGetValue(incoming.Id, out var local))
            {
                byId[incoming.Id] = incoming;
                continue;
            }

            if (local != incoming)
            {
                conflicts.Add(new PortableLogbookCustomFieldDefinitionConflict(incoming.Id, local, incoming));
            }
        }

        return new PortableLogbookCustomFieldDefinitionMergeResult(
            byId.Values.OrderBy(definition => definition.Order).ThenBy(definition => definition.Id.Value, StringComparer.Ordinal).ToArray(),
            conflicts);
    }

    public static IReadOnlyList<CustomFieldDefinition> Resolve(
        PortableLogbookCustomFieldDefinitionMergeResult mergeResult,
        IEnumerable<PortableLogbookCustomFieldDefinitionResolution> resolutions)
    {
        ArgumentNullException.ThrowIfNull(mergeResult);
        ArgumentNullException.ThrowIfNull(resolutions);

        var resolutionById = resolutions.ToDictionary(resolution => resolution.Id);
        var definitions = mergeResult.Definitions.ToDictionary(definition => definition.Id);
        foreach (var conflict in mergeResult.Conflicts)
        {
            if (!resolutionById.TryGetValue(conflict.Id, out var resolution))
            {
                throw new ArgumentException(
                    $"Custom field '{conflict.Id.Value}' requires an explicit resolution.",
                    nameof(resolutions));
            }

            definitions[conflict.Id] = resolution.Choice switch
            {
                PortableLogbookCustomFieldDefinitionChoice.KeepLocal => conflict.LocalDefinition,
                PortableLogbookCustomFieldDefinitionChoice.UseIncoming => conflict.IncomingDefinition,
                _ => throw new ArgumentOutOfRangeException(nameof(resolutions), "Unknown custom field resolution choice.")
            };
        }

        return definitions
            .Values
            .OrderBy(definition => definition.Order)
            .ThenBy(definition => definition.Id.Value, StringComparer.Ordinal)
            .ToArray();
    }
}

public sealed record PortableLogbookCustomFieldDefinitionMergeResult(
    IReadOnlyList<CustomFieldDefinition> Definitions,
    IReadOnlyList<PortableLogbookCustomFieldDefinitionConflict> Conflicts)
{
    public bool HasConflicts => Conflicts.Count > 0;
}

public sealed record PortableLogbookCustomFieldDefinitionConflict(
    CustomFieldId Id,
    CustomFieldDefinition LocalDefinition,
    CustomFieldDefinition IncomingDefinition);

public sealed record PortableLogbookCustomFieldDefinitionResolution(
    CustomFieldId Id,
    PortableLogbookCustomFieldDefinitionChoice Choice);

public enum PortableLogbookCustomFieldDefinitionChoice
{
    KeepLocal,
    UseIncoming
}
