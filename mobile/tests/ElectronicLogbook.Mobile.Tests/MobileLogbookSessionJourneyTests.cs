using System.Security.Cryptography;
using System.Text.Json;
using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;
using Microsoft.JSInterop;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileLogbookSessionJourneyTests
{
    [Fact]
    public async Task WorkbookMigrationPersistsExactValuesTotalsAndVerificationReceiptAcrossReload()
    {
        var jsRuntime = new JourneyJsRuntime();
        var logbookId = new LogbookId("log_migration_target");
        var hosted = new BrowserHostedSyncState(
            new HostedAccountId("acct_private"),
            logbookId,
            new DeviceId("dev_android"),
            0,
            PortableHostedSyncStatus.Synced);
        var empty = PortableLogbookDocumentV2.CreateAustraliaFirst(
            logbookId,
            MobileLogbookSession.CustomFields,
            PortableLogbookCurrencyOverrideDates.Empty,
            []);
        await new BrowserLogbookStore(jsRuntime).SaveStateAsync(
            new BrowserLogbookStateV2(empty, [], null, HostedSync: hosted));
        var session = CreateSession(jsRuntime);
        await session.EnsureLoadedWorkbookAsync();
        var customFields = PortableLogbookCustomFieldSet.CreateWorkbookCustomFields(["Role", "Employer", "Exercise", "Note"]);
        var entry = PortableLogbookWorkbookEntry.Empty with
        {
            Year = 2026,
            Month = 8,
            Day = 25,
            Type = "C172",
            Reg = "VH-MIG",
            From = "YSBK",
            To = "YSCN",
            CustomFields = new Dictionary<CustomFieldId, string?> { [customFields[0].Id] = "Captain" },
            SeCommandDay = 1.4m,
            IfrSim = 0.2m,
            LandingsDay = 1,
            Ils = 2
        };
        var rows = new[] { new MobileWorkbookMigrationRow(6, new EntryId("ent_workbook"), entry) };
        var totals = MobileWorkbookMigrationTotals.Calculate(rows.Select(row => row.Entry));
        var plan = new MobileWorkbookMigrationPlan(
            "Disposable.xlsm",
            new string('a', 64),
            "2.0.7",
            new LogbookId("log_legacy_source"),
            logbookId,
            customFields,
            new PortableLogbookCurrencyOverrideDates(new DateOnly(2026, 7, 31), null, null),
            rows,
            totals,
            MobileWorkbookMigrationCachedTotals.Empty,
            MobileWorkbookMigrationReader.ComputeEntryValuesSha256(rows.Select(row => row.Entry)));

        var result = await session.ApplyWorkbookMigrationAsync(
            plan,
            DateTimeOffset.Parse("2026-08-25T02:00:00Z"));

        Assert.True(result.DurableReadbackVerified);
        Assert.Equal(1, result.DurableEntryCount);
        Assert.Equal(logbookId, session.DocumentV2.LogbookId);
        Assert.Equal("Role", session.WorkbookCustomFields[0].Label);
        Assert.Equal(totals, session.WorkbookMigration?.Totals);

        var reloaded = CreateSession(jsRuntime);
        await reloaded.EnsureLoadedWorkbookAsync();

        Assert.NotNull(reloaded.WorkbookMigration);
        Assert.True(reloaded.WorkbookMigration.DurableReadbackVerified);
        Assert.Equal(plan.EntryValuesSha256, reloaded.WorkbookMigration.EntryValuesSha256);
        Assert.Equal(totals, reloaded.WorkbookMigration.Totals);
        var migrated = Assert.Single(reloaded.CurrentEntriesV2).Entry;
        Assert.NotNull(migrated);
        Assert.Equal("VH-MIG", migrated.Reg);
        Assert.Equal("Captain", migrated.CustomFields[customFields[0].Id]);
        Assert.Equal(1.6m, MobileLogbookSession.WorkbookLoggedTime(migrated));
        Assert.False(reloaded.CanMigrateWorkbook);
    }

    [Fact]
    public async Task PopulatedHostedLogbookAllowsReadOnlyComparisonButStillRejectsMigrationWrite()
    {
        var jsRuntime = new JourneyJsRuntime();
        var logbookId = new LogbookId("log_populated_comparison");
        var deviceId = new DeviceId("dev_android");
        var retainedEntry = PortableLogbookWorkbookEntry.Empty with
        {
            Year = 2026,
            Month = 8,
            Day = 25,
            Reg = "VH-RETAINED",
            SeCommandDay = 1.3m
        };
        var operation = PortableLogbookOperationV2.Create(
            logbookId,
            new EntryId("ent_retained"),
            new RevisionId("rev_retained"),
            deviceId,
            DateTimeOffset.Parse("2026-08-25T00:00:00Z"),
            retainedEntry);
        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(
            logbookId,
            MobileLogbookSession.CustomFields,
            PortableLogbookCurrencyOverrideDates.Empty,
            [operation]);
        var hosted = new BrowserHostedSyncState(
            new HostedAccountId("acct_private"),
            logbookId,
            deviceId,
            1,
            PortableHostedSyncStatus.Synced);
        await new BrowserLogbookStore(jsRuntime).SaveStateAsync(
            new BrowserLogbookStateV2(document, [], null, HostedSync: hosted));
        var session = CreateSession(jsRuntime);
        await session.EnsureLoadedWorkbookAsync();
        var rows = new[] { new MobileWorkbookMigrationRow(2, null, retainedEntry) };
        var totals = MobileWorkbookMigrationTotals.Calculate(rows.Select(row => row.Entry));
        var plan = new MobileWorkbookMigrationPlan(
            "Disposable.xlsm",
            new string('b', 64),
            "3.0.0",
            logbookId,
            logbookId,
            session.WorkbookCustomFields,
            PortableLogbookCurrencyOverrideDates.Empty,
            rows,
            totals,
            MobileWorkbookMigrationCachedTotals.Empty,
            MobileWorkbookMigrationReader.ComputeEntryValuesSha256(rows.Select(row => row.Entry)));

        Assert.True(session.CanPreviewWorkbookMigration);
        Assert.False(session.CanMigrateWorkbook);
        var comparison = MobileWorkbookMigrationWorkflow.CompareWithApp(
            plan,
            session.DocumentV2.LogbookId,
            session.CurrentEntriesV2.Select(entry => entry.Entry!),
            session.WorkbookCustomFields,
            session.DocumentV2.CurrencyOverrideDates);
        Assert.True(comparison.IsExactDataMatch);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.ApplyWorkbookMigrationAsync(plan, DateTimeOffset.Parse("2026-08-25T02:00:00Z")));
        Assert.Contains("already contains", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("VH-RETAINED", Assert.Single(session.CurrentEntriesV2).Entry?.Reg);
        Assert.Null(session.WorkbookMigration);
    }

    [Fact]
    public async Task HostedInviteAcceptanceInitializesAppOnlyV2LogbookBeforeWorkbookImport()
    {
        var jsRuntime = new JourneyJsRuntime();
        var clock = new ManualSyncClock(DateTimeOffset.Parse("2026-08-06T01:00:00Z"));
        var authenticator = new InMemoryHostedLogbookAuthenticator(
            new HostedAccountId("acct_private"),
            new DeviceId("dev_android"),
            clock);
        var session = CreateSession(jsRuntime, authenticator, clock);

        await session.EnsureLoadedWorkbookAsync();
        var signIn = await session.StartHostedInviteAcceptanceAsync("pilot@example.com");
        await session.CompleteHostedInviteAcceptanceAsync("123456");

        Assert.Equal("p***@example.com", signIn.DeliveryHint);
        Assert.Null(session.PendingHostedSignIn);
        Assert.Equal(PortableLogbookDocumentV2.CurrentSchemaVersion, session.DocumentV2.SchemaVersion);
        Assert.StartsWith("log_", session.DocumentV2.LogbookId.Value, StringComparison.Ordinal);
        Assert.Empty(session.DocumentV2.Operations);
        Assert.NotNull(session.HostedSync);
        Assert.Equal(new HostedAccountId("acct_private"), session.HostedSync.AccountId);
        Assert.Equal(session.DocumentV2.LogbookId, session.HostedSync.LogbookId);
        Assert.Equal(new DeviceId("dev_android"), session.HostedSync.DeviceId);
        Assert.Equal(PortableHostedSyncStatus.Synced, session.HostedSync.LastStatus);
        Assert.Equal(0, session.HostedSync.LastAcknowledgedHostedRevision);
        Assert.Equal("Ready", session.PackageKeyStatus);
        Assert.Equal("Account connected.", session.LastActionMessage);
        Assert.Single(jsRuntime.ImportedPackageKeys);

        var reloaded = CreateSession(jsRuntime, authenticator, clock);
        await reloaded.EnsureLoadedWorkbookAsync();

        Assert.Equal(session.DocumentV2.LogbookId, reloaded.DocumentV2.LogbookId);
        Assert.Equal(session.HostedSync, reloaded.HostedSync);
        Assert.Equal("Ready", reloaded.PackageKeyStatus);
    }

    [Fact]
    public async Task HostedInviteAcceptanceResumesLocalSetupAfterDeviceRegistrationSucceeded()
    {
        var jsRuntime = new JourneyJsRuntime { FailNextPackageKeyImport = true };
        var clock = new ManualSyncClock(DateTimeOffset.Parse("2026-08-06T01:00:00Z"));
        var authenticator = new InMemoryHostedLogbookAuthenticator(
            new HostedAccountId("acct_private"),
            new DeviceId("dev_android"),
            clock);
        var session = CreateSession(jsRuntime, authenticator, clock);

        await session.EnsureLoadedWorkbookAsync();
        await session.StartHostedInviteAcceptanceAsync("pilot@example.com");
        await Assert.ThrowsAsync<JSException>(async () =>
            await session.CompleteHostedInviteAcceptanceAsync("123456"));

        Assert.NotNull(await authenticator.GetCurrentSessionAsync());
        Assert.Null(session.HostedSync);
        Assert.Equal(new LogbookId("log_mobile_preview"), session.DocumentV2.LogbookId);

        await session.ResumeHostedInviteAcceptanceAsync();

        Assert.NotNull(session.HostedSync);
        Assert.Equal(new DeviceId("dev_android"), session.HostedSync.DeviceId);
        Assert.Equal(session.DocumentV2.LogbookId, session.HostedSync.LogbookId);
        Assert.Equal("Ready", session.PackageKeyStatus);
        Assert.Equal("Account connected.", session.LastActionMessage);
        Assert.Single(jsRuntime.ImportedPackageKeys);
    }

    [Fact]
    public async Task GoogleSignInAutomaticallyRestoresTheOnlyExistingLogbook()
    {
        var jsRuntime = new JourneyJsRuntime();
        var clock = new ManualSyncClock(DateTimeOffset.Parse("2026-08-10T01:00:00Z"));
        var accountId = new HostedAccountId("acct_private");
        var logbookId = new LogbookId("log_existing");
        var deviceId = new DeviceId("dev_replacement");
        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(
            logbookId,
            MobileLogbookSession.CustomFields,
            PortableLogbookCurrencyOverrideDates.Empty,
            []);
        var hosted = new BrowserHostedSyncState(
            accountId,
            logbookId,
            deviceId,
            LastAcknowledgedHostedRevision: 7,
            PortableHostedSyncStatus.Synced,
            LastAttemptedAt: clock.UtcNow,
            LastSyncedAt: clock.UtcNow);
        var recovery = new RecordingReplacementRecoveryWorkflow(
            new MobileReplacementRecoveryResult(document, hosted));
        var google = new RecordingGoogleAuthenticator(
            new HostedSignInException(
                HostedSignInFailureReason.AccountRecoveryRequired,
                "Existing account recovery is required."));
        await new BrowserPackageKeyStore(jsRuntime).ImportRecoveryCodeAsync(
            logbookId,
            PortableLogbookKey.Generate().ToRecoveryCode());
        var session = CreateSession(
            jsRuntime,
            hostedAuthenticator: new InMemoryHostedLogbookAuthenticator(accountId, deviceId, clock),
            syncClock: clock,
            googleAuthenticator: google,
            replacementRecovery: recovery);

        await session.EnsureLoadedWorkbookAsync();
        await session.SignInWithGoogleAsync();

        Assert.Equal(1, google.SignInCount);
        Assert.Equal(1, recovery.AutomaticRecoveryCount);
        Assert.Equal(logbookId, session.DocumentV2.LogbookId);
        Assert.Equal(deviceId, session.HostedSync?.DeviceId);
        Assert.Equal(PortableHostedSyncStatus.Synced, session.HostedSync?.LastStatus);
        Assert.Equal(7, session.HostedSync?.LastAcknowledgedHostedRevision);
        Assert.Equal("Ready", session.PackageKeyStatus);
        Assert.Equal("Existing logbook restored and synced.", session.LastActionMessage);
    }

    [Fact]
    public async Task EmailOtpSignInAutomaticallyRestoresTheOnlyExistingLogbook()
    {
        var jsRuntime = new JourneyJsRuntime();
        var clock = new ManualSyncClock(DateTimeOffset.Parse("2026-08-10T01:00:00Z"));
        var accountId = new HostedAccountId("acct_private");
        var logbookId = new LogbookId("log_existing");
        var deviceId = new DeviceId("dev_replacement");
        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(
            logbookId,
            MobileLogbookSession.CustomFields,
            PortableLogbookCurrencyOverrideDates.Empty,
            []);
        var hosted = new BrowserHostedSyncState(
            accountId,
            logbookId,
            deviceId,
            LastAcknowledgedHostedRevision: 7,
            PortableHostedSyncStatus.Synced,
            LastAttemptedAt: clock.UtcNow,
            LastSyncedAt: clock.UtcNow);
        var recovery = new RecordingReplacementRecoveryWorkflow(
            new MobileReplacementRecoveryResult(document, hosted));
        var authenticator = new RecoveryRequiredEmailAuthenticator(accountId, clock);
        await new BrowserPackageKeyStore(jsRuntime).ImportRecoveryCodeAsync(
            logbookId,
            PortableLogbookKey.Generate().ToRecoveryCode());
        var session = CreateSession(
            jsRuntime,
            hostedAuthenticator: authenticator,
            syncClock: clock,
            replacementRecovery: recovery);

        await session.EnsureLoadedWorkbookAsync();
        await session.StartHostedInviteAcceptanceAsync("pilot@example.com");
        await session.CompleteHostedInviteAcceptanceAsync("123456");

        Assert.Equal(1, authenticator.CompleteCount);
        Assert.Equal(1, recovery.AutomaticRecoveryCount);
        Assert.Equal(logbookId, session.DocumentV2.LogbookId);
        Assert.Equal(deviceId, session.HostedSync?.DeviceId);
        Assert.Equal(PortableHostedSyncStatus.Synced, session.HostedSync?.LastStatus);
        Assert.Equal("Existing logbook restored and synced.", session.LastActionMessage);
    }

    [Fact]
    public async Task ProvenRevokedDeviceCanReauthenticateWithoutRejectingRetainedOperations()
    {
        var jsRuntime = new JourneyJsRuntime();
        var clock = new ManualSyncClock(DateTimeOffset.Parse("2026-08-22T00:00:00Z"));
        var accountId = new HostedAccountId("acct_private");
        var revokedDeviceId = new DeviceId("dev_revoked");
        var initialAuthenticator = new InMemoryHostedLogbookAuthenticator(accountId, revokedDeviceId, clock);
        var initial = CreateSession(jsRuntime, initialAuthenticator, clock);
        await initial.EnsureLoadedWorkbookAsync();
        await initial.StartHostedInviteAcceptanceAsync("pilot@example.com");
        await initial.CompleteHostedInviteAcceptanceAsync("123456");
        FillWorkbookDraft(initial.WorkbookDraft);
        await initial.SaveWorkbookEntryAsync();

        var store = new BrowserLogbookStore(jsRuntime);
        var retained = Assert.IsType<BrowserLogbookStateV2>(await store.LoadStateV2Async());
        var retainedHosted = Assert.IsType<BrowserHostedSyncState>(retained.HostedSync);
        await store.SaveStateAsync(retained with
        {
            HostedSync = retainedHosted with
            {
                LastStatus = PortableHostedSyncStatus.NeedsAttention,
                AttentionRequiredReason = "The active hosted device does not match the retained credential."
            }
        });

        var replacementDeviceId = new DeviceId("dev_replacement");
        var restoredHosted = retainedHosted with
        {
            DeviceId = replacementDeviceId,
            LastStatus = PortableHostedSyncStatus.Synced,
            AttentionRequiredReason = null
        };
        var recovery = new RecordingReplacementRecoveryWorkflow(
            new MobileReplacementRecoveryResult(retained.Document, restoredHosted));
        var recoveryClient = new InactiveDeviceRecoveryClient(accountId, revokedDeviceId, clock.UtcNow.AddHours(1));
        var connectionRecovery = new MobileConnectionRecoveryWorkflow(
            recoveryClient,
            store,
            new BrowserPackageKeyStore(jsRuntime),
            clock);
        var authenticator = new RecoveryRequiredEmailAuthenticator(accountId, clock);
        var session = CreateSession(
            jsRuntime,
            hostedAuthenticator: authenticator,
            syncClock: clock,
            connectionRecovery: connectionRecovery,
            replacementRecovery: recovery);

        await session.EnsureLoadedWorkbookAsync();
        var diagnostics = await session.RunConnectionPreflightAsync();

        Assert.False(diagnostics.Passed);
        Assert.Equal(MobileConnectionStage.DEVICE_READ, diagnostics.CurrentStage);
        Assert.Equal("DEVICE_INACTIVE", diagnostics.ErrorCode);
        Assert.True(session.ShouldOfferHostedAuthentication);
        Assert.Single(session.DocumentV2.Operations);

        await session.StartHostedInviteAcceptanceAsync("pilot@example.com");
        await session.CompleteHostedInviteAcceptanceAsync("123456");

        Assert.Equal(1, authenticator.CompleteCount);
        Assert.Equal(1, recovery.AutomaticRecoveryCount);
        Assert.Single(session.DocumentV2.Operations);
        Assert.Equal(replacementDeviceId, session.HostedSync?.DeviceId);
        Assert.Equal(PortableHostedSyncStatus.Synced, session.HostedSync?.LastStatus);
        Assert.Equal("Existing logbook restored and synced.", session.LastActionMessage);
    }

    [Fact]
    public async Task GoogleSignInDoesNotStartRecoveryForUnrelatedAuthenticationFailure()
    {
        var recovery = new RecordingReplacementRecoveryWorkflow();
        var google = new RecordingGoogleAuthenticator(
            new HostedSignInException(
                HostedSignInFailureReason.AccountDisabled,
                "This account is disabled."));
        var clock = new ManualSyncClock(DateTimeOffset.Parse("2026-08-10T01:00:00Z"));
        var session = CreateSession(
            new JourneyJsRuntime(),
            hostedAuthenticator: new InMemoryHostedLogbookAuthenticator(
                new HostedAccountId("acct_private"),
                new DeviceId("dev_android"),
                clock),
            syncClock: clock,
            googleAuthenticator: google,
            replacementRecovery: recovery);

        await session.EnsureLoadedWorkbookAsync();
        var error = await Assert.ThrowsAsync<HostedSignInException>(session.SignInWithGoogleAsync);

        Assert.Equal(HostedSignInFailureReason.AccountDisabled, error.Reason);
        Assert.Equal(0, recovery.AutomaticRecoveryCount);
        Assert.False(session.HasHostedSync);
    }

    [Theory]
    [InlineData(HostedSignInFailureReason.WorkbookMigrationRequired)]
    [InlineData(HostedSignInFailureReason.WorkbookMigrationFailed)]
    [InlineData(HostedSignInFailureReason.WorkbookMigrationInvalid)]
    public async Task GoogleSignInDoesNotCreateAnEmptyLogbookForIncompleteMigration(
        HostedSignInFailureReason failureReason)
    {
        var jsRuntime = new JourneyJsRuntime();
        var recovery = new RecordingReplacementRecoveryWorkflow();
        var google = new RecordingGoogleAuthenticator(
            new HostedSignInException(failureReason, "The spreadsheet migration cannot continue on Android."));
        var clock = new ManualSyncClock(DateTimeOffset.Parse("2026-08-29T01:00:00Z"));
        var session = CreateSession(
            jsRuntime,
            hostedAuthenticator: new InMemoryHostedLogbookAuthenticator(
                new HostedAccountId("acct_private"),
                new DeviceId("dev_android"),
                clock),
            syncClock: clock,
            googleAuthenticator: google,
            replacementRecovery: recovery);

        await session.EnsureLoadedWorkbookAsync();
        var originalLogbookId = session.DocumentV2.LogbookId;
        var error = await Assert.ThrowsAsync<HostedSignInException>(session.SignInWithGoogleAsync);

        Assert.Equal(failureReason, error.Reason);
        Assert.Equal(0, recovery.AutomaticRecoveryCount);
        Assert.Equal(originalLogbookId, session.DocumentV2.LogbookId);
        Assert.Empty(session.DocumentV2.Operations);
        Assert.False(session.HasHostedSync);
        Assert.Empty(jsRuntime.ImportedPackageKeys);
    }

    [Fact]
    public async Task RetainedHostedSessionRetriesIdempotentRecoveryEnrollmentWithUnchangedIdentifiers()
    {
        var jsRuntime = new JourneyJsRuntime();
        var clock = new ManualSyncClock(DateTimeOffset.Parse("2026-08-09T01:00:00Z"));
        var authenticator = new InMemoryHostedLogbookAuthenticator(
            new HostedAccountId("acct_private"),
            new DeviceId("dev_android"),
            clock);
        var ledger = new InMemoryHostedLogbookLedger();
        var network = new StaticNetworkStatus(new NetworkAvailability(IsOnline: true));
        var initial = CreateSession(jsRuntime, authenticator, clock, ledger, network);
        await initial.EnsureLoadedWorkbookAsync();
        await initial.StartHostedInviteAcceptanceAsync("pilot@example.com");
        await initial.CompleteHostedInviteAcceptanceAsync("123456");
        var retainedLogbookId = initial.DocumentV2.LogbookId;
        var retainedDeviceId = initial.HostedSync!.DeviceId;

        var recoveryService = new RecordingRecoveryEnvelopeService();
        var reloaded = CreateSession(
            jsRuntime,
            authenticator,
            clock,
            ledger,
            network,
            recoveryEnvelopeService: recoveryService);
        await reloaded.EnsureLoadedWorkbookAsync();
        await reloaded.SyncHostedNowAsync();

        var reloadedAgain = CreateSession(
            jsRuntime,
            authenticator,
            clock,
            ledger,
            network,
            recoveryEnvelopeService: recoveryService);
        await reloadedAgain.EnsureLoadedWorkbookAsync();

        Assert.Equal(PortableHostedSyncStatus.Synced, reloadedAgain.HostedSync!.LastStatus);
        Assert.Equal(retainedLogbookId, reloadedAgain.DocumentV2.LogbookId);
        Assert.Equal(retainedLogbookId, reloadedAgain.HostedSync.LogbookId);
        Assert.Equal(retainedDeviceId, reloadedAgain.HostedSync.DeviceId);
        Assert.Equal(2, recoveryService.EnrollmentRequests.Count);
        Assert.All(recoveryService.EnrollmentRequests, request =>
        {
            Assert.Equal(retainedLogbookId, request.LogbookId);
            Assert.Equal(retainedDeviceId, request.DeviceId);
        });
        Assert.Equal(2, jsRuntime.RecoveryWrapCount);
    }

    [Fact]
    public async Task RecoveryEnrollmentFailurePersistsRedactedNeedsAttentionWithoutChangingLocalState()
    {
        var jsRuntime = new JourneyJsRuntime();
        var clock = new ManualSyncClock(DateTimeOffset.Parse("2026-08-09T01:00:00Z"));
        var authenticator = new InMemoryHostedLogbookAuthenticator(
            new HostedAccountId("acct_private"),
            new DeviceId("dev_android"),
            clock);
        var ledger = new InMemoryHostedLogbookLedger();
        var network = new StaticNetworkStatus(new NetworkAvailability(IsOnline: true));
        var initial = CreateSession(jsRuntime, authenticator, clock, ledger, network);
        await initial.EnsureLoadedWorkbookAsync();
        await initial.StartHostedInviteAcceptanceAsync("pilot@example.com");
        await initial.CompleteHostedInviteAcceptanceAsync("123456");
        FillWorkbookDraft(initial.WorkbookDraft);
        await initial.SaveWorkbookEntryAsync();
        var retainedDocument = initial.DocumentV2;
        var retainedHostedState = initial.HostedSync!;

        var recoveryService = new RecordingRecoveryEnvelopeService(failEnrollment: true);
        var reloaded = CreateSession(
            jsRuntime,
            authenticator,
            clock,
            ledger,
            network,
            recoveryEnvelopeService: recoveryService);
        await reloaded.EnsureLoadedWorkbookAsync();

        Assert.Equal(PortableHostedSyncStatus.NeedsAttention, reloaded.HostedSync!.LastStatus);
        Assert.Equal(retainedDocument.LogbookId, reloaded.DocumentV2.LogbookId);
        Assert.Equal(
            retainedDocument.Operations.Select(operation => operation.RevisionId),
            reloaded.DocumentV2.Operations.Select(operation => operation.RevisionId));
        Assert.Equal(retainedHostedState.AccountId, reloaded.HostedSync.AccountId);
        Assert.Equal(retainedHostedState.LogbookId, reloaded.HostedSync.LogbookId);
        Assert.Equal(retainedHostedState.DeviceId, reloaded.HostedSync.DeviceId);
        Assert.Equal("Account recovery setup needs attention (RECOVERY_SERVICE_REJECTED). Retry Sync now.", reloaded.HostedSync.AttentionRequiredReason);
        Assert.DoesNotContain("pilot@example.com", reloaded.HostedSync.AttentionRequiredReason, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", reloaded.HostedSync.AttentionRequiredReason, StringComparison.OrdinalIgnoreCase);
        var persisted = await new BrowserLogbookStore(jsRuntime).LoadStateV2Async();
        Assert.NotNull(persisted);
        Assert.Equal(reloaded.DocumentV2.LogbookId, persisted.Document.LogbookId);
        Assert.Equal(
            reloaded.DocumentV2.Operations.Select(operation => operation.RevisionId),
            persisted.Document.Operations.Select(operation => operation.RevisionId));
        Assert.NotNull(persisted.HostedSync);
        Assert.Equal(reloaded.HostedSync.AccountId, persisted.HostedSync.AccountId);
        Assert.Equal(reloaded.HostedSync.LogbookId, persisted.HostedSync.LogbookId);
        Assert.Equal(reloaded.HostedSync.DeviceId, persisted.HostedSync.DeviceId);
        Assert.Equal(reloaded.HostedSync.LastAcknowledgedHostedRevision, persisted.HostedSync.LastAcknowledgedHostedRevision);
        Assert.Equal(reloaded.HostedSync.LastStatus, persisted.HostedSync.LastStatus);
        Assert.Equal(reloaded.HostedSync.AttentionRequiredReason, persisted.HostedSync.AttentionRequiredReason);
        Assert.Equal(
            reloaded.HostedSync.UploadedRevisionIds ?? [],
            persisted.HostedSync.UploadedRevisionIds ?? []);
    }

    [Fact]
    public async Task RecoveryEnrollmentUnsupportedBridgePersistsNeedsAttentionWithoutReplacingHostedIdentity()
    {
        var jsRuntime = new JourneyJsRuntime();
        var clock = new ManualSyncClock(DateTimeOffset.Parse("2026-08-09T01:00:00Z"));
        var authenticator = new InMemoryHostedLogbookAuthenticator(
            new HostedAccountId("acct_private"),
            new DeviceId("dev_android"),
            clock);
        var ledger = new InMemoryHostedLogbookLedger();
        var network = new StaticNetworkStatus(new NetworkAvailability(IsOnline: true));
        var initial = CreateSession(jsRuntime, authenticator, clock, ledger, network);
        await initial.EnsureLoadedWorkbookAsync();
        await initial.StartHostedInviteAcceptanceAsync("pilot@example.com");
        await initial.CompleteHostedInviteAcceptanceAsync("123456");
        var retainedLogbookId = initial.DocumentV2.LogbookId;
        var retainedHostedState = initial.HostedSync!;

        jsRuntime.FailRecoveryBridge = true;
        var recoveryService = new RecordingRecoveryEnvelopeService();
        var reloaded = CreateSession(
            jsRuntime,
            authenticator,
            clock,
            ledger,
            network,
            recoveryEnvelopeService: recoveryService);
        await reloaded.EnsureLoadedWorkbookAsync();

        Assert.Equal(PortableHostedSyncStatus.NeedsAttention, reloaded.HostedSync!.LastStatus);
        Assert.Equal("Account recovery setup needs attention (RECOVERY_DEVICE_BRIDGE_UNAVAILABLE). Retry Sync now.", reloaded.HostedSync.AttentionRequiredReason);
        Assert.Equal(retainedLogbookId, reloaded.DocumentV2.LogbookId);
        Assert.Equal(retainedHostedState.AccountId, reloaded.HostedSync.AccountId);
        Assert.Equal(retainedHostedState.LogbookId, reloaded.HostedSync.LogbookId);
        Assert.Equal(retainedHostedState.DeviceId, reloaded.HostedSync.DeviceId);
        Assert.Empty(recoveryService.EnrollmentRequests);
        Assert.Equal(0, jsRuntime.RecoveryWrapCount);
    }

    [Fact]
    public async Task FirstInitializationRequiresRecoveryCodeRoundTripBeforeUploadingCodeEnvelope()
    {
        var jsRuntime = new JourneyJsRuntime();
        var clock = new ManualSyncClock(DateTimeOffset.Parse("2026-08-09T02:00:00Z"));
        var authenticator = new InMemoryHostedLogbookAuthenticator(
            new HostedAccountId("acct_private"),
            new DeviceId("dev_android"),
            clock);
        var recoveryService = new RecordingRecoveryEnvelopeService();
        var session = CreateSession(
            jsRuntime,
            authenticator,
            clock,
            new InMemoryHostedLogbookLedger(),
            new StaticNetworkStatus(new NetworkAvailability(IsOnline: true)),
            recoveryEnvelopeService: recoveryService);

        await session.EnsureLoadedWorkbookAsync();
        await session.StartHostedInviteAcceptanceAsync("pilot@example.com");
        await session.CompleteHostedInviteAcceptanceAsync("123456");

        Assert.True(session.IsRecoveryCodeConfirmationPending);
        Assert.Equal(jsRuntime.GeneratedRecoveryCode, session.PendingRecoveryCode);
        var abandonedCode = session.PendingRecoveryCode;
        Assert.Single(recoveryService.EnrollmentRequests);
        Assert.Empty(recoveryService.RecoveryCodeEnrollmentRequests);
        Assert.False(await session.ConfirmRecoveryCodeAsync("wrong-recovery-code-that-is-long-enough"));
        Assert.Empty(recoveryService.RecoveryCodeEnrollmentRequests);

        var resumed = CreateSession(
            jsRuntime,
            authenticator,
            clock,
            new InMemoryHostedLogbookLedger(),
            new StaticNetworkStatus(new NetworkAvailability(IsOnline: true)),
            recoveryEnvelopeService: recoveryService);
        await resumed.EnsureLoadedWorkbookAsync();
        Assert.True(resumed.IsRecoveryCodeConfirmationPending);
        Assert.NotEqual(abandonedCode, resumed.PendingRecoveryCode);

        Assert.True(await resumed.ConfirmRecoveryCodeAsync(resumed.PendingRecoveryCode!));
        Assert.False(resumed.IsRecoveryCodeConfirmationPending);
        Assert.Null(resumed.PendingRecoveryCode);
        Assert.Single(recoveryService.RecoveryCodeEnrollmentRequests);
        Assert.Equal("Recovery code confirmed. Account connected and synced.", resumed.LastActionMessage);
    }

    [Fact]
    public async Task HostedInviteAcceptanceDoesNotReplaceExistingWorkbookPackageState()
    {
        var jsRuntime = new JourneyJsRuntime();
        var clock = new ManualSyncClock(DateTimeOffset.Parse("2026-08-06T01:00:00Z"));
        var authenticator = new InMemoryHostedLogbookAuthenticator(
            new HostedAccountId("acct_private"),
            new DeviceId("dev_android"),
            clock);
        var session = CreateSession(jsRuntime, authenticator, clock);

        await session.EnsureLoadedWorkbookAsync();
        FillWorkbookDraft(session.WorkbookDraft);
        await session.SaveWorkbookEntryAsync();
        await session.StartHostedInviteAcceptanceAsync("pilot@example.com");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await session.CompleteHostedInviteAcceptanceAsync("123456"));

        Assert.Contains("before importing", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(session.HostedSync);
        Assert.Single(session.DocumentV2.Operations);
    }

    [Fact]
    public async Task HostedSessionAutomaticallySyncsAfterLocalEditAndAfterNetworkRestored()
    {
        var jsRuntime = new JourneyJsRuntime();
        var clock = new ManualSyncClock(DateTimeOffset.Parse("2026-08-06T01:00:00Z"));
        var ledger = new InMemoryHostedLogbookLedger();
        var network = new StaticNetworkStatus(new NetworkAvailability(IsOnline: true));
        var authenticator = new InMemoryHostedLogbookAuthenticator(
            new HostedAccountId("acct_private"),
            new DeviceId("dev_android"),
            clock);
        var session = CreateSession(jsRuntime, authenticator, clock, ledger, network);

        await session.EnsureLoadedWorkbookAsync();
        await session.StartHostedInviteAcceptanceAsync("pilot@example.com");
        await session.CompleteHostedInviteAcceptanceAsync("123456");
        FillWorkbookDraft(session.WorkbookDraft);

        await session.SaveWorkbookEntryAsync();

        Assert.Equal("Entry added.", session.LastActionMessage);
        Assert.NotNull(session.HostedSync);
        Assert.Equal(PortableHostedSyncStatus.Synced, session.HostedSync.LastStatus);
        Assert.Equal(1, session.HostedSync.LastAcknowledgedHostedRevision);
        var hostedAfterFirstSave = await ledger.ReadMissingOperationsAsync(session.DocumentV2.LogbookId, 0, 10);
        Assert.Single(hostedAfterFirstSave.Operations);
        Assert.DoesNotContain("\"entry\"", hostedAfterFirstSave.Operations[0].PayloadCiphertext, StringComparison.OrdinalIgnoreCase);

        network.Availability = new NetworkAvailability(IsOnline: false);
        FillWorkbookDraft(session.WorkbookDraft);
        session.WorkbookDraft.Reg = "VH-OFF";
        await session.SaveWorkbookEntryAsync();

        Assert.Equal(2, session.DocumentV2.Operations.Count);
        Assert.Equal(PortableHostedSyncStatus.Offline, session.HostedSync.LastStatus);
        Assert.Equal(1, session.HostedSync.PendingLocalOperationCount);
        Assert.Single((await ledger.ReadMissingOperationsAsync(session.DocumentV2.LogbookId, 0, 10)).Operations);

        network.Availability = new NetworkAvailability(IsOnline: true);
        var restored = await session.SyncHostedAfterNetworkRestoredAsync();

        Assert.NotNull(restored);
        Assert.Equal(PortableHostedSyncStatus.Synced, restored.Status);
        Assert.Equal(2, session.HostedSync.LastAcknowledgedHostedRevision);
        Assert.Equal(2, (await ledger.ReadMissingOperationsAsync(session.DocumentV2.LogbookId, 0, 10)).Operations.Count);
    }

    [Fact]
    public async Task LegacyLedgerCursorReplaysAllPagesBeforeTrustingItsSavedHighWaterMark()
    {
        var jsRuntime = new JourneyJsRuntime();
        var clock = new ManualSyncClock(DateTimeOffset.Parse("2026-08-22T00:00:00Z"));
        var accountId = new HostedAccountId("acct_private");
        var logbookId = new LogbookId("log_paged_repair");
        var sourceDeviceId = new DeviceId("dev_source");
        var replacementDeviceId = new DeviceId("dev_replacement");
        var packageKeyStore = new BrowserPackageKeyStore(jsRuntime);
        await packageKeyStore.ImportRecoveryCodeAsync(
            logbookId,
            PortableLogbookKey.Generate().ToRecoveryCode());
        var operations = Enumerable.Range(1, 223)
            .Select(index => PortableLogbookOperationV2.Create(
                logbookId,
                new EntryId($"ent_{index:D3}"),
                new RevisionId($"rev_{index:D3}"),
                sourceDeviceId,
                clock.UtcNow.AddSeconds(index),
                PortableLogbookWorkbookEntry.Empty with { FlightId = $"PAGE-{index:D3}" }))
            .ToArray();
        var completeDocument = PortableLogbookDocumentV2.CreateAustraliaFirst(
            logbookId,
            MobileLogbookSession.CustomFields,
            PortableLogbookCurrencyOverrideDates.Empty,
            operations);
        var ledger = new InMemoryHostedLogbookLedger();
        var network = new StaticNetworkStatus(new NetworkAvailability(IsOnline: true));
        var sourceAuthenticator = new InMemoryHostedLogbookAuthenticator(accountId, sourceDeviceId, clock);
        await sourceAuthenticator.StartEmailSignInAsync("pilot@example.com");
        await sourceAuthenticator.CompleteEmailSignInAsync("123456");
        var seeded = await new MobileHostedSyncWorkflow(
                packageKeyStore,
                ledger,
                sourceAuthenticator,
                network,
                clock)
            .SyncAsync(new PortableHostedSyncRequestContext(
                completeDocument,
                new BrowserHostedSyncState(
                    accountId,
                    logbookId,
                    sourceDeviceId,
                    LastAcknowledgedHostedRevision: 0,
                    PortableHostedSyncStatus.Waiting,
                    LedgerCursorVersion: BrowserHostedSyncState.CurrentLedgerCursorVersion),
                BackgroundSyncReason.ManualRefresh));
        Assert.True(
            seeded.Status == PortableHostedSyncStatus.Synced,
            seeded.AttentionRequiredReason);
        Assert.Equal(223, seeded.LastAcknowledgedHostedRevision);

        var incompleteDocument = PortableLogbookDocumentV2.CreateAustraliaFirst(
            logbookId,
            MobileLogbookSession.CustomFields,
            PortableLogbookCurrencyOverrideDates.Empty,
            operations.Take(200));
        var legacyHostedState = new BrowserHostedSyncState(
            accountId,
            logbookId,
            replacementDeviceId,
            LastAcknowledgedHostedRevision: 223,
            PortableHostedSyncStatus.Synced);
        var replacementAuthenticator = new InMemoryHostedLogbookAuthenticator(
            accountId,
            replacementDeviceId,
            clock);
        await replacementAuthenticator.StartEmailSignInAsync("pilot@example.com");
        await replacementAuthenticator.CompleteEmailSignInAsync("123456");
        var repaired = await new MobileHostedSyncWorkflow(
                packageKeyStore,
                ledger,
                replacementAuthenticator,
                network,
                clock)
            .SyncAsync(new PortableHostedSyncRequestContext(
                incompleteDocument,
                legacyHostedState,
                BackgroundSyncReason.ManualRefresh));
        var persisted = legacyHostedState.WithResult(repaired, clock.UtcNow);

        Assert.True(
            repaired.Status == PortableHostedSyncStatus.Synced,
            repaired.AttentionRequiredReason);
        Assert.Equal(223, repaired.Document.Operations.Count);
        Assert.Equal(23, repaired.DownloadedOperationCount);
        Assert.Equal(223, repaired.LastAcknowledgedHostedRevision);
        Assert.Equal(BrowserHostedSyncState.CurrentLedgerCursorVersion, persisted.LedgerCursorVersion);
    }

    [Fact]
    public async Task ConcurrentNetworkRestoredCallbacksSerializeOnePendingUpload()
    {
        var jsRuntime = new JourneyJsRuntime();
        var clock = new ManualSyncClock(DateTimeOffset.Parse("2026-08-15T00:55:00+10:00"));
        var ledger = new InMemoryHostedLogbookLedger();
        var network = new StaticNetworkStatus(new NetworkAvailability(IsOnline: true));
        var authenticator = new InMemoryHostedLogbookAuthenticator(
            new HostedAccountId("acct_private"),
            new DeviceId("dev_android"),
            clock);
        var initial = CreateSession(jsRuntime, authenticator, clock, ledger, network);
        await initial.EnsureLoadedWorkbookAsync();
        await initial.StartHostedInviteAcceptanceAsync("pilot@example.com");
        await initial.CompleteHostedInviteAcceptanceAsync("123456");
        FillWorkbookDraft(initial.WorkbookDraft);
        await initial.SaveWorkbookEntryAsync();

        network.Availability = new NetworkAvailability(IsOnline: false);
        FillWorkbookDraft(initial.WorkbookDraft);
        initial.WorkbookDraft.Reg = "VH-OFF";
        await initial.SaveWorkbookEntryAsync();

        var blockingLedger = new BlockingAppendLedger(ledger);
        var reloaded = CreateSession(jsRuntime, authenticator, clock, blockingLedger, network);
        await reloaded.EnsureLoadedWorkbookAsync();
        network.Availability = new NetworkAvailability(IsOnline: true);

        var first = reloaded.SyncHostedAfterNetworkRestoredAsync();
        await blockingLedger.FirstAppendStarted;
        var second = reloaded.SyncHostedAfterNetworkRestoredAsync();

        Assert.Equal(1, blockingLedger.AppendCallCount);
        blockingLedger.ReleaseFirstAppend();
        var results = await Task.WhenAll(first, second);

        Assert.All(results, result => Assert.Equal(PortableHostedSyncStatus.Synced, result?.Status));
        Assert.Equal(1, blockingLedger.AppendCallCount);
        Assert.Equal(PortableHostedSyncStatus.Synced, reloaded.HostedSync?.LastStatus);
        Assert.Equal(2, reloaded.HostedSync?.LastAcknowledgedHostedRevision);
        Assert.Equal(2, (await ledger.ReadMissingOperationsAsync(reloaded.DocumentV2.LogbookId, 0, 10)).Operations.Count);
    }

    [Fact]
    public async Task LostAppendResponseRetriesTheSameEncryptedPayloadWithoutDuplicatingTheOperation()
    {
        var jsRuntime = new JourneyJsRuntime();
        var clock = new ManualSyncClock(DateTimeOffset.Parse("2026-08-15T01:05:00+10:00"));
        var innerLedger = new InMemoryHostedLogbookLedger();
        var ledger = new AppendThenLoseResponseLedger(innerLedger);
        var network = new StaticNetworkStatus(new NetworkAvailability(IsOnline: true));
        var authenticator = new InMemoryHostedLogbookAuthenticator(
            new HostedAccountId("acct_private"),
            new DeviceId("dev_android"),
            clock);
        var session = CreateSession(jsRuntime, authenticator, clock, ledger, network);
        await session.EnsureLoadedWorkbookAsync();
        await session.StartHostedInviteAcceptanceAsync("pilot@example.com");
        await session.CompleteHostedInviteAcceptanceAsync("123456");
        FillWorkbookDraft(session.WorkbookDraft);

        await session.SaveWorkbookEntryAsync();

        Assert.Equal(PortableHostedSyncStatus.Offline, session.HostedSync?.LastStatus);
        Assert.Equal(1, session.HostedSync?.PendingLocalOperationCount);
        Assert.Single((await innerLedger.ReadMissingOperationsAsync(session.DocumentV2.LogbookId, 0, 10)).Operations);

        var retry = await session.SyncHostedNowAsync();

        Assert.NotNull(retry);
        Assert.Equal(PortableHostedSyncStatus.Synced, retry.Status);
        Assert.Equal(0, retry.PendingLocalOperationCount);
        Assert.Equal(1, retry.LastAcknowledgedHostedRevision);
        Assert.Single((await innerLedger.ReadMissingOperationsAsync(session.DocumentV2.LogbookId, 0, 10)).Operations);
    }

    [Fact]
    public async Task OfflinePendingCountExcludesDownloadedOtherDeviceHistoryAndDiagnosticsExplainBoth()
    {
        var jsRuntime = new JourneyJsRuntime();
        var clock = new ManualSyncClock(DateTimeOffset.Parse("2026-08-15T00:20:00+10:00"));
        var logbookId = new LogbookId("log_retained");
        var currentDeviceId = new DeviceId("dev_retained");
        var otherDeviceId = new DeviceId("dev_previous");
        var entryId = new EntryId("ent_offline_retest");
        var hostedCreate = PortableLogbookOperationV2.Create(
            logbookId,
            entryId,
            new RevisionId("rev_recovery_hosted"),
            otherDeviceId,
            DateTimeOffset.Parse("2026-08-11T13:30:12.783+10:00"),
            PortableLogbookWorkbookEntry.Empty with
            {
                FlightId = "RECOVERY-0811"
            });
        var offlineCorrection = PortableLogbookOperationV2.Correct(
            logbookId,
            entryId,
            new RevisionId("rev_offline_correction"),
            [hostedCreate.RevisionId],
            currentDeviceId,
            DateTimeOffset.Parse("2026-08-15T00:16:50.275+10:00"),
            PortableLogbookWorkbookEntry.Empty with
            {
                FlightId = "OFFLINE-0811",
                Remarks = "GATE1 OFFLINE RETEST"
            });
        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(
            logbookId,
            MobileLogbookSession.CustomFields,
            PortableLogbookCurrencyOverrideDates.Empty,
            [hostedCreate, offlineCorrection]);
        var hosted = new BrowserHostedSyncState(
            new HostedAccountId("acct_private"),
            logbookId,
            currentDeviceId,
            LastAcknowledgedHostedRevision: 1,
            PortableHostedSyncStatus.Synced,
            UploadedRevisionIds: []);
        await new BrowserLogbookStore(jsRuntime).SaveStateAsync(
            new BrowserLogbookStateV2(document, [], null, HostedSync: hosted));
        var session = CreateSession(
            jsRuntime,
            new InMemoryHostedLogbookAuthenticator(new HostedAccountId("acct_private"), currentDeviceId, clock),
            clock,
            new InMemoryHostedLogbookLedger(),
            new StaticNetworkStatus(new NetworkAvailability(IsOnline: false)));

        await session.EnsureLoadedWorkbookAsync();

        Assert.Equal(PortableHostedSyncStatus.Offline, session.HostedSync?.LastStatus);
        Assert.Equal(1, session.HostedSync?.PendingLocalOperationCount);
        Assert.Equal("1 local operation will sync when the network returns.", session.HostedSyncStatusDetail);
        var diagnostics = Assert.IsType<MobileHostedSyncDiagnosticSummary>(session.HostedSyncDiagnostics);
        var pending = Assert.Single(diagnostics.PendingUploads);
        Assert.Equal("Correction", pending.KindLabel);
        Assert.Equal("OFFLINE-0811", pending.FlightId);
        Assert.Equal("GATE1 OFFLINE RETEST", pending.Detail);
        var excluded = Assert.Single(diagnostics.OtherDeviceHistory);
        Assert.Equal("Create", excluded.KindLabel);
        Assert.Equal("RECOVERY-0811", excluded.FlightId);
        Assert.Equal(otherDeviceId.Value, excluded.DeviceId);
    }

    [Fact]
    public async Task NetworkBridgeFailureDuringColdStartStillLoadsRetainedDeviceCopyAsOffline()
    {
        var jsRuntime = new JourneyJsRuntime();
        var clock = new ManualSyncClock(DateTimeOffset.Parse("2026-08-24T08:00:00Z"));
        var logbookId = new LogbookId("log_retained_cold_start");
        var deviceId = new DeviceId("dev_retained");
        var retainedOperation = PortableLogbookOperationV2.Create(
            logbookId,
            new EntryId("ent_retained"),
            new RevisionId("rev_retained"),
            deviceId,
            clock.UtcNow.AddHours(-1),
            PortableLogbookWorkbookEntry.Empty with
            {
                FlightId = "OFFLINE-COLD-START",
                Reg = "VH-OFF"
            });
        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(
            logbookId,
            MobileLogbookSession.CustomFields,
            PortableLogbookCurrencyOverrideDates.Empty,
            [retainedOperation]);
        var hosted = new BrowserHostedSyncState(
            new HostedAccountId("acct_private"),
            logbookId,
            deviceId,
            LastAcknowledgedHostedRevision: 1,
            PortableHostedSyncStatus.Synced,
            UploadedRevisionIds: [retainedOperation.RevisionId]);
        await new BrowserLogbookStore(jsRuntime).SaveStateAsync(
            new BrowserLogbookStateV2(document, [], null, HostedSync: hosted));
        var session = CreateSession(
            jsRuntime,
            new InMemoryHostedLogbookAuthenticator(hosted.AccountId, deviceId, clock),
            clock,
            new InMemoryHostedLogbookLedger(),
            new ThrowingNetworkStatus());

        await session.EnsureLoadedWorkbookAsync();

        Assert.True(session.IsLoaded);
        Assert.False(session.IsStorageBlocked);
        Assert.Equal(PortableHostedSyncStatus.Offline, session.HostedSync?.LastStatus);
        Assert.Equal("Offline", session.HostedSyncStatusLabel);
        var retained = Assert.Single(session.CurrentEntriesV2);
        Assert.Equal("OFFLINE-COLD-START", retained.Entry?.FlightId);
        Assert.Equal("VH-OFF", retained.Entry?.Reg);
    }

    [Fact]
    public async Task UnreachableHostedTransportPersistsOfflineStateAndQueuedEditAcrossReload()
    {
        var jsRuntime = new JourneyJsRuntime();
        var clock = new ManualSyncClock(DateTimeOffset.Parse("2026-08-11T10:00:00Z"));
        var ledger = new TransportFailingLedger(new InMemoryHostedLogbookLedger());
        var network = new StaticNetworkStatus(new NetworkAvailability(IsOnline: true));
        var authenticator = new InMemoryHostedLogbookAuthenticator(
            new HostedAccountId("acct_private"),
            new DeviceId("dev_android"),
            clock);
        var session = CreateSession(jsRuntime, authenticator, clock, ledger, network);

        await session.EnsureLoadedWorkbookAsync();
        await session.StartHostedInviteAcceptanceAsync("pilot@example.com");
        await session.CompleteHostedInviteAcceptanceAsync("123456");
        FillWorkbookDraft(session.WorkbookDraft);
        await session.SaveWorkbookEntryAsync();
        Assert.Equal(PortableHostedSyncStatus.Synced, session.HostedSync?.LastStatus);

        ledger.FailTransport = true;
        FillWorkbookDraft(session.WorkbookDraft);
        session.WorkbookDraft.Reg = "VH-OFF";

        await session.SaveWorkbookEntryAsync();

        Assert.Equal(PortableHostedSyncStatus.Offline, session.HostedSync?.LastStatus);
        Assert.Equal(1, session.HostedSync?.PendingLocalOperationCount);

        var reloaded = CreateSession(jsRuntime, authenticator, clock, ledger, network);
        await reloaded.EnsureLoadedWorkbookAsync();

        Assert.True(reloaded.IsLoaded);
        Assert.Equal(2, reloaded.DocumentV2.Operations.Count);
        Assert.Equal(PortableHostedSyncStatus.Offline, reloaded.HostedSync?.LastStatus);
        Assert.Contains(reloaded.CurrentEntriesV2, entry => entry.Entry?.Reg == "VH-OFF");

        ledger.FailTransport = false;
        var hostedSyncChangeCount = 0;
        reloaded.HostedSyncChanged += () => hostedSyncChangeCount++;
        var restored = await reloaded.SyncHostedAfterNetworkRestoredAsync();

        Assert.Equal(PortableHostedSyncStatus.Synced, restored?.Status);
        Assert.Equal(2, reloaded.HostedSync?.LastAcknowledgedHostedRevision);
        Assert.Equal(1, hostedSyncChangeCount);
    }

    [Fact]
    public async Task WorkbookFaithfulJourneySavesCanonicalV2EntryAndReloadsFromBrowserStorage()
    {
        var jsRuntime = new JourneyJsRuntime();
        var session = CreateSession(jsRuntime);

        await session.EnsureLoadedWorkbookAsync();
        FillWorkbookDraft(session.WorkbookDraft);
        Assert.Empty(session.DocumentV2.Operations);

        await session.SaveWorkbookEntryAsync();

        var added = Assert.Single(session.CurrentEntriesV2);
        var operation = Assert.Single(session.DocumentV2.Operations);
        Assert.StartsWith("ent_", operation.EntryId.Value, StringComparison.Ordinal);
        Assert.Equal(36, operation.EntryId.Value.Length);
        Assert.NotNull(added.Entry);
        Assert.Equal("C172", added.Entry.Type);
        Assert.Equal("VH-WBV", added.Entry.Reg);
        Assert.Equal("Alex", added.Entry.Pic);
        Assert.Equal(1.1m, added.Entry.SeCommandDay);
        Assert.Equal(0.4m, added.Entry.IfrIf);
        Assert.Equal(2, added.Entry.Ils);
        Assert.Equal("Entry added.", session.LastActionMessage);
        Assert.Equal(1, jsRuntime.SaveCount);
        Assert.True(session.ExchangeStatus.HasUnexportedChanges);
        Assert.Equal(1, session.ExchangeStatus.PendingOperationCount);

        var reloaded = CreateSession(jsRuntime);
        await reloaded.EnsureLoadedWorkbookAsync();

        var reloadedEntry = Assert.Single(reloaded.CurrentEntriesV2);
        Assert.Equal(operation.EntryId, reloadedEntry.EntryId);
        Assert.Equal("VH-WBV", reloadedEntry.Entry?.Reg);
        Assert.Equal("Ready", reloaded.PackageKeyStatus);
    }

    [Fact]
    public async Task CurrencyRecencySummaryIsSharedUntilTheDateOrDocumentChanges()
    {
        var jsRuntime = new JourneyJsRuntime();
        var session = CreateSession(jsRuntime);
        var today = new DateOnly(2026, 8, 1);

        await session.EnsureLoadedWorkbookAsync();

        var first = session.GetCurrencyRecencySummary(today);
        Assert.Same(first, session.GetCurrencyRecencySummary(today));

        var nextDay = session.GetCurrencyRecencySummary(today.AddDays(1));
        Assert.NotSame(first, nextDay);
        Assert.Same(nextDay, session.GetCurrencyRecencySummary(today.AddDays(1)));

        FillWorkbookDraft(session.WorkbookDraft);
        await session.SaveWorkbookEntryAsync();

        Assert.NotSame(nextDay, session.GetCurrencyRecencySummary(today.AddDays(1)));
    }

    [Fact]
    public async Task WorkbookEntryDetailsUseDashForBlankValuesAndSeparateChecksFromCustomFields()
    {
        var session = CreateSession(new JourneyJsRuntime());
        await session.EnsureLoadedWorkbookAsync();

        var details = session.EntryDetails(session.WorkbookDraft.ToEntry(session.WorkbookCustomFields)).ToList();

        Assert.Equal("-", Assert.Single(details, detail => detail.Label == "Flight ID").Value);
        Assert.Equal("-", Assert.Single(details, detail => detail.Label == "SE ICUS day").Value);
        Assert.Equal("-", Assert.Single(details, detail => detail.Label == "Landings day").Value);
        Assert.All(details.Where(detail => detail.Label is "FR" or "IPC" or "OPC"),
            detail => Assert.Equal(EntryDetailGroup.Checks, detail.Group));
        Assert.All(details.Where(detail => detail.Group == EntryDetailGroup.CustomFields),
            detail => Assert.Equal("-", detail.Value));
        Assert.Equal(nameof(PortableLogbookWorkbookEntry.LandingsDay),
            Assert.Single(details, detail => detail.Label == "Landings day").Field);
    }

    [Fact]
    public async Task WorkbookDraftErrorsRemainHiddenUntilTheUserAttemptsToSave()
    {
        var session = CreateSession(new JourneyJsRuntime());
        await session.EnsureLoadedWorkbookAsync();

        Assert.NotEmpty(session.WorkbookDraftErrors);
        Assert.False(session.ShouldShowWorkbookDraftErrors);

        session.WorkbookDraft.Type = "C172";
        session.MarkDraftEdited();

        Assert.True(session.HasEditedDraft);
        Assert.False(session.HasAttemptedSubmit);
        Assert.False(session.ShouldShowWorkbookDraftErrors);

        await session.SaveWorkbookEntryAsync();

        Assert.True(session.HasAttemptedSubmit);
        Assert.True(session.ShouldShowWorkbookDraftErrors);
        Assert.DoesNotContain(session.WorkbookDraftErrors, error => error.Contains("Technical code", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PreparingAValidWorkbookDraftForReviewDoesNotPersistIt()
    {
        var jsRuntime = new JourneyJsRuntime();
        var session = CreateSession(jsRuntime);
        await session.EnsureLoadedWorkbookAsync();
        FillWorkbookDraft(session.WorkbookDraft);

        var isReadyForReview = session.PrepareWorkbookDraftForReview();

        Assert.True(isReadyForReview);
        Assert.True(session.HasAttemptedSubmit);
        Assert.False(session.ShouldShowWorkbookDraftErrors);
        Assert.Empty(session.DocumentV2.Operations);
        Assert.Equal(0, jsRuntime.SaveCount);
    }

    [Fact]
    public async Task WorkbookDraftReviewExposesAddToLogbookWarningsWithoutPersistingTheEntry()
    {
        var jsRuntime = new JourneyJsRuntime();
        var session = CreateSession(jsRuntime);
        await session.EnsureLoadedWorkbookAsync();
        FillWorkbookDraft(session.WorkbookDraft);
        session.WorkbookDraft.OperatorProficiencyCheck = true;
        session.WorkbookDraft.InstrumentProficiencyCheck = false;

        Assert.True(session.PrepareWorkbookDraftForReview());

        Assert.Contains(session.WorkbookDraftWarnings, warning => warning.Code == "NEWENTRY-W001");
        Assert.Empty(session.DocumentV2.Operations);
        Assert.Equal(0, jsRuntime.SaveCount);
    }

    [Fact]
    public async Task WorkbookFaithfulEntryIdIsAllocatedOnlyWhenSavePersistsCreateOperation()
    {
        var jsRuntime = new JourneyJsRuntime();
        var session = CreateSession(jsRuntime);

        await session.EnsureLoadedWorkbookAsync();
        Assert.Empty(session.DocumentV2.Operations);

        Assert.NotEmpty(session.WorkbookDraftErrors);
        Assert.Empty(session.DocumentV2.Operations);

        await session.SaveWorkbookEntryAsync();

        Assert.Empty(session.DocumentV2.Operations);
        Assert.Equal(0, jsRuntime.SaveCount);

        FillWorkbookDraft(session.WorkbookDraft);
        await session.SaveWorkbookEntryAsync();

        var create = Assert.Single(session.DocumentV2.Operations);
        Assert.Equal(PortableOperationKind.Create, create.Kind);
        Assert.StartsWith("ent_", create.EntryId.Value, StringComparison.Ordinal);
        Assert.Equal(1, jsRuntime.SaveCount);

        var savedEntryId = create.EntryId;
        var savedRevisionId = create.RevisionId;
        var current = Assert.Single(session.CurrentEntriesV2);
        session.EditWorkbookEntry(current);
        session.WorkbookDraft.Reg = "VH-WBX";
        await session.SaveWorkbookEntryAsync();

        var correction = session.DocumentV2.Operations.Last();
        Assert.Equal(PortableOperationKind.Correction, correction.Kind);
        Assert.Equal(savedEntryId, correction.EntryId);
        Assert.Equal(savedRevisionId, Assert.Single(correction.ParentRevisionIds));
        Assert.Equal(2, jsRuntime.SaveCount);

        await session.DeleteWorkbookEntryAsync(Assert.Single(session.CurrentEntriesV2));

        var deletion = session.DocumentV2.Operations.Last();
        Assert.Equal(PortableOperationKind.Deletion, deletion.Kind);
        Assert.Equal(savedEntryId, deletion.EntryId);
        Assert.Equal(correction.RevisionId, Assert.Single(deletion.ParentRevisionIds));
        Assert.Equal(3, jsRuntime.SaveCount);
    }

    [Fact]
    public async Task WorkbookDeletionCanBeUndoneWithinFiveSecondsWithoutRemovingAuditHistory()
    {
        var jsRuntime = new JourneyJsRuntime();
        var clock = new MutableSyncClock(DateTimeOffset.Parse("2026-08-14T02:00:00Z"));
        var session = CreateSession(jsRuntime, syncClock: clock);
        await session.EnsureLoadedWorkbookAsync();
        FillWorkbookDraft(session.WorkbookDraft);
        await session.SaveWorkbookEntryAsync();
        var original = Assert.Single(session.CurrentEntriesV2);

        await session.DeleteWorkbookEntryAsync(original);

        Assert.Equal("Entry deleted.", session.ActionFeedbackMessage);
        Assert.False(session.ShouldCelebrateActionFeedback);
        Assert.True(session.CanUndoLastWorkbookAction);
        Assert.Equal(MobileLogbookSession.ActionFeedbackWindow, session.ActionFeedbackRemaining);
        var deletion = session.DocumentV2.Operations.Last();
        Assert.Equal(PortableOperationKind.Deletion, deletion.Kind);
        Assert.Empty(session.CurrentEntriesV2);
        Assert.Single(session.DeletedEntriesV2);

        Assert.True(await session.UndoLastWorkbookActionAsync());

        var restored = Assert.Single(session.CurrentEntriesV2);
        Assert.Equal(original.EntryId, restored.EntryId);
        Assert.Equal(original.Entry, restored.Entry);
        Assert.Empty(session.DeletedEntriesV2);
        Assert.False(session.CanUndoLastWorkbookAction);
        Assert.Equal("Deletion undone.", session.LastActionMessage);
        Assert.Equal(3, session.DocumentV2.Operations.Count);
        Assert.Contains(deletion, session.DocumentV2.Operations);
        var restoration = session.DocumentV2.Operations.Last();
        Assert.Equal(PortableOperationKind.Correction, restoration.Kind);
        Assert.Equal(deletion.RevisionId, Assert.Single(restoration.ParentRevisionIds));
        Assert.Equal(3, jsRuntime.SaveCount);
    }

    [Fact]
    public async Task WorkbookDeletionCannotBeUndoneAfterFiveSeconds()
    {
        var jsRuntime = new JourneyJsRuntime();
        var clock = new MutableSyncClock(DateTimeOffset.Parse("2026-08-14T02:00:00Z"));
        var session = CreateSession(jsRuntime, syncClock: clock);
        await session.EnsureLoadedWorkbookAsync();
        FillWorkbookDraft(session.WorkbookDraft);
        await session.SaveWorkbookEntryAsync();
        await session.DeleteWorkbookEntryAsync(Assert.Single(session.CurrentEntriesV2));

        clock.Advance(MobileLogbookSession.ActionFeedbackWindow);

        Assert.False(session.CanUndoLastWorkbookAction);
        Assert.False(await session.UndoLastWorkbookActionAsync());
        Assert.Null(session.LastActionMessage);
        Assert.Empty(session.CurrentEntriesV2);
        Assert.Single(session.DeletedEntriesV2);
        Assert.Equal(2, session.DocumentV2.Operations.Count);
        Assert.Equal(2, jsRuntime.SaveCount);
    }

    [Fact]
    public async Task DeletedWorkbookEntryCanBeRestoredLaterWithoutRemovingItsTombstoneOrEarlierHistory()
    {
        var jsRuntime = new JourneyJsRuntime();
        var clock = new MutableSyncClock(DateTimeOffset.Parse("2026-08-14T02:00:00Z"));
        var session = CreateSession(jsRuntime, syncClock: clock);
        await session.EnsureLoadedWorkbookAsync();
        FillWorkbookDraft(session.WorkbookDraft);
        await session.SaveWorkbookEntryAsync();
        var original = Assert.Single(session.CurrentEntriesV2);
        await session.DeleteWorkbookEntryAsync(original);
        var tombstone = session.DocumentV2.Operations.Last();

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(session.CanUndoLastWorkbookAction);
        var deleted = Assert.Single(session.DeletedEntriesV2);
        Assert.Equal(original.Entry, session.FindLatestWorkbookEntryPayload(deleted));

        Assert.True(await session.RestoreWorkbookEntryAsync(deleted));

        var restored = Assert.Single(session.CurrentEntriesV2);
        Assert.Equal(original.EntryId, restored.EntryId);
        Assert.Equal(original.Entry, restored.Entry);
        Assert.Empty(session.DeletedEntriesV2);
        Assert.Equal("Entry restored.", session.ActionFeedbackMessage);
        Assert.True(session.CanUndoLastWorkbookAction);
        Assert.Equal(3, session.DocumentV2.Operations.Count);
        Assert.Contains(tombstone, session.DocumentV2.Operations);
        var restoration = session.DocumentV2.Operations.Last();
        Assert.Equal(PortableOperationKind.Correction, restoration.Kind);
        Assert.Equal(tombstone.RevisionId, Assert.Single(restoration.ParentRevisionIds));
        Assert.Equal(3, restored.RevisionHistory.Count);

        var reloaded = CreateSession(jsRuntime);
        await reloaded.EnsureLoadedWorkbookAsync();
        Assert.Single(reloaded.CurrentEntriesV2);
        Assert.Empty(reloaded.DeletedEntriesV2);
        Assert.Contains(reloaded.DocumentV2.Operations, operation => operation.RevisionId == tombstone.RevisionId);
        Assert.Equal(3, reloaded.DocumentV2.Operations.Count);

        Assert.True(await session.UndoLastWorkbookActionAsync());

        Assert.Empty(session.CurrentEntriesV2);
        var deletedAgain = Assert.Single(session.DeletedEntriesV2);
        Assert.Equal("Restoration undone.", session.LastActionMessage);
        Assert.False(session.CanUndoLastWorkbookAction);
        Assert.Equal(4, session.DocumentV2.Operations.Count);
        var restorationUndo = session.DocumentV2.Operations.Last();
        Assert.Equal(PortableOperationKind.Deletion, restorationUndo.Kind);
        Assert.Equal(restoration.RevisionId, Assert.Single(restorationUndo.ParentRevisionIds));
        Assert.Equal("Restoration undone.", restorationUndo.Reason);
        Assert.Equal(4, deletedAgain.RevisionHistory.Count);

        var reloadedAfterUndo = CreateSession(jsRuntime);
        await reloadedAfterUndo.EnsureLoadedWorkbookAsync();
        Assert.Empty(reloadedAfterUndo.CurrentEntriesV2);
        Assert.Single(reloadedAfterUndo.DeletedEntriesV2);
        Assert.Equal(4, reloadedAfterUndo.DocumentV2.Operations.Count);
    }

    [Fact]
    public async Task WorkbookAddFeedbackDoesNotOfferUndo()
    {
        var session = CreateSession(new JourneyJsRuntime());
        await session.EnsureLoadedWorkbookAsync();
        FillWorkbookDraft(session.WorkbookDraft);

        await session.SaveWorkbookEntryAsync();

        Assert.Equal("Entry added.", session.ActionFeedbackMessage);
        Assert.True(session.ShouldCelebrateActionFeedback);
        Assert.Equal("Entry added.", session.LastActionMessage);
        Assert.True(session.HasPendingActionFeedback);
        Assert.False(session.CanUndoLastWorkbookAction);
        Assert.False(await session.UndoLastWorkbookActionAsync());
        Assert.Single(session.CurrentEntriesV2);
        Assert.Single(session.DocumentV2.Operations);
    }

    [Fact]
    public async Task WorkbookModificationCanBeUndoneWithoutRemovingAuditHistory()
    {
        var jsRuntime = new JourneyJsRuntime();
        var clock = new MutableSyncClock(DateTimeOffset.Parse("2026-08-14T02:00:00Z"));
        var session = CreateSession(jsRuntime, syncClock: clock);
        await session.EnsureLoadedWorkbookAsync();
        FillWorkbookDraft(session.WorkbookDraft);
        await session.SaveWorkbookEntryAsync();
        var original = Assert.Single(session.CurrentEntriesV2);

        session.EditWorkbookEntry(original);
        session.WorkbookDraft.Reg = "VH-MOD";
        await session.SaveWorkbookEntryAsync();

        var modification = session.DocumentV2.Operations.Last();
        Assert.Equal(PortableOperationKind.Correction, modification.Kind);
        Assert.Equal("VH-MOD", Assert.Single(session.CurrentEntriesV2).Entry?.Reg);
        Assert.Equal("Entry modified.", session.ActionFeedbackMessage);
        Assert.True(session.ShouldCelebrateActionFeedback);
        Assert.True(session.CanUndoLastWorkbookAction);
        Assert.Equal(MobileLogbookSession.ActionFeedbackWindow, session.ActionFeedbackRemaining);

        Assert.True(await session.UndoLastWorkbookActionAsync());

        var restored = Assert.Single(session.CurrentEntriesV2);
        Assert.Equal(original.EntryId, restored.EntryId);
        Assert.Equal(original.Entry, restored.Entry);
        Assert.Equal("Modification undone.", session.LastActionMessage);
        Assert.False(session.CanUndoLastWorkbookAction);
        Assert.Equal(3, session.DocumentV2.Operations.Count);
        Assert.Contains(modification, session.DocumentV2.Operations);
        var undo = session.DocumentV2.Operations.Last();
        Assert.Equal(PortableOperationKind.Correction, undo.Kind);
        Assert.Equal(modification.RevisionId, Assert.Single(undo.ParentRevisionIds));
        Assert.Equal(3, jsRuntime.SaveCount);
    }

    [Fact]
    public async Task WorkbookFaithfulCorrectionKeepsEntryIdentityWhenItsDateChangesSortOrder()
    {
        var jsRuntime = new JourneyJsRuntime();
        var session = CreateSession(jsRuntime);

        await session.EnsureLoadedWorkbookAsync();
        FillWorkbookDraft(session.WorkbookDraft);
        await session.SaveWorkbookEntryAsync();
        var corrected = Assert.Single(session.CurrentEntriesV2);

        FillWorkbookDraft(session.WorkbookDraft);
        session.WorkbookDraft.Date = new DateOnly(2026, 7, 25);
        session.WorkbookDraft.Reg = "VH-LATER";
        await session.SaveWorkbookEntryAsync();

        Assert.Equal("VH-LATER", Assert.Single(session.CurrentEntriesV2, entry => entry.Entry?.Reg == "VH-LATER").Entry?.Reg);

        session.EditWorkbookEntry(corrected);
        session.WorkbookDraft.Date = new DateOnly(2026, 7, 26);
        session.WorkbookDraft.Reg = "VH-CORRECTED";
        await session.SaveWorkbookEntryAsync();

        var firstAfterResort = session.CurrentEntriesV2[0];
        Assert.Equal(corrected.EntryId, firstAfterResort.EntryId);
        Assert.Equal("VH-CORRECTED", firstAfterResort.Entry?.Reg);
    }

    [Fact]
    public async Task CloneEntryClearsAnOpenCorrectionIdentityAndSavesAsANewEntry()
    {
        var jsRuntime = new JourneyJsRuntime();
        var session = CreateSession(jsRuntime);

        await session.EnsureLoadedAsync();
        FillDraft(session.Draft, "VH-CLONE", 1.0m);
        await session.SaveEntryAsync();
        var original = Assert.Single(session.CurrentEntries);

        session.EditEntry(original);
        session.CloneEntry(original.Entry!);

        Assert.Null(session.EditingEntryId);
        Assert.Null(session.EditingRevisionId);

        await session.SaveEntryAsync();

        var cloned = Assert.Single(session.CurrentEntries, entry => entry.EntryId != original.EntryId);
        var cloneOperation = Assert.IsType<CreateEntryOperation>(session.Document.Operations.Last());
        Assert.Equal(cloned.EntryId, cloneOperation.EntryId);
        Assert.NotEqual(original.EntryId, cloned.EntryId);
    }

    [Fact]
    public async Task Gate3EntryJourneyAddsEditsClonesDeletesAndReloadsFromBrowserStorage()
    {
        var jsRuntime = new JourneyJsRuntime();
        var session = CreateSession(jsRuntime);

        await session.EnsureLoadedAsync();
        FillDraft(session.Draft, "VH-ADD", 1.0m);
        await session.SaveEntryAsync();

        var added = Assert.Single(session.CurrentEntries);
        Assert.Equal("Flight added.", session.LastActionMessage);
        Assert.Equal("VH-ADD", added.Entry?.Registration);
        Assert.Equal(1, jsRuntime.SaveCount);

        session.EditEntry(added);
        session.Draft.Registration = "VH-EDIT";
        session.Draft.PilotInCommand = 1.2m;
        session.Draft.Day = 1.2m;
        await session.SaveEntryAsync();

        var edited = Assert.Single(session.CurrentEntries);
        Assert.Equal("Correction saved.", session.LastActionMessage);
        Assert.Equal("VH-EDIT", edited.Entry?.Registration);
        Assert.Equal(2, edited.RevisionHistory.Count);

        session.CloneEntry(edited.Entry!);
        Assert.Null(session.EditingEntryId);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), session.Draft.Date);
        Assert.Equal("VH-EDIT", session.Draft.Registration);
        Assert.Null(session.Draft.TakeoffsDay);
        Assert.Null(session.Draft.TakeoffsNight);
        Assert.Equal("Draft started from recent flight.", session.LastActionMessage);
        Assert.True(session.ShouldShowLastActionMessage(MobileActionMessageSurface.Draft));
        Assert.False(session.ShouldShowLastActionMessage(MobileActionMessageSurface.Logbook));
        await session.SaveEntryAsync();

        Assert.Equal(2, session.CurrentEntries.Count);
        Assert.Equal(3, session.Document.Operations.Count);
        Assert.True(session.ShouldShowLastActionMessage(MobileActionMessageSurface.Logbook));

        await session.DeleteEntryAsync(edited);

        Assert.Equal("Entry deleted.", session.LastActionMessage);
        Assert.Single(session.CurrentEntries);
        var deleted = Assert.Single(session.DeletedEntries);
        Assert.Equal(edited.EntryId, deleted.EntryId);
        Assert.True(session.FindHistory(edited.EntryId.Value)?.IsDeleted);

        var reloaded = CreateSession(jsRuntime);
        await reloaded.EnsureLoadedAsync();

        Assert.Single(reloaded.CurrentEntries);
        Assert.Single(reloaded.DeletedEntries);
        Assert.Equal(session.Document.Operations.Count, reloaded.Document.Operations.Count);
        Assert.Equal("Ready", reloaded.PackageKeyStatus);
    }

    [Fact]
    public async Task Gate3ConflictJourneyLoadsConflictAndPersistsResolution()
    {
        var jsRuntime = new JourneyJsRuntime();
        var create = CreateOperation("rev_create", "VH-BASE", 1.0m);
        var local = CorrectOperation(create, "rev_local", "VH-LOCAL", 1.1m, new DeviceId("dev_mobile"));
        var incoming = CorrectOperation(create, "rev_incoming", "VH-IMPORT", 1.4m, new DeviceId("dev_excel"));
        var document = PortableLogbookDocument.CreateAustraliaFirst(
            create.LogbookId,
            MobileLogbookSession.CustomFields,
            [create, local, incoming]);
        await new BrowserLogbookStore(jsRuntime).SaveStateAsync(new BrowserLogbookState(document, [], null));

        var session = CreateSession(jsRuntime);
        await session.EnsureLoadedAsync();

        var conflict = Assert.Single(session.MergeResult.Conflicts);
        Assert.Equal(create.EntryId, conflict.EntryId);
        Assert.Equal([incoming.RevisionId, local.RevisionId], conflict.HeadRevisionIds);

        await session.ResolveConflictAsync(conflict, local.RevisionId);

        Assert.Empty(session.MergeResult.Conflicts);
        var resolved = Assert.Single(session.CurrentEntries);
        Assert.Equal("VH-LOCAL", resolved.Entry?.Registration);
        Assert.Equal("Conflict resolved.", session.LastActionMessage);
        Assert.IsType<ResolveConflictOperation>(session.Document.Operations.Last());

        var reloaded = CreateSession(jsRuntime);
        await reloaded.EnsureLoadedAsync();

        Assert.Empty(reloaded.MergeResult.Conflicts);
        Assert.Equal("VH-LOCAL", Assert.Single(reloaded.CurrentEntries).Entry?.Registration);
    }

    private static MobileLogbookSession CreateSession(
        JourneyJsRuntime jsRuntime,
        IHostedLogbookAuthenticator? hostedAuthenticator = null,
        ISyncClock? syncClock = null,
        IHostedLogbookLedger? hostedLedger = null,
        INetworkStatus? networkStatus = null,
        IMobileRecoveryEnvelopeService? recoveryEnvelopeService = null,
        IMobileGoogleHostedAuthenticator? googleAuthenticator = null,
        IMobileReplacementRecoveryWorkflow? replacementRecovery = null,
        MobileConnectionRecoveryWorkflow? connectionRecovery = null) =>
        new(
            new BrowserLogbookStore(jsRuntime),
            new BrowserPackageKeyStore(jsRuntime),
            hostedAuthenticator: hostedAuthenticator,
            hostedLedger: hostedLedger,
            networkStatus: networkStatus,
            syncClock: syncClock,
            connectionRecovery: connectionRecovery,
            recoveryEnvelopeService: recoveryEnvelopeService,
            googleAuthenticator: googleAuthenticator,
            replacementRecovery: replacementRecovery);

    private sealed class InactiveDeviceRecoveryClient(
        HostedAccountId accountId,
        DeviceId deviceId,
        DateTimeOffset expiresAt) : IMobileHostedRecoveryClient
    {
        public ValueTask ValidateConfigAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<MobileHostedCredentialSnapshot> LoadCredentialSnapshotAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new MobileHostedCredentialSnapshot(
                MobileCredentialState.Registered,
                accountId,
                deviceId,
                expiresAt));

        public ValueTask ValidateAccessTokenAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<MobileHostedPrincipal> ReadAuthUserAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new MobileHostedPrincipal(accountId));

        public ValueTask<MobileHostedAccountCheck> ReadAccountAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new MobileHostedAccountCheck(true, true, true));

        public ValueTask<MobileHostedDeviceCheck> ReadDeviceAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new MobileHostedDeviceCheck(true, false, true));

        public ValueTask<HostedSyncSession> GetRegisteredSessionAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new HostedSyncSession(accountId, deviceId, expiresAt));
    }

    private static void FillDraft(EntryDraft draft, string registration, decimal hours)
    {
        draft.Date = DateOnly.FromDateTime(DateTime.Today);
        draft.AircraftType = "C172";
        draft.Registration = registration;
        draft.FlightNumber = "AD332";
        draft.From = "YSCN";
        draft.To = "YMML";
        draft.Route = "YSCN YMML";
        draft.PilotInCommand = hours;
        draft.Day = hours;
        draft.TakeoffsDay = 1;
        draft.LandingsDay = 1;
    }

    private static void FillWorkbookDraft(MobileWorkbookEntryDraft draft)
    {
        draft.Date = new DateOnly(2026, 7, 24);
        draft.Type = "C172";
        draft.Reg = "VH-WBV";
        draft.FlightId = "AD332";
        draft.Pic = "Alex";
        draft.OtherPilotOrCrew = "Jamie";
        draft.From = "YSBK";
        draft.To = "YSCN";
        draft.Via = "YWOL";
        draft.Remarks = "Workbook faithful";
        draft.FlightReview = true;
        draft.SeCommandDay = 1.1m;
        draft.IfrIf = 0.4m;
        draft.LandingsDay = 1;
        draft.Ils = 2;
    }

    private static CreateEntryOperation CreateOperation(string revisionId, string registration, decimal hours)
    {
        var logbookId = new LogbookId("log_mobile_preview");
        return new CreateEntryOperation(
            logbookId,
            new EntryId("ent_gate3"),
            new RevisionId(revisionId),
            new DeviceId("dev_seed"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            Entry(registration, hours));
    }

    private static CorrectEntryOperation CorrectOperation(
        CreateEntryOperation parent,
        string revisionId,
        string registration,
        decimal hours,
        DeviceId deviceId) =>
        new(
            parent.LogbookId,
            parent.EntryId,
            new RevisionId(revisionId),
            new HashSet<RevisionId> { parent.RevisionId },
            deviceId,
            parent.CreatedAt.AddMinutes(1),
            Entry(registration, hours));

    private static PortableLogbookEntry Entry(string registration, decimal hours) =>
        PortableLogbookEntry.Empty with
        {
            Date = DateOnly.Parse("2026-07-18"),
            AircraftType = "C172",
            Registration = registration,
            FlightNumber = "AD332",
            From = "YSCN",
            To = "YMML",
            Route = "YSCN YMML",
            PilotInCommand = hours,
            Day = hours,
            TakeoffsDay = 1,
            LandingsDay = 1
        };

    private sealed class JourneyJsRuntime : IJSRuntime
    {
        public string? StoredJson { get; private set; }

        public int SaveCount { get; private set; }

        public bool FailNextPackageKeyImport { get; set; }

        public bool FailRecoveryBridge { get; set; }

        public int RecoveryWrapCount { get; private set; }

        public string? GeneratedRecoveryCode { get; private set; }

        public List<string> ImportedPackageKeys { get; } = [];

        private Dictionary<string, byte[]> PackageKeys { get; } = [];

        private Dictionary<string, string> StoredValues { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            return InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return identifier switch
            {
                "electronicLogbookStore.load" => Load<TValue>(args),
                "electronicLogbookStore.save" => Save<TValue>(args),
                "electronicLogbookKeys.isSupported" => new ValueTask<TValue>((TValue)(object)true),
                "electronicLogbookKeys.hasPackageKey" => new ValueTask<TValue>((TValue)(object)HasPackageKey(args)),
                "electronicLogbookKeys.importPackageKey" => ImportPackageKey<TValue>(args),
                "electronicLogbookKeys.getRecoveryPublicKey" => GetRecoveryPublicKey<TValue>(),
                "electronicLogbookKeys.wrapPackageKeyForRecoveryService" => WrapPackageKeyForRecoveryService<TValue>(args),
                "electronicLogbookKeys.wrapPackageKeyForRecoveryCode" => WrapPackageKeyForRecoveryCode<TValue>(args),
                "electronicLogbookKeys.testRecoveryCodeEnvelope" => TestRecoveryCodeEnvelope<TValue>(args),
                "electronicLogbookKeys.importRecoveryCodeEnvelope" => new ValueTask<TValue>((TValue)(object)true),
                "electronicLogbookKeys.encrypt" => Encrypt<TValue>(args),
                "electronicLogbookKeys.decrypt" => Decrypt<TValue>(args),
                _ => throw new JSException($"Unexpected JS call: {identifier}")
            };
        }

        private ValueTask<TValue> Load<TValue>(object?[]? args)
        {
            Assert.NotNull(args);
            var key = Assert.IsType<string>(args[0]);
            var value = key == "portable-document"
                ? StoredJson
                : StoredValues.GetValueOrDefault(key);
            return new ValueTask<TValue>((TValue)(object?)value!);
        }

        private ValueTask<TValue> Save<TValue>(object?[]? args)
        {
            Assert.NotNull(args);
            var key = Assert.IsType<string>(args[0]);
            var value = Assert.IsType<string>(args[1]);
            if (key != "portable-document")
            {
                StoredValues[key] = value;
                return new ValueTask<TValue>(default(TValue)!);
            }

            StoredJson = value;
            SaveCount++;
            JsonSerializer.Deserialize<BrowserLogbookStoredDocument>(StoredJson, PortableLogbookJson.SerializerOptions);
            return new ValueTask<TValue>(default(TValue)!);
        }

        private bool HasPackageKey(object?[]? args)
        {
            Assert.NotNull(args);
            var keyName = Assert.IsType<string>(args[0]);
            return PackageKeys.Count == 0 || PackageKeys.ContainsKey(keyName);
        }

        private ValueTask<TValue> ImportPackageKey<TValue>(object?[]? args)
        {
            if (FailNextPackageKeyImport)
            {
                FailNextPackageKeyImport = false;
                throw new JSException("Simulated interrupted local package-key setup.");
            }

            Assert.NotNull(args);
            var keyName = Assert.IsType<string>(args[0]);
            PackageKeys[keyName] = Assert.IsType<byte[]>(args[1]).ToArray();
            ImportedPackageKeys.Add(keyName);
            return new ValueTask<TValue>((TValue)(object)true);
        }

        private ValueTask<TValue> GetRecoveryPublicKey<TValue>()
        {
            if (FailRecoveryBridge)
            {
                throw new JSException("Recovery bridge unavailable. owner@example.com package_key=secret-value");
            }

            var material = PublicMaterial(4);
            return new ValueTask<TValue>((TValue)(object)new BrowserRecoveryPublicKey(
                material.PublicKey,
                material.Fingerprint,
                "RSA-OAEP-256"));
        }

        private ValueTask<TValue> WrapPackageKeyForRecoveryService<TValue>(object?[]? args)
        {
            Assert.NotNull(args);
            Assert.True(PackageKeys.ContainsKey(Assert.IsType<string>(args[0])));
            Assert.Equal(PublicMaterial(3).PublicKey, Assert.IsType<string>(args[1]));
            RecoveryWrapCount++;
            return new ValueTask<TValue>((TValue)(object)new BrowserRecoveryWrappedKey(
                "device-wrapped-package-key",
                "RSA-OAEP-256"));
        }

        private ValueTask<TValue> WrapPackageKeyForRecoveryCode<TValue>(object?[]? args)
        {
            Assert.NotNull(args);
            GeneratedRecoveryCode = Assert.IsType<string>(args[1]);
            return new ValueTask<TValue>((TValue)(object)new MobileRecoveryCodeEnvelopePayload(
                Convert.ToBase64String(new byte[48]),
                Convert.ToBase64String(new byte[12]),
                Convert.ToBase64String(new byte[16]),
                MobileRecoveryCodeEnvelope.Algorithm,
                MobileRecoveryCodeEnvelope.KeyVersionId));
        }

        private ValueTask<TValue> TestRecoveryCodeEnvelope<TValue>(object?[]? args)
        {
            Assert.NotNull(args);
            return new ValueTask<TValue>((TValue)(object)string.Equals(
                GeneratedRecoveryCode,
                Assert.IsType<string>(args[1]),
                StringComparison.Ordinal));
        }

        private ValueTask<TValue> Encrypt<TValue>(object?[]? args)
        {
            Assert.NotNull(args);
            var key = PackageKeys[Assert.IsType<string>(args[0])];
            var nonce = Assert.IsType<byte[]>(args[1]);
            var plaintext = Assert.IsType<byte[]>(args[2]);
            var additionalData = Assert.IsType<byte[]>(args[3]);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[16];
            using var aes = new AesGcm(key, tag.Length);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, additionalData);
            return new ValueTask<TValue>((TValue)(object)new BrowserPackageCiphertext(ciphertext, tag));
        }

        private ValueTask<TValue> Decrypt<TValue>(object?[]? args)
        {
            Assert.NotNull(args);
            var key = PackageKeys[Assert.IsType<string>(args[0])];
            var nonce = Assert.IsType<byte[]>(args[1]);
            var ciphertext = Assert.IsType<byte[]>(args[2]);
            var tag = Assert.IsType<byte[]>(args[3]);
            var additionalData = Assert.IsType<byte[]>(args[4]);
            var plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(key, tag.Length);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, additionalData);
            return new ValueTask<TValue>((TValue)(object)plaintext);
        }
    }

    private sealed class TransportFailingLedger(IHostedLogbookLedger inner) : IHostedLogbookLedger
    {
        public bool FailTransport { get; set; }

        public ValueTask<HostedOperationPage> ReadMissingOperationsAsync(
            LogbookId logbookId,
            long afterHostedRevision,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            FailTransport
                ? ValueTask.FromException<HostedOperationPage>(new HttpRequestException("Simulated unreachable hosted transport."))
                : inner.ReadMissingOperationsAsync(logbookId, afterHostedRevision, pageSize, cancellationToken);

        public ValueTask<HostedAppendResult> AppendOperationsAsync(
            LogbookId logbookId,
            DeviceId deviceId,
            IReadOnlyList<HostedOperationUpload> operations,
            CancellationToken cancellationToken = default) =>
            FailTransport
                ? ValueTask.FromException<HostedAppendResult>(new HttpRequestException("Simulated unreachable hosted transport."))
                : inner.AppendOperationsAsync(logbookId, deviceId, operations, cancellationToken);

        public ValueTask RecordAcknowledgementAsync(
            LogbookId logbookId,
            DeviceId deviceId,
            long throughHostedRevision,
            CancellationToken cancellationToken = default) =>
            FailTransport
                ? ValueTask.FromException(new HttpRequestException("Simulated unreachable hosted transport."))
                : inner.RecordAcknowledgementAsync(logbookId, deviceId, throughHostedRevision, cancellationToken);
    }

    private sealed class ThrowingNetworkStatus : INetworkStatus
    {
        public ValueTask<NetworkAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromException<NetworkAvailability>(
                new JSException("Android network bridge unavailable during offline cold-start."));
    }

    private sealed class BlockingAppendLedger(IHostedLogbookLedger inner) : IHostedLogbookLedger
    {
        private readonly TaskCompletionSource firstAppendStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseFirstAppend = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int AppendCallCount { get; private set; }

        public Task FirstAppendStarted => firstAppendStarted.Task;

        public void ReleaseFirstAppend() => releaseFirstAppend.TrySetResult();

        public ValueTask<HostedOperationPage> ReadMissingOperationsAsync(
            LogbookId logbookId,
            long afterHostedRevision,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            inner.ReadMissingOperationsAsync(logbookId, afterHostedRevision, pageSize, cancellationToken);

        public async ValueTask<HostedAppendResult> AppendOperationsAsync(
            LogbookId logbookId,
            DeviceId deviceId,
            IReadOnlyList<HostedOperationUpload> operations,
            CancellationToken cancellationToken = default)
        {
            AppendCallCount++;
            if (AppendCallCount == 1)
            {
                firstAppendStarted.TrySetResult();
                await releaseFirstAppend.Task.WaitAsync(cancellationToken);
            }

            return await inner.AppendOperationsAsync(logbookId, deviceId, operations, cancellationToken);
        }

        public ValueTask RecordAcknowledgementAsync(
            LogbookId logbookId,
            DeviceId deviceId,
            long throughHostedRevision,
            CancellationToken cancellationToken = default) =>
            inner.RecordAcknowledgementAsync(logbookId, deviceId, throughHostedRevision, cancellationToken);
    }

    private sealed class AppendThenLoseResponseLedger(IHostedLogbookLedger inner) : IHostedLogbookLedger
    {
        private bool loseNextAppendResponse = true;

        public ValueTask<HostedOperationPage> ReadMissingOperationsAsync(
            LogbookId logbookId,
            long afterHostedRevision,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            inner.ReadMissingOperationsAsync(logbookId, afterHostedRevision, pageSize, cancellationToken);

        public async ValueTask<HostedAppendResult> AppendOperationsAsync(
            LogbookId logbookId,
            DeviceId deviceId,
            IReadOnlyList<HostedOperationUpload> operations,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.AppendOperationsAsync(logbookId, deviceId, operations, cancellationToken);
            if (loseNextAppendResponse)
            {
                loseNextAppendResponse = false;
                throw new HttpRequestException("Simulated response loss after the server committed the operation.");
            }

            return result;
        }

        public ValueTask RecordAcknowledgementAsync(
            LogbookId logbookId,
            DeviceId deviceId,
            long throughHostedRevision,
            CancellationToken cancellationToken = default) =>
            inner.RecordAcknowledgementAsync(logbookId, deviceId, throughHostedRevision, cancellationToken);
    }

    private static (string PublicKey, string Fingerprint) PublicMaterial(byte value)
    {
        var encoded = Enumerable.Repeat(value, 294).ToArray();
        return (
            Convert.ToBase64String(encoded),
            Convert.ToHexString(SHA256.HashData(encoded)).ToLowerInvariant());
    }

    private sealed class RecordingGoogleAuthenticator(Exception? signInException = null)
        : IMobileGoogleHostedAuthenticator
    {
        public int SignInCount { get; private set; }

        public ValueTask<HostedSyncSession> SignInWithGoogleAsync(
            CancellationToken cancellationToken = default)
        {
            SignInCount++;
            return signInException is null
                ? ValueTask.FromResult(new HostedSyncSession(
                    new HostedAccountId("acct_private"),
                    new DeviceId("dev_android"),
                    DateTimeOffset.Parse("2099-01-01T00:00:00Z")))
                : ValueTask.FromException<HostedSyncSession>(signInException);
        }

        public ValueTask<HostedSyncSession> LinkGoogleIdentityAsync(
            CancellationToken cancellationToken = default) =>
            SignInWithGoogleAsync(cancellationToken);
    }

    private sealed class RecoveryRequiredEmailAuthenticator(
        HostedAccountId accountId,
        ISyncClock clock) : IHostedLogbookAuthenticator
    {
        public int CompleteCount { get; private set; }

        public ValueTask<HostedSyncSession?> GetCurrentSessionAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<HostedSyncSession?>(null);

        public ValueTask<HostedSignInStart> StartEmailSignInAsync(
            string email,
            bool shouldCreateUser = false,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new HostedSignInStart(
                accountId,
                "p***@example.com",
                clock.UtcNow.AddMinutes(10)));

        public ValueTask<HostedSyncSession> CompleteEmailSignInAsync(
            string verificationCode,
            CancellationToken cancellationToken = default)
        {
            CompleteCount++;
            return ValueTask.FromException<HostedSyncSession>(new HostedSignInException(
                HostedSignInFailureReason.AccountRecoveryRequired,
                "Existing account recovery is required."));
        }

        public ValueTask<HostedSyncSession> ResumeEmailSignInAsync(
            CancellationToken cancellationToken = default) =>
            CompleteEmailSignInAsync(string.Empty, cancellationToken);

        public ValueTask<HostedSyncSession> RefreshAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<HostedSyncSession>(new NotSupportedException());

        public ValueTask SignOutAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class MutableSyncClock(DateTimeOffset utcNow) : ISyncClock
    {
        public DateTimeOffset UtcNow { get; private set; } = utcNow;

        public void Advance(TimeSpan duration) => UtcNow = UtcNow.Add(duration);
    }

    private sealed class RecordingReplacementRecoveryWorkflow(
        MobileReplacementRecoveryResult? result = null) : IMobileReplacementRecoveryWorkflow
    {
        public int AutomaticRecoveryCount { get; private set; }

        public ValueTask<MobileReplacementRecoveryResult> RecoverOnlyLogbookAsync(
            CancellationToken cancellationToken = default)
        {
            AutomaticRecoveryCount++;
            return ValueTask.FromResult(result
                ?? throw new InvalidOperationException("No replacement recovery result was configured."));
        }

        public ValueTask<MobileReplacementRecoveryResult> RecoverAsync(
            LogbookId logbookId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(result
                ?? throw new InvalidOperationException("No replacement recovery result was configured."));

        public ValueTask<MobileReplacementRecoveryResult> RecoverOnlyLogbookWithCodeAsync(
            string recoveryCode,
            CancellationToken cancellationToken = default) =>
            RecoverOnlyLogbookAsync(cancellationToken);
    }

    private sealed class RecordingRecoveryEnvelopeService(bool failEnrollment = false)
        : IMobileRecoveryEnvelopeService
    {
        public List<MobileRecoveryEnvelopeEnrollmentRequest> EnrollmentRequests { get; } = [];
        public List<MobileRecoveryCodeEnrollmentRequest> RecoveryCodeEnrollmentRequests { get; } = [];

        public ValueTask<MobileRecoveryEnvelopeConfiguration> GetConfigurationAsync(
            CancellationToken cancellationToken = default)
        {
            var material = PublicMaterial(3);
            return ValueTask.FromResult(new MobileRecoveryEnvelopeConfiguration(
                material.PublicKey,
                material.Fingerprint,
                "RSA-OAEP-256",
                "managed-key-v1"));
        }

        public ValueTask<MobileRecoverySetupStatus> GetRecoverySetupStatusAsync(
            MobileRecoverySetupStatusRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new MobileRecoverySetupStatus(
                EnrollmentRequests.Count > 0,
                RecoveryCodeEnrollmentRequests.Count > 0));

        public ValueTask<MobileRecoveryEnvelopeEnrollmentResult> EnrollAsync(
            MobileRecoveryEnvelopeEnrollmentRequest request,
            CancellationToken cancellationToken = default)
        {
            EnrollmentRequests.Add(request);
            if (failEnrollment)
            {
                throw new MobileHostedDiagnosticException(
                    "RECOVERY_SERVICE_REJECTED",
                    "pilot@example.com secret-token package_key=secret-value");
            }

            return ValueTask.FromResult(new MobileRecoveryEnvelopeEnrollmentResult(true, "managed-key-v1"));
        }

        public ValueTask<MobileRecoveryEnvelopeRestoreResult> RestoreAsync(
            MobileRecoveryEnvelopeRestoreRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<MobileRecoveryCodeEnrollmentResult> EnrollRecoveryCodeAsync(
            MobileRecoveryCodeEnrollmentRequest request,
            CancellationToken cancellationToken = default)
        {
            RecoveryCodeEnrollmentRequests.Add(request);
            return ValueTask.FromResult(new MobileRecoveryCodeEnrollmentResult(true));
        }

        public ValueTask<MobileRecoveryCodeEnvelopePayload> RestoreWithRecoveryCodeAsync(
            MobileRecoveryCodeRestoreRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<MobileRecoveryDeviceActivationResult> ActivateAsync(
            MobileRecoveryDeviceActivationRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new MobileRecoveryDeviceActivationResult(true));
    }
}
