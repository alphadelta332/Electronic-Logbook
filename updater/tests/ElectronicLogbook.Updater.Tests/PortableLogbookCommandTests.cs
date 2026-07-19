using System.Text.Json;
using ElectronicLogbook.Portable;
using ElectronicLogbook.Updater;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookCommandTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"PortableLogbookCommandTests-{Guid.NewGuid():N}");

    public PortableLogbookCommandTests()
    {
        Directory.CreateDirectory(directory);
    }

    [Fact]
    public void ParseAcceptsPortableStatusWorkbookPath()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);

        var options = PortableLogbookCommandOptions.Parse(["status", "--workbook", workbook, "--json"]);

        Assert.Equal(PortableLogbookCommand.Status, options.Command);
        Assert.Equal(Path.GetFullPath(workbook), options.WorkbookPath);
        Assert.True(options.Json);
    }

    [Fact]
    public void ParseAcceptsPortableEnableWorkbookAndRecoveryOutputPath()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");

        var options = PortableLogbookCommandOptions.Parse(
            ["enable", "--workbook", workbook, "--recovery-output", recovery, "--json"]);

        Assert.Equal(PortableLogbookCommand.Enable, options.Command);
        Assert.Equal(Path.GetFullPath(workbook), options.WorkbookPath);
        Assert.Equal(Path.GetFullPath(recovery), options.RecoveryOutputPath);
        Assert.True(options.Json);
    }

    [Fact]
    public void ParseAcceptsPortableEnableWithWindowsCredentialSave()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");

        var options = PortableLogbookCommandOptions.Parse(
            ["enable", "--workbook", workbook, "--recovery-output", recovery, "--save-windows-credential"]);

        Assert.Equal(PortableLogbookCommand.Enable, options.Command);
        Assert.True(options.SaveWindowsCredential);
    }

    [Fact]
    public void ParseAcceptsPortableExportWorkbookRecoveryFileAndPackageOutputPath()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        File.WriteAllText(recovery, "Recovery code: placeholder");
        var package = Path.Combine(directory, "export.elogbook");

        var options = PortableLogbookCommandOptions.Parse(
            ["export", "--workbook", workbook, "--recovery-code-file", recovery, "--output", package, "--json"]);

        Assert.Equal(PortableLogbookCommand.Export, options.Command);
        Assert.Equal(Path.GetFullPath(workbook), options.WorkbookPath);
        Assert.Equal(Path.GetFullPath(recovery), options.RecoveryCodeFilePath);
        Assert.Equal(Path.GetFullPath(package), options.PackageOutputPath);
        Assert.True(options.Json);
    }

    [Fact]
    public void ParseAcceptsPortableExportWithWindowsCredentialTarget()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var package = Path.Combine(directory, "export.elogbook");

        var options = PortableLogbookCommandOptions.Parse(
            ["export", "--workbook", workbook, "--windows-credential-target", "ElectronicLogbook.Tests/target", "--output", package]);

        Assert.Equal(PortableLogbookCommand.Export, options.Command);
        Assert.Equal("ElectronicLogbook.Tests/target", options.WindowsCredentialTargetName);
        Assert.Null(options.RecoveryCodeFilePath);
    }

    [Fact]
    public void ParseAcceptsPortableImportPreviewWorkbookRecoveryFileAndPackagePath()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        File.WriteAllText(recovery, "Recovery code: placeholder");
        var package = Path.Combine(directory, "incoming.elogbook");
        File.WriteAllBytes(package, [1]);

        var options = PortableLogbookCommandOptions.Parse(
            ["import-preview", "--workbook", workbook, "--recovery-code-file", recovery, "--package", package, "--json"]);

        Assert.Equal(PortableLogbookCommand.ImportPreview, options.Command);
        Assert.Equal(Path.GetFullPath(workbook), options.WorkbookPath);
        Assert.Equal(Path.GetFullPath(recovery), options.RecoveryCodeFilePath);
        Assert.Equal(Path.GetFullPath(package), options.PackageInputPath);
        Assert.True(options.Json);
    }

    [Fact]
    public void ParseAcceptsPortableImportApplyWorkbookRecoveryFileAndPackagePath()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        File.WriteAllText(recovery, "Recovery code: placeholder");
        var package = Path.Combine(directory, "incoming.elogbook");
        File.WriteAllBytes(package, [1]);

        var options = PortableLogbookCommandOptions.Parse(
            ["import-apply", "--workbook", workbook, "--recovery-code-file", recovery, "--package", package, "--json"]);

        Assert.Equal(PortableLogbookCommand.ImportApply, options.Command);
        Assert.Equal(Path.GetFullPath(workbook), options.WorkbookPath);
        Assert.Equal(Path.GetFullPath(recovery), options.RecoveryCodeFilePath);
        Assert.Equal(Path.GetFullPath(package), options.PackageInputPath);
        Assert.True(options.Json);
    }

    [Fact]
    public void ParseRejectsMissingWorkbookForStatus()
    {
        var exception = Assert.Throws<UpdaterUsageException>(
            () => PortableLogbookCommandOptions.Parse(["status"]));

        Assert.Contains("--workbook", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsMissingRecoveryOutputForEnable()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);

        var exception = Assert.Throws<UpdaterUsageException>(
            () => PortableLogbookCommandOptions.Parse(["enable", "--workbook", workbook]));

        Assert.Contains("--recovery-output", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsMissingPackageOutputForExport()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        File.WriteAllText(recovery, "Recovery code: placeholder");

        var exception = Assert.Throws<UpdaterUsageException>(
            () => PortableLogbookCommandOptions.Parse(
                ["export", "--workbook", workbook, "--recovery-code-file", recovery]));

        Assert.Contains("--output", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsMultiplePortableKeySources()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        File.WriteAllText(recovery, "Recovery code: placeholder");

        var exception = Assert.Throws<UpdaterUsageException>(
            () => PortableLogbookCommandOptions.Parse(
                [
                    "export",
                    "--workbook",
                    workbook,
                    "--recovery-code-file",
                    recovery,
                    "--windows-credential-target",
                    "ElectronicLogbook.Tests/target",
                    "--output",
                    Path.Combine(directory, "export.elogbook")
                ]));

        Assert.Contains("Use only one portable key source", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsMissingPackageInputForImportPreview()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        File.WriteAllText(recovery, "Recovery code: placeholder");

        var exception = Assert.Throws<UpdaterUsageException>(
            () => PortableLogbookCommandOptions.Parse(
                ["import-preview", "--workbook", workbook, "--recovery-code-file", recovery]));

        Assert.Contains("--package", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsMissingPackageInputForImportApply()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        File.WriteAllText(recovery, "Recovery code: placeholder");

        var exception = Assert.Throws<UpdaterUsageException>(
            () => PortableLogbookCommandOptions.Parse(
                ["import-apply", "--workbook", workbook, "--recovery-code-file", recovery]));

        Assert.Contains("--package", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadStatusReportsWorkbookWithoutPortableStorageAsDisabled()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);

        var status = PortableLogbookCommandRunner.ReadStatus(workbook);

        Assert.False(status.IsEnabled);
        Assert.Equal(Path.GetFullPath(workbook), status.WorkbookPath);
        Assert.Null(status.Summary);
        Assert.Equal(0, status.ImportReceiptCount);
    }

    [Fact]
    public void EnableWritesPortableWorkbookStorageAndUsableRecoveryFile()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");

        var result = PortableLogbookCommandRunner.Enable(
            workbook,
            recovery,
            DateTimeOffset.Parse("2026-07-19T00:00:00Z"));

        Assert.Equal(Path.GetFullPath(workbook), result.WorkbookPath);
        Assert.Equal(Path.GetFullPath(recovery), result.RecoveryOutputPath);
        Assert.True(File.Exists(result.BackupPath));
        Assert.Null(PortableLogbookWorkbookPackageStorage.ReadEnvelope(result.BackupPath));
        Assert.Equal(0, result.InitialOperationCount);
        Assert.True(File.Exists(recovery));
        var recoveryText = File.ReadAllText(recovery);
        Assert.Contains("Recovery code:", recoveryText, StringComparison.Ordinal);
        var recoveryCode = recoveryText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Single(line => line.StartsWith("Recovery code:", StringComparison.Ordinal))
            .Split(':', 2)[1]
            .Trim();
        var key = PortableLogbookKey.FromRecoveryCode(recoveryCode);

        var state = PortableLogbookWorkbookPackageStorage.OpenState(workbook, key);

        Assert.NotNull(state);
        Assert.Equal(result.LogbookId, state.Document.LogbookId);
        Assert.Empty(state.Document.Operations);
    }

    [Fact]
    public void EnableRejectsWorkbookThatAlreadyHasPortableStorage()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        PortableLogbookWorkbookPackageStorage.WriteEnvelope(
            workbook,
            CreateEnvelope("log_cli", PortableLogbookKey.Generate()));

        var exception = Assert.Throws<UpdaterUsageException>(
            () => PortableLogbookCommandRunner.Enable(
                workbook,
                recovery,
                DateTimeOffset.Parse("2026-07-19T00:00:00Z")));

        Assert.Contains("already enabled", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(recovery));
    }

    [Fact]
    public void EnableCanSaveGeneratedKeyToWindowsCredentialManager()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        PortableLogbookEnableResult? result = null;
        try
        {
            result = PortableLogbookCommandRunner.Enable(
                workbook,
                recovery,
                DateTimeOffset.Parse("2026-07-19T00:00:00Z"),
                saveWindowsCredential: true);

            Assert.NotNull(result.WindowsCredentialTargetName);
            Assert.Contains(
                $"Windows credential target: {result.WindowsCredentialTargetName}",
                File.ReadAllText(recovery),
                StringComparison.Ordinal);
            var recoveryKey = PortableLogbookKey.FromRecoveryCode(ReadRecoveryCodeFromGeneratedFile(recovery));
            var storedKey = PortableLogbookWindowsCredentialStore.LoadKey(result.WindowsCredentialTargetName);

            Assert.Equal(recoveryKey, storedKey);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(result?.WindowsCredentialTargetName))
            {
                PortableLogbookWindowsCredentialStore.DeleteKey(result.WindowsCredentialTargetName);
            }
        }
    }

    [Fact]
    public void ExportWritesDecryptablePackageFromWorkbookStorage()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        var package = Path.Combine(directory, "export.elogbook");
        var enabled = PortableLogbookCommandRunner.Enable(
            workbook,
            recovery,
            DateTimeOffset.Parse("2026-07-19T00:00:00Z"));

        var export = PortableLogbookCommandRunner.Export(
            workbook,
            recovery,
            package,
            DateTimeOffset.Parse("2026-07-19T01:00:00Z"));

        Assert.Equal(Path.GetFullPath(workbook), export.WorkbookPath);
        Assert.Equal(Path.GetFullPath(package), export.PackageOutputPath);
        Assert.Equal(enabled.LogbookId, export.LogbookId);
        Assert.Equal(0, export.OperationCount);
        Assert.True(File.Exists(package));
        var key = PortableLogbookKey.FromRecoveryCode(ReadRecoveryCodeFromGeneratedFile(recovery));
        var read = PortableLogbookPackageFile.Read(package, key, enabled.LogbookId);
        Assert.Equal(enabled.LogbookId, read.Document.LogbookId);
        Assert.Empty(read.Document.Operations);
    }

    [Fact]
    public void ExportCanUseWindowsCredentialTarget()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        var package = Path.Combine(directory, "export.elogbook");
        PortableLogbookEnableResult? enabled = null;
        try
        {
            enabled = PortableLogbookCommandRunner.Enable(
                workbook,
                recovery,
                DateTimeOffset.Parse("2026-07-19T00:00:00Z"),
                saveWindowsCredential: true);

            var export = PortableLogbookCommandRunner.Export(
                workbook,
                PortableLogbookCommandKeySource.WindowsCredential(enabled.WindowsCredentialTargetName!),
                package,
                DateTimeOffset.Parse("2026-07-19T01:00:00Z"));

            Assert.Equal(enabled.LogbookId, export.LogbookId);
            Assert.True(File.Exists(package));
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(enabled?.WindowsCredentialTargetName))
            {
                PortableLogbookWindowsCredentialStore.DeleteKey(enabled.WindowsCredentialTargetName);
            }
        }
    }

    [Fact]
    public void ExportRejectsWorkbookWithoutPortableStorage()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        var package = Path.Combine(directory, "export.elogbook");
        File.WriteAllText(recovery, PortableLogbookKey.Generate().ToRecoveryCode());

        var exception = Assert.Throws<UpdaterUsageException>(
            () => PortableLogbookCommandRunner.Export(
                workbook,
                recovery,
                package,
                DateTimeOffset.Parse("2026-07-19T01:00:00Z")));

        Assert.Contains("not enabled", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(package));
    }

    [Fact]
    public void PreviewImportReportsPlanWithoutChangingWorkbookStorage()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        var incomingPackage = Path.Combine(directory, "incoming.elogbook");
        var enabled = PortableLogbookCommandRunner.Enable(
            workbook,
            recovery,
            DateTimeOffset.Parse("2026-07-19T00:00:00Z"));
        var key = PortableLogbookKey.FromRecoveryCode(ReadRecoveryCodeFromGeneratedFile(recovery));
        var incomingCreate = new CreateEntryOperation(
            enabled.LogbookId,
            new EntryId("ent_incoming"),
            new RevisionId("rev_incoming"),
            enabled.DeviceId,
            DateTimeOffset.Parse("2026-07-19T00:05:00Z"),
            Entry("VH-IMP"));
        var incomingDocument = PortableLogbookDocument.CreateAustraliaFirst(
            enabled.LogbookId,
            [],
            [incomingCreate]);
        PortableLogbookPackageFile.Write(incomingPackage, incomingDocument, key);

        var preview = PortableLogbookCommandRunner.PreviewImport(
            workbook,
            recovery,
            incomingPackage);
        var stateAfterPreview = PortableLogbookWorkbookPackageStorage.OpenState(workbook, key);

        Assert.Equal(enabled.LogbookId, preview.LogbookId);
        Assert.Equal("readyToApply", preview.Status);
        Assert.Equal(0, preview.LocalOperationCount);
        Assert.Equal(1, preview.IncomingOperationCount);
        Assert.Equal(1, preview.NewOperationCount);
        Assert.Equal(1, preview.CreateCount);
        Assert.Equal(0, preview.CorrectionCount);
        Assert.Equal(0, preview.DeletionCount);
        var summary = Assert.Single(preview.NewOperationSummaries);
        Assert.Equal(incomingCreate.EntryId, summary.EntryId);
        Assert.Equal("VH-IMP", summary.Registration);
        Assert.Empty(preview.DuplicateOperationSummaries);
        Assert.NotNull(stateAfterPreview);
        Assert.Empty(stateAfterPreview.Document.Operations);
    }

    [Fact]
    public void PreviewImportCanUseWindowsCredentialTarget()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        var incomingPackage = Path.Combine(directory, "incoming.elogbook");
        PortableLogbookEnableResult? enabled = null;
        try
        {
            enabled = PortableLogbookCommandRunner.Enable(
                workbook,
                recovery,
                DateTimeOffset.Parse("2026-07-19T00:00:00Z"),
                saveWindowsCredential: true);
            var key = PortableLogbookKey.FromRecoveryCode(ReadRecoveryCodeFromGeneratedFile(recovery));
            PortableLogbookPackageFile.Write(
                incomingPackage,
                PortableLogbookDocument.CreateAustraliaFirst(
                    enabled.LogbookId,
                    [],
                    [new CreateEntryOperation(
                        enabled.LogbookId,
                        new EntryId("ent_incoming"),
                        new RevisionId("rev_incoming"),
                        enabled.DeviceId,
                        DateTimeOffset.Parse("2026-07-19T00:05:00Z"),
                        Entry("VH-IMP"))]),
                key);

            var preview = PortableLogbookCommandRunner.PreviewImport(
                workbook,
                PortableLogbookCommandKeySource.WindowsCredential(enabled.WindowsCredentialTargetName!),
                incomingPackage);

            Assert.Equal("readyToApply", preview.Status);
            Assert.Equal(1, preview.NewOperationCount);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(enabled?.WindowsCredentialTargetName))
            {
                PortableLogbookWindowsCredentialStore.DeleteKey(enabled.WindowsCredentialTargetName);
            }
        }
    }

    [Fact]
    public void ApplyImportUpdatesPortableStorageAfterCreatingBackup()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        var incomingPackage = Path.Combine(directory, "incoming.elogbook");
        var enabled = PortableLogbookCommandRunner.Enable(
            workbook,
            recovery,
            DateTimeOffset.Parse("2026-07-19T00:00:00Z"));
        var key = PortableLogbookKey.FromRecoveryCode(ReadRecoveryCodeFromGeneratedFile(recovery));
        var incomingCreate = new CreateEntryOperation(
            enabled.LogbookId,
            new EntryId("ent_incoming"),
            new RevisionId("rev_incoming"),
            enabled.DeviceId,
            DateTimeOffset.Parse("2026-07-19T00:05:00Z"),
            Entry("VH-IMP"));
        PortableLogbookPackageFile.Write(
            incomingPackage,
            PortableLogbookDocument.CreateAustraliaFirst(enabled.LogbookId, [], [incomingCreate]),
            key);

        var applied = PortableLogbookCommandRunner.ApplyImport(
            workbook,
            recovery,
            incomingPackage,
            DateTimeOffset.Parse("2026-07-19T00:10:00Z"));
        var stateAfterApply = PortableLogbookWorkbookPackageStorage.OpenState(workbook, key);

        Assert.Equal("applied", applied.Status);
        Assert.Equal(1, applied.NewOperationCount);
        Assert.Equal(1, applied.WorkbookRowCount);
        Assert.Equal("VH-IMP", Assert.Single(applied.NewOperationSummaries).Registration);
        Assert.Empty(applied.DuplicateOperationSummaries);
        Assert.True(applied.ReceiptRecorded);
        Assert.True(applied.StorageUpdated);
        Assert.NotNull(applied.BackupPath);
        Assert.True(File.Exists(applied.BackupPath));
        Assert.NotNull(stateAfterApply);
        Assert.Equal([incomingCreate.RevisionId], stateAfterApply.Document.Operations.Select(operation => operation.RevisionId));
        var backupState = PortableLogbookWorkbookPackageStorage.OpenState(applied.BackupPath!, key);
        Assert.NotNull(backupState);
        Assert.Empty(backupState.Document.Operations);
    }

    [Fact]
    public void ApplyImportCanUseWindowsCredentialTarget()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        var incomingPackage = Path.Combine(directory, "incoming.elogbook");
        PortableLogbookEnableResult? enabled = null;
        try
        {
            enabled = PortableLogbookCommandRunner.Enable(
                workbook,
                recovery,
                DateTimeOffset.Parse("2026-07-19T00:00:00Z"),
                saveWindowsCredential: true);
            var key = PortableLogbookKey.FromRecoveryCode(ReadRecoveryCodeFromGeneratedFile(recovery));
            PortableLogbookPackageFile.Write(
                incomingPackage,
                PortableLogbookDocument.CreateAustraliaFirst(
                    enabled.LogbookId,
                    [],
                    [new CreateEntryOperation(
                        enabled.LogbookId,
                        new EntryId("ent_incoming"),
                        new RevisionId("rev_incoming"),
                        enabled.DeviceId,
                        DateTimeOffset.Parse("2026-07-19T00:05:00Z"),
                        Entry("VH-IMP"))]),
                key);

            var result = PortableLogbookCommandRunner.ApplyImport(
                workbook,
                PortableLogbookCommandKeySource.WindowsCredential(enabled.WindowsCredentialTargetName!),
                incomingPackage,
                DateTimeOffset.Parse("2026-07-19T00:10:00Z"));

            Assert.Equal("applied", result.Status);
            Assert.True(result.StorageUpdated);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(enabled?.WindowsCredentialTargetName))
            {
                PortableLogbookWindowsCredentialStore.DeleteKey(enabled.WindowsCredentialTargetName);
            }
        }
    }

    [Fact]
    public void ApplyImportRecordsDuplicateOnlyPackageOnceThenReplaysWithoutWriting()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        var incomingPackage = Path.Combine(directory, "duplicate.elogbook");
        var enabled = PortableLogbookCommandRunner.Enable(
            workbook,
            recovery,
            DateTimeOffset.Parse("2026-07-19T00:00:00Z"));
        var key = PortableLogbookKey.FromRecoveryCode(ReadRecoveryCodeFromGeneratedFile(recovery));
        PortableLogbookPackageFile.Write(
            incomingPackage,
            PortableLogbookDocument.CreateAustraliaFirst(enabled.LogbookId, [], []),
            key);

        var first = PortableLogbookCommandRunner.ApplyImport(
            workbook,
            recovery,
            incomingPackage,
            DateTimeOffset.Parse("2026-07-19T00:10:00Z"));
        var second = PortableLogbookCommandRunner.ApplyImport(
            workbook,
            recovery,
            incomingPackage,
            DateTimeOffset.Parse("2026-07-19T00:20:00Z"));

        Assert.Equal("duplicateOperationsRecorded", first.Status);
        Assert.True(first.StorageUpdated);
        Assert.True(first.ReceiptRecorded);
        Assert.NotNull(first.BackupPath);
        Assert.Equal("packageReplay", second.Status);
        Assert.False(second.StorageUpdated);
        Assert.False(second.ReceiptRecorded);
        Assert.Null(second.BackupPath);
        var state = PortableLogbookWorkbookPackageStorage.OpenState(workbook, key);
        Assert.NotNull(state);
        Assert.Single(state.ImportReceipts);
    }

    [Fact]
    public void ApplyImportDoesNotWriteStorageWhenResolutionIsRequired()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        var incomingPackage = Path.Combine(directory, "incoming.elogbook");
        var key = PortableLogbookKey.Generate();
        var create = new CreateEntryOperation(
            new LogbookId("log_conflict"),
            new EntryId("ent_conflict"),
            new RevisionId("rev_create"),
            new DeviceId("dev_excel"),
            DateTimeOffset.Parse("2026-07-19T00:00:00Z"),
            Entry("VH-BASE"));
        var localCorrection = new CorrectEntryOperation(
            create.LogbookId,
            create.EntryId,
            new RevisionId("rev_local"),
            new HashSet<RevisionId> { create.RevisionId },
            create.DeviceId,
            create.CreatedAt.AddMinutes(1),
            Entry("VH-LOCAL"));
        var incomingCorrection = new CorrectEntryOperation(
            create.LogbookId,
            create.EntryId,
            new RevisionId("rev_incoming"),
            new HashSet<RevisionId> { create.RevisionId },
            create.DeviceId,
            create.CreatedAt.AddMinutes(2),
            Entry("VH-INCOMING"));
        var localDocument = PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [create, localCorrection]);
        var envelope = PortableLogbookWorkbookStorage.CreateEnvelope(
            localDocument,
            PortableLogbookPackage.Write(localDocument, key),
            []);
        PortableLogbookWorkbookPackageStorage.WriteEnvelope(workbook, envelope);
        File.WriteAllText(recovery, key.ToRecoveryCode());
        PortableLogbookPackageFile.Write(
            incomingPackage,
            PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [create, incomingCorrection]),
            key);

        var result = PortableLogbookCommandRunner.ApplyImport(
            workbook,
            recovery,
            incomingPackage,
            DateTimeOffset.Parse("2026-07-19T00:10:00Z"));
        var stateAfterApplyAttempt = PortableLogbookWorkbookPackageStorage.OpenState(workbook, key);

        Assert.Equal("requiresResolution", result.Status);
        Assert.False(result.StorageUpdated);
        Assert.False(result.ReceiptRecorded);
        Assert.Null(result.BackupPath);
        Assert.Equal(1, result.ConflictCount);
        Assert.NotNull(stateAfterApplyAttempt);
        Assert.Equal([create.RevisionId, localCorrection.RevisionId], stateAfterApplyAttempt.Document.Operations.Select(operation => operation.RevisionId));
    }

    [Fact]
    public void ReadStatusReportsRedactedPortableStorageSummary()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var key = PortableLogbookKey.Generate();
        var envelope = CreateEnvelope("log_cli", key);
        PortableLogbookWorkbookPackageStorage.WriteEnvelope(workbook, envelope);

        var status = PortableLogbookCommandRunner.ReadStatus(workbook);

        Assert.True(status.IsEnabled);
        Assert.Equal(new LogbookId("log_cli"), status.LogbookId);
        Assert.Equal(PortableLogbookDocument.CurrentSchemaVersion, status.SchemaVersion);
        Assert.Equal(PortableLogbookWorkbookStorage.CurrentStorageVersion, status.StorageVersion);
        Assert.NotNull(status.Summary);
        Assert.Equal(1, status.Summary.OperationCount);
        Assert.Equal(1, status.Summary.CurrentRecordCount);
        Assert.Equal(0, status.ImportReceiptCount);
    }

    [Fact]
    public async Task RunAsyncDispatchesPortableStatusJsonWithoutMigrationArguments()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var key = PortableLogbookKey.Generate();
        PortableLogbookWorkbookPackageStorage.WriteEnvelope(workbook, CreateEnvelope("log_cli", key));
        using var output = new StringWriter();
        var originalOutput = Console.Out;
        Console.SetOut(output);
        try
        {
            var exitCode = await UpdaterProgram.RunAsync(["portable", "status", "--workbook", workbook, "--json"]);

            Assert.Equal(0, exitCode);
            using var json = JsonDocument.Parse(output.ToString());
            Assert.True(json.RootElement.GetProperty("isEnabled").GetBoolean());
            Assert.Equal("log_cli", json.RootElement.GetProperty("logbookId").GetProperty("value").GetString());
            Assert.Equal(1, json.RootElement.GetProperty("summary").GetProperty("operationCount").GetInt32());
        }
        finally
        {
            Console.SetOut(originalOutput);
        }
    }

    [Fact]
    public async Task RunAsyncDispatchesPortableEnableJsonWithoutPrintingRecoveryCode()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        using var output = new StringWriter();
        var originalOutput = Console.Out;
        Console.SetOut(output);
        try
        {
            var exitCode = await UpdaterProgram.RunAsync(
                ["portable", "enable", "--workbook", workbook, "--recovery-output", recovery, "--json"]);

            Assert.Equal(0, exitCode);
            using var json = JsonDocument.Parse(output.ToString());
            Assert.Equal(Path.GetFullPath(workbook), json.RootElement.GetProperty("workbookPath").GetString());
            Assert.Equal(Path.GetFullPath(recovery), json.RootElement.GetProperty("recoveryOutputPath").GetString());
            Assert.True(File.Exists(json.RootElement.GetProperty("backupPath").GetString()));
            Assert.False(json.RootElement.TryGetProperty("recoveryCode", out _));
            Assert.True(File.Exists(recovery));
            Assert.DoesNotContain(ReadRecoveryCode(File.ReadAllText(recovery)), output.ToString(), StringComparison.Ordinal);
            Assert.NotNull(PortableLogbookWorkbookPackageStorage.ReadEnvelope(workbook));
        }
        finally
        {
            Console.SetOut(originalOutput);
        }
    }

    [Fact]
    public async Task RunAsyncDispatchesPortableEnableCredentialSaveJsonWithoutPrintingRecoveryCode()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        string? targetName = null;
        using var output = new StringWriter();
        var originalOutput = Console.Out;
        Console.SetOut(output);
        try
        {
            var exitCode = await UpdaterProgram.RunAsync(
                ["portable", "enable", "--workbook", workbook, "--recovery-output", recovery, "--save-windows-credential", "--json"]);

            Assert.Equal(0, exitCode);
            using var json = JsonDocument.Parse(output.ToString());
            targetName = json.RootElement.GetProperty("windowsCredentialTargetName").GetString();
            Assert.False(string.IsNullOrWhiteSpace(targetName));
            Assert.NotNull(PortableLogbookWindowsCredentialStore.LoadKey(targetName));
            Assert.Contains(
                $"Windows credential target: {targetName}",
                File.ReadAllText(recovery),
                StringComparison.Ordinal);
            Assert.False(json.RootElement.TryGetProperty("recoveryCode", out _));
            Assert.DoesNotContain(ReadRecoveryCode(File.ReadAllText(recovery)), output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOutput);
            if (!string.IsNullOrWhiteSpace(targetName))
            {
                PortableLogbookWindowsCredentialStore.DeleteKey(targetName);
            }
        }
    }

    [Fact]
    public async Task RunAsyncDispatchesPortableExportJsonWithoutPrintingRecoveryCode()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        var package = Path.Combine(directory, "export.elogbook");
        PortableLogbookCommandRunner.Enable(
            workbook,
            recovery,
            DateTimeOffset.Parse("2026-07-19T00:00:00Z"));
        using var output = new StringWriter();
        var originalOutput = Console.Out;
        Console.SetOut(output);
        try
        {
            var exitCode = await UpdaterProgram.RunAsync(
                ["portable", "export", "--workbook", workbook, "--recovery-code-file", recovery, "--output", package, "--json"]);

            Assert.Equal(0, exitCode);
            using var json = JsonDocument.Parse(output.ToString());
            Assert.Equal(Path.GetFullPath(workbook), json.RootElement.GetProperty("workbookPath").GetString());
            Assert.Equal(Path.GetFullPath(package), json.RootElement.GetProperty("packageOutputPath").GetString());
            Assert.False(json.RootElement.TryGetProperty("recoveryCode", out _));
            Assert.True(File.Exists(package));
        }
        finally
        {
            Console.SetOut(originalOutput);
        }
    }

    [Fact]
    public async Task RunAsyncDispatchesPortableExportWithWindowsCredentialTarget()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        var package = Path.Combine(directory, "export.elogbook");
        PortableLogbookEnableResult? enabled = null;
        using var output = new StringWriter();
        var originalOutput = Console.Out;
        Console.SetOut(output);
        try
        {
            enabled = PortableLogbookCommandRunner.Enable(
                workbook,
                recovery,
                DateTimeOffset.Parse("2026-07-19T00:00:00Z"),
                saveWindowsCredential: true);
            var exitCode = await UpdaterProgram.RunAsync(
                ["portable", "export", "--workbook", workbook, "--windows-credential-target", enabled.WindowsCredentialTargetName!, "--output", package, "--json"]);

            Assert.Equal(0, exitCode);
            using var json = JsonDocument.Parse(output.ToString());
            Assert.Equal(Path.GetFullPath(package), json.RootElement.GetProperty("packageOutputPath").GetString());
            Assert.True(File.Exists(package));
        }
        finally
        {
            Console.SetOut(originalOutput);
            if (!string.IsNullOrWhiteSpace(enabled?.WindowsCredentialTargetName))
            {
                PortableLogbookWindowsCredentialStore.DeleteKey(enabled.WindowsCredentialTargetName);
            }
        }
    }

    [Fact]
    public async Task RunAsyncDispatchesPortableImportPreviewJsonWithoutPrintingRecoveryCode()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        var incomingPackage = Path.Combine(directory, "incoming.elogbook");
        var enabled = PortableLogbookCommandRunner.Enable(
            workbook,
            recovery,
            DateTimeOffset.Parse("2026-07-19T00:00:00Z"));
        var key = PortableLogbookKey.FromRecoveryCode(ReadRecoveryCodeFromGeneratedFile(recovery));
        PortableLogbookPackageFile.Write(
            incomingPackage,
            PortableLogbookDocument.CreateAustraliaFirst(
                enabled.LogbookId,
                [],
                [new CreateEntryOperation(
                    enabled.LogbookId,
                    new EntryId("ent_incoming"),
                    new RevisionId("rev_incoming"),
                    enabled.DeviceId,
                    DateTimeOffset.Parse("2026-07-19T00:05:00Z"),
                    Entry("VH-IMP"))]),
            key);
        using var output = new StringWriter();
        var originalOutput = Console.Out;
        Console.SetOut(output);
        try
        {
            var exitCode = await UpdaterProgram.RunAsync(
                ["portable", "import-preview", "--workbook", workbook, "--recovery-code-file", recovery, "--package", incomingPackage, "--json"]);

            Assert.Equal(0, exitCode);
            using var json = JsonDocument.Parse(output.ToString());
            Assert.Equal("readyToApply", json.RootElement.GetProperty("status").GetString());
            Assert.Equal(1, json.RootElement.GetProperty("newOperationCount").GetInt32());
            var summary = json.RootElement.GetProperty("newOperationSummaries")[0];
            Assert.Equal("VH-IMP", summary.GetProperty("registration").GetString());
            Assert.Equal("create", summary.GetProperty("kind").GetString());
            Assert.False(json.RootElement.TryGetProperty("recoveryCode", out _));
        }
        finally
        {
            Console.SetOut(originalOutput);
        }
    }

    [Fact]
    public async Task RunAsyncDispatchesPortableImportPreviewWithWindowsCredentialTarget()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        var incomingPackage = Path.Combine(directory, "incoming.elogbook");
        PortableLogbookEnableResult? enabled = null;
        using var output = new StringWriter();
        var originalOutput = Console.Out;
        Console.SetOut(output);
        try
        {
            enabled = PortableLogbookCommandRunner.Enable(
                workbook,
                recovery,
                DateTimeOffset.Parse("2026-07-19T00:00:00Z"),
                saveWindowsCredential: true);
            var key = PortableLogbookKey.FromRecoveryCode(ReadRecoveryCodeFromGeneratedFile(recovery));
            PortableLogbookPackageFile.Write(
                incomingPackage,
                PortableLogbookDocument.CreateAustraliaFirst(
                    enabled.LogbookId,
                    [],
                    [new CreateEntryOperation(
                        enabled.LogbookId,
                        new EntryId("ent_incoming"),
                        new RevisionId("rev_incoming"),
                        enabled.DeviceId,
                        DateTimeOffset.Parse("2026-07-19T00:05:00Z"),
                        Entry("VH-IMP"))]),
                key);
            var exitCode = await UpdaterProgram.RunAsync(
                ["portable", "import-preview", "--workbook", workbook, "--windows-credential-target", enabled.WindowsCredentialTargetName!, "--package", incomingPackage, "--json"]);

            Assert.Equal(0, exitCode);
            using var json = JsonDocument.Parse(output.ToString());
            Assert.Equal("readyToApply", json.RootElement.GetProperty("status").GetString());
            Assert.Equal(1, json.RootElement.GetProperty("newOperationCount").GetInt32());
        }
        finally
        {
            Console.SetOut(originalOutput);
            if (!string.IsNullOrWhiteSpace(enabled?.WindowsCredentialTargetName))
            {
                PortableLogbookWindowsCredentialStore.DeleteKey(enabled.WindowsCredentialTargetName);
            }
        }
    }

    [Fact]
    public async Task RunAsyncDispatchesPortableImportApplyJsonWithoutPrintingRecoveryCode()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        var incomingPackage = Path.Combine(directory, "incoming.elogbook");
        var enabled = PortableLogbookCommandRunner.Enable(
            workbook,
            recovery,
            DateTimeOffset.Parse("2026-07-19T00:00:00Z"));
        var key = PortableLogbookKey.FromRecoveryCode(ReadRecoveryCodeFromGeneratedFile(recovery));
        PortableLogbookPackageFile.Write(
            incomingPackage,
            PortableLogbookDocument.CreateAustraliaFirst(
                enabled.LogbookId,
                [],
                [new CreateEntryOperation(
                    enabled.LogbookId,
                    new EntryId("ent_incoming"),
                    new RevisionId("rev_incoming"),
                    enabled.DeviceId,
                    DateTimeOffset.Parse("2026-07-19T00:05:00Z"),
                    Entry("VH-IMP"))]),
            key);
        using var output = new StringWriter();
        var originalOutput = Console.Out;
        Console.SetOut(output);
        try
        {
            var exitCode = await UpdaterProgram.RunAsync(
                ["portable", "import-apply", "--workbook", workbook, "--recovery-code-file", recovery, "--package", incomingPackage, "--json"]);

            Assert.Equal(0, exitCode);
            using var json = JsonDocument.Parse(output.ToString());
            Assert.Equal("applied", json.RootElement.GetProperty("status").GetString());
            Assert.True(json.RootElement.GetProperty("storageUpdated").GetBoolean());
            var summary = json.RootElement.GetProperty("newOperationSummaries")[0];
            Assert.Equal("VH-IMP", summary.GetProperty("registration").GetString());
            Assert.True(File.Exists(json.RootElement.GetProperty("backupPath").GetString()));
            Assert.False(json.RootElement.TryGetProperty("recoveryCode", out _));
        }
        finally
        {
            Console.SetOut(originalOutput);
        }
    }

    [Fact]
    public async Task RunAsyncDispatchesPortableImportApplyWithWindowsCredentialTarget()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        var incomingPackage = Path.Combine(directory, "incoming.elogbook");
        PortableLogbookEnableResult? enabled = null;
        using var output = new StringWriter();
        var originalOutput = Console.Out;
        Console.SetOut(output);
        try
        {
            enabled = PortableLogbookCommandRunner.Enable(
                workbook,
                recovery,
                DateTimeOffset.Parse("2026-07-19T00:00:00Z"),
                saveWindowsCredential: true);
            var key = PortableLogbookKey.FromRecoveryCode(ReadRecoveryCodeFromGeneratedFile(recovery));
            PortableLogbookPackageFile.Write(
                incomingPackage,
                PortableLogbookDocument.CreateAustraliaFirst(
                    enabled.LogbookId,
                    [],
                    [new CreateEntryOperation(
                        enabled.LogbookId,
                        new EntryId("ent_incoming"),
                        new RevisionId("rev_incoming"),
                        enabled.DeviceId,
                        DateTimeOffset.Parse("2026-07-19T00:05:00Z"),
                        Entry("VH-IMP"))]),
                key);
            var exitCode = await UpdaterProgram.RunAsync(
                ["portable", "import-apply", "--workbook", workbook, "--windows-credential-target", enabled.WindowsCredentialTargetName!, "--package", incomingPackage, "--json"]);

            Assert.Equal(0, exitCode);
            using var json = JsonDocument.Parse(output.ToString());
            Assert.Equal("applied", json.RootElement.GetProperty("status").GetString());
            Assert.True(json.RootElement.GetProperty("storageUpdated").GetBoolean());
        }
        finally
        {
            Console.SetOut(originalOutput);
            if (!string.IsNullOrWhiteSpace(enabled?.WindowsCredentialTargetName))
            {
                PortableLogbookWindowsCredentialStore.DeleteKey(enabled.WindowsCredentialTargetName);
            }
        }
    }

    [Fact]
    public async Task RunAsyncReturnsUsageCodeForInvalidPortableArguments()
    {
        using var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);
        try
        {
            var exitCode = await UpdaterProgram.RunAsync(["portable", "status"]);

            Assert.Equal(2, exitCode);
            Assert.Contains("--workbook", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string ReadRecoveryCode(string recoveryText)
    {
        const string prefix = "Recovery code:";
        var line = recoveryText
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Single(value => value.StartsWith(prefix, StringComparison.Ordinal));
        return line[prefix.Length..].Trim();
    }

    private static PortableLogbookWorkbookStorageEnvelope CreateEnvelope(string logbookId, PortableLogbookKey key)
    {
        var create = new CreateEntryOperation(
            new LogbookId(logbookId),
            new EntryId("ent_1"),
            new RevisionId("rev_1"),
            new DeviceId("dev_excel"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            PortableLogbookEntry.Empty with
            {
                Date = new DateOnly(2026, 7, 18),
                AircraftType = "C172",
                Registration = "VH-ABC",
                From = "YSBK",
                To = "YSBK",
                PilotInCommand = 1.2m
            });
        var document = PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [create]);
        return PortableLogbookWorkbookStorage.CreateEnvelope(
            document,
            PortableLogbookPackage.Write(document, key),
            []);
    }

    private static PortableLogbookEntry Entry(string registration) =>
        PortableLogbookEntry.Empty with
        {
            Date = new DateOnly(2026, 7, 19),
            AircraftType = "C172",
            Registration = registration,
            From = "YSBK",
            To = "YSBK",
            PilotInCommand = 1.0m
        };

    private static string ReadRecoveryCodeFromGeneratedFile(string recoveryPath) =>
        File.ReadAllLines(recoveryPath)
            .Select(line => line.Trim())
            .Single(line => line.StartsWith("Recovery code:", StringComparison.Ordinal))
            .Split(':', 2)[1]
            .Trim();
}
