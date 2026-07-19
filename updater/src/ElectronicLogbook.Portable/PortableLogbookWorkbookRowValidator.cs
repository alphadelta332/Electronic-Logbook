namespace ElectronicLogbook.Portable;

public static class PortableLogbookWorkbookRowValidator
{
    public static PortableLogbookWorkbookRowValidationResult Validate(
        IEnumerable<PortableLogbookWorkbookRow> rows,
        IEnumerable<PortableLogbookMaterializedEntry> knownEntries)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(knownEntries);

        var knownByEntryId = knownEntries.ToDictionary(entry => entry.EntryId);
        var errors = new List<PortableLogbookWorkbookRowValidationError>();
        var seenEntryIds = new HashSet<EntryId>();
        var rowNumber = 0;
        foreach (var row in rows)
        {
            rowNumber++;
            if (row.Entry is null)
            {
                errors.Add(new PortableLogbookWorkbookRowValidationError(
                    rowNumber,
                    PortableLogbookWorkbookRowValidationCode.MissingEntryPayload,
                    "Workbook row is missing its portable entry payload."));
                continue;
            }

            if (row.EntryId is null && row.CurrentRevisionId is not null)
            {
                errors.Add(new PortableLogbookWorkbookRowValidationError(
                    rowNumber,
                    PortableLogbookWorkbookRowValidationCode.RevisionWithoutEntryId,
                    "Workbook row has a current revision ID without an entry ID."));
                continue;
            }

            if (row.EntryId is null)
            {
                continue;
            }

            if (!seenEntryIds.Add(row.EntryId.Value))
            {
                errors.Add(new PortableLogbookWorkbookRowValidationError(
                    rowNumber,
                    PortableLogbookWorkbookRowValidationCode.DuplicateEntryId,
                    $"Workbook row duplicates entry ID '{row.EntryId}'."));
                continue;
            }

            if (!knownByEntryId.TryGetValue(row.EntryId.Value, out var known))
            {
                errors.Add(new PortableLogbookWorkbookRowValidationError(
                    rowNumber,
                    PortableLogbookWorkbookRowValidationCode.UnknownEntryId,
                    $"Workbook row references unknown entry ID '{row.EntryId}'."));
                continue;
            }

            if (row.CurrentRevisionId is null)
            {
                errors.Add(new PortableLogbookWorkbookRowValidationError(
                    rowNumber,
                    PortableLogbookWorkbookRowValidationCode.MissingCurrentRevisionId,
                    $"Workbook row for entry '{row.EntryId}' is missing its current revision ID."));
                continue;
            }

            if (row.CurrentRevisionId.Value != known.CurrentRevisionId)
            {
                errors.Add(new PortableLogbookWorkbookRowValidationError(
                    rowNumber,
                    PortableLogbookWorkbookRowValidationCode.StaleCurrentRevisionId,
                    $"Workbook row for entry '{row.EntryId}' references stale revision '{row.CurrentRevisionId}'."));
            }
        }

        return new PortableLogbookWorkbookRowValidationResult(errors.Count == 0, errors);
    }
}

public sealed record PortableLogbookWorkbookRowValidationResult(
    bool IsValid,
    IReadOnlyList<PortableLogbookWorkbookRowValidationError> Errors);

public sealed record PortableLogbookWorkbookRowValidationError(
    int RowNumber,
    PortableLogbookWorkbookRowValidationCode Code,
    string Message);

public enum PortableLogbookWorkbookRowValidationCode
{
    RevisionWithoutEntryId,
    UnknownEntryId,
    MissingCurrentRevisionId,
    StaleCurrentRevisionId,
    DuplicateEntryId,
    MissingEntryPayload
}
