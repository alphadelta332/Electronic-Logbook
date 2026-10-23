using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
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
    public void ParseAcceptsHiddenHostedPairCommand()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);

        var options = PortableLogbookCommandOptions.Parse(
            [
                "hosted-pair",
                "--workbook",
                workbook,
                "--hosted-account-id",
                "acct_123",
                "--hosted-access-token",
                "access-token",
                "--hosted-refresh-token",
                "refresh-token",
                "--hosted-access-token-expires-at",
                "2026-08-06T12:00:00Z",
                "--json"
            ]);

        Assert.Equal(PortableLogbookCommand.HostedPair, options.Command);
        Assert.Equal(Path.GetFullPath(workbook), options.WorkbookPath);
        Assert.Equal("acct_123", options.HostedAccountId);
        Assert.Equal("access-token", options.HostedAccessToken);
        Assert.Equal("refresh-token", options.HostedRefreshToken);
        Assert.Equal(DateTimeOffset.Parse("2026-08-06T12:00:00Z"), options.HostedAccessTokenExpiresAt);
        Assert.True(options.Json);
    }

    [Fact]
    public void ParseAcceptsHiddenHostedSyncCommand()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);

        var options = PortableLogbookCommandOptions.Parse(
            ["hosted-sync", "--workbook", workbook, "--wait-for-workbook-unlock-seconds", "120", "--json"]);

        Assert.Equal(PortableLogbookCommand.HostedSync, options.Command);
        Assert.Equal(Path.GetFullPath(workbook), options.WorkbookPath);
        Assert.Equal(120, options.WaitForWorkbookUnlockSeconds);
        Assert.True(options.Json);
    }

    [Fact]
    public async Task HostedSyncWaitsForWorkbookExclusiveAccess()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var lockStream = new FileStream(workbook, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var waitTask = Task.Run(() => PortableLogbookCommandRunner.WaitForWorkbookUnlock(
            workbook,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromMilliseconds(20)));

        await Task.Delay(100);
        Assert.False(waitTask.IsCompleted);
        await lockStream.DisposeAsync();
        await waitTask;
    }

    [Fact]
    public void ParseRejectsHostedPairWithoutTokens()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);

        var exception = Assert.Throws<UpdaterUsageException>(
            () => PortableLogbookCommandOptions.Parse(
                ["hosted-pair", "--workbook", workbook, "--hosted-account-id", "acct_123"]));

        Assert.Contains("--hosted-access-token", exception.Message, StringComparison.Ordinal);
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
    public void ParseAcceptsPortablePrintedCopyWorkbookRecoveryFileAndOutputPath()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        File.WriteAllText(recovery, "Recovery code: placeholder");
        var output = Path.Combine(directory, "printed-copy.html");

        var options = PortableLogbookCommandOptions.Parse(
            [
                "printed-copy",
                "--workbook",
                workbook,
                "--recovery-code-file",
                recovery,
                "--output",
                output,
                "--holder-name",
                "Alex Pilot",
                "--holder-date-of-birth",
                "1990-01-02",
                "--certified-on",
                "2026-07-19",
                "--records-per-page",
                "12",
                "--json"
            ]);

        Assert.Equal(PortableLogbookCommand.PrintedCopy, options.Command);
        Assert.Equal(Path.GetFullPath(workbook), options.WorkbookPath);
        Assert.Equal(Path.GetFullPath(recovery), options.RecoveryCodeFilePath);
        Assert.Equal(Path.GetFullPath(output), options.PrintedCopyOutputPath);
        Assert.Equal("Alex Pilot", options.HolderName);
        Assert.Equal(new DateOnly(1990, 1, 2), options.HolderDateOfBirth);
        Assert.Equal(new DateOnly(2026, 7, 19), options.CertifiedOn);
        Assert.Equal(12, options.RecordsPerPage);
        Assert.True(options.Json);
    }

    [Fact]
    public void ParseAcceptsPortableRevisionHistoryWorkbookRecoveryFileAndEntryId()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        File.WriteAllText(recovery, "Recovery code: placeholder");

        var options = PortableLogbookCommandOptions.Parse(
            [
                "revision-history",
                "--workbook",
                workbook,
                "--recovery-code-file",
                recovery,
                "--entry-id",
                "ent_1",
                "--json"
            ]);

        Assert.Equal(PortableLogbookCommand.RevisionHistory, options.Command);
        Assert.Equal(Path.GetFullPath(workbook), options.WorkbookPath);
        Assert.Equal(Path.GetFullPath(recovery), options.RecoveryCodeFilePath);
        Assert.Equal("ent_1", options.EntryId);
        Assert.True(options.Json);
    }

    [Fact]
    public void ParseAcceptsPortableResolveConflictWorkbookRecoveryFileEntryAndRevisionId()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        File.WriteAllText(recovery, "Recovery code: placeholder");

        var options = PortableLogbookCommandOptions.Parse(
            [
                "resolve-conflict",
                "--workbook",
                workbook,
                "--recovery-code-file",
                recovery,
                "--entry-id",
                "ent_1",
                "--revision-id",
                "rev_b",
                "--note",
                "Kept mobile correction",
                "--json"
            ]);

        Assert.Equal(PortableLogbookCommand.ResolveConflict, options.Command);
        Assert.Equal(Path.GetFullPath(workbook), options.WorkbookPath);
        Assert.Equal(Path.GetFullPath(recovery), options.RecoveryCodeFilePath);
        Assert.Equal("ent_1", options.EntryId);
        Assert.Equal("rev_b", options.RevisionId);
        Assert.Equal("Kept mobile correction", options.Note);
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
    public void ParseRejectsPrintedCopyMissingHolderDateOfBirth()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        File.WriteAllText(recovery, "Recovery code: placeholder");

        var exception = Assert.Throws<UpdaterUsageException>(
            () => PortableLogbookCommandOptions.Parse(
                [
                    "printed-copy",
                    "--workbook",
                    workbook,
                    "--recovery-code-file",
                    recovery,
                    "--output",
                    Path.Combine(directory, "printed-copy.html"),
                    "--holder-name",
                    "Alex Pilot"
                ]));

        Assert.Contains("--holder-date-of-birth", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsRevisionHistoryMissingEntryId()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        File.WriteAllText(recovery, "Recovery code: placeholder");

        var exception = Assert.Throws<UpdaterUsageException>(
            () => PortableLogbookCommandOptions.Parse(
                ["revision-history", "--workbook", workbook, "--recovery-code-file", recovery]));

        Assert.Contains("--entry-id", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsResolveConflictMissingRevisionId()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        File.WriteAllText(recovery, "Recovery code: placeholder");

        var exception = Assert.Throws<UpdaterUsageException>(
            () => PortableLogbookCommandOptions.Parse(
                ["resolve-conflict", "--workbook", workbook, "--recovery-code-file", recovery, "--entry-id", "ent_1"]));

        Assert.Contains("--revision-id", exception.Message, StringComparison.Ordinal);
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
    public void HostedWorkbookMetadataRoundTripsThroughHiddenDefinedNames()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);

        var result = PortableLogbookWorkbookPackageStorage.EnsureHostedWorkbookMetadata(
            workbook,
            new HostedAccountId("acct_123"),
            "ElectronicLogbook.Hosted/log_123/dev_123",
            42,
            "Waiting",
            DateTimeOffset.Parse("2026-08-06T12:00:00Z"),
            "Network unavailable.");
        var readBack = PortableLogbookWorkbookPackageStorage.ReadHostedWorkbookMetadata(workbook);

        Assert.True(result.WorkbookMutated);
        Assert.NotNull(readBack);
        Assert.Equal("acct_123", readBack.AccountId.Value);
        Assert.Equal("ElectronicLogbook.Hosted/log_123/dev_123", readBack.CredentialTargetName);
        Assert.Equal(42, readBack.LastAcknowledgedHostedRevision);
        Assert.Equal("Waiting", readBack.Status);
        Assert.Equal(DateTimeOffset.Parse("2026-08-06T12:00:00Z"), readBack.StatusAt);
        Assert.Equal("Network unavailable.", readBack.AttentionRequiredReason);
    }

    [Fact]
    public void V2EnableSeedsWorkbookFaithfulStorageFromExistingRowsAndCustomFields()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "seeded-recovery.txt");
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            ReplaceLogbookTable(
                archive,
                "A1:L2",
                [
                    "Year",
                    "Month",
                    "Day",
                    "Type",
                    "Reg",
                    "From",
                    "To",
                    "SeCommandDay",
                    "Custom 1",
                    "Custom 2",
                    "Custom 3",
                    "Custom 4"
                ]);
            var worksheet = ReadXml(archive, "xl/worksheets/sheet2.xml");
            UpsertInlineStringCell(worksheet, "A2", "2026");
            UpsertInlineStringCell(worksheet, "B2", "7");
            UpsertInlineStringCell(worksheet, "C2", "20");
            UpsertInlineStringCell(worksheet, "D2", "C172");
            UpsertInlineStringCell(worksheet, "E2", "VH-SEED");
            UpsertInlineStringCell(worksheet, "F2", "YSBK");
            UpsertInlineStringCell(worksheet, "G2", "YSCN");
            UpsertInlineStringCell(worksheet, "H2", "1.3");
            UpsertInlineStringCell(worksheet, "I2", "Alpha");
            UpsertInlineStringCell(worksheet, "J2", "Bravo");
            UpsertInlineStringCell(worksheet, "K2", "Charlie");
            UpsertInlineStringCell(worksheet, "L2", "Delta");
            ReplaceXml(archive, "xl/worksheets/sheet2.xml", worksheet);
        }

        var result = PortableLogbookCommandRunner.Enable(
            workbook,
            recovery,
            DateTimeOffset.Parse("2026-07-20T00:00:00Z"));
        var key = PortableLogbookKey.FromRecoveryCode(ReadRecoveryCodeFromGeneratedFile(recovery));

        var state = PortableLogbookWorkbookPackageStorage.OpenStateV2(workbook, key);

        Assert.Equal(1, result.InitialOperationCount);
        Assert.NotNull(state);
        Assert.Equal(PortableLogbookDocumentV2.CurrentSchemaVersion, state.Document.SchemaVersion);
        Assert.Equal(4, state.Document.CustomFieldDefinitions.Count);
        Assert.Equal(["Custom 1", "Custom 2", "Custom 3", "Custom 4"], state.Document.CustomFieldDefinitions.Select(field => field.Label));
        var create = Assert.Single(state.Document.Operations);
        Assert.Equal(PortableOperationKind.Create, create.Kind);
        Assert.NotNull(create.Entry);
        Assert.Equal(new DateOnly(2026, 7, 20), create.Entry.Date);
        Assert.Equal("C172", create.Entry.Type);
        Assert.Equal("VH-SEED", create.Entry.Reg);
        Assert.Equal("YSBK", create.Entry.From);
        Assert.Equal("YSCN", create.Entry.To);
        Assert.Equal(1.3m, create.Entry.SeCommandDay);
        Assert.Equal("Alpha", create.Entry.CustomFields[new CustomFieldId("cf_workbook_1")]);
        Assert.Equal("Bravo", create.Entry.CustomFields[new CustomFieldId("cf_workbook_2")]);
        Assert.Equal("Charlie", create.Entry.CustomFields[new CustomFieldId("cf_workbook_3")]);
        Assert.Equal("Delta", create.Entry.CustomFields[new CustomFieldId("cf_workbook_4")]);
    }

    [Fact]
    public void V2CommandsEnableExportPreviewAndApplyWorkbookFaithfulPackage()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "v2-recovery.txt");
        var exportedPackage = Path.Combine(directory, "v2-export.elogbook");
        var incomingPackage = Path.Combine(directory, "v2-incoming.elogbook");

        var enabled = PortableLogbookCommandRunner.Enable(
            workbook,
            recovery,
            DateTimeOffset.Parse("2026-07-27T00:00:00Z"));
        var key = PortableLogbookKey.FromRecoveryCode(ReadRecoveryCodeFromGeneratedFile(recovery));
        var state = PortableLogbookWorkbookPackageStorage.OpenStateV2(workbook, key);

        Assert.NotNull(state);
        Assert.Equal(PortableLogbookDocumentV2.CurrentSchemaVersion, enabled.SchemaVersion);
        Assert.Equal(PortableLogbookDocumentV2.CurrentSchemaVersion, state.Document.SchemaVersion);

        var export = PortableLogbookCommandRunner.Export(
            workbook,
            recovery,
            exportedPackage,
            DateTimeOffset.Parse("2026-07-27T00:01:00Z"));
        var exported = PortableLogbookPackageFile.ReadV2(exportedPackage, key, enabled.LogbookId);
        Assert.Equal(PortableLogbookDocumentV2.CurrentSchemaVersion, export.SchemaVersion);
        Assert.Equal(enabled.LogbookId, exported.Document.LogbookId);

        var incoming = PortableLogbookDocumentV2.CreateAustraliaFirst(
            enabled.LogbookId,
            [],
            PortableLogbookCurrencyOverrideDates.Empty,
            [PortableLogbookOperationV2.Create(
                enabled.LogbookId,
                new EntryId("ent_v2_command_roundtrip"),
                new RevisionId("rev_v2_command_roundtrip"),
                new DeviceId("dev_v2_command_roundtrip"),
                new DateTimeOffset(2026, 7, 27, 0, 2, 0, TimeSpan.Zero),
                PortableLogbookWorkbookEntry.Empty with { Year = 2026, Month = 7, Day = 27, Type = "C172", Reg = "VH-V2" })]);
        File.WriteAllBytes(incomingPackage, PortableLogbookPackage.Write(incoming, key));

        var preview = PortableLogbookCommandRunner.PreviewImport(workbook, recovery, incomingPackage);
        var applied = PortableLogbookCommandRunner.ApplyImport(
            workbook,
            recovery,
            incomingPackage,
            DateTimeOffset.Parse("2026-07-27T00:03:00Z"));
        var updated = PortableLogbookWorkbookPackageStorage.OpenStateV2(workbook, key);

        Assert.Equal("readyToApply", preview.Status);
        Assert.Equal("applied", applied.Status);
        Assert.True(applied.StorageUpdated);
        Assert.NotNull(updated);
        Assert.Contains(updated.Document.Operations, operation => operation.EntryId == new EntryId("ent_v2_command_roundtrip"));
    }

    [Fact]
    public void PreserveVisibleWorkbookRowOrderKeepsSameDateIdsWithTheirVisibleEntries()
    {
        var first = PortableLogbookWorkbookEntry.Empty with { Year = 2026, Month = 7, Day = 27, Reg = "VH-FIRST" };
        var second = first with { Reg = "VH-SECOND" };
        var visibleRows = new[]
        {
            new PortableLogbookWorkbookRowV2(new EntryId("ent_z"), new RevisionId("rev_old_z"), first),
            new PortableLogbookWorkbookRowV2(new EntryId("ent_a"), new RevisionId("rev_old_a"), second)
        };
        var projectedRows = new[]
        {
            new PortableLogbookWorkbookRowV2(new EntryId("ent_a"), new RevisionId("rev_new_a"), second),
            new PortableLogbookWorkbookRowV2(new EntryId("ent_z"), new RevisionId("rev_new_z"), first)
        };

        var ordered = PortableLogbookCommandRunner.PreserveVisibleWorkbookRowOrder(visibleRows, projectedRows);

        Assert.Equal([new EntryId("ent_z"), new EntryId("ent_a")], ordered.Select(row => row.EntryId));
        Assert.Equal([new RevisionId("rev_new_z"), new RevisionId("rev_new_a")], ordered.Select(row => row.CurrentRevisionId));
        Assert.Equal(["VH-FIRST", "VH-SECOND"], ordered.Select(row => row.Entry.Reg));
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
    public void ApplyImportRejectsNonElogbookPackageBeforeCreatingBackup()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        var incomingPackage = Path.Combine(directory, "incoming.txt");
        var enabled = PortableLogbookCommandRunner.Enable(
            workbook,
            recovery,
            DateTimeOffset.Parse("2026-07-19T00:00:00Z"));
        var key = PortableLogbookKey.FromRecoveryCode(ReadRecoveryCodeFromGeneratedFile(recovery));
        File.WriteAllBytes(
            incomingPackage,
            PortableLogbookPackage.Write(
                PortableLogbookDocument.CreateAustraliaFirst(enabled.LogbookId, [], []),
                key));

        var exception = Assert.Throws<UpdaterUsageException>(() => PortableLogbookCommandRunner.ApplyImport(
            workbook,
            recovery,
            incomingPackage,
            DateTimeOffset.Parse("2026-07-19T00:10:00Z")));

        Assert.Contains(".elogbook", exception.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(directory, "*.portable-import-backup-*.xlsm"));
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

    private static string ReadRecoveryCodeFromGeneratedFile(string recoveryPath) =>
        File.ReadAllLines(recoveryPath)
            .Select(line => line.Trim())
            .Single(line => line.StartsWith("Recovery code:", StringComparison.Ordinal))
            .Split(':', 2)[1]
            .Trim();

    private static XDocument ReadXml(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName) ?? throw new InvalidOperationException($"{entryName} was not found.");
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static void ReplaceXml(ZipArchive archive, string entryName, XDocument document)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        document.Save(stream);
    }

    private static void ReplaceLogbookTable(
        ZipArchive archive,
        string reference,
        IReadOnlyList<string> columnNames)
    {
        var table = ReadXml(archive, "xl/tables/table1.xml");
        var ns = table.Root!.Name.Namespace;
        table.Root.SetAttributeValue("ref", reference);
        table.Root.Elements().Single(element => element.Name.LocalName == "autoFilter").SetAttributeValue("ref", reference);
        var tableColumns = table.Root.Elements().Single(element => element.Name.LocalName == "tableColumns");
        tableColumns.SetAttributeValue("count", columnNames.Count);
        tableColumns.Elements().Remove();
        for (var index = 0; index < columnNames.Count; index++)
        {
            tableColumns.Add(new XElement(
                ns + "tableColumn",
                new XAttribute("id", index + 1),
                new XAttribute("name", columnNames[index])));
        }

        ReplaceXml(archive, "xl/tables/table1.xml", table);
    }

    private static void UpsertInlineStringCell(XDocument worksheet, string cellReference, string value)
    {
        var root = worksheet.Root ?? throw new InvalidOperationException("Worksheet XML is invalid.");
        var ns = root.Name.Namespace;
        var rowNumber = int.Parse(
            new string(cellReference.SkipWhile(char.IsLetter).ToArray()),
            System.Globalization.CultureInfo.InvariantCulture);
        var sheetData = root.Element(ns + "sheetData");
        if (sheetData is null)
        {
            sheetData = new XElement(ns + "sheetData");
            root.Add(sheetData);
        }

        var row = sheetData.Elements(ns + "row")
            .FirstOrDefault(element => ((int?)element.Attribute("r") ?? 0) == rowNumber);
        if (row is null)
        {
            row = new XElement(ns + "row", new XAttribute("r", rowNumber));
            sheetData.Add(row);
        }

        row.Elements(ns + "c")
            .Where(cell => string.Equals((string?)cell.Attribute("r"), cellReference, StringComparison.OrdinalIgnoreCase))
            .Remove();
        row.Add(new XElement(
            ns + "c",
            new XAttribute("r", cellReference),
            new XAttribute("t", "inlineStr"),
            new XElement(ns + "is", new XElement(ns + "t", value))));
    }

}
