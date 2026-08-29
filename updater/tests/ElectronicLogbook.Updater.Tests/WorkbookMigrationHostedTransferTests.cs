using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class WorkbookMigrationHostedTransferTests
{
    [Fact]
    public void ConfigurationCipherIsDeterministicAuthenticatedAndRoundTripsExactValues()
    {
        var migration = Migration();
        var payload = Payload(migration, rowCount: 1);
        var key = PortableLogbookKey.Generate();
        var revision = PortableHostedConfigurationRevision.Create(
            payload.Document,
            new RevisionId("rev_configuration_test"),
            migration.DeviceId,
            migration.StartedAt);

        var first = HostedConfigurationRevisionCipher.Encrypt(revision, key);
        var retry = HostedConfigurationRevisionCipher.Encrypt(revision, key);

        Assert.Equal(first, retry);
        Assert.DoesNotContain("Training category", first.PayloadCiphertext, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("flightReview", first.PayloadCiphertext, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(64, first.PayloadHash.Length);

        var envelope = Envelope(first, hostedRevision: 1);
        var decrypted = HostedConfigurationRevisionCipher.Decrypt(
            migration.LogbookId,
            envelope,
            key);

        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(
                revision,
                PortableLogbookJson.SerializerOptions),
            System.Text.Json.JsonSerializer.Serialize(
                decrypted,
                PortableLogbookJson.SerializerOptions));

        var tampered = envelope with { PayloadTag = Convert.ToBase64String(new byte[16]) };
        var error = Assert.Throws<HostedConfigurationRevisionCipherException>(() =>
            HostedConfigurationRevisionCipher.Decrypt(migration.LogbookId, tampered, key));
        Assert.Contains("authentication", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadAndVerifyAsync_RetryUsesIdenticalEncryptedPayloadsAndReadsEveryPage()
    {
        var migration = Migration();
        var payload = Payload(migration, rowCount: 201);
        var key = PortableLogbookKey.Generate();
        var hosted = new RecordingHostedMigrationStore();
        var transfer = new WorkbookMigrationHostedTransfer(hosted, hosted);

        var first = await transfer.UploadAndVerifyAsync(migration, payload, key);
        var retry = await transfer.UploadAndVerifyAsync(migration, payload, key);

        Assert.Equal(payload.Receipt, first.VerifiedReceipt);
        Assert.Equal(first, retry);
        Assert.Equal(201, first.UploadedOperationCount);
        Assert.Equal(201, hosted.OperationCount);
        Assert.Equal(1, hosted.ConfigurationCount);
        Assert.Equal(4, hosted.OperationReadCallCount);
        Assert.Equal(2, hosted.ConfigurationReadCallCount);
        Assert.Equal(2, hosted.OperationUploadAttempts.Count);
        Assert.Equal(2, hosted.ConfigurationUploadAttempts.Count);
        Assert.Equal(
            hosted.OperationUploadAttempts[0].Select(EnvelopeSignature),
            hosted.OperationUploadAttempts[1].Select(EnvelopeSignature));
        Assert.Equal(
            EnvelopeSignature(hosted.ConfigurationUploadAttempts[0]),
            EnvelopeSignature(hosted.ConfigurationUploadAttempts[1]));
    }

    [Fact]
    public async Task UploadAndVerifyAsync_ExtraHostedOperationFailsClosedBeforeCompletion()
    {
        var migration = Migration();
        var payload = Payload(migration, rowCount: 1);
        var hosted = new RecordingHostedMigrationStore
        {
            AddUnexpectedOperationDuringReadback = true
        };
        var transfer = new WorkbookMigrationHostedTransfer(hosted, hosted);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            transfer.UploadAndVerifyAsync(
                migration,
                payload,
                PortableLogbookKey.Generate()));

        Assert.Contains("operation set", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadAndVerifyAsync_ChangedHostedConfigurationFailsClosedBeforeCompletion()
    {
        var migration = Migration();
        var payload = Payload(migration, rowCount: 1);
        var hosted = new RecordingHostedMigrationStore
        {
            ChangeConfigurationDuringReadback = true
        };
        var transfer = new WorkbookMigrationHostedTransfer(hosted, hosted);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            transfer.UploadAndVerifyAsync(
                migration,
                payload,
                PortableLogbookKey.Generate()));

        Assert.Contains("configuration", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exactly match", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadAndVerifyAsync_ValidButChangedDecryptedFlightFailsExactReceiptCheck()
    {
        var migration = Migration();
        var payload = Payload(migration, rowCount: 1);
        var key = PortableLogbookKey.Generate();
        var changedOperation = payload.Document.Operations[0] with
        {
            Entry = payload.Document.Operations[0].Entry! with { SeCommandDay = 9.9m }
        };
        var changedUpload = HostedOperationCipher.Encrypt(changedOperation, key);
        var hosted = new RecordingHostedMigrationStore
        {
            OperationReadbackMutation = envelope => new HostedOperationEnvelope(
                envelope.HostedRevision,
                changedUpload.RevisionId,
                changedUpload.EntryId,
                changedUpload.DeviceId,
                changedUpload.CreatedAt,
                changedUpload.SchemaVersion,
                changedUpload.PayloadCiphertext,
                changedUpload.PayloadNonce,
                changedUpload.PayloadTag,
                changedUpload.PayloadHash,
                changedUpload.ParentRevisionIds)
        };
        var transfer = new WorkbookMigrationHostedTransfer(hosted, hosted);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            transfer.UploadAndVerifyAsync(migration, payload, key));

        Assert.Contains("flight values", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadAndVerifyAsync_MismatchedMigrationStopsBeforeAnyHostedWrite()
    {
        var migration = Migration();
        var payload = Payload(migration, rowCount: 1);
        var hosted = new RecordingHostedMigrationStore();
        var transfer = new WorkbookMigrationHostedTransfer(hosted, hosted);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            transfer.UploadAndVerifyAsync(
                migration with { SourceFingerprint = new string('b', 64) },
                payload,
                PortableLogbookKey.Generate()));

        Assert.Contains("confirmed hosted migration", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(hosted.OperationUploadAttempts);
        Assert.Empty(hosted.ConfigurationUploadAttempts);
    }

    private static WorkbookMigrationPayload Payload(
        HostedWorkbookMigration migration,
        int rowCount)
    {
        var customFields = PortableLogbookCustomFieldSet.CreateWorkbookCustomFields(
            ["Training category", "Training course", "Client", "Notes"]);
        var rows = Enumerable.Range(0, rowCount)
            .Select(index => new PortableLogbookWorkbookRowV2(
                null,
                null,
                PortableLogbookWorkbookEntry.Empty with
                {
                    Year = 2026,
                    Month = 8,
                    Day = 1 + (index % 28),
                    Type = "C172",
                    Reg = "VH-ABC",
                    From = "YSBK",
                    To = "YSCN",
                    SeCommandDay = 1.1m,
                    LandingsDay = 1,
                    CustomFields = new Dictionary<CustomFieldId, string?>
                    {
                        [customFields[0].Id] = "Line flight"
                    }
                }))
            .ToArray();
        return WorkbookMigrationPayloadConverter.ConvertRows(
            rows,
            customFields,
            new PortableLogbookCurrencyOverrideDates(
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 2),
                null),
            migration);
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

    private static HostedConfigurationRevisionEnvelope Envelope(
        HostedConfigurationRevisionUpload upload,
        long hostedRevision) =>
        new(
            hostedRevision,
            upload.RevisionId,
            upload.DeviceId,
            upload.CreatedAt,
            upload.SchemaVersion,
            upload.PayloadCiphertext,
            upload.PayloadNonce,
            upload.PayloadTag,
            upload.PayloadHash);

    private static string EnvelopeSignature(HostedOperationUpload operation) =>
        string.Join(
            "|",
            operation.RevisionId.Value,
            operation.PayloadCiphertext,
            operation.PayloadNonce,
            operation.PayloadTag,
            operation.PayloadHash);

    private static string EnvelopeSignature(HostedConfigurationRevisionUpload revision) =>
        string.Join(
            "|",
            revision.RevisionId.Value,
            revision.PayloadCiphertext,
            revision.PayloadNonce,
            revision.PayloadTag,
            revision.PayloadHash);

    private sealed class RecordingHostedMigrationStore :
        IHostedLogbookLedger,
        IWorkbookMigrationConfigurationClient
    {
        private readonly Dictionary<RevisionId, HostedOperationEnvelope> operations = [];
        private readonly Dictionary<RevisionId, HostedConfigurationRevisionEnvelope> configurations = [];

        public bool AddUnexpectedOperationDuringReadback { get; init; }
        public bool ChangeConfigurationDuringReadback { get; init; }
        public Func<HostedOperationEnvelope, HostedOperationEnvelope>? OperationReadbackMutation { get; init; }
        public int OperationReadCallCount { get; private set; }
        public int ConfigurationReadCallCount { get; private set; }
        public int OperationCount => operations.Count;
        public int ConfigurationCount => configurations.Count;
        public List<IReadOnlyList<HostedOperationUpload>> OperationUploadAttempts { get; } = [];
        public List<HostedConfigurationRevisionUpload> ConfigurationUploadAttempts { get; } = [];

        public ValueTask<HostedAppendResult> AppendOperationsAsync(
            LogbookId logbookId,
            DeviceId deviceId,
            IReadOnlyList<HostedOperationUpload> uploads,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OperationUploadAttempts.Add(uploads.ToArray());
            var accepted = new List<HostedOperationEnvelope>();
            foreach (var upload in uploads)
            {
                if (!operations.TryGetValue(upload.RevisionId, out var envelope))
                {
                    envelope = new HostedOperationEnvelope(
                        operations.Count + 1,
                        upload.RevisionId,
                        upload.EntryId,
                        upload.DeviceId,
                        upload.CreatedAt,
                        upload.SchemaVersion,
                        upload.PayloadCiphertext,
                        upload.PayloadNonce,
                        upload.PayloadTag,
                        upload.PayloadHash,
                        upload.ParentRevisionIds);
                    operations.Add(upload.RevisionId, envelope);
                }
                else if (WorkbookMigrationHostedTransferTests.EnvelopeSignature(upload) !=
                    EnvelopeSignature(envelope))
                {
                    throw new InvalidDataException("Operation replay changed its encrypted payload.");
                }
                accepted.Add(envelope);
            }

            return ValueTask.FromResult(new HostedAppendResult(
                accepted,
                operations.Count == 0 ? 0 : operations.Values.Max(operation => operation.HostedRevision)));
        }

        public ValueTask<HostedOperationPage> ReadMissingOperationsAsync(
            LogbookId logbookId,
            long afterHostedRevision,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OperationReadCallCount++;
            var available = operations.Values.OrderBy(operation => operation.HostedRevision).ToList();
            if (AddUnexpectedOperationDuringReadback && available.Count > 0)
            {
                var source = available[0];
                available.Add(source with
                {
                    HostedRevision = available.Max(operation => operation.HostedRevision) + 1,
                    RevisionId = new RevisionId("rev_unexpected")
                });
            }
            if (OperationReadbackMutation is not null)
            {
                available = available.Select(OperationReadbackMutation).ToList();
            }
            var page = available
                .Where(operation => operation.HostedRevision > afterHostedRevision)
                .Take(pageSize)
                .ToArray();
            var highest = available.Count == 0
                ? afterHostedRevision
                : available.Max(operation => operation.HostedRevision);
            var through = page.Length == 0 ? afterHostedRevision : page[^1].HostedRevision;
            return ValueTask.FromResult(new HostedOperationPage(
                page,
                through,
                through < highest));
        }

        public ValueTask RecordAcknowledgementAsync(
            LogbookId logbookId,
            DeviceId deviceId,
            long throughHostedRevision,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public Task<HostedConfigurationRevisionEnvelope> AppendWorkbookConfigurationRevisionAsync(
            HostedWorkbookMigration migration,
            HostedConfigurationRevisionUpload revision,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConfigurationUploadAttempts.Add(revision);
            if (!configurations.TryGetValue(revision.RevisionId, out var envelope))
            {
                envelope = WorkbookMigrationHostedTransferTests.Envelope(
                    revision,
                    configurations.Count + 1);
                configurations.Add(revision.RevisionId, envelope);
            }
            else if (WorkbookMigrationHostedTransferTests.EnvelopeSignature(revision) !=
                EnvelopeSignature(envelope))
            {
                throw new InvalidDataException("Configuration replay changed its encrypted payload.");
            }
            return Task.FromResult(envelope);
        }

        public Task<HostedConfigurationRevisionPage> ReadWorkbookConfigurationRevisionsAsync(
            HostedWorkbookMigration migration,
            long afterHostedRevision = 0,
            int pageSize = IHostedLogbookLedger.MaxOperationPageSize,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConfigurationReadCallCount++;
            var available = configurations.Values
                .OrderBy(revision => revision.HostedRevision)
                .Select(revision => ChangeConfigurationDuringReadback
                    ? revision with { PayloadHash = new string('f', 64) }
                    : revision)
                .ToArray();
            var page = available
                .Where(revision => revision.HostedRevision > afterHostedRevision)
                .Take(pageSize)
                .ToArray();
            var highest = available.Length == 0
                ? afterHostedRevision
                : available.Max(revision => revision.HostedRevision);
            var through = page.Length == 0 ? afterHostedRevision : page[^1].HostedRevision;
            return Task.FromResult(new HostedConfigurationRevisionPage(
                page,
                through,
                through < highest));
        }

        private static string EnvelopeSignature(HostedOperationEnvelope operation) =>
            string.Join(
                "|",
                operation.RevisionId.Value,
                operation.PayloadCiphertext,
                operation.PayloadNonce,
                operation.PayloadTag,
                operation.PayloadHash);

        private static string EnvelopeSignature(HostedConfigurationRevisionEnvelope revision) =>
            string.Join(
                "|",
                revision.RevisionId.Value,
                revision.PayloadCiphertext,
                revision.PayloadNonce,
                revision.PayloadTag,
                revision.PayloadHash);
    }
}
