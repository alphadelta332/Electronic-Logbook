using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public static class MobileWorkbookMigrationWorkflow
{
    public static MobileWorkbookMigrationComparison CompareWithApp(
        MobileWorkbookMigrationPlan plan,
        LogbookId appLogbookId,
        IEnumerable<PortableLogbookWorkbookEntry> appEntries,
        IEnumerable<CustomFieldDefinition> appCustomFields,
        PortableLogbookCurrencyOverrideDates appCurrencyOverrideDates)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(appEntries);
        ArgumentNullException.ThrowIfNull(appCustomFields);
        ArgumentNullException.ThrowIfNull(appCurrencyOverrideDates);

        var materializedAppEntries = appEntries.ToArray();
        var appTotals = MobileWorkbookMigrationTotals.Calculate(materializedAppEntries);
        var workbookValuesHash = MobileWorkbookMigrationReader.ComputeOrderIndependentEntryValuesSha256(
            plan.Rows.Select(row => row.Entry));
        var appValuesHash = MobileWorkbookMigrationReader.ComputeOrderIndependentEntryValuesSha256(materializedAppEntries);
        var workbookFields = CanonicalCustomFields(plan.CustomFieldDefinitions);
        var currentAppFields = CanonicalCustomFields(appCustomFields);
        var workbookOnlyRows = FindWorkbookOnlyRows(plan.Rows, materializedAppEntries);
        var appOnlyEntries = FindAppOnlyEntries(plan.Rows, materializedAppEntries);
        var customFieldDifferences = FindCustomFieldDifferences(
            plan.CustomFieldDefinitions,
            appCustomFields);

        return new MobileWorkbookMigrationComparison(
            plan.EmbeddedWorkbookLogbookId is null
                ? null
                : plan.EmbeddedWorkbookLogbookId == appLogbookId,
            plan.Rows.Count == materializedAppEntries.Length,
            string.Equals(workbookValuesHash, appValuesHash, StringComparison.OrdinalIgnoreCase),
            customFieldDifferences.Count == 0 &&
            workbookFields.SequenceEqual(currentAppFields, StringComparer.Ordinal),
            plan.CurrencyOverrideDates == appCurrencyOverrideDates,
            plan.CalculatedTotals == appTotals,
            materializedAppEntries.Length,
            appValuesHash,
            appTotals,
            workbookOnlyRows,
            appOnlyEntries,
            customFieldDifferences);
    }

    public static MobileWorkbookMigrationCandidate CreateCandidate(
        MobileWorkbookMigrationPlan plan,
        PortableLogbookDocumentV2 currentDocument,
        DeviceId deviceId,
        DateTimeOffset importedAt,
        PortableLogbookIdFactory? idFactory = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(currentDocument);
        if (plan.TargetLogbookId != currentDocument.LogbookId)
        {
            throw new InvalidOperationException("Migration preview targets a different app logbook. Select the workbook again.");
        }

        if (currentDocument.Operations.Count > 0)
        {
            throw new InvalidOperationException("Workbook migration is only available before flights are recorded in this app logbook.");
        }

        if (plan.Rows.Count == 0)
        {
            throw new InvalidOperationException("Selected workbook does not contain any flight entries to migrate.");
        }

        if (!plan.CachedTotalsMatch)
        {
            throw new InvalidOperationException("Workbook cached totals do not match its visible flight rows. Recalculate and save a disposable workbook copy before migrating.");
        }

        idFactory ??= PortableLogbookIdFactory.Default;
        var allocatedEntryIds = new HashSet<EntryId>();
        var operations = plan.Rows.Select((row, index) =>
        {
            var entryId = row.SourceEntryId is { } sourceId &&
                          EntryId.IsValid(sourceId) &&
                          allocatedEntryIds.Add(sourceId)
                ? sourceId
                : AllocateEntryId(idFactory, allocatedEntryIds);
            allocatedEntryIds.Add(entryId);
            return PortableLogbookOperationV2.Create(
                currentDocument.LogbookId,
                entryId,
                idFactory.NewRevisionId(),
                deviceId,
                importedAt.AddTicks(index),
                row.Entry);
        }).ToArray();
        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(
            currentDocument.LogbookId,
            plan.CustomFieldDefinitions,
            plan.CurrencyOverrideDates,
            operations);
        var receipt = new BrowserWorkbookMigrationReceipt(
            plan.SourceFileName,
            plan.SourceSha256,
            plan.WorkbookVersion,
            plan.EmbeddedWorkbookLogbookId,
            plan.TargetLogbookId,
            plan.Rows.Count,
            plan.EntryValuesSha256,
            plan.CalculatedTotals,
            importedAt,
            DurableReadbackVerified: true);
        return new MobileWorkbookMigrationCandidate(document, receipt);
    }

    public static void VerifyDurableReadback(
        MobileWorkbookMigrationCandidate candidate,
        BrowserLogbookStateV2? reloaded)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (reloaded is null)
        {
            throw new BrowserLogbookStoreException("Workbook migration was not found after durable storage readback.");
        }

        if (reloaded.WorkbookMigration != candidate.Receipt)
        {
            throw new BrowserLogbookStoreException("Workbook migration verification receipt changed during durable storage readback.");
        }

        var candidateJson = PortableLogbookJson.SerializeV2(candidate.Document);
        var reloadedJson = PortableLogbookJson.SerializeV2(reloaded.Document);
        if (!string.Equals(candidateJson, reloadedJson, StringComparison.Ordinal))
        {
            throw new BrowserLogbookStoreException("Workbook migration data changed during durable storage readback.");
        }

        var importedEntries = reloaded.Document.Operations
            .Where(operation => operation.Kind == PortableOperationKind.Create && operation.Entry is not null)
            .Select(operation => operation.Entry!)
            .ToArray();
        var actualHash = MobileWorkbookMigrationReader.ComputeEntryValuesSha256(importedEntries);
        if (importedEntries.Length != candidate.Receipt.EntryCount ||
            !string.Equals(actualHash, candidate.Receipt.EntryValuesSha256, StringComparison.OrdinalIgnoreCase) ||
            MobileWorkbookMigrationTotals.Calculate(importedEntries) != candidate.Receipt.Totals)
        {
            throw new BrowserLogbookStoreException("Workbook migration entries or totals did not match after durable storage readback.");
        }
    }

    private static EntryId AllocateEntryId(PortableLogbookIdFactory idFactory, IReadOnlySet<EntryId> allocatedEntryIds) =>
        idFactory.NewEntryIdExcluding(allocatedEntryIds);

    private static string[] CanonicalCustomFields(IEnumerable<CustomFieldDefinition> fields) =>
        fields
            .OrderBy(field => field.Order)
            .ThenBy(field => field.Id.Value, StringComparer.Ordinal)
            .Select(field => $"{field.Id.Value}\u001f{field.Order}\u001f{field.Label.Trim()}")
            .ToArray();

    private static IReadOnlyList<MobileWorkbookMigrationRow> FindWorkbookOnlyRows(
        IEnumerable<MobileWorkbookMigrationRow> workbookRows,
        IEnumerable<PortableLogbookWorkbookEntry> appEntries)
    {
        var remainingAppValues = CreateValueCounts(appEntries);
        var differences = new List<MobileWorkbookMigrationRow>();
        foreach (var row in workbookRows)
        {
            var value = MobileWorkbookMigrationReader.CanonicalEntryValue(row.Entry);
            if (!ConsumeValue(remainingAppValues, value))
            {
                differences.Add(row);
            }
        }

        return differences;
    }

    private static IReadOnlyList<PortableLogbookWorkbookEntry> FindAppOnlyEntries(
        IEnumerable<MobileWorkbookMigrationRow> workbookRows,
        IEnumerable<PortableLogbookWorkbookEntry> appEntries)
    {
        var remainingWorkbookValues = CreateValueCounts(workbookRows.Select(row => row.Entry));
        var differences = new List<PortableLogbookWorkbookEntry>();
        foreach (var entry in appEntries)
        {
            var value = MobileWorkbookMigrationReader.CanonicalEntryValue(entry);
            if (!ConsumeValue(remainingWorkbookValues, value))
            {
                differences.Add(entry);
            }
        }

        return differences;
    }

    private static Dictionary<string, int> CreateValueCounts(IEnumerable<PortableLogbookWorkbookEntry> entries) =>
        entries
            .Select(MobileWorkbookMigrationReader.CanonicalEntryValue)
            .GroupBy(value => value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    private static bool ConsumeValue(IDictionary<string, int> counts, string value)
    {
        if (!counts.TryGetValue(value, out var count) || count == 0)
        {
            return false;
        }

        counts[value] = count - 1;
        return true;
    }

    private static IReadOnlyList<MobileWorkbookMigrationCustomFieldDifference> FindCustomFieldDifferences(
        IEnumerable<CustomFieldDefinition> workbookFields,
        IEnumerable<CustomFieldDefinition> appFields)
    {
        var workbookByOrder = workbookFields.ToDictionary(field => field.Order);
        var appByOrder = appFields.ToDictionary(field => field.Order);
        return workbookByOrder.Keys
            .Concat(appByOrder.Keys)
            .Distinct()
            .OrderBy(order => order)
            .Select(order => new MobileWorkbookMigrationCustomFieldDifference(
                order,
                workbookByOrder.GetValueOrDefault(order)?.Label,
                appByOrder.GetValueOrDefault(order)?.Label))
            .Where(difference => !string.Equals(
                difference.WorkbookLabel?.Trim(),
                difference.AppLabel?.Trim(),
                StringComparison.Ordinal))
            .ToArray();
    }
}

public sealed record MobileWorkbookMigrationCustomFieldDifference(
    int Order,
    string? WorkbookLabel,
    string? AppLabel);

public sealed record MobileWorkbookMigrationComparison(
    bool? EmbeddedIdentityMatches,
    bool EntryCountMatches,
    bool EntryValuesMatch,
    bool CustomFieldsMatch,
    bool CurrencyOverrideDatesMatch,
    bool TotalsMatch,
    int AppEntryCount,
    string AppEntryValuesSha256,
    MobileWorkbookMigrationTotals AppTotals,
    IReadOnlyList<MobileWorkbookMigrationRow> WorkbookOnlyRows,
    IReadOnlyList<PortableLogbookWorkbookEntry> AppOnlyEntries,
    IReadOnlyList<MobileWorkbookMigrationCustomFieldDifference> CustomFieldDifferences)
{
    public bool IsExactDataMatch =>
        EntryCountMatches &&
        EntryValuesMatch &&
        CustomFieldsMatch &&
        CurrencyOverrideDatesMatch &&
        TotalsMatch;
}

public sealed record MobileWorkbookMigrationCandidate(
    PortableLogbookDocumentV2 Document,
    BrowserWorkbookMigrationReceipt Receipt);

public sealed record BrowserWorkbookMigrationReceipt(
    string SourceFileName,
    string SourceSha256,
    string? WorkbookVersion,
    LogbookId? EmbeddedWorkbookLogbookId,
    LogbookId TargetLogbookId,
    int EntryCount,
    string EntryValuesSha256,
    MobileWorkbookMigrationTotals Totals,
    DateTimeOffset ImportedAt,
    bool DurableReadbackVerified);

public sealed record MobileWorkbookMigrationApplyResult(
    BrowserWorkbookMigrationReceipt Receipt,
    int DurableEntryCount,
    bool DurableReadbackVerified);
