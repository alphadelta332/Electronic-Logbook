using System.Text.Json;
using System.Text;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater;

public static class PortableLogbookCommandRunner
{
    public static Task<int> RunAsync(IReadOnlyList<string> args)
    {
        try
        {
            var options = PortableLogbookCommandOptions.Parse(args);
            if (options.ShowHelp)
            {
                Console.WriteLine(PortableLogbookCommandOptions.HelpText);
                return Task.FromResult(0);
            }

            object result = options.Command switch
            {
                PortableLogbookCommand.Enable => Enable(
                    options.WorkbookPath!,
                    options.RecoveryOutputPath!,
                    DateTimeOffset.UtcNow,
                    options.SaveWindowsCredential),
                PortableLogbookCommand.Export => Export(
                    options.WorkbookPath!,
                    CreateKeySource(options),
                    options.PackageOutputPath!,
                    DateTimeOffset.UtcNow),
                PortableLogbookCommand.ImportApply => ApplyImport(
                    options.WorkbookPath!,
                    CreateKeySource(options),
                    options.PackageInputPath!,
                    DateTimeOffset.UtcNow),
                PortableLogbookCommand.ImportPreview => PreviewImport(
                    options.WorkbookPath!,
                    CreateKeySource(options),
                    options.PackageInputPath!),
                PortableLogbookCommand.PrintedCopy => CreatePrintedCopy(
                    options.WorkbookPath!,
                    CreateKeySource(options),
                    options.PrintedCopyOutputPath!,
                    options.HolderName!,
                    options.HolderDateOfBirth!.Value,
                    options.CertifiedOn ?? DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
                    options.RecordsPerPage ?? 25),
                PortableLogbookCommand.RevisionHistory => ReadRevisionHistory(
                    options.WorkbookPath!,
                    CreateKeySource(options),
                    new EntryId(options.EntryId!)),
                PortableLogbookCommand.ResolveConflict => ResolveConflict(
                    options.WorkbookPath!,
                    CreateKeySource(options),
                    new EntryId(options.EntryId!),
                    new RevisionId(options.RevisionId!),
                    options.Note,
                    DateTimeOffset.UtcNow),
                PortableLogbookCommand.Status => ReadStatus(options.WorkbookPath!),
                _ => throw new UpdaterUsageException("Portable command is required.")
            };

            if (options.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(result, JsonDefaults.Indented));
            }
            else
            {
                WriteHumanResult(result);
            }

            return Task.FromResult(0);
        }
        catch (UpdaterUsageException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(PortableLogbookCommandOptions.HelpText);
            return Task.FromResult(2);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Portable logbook command failed: {ex.Message}");
            return Task.FromResult(1);
        }
    }

    public static PortableLogbookWorkbookStatus ReadStatus(string workbookPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);

        var envelope = PortableLogbookWorkbookPackageStorage.ReadEnvelope(workbookPath);
        if (envelope is null)
        {
            return new PortableLogbookWorkbookStatus(
                IsEnabled: false,
                WorkbookPath: Path.GetFullPath(workbookPath),
                LogbookId: null,
                SchemaVersion: null,
                StorageVersion: null,
                Summary: null,
                ImportReceiptCount: 0);
        }

        if (envelope.SchemaVersion != PortableLogbookDocumentV2.CurrentSchemaVersion)
        {
            throw new UpdaterUsageException(
                "This workbook contains an unsupported legacy portable-logbook envelope. Reset its portable metadata/package state from a timestamped backup before enabling schema v2.");
        }

        return new PortableLogbookWorkbookStatus(
            IsEnabled: true,
            WorkbookPath: Path.GetFullPath(workbookPath),
            envelope.LogbookId,
            envelope.SchemaVersion,
            envelope.StorageVersion,
            envelope.Summary,
            envelope.ImportReceipts.Count);
    }

    public static PortableLogbookEnableResult Enable(
        string workbookPath,
        string recoveryOutputPath,
        DateTimeOffset createdAt,
        bool saveWindowsCredential = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryOutputPath);

        var existingEnvelope = PortableLogbookWorkbookPackageStorage.ReadEnvelope(workbookPath);
        if (existingEnvelope is { SchemaVersion: PortableLogbookDocumentV2.CurrentSchemaVersion })
        {
            throw new UpdaterUsageException("Portable logbook storage is already enabled for this workbook.");
        }

        if (File.Exists(recoveryOutputPath))
        {
            throw new UpdaterUsageException($"Recovery output file already exists: {recoveryOutputPath}");
        }

        var customFieldDefinitions = PortableLogbookWorkbookPackageStorage.ReadWorkbookCustomFieldDefinitions(workbookPath);
        var existingRows = PortableLogbookWorkbookPackageStorage.ReadCurrentRowsV2(workbookPath, customFieldDefinitions);
        var currencyOverrideDates = PortableLogbookWorkbookPackageStorage.ReadCurrencyOverrideDates(workbookPath);
        if (existingEnvelope is not null)
        {
            // v1 was never released. The backup below retains its encrypted metadata;
            // the working copy is re-enrolled from its unchanged visible flight rows.
            existingRows = existingRows
                .Select(row => new PortableLogbookWorkbookRowV2(null, null, row.Entry))
                .ToArray();
        }
        var setup = PortableLogbookSetup.CreateInitialSetupPlanV2(
            existingRows,
            customFieldDefinitions,
            currencyOverrideDates,
            createdAt);
        var envelope = PortableLogbookWorkbookStorage.CreateEnvelope(
            setup.InitialDocument,
            setup.InitialPackageBytes,
            []);
        var backupPath = CreateWorkbookBackup(workbookPath, "portable-enable", createdAt);
        var credentialTargetName = saveWindowsCredential
            ? PortableLogbookWindowsCredentialStore.CreateTargetName(setup.LogbookId, setup.DeviceId)
            : null;

        File.WriteAllText(recoveryOutputPath, CreateRecoveryFileText(setup, workbookPath, credentialTargetName, createdAt));
        try
        {
            if (credentialTargetName is not null)
            {
                PortableLogbookWindowsCredentialStore.SaveKey(credentialTargetName, setup.Key);
            }

            PortableLogbookWorkbookPackageStorage.EnsureHiddenMetadataColumns(workbookPath);
            PortableLogbookWorkbookPackageStorage.WriteHiddenMetadataColumnValuesV2(
                workbookPath,
                setup.WorkbookRows,
                setup.InitialDocument.CustomFieldDefinitions,
                writeVisiblePayloadCells: false);
            PortableLogbookWorkbookPackageStorage.EnsureWorkbookIdentityMetadata(
                workbookPath,
                setup.LogbookId,
                setup.DeviceId,
                setup.InitialDocument.SchemaVersion);
            PortableLogbookWorkbookPackageStorage.WriteEnvelope(workbookPath, envelope);
        }
        catch
        {
            TryDeleteRecoveryFile(recoveryOutputPath);
            if (credentialTargetName is not null)
            {
                TryDeleteWindowsCredential(credentialTargetName);
            }

            throw;
        }

        return new PortableLogbookEnableResult(
            Path.GetFullPath(workbookPath),
            Path.GetFullPath(recoveryOutputPath),
            Path.GetFullPath(backupPath),
            setup.LogbookId,
            setup.DeviceId,
            setup.InitialDocument.SchemaVersion,
            envelope.StorageVersion,
            setup.InitialDocument.Operations.Count,
            credentialTargetName,
            createdAt);
    }

    public static PortableLogbookExportResult Export(
        string workbookPath,
        string recoveryCodeFilePath,
        string packageOutputPath,
        DateTimeOffset exportedAt) =>
        Export(
            workbookPath,
            PortableLogbookCommandKeySource.RecoveryCodeFile(recoveryCodeFilePath),
            packageOutputPath,
            exportedAt);

    public static PortableLogbookExportResult Export(
        string workbookPath,
        PortableLogbookCommandKeySource keySource,
        string packageOutputPath,
        DateTimeOffset exportedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);
        ArgumentNullException.ThrowIfNull(keySource);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageOutputPath);

        if (File.Exists(packageOutputPath))
        {
            throw new UpdaterUsageException($"Portable package output file already exists: {packageOutputPath}");
        }

        var key = ReadKey(keySource);
        var state = PortableLogbookWorkbookPackageStorage.OpenStateV2(workbookPath, key)
            ?? throw new UpdaterUsageException("Portable logbook storage is not enabled for this workbook.");
        var identity = PortableLogbookWorkbookPackageStorage.ReadWorkbookIdentityMetadata(workbookPath)
            ?? throw new UpdaterUsageException("Portable logbook workbook identity metadata is missing.");
        if (identity.LogbookId != state.Document.LogbookId)
        {
            throw new UpdaterUsageException("Portable logbook workbook identity does not match the stored operation history.");
        }

        var merge = PortableLogbookWorkbookProjection.MergeV2(state.Document.Operations);
        var currentRows = PortableLogbookWorkbookPackageStorage.ReadCurrentRowsV2(
            workbookPath,
            state.Document.CustomFieldDefinitions);
        var projection = PortableLogbookWorkbookProjection.ReconcileV2(
            merge.Entries.Values,
            currentRows,
            state.Document.LogbookId,
            identity.DeviceId,
            exportedAt);
        var document = projection.Operations.Count == 0
            ? state.Document
            : PortableLogbookDocumentV2.CreateAustraliaFirst(
                state.Document.LogbookId,
                state.Document.CustomFieldDefinitions,
                state.Document.CurrencyOverrideDates,
                state.Document.Operations.Concat(projection.Operations));
        var packageBytes = PortableLogbookPackage.Write(document, key);
        File.WriteAllBytes(packageOutputPath, packageBytes);
        var manifest = PortableLogbookPackage.ReadManifestForInspection(packageBytes);
        var envelope = PortableLogbookWorkbookStorage.CreateEnvelope(document, packageBytes, state.ImportReceipts);
        var workbookRows = PortableLogbookWorkbookProjection.CreateCurrentRows(document);
        PortableLogbookWorkbookPackageStorage.WriteHiddenMetadataColumnValuesV2(
            workbookPath,
            workbookRows,
            document.CustomFieldDefinitions,
            writeVisiblePayloadCells: false);
        PortableLogbookWorkbookPackageStorage.WriteEnvelope(workbookPath, envelope);

        return new PortableLogbookExportResult(
            Path.GetFullPath(workbookPath),
            Path.GetFullPath(packageOutputPath),
            document.LogbookId,
            document.SchemaVersion,
            document.Operations.Count,
            document.CustomFieldDefinitions.Count,
            workbookRows.Count,
            projection.Operations.Count,
            projection.CreateCount,
            projection.CorrectionCount,
            projection.DeletionCount,
            manifest.CreatedAt,
            exportedAt);
    }

    public static PortableLogbookImportPreviewResult PreviewImport(
        string workbookPath,
        string recoveryCodeFilePath,
        string packageInputPath) =>
        PreviewImport(
            workbookPath,
            PortableLogbookCommandKeySource.RecoveryCodeFile(recoveryCodeFilePath),
            packageInputPath);

    public static PortableLogbookImportPreviewResult PreviewImport(
        string workbookPath,
        PortableLogbookCommandKeySource keySource,
        string packageInputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);
        ArgumentNullException.ThrowIfNull(keySource);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageInputPath);

        var key = ReadKey(keySource);
        var state = PortableLogbookWorkbookPackageStorage.OpenStateV2(workbookPath, key)
            ?? throw new UpdaterUsageException("Portable logbook storage is not enabled for this workbook.");
        var plan = CreateImportPlanV2(state.Document, ReadPackageForCommandV2(packageInputPath, key, state.Document.LogbookId));

        return new PortableLogbookImportPreviewResult(
            Path.GetFullPath(workbookPath),
            Path.GetFullPath(packageInputPath),
            state.Document.LogbookId,
            state.Document.Operations.Count,
            plan.Incoming.Document.Operations.Count,
            plan.Status,
            plan.NewOperations.Count,
            plan.DuplicateOperations.Count,
            plan.NewOperations.Count(operation => operation.Kind == PortableOperationKind.Create),
            plan.NewOperations.Count(operation => operation.Kind == PortableOperationKind.Correction),
            plan.NewOperations.Count(operation => operation.Kind == PortableOperationKind.Deletion),
            plan.Conflicts.Count,
            plan.CustomFieldConflictCount,
            CreateCommandSummariesV2(plan.NewOperations),
            CreateCommandSummariesV2(plan.DuplicateOperations));
    }

    public static PortableLogbookImportApplyResult ApplyImport(
        string workbookPath,
        string recoveryCodeFilePath,
        string packageInputPath,
        DateTimeOffset importedAt) =>
        ApplyImport(
            workbookPath,
            PortableLogbookCommandKeySource.RecoveryCodeFile(recoveryCodeFilePath),
            packageInputPath,
            importedAt);

    public static PortableLogbookImportApplyResult ApplyImport(
        string workbookPath,
        PortableLogbookCommandKeySource keySource,
        string packageInputPath,
        DateTimeOffset importedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);
        ArgumentNullException.ThrowIfNull(keySource);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageInputPath);

        var key = ReadKey(keySource);
        var state = PortableLogbookWorkbookPackageStorage.OpenStateV2(workbookPath, key)
            ?? throw new UpdaterUsageException("Portable logbook storage is not enabled for this workbook.");
        var packageBytes = ReadPackageBytesForCommand(packageInputPath);
        if (PortableLogbookImportLedger.HasSeenPackage(state.ImportReceipts, packageBytes))
        {
            return CreateImportApplyResultV2(workbookPath, packageInputPath, null, state.Document.LogbookId, "packageReplay", [], [], [], 0, false, false, importedAt);
        }

        var plan = CreateImportPlanV2(
            state.Document,
            ReadPackageForCommandV2(packageInputPath, key, state.Document.LogbookId));
        if (plan.Status is "requiresConflictResolution" or "requiresCustomFieldResolution")
        {
            return CreateImportApplyResultV2(workbookPath, packageInputPath, null, state.Document.LogbookId, "requiresResolution", plan.NewOperations, plan.DuplicateOperations, [], plan.CustomFieldConflictCount, false, false, importedAt, plan.Conflicts.Count);
        }

        var document = plan.NewOperations.Count == 0 &&
            state.Document.CustomFieldDefinitions.SequenceEqual(plan.CustomFieldDefinitions)
            ? state.Document
            : PortableLogbookDocumentV2.CreateAustraliaFirst(
                state.Document.LogbookId,
                plan.CustomFieldDefinitions,
                state.Document.CurrencyOverrideDates,
                state.Document.Operations.Concat(plan.NewOperations));
        var receipt = PortableLogbookImportLedger.CreateReceipt(packageBytes, plan.Incoming.Manifest, importedAt);
        var envelope = PortableLogbookWorkbookStorage.CreateEnvelope(
            document,
            PortableLogbookPackage.Write(document, key),
            state.ImportReceipts.Concat([receipt]));
        var workbookRows = PortableLogbookWorkbookProjection.CreateCurrentRows(document);
        var backupPath = CreateWorkbookBackup(workbookPath, "portable-import", importedAt);
        PortableLogbookWorkbookPackageStorage.WriteHiddenMetadataColumnValuesV2(
            workbookPath,
            workbookRows,
            document.CustomFieldDefinitions);
        PortableLogbookWorkbookPackageStorage.WriteEnvelope(workbookPath, envelope);

        return CreateImportApplyResultV2(workbookPath, packageInputPath, backupPath, document.LogbookId, plan.NewOperations.Count == 0 ? "duplicateOperationsRecorded" : "applied", plan.NewOperations, plan.DuplicateOperations, workbookRows, 0, true, true, importedAt, plan.Conflicts.Count);
    }

    public static PortableLogbookPrintedCopyResult CreatePrintedCopy(
        string workbookPath,
        string recoveryCodeFilePath,
        string outputPath,
        string holderName,
        DateOnly holderDateOfBirth,
        DateOnly certifiedOn,
        int recordsPerPage) =>
        CreatePrintedCopy(
            workbookPath,
            PortableLogbookCommandKeySource.RecoveryCodeFile(recoveryCodeFilePath),
            outputPath,
            holderName,
            holderDateOfBirth,
            certifiedOn,
            recordsPerPage);

    public static PortableLogbookPrintedCopyResult CreatePrintedCopy(
        string workbookPath,
        PortableLogbookCommandKeySource keySource,
        string outputPath,
        string holderName,
        DateOnly holderDateOfBirth,
        DateOnly certifiedOn,
        int recordsPerPage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);
        ArgumentNullException.ThrowIfNull(keySource);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(holderName);

        if (recordsPerPage < 1)
        {
            throw new UpdaterUsageException("--records-per-page must be a positive integer.");
        }

        if (File.Exists(outputPath))
        {
            throw new UpdaterUsageException($"Printed-copy output file already exists: {outputPath}");
        }

        var key = ReadKey(keySource);
        var state = PortableLogbookWorkbookPackageStorage.OpenStateV2(workbookPath, key)
            ?? throw new UpdaterUsageException("Portable logbook storage is not enabled for this workbook.");
        var request = PortableLogbookPrintedCopy.CreateRequest(
            CreatePresentationDocument(state.Document),
            holderName,
            holderDateOfBirth,
            certifiedOn);
        var pagePlan = PortableLogbookPrintedCopy.CreatePagePlan(request, recordsPerPage);
        var html = PortableLogbookPrintedCopy.RenderHtml(pagePlan);

        File.WriteAllText(outputPath, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return new PortableLogbookPrintedCopyResult(
            Path.GetFullPath(workbookPath),
            Path.GetFullPath(outputPath),
            state.Document.LogbookId,
            state.Document.SchemaVersion,
            pagePlan.Pages.Count,
            pagePlan.AuditSummary.CurrentRecordCount,
            pagePlan.AuditSummary.RevisionCount,
            pagePlan.AuditSummary.ConflictCount,
            recordsPerPage,
            certifiedOn);
    }

    public static PortableLogbookRevisionHistoryResult ReadRevisionHistory(
        string workbookPath,
        string recoveryCodeFilePath,
        EntryId entryId) =>
        ReadRevisionHistory(
            workbookPath,
            PortableLogbookCommandKeySource.RecoveryCodeFile(recoveryCodeFilePath),
            entryId);

    public static PortableLogbookRevisionHistoryResult ReadRevisionHistory(
        string workbookPath,
        PortableLogbookCommandKeySource keySource,
        EntryId entryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);
        ArgumentNullException.ThrowIfNull(keySource);

        var key = ReadKey(keySource);
        var state = PortableLogbookWorkbookPackageStorage.OpenStateV2(workbookPath, key)
            ?? throw new UpdaterUsageException("Portable logbook storage is not enabled for this workbook.");
        var view = PortableLogbookRevisionHistory.ForEntry(CreatePresentationDocument(state.Document), entryId);

        return new PortableLogbookRevisionHistoryResult(
            Path.GetFullPath(workbookPath),
            state.Document.LogbookId,
            view.EntryId,
            view.CurrentRevisionId,
            view.IsDeleted,
            view.HasConflict,
            view.ConflictHeadRevisionIds,
            view.Revisions);
    }

    public static PortableLogbookResolveConflictResult ResolveConflict(
        string workbookPath,
        string recoveryCodeFilePath,
        EntryId entryId,
        RevisionId selectedRevisionId,
        string? note,
        DateTimeOffset resolvedAt) =>
        ResolveConflict(
            workbookPath,
            PortableLogbookCommandKeySource.RecoveryCodeFile(recoveryCodeFilePath),
            entryId,
            selectedRevisionId,
            note,
            resolvedAt);

    public static PortableLogbookResolveConflictResult ResolveConflict(
        string workbookPath,
        PortableLogbookCommandKeySource keySource,
        EntryId entryId,
        RevisionId selectedRevisionId,
        string? note,
        DateTimeOffset resolvedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);
        ArgumentNullException.ThrowIfNull(keySource);
        ArgumentNullException.ThrowIfNull(entryId);
        ArgumentNullException.ThrowIfNull(selectedRevisionId);

        var key = ReadKey(keySource);
        var state = PortableLogbookWorkbookPackageStorage.OpenStateV2(workbookPath, key)
            ?? throw new UpdaterUsageException("Portable logbook storage is not enabled for this workbook.");
        var merge = PortableLogbookWorkbookProjection.MergeV2(state.Document.Operations);
        var conflict = merge.Conflicts.SingleOrDefault(item => item.EntryId == entryId)
            ?? throw new UpdaterUsageException($"Portable entry '{entryId.Value}' does not have an unresolved conflict.");
        if (!conflict.HeadRevisionIds.Contains(selectedRevisionId))
        {
            throw new UpdaterUsageException($"Revision '{selectedRevisionId.Value}' is not a conflict head for portable entry '{entryId.Value}'.");
        }

        var selectedOperation = state.Document.Operations.SingleOrDefault(operation =>
                operation.EntryId == entryId &&
                operation.RevisionId == selectedRevisionId)
            ?? throw new UpdaterUsageException($"Revision '{selectedRevisionId.Value}' was not found for portable entry '{entryId.Value}'.");
        var deviceId = selectedOperation.DeviceId;
        var resolutionRevisionId = RevisionId.New();
        var resolution = selectedOperation.Entry is null
            ? PortableLogbookOperationV2.Delete(
                state.Document.LogbookId,
                entryId,
                resolutionRevisionId,
                conflict.HeadRevisionIds,
                deviceId,
                resolvedAt,
                note)
            : PortableLogbookOperationV2.ResolveConflict(
                state.Document.LogbookId,
                entryId,
                resolutionRevisionId,
                conflict.HeadRevisionIds,
                deviceId,
                resolvedAt,
                selectedOperation.Entry,
                note);
        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(
            state.Document.LogbookId,
            state.Document.CustomFieldDefinitions,
            state.Document.CurrencyOverrideDates,
            state.Document.Operations.Concat([resolution]));
        var backupPath = CreateWorkbookBackup(workbookPath, "portable-resolve-conflict", resolvedAt);
        var envelope = PortableLogbookWorkbookStorage.CreateEnvelope(
            document,
            PortableLogbookPackage.Write(document, key),
            state.ImportReceipts);
        PortableLogbookWorkbookPackageStorage.WriteHiddenMetadataColumnValuesV2(
            workbookPath,
            PortableLogbookWorkbookProjection.CreateCurrentRows(document),
            document.CustomFieldDefinitions);
        PortableLogbookWorkbookPackageStorage.WriteEnvelope(workbookPath, envelope);

        var postMerge = PortableLogbookWorkbookProjection.MergeV2(document.Operations);
        return new PortableLogbookResolveConflictResult(
            Path.GetFullPath(workbookPath),
            Path.GetFullPath(backupPath),
            state.Document.LogbookId,
            entryId,
            selectedRevisionId,
            resolutionRevisionId,
            postMerge.Conflicts.Count,
            resolvedAt);
    }

    // Printed-copy and revision-history views pre-date the workbook-faithful
    // schema. They receive a read-only presentation projection; persistent
    // storage and package exchange remain entirely schema-v2.
    private static PortableLogbookDocument CreatePresentationDocument(PortableLogbookDocumentV2 document) =>
        PortableLogbookDocument.CreateAustraliaFirst(
            document.LogbookId,
            document.CustomFieldDefinitions,
            document.Operations.Select(ToPresentationOperation));

    private static PortableLogbookOperation ToPresentationOperation(PortableLogbookOperationV2 operation)
    {
        var entry = operation.Entry is null ? null : ToPresentationEntry(operation.Entry);
        return operation.Kind switch
        {
            PortableOperationKind.Create => new CreateEntryOperation(
                operation.LogbookId, operation.EntryId, operation.RevisionId, operation.DeviceId, operation.CreatedAt,
                entry ?? throw new InvalidOperationException("Create operation is missing its workbook entry.")),
            PortableOperationKind.Correction => new CorrectEntryOperation(
                operation.LogbookId, operation.EntryId, operation.RevisionId, operation.ParentRevisionIds.ToHashSet(), operation.DeviceId, operation.CreatedAt,
                entry ?? throw new InvalidOperationException("Correction operation is missing its workbook entry.")),
            PortableOperationKind.Deletion => new DeleteEntryOperation(
                operation.LogbookId, operation.EntryId, operation.RevisionId, operation.ParentRevisionIds.ToHashSet(), operation.DeviceId, operation.CreatedAt, operation.Reason),
            PortableOperationKind.ConflictResolution => new ResolveConflictOperation(
                operation.LogbookId, operation.EntryId, operation.RevisionId, operation.ParentRevisionIds.ToHashSet(), operation.DeviceId, operation.CreatedAt,
                entry ?? throw new InvalidOperationException("Conflict-resolution operation is missing its workbook entry."), operation.ResolutionNote),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation.Kind, "Unknown portable operation kind.")
        };
    }

    private static PortableLogbookEntry ToPresentationEntry(PortableLogbookWorkbookEntry entry) => new(
        entry.Date,
        entry.Type,
        entry.Reg,
        entry.FlightId,
        entry.From,
        entry.To,
        entry.Via,
        entry.Remarks,
        Sum(entry.MeIcusDay, entry.MeIcusNight),
        Sum(entry.SeCommandDay, entry.SeCommandNight, entry.MeCommandDay, entry.MeCommandNight),
        Sum(entry.CopilotDay, entry.CopilotNight),
        Sum(entry.SeDualDay, entry.SeDualNight, entry.MeDualDay, entry.MeDualNight),
        null,
        Sum(entry.SeIcusDay, entry.SeDualDay, entry.SeCommandDay, entry.MeIcusDay, entry.MeDualDay, entry.MeCommandDay, entry.CopilotDay),
        Sum(entry.SeIcusNight, entry.SeDualNight, entry.SeCommandNight, entry.MeIcusNight, entry.MeDualNight, entry.MeCommandNight, entry.CopilotNight),
        entry.IfrIf,
        entry.IfrSim,
        null,
        null,
        entry.LandingsDay,
        entry.LandingsNight,
        Sum(entry.Ils, entry.Vor, entry.Rnp, entry.Ndb, entry.DgaCdi, entry.DgaAzi, entry.Circling),
        null,
        entry.Rnp,
        entry.Circling,
        entry.CustomFields);

    private static decimal? Sum(params decimal?[] values)
    {
        var presentValues = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return presentValues.Length == 0 ? null : presentValues.Sum();
    }

    private static int? Sum(params int?[] values)
    {
        var presentValues = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return presentValues.Length == 0 ? null : presentValues.Sum();
    }

    private static V2CommandImportPlan CreateImportPlanV2(
        PortableLogbookDocumentV2 localDocument,
        PortableLogbookPackageReadResultV2 incoming)
    {
        var plan = PortableLogbookExchange.PlanImport(localDocument, incoming.Document);
        return new V2CommandImportPlan(
            incoming,
            plan.Preview.NewOperations,
            plan.Preview.DuplicateOperations,
            plan.Preview.Conflicts,
            plan.Preview.CustomFieldDefinitions.Conflicts.Count,
            plan.Preview.CustomFieldDefinitions.Definitions,
            FormatImportPlanStatus(plan.Status));
    }

    private static PortableLogbookImportApplyResult CreateImportApplyResultV2(
        string workbookPath,
        string packageInputPath,
        string? backupPath,
        LogbookId logbookId,
        string status,
        IReadOnlyList<PortableLogbookOperationV2> newOperations,
        IReadOnlyList<PortableLogbookOperationV2> duplicateOperations,
        IReadOnlyList<PortableLogbookWorkbookRowV2> workbookRows,
        int customFieldConflictCount,
        bool receiptRecorded,
        bool storageUpdated,
        DateTimeOffset importedAt,
        int conflictCount = 0) =>
        new(
            Path.GetFullPath(workbookPath),
            Path.GetFullPath(packageInputPath),
            backupPath is null ? null : Path.GetFullPath(backupPath),
            logbookId,
            status,
            newOperations.Count,
            duplicateOperations.Count,
            newOperations.Count(operation => operation.Kind == PortableOperationKind.Create),
            newOperations.Count(operation => operation.Kind == PortableOperationKind.Correction),
            newOperations.Count(operation => operation.Kind == PortableOperationKind.Deletion),
            conflictCount,
            customFieldConflictCount,
            CreateCommandSummariesV2(newOperations),
            CreateCommandSummariesV2(duplicateOperations),
            workbookRows.Count,
            receiptRecorded,
            storageUpdated,
            importedAt);

    private static IReadOnlyList<PortableLogbookImportCommandSummary> CreateCommandSummariesV2(
        IReadOnlyList<PortableLogbookOperationV2> operations) =>
        operations.Select(operation => new PortableLogbookImportCommandSummary(
            operation.EntryId,
            operation.RevisionId,
            FormatOperationKind(operation.Kind),
            operation.Entry?.Date,
            operation.Entry?.Type,
            operation.Entry?.Reg,
            operation.Entry?.From,
            operation.Entry?.To,
            operation.Entry?.Remarks,
            operation.Reason)).ToArray();

    private static void WriteHumanResult(object result)
    {
        switch (result)
        {
            case PortableLogbookEnableResult enable:
                WriteHumanEnableResult(enable);
                break;
            case PortableLogbookExportResult export:
                WriteHumanExportResult(export);
                break;
            case PortableLogbookImportPreviewResult importPreview:
                WriteHumanImportPreviewResult(importPreview);
                break;
            case PortableLogbookImportApplyResult importApply:
                WriteHumanImportApplyResult(importApply);
                break;
            case PortableLogbookPrintedCopyResult printedCopy:
                WriteHumanPrintedCopyResult(printedCopy);
                break;
            case PortableLogbookRevisionHistoryResult revisionHistory:
                WriteHumanRevisionHistoryResult(revisionHistory);
                break;
            case PortableLogbookResolveConflictResult resolveConflict:
                WriteHumanResolveConflictResult(resolveConflict);
                break;
            case PortableLogbookWorkbookStatus status:
                WriteHumanStatus(status);
                break;
            default:
                throw new InvalidOperationException($"Unsupported portable command result: {result.GetType().Name}");
        }
    }

    private static void WriteHumanEnableResult(PortableLogbookEnableResult result)
    {
        Console.WriteLine("Portable logbook: enabled");
        Console.WriteLine($"Workbook: {result.WorkbookPath}");
        Console.WriteLine($"Logbook ID: {result.LogbookId}");
        Console.WriteLine($"Device ID: {result.DeviceId}");
        Console.WriteLine($"Schema version: {result.SchemaVersion}");
        Console.WriteLine($"Storage version: {result.StorageVersion}");
        Console.WriteLine($"Initial operations: {result.InitialOperationCount}");
        Console.WriteLine($"Recovery file: {result.RecoveryOutputPath}");
        Console.WriteLine($"Backup: {result.BackupPath}");
        if (!string.IsNullOrWhiteSpace(result.WindowsCredentialTargetName))
        {
            Console.WriteLine($"Windows credential: {result.WindowsCredentialTargetName}");
        }
    }

    private static void WriteHumanExportResult(PortableLogbookExportResult result)
    {
        Console.WriteLine("Portable package: exported");
        Console.WriteLine($"Workbook: {result.WorkbookPath}");
        Console.WriteLine($"Package: {result.PackageOutputPath}");
        Console.WriteLine($"Logbook ID: {result.LogbookId}");
        Console.WriteLine($"Schema version: {result.SchemaVersion}");
        Console.WriteLine($"Operations: {result.OperationCount}");
        Console.WriteLine($"Custom fields: {result.CustomFieldDefinitionCount}");
        Console.WriteLine($"Workbook rows: {result.WorkbookRowCount}");
        Console.WriteLine($"Reconciled workbook changes: {result.PendingOperationCount}");
        Console.WriteLine($"Package created: {result.PackageCreatedAt:O}");
    }

    private static void WriteHumanImportPreviewResult(PortableLogbookImportPreviewResult result)
    {
        Console.WriteLine("Portable import preview");
        Console.WriteLine($"Workbook: {result.WorkbookPath}");
        Console.WriteLine($"Package: {result.PackageInputPath}");
        Console.WriteLine($"Logbook ID: {result.LogbookId}");
        Console.WriteLine($"Status: {result.Status}");
        Console.WriteLine($"Local operations: {result.LocalOperationCount}");
        Console.WriteLine($"Incoming operations: {result.IncomingOperationCount}");
        Console.WriteLine($"New operations: {result.NewOperationCount}");
        Console.WriteLine($"Duplicate operations: {result.DuplicateOperationCount}");
        Console.WriteLine($"Creates: {result.CreateCount}");
        Console.WriteLine($"Corrections: {result.CorrectionCount}");
        Console.WriteLine($"Deletions: {result.DeletionCount}");
        Console.WriteLine($"Entry conflicts: {result.ConflictCount}");
        Console.WriteLine($"Custom-field conflicts: {result.CustomFieldConflictCount}");
        WriteImportSummaries("New operation", result.NewOperationSummaries);
        WriteImportSummaries("Duplicate operation", result.DuplicateOperationSummaries);
    }

    private static void WriteHumanImportApplyResult(PortableLogbookImportApplyResult result)
    {
        Console.WriteLine("Portable import apply");
        Console.WriteLine($"Workbook: {result.WorkbookPath}");
        Console.WriteLine($"Package: {result.PackageInputPath}");
        Console.WriteLine($"Logbook ID: {result.LogbookId}");
        Console.WriteLine($"Status: {result.Status}");
        Console.WriteLine($"New operations: {result.NewOperationCount}");
        Console.WriteLine($"Duplicate operations: {result.DuplicateOperationCount}");
        Console.WriteLine($"Creates: {result.CreateCount}");
        Console.WriteLine($"Corrections: {result.CorrectionCount}");
        Console.WriteLine($"Deletions: {result.DeletionCount}");
        Console.WriteLine($"Entry conflicts: {result.ConflictCount}");
        Console.WriteLine($"Custom-field conflicts: {result.CustomFieldConflictCount}");
        WriteImportSummaries("New operation", result.NewOperationSummaries);
        WriteImportSummaries("Duplicate operation", result.DuplicateOperationSummaries);
        Console.WriteLine($"Workbook rows requiring sync: {result.WorkbookRowCount}");
        Console.WriteLine($"Storage updated: {result.StorageUpdated}");
        if (!string.IsNullOrWhiteSpace(result.BackupPath))
        {
            Console.WriteLine($"Backup: {result.BackupPath}");
        }
    }

    private static void WriteHumanPrintedCopyResult(PortableLogbookPrintedCopyResult result)
    {
        Console.WriteLine("Portable printed copy: created");
        Console.WriteLine($"Workbook: {result.WorkbookPath}");
        Console.WriteLine($"Output: {result.OutputPath}");
        Console.WriteLine($"Logbook ID: {result.LogbookId}");
        Console.WriteLine($"Schema version: {result.SchemaVersion}");
        Console.WriteLine($"Pages: {result.PageCount}");
        Console.WriteLine($"Current records: {result.CurrentRecordCount}");
        Console.WriteLine($"Revision history records: {result.RevisionCount}");
        Console.WriteLine($"Unresolved conflicts: {result.ConflictCount}");
        Console.WriteLine($"Records per page: {result.RecordsPerPage}");
        Console.WriteLine($"Certified on: {result.CertifiedOn:yyyy-MM-dd}");
    }

    private static void WriteHumanRevisionHistoryResult(PortableLogbookRevisionHistoryResult result)
    {
        Console.WriteLine("Portable revision history");
        Console.WriteLine($"Workbook: {result.WorkbookPath}");
        Console.WriteLine($"Logbook ID: {result.LogbookId}");
        Console.WriteLine($"Entry ID: {result.EntryId}");
        Console.WriteLine($"Current revision ID: {result.CurrentRevisionId?.Value ?? "(none)"}");
        Console.WriteLine($"Deleted: {result.IsDeleted}");
        Console.WriteLine($"Conflict: {result.HasConflict}");
        if (result.ConflictHeadRevisionIds.Count > 0)
        {
            Console.WriteLine($"Conflict heads: {string.Join(", ", result.ConflictHeadRevisionIds.Select(id => id.Value))}");
        }

        Console.WriteLine($"Revisions: {result.Revisions.Count}");
        foreach (var revision in result.Revisions)
        {
            Console.WriteLine(
                $"Revision: {FormatOperationKind(revision.Kind)} {revision.RevisionId.Value} " +
                $"{revision.CreatedAt:O} device={revision.DeviceId.Value}");
            if (revision.Entry is not null)
            {
                Console.WriteLine(
                    $"  Entry: {revision.Entry.Date?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "unknown-date"} " +
                    $"{revision.Entry.Registration ?? "unknown-registration"} " +
                    $"{revision.Entry.From ?? "?"}-{revision.Entry.To ?? "?"}");
            }
        }
    }

    private static void WriteHumanResolveConflictResult(PortableLogbookResolveConflictResult result)
    {
        Console.WriteLine("Portable conflict: resolved");
        Console.WriteLine($"Workbook: {result.WorkbookPath}");
        Console.WriteLine($"Backup: {result.BackupPath}");
        Console.WriteLine($"Logbook ID: {result.LogbookId}");
        Console.WriteLine($"Entry ID: {result.EntryId}");
        Console.WriteLine($"Kept revision ID: {result.KeptRevisionId}");
        Console.WriteLine($"Resolution revision ID: {result.ResolutionRevisionId}");
        Console.WriteLine($"Remaining conflicts: {result.RemainingConflictCount}");
    }

    private static void WriteImportSummaries(
        string label,
        IReadOnlyList<PortableLogbookImportCommandSummary> summaries)
    {
        foreach (var summary in summaries)
        {
            Console.WriteLine(
                $"{label}: {summary.Kind} {summary.Date?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "unknown-date"} " +
                $"{summary.Registration ?? "unknown-registration"} {summary.From ?? "?"}-{summary.To ?? "?"}");
        }
    }

    private static string FormatOperationKind(PortableOperationKind kind) =>
        kind switch
        {
            PortableOperationKind.Create => "create",
            PortableOperationKind.Correction => "correction",
            PortableOperationKind.Deletion => "deletion",
            PortableOperationKind.ConflictResolution => "conflictResolution",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown portable operation kind.")
        };

    private static void WriteHumanStatus(PortableLogbookWorkbookStatus status)
    {
        Console.WriteLine(status.IsEnabled
            ? "Portable logbook: enabled"
            : "Portable logbook: not enabled");
        Console.WriteLine($"Workbook: {status.WorkbookPath}");

        if (!status.IsEnabled || status.Summary is null)
        {
            return;
        }

        Console.WriteLine($"Logbook ID: {status.LogbookId}");
        Console.WriteLine($"Schema version: {status.SchemaVersion}");
        Console.WriteLine($"Storage version: {status.StorageVersion}");
        Console.WriteLine($"Operations: {status.Summary.OperationCount}");
        Console.WriteLine($"Current records: {status.Summary.CurrentRecordCount}");
        Console.WriteLine($"Unresolved conflicts: {status.Summary.UnresolvedConflictCount}");
        Console.WriteLine($"Import receipts: {status.ImportReceiptCount}");
        Console.WriteLine($"Last operation: {status.Summary.LastOperationAt:O}");
    }

    private static string CreateRecoveryFileText(
        PortableLogbookSetupPlan setup,
        string workbookPath,
        string? credentialTargetName,
        DateTimeOffset createdAt) =>
        string.Join(
            Environment.NewLine,
            CreateRecoveryFileLines(setup.LogbookId, setup.DeviceId, setup.Key, workbookPath, credentialTargetName, createdAt));

    private static string CreateRecoveryFileText(
        PortableLogbookSetupPlanV2 setup,
        string workbookPath,
        string? credentialTargetName,
        DateTimeOffset createdAt) =>
        string.Join(
            Environment.NewLine,
            CreateRecoveryFileLines(setup.LogbookId, setup.DeviceId, setup.Key, workbookPath, credentialTargetName, createdAt));

    private static IReadOnlyList<string> CreateRecoveryFileLines(
        LogbookId logbookId,
        DeviceId deviceId,
        PortableLogbookKey key,
        string workbookPath,
        string? credentialTargetName,
        DateTimeOffset createdAt)
    {
        var lines = new List<string>
        {
                "Electronic Logbook portable recovery code",
                string.Empty,
                $"Workbook: {Path.GetFullPath(workbookPath)}",
                $"Created: {createdAt:O}",
                $"Logbook ID: {logbookId.Value}",
                $"Device ID: {deviceId.Value}",
                $"Recovery code: {key.ToRecoveryCode()}",
        };
        if (!string.IsNullOrWhiteSpace(credentialTargetName))
        {
            lines.Add($"Windows credential target: {credentialTargetName}");
        }

        lines.AddRange(
            [
                string.Empty,
                "Keep this file separate from the workbook. Anyone with this recovery code can decrypt portable logbook packages for this logbook.",
                string.Empty
            ]);
        return lines;
    }

    private static string CreateWorkbookBackup(string workbookPath, string purpose, DateTimeOffset createdAt)
    {
        var fullPath = Path.GetFullPath(workbookPath);
        var directory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fullPath);
        var extension = Path.GetExtension(fullPath);
        var timestamp = createdAt.ToLocalTime().ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture);
        var backupPath = Path.Combine(directory, $"{nameWithoutExtension}.{purpose}-backup-{timestamp}{extension}");
        var suffix = 1;
        while (File.Exists(backupPath))
        {
            backupPath = Path.Combine(directory, $"{nameWithoutExtension}.{purpose}-backup-{timestamp}-{suffix}{extension}");
            suffix++;
        }

        File.Copy(fullPath, backupPath);
        return backupPath;
    }

    private static string ReadRecoveryCode(string recoveryCodeFilePath)
    {
        var lines = File.ReadAllLines(recoveryCodeFilePath);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Recovery code:", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.Split(':', 2)[1].Trim();
            }
        }

        var content = File.ReadAllText(recoveryCodeFilePath).Trim();
        if (!string.IsNullOrWhiteSpace(content) && !content.Contains('\r') && !content.Contains('\n'))
        {
            return content;
        }

        throw new UpdaterUsageException("Recovery code file does not contain a recovery code.");
    }

    private static PortableLogbookCommandKeySource CreateKeySource(PortableLogbookCommandOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.RecoveryCodeFilePath))
        {
            return PortableLogbookCommandKeySource.RecoveryCodeFile(options.RecoveryCodeFilePath);
        }

        if (!string.IsNullOrWhiteSpace(options.WindowsCredentialTargetName))
        {
            return PortableLogbookCommandKeySource.WindowsCredential(options.WindowsCredentialTargetName);
        }

        throw new UpdaterUsageException("Portable command requires a recovery-code file or Windows credential target.");
    }

    private static PortableLogbookKey ReadKey(PortableLogbookCommandKeySource keySource)
    {
        return keySource.Kind switch
        {
            PortableLogbookCommandKeySourceKind.RecoveryCodeFile => PortableLogbookKey.FromRecoveryCode(ReadRecoveryCode(keySource.Value)),
            PortableLogbookCommandKeySourceKind.WindowsCredential => PortableLogbookWindowsCredentialStore.LoadKey(keySource.Value)
                ?? throw new UpdaterUsageException($"Windows credential was not found: {keySource.Value}"),
            _ => throw new ArgumentOutOfRangeException(nameof(keySource), "Unsupported portable key source.")
        };
    }

    private static PortableLogbookPackageReadResultV2 ReadPackageForCommandV2(
        string packageInputPath,
        PortableLogbookKey key,
        LogbookId expectedLogbookId)
    {
        try
        {
            return PortableLogbookPackageFile.ReadV2(packageInputPath, key, expectedLogbookId);
        }
        catch (Exception ex) when (IsPackageUsageError(ex))
        {
            throw new UpdaterUsageException(ex.Message);
        }
    }

    private static byte[] ReadPackageBytesForCommand(string packageInputPath)
    {
        try
        {
            return PortableLogbookPackageFile.ReadBytes(packageInputPath);
        }
        catch (Exception ex) when (IsPackageUsageError(ex))
        {
            throw new UpdaterUsageException(ex.Message);
        }
    }

    private static bool IsPackageUsageError(Exception ex) =>
        ex is ArgumentException ||
        ex is PortableLogbookPackageException
        {
            Error: PortableLogbookPackageError.PackageTooLarge or
                PortableLogbookPackageError.PackageEmpty
        };

    private static string FormatImportPlanStatus(PortableLogbookImportPlanStatus status) =>
        status switch
        {
            PortableLogbookImportPlanStatus.DuplicateOnly => "duplicateOnly",
            PortableLogbookImportPlanStatus.ReadyToApply => "readyToApply",
            PortableLogbookImportPlanStatus.RequiresConflictResolution => "requiresConflictResolution",
            PortableLogbookImportPlanStatus.RequiresCustomFieldResolution => "requiresCustomFieldResolution",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown import plan status.")
        };

    private static string FormatPackageImportStatus(PortableLogbookPackageImportStatus status) =>
        status switch
        {
            PortableLogbookPackageImportStatus.PackageReplay => "packageReplay",
            PortableLogbookPackageImportStatus.DuplicateOperationsRecorded => "duplicateOperationsRecorded",
            PortableLogbookPackageImportStatus.Applied => "applied",
            PortableLogbookPackageImportStatus.RequiresResolution => "requiresResolution",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown package import status.")
        };

    private static void TryDeleteRecoveryFile(string recoveryOutputPath)
    {
        try
        {
            File.Delete(recoveryOutputPath);
        }
        catch
        {
            // Preserve the workbook-write failure that triggered cleanup.
        }
    }

    private static void TryDeleteWindowsCredential(string targetName)
    {
        try
        {
            PortableLogbookWindowsCredentialStore.DeleteKey(targetName);
        }
        catch
        {
            // Preserve the workbook-write failure that triggered cleanup.
        }
    }
}

internal sealed record V2CommandImportPlan(
    PortableLogbookPackageReadResultV2 Incoming,
    IReadOnlyList<PortableLogbookOperationV2> NewOperations,
    IReadOnlyList<PortableLogbookOperationV2> DuplicateOperations,
    IReadOnlyList<PortableLogbookConflict> Conflicts,
    int CustomFieldConflictCount,
    IReadOnlyList<CustomFieldDefinition> CustomFieldDefinitions,
    string Status);

public sealed record PortableLogbookEnableResult(
    string WorkbookPath,
    string RecoveryOutputPath,
    string BackupPath,
    LogbookId LogbookId,
    DeviceId DeviceId,
    int SchemaVersion,
    int StorageVersion,
    int InitialOperationCount,
    string? WindowsCredentialTargetName,
    DateTimeOffset CreatedAt);

public sealed record PortableLogbookExportResult(
    string WorkbookPath,
    string PackageOutputPath,
    LogbookId LogbookId,
    int SchemaVersion,
    int OperationCount,
    int CustomFieldDefinitionCount,
    int WorkbookRowCount,
    int PendingOperationCount,
    int PendingCreateCount,
    int PendingCorrectionCount,
    int PendingDeletionCount,
    DateTimeOffset PackageCreatedAt,
    DateTimeOffset ExportedAt);

public sealed record PortableLogbookImportPreviewResult(
    string WorkbookPath,
    string PackageInputPath,
    LogbookId LogbookId,
    int LocalOperationCount,
    int IncomingOperationCount,
    string Status,
    int NewOperationCount,
    int DuplicateOperationCount,
    int CreateCount,
    int CorrectionCount,
    int DeletionCount,
    int ConflictCount,
    int CustomFieldConflictCount,
    IReadOnlyList<PortableLogbookImportCommandSummary> NewOperationSummaries,
    IReadOnlyList<PortableLogbookImportCommandSummary> DuplicateOperationSummaries);

public sealed record PortableLogbookImportApplyResult(
    string WorkbookPath,
    string PackageInputPath,
    string? BackupPath,
    LogbookId LogbookId,
    string Status,
    int NewOperationCount,
    int DuplicateOperationCount,
    int CreateCount,
    int CorrectionCount,
    int DeletionCount,
    int ConflictCount,
    int CustomFieldConflictCount,
    IReadOnlyList<PortableLogbookImportCommandSummary> NewOperationSummaries,
    IReadOnlyList<PortableLogbookImportCommandSummary> DuplicateOperationSummaries,
    int WorkbookRowCount,
    bool ReceiptRecorded,
    bool StorageUpdated,
    DateTimeOffset ImportedAt);

public sealed record PortableLogbookPrintedCopyResult(
    string WorkbookPath,
    string OutputPath,
    LogbookId LogbookId,
    int SchemaVersion,
    int PageCount,
    int CurrentRecordCount,
    int RevisionCount,
    int ConflictCount,
    int RecordsPerPage,
    DateOnly CertifiedOn);

public sealed record PortableLogbookRevisionHistoryResult(
    string WorkbookPath,
    LogbookId LogbookId,
    EntryId EntryId,
    RevisionId? CurrentRevisionId,
    bool IsDeleted,
    bool HasConflict,
    IReadOnlyList<RevisionId> ConflictHeadRevisionIds,
    IReadOnlyList<PortableLogbookRevisionHistoryItem> Revisions)
{
    public int RevisionCount => Revisions.Count;
}

public sealed record PortableLogbookResolveConflictResult(
    string WorkbookPath,
    string BackupPath,
    LogbookId LogbookId,
    EntryId EntryId,
    RevisionId KeptRevisionId,
    RevisionId ResolutionRevisionId,
    int RemainingConflictCount,
    DateTimeOffset ResolvedAt);

public sealed record PortableLogbookImportCommandSummary(
    EntryId EntryId,
    RevisionId RevisionId,
    string Kind,
    DateOnly? Date,
    string? AircraftType,
    string? Registration,
    string? From,
    string? To,
    string? Details,
    string? DeletionReason);

public sealed record PortableLogbookCommandKeySource(PortableLogbookCommandKeySourceKind Kind, string Value)
{
    public static PortableLogbookCommandKeySource RecoveryCodeFile(string path) =>
        new(PortableLogbookCommandKeySourceKind.RecoveryCodeFile, path);

    public static PortableLogbookCommandKeySource WindowsCredential(string targetName) =>
        new(PortableLogbookCommandKeySourceKind.WindowsCredential, targetName);
}

public enum PortableLogbookCommandKeySourceKind
{
    RecoveryCodeFile,
    WindowsCredential
}

public sealed record PortableLogbookWorkbookStatus(
    bool IsEnabled,
    string WorkbookPath,
    LogbookId? LogbookId,
    int? SchemaVersion,
    int? StorageVersion,
    PortableLogbookRedactedSummary? Summary,
    int ImportReceiptCount);
