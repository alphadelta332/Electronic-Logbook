namespace ElectronicLogbook.Portable;

public static class PortableLogbookValidatorV2
{
    public static PortableLogbookValidationResult Validate(PortableLogbookDocumentV2 document) =>
        Validate(document, DateOnly.FromDateTime(DateTime.Today));

    public static PortableLogbookValidationResult Validate(
        PortableLogbookDocumentV2 document,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(document);

        var errors = new List<PortableLogbookValidationError>();
        if (document.SchemaVersion != PortableLogbookDocumentV2.CurrentSchemaVersion)
        {
            errors.Add(new PortableLogbookValidationError(
                PortableLogbookValidationCode.UnsupportedSchemaVersion,
                $"Schema version {document.SchemaVersion} is not supported. Re-import the authoritative workbook to create a workbook-faithful mobile package."));
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

        ValidateDocumentIdentifiers(document, errors);
        ValidateCustomFieldDefinitions(document, errors);
        ValidateOperations(document, today, errors);

        return new PortableLogbookValidationResult(errors.Count == 0, errors);
    }

    private static void ValidateDocumentIdentifiers(
        PortableLogbookDocumentV2 document,
        List<PortableLogbookValidationError> errors)
    {
        if (IsBlank(document.LogbookId.Value))
        {
            errors.Add(new PortableLogbookValidationError(
                PortableLogbookValidationCode.InvalidIdentifier,
                "Document logbook ID is required."));
        }
    }

    private static void ValidateCustomFieldDefinitions(
        PortableLogbookDocumentV2 document,
        List<PortableLogbookValidationError> errors)
    {
        foreach (var field in document.CustomFieldDefinitions.Where(field => IsBlank(field.Id.Value)))
        {
            errors.Add(new PortableLogbookValidationError(
                PortableLogbookValidationCode.InvalidIdentifier,
                $"Custom field '{field.Label}' has an invalid ID."));
        }

        foreach (var fieldId in document.CustomFieldDefinitions
            .GroupBy(field => field.Id)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key))
        {
            errors.Add(new PortableLogbookValidationError(
                PortableLogbookValidationCode.DuplicateCustomFieldId,
                $"Custom field '{fieldId}' is defined more than once."));
        }
    }

    private static void ValidateOperations(
        PortableLogbookDocumentV2 document,
        DateOnly today,
        List<PortableLogbookValidationError> errors)
    {
        var customFieldIds = document.CustomFieldDefinitions.Select(field => field.Id).ToHashSet();
        var revisionsByEntry = document.Operations
            .GroupBy(operation => operation.EntryId)
            .ToDictionary(group => group.Key, group => group.Select(operation => operation.RevisionId).ToHashSet());

        foreach (var duplicateRevisionId in document.Operations
            .GroupBy(operation => operation.RevisionId)
            .Where(group => group.Select(operation => operation).Distinct().Count() > 1)
            .Select(group => group.Key))
        {
            errors.Add(new PortableLogbookValidationError(
                PortableLogbookValidationCode.DuplicateRevisionId,
                $"Revision '{duplicateRevisionId}' appears with conflicting operation payloads."));
        }

        foreach (var operation in document.Operations)
        {
            ValidateOperation(document, operation, revisionsByEntry, customFieldIds, today, errors);
        }

        ValidateAcyclicRevisionGraph(document.Operations, errors);
    }

    private static void ValidateOperation(
        PortableLogbookDocumentV2 document,
        PortableLogbookOperationV2 operation,
        IReadOnlyDictionary<EntryId, HashSet<RevisionId>> revisionsByEntry,
        IReadOnlySet<CustomFieldId> customFieldIds,
        DateOnly today,
        List<PortableLogbookValidationError> errors)
    {
        ValidateOperationIdentifiers(operation, errors);

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

        switch (operation.Kind)
        {
            case PortableOperationKind.Create:
                if (operation.ParentRevisionIds.Count != 0)
                {
                    errors.Add(new PortableLogbookValidationError(
                        PortableLogbookValidationCode.InvalidParentCount,
                        $"Create revision '{operation.RevisionId}' must not have parent revisions."));
                }

                ValidateRequiredEntry(operation, customFieldIds, today, errors);
                break;
            case PortableOperationKind.Correction:
                ValidateParentCount(operation, minimumParents: 1, errors);
                ValidateRequiredEntry(operation, customFieldIds, today, errors);
                break;
            case PortableOperationKind.Deletion:
                ValidateParentCount(operation, minimumParents: 1, errors);
                if (operation.Entry is not null)
                {
                    errors.Add(new PortableLogbookValidationError(
                        PortableLogbookValidationCode.InvalidEntryField,
                        $"Deletion revision '{operation.RevisionId}' must not carry entry data."));
                }

                break;
            case PortableOperationKind.ConflictResolution:
                ValidateParentCount(operation, minimumParents: 2, errors);
                ValidateRequiredEntry(operation, customFieldIds, today, errors);
                break;
            default:
                errors.Add(new PortableLogbookValidationError(
                    PortableLogbookValidationCode.InvalidEntryField,
                    $"Revision '{operation.RevisionId}' has unsupported operation kind '{operation.Kind}'."));
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

    private static void ValidateOperationIdentifiers(
        PortableLogbookOperationV2 operation,
        List<PortableLogbookValidationError> errors)
    {
        if (IsBlank(operation.LogbookId.Value))
        {
            errors.Add(new PortableLogbookValidationError(
                PortableLogbookValidationCode.InvalidIdentifier,
                $"Revision '{operation.RevisionId}' has an invalid logbook ID."));
        }

        if (!EntryId.IsValid(operation.EntryId))
        {
            errors.Add(new PortableLogbookValidationError(
                PortableLogbookValidationCode.InvalidIdentifier,
                $"Revision '{operation.RevisionId}' has an invalid entry ID."));
        }

        if (IsBlank(operation.RevisionId.Value))
        {
            errors.Add(new PortableLogbookValidationError(
                PortableLogbookValidationCode.InvalidIdentifier,
                "Operation revision ID is required."));
        }

        if (IsBlank(operation.DeviceId.Value))
        {
            errors.Add(new PortableLogbookValidationError(
                PortableLogbookValidationCode.InvalidIdentifier,
                $"Revision '{operation.RevisionId}' has an invalid device ID."));
        }

        foreach (var parentRevisionId in operation.ParentRevisionIds.Where(parentRevisionId => IsBlank(parentRevisionId.Value)))
        {
            errors.Add(new PortableLogbookValidationError(
                PortableLogbookValidationCode.InvalidIdentifier,
                $"Revision '{operation.RevisionId}' references invalid parent revision '{parentRevisionId}'."));
        }
    }

    private static void ValidateParentCount(
        PortableLogbookOperationV2 operation,
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

    private static void ValidateRequiredEntry(
        PortableLogbookOperationV2 operation,
        IReadOnlySet<CustomFieldId> customFieldIds,
        DateOnly today,
        List<PortableLogbookValidationError> errors)
    {
        if (operation.Entry is null)
        {
            errors.Add(new PortableLogbookValidationError(
                PortableLogbookValidationCode.InvalidEntryField,
                $"Revision '{operation.RevisionId}' must carry workbook-faithful entry data."));
            return;
        }

        ValidateEntry(operation.RevisionId, operation.Entry, customFieldIds, today, errors);
    }

    private static void ValidateEntry(
        RevisionId revisionId,
        PortableLogbookWorkbookEntry entry,
        IReadOnlySet<CustomFieldId> customFieldIds,
        DateOnly today,
        List<PortableLogbookValidationError> errors)
    {
        foreach (var fieldId in entry.CustomFields.Keys.Where(fieldId => !customFieldIds.Contains(fieldId)))
        {
            errors.Add(new PortableLogbookValidationError(
                PortableLogbookValidationCode.UnknownCustomFieldId,
                $"Revision '{revisionId}' references undefined custom field '{fieldId}'."));
        }

        if (entry.Date is null || entry.Date > today)
        {
            errors.Add(new PortableLogbookValidationError(
                PortableLogbookValidationCode.InvalidEntryField,
                $"Revision '{revisionId}' has invalid entry data: the Year, Month, and Day fields must form a non-future date."));
        }

        foreach (var field in PortableLogbookWorkbookEntryFields.ToFieldValues(entry))
        {
            if (field.Value is decimal decimalValue && decimalValue < 0)
            {
                errors.Add(new PortableLogbookValidationError(
                    PortableLogbookValidationCode.InvalidEntryField,
                    $"Revision '{revisionId}' has invalid entry data: {PortableLogbookWorkbookFieldCatalog.ById[field.Key].WorkbookColumnName} cannot be negative."));
            }

            if (field.Value is int intValue && intValue < 0)
            {
                errors.Add(new PortableLogbookValidationError(
                    PortableLogbookValidationCode.InvalidEntryField,
                    $"Revision '{revisionId}' has invalid entry data: {PortableLogbookWorkbookFieldCatalog.ById[field.Key].WorkbookColumnName} cannot be negative."));
            }
        }
    }

    private static void ValidateAcyclicRevisionGraph(
        IReadOnlyList<PortableLogbookOperationV2> operations,
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
        IReadOnlyDictionary<RevisionId, PortableLogbookOperationV2> byRevision,
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

    private static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);
}
