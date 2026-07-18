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
