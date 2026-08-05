namespace ElectronicLogbook.Portable;

public static class PortableLogbookSetup
{
    public static PortableLogbookSetupPlanV2 CreateInitialSetupPlanV2(
        IEnumerable<PortableLogbookWorkbookRowV2> existingRows,
        IEnumerable<CustomFieldDefinition> customFieldDefinitions,
        PortableLogbookCurrencyOverrideDates currencyOverrideDates,
        DateTimeOffset createdAt,
        LogbookId? logbookId = null,
        DeviceId? deviceId = null,
        PortableLogbookKey? key = null,
        PortableLogbookIdFactory? idFactory = null)
    {
        ArgumentNullException.ThrowIfNull(existingRows);
        ArgumentNullException.ThrowIfNull(customFieldDefinitions);
        ArgumentNullException.ThrowIfNull(currencyOverrideDates);

        idFactory ??= PortableLogbookIdFactory.Default;
        var resolvedLogbookId = logbookId ?? LogbookId.New();
        var resolvedDeviceId = deviceId ?? DeviceId.New();
        var resolvedKey = key ?? PortableLogbookKey.Generate();
        var allocatedEntryIds = new HashSet<EntryId>();
        var operations = existingRows.Select(row =>
        {
            if (row.EntryId is not null || row.CurrentRevisionId is not null)
            {
                throw new ArgumentException(
                    "Initial v2 setup cannot enrol a workbook row that already has portable metadata.",
                    nameof(existingRows));
            }

            var entryId = idFactory.NewEntryIdExcluding(allocatedEntryIds);
            allocatedEntryIds.Add(entryId);
            return PortableLogbookOperationV2.Create(
                resolvedLogbookId,
                entryId,
                idFactory.NewRevisionId(),
                resolvedDeviceId,
                createdAt,
                row.Entry);
        }).ToArray();
        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(
            resolvedLogbookId,
            customFieldDefinitions,
            currencyOverrideDates,
            operations);
        // Keep the workbook's visible row order when assigning the initial hidden
        // identifiers. The document projection uses EntryId as a same-date
        // tiebreaker, but freshly generated IDs must not reshuffle multiple flights
        // logged on the same day.
        var workbookRows = operations
            .Select(operation => new PortableLogbookWorkbookRowV2(
                operation.EntryId,
                operation.RevisionId,
                operation.Entry!))
            .ToArray();
        var packageBytes = PortableLogbookPackage.Write(document, resolvedKey);
        return new PortableLogbookSetupPlanV2(
            resolvedLogbookId,
            resolvedDeviceId,
            resolvedKey,
            document,
            workbookRows,
            packageBytes);
    }

    public static PortableLogbookSetupPlan CreateInitialSetupPlan(
        IEnumerable<PortableLogbookEntry> existingEntries,
        IEnumerable<CustomFieldDefinition> customFieldDefinitions,
        DateTimeOffset createdAt,
        LogbookId? logbookId = null,
        DeviceId? deviceId = null,
        PortableLogbookKey? key = null,
        PortableLogbookIdFactory? idFactory = null)
    {
        var resolvedLogbookId = logbookId ?? LogbookId.New();
        var resolvedDeviceId = deviceId ?? DeviceId.New();
        var resolvedKey = key ?? PortableLogbookKey.Generate();
        var document = PortableLogbookInitializer.CreateInitialDocument(
            existingEntries,
            customFieldDefinitions,
            resolvedLogbookId,
            resolvedDeviceId,
            createdAt,
            idFactory);
        var workbookRows = document
            .Operations
            .OfType<CreateEntryOperation>()
            .Select(operation => new PortableLogbookWorkbookRow(
                operation.EntryId,
                operation.RevisionId,
                operation.Entry))
            .ToArray();
        var packageBytes = PortableLogbookPackage.Write(document, resolvedKey);
        return new PortableLogbookSetupPlan(resolvedLogbookId, resolvedDeviceId, resolvedKey, document, workbookRows, packageBytes);
    }
}

public sealed record PortableLogbookSetupPlan(
    LogbookId LogbookId,
    DeviceId DeviceId,
    PortableLogbookKey Key,
    PortableLogbookDocument InitialDocument,
    IReadOnlyList<PortableLogbookWorkbookRow> WorkbookRows,
    byte[] InitialPackageBytes);

public sealed record PortableLogbookSetupPlanV2(
    LogbookId LogbookId,
    DeviceId DeviceId,
    PortableLogbookKey Key,
    PortableLogbookDocumentV2 InitialDocument,
    IReadOnlyList<PortableLogbookWorkbookRowV2> WorkbookRows,
    byte[] InitialPackageBytes);
