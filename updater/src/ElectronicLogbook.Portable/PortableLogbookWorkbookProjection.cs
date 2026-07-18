namespace ElectronicLogbook.Portable;

public static class PortableLogbookWorkbookProjection
{
    public static PortableLogbookProjectionResult Reconcile(
        IEnumerable<PortableLogbookMaterializedEntry> knownEntries,
        IEnumerable<PortableLogbookWorkbookRow> currentRows,
        LogbookId logbookId,
        DeviceId deviceId,
        DateTimeOffset createdAt,
        PortableLogbookIdFactory? idFactory = null)
    {
        ArgumentNullException.ThrowIfNull(knownEntries);
        ArgumentNullException.ThrowIfNull(currentRows);

        idFactory ??= PortableLogbookIdFactory.Default;
        var knownByEntryId = knownEntries.ToDictionary(entry => entry.EntryId);
        var rows = currentRows.ToArray();
        var rowValidation = PortableLogbookWorkbookRowValidator.Validate(rows, knownByEntryId.Values);
        if (!rowValidation.IsValid)
        {
            throw new PortableLogbookWorkbookProjectionException(
                PortableLogbookWorkbookProjectionError.InvalidRowMetadata,
                "Workbook row metadata is invalid.",
                rowValidation);
        }

        var seenKnownIds = new HashSet<EntryId>();
        var operations = new List<PortableLogbookOperation>();

        foreach (var row in rows)
        {
            if (row.EntryId is null || !knownByEntryId.TryGetValue(row.EntryId.Value, out var known))
            {
                operations.Add(new CreateEntryOperation(
                    logbookId,
                    idFactory.NewEntryId(),
                    idFactory.NewRevisionId(),
                    deviceId,
                    createdAt,
                    row.Entry));
                continue;
            }

            seenKnownIds.Add(known.EntryId);
            if (!known.IsDeleted &&
                known.Entry is not null &&
                EntriesEqual(known.Entry, row.Entry))
            {
                continue;
            }

            operations.Add(new CorrectEntryOperation(
                logbookId,
                known.EntryId,
                idFactory.NewRevisionId(),
                new HashSet<RevisionId> { known.CurrentRevisionId },
                deviceId,
                createdAt,
                row.Entry));
        }

        foreach (var missing in knownByEntryId.Values.Where(entry => !entry.IsDeleted && !seenKnownIds.Contains(entry.EntryId)))
        {
            operations.Add(new DeleteEntryOperation(
                logbookId,
                missing.EntryId,
                idFactory.NewRevisionId(),
                new HashSet<RevisionId> { missing.CurrentRevisionId },
                deviceId,
                createdAt,
                "Row missing from workbook projection."));
        }

        return new PortableLogbookProjectionResult(operations);
    }

    private static bool EntriesEqual(PortableLogbookEntry left, PortableLogbookEntry right) =>
        left.Date == right.Date &&
        left.AircraftType == right.AircraftType &&
        left.Registration == right.Registration &&
        left.FlightNumber == right.FlightNumber &&
        left.From == right.From &&
        left.To == right.To &&
        left.Route == right.Route &&
        left.Details == right.Details &&
        left.MultiPilot == right.MultiPilot &&
        left.PilotInCommand == right.PilotInCommand &&
        left.CoPilot == right.CoPilot &&
        left.Dual == right.Dual &&
        left.Instructor == right.Instructor &&
        left.Day == right.Day &&
        left.Night == right.Night &&
        left.InstrumentActual == right.InstrumentActual &&
        left.InstrumentSimulated == right.InstrumentSimulated &&
        left.TakeoffsDay == right.TakeoffsDay &&
        left.TakeoffsNight == right.TakeoffsNight &&
        left.LandingsDay == right.LandingsDay &&
        left.LandingsNight == right.LandingsNight &&
        left.IfrApproaches == right.IfrApproaches &&
        left.Holding == right.Holding &&
        left.Rnav == right.Rnav &&
        left.Circling == right.Circling &&
        left.CustomFields.Count == right.CustomFields.Count &&
        left.CustomFields.All(pair => right.CustomFields.TryGetValue(pair.Key, out var value) && value == pair.Value);
}

public sealed record PortableLogbookWorkbookRow(
    EntryId? EntryId,
    RevisionId? CurrentRevisionId,
    PortableLogbookEntry Entry);

public sealed record PortableLogbookProjectionResult(
    IReadOnlyList<PortableLogbookOperation> Operations)
{
    public int CreateCount => Operations.Count(operation => operation.Kind == PortableOperationKind.Create);

    public int CorrectionCount => Operations.Count(operation => operation.Kind == PortableOperationKind.Correction);

    public int DeletionCount => Operations.Count(operation => operation.Kind == PortableOperationKind.Deletion);
}

public sealed class PortableLogbookWorkbookProjectionException : Exception
{
    public PortableLogbookWorkbookProjectionException(
        PortableLogbookWorkbookProjectionError error,
        string message,
        PortableLogbookWorkbookRowValidationResult rowValidation)
        : base(message)
    {
        Error = error;
        RowValidation = rowValidation;
    }

    public PortableLogbookWorkbookProjectionError Error { get; }

    public PortableLogbookWorkbookRowValidationResult RowValidation { get; }
}

public enum PortableLogbookWorkbookProjectionError
{
    InvalidRowMetadata
}
