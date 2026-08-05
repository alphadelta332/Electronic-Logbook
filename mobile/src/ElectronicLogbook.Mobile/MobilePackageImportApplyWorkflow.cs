using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public static class MobilePackageImportApplyWorkflow
{
    public static async ValueTask<MobilePackageImportApplyWorkflowResultV2> ApplyIfReadyAsync(
        PortableLogbookDocumentV2 localDocument,
        BrowserFile file,
        BrowserPackageKeyStore keyStore,
        IEnumerable<PortableLogbookPackageReceipt> existingReceipts,
        DateTimeOffset importedAt)
    {
        ArgumentNullException.ThrowIfNull(localDocument);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(keyStore);
        ArgumentNullException.ThrowIfNull(existingReceipts);

        var receipts = existingReceipts.ToArray();
        BrowserFileStore.ValidateElogbookFile(file);
        if (PortableLogbookImportLedger.HasSeenPackage(receipts, file.Bytes))
        {
            return new MobilePackageImportApplyWorkflowResultV2(
                MobilePackageImportApplyStatus.PackageReplay,
                localDocument,
                receipts,
                null,
                null);
        }

        var read = await MobilePackageImportWorkflow.ReadV2Async(localDocument, file, keyStore).ConfigureAwait(false);
        var plan = PortableLogbookExchange.PlanImport(localDocument, read.Document);
        if (plan.Status == PortableLogbookImportPlanStatus.RequiresCustomFieldResolution)
        {
            return new MobilePackageImportApplyWorkflowResultV2(
                MobilePackageImportApplyStatus.RequiresResolution,
                localDocument,
                receipts,
                plan,
                null);
        }

        return ApplyReadyPlan(localDocument, file.Bytes, read.Manifest, read.Document, receipts, plan, importedAt);
    }

    public static async ValueTask<MobilePackageImportApplyWorkflowResult> ApplyIfReadyAsync(
        PortableLogbookDocument localDocument,
        BrowserFile file,
        BrowserPackageKeyStore keyStore,
        IEnumerable<PortableLogbookPackageReceipt> existingReceipts,
        DateTimeOffset importedAt)
    {
        ArgumentNullException.ThrowIfNull(localDocument);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(keyStore);
        ArgumentNullException.ThrowIfNull(existingReceipts);

        var receipts = existingReceipts.ToArray();
        BrowserFileStore.ValidateElogbookFile(file);
        if (PortableLogbookImportLedger.HasSeenPackage(receipts, file.Bytes))
        {
            return new MobilePackageImportApplyWorkflowResult(
                MobilePackageImportApplyStatus.PackageReplay,
                localDocument,
                receipts,
                null,
                null);
        }

        var read = await MobilePackageImportWorkflow.ReadAsync(localDocument, file, keyStore).ConfigureAwait(false);
        var plan = PortableLogbookExchange.PlanImport(localDocument, read.Document);
        if (plan.Status == PortableLogbookImportPlanStatus.RequiresCustomFieldResolution)
        {
            return new MobilePackageImportApplyWorkflowResult(
                MobilePackageImportApplyStatus.RequiresResolution,
                localDocument,
                receipts,
                plan,
                null);
        }

        return ApplyReadyPlan(localDocument, file.Bytes, read.Manifest, read.Document, receipts, plan, importedAt);
    }

    public static async ValueTask<MobilePackageImportApplyWorkflowResult> ApplyWithCustomFieldResolutionsAsync(
        PortableLogbookDocument localDocument,
        BrowserFile file,
        BrowserPackageKeyStore keyStore,
        IEnumerable<PortableLogbookPackageReceipt> existingReceipts,
        IEnumerable<PortableLogbookCustomFieldDefinitionResolution> resolutions,
        DateTimeOffset importedAt)
    {
        ArgumentNullException.ThrowIfNull(localDocument);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(keyStore);
        ArgumentNullException.ThrowIfNull(existingReceipts);
        ArgumentNullException.ThrowIfNull(resolutions);

        var receipts = existingReceipts.ToArray();
        BrowserFileStore.ValidateElogbookFile(file);
        if (PortableLogbookImportLedger.HasSeenPackage(receipts, file.Bytes))
        {
            return new MobilePackageImportApplyWorkflowResult(
                MobilePackageImportApplyStatus.PackageReplay,
                localDocument,
                receipts,
                null,
                null);
        }

        var read = await MobilePackageImportWorkflow.ReadAsync(localDocument, file, keyStore).ConfigureAwait(false);
        var plan = PortableLogbookExchange.PlanImport(localDocument, read.Document);
        if (plan.Status != PortableLogbookImportPlanStatus.RequiresCustomFieldResolution)
        {
            return ApplyReadyPlan(localDocument, file.Bytes, read.Manifest, read.Document, receipts, plan, importedAt);
        }

        var resolvedDefinitions = PortableLogbookCustomFieldDefinitions.Resolve(
            plan.Preview.CustomFieldDefinitions,
            resolutions);
        var importedDocument = PortableLogbookDocument.CreateAustraliaFirst(
            localDocument.LogbookId,
            resolvedDefinitions,
            localDocument.Operations.Concat(plan.Preview.NewOperations));
        var receipt = PortableLogbookImportLedger.CreateReceipt(file.Bytes, read.Manifest, importedAt);
        var updatedReceipts = receipts.Concat([receipt]).ToArray();
        return new MobilePackageImportApplyWorkflowResult(
            plan.Preview.HasConflicts
                ? MobilePackageImportApplyStatus.AppliedWithConflicts
                : MobilePackageImportApplyStatus.Applied,
            importedDocument,
            updatedReceipts,
            plan,
            receipt);
    }

    public static async ValueTask<MobilePackageImportApplyWorkflowResultV2> ApplyWithCustomFieldResolutionsAsync(
        PortableLogbookDocumentV2 localDocument,
        BrowserFile file,
        BrowserPackageKeyStore keyStore,
        IEnumerable<PortableLogbookPackageReceipt> existingReceipts,
        IEnumerable<PortableLogbookCustomFieldDefinitionResolution> resolutions,
        DateTimeOffset importedAt)
    {
        ArgumentNullException.ThrowIfNull(localDocument);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(keyStore);
        ArgumentNullException.ThrowIfNull(existingReceipts);
        ArgumentNullException.ThrowIfNull(resolutions);

        var receipts = existingReceipts.ToArray();
        BrowserFileStore.ValidateElogbookFile(file);
        if (PortableLogbookImportLedger.HasSeenPackage(receipts, file.Bytes))
        {
            return new MobilePackageImportApplyWorkflowResultV2(
                MobilePackageImportApplyStatus.PackageReplay,
                localDocument,
                receipts,
                null,
                null);
        }

        var read = await MobilePackageImportWorkflow.ReadV2Async(localDocument, file, keyStore).ConfigureAwait(false);
        var plan = PortableLogbookExchange.PlanImport(localDocument, read.Document);
        if (plan.Status != PortableLogbookImportPlanStatus.RequiresCustomFieldResolution)
        {
            return ApplyReadyPlan(localDocument, file.Bytes, read.Manifest, read.Document, receipts, plan, importedAt);
        }

        var resolvedDefinitions = PortableLogbookCustomFieldDefinitions.Resolve(
            plan.Preview.CustomFieldDefinitions,
            resolutions);
        var importedDocument = PortableLogbookDocumentV2.CreateAustraliaFirst(
            localDocument.LogbookId,
            resolvedDefinitions,
            localDocument.CurrencyOverrideDates,
            localDocument.Operations.Concat(plan.Preview.NewOperations));
        var receipt = PortableLogbookImportLedger.CreateReceipt(file.Bytes, read.Manifest, importedAt);
        var updatedReceipts = receipts.Concat([receipt]).ToArray();
        return new MobilePackageImportApplyWorkflowResultV2(
            plan.Preview.HasConflicts
                ? MobilePackageImportApplyStatus.AppliedWithConflicts
                : MobilePackageImportApplyStatus.Applied,
            importedDocument,
            updatedReceipts,
            plan,
            receipt);
    }

    private static MobilePackageImportApplyWorkflowResult ApplyReadyPlan(
        PortableLogbookDocument localDocument,
        byte[] packageBytes,
        PortableLogbookPackageManifest manifest,
        PortableLogbookDocument readDocument,
        IReadOnlyList<PortableLogbookPackageReceipt> receipts,
        PortableLogbookImportPlan plan,
        DateTimeOffset importedAt)
    {
        var importedDocument = plan.Status switch
        {
            PortableLogbookImportPlanStatus.DuplicateOnly => localDocument,
            PortableLogbookImportPlanStatus.RequiresConflictResolution => PortableLogbookDocument.CreateAustraliaFirst(
                localDocument.LogbookId,
                plan.Preview.CustomFieldDefinitions.Definitions,
                localDocument.Operations.Concat(plan.Preview.NewOperations)),
            _ => PortableLogbookExchange.ApplyImport(localDocument, readDocument)
        };
        var receipt = PortableLogbookImportLedger.CreateReceipt(packageBytes, manifest, importedAt);
        var updatedReceipts = receipts.Concat([receipt]).ToArray();
        return new MobilePackageImportApplyWorkflowResult(
            plan.Status switch
            {
                PortableLogbookImportPlanStatus.DuplicateOnly => MobilePackageImportApplyStatus.DuplicateOperationsRecorded,
                PortableLogbookImportPlanStatus.RequiresConflictResolution => MobilePackageImportApplyStatus.AppliedWithConflicts,
                _ => MobilePackageImportApplyStatus.Applied
            },
            importedDocument,
            updatedReceipts,
            plan,
            receipt);
    }

    private static MobilePackageImportApplyWorkflowResultV2 ApplyReadyPlan(
        PortableLogbookDocumentV2 localDocument,
        byte[] packageBytes,
        PortableLogbookPackageManifest manifest,
        PortableLogbookDocumentV2 readDocument,
        IReadOnlyList<PortableLogbookPackageReceipt> receipts,
        PortableLogbookImportPlanV2 plan,
        DateTimeOffset importedAt)
    {
        var importedDocument = plan.Status switch
        {
            PortableLogbookImportPlanStatus.DuplicateOnly => localDocument,
            PortableLogbookImportPlanStatus.RequiresConflictResolution => PortableLogbookDocumentV2.CreateAustraliaFirst(
                localDocument.LogbookId,
                plan.Preview.CustomFieldDefinitions.Definitions,
                localDocument.CurrencyOverrideDates,
                localDocument.Operations.Concat(plan.Preview.NewOperations)),
            _ => PortableLogbookExchange.ApplyImport(localDocument, readDocument)
        };
        var receipt = PortableLogbookImportLedger.CreateReceipt(packageBytes, manifest, importedAt);
        var updatedReceipts = receipts.Concat([receipt]).ToArray();
        return new MobilePackageImportApplyWorkflowResultV2(
            plan.Status switch
            {
                PortableLogbookImportPlanStatus.DuplicateOnly => MobilePackageImportApplyStatus.DuplicateOperationsRecorded,
                PortableLogbookImportPlanStatus.RequiresConflictResolution => MobilePackageImportApplyStatus.AppliedWithConflicts,
                _ => MobilePackageImportApplyStatus.Applied
            },
            importedDocument,
            updatedReceipts,
            plan,
            receipt);
    }
}

public sealed record MobilePackageImportApplyWorkflowResult(
    MobilePackageImportApplyStatus Status,
    PortableLogbookDocument Document,
    IReadOnlyList<PortableLogbookPackageReceipt> ImportReceipts,
    PortableLogbookImportPlan? Plan,
    PortableLogbookPackageReceipt? Receipt);

public sealed record MobilePackageImportApplyWorkflowResultV2(
    MobilePackageImportApplyStatus Status,
    PortableLogbookDocumentV2 Document,
    IReadOnlyList<PortableLogbookPackageReceipt> ImportReceipts,
    PortableLogbookImportPlanV2? Plan,
    PortableLogbookPackageReceipt? Receipt);

public enum MobilePackageImportApplyStatus
{
    PackageReplay,
    DuplicateOperationsRecorded,
    Applied,
    AppliedWithConflicts,
    RequiresResolution
}
