using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public sealed class MobileReplacementRecoveryWorkflow(
    BrowserPackageKeyStore packageKeyStore,
    BrowserLogbookStore logbookStore,
    IMobileReplacementRecoveryClient recoveryClient,
    IMobileRecoveryEnvelopeService recoveryEnvelopeService,
    IHostedLogbookLedger ledger,
    IHostedConfigurationRevisionLedger configurationLedger,
    IHostedLogbookAuthenticator authenticator,
    INetworkStatus networkStatus,
    ISyncClock clock) : IMobileReplacementRecoveryWorkflow
{
    private const int MaxContinuationRuns = 100;

    public async ValueTask<MobileReplacementRecoveryResult> RecoverOnlyLogbookAsync(
        CancellationToken cancellationToken = default)
    {
        var memberships = await recoveryClient.DiscoverActiveLogbooksAsync(cancellationToken);
        if (memberships.Count != 1)
        {
            throw new MobileHostedDiagnosticException(
                "RECOVERY_LOGBOOK_SELECTION_REQUIRED",
                memberships.Count == 0
                    ? "No existing logbook is available to this account."
                    : "More than one existing logbook is available; automatic recovery cannot choose between them.");
        }

        return await RecoverAsync(memberships[0].LogbookId, cancellationToken);
    }

    public async ValueTask<MobileReplacementRecoveryResult> RecoverOnlyLogbookWithCodeAsync(
        string recoveryCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryCode);
        var memberships = await recoveryClient.DiscoverActiveLogbooksAsync(cancellationToken);
        if (memberships.Count != 1)
        {
            throw new MobileHostedDiagnosticException(
                "RECOVERY_LOGBOOK_SELECTION_REQUIRED",
                memberships.Count == 0
                    ? "No existing logbook is available to this account."
                    : "More than one existing logbook is available; select one before using a recovery code.");
        }
        return await RecoverCoreAsync(memberships[0].LogbookId, recoveryCode, cancellationToken);
    }

    public async ValueTask<MobileReplacementRecoveryResult> RecoverAsync(
        LogbookId logbookId,
        CancellationToken cancellationToken = default) =>
        await RecoverCoreAsync(logbookId, null, cancellationToken);

    private async ValueTask<MobileReplacementRecoveryResult> RecoverCoreAsync(
        LogbookId logbookId,
        string? recoveryCode,
        CancellationToken cancellationToken)
    {
        var context = await recoveryClient.PrepareReplacementRecoveryAsync(logbookId, cancellationToken);
        if (context.Membership.CurrentSchemaVersion != PortableLogbookDocumentV2.CurrentSchemaVersion
            || context.Membership.OperationFormatVersion != 1)
        {
            throw new MobileHostedDiagnosticException(
                "RECOVERY_SCHEMA_UNSUPPORTED",
                "The selected logbook requires a newer app version.");
        }

        if (string.IsNullOrWhiteSpace(recoveryCode))
        {
            await packageKeyStore.RestoreRecoveryEnvelopeAsync(
                logbookId,
                context.Session.DeviceId,
                context.PlatformLabel,
                recoveryEnvelopeService,
                cancellationToken);
        }
        else
        {
            var publicKey = await packageKeyStore.GetRecoveryPublicKeyAsync();
            var envelope = await recoveryEnvelopeService.RestoreWithRecoveryCodeAsync(
                new MobileRecoveryCodeRestoreRequest(
                    logbookId,
                    context.Session.DeviceId,
                    context.PlatformLabel,
                    new MobileRecoveryDeviceKey(
                        publicKey.PublicKey,
                        publicKey.Fingerprint,
                        publicKey.Algorithm)),
                cancellationToken);
            if (!await packageKeyStore.ImportRecoveryCodeEnvelopeAsync(logbookId, recoveryCode, envelope))
            {
                throw new MobileHostedDiagnosticException(
                    "RECOVERY_KEY_IMPORT_FAILED",
                    "The recovered logbook could not be retained by Android Keystore.");
            }
            await packageKeyStore.VerifyPackageKeyAsync(
                logbookId,
                "RECOVERY_KEY_READBACK_FAILED",
                cancellationToken);
        }

        PortableHostedConfigurationRevision? restoredConfiguration;
        try
        {
            restoredConfiguration = await new MobileHostedConfigurationRestore(
                    packageKeyStore,
                    configurationLedger)
                .RestoreLatestAsync(logbookId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HostedLedgerException
            or HostedConfigurationRevisionCipherException
            or InvalidDataException
            or HttpRequestException)
        {
            throw new MobileHostedDiagnosticException(
                "RECOVERY_CONFIGURATION_RESTORE_FAILED",
                "The hosted logbook settings could not be restored. The replacement device was not activated.",
                innerException: ex);
        }
        if (context.Membership.WorkbookMigration is not null && restoredConfiguration is null)
        {
            throw new MobileHostedDiagnosticException(
                "RECOVERY_CONFIGURATION_MISSING",
                "The migrated logbook settings are missing from hosted recovery. The replacement device was not activated.");
        }

        var retained = await logbookStore.LoadStateV2Async();
        var canResume = retained?.HostedSync is not null
            && retained.Document.LogbookId == logbookId
            && retained.HostedSync.AccountId == context.Session.AccountId
            && retained.HostedSync.DeviceId == context.Session.DeviceId;
        var retainedDocument = canResume ? retained!.Document : null;
        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(
            logbookId,
            restoredConfiguration?.CustomFieldDefinitions
                ?? retainedDocument?.CustomFieldDefinitions
                ?? MobileLogbookSession.CustomFields,
            restoredConfiguration?.CurrencyOverrideDates
                ?? retainedDocument?.CurrencyOverrideDates
                ?? PortableLogbookCurrencyOverrideDates.Empty,
            retainedDocument?.Operations ?? []);
        var hosted = canResume
            ? retained!.HostedSync!
            : new BrowserHostedSyncState(
                context.Session.AccountId,
                logbookId,
                context.Session.DeviceId,
                LastAcknowledgedHostedRevision: 0,
                PortableHostedSyncStatus.Waiting,
                LastAttemptedAt: clock.UtcNow);
        var sync = new MobileHostedSyncWorkflow(
            packageKeyStore,
            ledger,
            authenticator,
            networkStatus,
            clock);

        for (var run = 0; run < MaxContinuationRuns; run++)
        {
            var result = await sync.SyncRecoveryAsync(
                new PortableHostedSyncRequestContext(document, hosted, BackgroundSyncReason.ManualRefresh),
                context.Session,
                cancellationToken);
            document = result.Document;
            hosted = hosted.WithResult(result, clock.UtcNow);
            if (result.Status == PortableHostedSyncStatus.Synced)
            {
                VerifyCompletedWorkbookMigrationHistory(context.Membership, document);
            }
            await logbookStore.SaveStateAsync(new BrowserLogbookStateV2(document, [], null, null, hosted));

            if (result.Status == PortableHostedSyncStatus.Synced)
            {
                await VerifyDurableReadbackAsync(document, hosted);
                await recoveryClient.CompleteReplacementRecoveryAsync(
                    logbookId,
                    context.Session.DeviceId,
                    cancellationToken);
                return new MobileReplacementRecoveryResult(document, hosted);
            }
            if (result.Status != PortableHostedSyncStatus.Waiting)
            {
                throw new MobileHostedDiagnosticException(
                    "RECOVERY_LEDGER_RESTORE_INCOMPLETE",
                    result.AttentionRequiredReason ?? "The hosted logbook restore did not complete.");
            }
        }

        throw new MobileHostedDiagnosticException(
            "RECOVERY_LEDGER_PAGE_LIMIT",
            "The hosted logbook restore exceeded the safe continuation limit.");
    }

    private static void VerifyCompletedWorkbookMigrationHistory(
        MobileHostedLogbookMembership membership,
        PortableLogbookDocumentV2 document)
    {
        var expected = membership.WorkbookMigration;
        if (expected is null)
        {
            return;
        }

        var migratedOperations = document.Operations
            .Where(operation => operation.DeviceId == expected.SourceDeviceId)
            .ToArray();
        PortableWorkbookMigrationReceipt actual;
        try
        {
            var migratedDocument = PortableLogbookDocumentV2.CreateAustraliaFirst(
                document.LogbookId,
                document.CustomFieldDefinitions,
                document.CurrencyOverrideDates,
                migratedOperations);
            actual = PortableWorkbookMigrationVerification.CreateReceipt(
                expected.SourceFingerprint,
                migratedDocument);
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
        {
            throw new MobileHostedDiagnosticException(
                "RECOVERY_MIGRATION_HISTORY_MISMATCH",
                "The spreadsheet flights in hosted recovery do not match the completed migration. The replacement device was not activated; contact FlightLogX support.",
                innerException: ex);
        }

        if (actual.DeviceId != expected.SourceDeviceId
            || actual.EntryCount != expected.ExpectedOperationCount
            || !string.Equals(
                actual.VerificationReceiptSha256,
                expected.VerificationReceiptHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new MobileHostedDiagnosticException(
                "RECOVERY_MIGRATION_RECEIPT_MISMATCH",
                "The spreadsheet migration receipt does not match the hosted logbook. The replacement device was not activated; contact FlightLogX support.");
        }
    }

    private async ValueTask VerifyDurableReadbackAsync(
        PortableLogbookDocumentV2 expectedDocument,
        BrowserHostedSyncState expectedHosted)
    {
        var saved = await logbookStore.LoadStateV2Async();
        if (saved?.HostedSync is null
            || saved.HostedSync.AccountId != expectedHosted.AccountId
            || saved.HostedSync.LogbookId != expectedHosted.LogbookId
            || saved.HostedSync.DeviceId != expectedHosted.DeviceId
            || saved.HostedSync.LastAcknowledgedHostedRevision != expectedHosted.LastAcknowledgedHostedRevision
            || saved.HostedSync.LastStatus != PortableHostedSyncStatus.Synced
            || PortableLogbookJson.SerializeV2(saved.Document)
                != PortableLogbookJson.SerializeV2(expectedDocument))
        {
            throw new MobileHostedDiagnosticException(
                "RECOVERY_LOCAL_READBACK_MISMATCH",
                "The restored logbook did not survive local storage read-back verification.");
        }
    }
}
