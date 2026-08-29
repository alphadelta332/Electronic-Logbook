using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class WorkbookMigrationPayloadConverterTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"WorkbookMigrationPayloadConverterTests-{Guid.NewGuid():N}");

    public WorkbookMigrationPayloadConverterTests()
    {
        Directory.CreateDirectory(directory);
    }

    [Fact]
    public void ConvertRowsRetryProducesIdenticalOperationsCiphertextAndExactReceipt()
    {
        var migration = Migration();
        var customFields = PortableLogbookCustomFieldSet.CreateWorkbookCustomFields(
            ["Operation", "Training course", "Client", "Notes"]);
        var rows = new[]
        {
            new PortableLogbookWorkbookRowV2(
                null,
                null,
                Flight(
                    new DateOnly(2026, 7, 1),
                    1.2m,
                    0.3m,
                    new Dictionary<CustomFieldId, string?>
                    {
                        [customFields[1].Id] = "Night exercise",
                        [customFields[0].Id] = "Line flight"
                    })),
            new PortableLogbookWorkbookRowV2(
                new EntryId("ent_ignored_source_id"),
                new RevisionId("rev_ignored_source_revision"),
                Flight(
                    new DateOnly(2026, 7, 2),
                    0.8m,
                    null,
                    new Dictionary<CustomFieldId, string?>
                    {
                        [customFields[0].Id] = "Check"
                    }) with
                {
                    LandingsDay = 2,
                    LandingsNight = 1,
                    Ils = 1,
                    Rnp = 2,
                    Circling = 1
                })
        };
        var overrides = new PortableLogbookCurrencyOverrideDates(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 2),
            new DateOnly(2026, 6, 3));

        var first = WorkbookMigrationPayloadConverter.ConvertRows(
            rows,
            customFields,
            overrides,
            migration);
        var retry = WorkbookMigrationPayloadConverter.ConvertRows(
            rows,
            customFields,
            overrides,
            migration with
            {
                Status = HostedWorkbookMigrationStatus.Completed,
                ExpectedOperationCount = rows.Length,
                VerifiedOperationCount = rows.Length,
                VerificationReceiptHash = first.Receipt.VerificationReceiptSha256
            });

        Assert.Equal(
            PortableLogbookJson.SerializeV2(first.Document),
            PortableLogbookJson.SerializeV2(retry.Document));
        Assert.Equal(first.Receipt, retry.Receipt);
        Assert.Equal(2, first.Document.Operations.Count);
        Assert.Equal(2, first.Document.Operations.Select(operation => operation.EntryId).Distinct().Count());
        Assert.All(first.Document.Operations, operation =>
        {
            Assert.StartsWith("ent_", operation.EntryId.Value, StringComparison.Ordinal);
            Assert.StartsWith("rev_", operation.RevisionId.Value, StringComparison.Ordinal);
            Assert.Equal(migration.LogbookId, operation.LogbookId);
            Assert.Equal(migration.DeviceId, operation.DeviceId);
        });
        Assert.Equal(migration.StartedAt, first.Document.Operations[0].CreatedAt);
        Assert.Equal(migration.StartedAt.AddMilliseconds(1), first.Document.Operations[1].CreatedAt);
        Assert.Equal(
            new PortableWorkbookMigrationTotals(2, 2.0m, 0.3m, 2.3m, 3, 1, 3, 1),
            first.Receipt.CalculatedTotals);
        Assert.Equal(64, first.Receipt.EntryValuesSha256.Length);
        Assert.Equal(64, first.Receipt.CustomFieldDefinitionsSha256.Length);
        Assert.Equal(64, first.Receipt.CurrencyOverrideDatesSha256.Length);
        Assert.Equal(64, first.Receipt.DocumentSha256.Length);
        Assert.Equal(64, first.Receipt.VerificationReceiptSha256.Length);

        var key = PortableLogbookKey.Generate();
        var firstEncrypted = HostedOperationCipher.Encrypt(first.Document.Operations[0], key);
        var retryEncrypted = HostedOperationCipher.Encrypt(retry.Document.Operations[0], key);
        Assert.Equal(firstEncrypted.PayloadCiphertext, retryEncrypted.PayloadCiphertext);
        Assert.Equal(firstEncrypted.PayloadNonce, retryEncrypted.PayloadNonce);
        Assert.Equal(firstEncrypted.PayloadTag, retryEncrypted.PayloadTag);
        Assert.Equal(firstEncrypted.PayloadHash, retryEncrypted.PayloadHash);

        var envelope = new HostedOperationEnvelope(
            HostedRevision: 1,
            firstEncrypted.RevisionId,
            firstEncrypted.EntryId,
            firstEncrypted.DeviceId,
            firstEncrypted.CreatedAt,
            firstEncrypted.SchemaVersion,
            firstEncrypted.PayloadCiphertext,
            firstEncrypted.PayloadNonce,
            firstEncrypted.PayloadTag,
            firstEncrypted.PayloadHash,
            firstEncrypted.ParentRevisionIds);
        Assert.Equal(
            PortableLogbookJson.SerializeOperationV2(first.Document.Operations[0]),
            PortableLogbookJson.SerializeOperationV2(HostedOperationCipher.Decrypt(envelope, key)));
        PortableWorkbookMigrationVerification.VerifyExact(first.Receipt, retry.Document);
    }

    [Fact]
    public void VerifyExactRejectsChangedEntryConfigurationAndTotals()
    {
        var migration = Migration();
        var customFields = PortableLogbookCustomFieldSet.CreateWorkbookCustomFields(
            ["One", "Two", "Three", "Four"]);
        var converted = WorkbookMigrationPayloadConverter.ConvertRows(
            [new PortableLogbookWorkbookRowV2(null, null, Flight(new DateOnly(2026, 7, 1), 1.2m, 0.3m))],
            customFields,
            PortableLogbookCurrencyOverrideDates.Empty,
            migration);
        var changedOperation = converted.Document.Operations[0] with
        {
            Entry = converted.Document.Operations[0].Entry! with { SeCommandDay = 9.9m }
        };
        var changedEntry = converted.Document with { Operations = [changedOperation] };
        var changedFields = converted.Document with
        {
            CustomFieldDefinitions = customFields
                .Select((field, index) => index == 0 ? field with { Label = "Changed" } : field)
                .ToArray()
        };
        var changedOverrides = converted.Document with
        {
            CurrencyOverrideDates = new PortableLogbookCurrencyOverrideDates(
                new DateOnly(2026, 7, 5),
                null,
                null)
        };

        var entryError = Assert.Throws<InvalidDataException>(() =>
            PortableWorkbookMigrationVerification.VerifyExact(converted.Receipt, changedEntry));
        var fieldError = Assert.Throws<InvalidDataException>(() =>
            PortableWorkbookMigrationVerification.VerifyExact(converted.Receipt, changedFields));
        var overrideError = Assert.Throws<InvalidDataException>(() =>
            PortableWorkbookMigrationVerification.VerifyExact(converted.Receipt, changedOverrides));

        Assert.Contains("flight values", entryError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("custom fields", fieldError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("override dates", overrideError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConvertWorkbookReadsPackageAndRejectsEmptyWorkbookClearly()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, "3.0.0");

        var error = Assert.Throws<InvalidOperationException>(() =>
            WorkbookMigrationPayloadConverter.ConvertWorkbook(workbook, Migration()));

        Assert.Contains("does not contain any flights", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConvertRowsRejectsMigrationThatCannotBeVerifiedOrUploaded()
    {
        var migration = Migration() with { Status = HostedWorkbookMigrationStatus.Failed };

        var error = Assert.Throws<InvalidOperationException>(() =>
            WorkbookMigrationPayloadConverter.ConvertRows(
                [new PortableLogbookWorkbookRowV2(null, null, Flight(new DateOnly(2026, 7, 1), 1m, null))],
                [],
                PortableLogbookCurrencyOverrideDates.Empty,
                migration));

        Assert.Contains("pending or completed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static HostedWorkbookMigration Migration() =>
        new(
            new WorkbookMigrationId("mig_001"),
            new HostedAccountId("acct_001"),
            new LogbookId("log_001"),
            new DeviceId("dev_workbook"),
            new string('a', 64),
            HostedWorkbookMigrationStatus.Pending,
            AttemptCount: 1,
            ExpectedOperationCount: null,
            VerifiedOperationCount: null,
            VerificationReceiptHash: null,
            FailureCode: null,
            DateTimeOffset.Parse("2026-08-29T01:02:03Z"),
            DateTimeOffset.Parse("2026-08-29T01:02:03Z"),
            CompletedAt: null,
            FailedAt: null);

    private static PortableLogbookWorkbookEntry Flight(
        DateOnly date,
        decimal flightHours,
        decimal? simulatorHours,
        IReadOnlyDictionary<CustomFieldId, string?>? customFields = null) =>
        PortableLogbookWorkbookEntry.Empty with
        {
            Year = date.Year,
            Month = date.Month,
            Day = date.Day,
            Type = "C172",
            Reg = "VH-ABC",
            From = "YSBK",
            To = "YSCN",
            SeCommandDay = flightHours,
            IfrSim = simulatorHours,
            LandingsDay = 1,
            CustomFields = customFields ?? new Dictionary<CustomFieldId, string?>()
        };
}
