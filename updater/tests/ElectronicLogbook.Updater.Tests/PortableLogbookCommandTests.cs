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

        var options = PortableLogbookCommandOptions.Parse(["hosted-sync", "--workbook", workbook, "--json"]);

        Assert.Equal(PortableLogbookCommand.HostedSync, options.Command);
        Assert.Equal(Path.GetFullPath(workbook), options.WorkbookPath);
        Assert.True(options.Json);
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

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
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
        using var archive = System.IO.Compression.ZipFile.OpenRead(workbook);
        var table = archive.GetEntry("xl/tables/table1.xml");
        Assert.NotNull(table);
        using var tableStream = table.Open();
        var tableDocument = System.Xml.Linq.XDocument.Load(tableStream);
        Assert.Contains(
            tableDocument.Descendants().Where(element => element.Name.LocalName == "tableColumn"),
            column => (string?)column.Attribute("name") == "EntryID");
        Assert.Contains(
            tableDocument.Descendants().Where(element => element.Name.LocalName == "tableColumn"),
            column => (string?)column.Attribute("name") == "Portable Current Revision ID");
        var workbookXml = archive.GetEntry("xl/workbook.xml");
        Assert.NotNull(workbookXml);
        using var workbookStream = workbookXml.Open();
        var workbookDocument = System.Xml.Linq.XDocument.Load(workbookStream);
        Assert.Contains(
            workbookDocument.Descendants().Where(element => element.Name.LocalName == "definedName"),
            name =>
                (string?)name.Attribute("name") == PortableLogbookWorkbookMetadata.LogbookIdName &&
                name.Value == "'Backend'!$A$8" &&
                (string?)name.Attribute("hidden") == "1");
        var backendXml = archive.GetEntry("xl/worksheets/sheet1.xml");
        Assert.NotNull(backendXml);
        using var backendStream = backendXml.Open();
        var backendDocument = System.Xml.Linq.XDocument.Load(backendStream);
        Assert.Contains(
            backendDocument.Descendants().Where(element => element.Name.LocalName == "c"),
            cell =>
                (string?)cell.Attribute("r") == "A8" &&
                cell.Descendants().Any(text => text.Name.LocalName == "t" && text.Value == result.LogbookId.Value));
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

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
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

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
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
        Assert.Equal(0, export.WorkbookRowCount);
        Assert.Equal(0, export.PendingOperationCount);
        Assert.Equal(0, export.PendingCreateCount);
        Assert.Equal(0, export.PendingCorrectionCount);
        Assert.Equal(0, export.PendingDeletionCount);
        Assert.True(File.Exists(package));
        var key = PortableLogbookKey.FromRecoveryCode(ReadRecoveryCodeFromGeneratedFile(recovery));
        var read = PortableLogbookPackageFile.Read(package, key, enabled.LogbookId);
        Assert.Equal(enabled.LogbookId, read.Document.LogbookId);
        Assert.Empty(read.Document.Operations);
    }

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
    public void ExportReconcilesInsertedWorkbookRowsIntoPackageAndWorkbookStorage()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        var package = Path.Combine(directory, "export-inserted.elogbook");
        var enabled = PortableLogbookCommandRunner.Enable(
            workbook,
            recovery,
            DateTimeOffset.Parse("2026-07-19T00:00:00Z"));
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            ReplaceLogbookTable(
                archive,
                "A1:H2",
                ["Date", "Aircraft Type", "Reg", "From", "To", "PIC", "EntryID", "Portable Current Revision ID"]);
            var worksheet = ReadXml(archive, "xl/worksheets/sheet2.xml");
            UpsertInlineStringCell(worksheet, "A2", "2026-07-19");
            UpsertInlineStringCell(worksheet, "B2", "C172");
            UpsertInlineStringCell(worksheet, "C2", "VH-NEW");
            UpsertInlineStringCell(worksheet, "D2", "YSBK");
            UpsertInlineStringCell(worksheet, "E2", "YSCN");
            UpsertInlineStringCell(worksheet, "F2", "1.4");
            ReplaceXml(archive, "xl/worksheets/sheet2.xml", worksheet);
        }

        var export = PortableLogbookCommandRunner.Export(
            workbook,
            recovery,
            package,
            DateTimeOffset.Parse("2026-07-19T01:00:00Z"));
        var key = PortableLogbookKey.FromRecoveryCode(ReadRecoveryCodeFromGeneratedFile(recovery));
        var read = PortableLogbookPackageFile.Read(package, key, enabled.LogbookId);
        var state = PortableLogbookWorkbookPackageStorage.OpenState(workbook, key);

        Assert.Equal(1, export.OperationCount);
        Assert.Equal(1, export.WorkbookRowCount);
        Assert.Equal(1, export.PendingOperationCount);
        Assert.Equal(1, export.PendingCreateCount);
        Assert.Equal(0, export.PendingCorrectionCount);
        Assert.Equal(0, export.PendingDeletionCount);
        var create = Assert.IsType<CreateEntryOperation>(Assert.Single(read.Document.Operations));
        Assert.Equal("VH-NEW", create.Entry.Registration);
        Assert.Equal(1.4m, create.Entry.PilotInCommand);
        Assert.NotNull(state);
        Assert.Equal([create.RevisionId], state.Document.Operations.Select(operation => operation.RevisionId));
        using var readArchive = ZipFile.OpenRead(workbook);
        var worksheetAfter = ReadXml(readArchive, "xl/worksheets/sheet2.xml");
        Assert.Equal(create.EntryId.Value, ReadInlineStringCell(worksheetAfter, "G2"));
        Assert.Equal(create.RevisionId.Value, ReadInlineStringCell(worksheetAfter, "H2"));
    }

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
    public void ExportReconcilesDirectWorkbookCellEditAsCorrection()
    {
        var workbook = CreateWorkbookWithInsertedExportedRow(
            "export-before-edit.elogbook",
            out var recovery,
            out var key,
            out var enabled,
            out var create);
        var package = Path.Combine(directory, "export-after-edit.elogbook");
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            var worksheet = ReadXml(archive, "xl/worksheets/sheet2.xml");
            UpsertInlineStringCell(worksheet, "C2", "VH-EDIT");
            ReplaceXml(archive, "xl/worksheets/sheet2.xml", worksheet);
        }

        var export = PortableLogbookCommandRunner.Export(
            workbook,
            recovery,
            package,
            DateTimeOffset.Parse("2026-07-19T02:00:00Z"));
        var read = PortableLogbookPackageFile.Read(package, key, enabled.LogbookId);
        var state = PortableLogbookWorkbookPackageStorage.OpenState(workbook, key);

        Assert.Equal(2, export.OperationCount);
        Assert.Equal(1, export.PendingOperationCount);
        Assert.Equal(0, export.PendingCreateCount);
        Assert.Equal(1, export.PendingCorrectionCount);
        Assert.Equal(0, export.PendingDeletionCount);
        var correction = Assert.IsType<CorrectEntryOperation>(read.Document.Operations.Last());
        Assert.Equal(create.EntryId, correction.EntryId);
        Assert.Equal("VH-EDIT", correction.Entry.Registration);
        Assert.Equal(create.RevisionId, Assert.Single(correction.ParentRevisionIds));
        Assert.NotNull(state);
        Assert.Equal(correction.RevisionId, state.Document.Operations.Last().RevisionId);
        using var readArchive = ZipFile.OpenRead(workbook);
        var worksheetAfter = ReadXml(readArchive, "xl/worksheets/sheet2.xml");
        Assert.Equal(create.EntryId.Value, ReadInlineStringCell(worksheetAfter, "G2"));
        Assert.Equal(correction.RevisionId.Value, ReadInlineStringCell(worksheetAfter, "H2"));
    }

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
    public void ExportReconcilesRemovedWorkbookRowAsDeletion()
    {
        var workbook = CreateWorkbookWithInsertedExportedRow(
            "export-before-delete.elogbook",
            out var recovery,
            out var key,
            out var enabled,
            out var create);
        var package = Path.Combine(directory, "export-after-delete.elogbook");
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            ReplaceLogbookTable(
                archive,
                "A1:H1",
                ["Date", "Aircraft Type", "Reg", "From", "To", "PIC", "EntryID", "Portable Current Revision ID"]);
            var worksheet = ReadXml(archive, "xl/worksheets/sheet2.xml");
            for (var column = 'A'; column <= 'H'; column++)
            {
                RemoveCell(worksheet, $"{column}2");
            }

            ReplaceXml(archive, "xl/worksheets/sheet2.xml", worksheet);
        }

        var export = PortableLogbookCommandRunner.Export(
            workbook,
            recovery,
            package,
            DateTimeOffset.Parse("2026-07-19T02:00:00Z"));
        var read = PortableLogbookPackageFile.Read(package, key, enabled.LogbookId);
        var state = PortableLogbookWorkbookPackageStorage.OpenState(workbook, key);

        Assert.Equal(2, export.OperationCount);
        Assert.Equal(0, export.WorkbookRowCount);
        Assert.Equal(1, export.PendingOperationCount);
        Assert.Equal(0, export.PendingCreateCount);
        Assert.Equal(0, export.PendingCorrectionCount);
        Assert.Equal(1, export.PendingDeletionCount);
        var deletion = Assert.IsType<DeleteEntryOperation>(read.Document.Operations.Last());
        Assert.Equal(create.EntryId, deletion.EntryId);
        Assert.Equal(create.RevisionId, Assert.Single(deletion.ParentRevisionIds));
        Assert.NotNull(state);
        Assert.True(PortableLogbookMerger.Merge(state.Document.Operations).Entries[create.EntryId].IsDeleted);
    }

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
    public void ExportPreservesStableIdsWhenWorkbookRowsAreSorted()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        var incomingPackage = Path.Combine(directory, "incoming-sort.elogbook");
        var exportedPackage = Path.Combine(directory, "export-after-sort.elogbook");
        var enabled = PortableLogbookCommandRunner.Enable(
            workbook,
            recovery,
            DateTimeOffset.Parse("2026-07-19T00:00:00Z"));
        var key = PortableLogbookKey.FromRecoveryCode(ReadRecoveryCodeFromGeneratedFile(recovery));
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            ReplaceLogbookTable(
                archive,
                "A1:H3",
                ["Date", "Aircraft Type", "Reg", "From", "To", "PIC", "EntryID", "Portable Current Revision ID"]);
        }

        var first = new CreateEntryOperation(
            enabled.LogbookId,
            new EntryId("ent_first"),
            new RevisionId("rev_first"),
            enabled.DeviceId,
            DateTimeOffset.Parse("2026-07-19T00:05:00Z"),
            Entry("VH-AAA") with { Date = new DateOnly(2026, 7, 18) });
        var second = new CreateEntryOperation(
            enabled.LogbookId,
            new EntryId("ent_second"),
            new RevisionId("rev_second"),
            enabled.DeviceId,
            DateTimeOffset.Parse("2026-07-19T00:06:00Z"),
            Entry("VH-BBB") with { Date = new DateOnly(2026, 7, 19) });
        PortableLogbookPackageFile.Write(
            incomingPackage,
            PortableLogbookDocument.CreateAustraliaFirst(enabled.LogbookId, [], [first, second]),
            key);
        PortableLogbookCommandRunner.ApplyImport(
            workbook,
            recovery,
            incomingPackage,
            DateTimeOffset.Parse("2026-07-19T00:10:00Z"));

        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            var worksheet = ReadXml(archive, "xl/worksheets/sheet2.xml");
            SwapWorksheetRows(worksheet, 2, 3, 'A', 'H');
            ReplaceXml(archive, "xl/worksheets/sheet2.xml", worksheet);
        }

        var export = PortableLogbookCommandRunner.Export(
            workbook,
            recovery,
            exportedPackage,
            DateTimeOffset.Parse("2026-07-19T01:00:00Z"));
        var read = PortableLogbookPackageFile.Read(exportedPackage, key, enabled.LogbookId);

        Assert.Equal(2, export.OperationCount);
        Assert.Equal(2, export.WorkbookRowCount);
        Assert.Equal(0, export.PendingOperationCount);
        Assert.Equal(0, export.PendingCreateCount);
        Assert.Equal(0, export.PendingCorrectionCount);
        Assert.Equal(0, export.PendingDeletionCount);
        Assert.Equal([first.RevisionId, second.RevisionId], read.Document.Operations.Select(operation => operation.RevisionId));
        using var readArchive = ZipFile.OpenRead(workbook);
        var worksheetAfter = ReadXml(readArchive, "xl/worksheets/sheet2.xml");
        Assert.Equal(first.EntryId.Value, ReadInlineStringCell(worksheetAfter, "G2"));
        Assert.Equal(first.RevisionId.Value, ReadInlineStringCell(worksheetAfter, "H2"));
        Assert.Equal(second.EntryId.Value, ReadInlineStringCell(worksheetAfter, "G3"));
        Assert.Equal(second.RevisionId.Value, ReadInlineStringCell(worksheetAfter, "H3"));
    }

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
    public void ExportIgnoresBlankInsertedWorkbookRowsBetweenKnownRows()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        var incomingPackage = Path.Combine(directory, "incoming-blank-row.elogbook");
        var exportedPackage = Path.Combine(directory, "export-after-blank-row.elogbook");
        var enabled = PortableLogbookCommandRunner.Enable(
            workbook,
            recovery,
            DateTimeOffset.Parse("2026-07-19T00:00:00Z"));
        var key = PortableLogbookKey.FromRecoveryCode(ReadRecoveryCodeFromGeneratedFile(recovery));
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            ReplaceLogbookTable(
                archive,
                "A1:H3",
                ["Date", "Aircraft Type", "Reg", "From", "To", "PIC", "EntryID", "Portable Current Revision ID"]);
        }

        var first = new CreateEntryOperation(
            enabled.LogbookId,
            new EntryId("ent_first"),
            new RevisionId("rev_first"),
            enabled.DeviceId,
            DateTimeOffset.Parse("2026-07-19T00:05:00Z"),
            Entry("VH-AAA") with { Date = new DateOnly(2026, 7, 18) });
        var second = new CreateEntryOperation(
            enabled.LogbookId,
            new EntryId("ent_second"),
            new RevisionId("rev_second"),
            enabled.DeviceId,
            DateTimeOffset.Parse("2026-07-19T00:06:00Z"),
            Entry("VH-BBB") with { Date = new DateOnly(2026, 7, 19) });
        PortableLogbookPackageFile.Write(
            incomingPackage,
            PortableLogbookDocument.CreateAustraliaFirst(enabled.LogbookId, [], [first, second]),
            key);
        PortableLogbookCommandRunner.ApplyImport(
            workbook,
            recovery,
            incomingPackage,
            DateTimeOffset.Parse("2026-07-19T00:10:00Z"));

        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            ReplaceLogbookTable(
                archive,
                "A1:H4",
                ["Date", "Aircraft Type", "Reg", "From", "To", "PIC", "EntryID", "Portable Current Revision ID"]);
            var worksheet = ReadXml(archive, "xl/worksheets/sheet2.xml");
            MoveWorksheetRowValues(worksheet, 3, 4, 'A', 'H');
            ReplaceXml(archive, "xl/worksheets/sheet2.xml", worksheet);
        }

        var export = PortableLogbookCommandRunner.Export(
            workbook,
            recovery,
            exportedPackage,
            DateTimeOffset.Parse("2026-07-19T01:00:00Z"));
        var read = PortableLogbookPackageFile.Read(exportedPackage, key, enabled.LogbookId);

        Assert.Equal(2, export.OperationCount);
        Assert.Equal(2, export.WorkbookRowCount);
        Assert.Equal(0, export.PendingOperationCount);
        Assert.Equal(0, export.PendingCreateCount);
        Assert.Equal(0, export.PendingCorrectionCount);
        Assert.Equal(0, export.PendingDeletionCount);
        Assert.Equal([first.RevisionId, second.RevisionId], read.Document.Operations.Select(operation => operation.RevisionId));
        using var readArchive = ZipFile.OpenRead(workbook);
        var worksheetAfter = ReadXml(readArchive, "xl/worksheets/sheet2.xml");
        Assert.Equal(first.EntryId.Value, ReadInlineStringCell(worksheetAfter, "G2"));
        Assert.Equal(first.RevisionId.Value, ReadInlineStringCell(worksheetAfter, "H2"));
        Assert.Equal(second.EntryId.Value, ReadInlineStringCell(worksheetAfter, "G3"));
        Assert.Equal(second.RevisionId.Value, ReadInlineStringCell(worksheetAfter, "H3"));
        Assert.Null(ReadInlineStringCell(worksheetAfter, "G4"));
        Assert.Null(ReadInlineStringCell(worksheetAfter, "H4"));
    }

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
    public void ExportRejectsStaleWorkbookRowMetadataWithoutMutatingStorageOrWritingPackage()
    {
        var workbook = CreateWorkbookWithInsertedExportedRow(
            "export-before-stale-metadata.elogbook",
            out var recovery,
            out var key,
            out var enabled,
            out var create);
        var package = Path.Combine(directory, "export-stale-metadata.elogbook");
        var stateBefore = PortableLogbookWorkbookPackageStorage.OpenState(workbook, key);
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            var worksheet = ReadXml(archive, "xl/worksheets/sheet2.xml");
            UpsertInlineStringCell(worksheet, "H2", "rev_stale");
            ReplaceXml(archive, "xl/worksheets/sheet2.xml", worksheet);
        }

        var exception = Assert.Throws<PortableLogbookWorkbookProjectionException>(() =>
            PortableLogbookCommandRunner.Export(
                workbook,
                recovery,
                package,
                DateTimeOffset.Parse("2026-07-19T02:00:00Z")));
        var stateAfter = PortableLogbookWorkbookPackageStorage.OpenState(workbook, key);

        Assert.Equal(PortableLogbookWorkbookProjectionError.InvalidRowMetadata, exception.Error);
        Assert.Contains(exception.RowValidation.Errors, error => error.Code == PortableLogbookWorkbookRowValidationCode.StaleCurrentRevisionId);
        Assert.False(File.Exists(package));
        Assert.NotNull(stateBefore);
        Assert.NotNull(stateAfter);
        Assert.Equal(
            stateBefore.Document.Operations.Select(operation => operation.RevisionId),
            stateAfter.Document.Operations.Select(operation => operation.RevisionId));
        using var readArchive = ZipFile.OpenRead(workbook);
        var worksheetAfter = ReadXml(readArchive, "xl/worksheets/sheet2.xml");
        Assert.Equal(create.EntryId.Value, ReadInlineStringCell(worksheetAfter, "G2"));
        Assert.Equal("rev_stale", ReadInlineStringCell(worksheetAfter, "H2"));
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

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
    public void CreatePrintedCopyWritesHtmlFromWorkbookStorage()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var key = PortableLogbookKey.Generate();
        PortableLogbookWorkbookPackageStorage.WriteEnvelope(workbook, CreateEnvelope("log_cli_print", key));
        var recovery = Path.Combine(directory, "recovery.txt");
        File.WriteAllText(recovery, key.ToRecoveryCode());
        var output = Path.Combine(directory, "printed-copy.html");

        var result = PortableLogbookCommandRunner.CreatePrintedCopy(
            workbook,
            recovery,
            output,
            "Alex Pilot",
            new DateOnly(1990, 1, 2),
            new DateOnly(2026, 7, 19),
            recordsPerPage: 25);

        Assert.Equal(Path.GetFullPath(workbook), result.WorkbookPath);
        Assert.Equal(Path.GetFullPath(output), result.OutputPath);
        Assert.Equal(new LogbookId("log_cli_print"), result.LogbookId);
        Assert.Equal(1, result.PageCount);
        Assert.Equal(1, result.CurrentRecordCount);
        Assert.Equal(1, result.RevisionCount);
        var html = File.ReadAllText(output);
        Assert.Contains("Certified Electronic Logbook Printed Copy", html, StringComparison.Ordinal);
        Assert.Contains("Alex Pilot", html, StringComparison.Ordinal);
        Assert.Contains("1990-01-02", html, StringComparison.Ordinal);
        Assert.Contains("VH-ABC", html, StringComparison.Ordinal);
    }

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
    public void ReadRevisionHistoryReturnsEntryHistoryFromWorkbookStorage()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var key = PortableLogbookKey.Generate();
        PortableLogbookWorkbookPackageStorage.WriteEnvelope(workbook, CreateEnvelope("log_cli_history", key));
        var recovery = Path.Combine(directory, "recovery.txt");
        File.WriteAllText(recovery, key.ToRecoveryCode());

        var result = PortableLogbookCommandRunner.ReadRevisionHistory(
            workbook,
            recovery,
            new EntryId("ent_1"));

        Assert.Equal(Path.GetFullPath(workbook), result.WorkbookPath);
        Assert.Equal(new LogbookId("log_cli_history"), result.LogbookId);
        Assert.Equal(new EntryId("ent_1"), result.EntryId);
        Assert.Equal(new RevisionId("rev_1"), result.CurrentRevisionId);
        Assert.False(result.IsDeleted);
        Assert.False(result.HasConflict);
        var revision = Assert.Single(result.Revisions);
        Assert.Equal(PortableOperationKind.Create, revision.Kind);
        Assert.Equal("VH-ABC", revision.Entry?.Registration);
    }

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
    public void ResolveConflictKeepsSelectedHeadRevisionAndCreatesBackup()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var key = PortableLogbookKey.Generate();
        PortableLogbookWorkbookPackageStorage.WriteEnvelope(workbook, CreateConflictEnvelope("log_cli_conflict", key));
        var recovery = Path.Combine(directory, "recovery.txt");
        File.WriteAllText(recovery, key.ToRecoveryCode());

        var result = PortableLogbookCommandRunner.ResolveConflict(
            workbook,
            recovery,
            new EntryId("ent_1"),
            new RevisionId("rev_b"),
            "Kept imported correction",
            DateTimeOffset.Parse("2026-07-19T00:20:00Z"));
        var state = PortableLogbookWorkbookPackageStorage.OpenState(workbook, key);

        Assert.Equal(Path.GetFullPath(workbook), result.WorkbookPath);
        Assert.True(File.Exists(result.BackupPath));
        Assert.Equal(new LogbookId("log_cli_conflict"), result.LogbookId);
        Assert.Equal(new EntryId("ent_1"), result.EntryId);
        Assert.Equal(new RevisionId("rev_b"), result.KeptRevisionId);
        Assert.Equal(0, result.RemainingConflictCount);
        Assert.NotNull(state);
        var merge = PortableLogbookMerger.Merge(state.Document.Operations);
        Assert.Empty(merge.Conflicts);
        var current = Assert.Single(merge.Entries.Values);
        Assert.Equal("VH-BBB", current.Entry?.Registration);
        var resolution = Assert.IsType<ResolveConflictOperation>(state.Document.Operations.Last());
        Assert.Equal("Kept imported correction", resolution.ResolutionNote);
        Assert.Equal([new RevisionId("rev_a"), new RevisionId("rev_b")], resolution.ParentRevisionIds.OrderBy(id => id.Value).ToArray());
        var backupState = PortableLogbookWorkbookPackageStorage.OpenState(result.BackupPath, key);
        Assert.NotNull(backupState);
        Assert.Single(PortableLogbookMerger.Merge(backupState.Document.Operations).Conflicts);
    }

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
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

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
    public void PreviewImportCorrectionDoesNotMutateVisibleWorkbookRowsOrStorage()
    {
        var workbook = CreateWorkbookWithInsertedExportedRow(
            "preview-base.elogbook",
            out var recovery,
            out var key,
            out var enabled,
            out var create);
        var incomingPackage = Path.Combine(directory, "preview-correction.elogbook");
        var correction = new CorrectEntryOperation(
            enabled.LogbookId,
            create.EntryId,
            new RevisionId("rev_preview_correction"),
            new HashSet<RevisionId> { create.RevisionId },
            enabled.DeviceId,
            DateTimeOffset.Parse("2026-07-19T02:00:00Z"),
            create.Entry with
            {
                Registration = "VH-PRV",
                PilotInCommand = 1.8m
            });
        PortableLogbookPackageFile.Write(
            incomingPackage,
            PortableLogbookDocument.CreateAustraliaFirst(enabled.LogbookId, [], [create, correction]),
            key);
        var stateBefore = PortableLogbookWorkbookPackageStorage.OpenState(workbook, key);
        string? registrationBefore;
        string? picBefore;
        string? entryIdBefore;
        string? revisionIdBefore;
        using (var archiveBefore = ZipFile.OpenRead(workbook))
        {
            var worksheetBefore = ReadXml(archiveBefore, "xl/worksheets/sheet2.xml");
            registrationBefore = ReadInlineStringCell(worksheetBefore, "C2");
            picBefore = ReadInlineStringCell(worksheetBefore, "F2");
            entryIdBefore = ReadInlineStringCell(worksheetBefore, "G2");
            revisionIdBefore = ReadInlineStringCell(worksheetBefore, "H2");
        }

        var preview = PortableLogbookCommandRunner.PreviewImport(
            workbook,
            recovery,
            incomingPackage);
        var stateAfter = PortableLogbookWorkbookPackageStorage.OpenState(workbook, key);

        Assert.Equal("readyToApply", preview.Status);
        Assert.Equal(1, preview.NewOperationCount);
        Assert.Equal(1, preview.CorrectionCount);
        Assert.NotNull(stateBefore);
        Assert.NotNull(stateAfter);
        Assert.Equal(stateBefore.Document.Operations.Select(operation => operation.RevisionId), stateAfter.Document.Operations.Select(operation => operation.RevisionId));
        using var archiveAfter = ZipFile.OpenRead(workbook);
        var worksheetAfter = ReadXml(archiveAfter, "xl/worksheets/sheet2.xml");
        Assert.Equal(registrationBefore, ReadInlineStringCell(worksheetAfter, "C2"));
        Assert.Equal(picBefore, ReadInlineStringCell(worksheetAfter, "F2"));
        Assert.Equal(entryIdBefore, ReadInlineStringCell(worksheetAfter, "G2"));
        Assert.Equal(revisionIdBefore, ReadInlineStringCell(worksheetAfter, "H2"));
    }

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
    public void PreviewImportDeletionDoesNotMutateVisibleWorkbookRowsOrStorage()
    {
        var workbook = CreateWorkbookWithInsertedExportedRow(
            "preview-delete-base.elogbook",
            out var recovery,
            out var key,
            out var enabled,
            out var create);
        var incomingPackage = Path.Combine(directory, "preview-deletion.elogbook");
        var deletion = new DeleteEntryOperation(
            enabled.LogbookId,
            create.EntryId,
            new RevisionId("rev_preview_deletion"),
            new HashSet<RevisionId> { create.RevisionId },
            enabled.DeviceId,
            DateTimeOffset.Parse("2026-07-19T02:00:00Z"),
            "preview only");
        PortableLogbookPackageFile.Write(
            incomingPackage,
            PortableLogbookDocument.CreateAustraliaFirst(enabled.LogbookId, [], [create, deletion]),
            key);
        var stateBefore = PortableLogbookWorkbookPackageStorage.OpenState(workbook, key);
        string? registrationBefore;
        string? picBefore;
        string? entryIdBefore;
        string? revisionIdBefore;
        using (var archiveBefore = ZipFile.OpenRead(workbook))
        {
            var worksheetBefore = ReadXml(archiveBefore, "xl/worksheets/sheet2.xml");
            registrationBefore = ReadInlineStringCell(worksheetBefore, "C2");
            picBefore = ReadInlineStringCell(worksheetBefore, "F2");
            entryIdBefore = ReadInlineStringCell(worksheetBefore, "G2");
            revisionIdBefore = ReadInlineStringCell(worksheetBefore, "H2");
        }

        var preview = PortableLogbookCommandRunner.PreviewImport(
            workbook,
            recovery,
            incomingPackage);
        var stateAfter = PortableLogbookWorkbookPackageStorage.OpenState(workbook, key);

        Assert.Equal("readyToApply", preview.Status);
        Assert.Equal(1, preview.NewOperationCount);
        Assert.Equal(1, preview.DeletionCount);
        Assert.NotNull(stateBefore);
        Assert.NotNull(stateAfter);
        Assert.Equal(stateBefore.Document.Operations.Select(operation => operation.RevisionId), stateAfter.Document.Operations.Select(operation => operation.RevisionId));
        using var archiveAfter = ZipFile.OpenRead(workbook);
        var worksheetAfter = ReadXml(archiveAfter, "xl/worksheets/sheet2.xml");
        Assert.Equal(registrationBefore, ReadInlineStringCell(worksheetAfter, "C2"));
        Assert.Equal(picBefore, ReadInlineStringCell(worksheetAfter, "F2"));
        Assert.Equal(entryIdBefore, ReadInlineStringCell(worksheetAfter, "G2"));
        Assert.Equal(revisionIdBefore, ReadInlineStringCell(worksheetAfter, "H2"));
    }

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
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

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
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
        using (var archive = ZipFile.OpenRead(workbook))
        {
            var worksheet = ReadXml(archive, "xl/worksheets/sheet2.xml");
            Assert.Equal("ent_incoming", ReadInlineStringCell(worksheet, "B2"));
            Assert.Equal("rev_incoming", ReadInlineStringCell(worksheet, "C2"));
        }

        var backupState = PortableLogbookWorkbookPackageStorage.OpenState(applied.BackupPath!, key);
        Assert.NotNull(backupState);
        Assert.Empty(backupState.Document.Operations);
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

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
    public void ApplyImportAndExportRoundTripCustomFieldWorkbookEdits()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        var incomingPackage = Path.Combine(directory, "incoming-custom.elogbook");
        var exportedPackage = Path.Combine(directory, "export-custom-correction.elogbook");
        var enabled = PortableLogbookCommandRunner.Enable(
            workbook,
            recovery,
            DateTimeOffset.Parse("2026-07-19T00:00:00Z"));
        var key = PortableLogbookKey.FromRecoveryCode(ReadRecoveryCodeFromGeneratedFile(recovery));
        var customField = new CustomFieldDefinition(new CustomFieldId("cf_training_kind"), "Training kind", 1);
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            ReplaceLogbookTable(
                archive,
                "A1:I2",
                [
                    "Date",
                    "Aircraft Type",
                    "Reg",
                    "From",
                    "To",
                    "PIC",
                    "Training kind",
                    "EntryID",
                    "Portable Current Revision ID"
                ]);
        }

        var incomingCreate = new CreateEntryOperation(
            enabled.LogbookId,
            new EntryId("ent_custom"),
            new RevisionId("rev_custom_create"),
            enabled.DeviceId,
            DateTimeOffset.Parse("2026-07-19T00:05:00Z"),
            Entry("VH-CST") with
            {
                Date = new DateOnly(2026, 7, 19),
                CustomFields = new Dictionary<CustomFieldId, string?> { [customField.Id] = "Imported" }
            });
        PortableLogbookPackageFile.Write(
            incomingPackage,
            PortableLogbookDocument.CreateAustraliaFirst(enabled.LogbookId, [customField], [incomingCreate]),
            key);

        var applied = PortableLogbookCommandRunner.ApplyImport(
            workbook,
            recovery,
            incomingPackage,
            DateTimeOffset.Parse("2026-07-19T00:10:00Z"));

        Assert.Equal(1, applied.WorkbookRowCount);
        using (var archive = ZipFile.OpenRead(workbook))
        {
            var worksheet = ReadXml(archive, "xl/worksheets/sheet2.xml");
            Assert.Equal("2026-07-19", ReadInlineStringCell(worksheet, "A2"));
            Assert.Equal("C172", ReadInlineStringCell(worksheet, "B2"));
            Assert.Equal("VH-CST", ReadInlineStringCell(worksheet, "C2"));
            Assert.Equal("YSBK", ReadInlineStringCell(worksheet, "D2"));
            Assert.Equal("YSBK", ReadInlineStringCell(worksheet, "E2"));
            Assert.Equal("1.0", ReadInlineStringCell(worksheet, "F2"));
            Assert.Equal("Imported", ReadInlineStringCell(worksheet, "G2"));
            Assert.Equal("ent_custom", ReadInlineStringCell(worksheet, "H2"));
            Assert.Equal("rev_custom_create", ReadInlineStringCell(worksheet, "I2"));
        }

        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            var worksheet = ReadXml(archive, "xl/worksheets/sheet2.xml");
            UpsertInlineStringCell(worksheet, "G2", "Reviewed");
            ReplaceXml(archive, "xl/worksheets/sheet2.xml", worksheet);
        }

        var export = PortableLogbookCommandRunner.Export(
            workbook,
            recovery,
            exportedPackage,
            DateTimeOffset.Parse("2026-07-19T01:00:00Z"));
        var read = PortableLogbookPackageFile.Read(exportedPackage, key, enabled.LogbookId);
        var correction = Assert.IsType<CorrectEntryOperation>(read.Document.Operations.Last());

        Assert.Equal(1, export.PendingOperationCount);
        Assert.Equal(0, export.PendingCreateCount);
        Assert.Equal(1, export.PendingCorrectionCount);
        Assert.Equal(0, export.PendingDeletionCount);
        Assert.Equal(incomingCreate.EntryId, correction.EntryId);
        Assert.Equal("Reviewed", correction.Entry.CustomFields[customField.Id]);
        Assert.Equal(incomingCreate.RevisionId, Assert.Single(correction.ParentRevisionIds));
        using (var archive = ZipFile.OpenRead(workbook))
        {
            var worksheet = ReadXml(archive, "xl/worksheets/sheet2.xml");
            Assert.Equal("Reviewed", ReadInlineStringCell(worksheet, "G2"));
            Assert.Equal(incomingCreate.EntryId.Value, ReadInlineStringCell(worksheet, "H2"));
            Assert.Equal(correction.RevisionId.Value, ReadInlineStringCell(worksheet, "I2"));
        }
    }

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
    public void ApplyImportCorrectionUpdatesVisibleWorkbookCreatesValidatedBackupAndSurvivesReopen()
    {
        var workbook = CreateWorkbookWithInsertedExportedRow(
            "gate7-base.elogbook",
            out var recovery,
            out var key,
            out var enabled,
            out var create);
        var incomingPackage = Path.Combine(directory, "gate7-mobile-correction.elogbook");
        var correction = new CorrectEntryOperation(
            enabled.LogbookId,
            create.EntryId,
            new RevisionId("rev_gate7_mobile_correction"),
            new HashSet<RevisionId> { create.RevisionId },
            new DeviceId("dev_mobile"),
            DateTimeOffset.Parse("2026-07-19T02:05:00Z"),
            create.Entry with
            {
                Registration = "VH-G7",
                To = "YMML",
                PilotInCommand = 1.7m
            });
        PortableLogbookPackageFile.Write(
            incomingPackage,
            PortableLogbookDocument.CreateAustraliaFirst(
                enabled.LogbookId,
                [],
                [create, correction]),
            key);

        var result = PortableLogbookCommandRunner.ApplyImport(
            workbook,
            recovery,
            incomingPackage,
            DateTimeOffset.Parse("2026-07-19T02:10:00Z"));

        Assert.Equal("applied", result.Status);
        Assert.True(result.StorageUpdated);
        Assert.True(result.ReceiptRecorded);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        using (var archive = ZipFile.OpenRead(workbook))
        {
            var worksheet = ReadXml(archive, "xl/worksheets/sheet2.xml");
            Assert.Equal("VH-G7", ReadInlineStringCell(worksheet, "C2"));
            Assert.Equal("YMML", ReadInlineStringCell(worksheet, "E2"));
            Assert.Equal("1.7", ReadInlineStringCell(worksheet, "F2"));
            Assert.Equal(create.EntryId.Value, ReadInlineStringCell(worksheet, "G2"));
            Assert.Equal(correction.RevisionId.Value, ReadInlineStringCell(worksheet, "H2"));
        }

        var backupState = PortableLogbookWorkbookPackageStorage.OpenState(result.BackupPath!, key);
        Assert.NotNull(backupState);
        Assert.Equal([create.RevisionId], backupState.Document.Operations.Select(operation => operation.RevisionId));
        var reopenedState = PortableLogbookWorkbookPackageStorage.OpenState(workbook, key);
        Assert.NotNull(reopenedState);
        Assert.Equal([create.RevisionId, correction.RevisionId], reopenedState.Document.Operations.Select(operation => operation.RevisionId));
        var current = Assert.Single(PortableLogbookMerger.Merge(reopenedState.Document.Operations).Entries.Values);
        Assert.Equal(correction.RevisionId, current.CurrentRevisionId);
        Assert.Equal("VH-G7", current.Entry?.Registration);
        Assert.Equal("YMML", current.Entry?.To);
        Assert.Equal(1.7m, current.Entry?.PilotInCommand);
    }

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
    public void ExportDoesNotStampWorkbookWhenPackageWriteFails()
    {
        var workbook = CreateWorkbookWithInsertedExportedRow(
            "export-before-write-failure.elogbook",
            out var recovery,
            out var key,
            out var enabled,
            out var create);
        var missingDirectoryPackage = Path.Combine(directory, "missing-output-directory", "export.elogbook");

        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            var worksheet = ReadXml(archive, "xl/worksheets/sheet2.xml");
            UpsertInlineStringCell(worksheet, "C2", "VH-EDIT");
            ReplaceXml(archive, "xl/worksheets/sheet2.xml", worksheet);
        }

        Assert.Throws<DirectoryNotFoundException>(() => PortableLogbookCommandRunner.Export(
            workbook,
            recovery,
            missingDirectoryPackage,
            DateTimeOffset.Parse("2026-07-19T01:00:00Z")));

        var state = PortableLogbookWorkbookPackageStorage.OpenState(workbook, key);
        Assert.NotNull(state);
        Assert.Equal([create.RevisionId], state.Document.Operations.Select(operation => operation.RevisionId));
        using var readArchive = ZipFile.OpenRead(workbook);
        var worksheetAfter = ReadXml(readArchive, "xl/worksheets/sheet2.xml");
        Assert.Equal("VH-EDIT", ReadInlineStringCell(worksheetAfter, "C2"));
        Assert.Equal(create.EntryId.Value, ReadInlineStringCell(worksheetAfter, "G2"));
        Assert.Equal(create.RevisionId.Value, ReadInlineStringCell(worksheetAfter, "H2"));
        Assert.Equal(enabled.LogbookId, state.Document.LogbookId);
    }

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
    public void WorkbookPwaWorkbookPackageLoopPreservesIdsVisibleRowsAndRevisionHistory()
    {
        var workbook = CreateWorkbookWithInsertedExportedRow(
            "workbook-to-pwa.elogbook",
            out var recovery,
            out var key,
            out var enabled,
            out var create);
        var workbookExport = Path.Combine(directory, "workbook-export.elogbook");
        var pwaExport = Path.Combine(directory, "pwa-export.elogbook");
        var finalExport = Path.Combine(directory, "final-export.elogbook");

        PortableLogbookCommandRunner.Export(
            workbook,
            recovery,
            workbookExport,
            DateTimeOffset.Parse("2026-07-19T02:00:00Z"));
        var pwaRead = PortableLogbookPackageFile.Read(workbookExport, key, enabled.LogbookId);
        var pwaCorrection = new CorrectEntryOperation(
            enabled.LogbookId,
            create.EntryId,
            new RevisionId("rev_pwa_correction"),
            new HashSet<RevisionId> { create.RevisionId },
            new DeviceId("dev_pwa"),
            DateTimeOffset.Parse("2026-07-19T02:05:00Z"),
            create.Entry with
            {
                Registration = "VH-PWA",
                From = "YSBK",
                To = "YMML",
                PilotInCommand = 1.6m
            });
        PortableLogbookPackageFile.Write(
            pwaExport,
            PortableLogbookDocument.CreateAustraliaFirst(
                enabled.LogbookId,
                pwaRead.Document.CustomFieldDefinitions,
                pwaRead.Document.Operations.Append(pwaCorrection)),
            key);

        var import = PortableLogbookCommandRunner.ApplyImport(
            workbook,
            recovery,
            pwaExport,
            DateTimeOffset.Parse("2026-07-19T02:10:00Z"));
        var export = PortableLogbookCommandRunner.Export(
            workbook,
            recovery,
            finalExport,
            DateTimeOffset.Parse("2026-07-19T02:20:00Z"));
        var final = PortableLogbookPackageFile.Read(finalExport, key, enabled.LogbookId);
        var current = Assert.Single(PortableLogbookMerger.Merge(final.Document.Operations).Entries.Values);

        Assert.Equal("applied", import.Status);
        Assert.Equal(1, import.NewOperationCount);
        Assert.True(import.StorageUpdated);
        Assert.True(import.ReceiptRecorded);
        Assert.Equal(2, export.OperationCount);
        Assert.Equal(0, export.PendingOperationCount);
        Assert.Equal([create.RevisionId, pwaCorrection.RevisionId], final.Document.Operations.Select(operation => operation.RevisionId));
        Assert.Equal(create.EntryId, current.EntryId);
        Assert.Equal(pwaCorrection.RevisionId, current.CurrentRevisionId);
        Assert.Equal("VH-PWA", current.Entry?.Registration);
        Assert.Equal("YMML", current.Entry?.To);
        Assert.Equal(1.6m, current.Entry?.PilotInCommand);
        using var readArchive = ZipFile.OpenRead(workbook);
        var worksheetAfter = ReadXml(readArchive, "xl/worksheets/sheet2.xml");
        Assert.Equal("VH-PWA", ReadInlineStringCell(worksheetAfter, "C2"));
        Assert.Equal("YMML", ReadInlineStringCell(worksheetAfter, "E2"));
        Assert.Equal("1.6", ReadInlineStringCell(worksheetAfter, "F2"));
        Assert.Equal(create.EntryId.Value, ReadInlineStringCell(worksheetAfter, "G2"));
        Assert.Equal(pwaCorrection.RevisionId.Value, ReadInlineStringCell(worksheetAfter, "H2"));
    }

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
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

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
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

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
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

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
    public void PreviewAndApplyImportDoNotWriteStorageWhenCustomFieldResolutionIsRequired()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var recovery = Path.Combine(directory, "recovery.txt");
        var incomingPackage = Path.Combine(directory, "incoming-custom-field-conflict.elogbook");
        var key = PortableLogbookKey.Generate();
        var fieldId = new CustomFieldId("cf_training_kind");
        var create = new CreateEntryOperation(
            new LogbookId("log_custom_field_conflict"),
            new EntryId("ent_custom_field_conflict"),
            new RevisionId("rev_create"),
            new DeviceId("dev_excel"),
            DateTimeOffset.Parse("2026-07-19T00:00:00Z"),
            Entry("VH-CFC") with
            {
                CustomFields = new Dictionary<CustomFieldId, string?> { [fieldId] = "Training" }
            });
        var localDefinition = new CustomFieldDefinition(fieldId, "Training kind", 1);
        var incomingDefinition = new CustomFieldDefinition(fieldId, "Training category", 1);
        var localDocument = PortableLogbookDocument.CreateAustraliaFirst(
            create.LogbookId,
            [localDefinition],
            [create]);
        PortableLogbookWorkbookPackageStorage.WriteEnvelope(
            workbook,
            PortableLogbookWorkbookStorage.CreateEnvelope(
                localDocument,
                PortableLogbookPackage.Write(localDocument, key),
                []));
        File.WriteAllText(recovery, key.ToRecoveryCode());
        PortableLogbookPackageFile.Write(
            incomingPackage,
            PortableLogbookDocument.CreateAustraliaFirst(
                create.LogbookId,
                [incomingDefinition],
                [create]),
            key);

        var preview = PortableLogbookCommandRunner.PreviewImport(
            workbook,
            recovery,
            incomingPackage);
        var stateAfterPreview = PortableLogbookWorkbookPackageStorage.OpenState(workbook, key);
        var applied = PortableLogbookCommandRunner.ApplyImport(
            workbook,
            recovery,
            incomingPackage,
            DateTimeOffset.Parse("2026-07-19T00:10:00Z"));
        var stateAfterApplyAttempt = PortableLogbookWorkbookPackageStorage.OpenState(workbook, key);

        Assert.Equal("requiresCustomFieldResolution", preview.Status);
        Assert.Equal(1, preview.CustomFieldConflictCount);
        Assert.NotNull(stateAfterPreview);
        Assert.Equal([localDefinition], stateAfterPreview.Document.CustomFieldDefinitions);
        Assert.Equal("requiresResolution", applied.Status);
        Assert.False(applied.StorageUpdated);
        Assert.False(applied.ReceiptRecorded);
        Assert.Null(applied.BackupPath);
        Assert.Equal(1, applied.CustomFieldConflictCount);
        Assert.NotNull(stateAfterApplyAttempt);
        Assert.Equal([localDefinition], stateAfterApplyAttempt.Document.CustomFieldDefinitions);
        Assert.Equal([create.RevisionId], stateAfterApplyAttempt.Document.Operations.Select(operation => operation.RevisionId));
    }

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
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

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
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

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
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

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
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

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
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

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
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

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
    public async Task RunAsyncDispatchesPortablePrintedCopyJsonWithoutPrintingRecoveryCode()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var key = PortableLogbookKey.Generate();
        PortableLogbookWorkbookPackageStorage.WriteEnvelope(workbook, CreateEnvelope("log_cli_print", key));
        var recovery = Path.Combine(directory, "recovery.txt");
        File.WriteAllText(recovery, key.ToRecoveryCode());
        var outputPath = Path.Combine(directory, "printed-copy.html");
        using var output = new StringWriter();
        var originalOutput = Console.Out;
        Console.SetOut(output);
        try
        {
            var exitCode = await UpdaterProgram.RunAsync(
                [
                    "portable",
                    "printed-copy",
                    "--workbook",
                    workbook,
                    "--recovery-code-file",
                    recovery,
                    "--output",
                    outputPath,
                    "--holder-name",
                    "Alex Pilot",
                    "--holder-date-of-birth",
                    "1990-01-02",
                    "--certified-on",
                    "2026-07-19",
                    "--json"
                ]);

            Assert.Equal(0, exitCode);
            using var json = JsonDocument.Parse(output.ToString());
            Assert.Equal(Path.GetFullPath(outputPath), json.RootElement.GetProperty("outputPath").GetString());
            Assert.Equal(1, json.RootElement.GetProperty("currentRecordCount").GetInt32());
            Assert.False(json.RootElement.TryGetProperty("holderName", out _));
            Assert.False(json.RootElement.TryGetProperty("holderDateOfBirth", out _));
            Assert.False(json.RootElement.TryGetProperty("recoveryCode", out _));
            Assert.DoesNotContain(key.ToRecoveryCode(), output.ToString(), StringComparison.Ordinal);
            Assert.True(File.Exists(outputPath));
        }
        finally
        {
            Console.SetOut(originalOutput);
        }
    }

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
    public async Task RunAsyncDispatchesPortablePrintedCopyHumanOutputWithoutPrintingHolderIdentity()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var key = PortableLogbookKey.Generate();
        PortableLogbookWorkbookPackageStorage.WriteEnvelope(workbook, CreateEnvelope("log_cli_print", key));
        var recovery = Path.Combine(directory, "recovery.txt");
        File.WriteAllText(recovery, key.ToRecoveryCode());
        var outputPath = Path.Combine(directory, "printed-copy.html");
        using var output = new StringWriter();
        var originalOutput = Console.Out;
        Console.SetOut(output);
        try
        {
            var exitCode = await UpdaterProgram.RunAsync(
                [
                    "portable",
                    "printed-copy",
                    "--workbook",
                    workbook,
                    "--recovery-code-file",
                    recovery,
                    "--output",
                    outputPath,
                    "--holder-name",
                    "Alex Pilot",
                    "--holder-date-of-birth",
                    "1990-01-02",
                    "--certified-on",
                    "2026-07-19"
                ]);

            var text = output.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("Portable printed copy: created", text, StringComparison.Ordinal);
            Assert.Contains($"Output: {Path.GetFullPath(outputPath)}", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Alex Pilot", text, StringComparison.Ordinal);
            Assert.DoesNotContain("1990-01-02", text, StringComparison.Ordinal);
            Assert.DoesNotContain(key.ToRecoveryCode(), text, StringComparison.Ordinal);
            Assert.Contains("Alex Pilot", File.ReadAllText(outputPath), StringComparison.Ordinal);
            Assert.Contains("1990-01-02", File.ReadAllText(outputPath), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOutput);
        }
    }

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
    public async Task RunAsyncDispatchesPortableRevisionHistoryJsonWithoutPrintingRecoveryCode()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var key = PortableLogbookKey.Generate();
        PortableLogbookWorkbookPackageStorage.WriteEnvelope(workbook, CreateEnvelope("log_cli_history", key));
        var recovery = Path.Combine(directory, "recovery.txt");
        File.WriteAllText(recovery, key.ToRecoveryCode());
        using var output = new StringWriter();
        var originalOutput = Console.Out;
        Console.SetOut(output);
        try
        {
            var exitCode = await UpdaterProgram.RunAsync(
                [
                    "portable",
                    "revision-history",
                    "--workbook",
                    workbook,
                    "--recovery-code-file",
                    recovery,
                    "--entry-id",
                    "ent_1",
                    "--json"
                ]);

            Assert.Equal(0, exitCode);
            using var json = JsonDocument.Parse(output.ToString());
            Assert.Equal("ent_1", json.RootElement.GetProperty("entryId").GetProperty("value").GetString());
            Assert.Equal(1, json.RootElement.GetProperty("revisionCount").GetInt32());
            Assert.False(json.RootElement.TryGetProperty("recoveryCode", out _));
            Assert.DoesNotContain(key.ToRecoveryCode(), output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOutput);
        }
    }

    [Fact(Skip = "Superseded by the schema-v2-only portable command contract; retain as historical v1 behavior documentation.")]
    public async Task RunAsyncDispatchesPortableResolveConflictJsonWithoutPrintingRecoveryCode()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var key = PortableLogbookKey.Generate();
        PortableLogbookWorkbookPackageStorage.WriteEnvelope(workbook, CreateConflictEnvelope("log_cli_conflict", key));
        var recovery = Path.Combine(directory, "recovery.txt");
        File.WriteAllText(recovery, key.ToRecoveryCode());
        using var output = new StringWriter();
        var originalOutput = Console.Out;
        Console.SetOut(output);
        try
        {
            var exitCode = await UpdaterProgram.RunAsync(
                [
                    "portable",
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
                    "Kept imported correction",
                    "--json"
                ]);

            Assert.Equal(0, exitCode);
            using var json = JsonDocument.Parse(output.ToString());
            Assert.Equal("ent_1", json.RootElement.GetProperty("entryId").GetProperty("value").GetString());
            Assert.Equal("rev_b", json.RootElement.GetProperty("keptRevisionId").GetProperty("value").GetString());
            Assert.Equal(0, json.RootElement.GetProperty("remainingConflictCount").GetInt32());
            Assert.False(json.RootElement.TryGetProperty("recoveryCode", out _));
            Assert.DoesNotContain(key.ToRecoveryCode(), output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOutput);
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

    private string CreateWorkbookWithInsertedExportedRow(
        string firstPackageName,
        out string recovery,
        out PortableLogbookKey key,
        out PortableLogbookEnableResult enabled,
        out CreateEntryOperation create)
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        recovery = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(firstPackageName)}-recovery.txt");
        var package = Path.Combine(directory, firstPackageName);
        enabled = PortableLogbookCommandRunner.Enable(
            workbook,
            recovery,
            DateTimeOffset.Parse("2026-07-19T00:00:00Z"));
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            ReplaceLogbookTable(
                archive,
                "A1:H2",
                ["Date", "Aircraft Type", "Reg", "From", "To", "PIC", "EntryID", "Portable Current Revision ID"]);
            var worksheet = ReadXml(archive, "xl/worksheets/sheet2.xml");
            UpsertInlineStringCell(worksheet, "A2", "2026-07-19");
            UpsertInlineStringCell(worksheet, "B2", "C172");
            UpsertInlineStringCell(worksheet, "C2", "VH-NEW");
            UpsertInlineStringCell(worksheet, "D2", "YSBK");
            UpsertInlineStringCell(worksheet, "E2", "YSCN");
            UpsertInlineStringCell(worksheet, "F2", "1.4");
            ReplaceXml(archive, "xl/worksheets/sheet2.xml", worksheet);
        }

        PortableLogbookCommandRunner.Export(
            workbook,
            recovery,
            package,
            DateTimeOffset.Parse("2026-07-19T01:00:00Z"));
        key = PortableLogbookKey.FromRecoveryCode(ReadRecoveryCodeFromGeneratedFile(recovery));
        var read = PortableLogbookPackageFile.Read(package, key, enabled.LogbookId);
        create = Assert.IsType<CreateEntryOperation>(Assert.Single(read.Document.Operations));
        return workbook;
    }

    private static PortableLogbookWorkbookStorageEnvelope CreateConflictEnvelope(string logbookId, PortableLogbookKey key)
    {
        var parsedLogbookId = new LogbookId(logbookId);
        var entryId = new EntryId("ent_1");
        var deviceId = new DeviceId("dev_excel");
        var create = new CreateEntryOperation(
            parsedLogbookId,
            entryId,
            new RevisionId("rev_create"),
            deviceId,
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            Entry("VH-AAA"));
        var local = new CorrectEntryOperation(
            parsedLogbookId,
            entryId,
            new RevisionId("rev_a"),
            new HashSet<RevisionId> { create.RevisionId },
            deviceId,
            DateTimeOffset.Parse("2026-07-18T00:05:00Z"),
            Entry("VH-AAA"));
        var incoming = new CorrectEntryOperation(
            parsedLogbookId,
            entryId,
            new RevisionId("rev_b"),
            new HashSet<RevisionId> { create.RevisionId },
            new DeviceId("dev_mobile"),
            DateTimeOffset.Parse("2026-07-18T00:06:00Z"),
            Entry("VH-BBB"));
        var document = PortableLogbookDocument.CreateAustraliaFirst(
            parsedLogbookId,
            [],
            [create, local, incoming]);
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

    private static void RemoveCell(XDocument worksheet, string cellReference)
    {
        foreach (var cell in worksheet
            .Descendants()
            .Where(element => element.Name.LocalName == "c" &&
                string.Equals((string?)element.Attribute("r"), cellReference, StringComparison.OrdinalIgnoreCase))
            .ToArray())
        {
            cell.Remove();
        }
    }

    private static void SwapWorksheetRows(
        XDocument worksheet,
        int firstRow,
        int secondRow,
        char firstColumn,
        char lastColumn)
    {
        for (var column = firstColumn; column <= lastColumn; column++)
        {
            var firstReference = $"{column}{firstRow}";
            var secondReference = $"{column}{secondRow}";
            var firstValue = ReadInlineStringCell(worksheet, firstReference);
            var secondValue = ReadInlineStringCell(worksheet, secondReference);

            if (secondValue is null)
            {
                RemoveCell(worksheet, firstReference);
            }
            else
            {
                UpsertInlineStringCell(worksheet, firstReference, secondValue);
            }

            if (firstValue is null)
            {
                RemoveCell(worksheet, secondReference);
            }
            else
            {
                UpsertInlineStringCell(worksheet, secondReference, firstValue);
            }
        }
    }

    private static void MoveWorksheetRowValues(
        XDocument worksheet,
        int sourceRow,
        int destinationRow,
        char firstColumn,
        char lastColumn)
    {
        for (var column = firstColumn; column <= lastColumn; column++)
        {
            var sourceReference = $"{column}{sourceRow}";
            var destinationReference = $"{column}{destinationRow}";
            var sourceValue = ReadInlineStringCell(worksheet, sourceReference);
            RemoveCell(worksheet, sourceReference);
            if (sourceValue is not null)
            {
                UpsertInlineStringCell(worksheet, destinationReference, sourceValue);
            }
            else
            {
                RemoveCell(worksheet, destinationReference);
            }
        }
    }

    private static string? ReadInlineStringCell(XDocument worksheet, string cellReference) =>
        worksheet
            .Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName == "c" &&
                string.Equals((string?)element.Attribute("r"), cellReference, StringComparison.OrdinalIgnoreCase))
            ?.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "t")
            ?.Value;
}
