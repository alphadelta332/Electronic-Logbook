using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public static class MobileWorkbookMigrationWorkflow
{
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
