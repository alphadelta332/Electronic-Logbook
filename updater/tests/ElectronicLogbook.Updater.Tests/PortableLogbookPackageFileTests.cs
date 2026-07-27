using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using ElectronicLogbook.Portable;
using ElectronicLogbook.Updater;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookPackageFileTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(Path.GetTempPath(), "elogbook-package-file-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ReadV2ReadsWorkbookFaithfulPackage()
    {
        Directory.CreateDirectory(tempDirectory);
        var path = Path.Combine(tempDirectory, "v2-export.elogbook");
        var key = PortableLogbookKey.Generate();
        var logbookId = new LogbookId("log_v2_package_file");
        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(
            logbookId,
            [],
            PortableLogbookCurrencyOverrideDates.Empty,
            [PortableLogbookOperationV2.Create(
                logbookId,
                new EntryId("ent_v2_package_file"),
                new RevisionId("rev_v2_package_file"),
                new DeviceId("device_v2_package_file"),
                new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero),
                PortableLogbookWorkbookEntry.Empty with { Year = 2026, Month = 7, Day = 27, Reg = "VH-V2" })]);
        File.WriteAllBytes(path, PortableLogbookPackage.Write(document, key));

        var result = PortableLogbookPackageFile.ReadV2(path, key, logbookId);

        Assert.Equal(document.SchemaVersion, result.Document.SchemaVersion);
        Assert.Equal(document.LogbookId, result.Document.LogbookId);
        Assert.Equal(document.CurrencyOverrideDates, result.Document.CurrencyOverrideDates);
        Assert.Single(result.Document.Operations);
        Assert.Equal(document.Operations[0].EntryId, result.Document.Operations[0].EntryId);
        Assert.Equal(document.Operations[0].RevisionId, result.Document.Operations[0].RevisionId);
        var expectedEntry = document.Operations[0].Entry;
        var actualEntry = result.Document.Operations[0].Entry;
        Assert.NotNull(expectedEntry);
        Assert.NotNull(actualEntry);
        Assert.Equal(expectedEntry!.Date, actualEntry!.Date);
        Assert.Equal(expectedEntry.Reg, actualEntry.Reg);
    }

    [Fact]
    public void V2CommandPathEnablesExportsAndRecordsDuplicatePackage()
    {
        Directory.CreateDirectory(tempDirectory);
        var workbook = TestRepo.CreateMinimalWorkbookPackage(tempDirectory, TestRepo.Version);
        var recoveryPath = Path.Combine(tempDirectory, "recovery.txt");
        var packagePath = Path.Combine(tempDirectory, "export.elogbook");
        var enabled = PortableLogbookCommandRunner.Enable(
            workbook,
            recoveryPath,
            new DateTimeOffset(2026, 7, 27, 1, 0, 0, TimeSpan.Zero));
        var recoveryCode = File.ReadLines(recoveryPath)
            .Single(line => line.StartsWith("Recovery code:", StringComparison.OrdinalIgnoreCase))
            .Split(':', 2)[1]
            .Trim();
        var key = PortableLogbookKey.FromRecoveryCode(recoveryCode);

        var export = PortableLogbookCommandRunner.Export(
            workbook,
            recoveryPath,
            packagePath,
            new DateTimeOffset(2026, 7, 27, 1, 5, 0, TimeSpan.Zero));
        var stored = PortableLogbookWorkbookPackageStorage.OpenStateV2(workbook, key);
        var package = PortableLogbookPackageFile.ReadV2(packagePath, key, enabled.LogbookId);
        var preview = PortableLogbookCommandRunner.PreviewImport(workbook, recoveryPath, packagePath);
        var applied = PortableLogbookCommandRunner.ApplyImport(
            workbook,
            recoveryPath,
            packagePath,
            new DateTimeOffset(2026, 7, 27, 1, 10, 0, TimeSpan.Zero));

        Assert.NotNull(stored);
        Assert.Equal(PortableLogbookDocumentV2.CurrentSchemaVersion, enabled.SchemaVersion);
        Assert.Equal(PortableLogbookDocumentV2.CurrentSchemaVersion, export.SchemaVersion);
        Assert.Equal(PortableLogbookDocumentV2.CurrentSchemaVersion, package.Document.SchemaVersion);
        Assert.Equal("duplicateOnly", preview.Status);
        Assert.Equal("duplicateOperationsRecorded", applied.Status);
        Assert.True(applied.ReceiptRecorded);
        Assert.True(applied.StorageUpdated);
    }

    [Fact]
    public void V2ImportAppliesCompatibleCustomFieldDefinition()
    {
        Directory.CreateDirectory(tempDirectory);
        var workbook = TestRepo.CreateMinimalWorkbookPackage(tempDirectory, TestRepo.Version);
        var recoveryPath = Path.Combine(tempDirectory, "recovery.txt");
        var incomingPath = Path.Combine(tempDirectory, "incoming.elogbook");
        var enabled = PortableLogbookCommandRunner.Enable(
            workbook,
            recoveryPath,
            new DateTimeOffset(2026, 7, 27, 1, 0, 0, TimeSpan.Zero));
        var key = PortableLogbookKey.FromRecoveryCode(
            File.ReadLines(recoveryPath)
                .Single(line => line.StartsWith("Recovery code:", StringComparison.OrdinalIgnoreCase))
                .Split(':', 2)[1]
                .Trim());
        var customField = new CustomFieldDefinition(new CustomFieldId("cf_v2_role"), "Role", 1);
        var incoming = PortableLogbookDocumentV2.CreateAustraliaFirst(
            enabled.LogbookId,
            [customField],
            PortableLogbookCurrencyOverrideDates.Empty,
            [PortableLogbookOperationV2.Create(
                enabled.LogbookId,
                new EntryId("ent_v2_custom_field"),
                new RevisionId("rev_v2_custom_field"),
                new DeviceId("dev_v2_custom_field"),
                new DateTimeOffset(2026, 7, 27, 1, 5, 0, TimeSpan.Zero),
                PortableLogbookWorkbookEntry.Empty with
                {
                    Year = 2026,
                    Month = 7,
                    Day = 27,
                    Reg = "VH-CF",
                    CustomFields = new Dictionary<CustomFieldId, string?> { [customField.Id] = "Captain" }
                })]);
        File.WriteAllBytes(incomingPath, PortableLogbookPackage.Write(incoming, key));

        var preview = PortableLogbookCommandRunner.PreviewImport(workbook, recoveryPath, incomingPath);
        var applied = PortableLogbookCommandRunner.ApplyImport(
            workbook,
            recoveryPath,
            incomingPath,
            new DateTimeOffset(2026, 7, 27, 1, 10, 0, TimeSpan.Zero));
        var state = PortableLogbookWorkbookPackageStorage.OpenStateV2(workbook, key);

        Assert.Equal("readyToApply", preview.Status);
        Assert.Equal("applied", applied.Status);
        Assert.NotNull(state);
        Assert.Equal([customField], state.Document.CustomFieldDefinitions);
        Assert.Equal("Captain", state.Document.Operations.Single().Entry!.CustomFields[customField.Id]);
    }

    [Fact]
    public void EnableResetsUnreleasedV1EnvelopeFromBackupAsV2()
    {
        Directory.CreateDirectory(tempDirectory);
        var workbook = TestRepo.CreateMinimalWorkbookPackage(tempDirectory, TestRepo.Version, "legacy.xlsm");
        var legacyKey = PortableLogbookKey.Generate();
        var legacyDocument = PortableLogbookDocument.CreateAustraliaFirst(new LogbookId("log_unreleased_v1"), [], []);
        PortableLogbookWorkbookPackageStorage.WriteEnvelope(
            workbook,
            PortableLogbookWorkbookStorage.CreateEnvelope(
                legacyDocument,
                PortableLogbookPackage.Write(legacyDocument, legacyKey),
                []));
        var visibleRowsBeforeReset = PortableLogbookWorkbookPackageStorage.ReadCurrentRowsV2(workbook);
        var recoveryPath = Path.Combine(tempDirectory, "replacement-recovery.txt");

        var enabled = PortableLogbookCommandRunner.Enable(
            workbook,
            recoveryPath,
            new DateTimeOffset(2026, 7, 27, 2, 0, 0, TimeSpan.Zero));
        var replacementKey = PortableLogbookKey.FromRecoveryCode(
            File.ReadLines(recoveryPath)
                .Single(line => line.StartsWith("Recovery code:", StringComparison.OrdinalIgnoreCase))
                .Split(':', 2)[1]
                .Trim());
        var state = PortableLogbookWorkbookPackageStorage.OpenStateV2(workbook, replacementKey);
        var retainedLegacyState = PortableLogbookWorkbookPackageStorage.OpenState(enabled.BackupPath, legacyKey);
        var backupEnvelope = PortableLogbookWorkbookPackageStorage.ReadEnvelope(enabled.BackupPath);
        var visibleRowsAfterReset = PortableLogbookWorkbookPackageStorage.ReadCurrentRowsV2(workbook);

        Assert.True(File.Exists(enabled.BackupPath));
        Assert.NotEqual(legacyDocument.LogbookId, enabled.LogbookId);
        Assert.NotNull(backupEnvelope);
        Assert.Equal(PortableLogbookDocument.CurrentSchemaVersion, backupEnvelope.SchemaVersion);
        Assert.NotNull(retainedLegacyState);
        Assert.Equal(legacyDocument.LogbookId, retainedLegacyState.Document.LogbookId);
        Assert.Equal(legacyDocument.SchemaVersion, retainedLegacyState.Document.SchemaVersion);
        Assert.Equal(legacyDocument.Operations, retainedLegacyState.Document.Operations);
        Assert.Equal(visibleRowsBeforeReset.Select(row => row.Entry), visibleRowsAfterReset.Select(row => row.Entry));
        Assert.NotNull(state);
        Assert.Equal(PortableLogbookDocumentV2.CurrentSchemaVersion, state.Document.SchemaVersion);
    }

    [Fact]
    public void WriteAndReadRoundTripElogbookFile()
    {
        var document = CreateDocument();
        var key = PortableLogbookKey.Generate();
        var path = Path.Combine(tempDirectory, "export.elogbook");

        PortableLogbookPackageFile.Write(path, document, key);
        var result = PortableLogbookPackageFile.Read(path, key, document.LogbookId);

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Equal(document.LogbookId, result.Document.LogbookId);
    }

    [Fact]
    public void ReadManifestReturnsFilePackageMetadataWithoutKey()
    {
        var document = CreateDocument();
        var path = Path.Combine(tempDirectory, "export.elogbook");

        PortableLogbookPackageFile.Write(path, document, PortableLogbookKey.Generate());
        var manifest = PortableLogbookPackageFile.ReadManifest(path);

        Assert.Equal(document.LogbookId, manifest.LogbookId);
        Assert.Equal(document.Operations.Count, manifest.OperationCount);
    }

    [Fact]
    public void WriteDeletesTempFileWhenFinalMoveFails()
    {
        var path = Path.Combine(tempDirectory, "blocked.elogbook");
        Directory.CreateDirectory(path);

        Assert.ThrowsAny<Exception>(() =>
            PortableLogbookPackageFile.Write(path, CreateDocument(), PortableLogbookKey.Generate()));

        Assert.True(Directory.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void ReadManifestForInspectionReturnsUnsupportedSchemaMetadataWithoutKey()
    {
        var document = CreateDocument();
        var key = PortableLogbookKey.Generate();
        var packageBytes = PortableLogbookPackage.Write(document, key);
        var manifest = PortableLogbookPackage.ReadManifest(packageBytes) with
        {
            SchemaVersion = PortableLogbookDocument.CurrentSchemaVersion + 1
        };
        var path = Path.Combine(tempDirectory, "future.elogbook");
        Directory.CreateDirectory(tempDirectory);
        File.WriteAllBytes(path, ReplaceManifest(packageBytes, manifest));

        var inspected = PortableLogbookPackageFile.ReadManifestForInspection(path);
        var strictError = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackageFile.ReadManifest(path));

        Assert.Equal(manifest.SchemaVersion, inspected.SchemaVersion);
        Assert.Equal(PortableLogbookPackageError.UnsupportedSchemaVersion, strictError.Error);
    }

    [Theory]
    [InlineData("export.zip")]
    [InlineData("export")]
    public void WriteRejectsNonElogbookExtension(string fileName)
    {
        var path = Path.Combine(tempDirectory, fileName);

        var exception = Assert.Throws<ArgumentException>(
            () => PortableLogbookPackageFile.Write(path, CreateDocument(), PortableLogbookKey.Generate()));

        Assert.Equal("path", exception.ParamName);
    }

    [Fact]
    public void ReadRejectsOversizedFileBeforePackageParsing()
    {
        Directory.CreateDirectory(tempDirectory);
        var path = Path.Combine(tempDirectory, "oversized.elogbook");
        File.WriteAllBytes(path, new byte[128]);

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackageFile.Read(
                path,
                PortableLogbookKey.Generate(),
                expectedLogbookId: null,
                new PortableLogbookPackageReadOptions(127)));

        Assert.Equal(PortableLogbookPackageError.PackageTooLarge, exception.Error);
    }

    [Fact]
    public void ReadManifestRejectsOversizedFileBeforePackageParsing()
    {
        Directory.CreateDirectory(tempDirectory);
        var path = Path.Combine(tempDirectory, "oversized.elogbook");
        File.WriteAllBytes(path, new byte[128]);

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackageFile.ReadManifest(path, new PortableLogbookPackageReadOptions(127)));

        Assert.Equal(PortableLogbookPackageError.PackageTooLarge, exception.Error);
    }

    [Fact]
    public void ReadManifestForInspectionRejectsOversizedFileBeforePackageParsing()
    {
        Directory.CreateDirectory(tempDirectory);
        var path = Path.Combine(tempDirectory, "oversized.elogbook");
        File.WriteAllBytes(path, new byte[128]);

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackageFile.ReadManifestForInspection(path, new PortableLogbookPackageReadOptions(127)));

        Assert.Equal(PortableLogbookPackageError.PackageTooLarge, exception.Error);
    }

    [Fact]
    public void ReadBytesRejectsOversizedFileBeforeReturningContent()
    {
        Directory.CreateDirectory(tempDirectory);
        var path = Path.Combine(tempDirectory, "oversized.elogbook");
        File.WriteAllBytes(path, new byte[128]);

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackageFile.ReadBytes(path, new PortableLogbookPackageReadOptions(127)));

        Assert.Equal(PortableLogbookPackageError.PackageTooLarge, exception.Error);
    }

    [Fact]
    public void ReadRejectsEmptyFileBeforePackageParsing()
    {
        Directory.CreateDirectory(tempDirectory);
        var path = Path.Combine(tempDirectory, "empty.elogbook");
        File.WriteAllBytes(path, []);

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackageFile.Read(path, PortableLogbookKey.Generate()));

        Assert.Equal(PortableLogbookPackageError.PackageEmpty, exception.Error);
    }

    [Fact]
    public void ReadManifestRejectsEmptyFileBeforePackageParsing()
    {
        Directory.CreateDirectory(tempDirectory);
        var path = Path.Combine(tempDirectory, "empty.elogbook");
        File.WriteAllBytes(path, []);

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackageFile.ReadManifest(path));

        Assert.Equal(PortableLogbookPackageError.PackageEmpty, exception.Error);
    }

    [Fact]
    public void ReadManifestForInspectionRejectsEmptyFileBeforePackageParsing()
    {
        Directory.CreateDirectory(tempDirectory);
        var path = Path.Combine(tempDirectory, "empty.elogbook");
        File.WriteAllBytes(path, []);

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackageFile.ReadManifestForInspection(path));

        Assert.Equal(PortableLogbookPackageError.PackageEmpty, exception.Error);
    }

    [Fact]
    public void ReadManifestRejectsNonElogbookExtension()
    {
        var path = Path.Combine(tempDirectory, "export.zip");

        var exception = Assert.Throws<ArgumentException>(() => PortableLogbookPackageFile.ReadManifest(path));

        Assert.Equal("path", exception.ParamName);
    }

    [Fact]
    public void ReadManifestForInspectionRejectsNonElogbookExtension()
    {
        var path = Path.Combine(tempDirectory, "export.zip");

        var exception = Assert.Throws<ArgumentException>(() => PortableLogbookPackageFile.ReadManifestForInspection(path));

        Assert.Equal("path", exception.ParamName);
    }

    [Fact]
    public void ReadPropagatesMissingPackageFileAsFileNotFound()
    {
        var path = Path.Combine(tempDirectory, "missing.elogbook");

        Assert.Throws<FileNotFoundException>(() => PortableLogbookPackageFile.Read(path, PortableLogbookKey.Generate()));
    }

    private static PortableLogbookDocument CreateDocument()
    {
        var create = new CreateEntryOperation(
            new LogbookId("log_file"),
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

        return PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [create]);
    }

    private static byte[] ReplaceManifest(
        byte[] packageBytes,
        PortableLogbookPackageManifest manifest)
    {
        var newManifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, PortableLogbookJson.SerializerOptions);
        var originalManifestLength = BinaryPrimitives.ReadInt32LittleEndian(packageBytes.AsSpan("ELOGPKG1".Length, sizeof(int)));
        var remainderStart = "ELOGPKG1".Length + sizeof(int) + originalManifestLength;
        using var output = new MemoryStream();
        output.Write(Encoding.ASCII.GetBytes("ELOGPKG1"));
        Span<byte> manifestLength = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(manifestLength, newManifestBytes.Length);
        output.Write(manifestLength);
        output.Write(newManifestBytes);
        output.Write(packageBytes.AsSpan(remainderStart));
        return output.ToArray();
    }
}
