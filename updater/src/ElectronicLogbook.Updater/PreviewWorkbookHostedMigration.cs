using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater;

public sealed record PreviewWorkbookHostedMigrationResult(
    HostedWorkbookMigration Migration,
    PortableWorkbookMigrationReceipt VerifiedReceipt,
    int VerifiedFlightCount,
    bool ResumedCompletedMigration);

public sealed class PreviewWorkbookHostedMigration
{
    private readonly IWorkbookMigrationHostedClient client;
    private readonly SupabaseWorkbookSession session;
    private readonly StagedWorkbookMigrationRecovery stagingBoundary;
    private readonly Func<HostedWorkbookMigration, IHostedLogbookLedger> createLedger;
    private readonly Func<string, HostedWorkbookMigration, WorkbookMigrationPayload> convertWorkbook;

    public PreviewWorkbookHostedMigration(
        SupabaseWorkbookConnectionClient client,
        SupabaseWorkbookSession session,
        SupabaseHostedSyncConfiguration configuration)
        : this(
            client,
            session,
            migration => new SupabaseHostedSyncClient(
                configuration.SupabaseUrl,
                configuration.AnonKey,
                session.AccountId,
                migration.DeviceId,
                session.Credential),
            WorkbookMigrationPayloadConverter.ConvertWorkbook)
    {
        ArgumentNullException.ThrowIfNull(configuration);
    }

    internal PreviewWorkbookHostedMigration(
        IWorkbookMigrationHostedClient client,
        SupabaseWorkbookSession session,
        Func<HostedWorkbookMigration, IHostedLogbookLedger> createLedger,
        Func<string, HostedWorkbookMigration, WorkbookMigrationPayload> convertWorkbook)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(createLedger);
        ArgumentNullException.ThrowIfNull(convertWorkbook);

        this.client = client;
        this.session = session;
        this.createLedger = createLedger;
        this.convertWorkbook = convertWorkbook;
        stagingBoundary = new StagedWorkbookMigrationRecovery(
            new WorkbookMigrationRecoveryCoordinator(client));
    }

    public Task<PreviewWorkbookHostedMigrationResult> RunAsync(
        StagedWorkbookMigration staging,
        string logbookDisplayName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(staging);
        return stagingBoundary.RunAfterValidatedStagingOrCompletionAsync(
            staging,
            logbookDisplayName,
            (state, token) => ContinueHostedMigrationAsync(
                staging.StagedWorkbookPath,
                state,
                token),
            cancellationToken);
    }

    private async Task<PreviewWorkbookHostedMigrationResult> ContinueHostedMigrationAsync(
        string stagedWorkbookPath,
        WorkbookMigrationRecoveryState state,
        CancellationToken cancellationToken)
    {
        var migration = state.Migration;
        if (migration.AccountId != session.AccountId)
        {
            throw new InvalidDataException(
                "The hosted spreadsheet migration does not belong to the signed-in account.");
        }

        var payload = convertWorkbook(stagedWorkbookPath, migration);
        if (state.IsAlreadyCompleted)
        {
            EnsureCompletedMigrationMatches(migration, payload);
            return new PreviewWorkbookHostedMigrationResult(
                migration,
                payload.Receipt,
                payload.Receipt.EntryCount,
                ResumedCompletedMigration: true);
        }

        var recoveryMaterial = state.RecoveryMaterial
            ?? throw new InvalidOperationException(
                "The pending spreadsheet migration returned no temporary recovery keys.");
        var ledger = createLedger(migration);
        try
        {
            var transfer = new WorkbookMigrationHostedTransfer(ledger, client);
            var verified = await transfer.UploadAndVerifyAsync(
                migration,
                payload,
                recoveryMaterial.LogbookKey,
                cancellationToken);
            var completed = await client.CompleteWorkbookMigrationAsync(
                migration.MigrationId,
                verified.UploadedOperationCount,
                verified.VerifiedReceipt.VerificationReceiptSha256,
                cancellationToken);
            EnsureCompletedMigrationMatches(completed, payload);

            return new PreviewWorkbookHostedMigrationResult(
                completed,
                verified.VerifiedReceipt,
                verified.UploadedOperationCount,
                ResumedCompletedMigration: false);
        }
        finally
        {
            (ledger as IDisposable)?.Dispose();
        }
    }

    private static void EnsureCompletedMigrationMatches(
        HostedWorkbookMigration completed,
        WorkbookMigrationPayload payload)
    {
        if (completed.Status != HostedWorkbookMigrationStatus.Completed ||
            completed.LogbookId != payload.Receipt.LogbookId ||
            completed.DeviceId != payload.Receipt.DeviceId ||
            completed.ExpectedOperationCount != payload.Receipt.EntryCount ||
            completed.VerifiedOperationCount != payload.Receipt.EntryCount ||
            !string.Equals(
                completed.SourceFingerprint,
                payload.Receipt.SourceFingerprint,
                StringComparison.Ordinal) ||
            !string.Equals(
                completed.VerificationReceiptHash,
                payload.Receipt.VerificationReceiptSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The completed hosted spreadsheet migration does not exactly match the verified workbook receipt.");
        }
    }
}
