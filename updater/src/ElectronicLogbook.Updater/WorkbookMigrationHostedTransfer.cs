using System.Security.Cryptography;
using System.Text;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater;

public interface IWorkbookMigrationConfigurationClient
{
    Task<HostedConfigurationRevisionEnvelope> AppendWorkbookConfigurationRevisionAsync(
        HostedWorkbookMigration migration,
        HostedConfigurationRevisionUpload revision,
        CancellationToken cancellationToken = default);

    Task<HostedConfigurationRevisionPage> ReadWorkbookConfigurationRevisionsAsync(
        HostedWorkbookMigration migration,
        long afterHostedRevision = 0,
        int pageSize = IHostedLogbookLedger.MaxOperationPageSize,
        CancellationToken cancellationToken = default);
}

public sealed record WorkbookMigrationHostedTransferResult(
    PortableWorkbookMigrationReceipt VerifiedReceipt,
    int UploadedOperationCount,
    long ThroughHostedOperationRevision,
    long ThroughHostedConfigurationRevision);

public sealed class WorkbookMigrationHostedTransfer
{
    private readonly IHostedLogbookLedger operationLedger;
    private readonly IWorkbookMigrationConfigurationClient configurationClient;

    public WorkbookMigrationHostedTransfer(
        IHostedLogbookLedger operationLedger,
        IWorkbookMigrationConfigurationClient configurationClient)
    {
        ArgumentNullException.ThrowIfNull(operationLedger);
        ArgumentNullException.ThrowIfNull(configurationClient);
        this.operationLedger = operationLedger;
        this.configurationClient = configurationClient;
    }

    public async Task<WorkbookMigrationHostedTransferResult> UploadAndVerifyAsync(
        HostedWorkbookMigration migration,
        WorkbookMigrationPayload payload,
        PortableLogbookKey logbookKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(migration);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(logbookKey);
        ValidateScope(migration, payload);

        var encryptedOperations = payload.Document.Operations
            .Select(operation => HostedOperationCipher.Encrypt(operation, logbookKey))
            .ToArray();
        var configurationRevision = PortableHostedConfigurationRevision.Create(
            payload.Document,
            CreateConfigurationRevisionId(migration, payload.Receipt),
            migration.DeviceId,
            migration.StartedAt);
        var encryptedConfiguration = HostedConfigurationRevisionCipher.Encrypt(
            configurationRevision,
            logbookKey);

        var appendedOperations = await operationLedger.AppendOperationsAsync(
            migration.LogbookId,
            migration.DeviceId,
            encryptedOperations,
            cancellationToken);
        _ = await configurationClient.AppendWorkbookConfigurationRevisionAsync(
            migration,
            encryptedConfiguration,
            cancellationToken);

        var hostedOperations = await ReadAllOperationsAsync(
            migration.LogbookId,
            cancellationToken);
        EnsureExactOperationSet(encryptedOperations, hostedOperations);
        var hostedConfigurations = await ReadAllConfigurationsAsync(
            migration,
            cancellationToken);
        var hostedConfiguration = EnsureExactConfiguration(
            encryptedConfiguration,
            hostedConfigurations);

        var decryptedOperations = hostedOperations
            .OrderBy(operation => operation.HostedRevision)
            .Select(operation => HostedOperationCipher.Decrypt(operation, logbookKey))
            .ToArray();
        var decryptedConfiguration = HostedConfigurationRevisionCipher.Decrypt(
            migration.LogbookId,
            hostedConfiguration,
            logbookKey);
        var readback = PortableLogbookDocumentV2.CreateAustraliaFirst(
            migration.LogbookId,
            decryptedConfiguration.CustomFieldDefinitions,
            decryptedConfiguration.CurrencyOverrideDates,
            decryptedOperations);
        PortableWorkbookMigrationVerification.VerifyExact(payload.Receipt, readback);

        return new WorkbookMigrationHostedTransferResult(
            payload.Receipt,
            encryptedOperations.Length,
            Math.Max(
                appendedOperations.ThroughHostedRevision,
                hostedOperations.Count == 0 ? 0 : hostedOperations.Max(operation => operation.HostedRevision)),
            hostedConfigurations.Max(configuration => configuration.HostedRevision));
    }

    private async Task<IReadOnlyList<HostedOperationEnvelope>> ReadAllOperationsAsync(
        LogbookId logbookId,
        CancellationToken cancellationToken)
    {
        var operations = new List<HostedOperationEnvelope>();
        var afterRevision = 0L;
        while (true)
        {
            var page = await operationLedger.ReadMissingOperationsAsync(
                logbookId,
                afterRevision,
                IHostedLogbookLedger.MaxOperationPageSize,
                cancellationToken);
            operations.AddRange(page.Operations);
            if (!page.HasMore)
            {
                return operations;
            }
            if (page.ThroughHostedRevision <= afterRevision)
            {
                throw new InvalidDataException(
                    "Hosted flight-operation readback did not advance to the next page.");
            }
            afterRevision = page.ThroughHostedRevision;
        }
    }

    private async Task<IReadOnlyList<HostedConfigurationRevisionEnvelope>> ReadAllConfigurationsAsync(
        HostedWorkbookMigration migration,
        CancellationToken cancellationToken)
    {
        var revisions = new List<HostedConfigurationRevisionEnvelope>();
        var afterRevision = 0L;
        while (true)
        {
            var page = await configurationClient.ReadWorkbookConfigurationRevisionsAsync(
                migration,
                afterRevision,
                IHostedLogbookLedger.MaxOperationPageSize,
                cancellationToken);
            revisions.AddRange(page.Revisions);
            if (!page.HasMore)
            {
                return revisions;
            }
            if (page.ThroughHostedRevision <= afterRevision)
            {
                throw new InvalidDataException(
                    "Hosted configuration readback did not advance to the next page.");
            }
            afterRevision = page.ThroughHostedRevision;
        }
    }

    private static void ValidateScope(
        HostedWorkbookMigration migration,
        WorkbookMigrationPayload payload)
    {
        if (migration.Status != HostedWorkbookMigrationStatus.Pending)
        {
            throw new InvalidOperationException(
                "Workbook data can be uploaded only while the hosted migration is pending.");
        }
        if (payload.Document.LogbookId != migration.LogbookId ||
            payload.Receipt.LogbookId != migration.LogbookId ||
            payload.Receipt.DeviceId != migration.DeviceId ||
            !string.Equals(
                payload.Receipt.SourceFingerprint,
                migration.SourceFingerprint,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Converted workbook data does not belong to the confirmed hosted migration.");
        }
        if (payload.Document.Operations.Count == 0 ||
            payload.Document.Operations.Any(operation =>
                operation.LogbookId != migration.LogbookId ||
                operation.DeviceId != migration.DeviceId))
        {
            throw new InvalidDataException(
                "Converted workbook operations do not belong to the temporary spreadsheet device.");
        }

        PortableWorkbookMigrationVerification.VerifyExact(payload.Receipt, payload.Document);
    }

    private static void EnsureExactOperationSet(
        IReadOnlyList<HostedOperationUpload> expected,
        IReadOnlyList<HostedOperationEnvelope> actual)
    {
        var expectedRevisionIds = expected.Select(operation => operation.RevisionId).ToHashSet();
        var actualRevisionIds = actual.Select(operation => operation.RevisionId).ToArray();
        if (actualRevisionIds.Length != expectedRevisionIds.Count ||
            actualRevisionIds.Distinct().Count() != actualRevisionIds.Length ||
            actualRevisionIds.Any(revisionId => !expectedRevisionIds.Contains(revisionId)))
        {
            throw new InvalidDataException(
                "Hosted flight-operation readback does not exactly match the converted workbook operation set.");
        }
    }

    private static HostedConfigurationRevisionEnvelope EnsureExactConfiguration(
        HostedConfigurationRevisionUpload expected,
        IReadOnlyList<HostedConfigurationRevisionEnvelope> actual)
    {
        if (actual.Count != 1)
        {
            throw new InvalidDataException(
                "Hosted configuration readback does not contain exactly one workbook migration revision.");
        }

        var revision = actual[0];
        if (revision.RevisionId != expected.RevisionId ||
            revision.DeviceId != expected.DeviceId ||
            revision.SchemaVersion != expected.SchemaVersion ||
            revision.CreatedAt.ToUniversalTime() != expected.CreatedAt.ToUniversalTime() ||
            !string.Equals(revision.PayloadCiphertext, expected.PayloadCiphertext, StringComparison.Ordinal) ||
            !string.Equals(revision.PayloadNonce, expected.PayloadNonce, StringComparison.Ordinal) ||
            !string.Equals(revision.PayloadTag, expected.PayloadTag, StringComparison.Ordinal) ||
            !string.Equals(revision.PayloadHash, expected.PayloadHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Hosted configuration readback does not exactly match the encrypted workbook configuration.");
        }

        return revision;
    }

    private static RevisionId CreateConfigurationRevisionId(
        HostedWorkbookMigration migration,
        PortableWorkbookMigrationReceipt receipt)
    {
        var seed = string.Join(
            "\0",
            "electronic-logbook.workbook-migration-configuration.v1",
            migration.MigrationId.Value,
            migration.SourceFingerprint,
            receipt.CustomFieldDefinitionsSha256,
            receipt.CurrencyOverrideDatesSha256);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return new RevisionId("rev_" + Convert.ToHexString(bytes.AsSpan(0, 16)).ToLowerInvariant());
    }
}
