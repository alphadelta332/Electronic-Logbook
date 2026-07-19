namespace ElectronicLogbook.Portable;

public static class PortableLogbookValidator
{
    public static PortableLogbookValidationResult Validate(PortableLogbookDocument document) =>
        Validate(document, DateOnly.FromDateTime(DateTime.UtcNow));

    public static PortableLogbookValidationResult Validate(
        PortableLogbookDocument document,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(document);

        var errors = new List<PortableLogbookValidationError>();
        if (document.SchemaVersion != PortableLogbookDocument.CurrentSchemaVersion)
        {
            errors.Add(new PortableLogbookValidationError(
                PortableLogbookValidationCode.UnsupportedSchemaVersion,
                $"Schema version {document.SchemaVersion} is not supported."));
        }

        if (string.IsNullOrWhiteSpace(document.JurisdictionProfile))
        {
            errors.Add(new PortableLogbookValidationError(
                PortableLogbookValidationCode.MissingJurisdictionProfile,
                "Jurisdiction profile is required."));
        }

        if (document.JurisdictionProfileVersion < 1)
        {
            errors.Add(new PortableLogbookValidationError(
                PortableLogbookValidationCode.InvalidJurisdictionProfileVersion,
                "Jurisdiction profile version must be at least 1."));
        }

        var duplicateCustomFieldIds = document.CustomFieldDefinitions
            .GroupBy(field => field.Id)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        foreach (var fieldId in duplicateCustomFieldIds)
        {
            errors.Add(new PortableLogbookValidationError(
                PortableLogbookValidationCode.DuplicateCustomFieldId,
                $"Custom field '{fieldId}' is defined more than once."));
        }

        var customFieldIds = document.CustomFieldDefinitions.Select(field => field.Id).ToHashSet();
        var duplicateRevisionIds = document.Operations
            .GroupBy(operation => operation.RevisionId)
            .Where(group => group.Select(operation => operation).Distinct().Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        foreach (var revisionId in duplicateRevisionIds)
        {
            errors.Add(new PortableLogbookValidationError(
                PortableLogbookValidationCode.DuplicateRevisionId,
                $"Revision '{revisionId}' appears with conflicting operation payloads."));
        }

        var revisionsByEntry = document.Operations
            .GroupBy(operation => operation.EntryId)
            .ToDictionary(group => group.Key, group => group.Select(operation => operation.RevisionId).ToHashSet());

        foreach (var operation in document.Operations)
        {
            ValidateOperation(document, operation, revisionsByEntry, customFieldIds, today, errors);
        }

        ValidateAcyclicRevisionGraph(document.Operations, errors);

        return new PortableLogbookValidationResult(errors.Count == 0, errors);
    }

    private static void ValidateOperation(
        PortableLogbookDocument document,
        PortableLogbookOperation operation,
        IReadOnlyDictionary<EntryId, HashSet<RevisionId>> revisionsByEntry,
        IReadOnlySet<CustomFieldId> customFieldIds,
        DateOnly today,
        List<PortableLogbookValidationError> errors)
    {
        if (operation.LogbookId != document.LogbookId)
        {
            errors.Add(new PortableLogbookValidationError(
                PortableLogbookValidationCode.OperationLogbookMismatch,
                $"Revision '{operation.RevisionId}' belongs to logbook '{operation.LogbookId}', not '{document.LogbookId}'."));
        }

        if (operation.ParentRevisionIds.Contains(operation.RevisionId))
        {
            errors.Add(new PortableLogbookValidationError(
                PortableLogbookValidationCode.SelfParentRevision,
                $"Revision '{operation.RevisionId}' cannot reference itself as a parent."));
        }

        switch (operation)
        {
            case CreateEntryOperation create:
                if (create.ParentRevisionIds.Count != 0)
                {
                    errors.Add(new PortableLogbookValidationError(
                        PortableLogbookValidationCode.InvalidParentCount,
                        $"Create revision '{create.RevisionId}' must not have parent revisions."));
                }

                ValidateEntryFields(create.RevisionId, create.Entry, customFieldIds, today, errors);
                break;
            case CorrectEntryOperation correction:
                ValidateParentCount(correction, minimumParents: 1, errors);
                ValidateEntryFields(correction.RevisionId, correction.Entry, customFieldIds, today, errors);
                break;
            case DeleteEntryOperation deletion:
                ValidateParentCount(deletion, minimumParents: 1, errors);
                break;
            case ResolveConflictOperation resolution:
                ValidateParentCount(resolution, minimumParents: 2, errors);
                ValidateEntryFields(resolution.RevisionId, resolution.Entry, customFieldIds, today, errors);
                break;
        }

        foreach (var parentRevisionId in operation.ParentRevisionIds)
        {
            if (!revisionsByEntry.TryGetValue(operation.EntryId, out var entryRevisions) ||
                !entryRevisions.Contains(parentRevisionId))
            {
                errors.Add(new PortableLogbookValidationError(
                    PortableLogbookValidationCode.MissingParentRevision,
                    $"Revision '{operation.RevisionId}' references missing parent revision '{parentRevisionId}'."));
            }
        }
    }

    private static void ValidateParentCount(
        PortableLogbookOperation operation,
        int minimumParents,
        List<PortableLogbookValidationError> errors)
    {
        if (operation.ParentRevisionIds.Count >= minimumParents)
        {
            return;
        }

        errors.Add(new PortableLogbookValidationError(
            PortableLogbookValidationCode.InvalidParentCount,
            $"Revision '{operation.RevisionId}' requires at least {minimumParents} parent revision(s)."));
    }

    private static void ValidateEntryFields(
        RevisionId revisionId,
        PortableLogbookEntry entry,
        IReadOnlySet<CustomFieldId> definedCustomFieldIds,
        DateOnly today,
        List<PortableLogbookValidationError> errors)
    {
        foreach (var fieldId in entry.CustomFields.Keys.Where(fieldId => !definedCustomFieldIds.Contains(fieldId)))
        {
            errors.Add(new PortableLogbookValidationError(
                PortableLogbookValidationCode.UnknownCustomFieldId,
                $"Revision '{revisionId}' references undefined custom field '{fieldId}'."));
        }

        foreach (var violation in PortableLogbookEntryRules.Validate(entry, today).Errors)
        {
            errors.Add(new PortableLogbookValidationError(
                PortableLogbookValidationCode.InvalidEntryField,
                $"Revision '{revisionId}' has invalid entry data: {violation.Message}"));
        }
    }

    private static void ValidateAcyclicRevisionGraph(
        IReadOnlyList<PortableLogbookOperation> operations,
        List<PortableLogbookValidationError> errors)
    {
        foreach (var entryGroup in operations.GroupBy(operation => operation.EntryId))
        {
            if (entryGroup.GroupBy(operation => operation.RevisionId).Any(group => group.Count() > 1))
            {
                continue;
            }

            var byRevision = entryGroup.ToDictionary(operation => operation.RevisionId);
            var visiting = new HashSet<RevisionId>();
            var visited = new HashSet<RevisionId>();

            foreach (var operation in entryGroup)
            {
                if (HasCycle(operation.RevisionId, byRevision, visiting, visited))
                {
                    errors.Add(new PortableLogbookValidationError(
                        PortableLogbookValidationCode.CyclicRevisionHistory,
                        $"Entry '{entryGroup.Key}' has cyclic revision history."));
                    break;
                }
            }
        }
    }

    private static bool HasCycle(
        RevisionId revisionId,
        IReadOnlyDictionary<RevisionId, PortableLogbookOperation> byRevision,
        HashSet<RevisionId> visiting,
        HashSet<RevisionId> visited)
    {
        if (visited.Contains(revisionId))
        {
            return false;
        }

        if (!visiting.Add(revisionId))
        {
            return true;
        }

        if (byRevision.TryGetValue(revisionId, out var operation) &&
            operation.ParentRevisionIds.Any(parent => byRevision.ContainsKey(parent) && HasCycle(parent, byRevision, visiting, visited)))
        {
            return true;
        }

        visiting.Remove(revisionId);
        visited.Add(revisionId);
        return false;
    }
}

public sealed record PortableLogbookValidationResult(
    bool IsValid,
    IReadOnlyList<PortableLogbookValidationError> Errors);

public sealed record PortableLogbookValidationError(
    PortableLogbookValidationCode Code,
    string Message);

public enum PortableLogbookValidationCode
{
    UnsupportedSchemaVersion,
    MissingJurisdictionProfile,
    InvalidJurisdictionProfileVersion,
    DuplicateCustomFieldId,
    DuplicateRevisionId,
    OperationLogbookMismatch,
    InvalidParentCount,
    MissingParentRevision,
    SelfParentRevision,
    UnknownCustomFieldId,
    InvalidEntryField,
    CyclicRevisionHistory
}
