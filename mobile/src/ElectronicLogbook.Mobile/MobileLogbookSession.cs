using ElectronicLogbook.Portable;
using Microsoft.JSInterop;

namespace ElectronicLogbook.Mobile;

public sealed class MobileLogbookSession(
    BrowserLogbookStore logbookStore,
    BrowserPackageKeyStore packageKeyStore,
    PortableLogbookIdFactory? portableIdFactory = null,
    IHostedLogbookAuthenticator? hostedAuthenticator = null,
    IHostedLogbookLedger? hostedLedger = null,
    INetworkStatus? networkStatus = null,
    ISyncClock? syncClock = null,
    MobileConnectionRecoveryWorkflow? connectionRecovery = null,
    IMobileGoogleHostedAuthenticator? googleAuthenticator = null,
    IMobileRecoveryEnvelopeService? recoveryEnvelopeService = null,
    IMobileReplacementRecoveryWorkflow? replacementRecovery = null)
{
    public event Action? HostedSyncChanged;

    private DeviceId deviceId = new("dev_mobile_preview");
    private readonly PortableLogbookIdFactory portableIdFactory = portableIdFactory ?? PortableLogbookIdFactory.Default;
    private readonly ISyncClock syncClock = syncClock ?? SystemSyncClock.Instance;
    private readonly HashSet<(LogbookId LogbookId, DeviceId DeviceId)> verifiedRecoveryEnrollments = [];
    private readonly HashSet<(LogbookId LogbookId, DeviceId DeviceId)> verifiedRecoveryCodeConfigurations = [];
    private MobileRecoveryCodeSetup? pendingRecoveryCodeSetup;

    public static readonly CustomFieldDefinition[] CustomFields =
    [
        new(new CustomFieldId("cf_workbook_1"), "Custom 1", 1),
        new(new CustomFieldId("cf_workbook_2"), "Custom 2", 2),
        new(new CustomFieldId("cf_workbook_3"), "Custom 3", 3),
        new(new CustomFieldId("cf_workbook_4"), "Custom 4", 4)
    ];

    public PortableLogbookDocument Document { get; private set; } =
        PortableLogbookDocument.CreateAustraliaFirst(new LogbookId("log_mobile_preview"), CustomFields, []);

    private PortableLogbookDocumentV2 documentV2 =
        PortableLogbookDocumentV2.CreateAustraliaFirst(
            new LogbookId("log_mobile_preview"),
            CustomFields,
            PortableLogbookCurrencyOverrideDates.Empty,
            []);

    private PortableLogbookMergeResultV2? mergeResultV2Cache;
    private IReadOnlyList<PortableLogbookMaterializedEntryV2>? currentEntriesV2Cache;
    private IReadOnlyList<PortableLogbookMaterializedEntryV2>? deletedEntriesV2Cache;
    private MobileCurrencyRecencySummary? currencyRecencySummaryCache;
    private DateOnly? currencyRecencySummaryDate;

    public PortableLogbookDocumentV2 DocumentV2
    {
        get => documentV2;
        private set
        {
            documentV2 = value;
            InvalidateWorkbookProjectionCache();
        }
    }

    public IReadOnlyList<PortableLogbookPackageReceipt> ImportReceipts { get; private set; } = [];

    public DateTimeOffset? LastSuccessfulExportAt { get; private set; }

    public BrowserLogbookExportCheckpoint? LastSuccessfulExport { get; private set; }

    public BrowserHostedSyncState? HostedSync { get; private set; }

    public HostedSignInStart? PendingHostedSignIn { get; private set; }

    public MobileConnectionDiagnosticReport? LastConnectionDiagnostics { get; private set; }

    public IReadOnlyList<MobileConnectionDiagnosticReport> ConnectionDiagnosticHistory { get; private set; } = [];

    public EntryDraft Draft { get; private set; } = EntryDraft.Create();

    public MobileWorkbookEntryDraft WorkbookDraft { get; private set; } =
        MobileWorkbookEntryDraft.Create(CustomFields);

    public IReadOnlyList<CustomFieldDefinition> WorkbookCustomFields =>
        DocumentV2.CustomFieldDefinitions.Count > 0
            ? DocumentV2.CustomFieldDefinitions.OrderBy(customField => customField.Order).ToArray()
            : CustomFields;

    public bool HasAttemptedSubmit { get; private set; }

    public bool HasEditedDraft { get; private set; }

    public EntryId? EditingEntryId { get; private set; }

    public RevisionId? EditingRevisionId { get; private set; }

    private bool IsLegacyLoaded { get; set; }

    public bool IsLoaded { get; private set; }

    public bool IsStorageBlocked { get; private set; }

    public string? StorageError { get; private set; }

    public string PackageKeyStatus { get; private set; } = "Checking";

    public bool HasHostedSync => HostedSync is not null;

    public string? PendingRecoveryCode => pendingRecoveryCodeSetup?.RecoveryCode;

    public bool IsRecoveryCodeConfirmationPending => pendingRecoveryCodeSetup is not null;

    public string HostedSyncStatusLabel =>
        HostedSync?.LastStatus switch
        {
            PortableHostedSyncStatus.Synced => "Synced",
            PortableHostedSyncStatus.Waiting => "Waiting",
            PortableHostedSyncStatus.Offline => "Offline",
            PortableHostedSyncStatus.SigningIn => "Signing in",
            PortableHostedSyncStatus.NeedsAttention => "Needs attention",
            _ => "Not connected"
        };

    public string HostedSyncStatusDetail
    {
        get
        {
            if (HostedSync is null)
            {
                return "Connect an invited account to use automatic hosted sync.";
            }

            return HostedSync.LastStatus switch
            {
                PortableHostedSyncStatus.Synced => HostedSync.LastSyncedAt is DateTimeOffset syncedAt
                    ? $"Last synced {FormatTimestamp(syncedAt)}."
                    : "This device is connected and ready to sync.",
                PortableHostedSyncStatus.Waiting => "More hosted operations are waiting; sync will continue shortly.",
                PortableHostedSyncStatus.Offline => HostedSync.PendingLocalOperationCount > 0
                    ? $"{HostedSync.PendingLocalOperationCount} local operation(s) will sync when the network returns."
                    : "This device is offline; local edits remain available.",
                PortableHostedSyncStatus.SigningIn => "Finish signing in to resume hosted sync.",
                PortableHostedSyncStatus.NeedsAttention => HostedSync.AttentionRequiredReason ?? "Hosted sync needs attention.",
                _ => "Hosted sync is not connected."
            };
        }
    }

    public string? LastActionMessage { get; private set; }

    private MobileActionMessageScope LastActionMessageScope { get; set; } = MobileActionMessageScope.Global;

    public bool ShouldShowLastActionMessage(MobileActionMessageSurface surface) =>
        !string.IsNullOrWhiteSpace(LastActionMessage) &&
        (LastActionMessageScope == MobileActionMessageScope.Global ||
            surface == MobileActionMessageSurface.Draft);

    public PortableLogbookMergeResult MergeResult =>
        PortableLogbookMerger.Merge(Document.Operations);

    public PortableLogbookMergeResultV2 MergeResultV2 =>
        mergeResultV2Cache ??= PortableLogbookWorkbookProjection.MergeV2(DocumentV2.Operations);

    public IReadOnlyList<PortableLogbookMaterializedEntry> CurrentEntries =>
        MergeResult
            .Entries
            .Values
            .Where(entry => !entry.IsDeleted && entry.Entry is not null)
            .OrderByDescending(entry => entry.Entry!.Date)
            .ToArray();

    public IReadOnlyList<PortableLogbookMaterializedEntryV2> CurrentEntriesV2 =>
        currentEntriesV2Cache ??= MergeResultV2
            .Entries
            .Values
            .Where(entry => !entry.IsDeleted && entry.Entry is not null)
            .OrderByDescending(entry => entry.Entry!.Date)
            .ToArray();

    public MobileCurrencyRecencySummary GetCurrencyRecencySummary(DateOnly today)
    {
        if (currencyRecencySummaryCache is null || currencyRecencySummaryDate != today)
        {
            currencyRecencySummaryCache = MobileCurrencyRecencySummary.Create(
                CurrentEntriesV2.Select(materialized => materialized.Entry!),
                DocumentV2.CurrencyOverrideDates,
                today);
            currencyRecencySummaryDate = today;
        }

        return currencyRecencySummaryCache;
    }

    public PortableLogbookExchangeStatusSnapshot ExchangeStatus
    {
        get
        {
            var exportRequired = DocumentV2.Operations.Count > 0 &&
                LastSuccessfulExport?.Covers(DocumentV2) != true;
            var pendingOperations = exportRequired
                ? DocumentV2.Operations.ToArray()
                : Array.Empty<PortableLogbookOperationV2>();
            return PortableLogbookExchangeStatus.Create(
                new PortableLogbookWorkingCopyState(
                    exportRequired,
                    pendingOperations.Length,
                    pendingOperations.Count(operation => operation.Kind == PortableOperationKind.Create),
                    pendingOperations.Count(operation => operation.Kind == PortableOperationKind.Correction),
                    pendingOperations.Count(operation => operation.Kind == PortableOperationKind.Deletion),
                    pendingOperations.Select(operation => operation.RevisionId).ToArray(),
                    DateTimeOffset.UtcNow),
                ImportReceipts,
                LastSuccessfulExportAt);
        }
    }

    public IReadOnlyList<string> DraftErrors => ValidateDraft();

    public IReadOnlyList<string> WorkbookDraftErrors => ValidateWorkbookDraft();

    public IReadOnlyList<string> DraftWarnings => ValidateDraftWarnings();

    public bool ShouldShowDraftErrors => DraftErrors.Count > 0 && (HasAttemptedSubmit || HasEditedDraft);

    public bool ShouldShowWorkbookDraftErrors => WorkbookDraftErrors.Count > 0 && (HasAttemptedSubmit || HasEditedDraft);

    public string DraftModeLabel => EditingEntryId is null ? "New flight" : "Correction";

    public string SaveLabel => EditingEntryId is null ? "Add flight" : "Save correction";

    public string PackageKeyNotice => MobilePackageKeyNotice.Create(PackageKeyStatus);

    public IReadOnlyList<PortableLogbookMaterializedEntry> DeletedEntries =>
        MergeResult
            .Entries
            .Values
            .Where(entry => entry.IsDeleted)
            .OrderByDescending(entry => FindOperation(entry.CurrentRevisionId).CreatedAt)
            .ToArray();

    public IReadOnlyList<PortableLogbookMaterializedEntryV2> DeletedEntriesV2 =>
        deletedEntriesV2Cache ??= MergeResultV2
            .Entries
            .Values
            .Where(entry => entry.IsDeleted)
            .OrderByDescending(entry => FindOperationV2(entry.CurrentRevisionId).CreatedAt)
            .ToArray();

    public async Task EnsureLoadedAsync()
    {
        if (IsLegacyLoaded)
        {
            return;
        }

        try
        {
            var state = await logbookStore.LoadStateAsync();
            if (state is not null)
            {
                Document = state.Document;
                ImportReceipts = state.ImportReceipts;
                LastSuccessfulExportAt = state.LastSuccessfulExportAt;
                LastSuccessfulExport = state.LastSuccessfulExport;
                HostedSync = state.HostedSync;
                deviceId = HostedSync?.DeviceId ?? deviceId;
            }

            await RefreshPackageKeyStatusAsync();
            ResetDraft();
        }
        catch (BrowserLogbookStoreException ex)
        {
            StorageError = ex.Message + " Export a valid backup package from a compatible device before using this app version.";
            IsStorageBlocked = true;
        }
        finally
        {
            IsLegacyLoaded = true;
        }
    }

    public async Task EnsureLoadedWorkbookAsync()
    {
        if (IsLoaded)
        {
            return;
        }

        try
        {
            var state = await logbookStore.LoadStateV2Async();
            ConnectionDiagnosticHistory = await logbookStore.LoadConnectionDiagnosticsAsync();
            LastConnectionDiagnostics = ConnectionDiagnosticHistory.FirstOrDefault();
            if (state is not null)
            {
                DocumentV2 = state.Document;
                ImportReceipts = state.ImportReceipts;
                LastSuccessfulExportAt = state.LastSuccessfulExportAt;
                LastSuccessfulExport = state.LastSuccessfulExport;
                HostedSync = state.HostedSync;
                deviceId = HostedSync?.DeviceId ?? deviceId;
            }

            await RefreshPackageKeyStatusAsync(DocumentV2.LogbookId);
            ResetWorkbookDraft();
            await TrySyncHostedAsync(BackgroundSyncReason.LaunchOrResume);
        }
        catch (BrowserLogbookStoreException ex)
        {
            StorageError = ex.Message + " Re-import the authoritative workbook to create a workbook-faithful mobile logbook.";
            IsStorageBlocked = true;
        }
        finally
        {
            IsLoaded = true;
        }
    }

    public async Task SaveEntryAsync()
    {
        if (IsStorageBlocked)
        {
            return;
        }

        HasAttemptedSubmit = true;
        ClearLastActionMessage();
        if (DraftErrors.Count > 0)
        {
            return;
        }

        PortableLogbookOperation operation = EditingEntryId is null || EditingRevisionId is null
            ? new CreateEntryOperation(
                Document.LogbookId,
                portableIdFactory.NewEntryIdExcluding(Document.Operations.Select(operation => operation.EntryId).ToHashSet()),
                RevisionId.New(),
                deviceId,
                DateTimeOffset.UtcNow,
                Draft.ToEntry(CustomFields))
            : new CorrectEntryOperation(
                Document.LogbookId,
                EditingEntryId.Value,
                RevisionId.New(),
                new HashSet<RevisionId> { EditingRevisionId.Value },
                deviceId,
                DateTimeOffset.UtcNow,
                Draft.ToEntry(CustomFields));

        Document = MobileLogbookDocument.AppendOperation(Document, CustomFields, operation);
        await SaveStateAsync();
        SetLastActionMessage(EditingEntryId is null ? "Flight added." : "Correction saved.");
        ResetDraft();
    }

    public async Task SaveWorkbookEntryAsync()
    {
        if (IsStorageBlocked)
        {
            return;
        }

        HasAttemptedSubmit = true;
        ClearLastActionMessage();

        if (WorkbookDraftErrors.Count > 0)
        {
            return;
        }

        PortableLogbookOperationV2 operation = EditingEntryId is null || EditingRevisionId is null
            ? PortableLogbookOperationV2.Create(
                DocumentV2.LogbookId,
                portableIdFactory.NewEntryIdExcluding(DocumentV2.Operations.Select(operation => operation.EntryId).ToHashSet()),
                RevisionId.New(),
                deviceId,
                DateTimeOffset.UtcNow,
                WorkbookDraft.ToEntry(WorkbookCustomFields))
            : PortableLogbookOperationV2.Correct(
                DocumentV2.LogbookId,
                EditingEntryId.Value,
                RevisionId.New(),
                [EditingRevisionId.Value],
                deviceId,
                DateTimeOffset.UtcNow,
                WorkbookDraft.ToEntry(WorkbookCustomFields));

        DocumentV2 = MobileLogbookDocument.AppendOperation(DocumentV2, CustomFields, operation);
        await SaveStateV2Async();
        await TrySyncHostedAsync(BackgroundSyncReason.LocalEdit);
        SetLastActionMessage(EditingEntryId is null ? "Flight added." : "Correction saved.");
        ResetWorkbookDraft();
    }

    public void ResetDraft()
    {
        if (IsStorageBlocked)
        {
            return;
        }

        Draft = EntryDraft.Create(FindDraftDefaults());
        HasAttemptedSubmit = false;
        HasEditedDraft = false;
        EditingEntryId = null;
        EditingRevisionId = null;
    }

    public void ResetWorkbookDraft()
    {
        if (IsStorageBlocked)
        {
            return;
        }

        WorkbookDraft = MobileWorkbookEntryDraft.Create(WorkbookCustomFields);
        HasAttemptedSubmit = false;
        HasEditedDraft = false;
        EditingEntryId = null;
        EditingRevisionId = null;
    }

    public void CloneEntry(PortableLogbookEntry entry)
    {
        if (IsStorageBlocked)
        {
            return;
        }

        Draft = EntryDraft.FromEntry(entry, preserveDate: false);
        Draft.ClearUnsupportedWorkbookFields();
        EditingEntryId = null;
        EditingRevisionId = null;
        HasAttemptedSubmit = false;
        HasEditedDraft = false;
        SetLastActionMessage("Draft started from recent flight.", MobileActionMessageScope.Draft);
    }

    public void EditEntry(PortableLogbookMaterializedEntry entry)
    {
        if (IsStorageBlocked || entry.Entry is null)
        {
            return;
        }

        Draft = EntryDraft.FromEntry(entry.Entry, preserveDate: true);
        EditingEntryId = entry.EntryId;
        EditingRevisionId = entry.CurrentRevisionId;
        HasAttemptedSubmit = false;
        HasEditedDraft = false;
        SetLastActionMessage("Correction draft opened.", MobileActionMessageScope.Draft);
    }

    public void CloneWorkbookEntry(PortableLogbookWorkbookEntry entry)
    {
        if (IsStorageBlocked)
        {
            return;
        }

        WorkbookDraft = MobileWorkbookEntryDraft.FromEntry(entry, WorkbookCustomFields, preserveDate: false);
        EditingEntryId = null;
        EditingRevisionId = null;
        HasAttemptedSubmit = false;
        HasEditedDraft = false;
        SetLastActionMessage("Draft started from recent flight.", MobileActionMessageScope.Draft);
    }

    public void EditWorkbookEntry(PortableLogbookMaterializedEntryV2 entry)
    {
        if (IsStorageBlocked || entry.Entry is null)
        {
            return;
        }

        WorkbookDraft = MobileWorkbookEntryDraft.FromEntry(entry.Entry, WorkbookCustomFields, preserveDate: true);
        EditingEntryId = entry.EntryId;
        EditingRevisionId = entry.CurrentRevisionId;
        HasAttemptedSubmit = false;
        HasEditedDraft = false;
        SetLastActionMessage("Correction draft opened.", MobileActionMessageScope.Draft);
    }

    public async Task DeleteEntryAsync(PortableLogbookMaterializedEntry entry)
    {
        if (IsStorageBlocked)
        {
            return;
        }

        var operation = new DeleteEntryOperation(
            Document.LogbookId,
            entry.EntryId,
            RevisionId.New(),
            new HashSet<RevisionId> { entry.CurrentRevisionId },
            deviceId,
            DateTimeOffset.UtcNow);

        Document = MobileLogbookDocument.AppendOperation(Document, CustomFields, operation);
        await SaveStateAsync();
        if (EditingEntryId == entry.EntryId)
        {
            ResetDraft();
        }

        SetLastActionMessage("Entry deleted.");
    }

    public async Task DeleteWorkbookEntryAsync(PortableLogbookMaterializedEntryV2 entry)
    {
        if (IsStorageBlocked)
        {
            return;
        }

        var operation = PortableLogbookOperationV2.Delete(
            DocumentV2.LogbookId,
            entry.EntryId,
            RevisionId.New(),
            [entry.CurrentRevisionId],
            deviceId,
            DateTimeOffset.UtcNow);

        DocumentV2 = MobileLogbookDocument.AppendOperation(DocumentV2, CustomFields, operation);
        await SaveStateV2Async();
        await TrySyncHostedAsync(BackgroundSyncReason.LocalEdit);
        if (EditingEntryId == entry.EntryId)
        {
            ResetWorkbookDraft();
        }

        SetLastActionMessage("Entry deleted.");
    }

    public async Task ResolveConflictAsync(PortableLogbookConflict conflict, RevisionId selectedRevisionId)
    {
        if (IsStorageBlocked)
        {
            return;
        }

        var selectedOperation = FindOperation(selectedRevisionId);
        var selectedEntry = EntryPayload(selectedOperation);
        PortableLogbookOperation resolution = selectedEntry is null
            ? new DeleteEntryOperation(
                Document.LogbookId,
                conflict.EntryId,
                RevisionId.New(),
                conflict.HeadRevisionIds.ToHashSet(),
                deviceId,
                DateTimeOffset.UtcNow)
            : PortableLogbookConflictResolution.CreateResolution(
                conflict,
                Document.LogbookId,
                deviceId,
                RevisionId.New(),
                DateTimeOffset.UtcNow,
                selectedEntry,
                $"Kept revision {selectedRevisionId.Value} in the mobile app.");

        Document = MobileLogbookDocument.AppendOperation(Document, CustomFields, resolution);
        await SaveStateAsync();
        SetLastActionMessage("Conflict resolved.");
    }

    public async Task ResolveWorkbookConflictAsync(PortableLogbookConflict conflict, RevisionId selectedRevisionId)
    {
        if (IsStorageBlocked)
        {
            return;
        }

        var selectedOperation = FindOperationV2(selectedRevisionId);
        PortableLogbookOperationV2 resolution = selectedOperation.Entry is null
            ? PortableLogbookOperationV2.Delete(
                DocumentV2.LogbookId,
                conflict.EntryId,
                RevisionId.New(),
                conflict.HeadRevisionIds,
                deviceId,
                DateTimeOffset.UtcNow)
            : PortableLogbookOperationV2.ResolveConflict(
                DocumentV2.LogbookId,
                conflict.EntryId,
                RevisionId.New(),
                conflict.HeadRevisionIds,
                deviceId,
                DateTimeOffset.UtcNow,
                selectedOperation.Entry,
                $"Kept revision {selectedRevisionId.Value} in the mobile app.");

        DocumentV2 = MobileLogbookDocument.AppendOperation(DocumentV2, CustomFields, resolution);
        await SaveStateV2Async();
        await TrySyncHostedAsync(BackgroundSyncReason.LocalEdit);
        SetLastActionMessage("Conflict resolved.");
    }

    public async Task<bool> RestoreWorkbookPackageKeyAsync(LogbookId logbookId, string recoveryCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryCode);

        await packageKeyStore.ImportRecoveryCodeAsync(logbookId, recoveryCode);
        await RefreshPackageKeyStatusAsync(logbookId);
        return PackageKeyStatus == "Ready";
    }

    public async Task AdoptWorkbookLogbookAsync(LogbookId logbookId)
    {
        if (DocumentV2.LogbookId == logbookId)
        {
            return;
        }

        if (DocumentV2.Operations.Count > 0 || ImportReceipts.Count > 0)
        {
            throw new InvalidOperationException(
                "Cannot switch workbook logbooks after this device copy has local entries or package receipts.");
        }

        DocumentV2 = PortableLogbookDocumentV2.CreateAustraliaFirst(
            logbookId,
            WorkbookCustomFields,
            PortableLogbookCurrencyOverrideDates.Empty,
            []);
        LastSuccessfulExportAt = null;
        LastSuccessfulExport = null;
        await SaveStateV2Async();
    }

    public async Task<HostedSignInStart> StartHostedInviteAcceptanceAsync(string email)
    {
        if (hostedAuthenticator is null)
        {
            throw new InvalidOperationException("Hosted sync is not configured on this device.");
        }

        ClearLastActionMessage();
        PendingHostedSignIn = await hostedAuthenticator.StartEmailSignInAsync(email, shouldCreateUser: false);
        SetLastActionMessage("Sign-in email sent.");
        return PendingHostedSignIn;
    }

    public async Task CompleteHostedInviteAcceptanceAsync(string verificationCode)
    {
        EnsureHostedInviteAcceptanceAvailable();
        await CompleteEmailSignInOrRecoverAsync(
            () => hostedAuthenticator!.CompleteEmailSignInAsync(verificationCode));
    }

    public async Task ResumeHostedInviteAcceptanceAsync()
    {
        EnsureHostedInviteAcceptanceAvailable();
        await CompleteEmailSignInOrRecoverAsync(
            () => hostedAuthenticator!.ResumeEmailSignInAsync());
    }

    private async Task CompleteEmailSignInOrRecoverAsync(
        Func<ValueTask<HostedSyncSession>> completeSignIn)
    {
        try
        {
            await CompleteHostedInviteSetupAsync(await completeSignIn());
        }
        catch (HostedSignInException exception)
            when (exception.Reason == HostedSignInFailureReason.AccountRecoveryRequired)
        {
            if (replacementRecovery is null)
            {
                throw new InvalidOperationException(
                    "Automatic account recovery is not configured on this device.",
                    exception);
            }

            await ApplyReplacementRecoveryAsync(
                await replacementRecovery.RecoverOnlyLogbookAsync());
        }
    }

    public async Task SignInWithGoogleAsync()
    {
        EnsureGoogleSignInAvailable();
        EnsureHostedInviteAcceptanceAvailable();
        try
        {
            var session = await googleAuthenticator!.SignInWithGoogleAsync();
            await CompleteHostedInviteSetupAsync(session);
        }
        catch (HostedSignInException exception)
            when (exception.Reason == HostedSignInFailureReason.AccountRecoveryRequired)
        {
            if (replacementRecovery is null)
            {
                throw new InvalidOperationException(
                    "Automatic account recovery is not configured on this device.",
                    exception);
            }

            await ApplyReplacementRecoveryAsync(
                await replacementRecovery.RecoverOnlyLogbookAsync());
        }
    }

    public async Task RecoverReplacementDeviceAsync(
        LogbookId logbookId,
        CancellationToken cancellationToken = default)
    {
        if (replacementRecovery is null)
        {
            throw new InvalidOperationException("Replacement-device recovery is not configured on this device.");
        }

        ClearLastActionMessage();
        await ApplyReplacementRecoveryAsync(
            await replacementRecovery.RecoverAsync(logbookId, cancellationToken));
    }

    public async Task RecoverReplacementDeviceWithCodeAsync(
        string recoveryCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryCode);
        if (replacementRecovery is null)
        {
            throw new InvalidOperationException("Replacement-device recovery is not configured on this device.");
        }

        ClearLastActionMessage();
        await ApplyReplacementRecoveryAsync(
            await replacementRecovery.RecoverOnlyLogbookWithCodeAsync(recoveryCode, cancellationToken));
    }

    public async Task<bool> ConfirmRecoveryCodeAsync(string enteredCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(enteredCode);
        if (pendingRecoveryCodeSetup is null || HostedSync is null)
        {
            throw new InvalidOperationException("No new recovery code is awaiting confirmation.");
        }
        if (!await packageKeyStore.TestRecoveryCodeEnvelopeAsync(
            DocumentV2.LogbookId,
            enteredCode,
            pendingRecoveryCodeSetup.Envelope))
        {
            return false;
        }
        if (recoveryEnvelopeService is null)
        {
            throw new InvalidOperationException("Account recovery is not configured on this device.");
        }

        var result = await recoveryEnvelopeService.EnrollRecoveryCodeAsync(
            new MobileRecoveryCodeEnrollmentRequest(
                DocumentV2.LogbookId,
                HostedSync.DeviceId,
                pendingRecoveryCodeSetup.Envelope));
        if (!result.Enrolled)
        {
            throw new MobileHostedDiagnosticException(
                "RECOVERY_CODE_ENROLLMENT_INVALID",
                "Recovery-code setup returned an invalid result.");
        }
        pendingRecoveryCodeSetup = null;
        verifiedRecoveryCodeConfigurations.Add((DocumentV2.LogbookId, HostedSync.DeviceId));
        SetLastActionMessage("Recovery code confirmed. Account connected and synced.");
        return true;
    }

    private async Task ApplyReplacementRecoveryAsync(MobileReplacementRecoveryResult restored)
    {
        DocumentV2 = restored.Document;
        HostedSync = restored.HostedSync;
        deviceId = restored.HostedSync.DeviceId;
        ImportReceipts = [];
        LastSuccessfulExportAt = null;
        LastSuccessfulExport = null;
        PendingHostedSignIn = null;
        await RefreshPackageKeyStatusAsync(restored.Document.LogbookId);
        SetLastActionMessage("Existing logbook restored and synced.");
    }

    public async Task LinkGoogleIdentityAsync()
    {
        EnsureGoogleSignInAvailable();
        if (!HasHostedSync)
        {
            throw new InvalidOperationException("Connect the invited account before adding Google sign-in.");
        }

        ClearLastActionMessage();
        await googleAuthenticator!.LinkGoogleIdentityAsync();
        SetLastActionMessage("Google sign-in added to this account.");
    }

    private void EnsureGoogleSignInAvailable()
    {
        if (googleAuthenticator is null)
        {
            throw new InvalidOperationException("Google sign-in is not configured on this device.");
        }
    }

    public async Task<MobileConnectionDiagnosticReport> RunConnectionPreflightAsync(
        CancellationToken cancellationToken = default)
    {
        if (connectionRecovery is null)
        {
            throw new InvalidOperationException("Connection diagnostics are not configured on this device.");
        }

        ClearLastActionMessage();
        LastConnectionDiagnostics = await connectionRecovery.RunPreflightAsync(cancellationToken);
        await RecordConnectionDiagnosticsAsync(LastConnectionDiagnostics);
        SetLastActionMessage(LastConnectionDiagnostics.Passed
            ? "Connection preflight passed. Recovery is ready."
            : $"Connection preflight stopped at {LastConnectionDiagnostics.CurrentStage}: {LastConnectionDiagnostics.ErrorCode}.");
        return LastConnectionDiagnostics;
    }

    public async Task<MobileConnectionDiagnosticReport> RecoverHostedConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        if (connectionRecovery is null)
        {
            throw new InvalidOperationException("Connection recovery is not configured on this device.");
        }

        EnsureHostedInviteAcceptanceAvailable();
        var result = await connectionRecovery.RecoverAsync(LastConnectionDiagnostics, cancellationToken);
        LastConnectionDiagnostics = result.Diagnostics;
        await RecordConnectionDiagnosticsAsync(result.Diagnostics);
        if (!result.Diagnostics.Passed || result.Document is null || result.HostedSync is null)
        {
            SetLastActionMessage($"Connection recovery stopped at {result.Diagnostics.CurrentStage}: {result.Diagnostics.ErrorCode}.");
            return result.Diagnostics;
        }

        DocumentV2 = result.Document;
        deviceId = result.HostedSync.DeviceId;
        ImportReceipts = [];
        LastSuccessfulExportAt = null;
        LastSuccessfulExport = null;
        HostedSync = result.HostedSync;
        PendingHostedSignIn = null;
        await RefreshPackageKeyStatusAsync(DocumentV2.LogbookId);
        SetLastActionMessage("Account connected and local state verified.");
        return result.Diagnostics;
    }

    private async Task RecordConnectionDiagnosticsAsync(MobileConnectionDiagnosticReport report)
    {
        await logbookStore.AppendConnectionDiagnosticsAsync(report);
        ConnectionDiagnosticHistory = await logbookStore.LoadConnectionDiagnosticsAsync();
    }

    private void EnsureHostedInviteAcceptanceAvailable()
    {
        if (hostedAuthenticator is null)
        {
            throw new InvalidOperationException("Hosted sync is not configured on this device.");
        }

        if (DocumentV2.Operations.Count > 0 || ImportReceipts.Count > 0)
        {
            throw new InvalidOperationException(
                "App-only setup must start before importing a workbook package or recording flights.");
        }

        ClearLastActionMessage();
    }

    private async Task CompleteHostedInviteSetupAsync(HostedSyncSession session)
    {
        var plan = MobileAppOnlyLogbookPlan.Create(session.DeviceId);
        await packageKeyStore.ImportRecoveryCodeAsync(plan.LogbookId, plan.Key.ToRecoveryCode());

        DocumentV2 = plan.InitialDocument;
        deviceId = session.DeviceId;
        ImportReceipts = [];
        LastSuccessfulExportAt = null;
        LastSuccessfulExport = null;
        HostedSync = new BrowserHostedSyncState(
            session.AccountId,
            plan.LogbookId,
            session.DeviceId,
            LastAcknowledgedHostedRevision: 0,
            PortableHostedSyncStatus.Synced,
            LastAttemptedAt: syncClock.UtcNow);
        PendingHostedSignIn = null;
        await SaveStateV2Async();
        await RefreshPackageKeyStatusAsync(DocumentV2.LogbookId);
        if (recoveryEnvelopeService is not null
            && hostedLedger is not null
            && networkStatus is not null)
        {
            var sync = await SyncHostedAsync(BackgroundSyncReason.ManualRefresh);
            if (sync?.Status != PortableHostedSyncStatus.Synced)
            {
                throw new MobileHostedDiagnosticException(
                    "RECOVERY_INITIALIZATION_INCOMPLETE",
                    "Account recovery could not be initialized. Retry Sync now before recording flights.");
            }
            SetLastActionMessage(IsRecoveryCodeConfirmationPending
                ? "Save the recovery code and enter it again to confirm recovery setup."
                : "Account connected and synced.");
        }
        else
        {
            SetLastActionMessage("Account connected.");
        }
    }

    public Task<PortableHostedSyncResult?> SyncHostedNowAsync() =>
        SyncHostedAsync(BackgroundSyncReason.ManualRefresh);

    public Task<PortableHostedSyncResult?> SyncHostedAfterNetworkRestoredAsync() =>
        SyncHostedAsync(BackgroundSyncReason.NetworkRestored);

    public async Task<MobilePackageImportApplyWorkflowResultV2> ApplyWorkbookPackageAsync(
        BrowserFile file,
        DateTimeOffset importedAt)
    {
        ArgumentNullException.ThrowIfNull(file);
        var result = await MobilePackageImportApplyWorkflow.ApplyIfReadyAsync(
            DocumentV2,
            file,
            packageKeyStore,
            ImportReceipts,
            importedAt);
        if (result.Status == MobilePackageImportApplyStatus.RequiresResolution)
        {
            return result;
        }

        DocumentV2 = result.Document;
        ImportReceipts = result.ImportReceipts;
        await SaveStateV2Async();
        return result;
    }

    public async Task<MobilePackageImportApplyWorkflowResultV2> ApplyWorkbookPackageWithCustomFieldResolutionsAsync(
        BrowserFile file,
        IEnumerable<PortableLogbookCustomFieldDefinitionResolution> resolutions,
        DateTimeOffset importedAt)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(resolutions);
        var result = await MobilePackageImportApplyWorkflow.ApplyWithCustomFieldResolutionsAsync(
            DocumentV2,
            file,
            packageKeyStore,
            ImportReceipts,
            resolutions,
            importedAt);
        DocumentV2 = result.Document;
        ImportReceipts = result.ImportReceipts;
        await SaveStateV2Async();
        return result;
    }

    public async Task<MobilePackageExportWorkflowResult> ExportWorkbookPackageAsync(
        BrowserFileStore fileStore,
        DateTimeOffset exportedAt)
    {
        ArgumentNullException.ThrowIfNull(fileStore);
        var result = await MobilePackageExportWorkflow.ExportAsync(DocumentV2, packageKeyStore, fileStore, exportedAt);
        LastSuccessfulExportAt = result.ExportedAt;
        LastSuccessfulExport = BrowserLogbookExportCheckpoint.Create(DocumentV2, result);
        await SaveStateV2Async();
        return result;
    }

    private void SetLastActionMessage(
        string message,
        MobileActionMessageScope scope = MobileActionMessageScope.Global)
    {
        LastActionMessage = message;
        LastActionMessageScope = scope;
    }

    private void ClearLastActionMessage()
    {
        LastActionMessage = null;
        LastActionMessageScope = MobileActionMessageScope.Global;
    }

    private void InvalidateWorkbookProjectionCache()
    {
        mergeResultV2Cache = null;
        currentEntriesV2Cache = null;
        deletedEntriesV2Cache = null;
        currencyRecencySummaryCache = null;
        currencyRecencySummaryDate = null;
    }

    public PortableLogbookMaterializedEntry? FindCurrentEntry(string? entryId)
    {
        if (string.IsNullOrWhiteSpace(entryId))
        {
            return null;
        }

        return MergeResult.Entries.TryGetValue(new EntryId(entryId), out var entry)
            ? entry
            : null;
    }

    public PortableLogbookMaterializedEntryV2? FindCurrentEntryV2(string? entryId)
    {
        if (string.IsNullOrWhiteSpace(entryId))
        {
            return null;
        }

        return MergeResultV2.Entries.TryGetValue(new EntryId(entryId), out var entry)
            ? entry
            : null;
    }

    public PortableLogbookRevisionHistoryView? FindHistory(string? entryId)
    {
        if (string.IsNullOrWhiteSpace(entryId))
        {
            return null;
        }

        try
        {
            return PortableLogbookRevisionHistory.ForEntry(Document, new EntryId(entryId));
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    public void MarkDraftEdited()
    {
        HasEditedDraft = true;
    }

    public void UseFlightTimeAsDay()
    {
        if (IsStorageBlocked)
        {
            return;
        }

        Draft.Day = Draft.FlightTime;
        Draft.Night = 0;
        MarkDraftEdited();
    }

    public void UseDirectRoute()
    {
        if (IsStorageBlocked)
        {
            return;
        }

        var from = Draft.From.Trim();
        var to = Draft.To.Trim();
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            return;
        }

        Draft.Route = string.Equals(from, to, StringComparison.OrdinalIgnoreCase)
            ? from
            : $"{from} {to}";
        MarkDraftEdited();
    }

    public void UseRecentRemark(string? value)
    {
        if (IsStorageBlocked || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        Draft.Remarks = value;
        MarkDraftEdited();
    }

    public IEnumerable<string> RecentValues(Func<PortableLogbookEntry, string?> selector) =>
        MobileRecentValues.Create(CurrentEntries, selector);

    public IEnumerable<string> RecentWorkbookValues(Func<PortableLogbookWorkbookEntry, string?> selector) =>
        MobileRecentValues.Create(CurrentEntriesV2, selector);

    public IEnumerable<string> RecentAirportValues() =>
        MobileAirportSuggestions.Create(CurrentEntries);

    public IEnumerable<string> RecentWorkbookAirportValues() =>
        MobileAirportSuggestions.Create(CurrentEntriesV2);

    public static string FormatHours(decimal hours) => hours.ToString("0.0");

    public static string FormatDate(DateOnly? date) => date?.ToString("dd MMM yyyy") ?? "No date";

    public static string FormatTimestamp(DateTimeOffset? timestamp) =>
        timestamp?.ToLocalTime().ToString("dd MMM yyyy HH:mm") ?? "Never";

    public string FormatRoute(PortableLogbookEntry entry)
    {
        var route = $"{entry.From}-{entry.To}".Trim(' ', '-');
        return string.IsNullOrWhiteSpace(route) ? "Route pending" : route;
    }

    public string FormatRoute(PortableLogbookWorkbookEntry entry)
    {
        var route = $"{entry.From}-{entry.To}".Trim(' ', '-');
        return string.IsNullOrWhiteSpace(route) ? "Route pending" : route;
    }

    public string FormatAircraft(PortableLogbookEntry entry)
    {
        var aircraft = $"{entry.AircraftType} {entry.Registration}".Trim();
        return string.IsNullOrWhiteSpace(aircraft) ? "Aircraft pending" : aircraft;
    }

    public string FormatAircraft(PortableLogbookWorkbookEntry entry)
    {
        var aircraft = $"{entry.Type} {entry.Reg}".Trim();
        return string.IsNullOrWhiteSpace(aircraft) ? "Aircraft pending" : aircraft;
    }

    public string FormatRegistration(PortableLogbookEntry entry) =>
        string.IsNullOrWhiteSpace(entry.Registration) ? "REG" : entry.Registration.Trim().ToUpperInvariant();

    public string FormatRegistration(PortableLogbookWorkbookEntry entry) =>
        string.IsNullOrWhiteSpace(entry.Reg) ? "REG" : entry.Reg.Trim().ToUpperInvariant();

    public PortableLogbookOperation FindOperation(RevisionId revisionId) =>
        Document.Operations.First(operation => operation.RevisionId == revisionId);

    public PortableLogbookOperationV2 FindOperationV2(RevisionId revisionId) =>
        DocumentV2.Operations.First(operation => operation.RevisionId == revisionId);

    public IEnumerable<EntryDetail> EntryDetails(PortableLogbookEntry entry)
    {
        yield return new("Date", FormatDate(entry.Date));
        yield return new("Aircraft type", FormatText(entry.AircraftType));
        yield return new("Registration", FormatText(entry.Registration));
        yield return new("Flight number", FormatText(entry.FlightNumber));
        yield return new("From", FormatText(entry.From));
        yield return new("To", FormatText(entry.To));
        yield return new("Route", FormatText(entry.Route));
        yield return new("Remarks", FormatText(entry.Details));
        yield return new("Multi-pilot", FormatNullableHours(entry.MultiPilot));
        yield return new("PIC", FormatNullableHours(entry.PilotInCommand));
        yield return new("Co-pilot", FormatNullableHours(entry.CoPilot));
        yield return new("Dual", FormatNullableHours(entry.Dual));
        yield return new("Instructor", FormatNullableHours(entry.Instructor));
        yield return new("Day", FormatNullableHours(entry.Day));
        yield return new("Night", FormatNullableHours(entry.Night));
        yield return new("Instrument actual", FormatNullableHours(entry.InstrumentActual));
        yield return new("Instrument sim", FormatNullableHours(entry.InstrumentSimulated));
        yield return new("Landings day", FormatNullableCount(entry.LandingsDay));
        yield return new("Landings night", FormatNullableCount(entry.LandingsNight));
        yield return new("IFR approaches", FormatNullableCount(entry.IfrApproaches));
        yield return new("Holding", FormatNullableCount(entry.Holding));
        yield return new("RNP", FormatNullableCount(entry.Rnav));
        yield return new("Circling", FormatNullableCount(entry.Circling));

        foreach (var field in EntryCustomFields())
        {
            entry.CustomFields.TryGetValue(field.Id, out var customValue);
            yield return new(field.Label, FormatText(customValue));
        }
    }

    public string FormatConflictRoute(PortableLogbookOperation operation)
    {
        var entry = EntryPayload(operation);
        return entry is null ? "Deleted entry" : FormatRoute(entry);
    }

    public string FormatConflictAircraft(PortableLogbookOperation operation)
    {
        var entry = EntryPayload(operation);
        return entry is null ? "No active payload" : FormatAircraft(entry);
    }

    private async Task RefreshPackageKeyStatusAsync()
    {
        try
        {
            if (!await packageKeyStore.IsSupportedAsync())
            {
                PackageKeyStatus = "Unavailable";
                return;
            }

            PackageKeyStatus = await packageKeyStore.HasPackageKeyAsync(Document.LogbookId)
                ? "Ready"
                : "Not set";
        }
        catch (JSException)
        {
            PackageKeyStatus = "Unavailable";
        }
    }

    public IEnumerable<EntryDetail> EntryDetails(PortableLogbookWorkbookEntry entry)
    {
        yield return new("Date", FormatDate(entry.Date), EntryDetailGroup.Flight);
        yield return new("Type", FormatText(entry.Type), EntryDetailGroup.Flight);
        yield return new("Reg", FormatText(entry.Reg), EntryDetailGroup.Flight);
        yield return new("Flight ID", FormatText(entry.FlightId), EntryDetailGroup.Flight);
        yield return new("PIC", FormatText(entry.Pic), EntryDetailGroup.CrewAndRoute);
        yield return new("Other pilot or crew", FormatText(entry.OtherPilotOrCrew), EntryDetailGroup.CrewAndRoute);
        yield return new("From", FormatText(entry.From), EntryDetailGroup.CrewAndRoute);
        yield return new("To", FormatText(entry.To), EntryDetailGroup.CrewAndRoute);
        yield return new("Via", FormatText(entry.Via), EntryDetailGroup.CrewAndRoute);
        yield return new("Remarks", FormatText(entry.Remarks), EntryDetailGroup.CrewAndRoute);
        yield return new("FR", FormatFlag(entry.FlightReview), EntryDetailGroup.Checks);
        yield return new("IPC", FormatFlag(entry.InstrumentProficiencyCheck), EntryDetailGroup.Checks);
        yield return new("OPC", FormatFlag(entry.OperatorProficiencyCheck), EntryDetailGroup.Checks);
        yield return new("SE ICUS day", FormatNullableHours(entry.SeIcusDay), EntryDetailGroup.LoggedTime);
        yield return new("SE ICUS night", FormatNullableHours(entry.SeIcusNight), EntryDetailGroup.LoggedTime);
        yield return new("SE dual day", FormatNullableHours(entry.SeDualDay), EntryDetailGroup.LoggedTime);
        yield return new("SE dual night", FormatNullableHours(entry.SeDualNight), EntryDetailGroup.LoggedTime);
        yield return new("SE command day", FormatNullableHours(entry.SeCommandDay), EntryDetailGroup.LoggedTime);
        yield return new("SE command night", FormatNullableHours(entry.SeCommandNight), EntryDetailGroup.LoggedTime);
        yield return new("ME ICUS day", FormatNullableHours(entry.MeIcusDay), EntryDetailGroup.LoggedTime);
        yield return new("ME ICUS night", FormatNullableHours(entry.MeIcusNight), EntryDetailGroup.LoggedTime);
        yield return new("ME dual day", FormatNullableHours(entry.MeDualDay), EntryDetailGroup.LoggedTime);
        yield return new("ME dual night", FormatNullableHours(entry.MeDualNight), EntryDetailGroup.LoggedTime);
        yield return new("ME command day", FormatNullableHours(entry.MeCommandDay), EntryDetailGroup.LoggedTime);
        yield return new("ME command night", FormatNullableHours(entry.MeCommandNight), EntryDetailGroup.LoggedTime);
        yield return new("Copilot day", FormatNullableHours(entry.CopilotDay), EntryDetailGroup.LoggedTime);
        yield return new("Copilot night", FormatNullableHours(entry.CopilotNight), EntryDetailGroup.LoggedTime);
        yield return new("IFR IF", FormatNullableHours(entry.IfrIf), EntryDetailGroup.LoggedTime);
        yield return new("IFR sim", FormatNullableHours(entry.IfrSim), EntryDetailGroup.LoggedTime);
        yield return new("Landings day", FormatNullableCount(entry.LandingsDay), EntryDetailGroup.LandingsAndApproaches);
        yield return new("Landings night", FormatNullableCount(entry.LandingsNight), EntryDetailGroup.LandingsAndApproaches);
        yield return new("ILS", FormatNullableCount(entry.Ils), EntryDetailGroup.LandingsAndApproaches);
        yield return new("VOR", FormatNullableCount(entry.Vor), EntryDetailGroup.LandingsAndApproaches);
        yield return new("RNP", FormatNullableCount(entry.Rnp), EntryDetailGroup.LandingsAndApproaches);
        yield return new("NDB", FormatNullableCount(entry.Ndb), EntryDetailGroup.LandingsAndApproaches);
        yield return new("DGA (CDI)", FormatNullableCount(entry.DgaCdi), EntryDetailGroup.LandingsAndApproaches);
        yield return new("DGA (Azi)", FormatNullableCount(entry.DgaAzi), EntryDetailGroup.LandingsAndApproaches);
        yield return new("Circling", FormatNullableCount(entry.Circling), EntryDetailGroup.LandingsAndApproaches);

        foreach (var field in WorkbookCustomFields)
        {
            entry.CustomFields.TryGetValue(field.Id, out var customValue);
            yield return new(field.Label, FormatText(customValue), EntryDetailGroup.CustomFields);
        }
    }

    private async Task RefreshPackageKeyStatusAsync(LogbookId logbookId)
    {
        try
        {
            if (!await packageKeyStore.IsSupportedAsync())
            {
                PackageKeyStatus = "Unavailable";
                return;
            }

            PackageKeyStatus = await packageKeyStore.HasPackageKeyAsync(logbookId)
                ? "Ready"
                : "Not set";
        }
        catch (JSException)
        {
            PackageKeyStatus = "Unavailable";
        }
    }

    private ValueTask SaveStateAsync() =>
        logbookStore.SaveStateAsync(new BrowserLogbookState(Document, ImportReceipts, LastSuccessfulExportAt, LastSuccessfulExport, HostedSync));

    private ValueTask SaveStateV2Async() =>
        logbookStore.SaveStateAsync(new BrowserLogbookStateV2(DocumentV2, ImportReceipts, LastSuccessfulExportAt, LastSuccessfulExport, HostedSync));

    private async Task TrySyncHostedAsync(BackgroundSyncReason reason)
    {
        if (HostedSync is null || hostedLedger is null || networkStatus is null || hostedAuthenticator is null)
        {
            return;
        }

        _ = await SyncHostedAsync(reason);
    }

    private async Task<PortableHostedSyncResult?> SyncHostedAsync(BackgroundSyncReason reason)
    {
        if (HostedSync is null)
        {
            return null;
        }

        if (hostedLedger is null || networkStatus is null || hostedAuthenticator is null)
        {
            var unavailable = PortableHostedSyncResult.NeedsAttention(
                DocumentV2,
                HostedSync.LastAcknowledgedHostedRevision,
                DocumentV2.Operations.Count,
                "Hosted sync is not configured on this device.");
            HostedSync = HostedSync.WithResult(unavailable, syncClock.UtcNow);
            await SaveStateV2Async();
            HostedSyncChanged?.Invoke();
            return unavailable;
        }

        var result = await new MobileHostedSyncWorkflow(
                packageKeyStore,
                hostedLedger,
                hostedAuthenticator,
                networkStatus,
                syncClock)
            .SyncAsync(new PortableHostedSyncRequestContext(DocumentV2, HostedSync, reason));
        var recoveryEnrollment = (result.Document.LogbookId, HostedSync.DeviceId);
        if (result.Status == PortableHostedSyncStatus.Synced
            && recoveryEnvelopeService is not null
            && !verifiedRecoveryEnrollments.Contains(recoveryEnrollment))
        {
            try
            {
                _ = await packageKeyStore.EnrollRecoveryEnvelopeAsync(
                    result.Document.LogbookId,
                    HostedSync.DeviceId,
                    recoveryEnvelopeService);
                verifiedRecoveryEnrollments.Add(recoveryEnrollment);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result = result with
                {
                    Status = PortableHostedSyncStatus.NeedsAttention,
                    AttentionRequiredReason = RecoveryEnrollmentAttention(ex)
                };
            }
        }
        if (result.Status == PortableHostedSyncStatus.Synced
            && recoveryEnvelopeService is not null
            && !verifiedRecoveryCodeConfigurations.Contains(recoveryEnrollment)
            && pendingRecoveryCodeSetup is null)
        {
            try
            {
                var status = await recoveryEnvelopeService.GetRecoverySetupStatusAsync(
                    new MobileRecoverySetupStatusRequest(
                        result.Document.LogbookId,
                        HostedSync.DeviceId));
                if (!status.ManagedEnvelopeConfigured)
                {
                    throw new MobileHostedDiagnosticException(
                        "RECOVERY_ENROLLMENT_MISSING",
                        "Managed account recovery was not retained by the service.");
                }
                if (status.RecoveryCodeConfigured)
                {
                    verifiedRecoveryCodeConfigurations.Add(recoveryEnrollment);
                }
                else
                {
                    var recoveryCode = MobileRecoveryCodeEnvelope.GenerateRecoveryCode();
                    pendingRecoveryCodeSetup = new MobileRecoveryCodeSetup(
                        recoveryCode,
                        await packageKeyStore.WrapPackageKeyForRecoveryCodeAsync(
                            result.Document.LogbookId,
                            recoveryCode));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result = result with
                {
                    Status = PortableHostedSyncStatus.NeedsAttention,
                    AttentionRequiredReason = RecoveryEnrollmentAttention(ex)
                };
            }
        }

        DocumentV2 = result.Document;
        HostedSync = HostedSync.WithResult(result, syncClock.UtcNow);
        await SaveStateV2Async();
        HostedSyncChanged?.Invoke();
        return result;
    }

    private static string RecoveryEnrollmentAttention(Exception exception)
    {
        var errorCode = exception is MobileHostedDiagnosticException diagnostic
            ? MobileDiagnosticRedactor.Redact(diagnostic.ErrorCode)
            : exception is JSException
                ? "RECOVERY_DEVICE_BRIDGE_UNAVAILABLE"
                : "RECOVERY_ENROLLMENT_FAILED";
        if (string.IsNullOrWhiteSpace(errorCode))
        {
            errorCode = "RECOVERY_ENROLLMENT_FAILED";
        }

        return $"Account recovery setup needs attention ({errorCode}). Retry Sync now.";
    }

    private string[] ValidateDraft()
    {
        return PortableLogbookEntryRules
            .Validate(Draft.ToEntry(CustomFields), DateOnly.FromDateTime(DateTime.Today))
            .Errors
            .Select(error => error.Message)
            .ToArray();
    }

    private string[] ValidateDraftWarnings()
    {
        return MobileLogbookEntryWarnings
            .Create(Draft.ToEntry(CustomFields), CurrentEntries, EditingEntryId)
            .ToArray();
    }

    private string[] ValidateWorkbookDraft()
    {
        var entry = WorkbookDraft.ToEntry(WorkbookCustomFields);
        var errors = new List<string>();
        if (entry.Date is null || entry.Date > DateOnly.FromDateTime(DateTime.Today))
        {
            errors.Add("Year, Month, and Day must form a non-future date.");
        }

        if (string.IsNullOrWhiteSpace(entry.Type))
        {
            errors.Add("Type is required before this entry can be added.");
        }

        if (string.IsNullOrWhiteSpace(entry.Reg))
        {
            errors.Add("Reg is required before this entry can be added.");
        }

        if (string.IsNullOrWhiteSpace(entry.From))
        {
            errors.Add("From is required before this entry can be added.");
        }

        if (string.IsNullOrWhiteSpace(entry.To))
        {
            errors.Add("To is required before this entry can be added.");
        }

        if (WorkbookFlightTime(entry) + (entry.IfrSim ?? 0) <= 0)
        {
            errors.Add("Workbook flight or simulator time cannot be zero.");
        }

        return errors.ToArray();
    }

    private MobileEntryDraftDefaults FindDraftDefaults() =>
        MobileEntryDraftDefaultPlanner.Create(CurrentEntries);

    private IEnumerable<CustomFieldDefinition> EntryCustomFields() =>
        Document.CustomFieldDefinitions.Count > 0
            ? Document.CustomFieldDefinitions.OrderBy(field => field.Order)
            : CustomFields;

    private static PortableLogbookEntry? EntryPayload(PortableLogbookOperation operation) =>
        operation switch
        {
            CreateEntryOperation create => create.Entry,
            CorrectEntryOperation correction => correction.Entry,
            ResolveConflictOperation resolution => resolution.Entry,
            DeleteEntryOperation => null,
            _ => throw new InvalidOperationException($"Unsupported operation type {operation.GetType().Name}.")
        };

    private static string FormatText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private static string FormatNullableHours(decimal? hours) =>
        hours.GetValueOrDefault() == 0 ? "-" : FormatHours(hours!.Value);

    private static string FormatNullableCount(int? count) =>
        count.GetValueOrDefault() == 0
            ? "-"
            : count!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string FormatFlag(bool? value) =>
        value == true ? "Yes" : "No";

    public static decimal WorkbookFlightTime(PortableLogbookWorkbookEntry entry) =>
        (entry.SeIcusDay ?? 0) +
        (entry.SeIcusNight ?? 0) +
        (entry.SeDualDay ?? 0) +
        (entry.SeDualNight ?? 0) +
        (entry.SeCommandDay ?? 0) +
        (entry.SeCommandNight ?? 0) +
        (entry.MeIcusDay ?? 0) +
        (entry.MeIcusNight ?? 0) +
        (entry.MeDualDay ?? 0) +
        (entry.MeDualNight ?? 0) +
        (entry.MeCommandDay ?? 0) +
        (entry.MeCommandNight ?? 0) +
        (entry.CopilotDay ?? 0) +
        (entry.CopilotNight ?? 0);

    public static decimal WorkbookLoggedTime(PortableLogbookWorkbookEntry entry) =>
        WorkbookFlightTime(entry) + (entry.IfrSim ?? 0);

    public static int WorkbookApproaches(PortableLogbookWorkbookEntry entry) =>
        entry.Ils.GetValueOrDefault() +
        entry.Vor.GetValueOrDefault() +
        entry.Rnp.GetValueOrDefault() +
        entry.Ndb.GetValueOrDefault() +
        entry.DgaCdi.GetValueOrDefault() +
        entry.DgaAzi.GetValueOrDefault() +
        entry.Circling.GetValueOrDefault();
}

public sealed record EntryDetail(
    string Label,
    string Value,
    EntryDetailGroup Group = EntryDetailGroup.Record);

public enum EntryDetailGroup
{
    Record,
    Flight,
    CrewAndRoute,
    Checks,
    LoggedTime,
    CustomFields,
    LandingsAndApproaches
}

public sealed class EntryDraft
{
    public DateOnly Date { get; set; }
    public string AircraftType { get; set; } = string.Empty;
    public string Registration { get; set; } = string.Empty;
    public string FlightNumber { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public decimal MultiPilot { get; set; }
    public decimal PilotInCommand { get; set; }
    public decimal CoPilot { get; set; }
    public decimal Dual { get; set; }
    public decimal Instructor { get; set; }
    public decimal Day { get; set; }
    public decimal Night { get; set; }
    public decimal InstrumentActual { get; set; }
    public decimal InstrumentSimulated { get; set; }
    public int? TakeoffsDay { get; set; }
    public int? TakeoffsNight { get; set; }
    public int? LandingsDay { get; set; }
    public int? LandingsNight { get; set; }
    public int? IfrApproaches { get; set; }
    public int? Holding { get; set; }
    public int? Rnav { get; set; }
    public int? Circling { get; set; }
    public string Remarks { get; set; } = string.Empty;
    public Dictionary<CustomFieldId, string> CustomValues { get; } = [];

    public decimal FlightTime =>
        MultiPilot + PilotInCommand + CoPilot + Dual + Instructor;

    public decimal LoggedTime => FlightTime + InstrumentSimulated;

    public int TotalLandings => LandingsDay.GetValueOrDefault() + LandingsNight.GetValueOrDefault();

    public int TotalApproaches =>
        IfrApproaches.GetValueOrDefault() +
        Holding.GetValueOrDefault() +
        Rnav.GetValueOrDefault() +
        Circling.GetValueOrDefault();

    public static EntryDraft Create(MobileEntryDraftDefaults? defaults = null)
    {
        defaults ??= MobileEntryDraftDefaults.Empty;
        var draft = new EntryDraft
        {
            Date = DateOnly.FromDateTime(DateTime.Today),
            AircraftType = defaults.AircraftType,
            Registration = defaults.Registration,
            From = defaults.From,
            To = defaults.To,
            Route = defaults.Route
        };
        foreach (var field in MobileLogbookSession.CustomFields)
        {
            draft.CustomValues[field.Id] = string.Empty;
        }

        return draft;
    }

    public static EntryDraft FromEntry(PortableLogbookEntry entry, bool preserveDate)
    {
        var draft = Create();
        draft.Date = preserveDate && entry.Date is not null
            ? entry.Date.Value
            : DateOnly.FromDateTime(DateTime.Today);
        draft.AircraftType = entry.AircraftType ?? string.Empty;
        draft.Registration = entry.Registration ?? string.Empty;
        draft.FlightNumber = entry.FlightNumber ?? string.Empty;
        draft.From = entry.From ?? string.Empty;
        draft.To = entry.To ?? string.Empty;
        draft.Route = entry.Route ?? string.Empty;
        draft.MultiPilot = entry.MultiPilot ?? 0;
        draft.PilotInCommand = entry.PilotInCommand ?? 0;
        draft.CoPilot = entry.CoPilot ?? 0;
        draft.Dual = entry.Dual ?? 0;
        draft.Instructor = entry.Instructor ?? 0;
        draft.Day = entry.Day ?? 0;
        draft.Night = entry.Night ?? 0;
        draft.InstrumentActual = entry.InstrumentActual ?? 0;
        draft.InstrumentSimulated = entry.InstrumentSimulated ?? 0;
        draft.TakeoffsDay = entry.TakeoffsDay;
        draft.TakeoffsNight = entry.TakeoffsNight;
        draft.LandingsDay = entry.LandingsDay;
        draft.LandingsNight = entry.LandingsNight;
        draft.IfrApproaches = entry.IfrApproaches;
        draft.Holding = entry.Holding;
        draft.Rnav = entry.Rnav;
        draft.Circling = entry.Circling;
        draft.Remarks = entry.Details ?? string.Empty;
        foreach (var customField in entry.CustomFields)
        {
            draft.CustomValues[customField.Key] = customField.Value ?? string.Empty;
        }

        return draft;
    }

    public void ClearUnsupportedWorkbookFields()
    {
        TakeoffsDay = null;
        TakeoffsNight = null;
    }

    public PortableLogbookEntry ToEntry(IEnumerable<CustomFieldDefinition> fields)
    {
        IReadOnlyDictionary<CustomFieldId, string?> customValues = fields
            .Select(field => new KeyValuePair<CustomFieldId, string>(field.Id, CustomValues.GetValueOrDefault(field.Id, string.Empty)))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => (string?)pair.Value);

        return PortableLogbookEntry.Empty with
        {
            Date = Date,
            AircraftType = AircraftType.Trim(),
            Registration = Registration.Trim(),
            FlightNumber = FlightNumber.Trim(),
            From = From.Trim(),
            To = To.Trim(),
            Route = Route.Trim(),
            Details = Remarks.Trim(),
            MultiPilot = MultiPilot,
            PilotInCommand = PilotInCommand,
            CoPilot = CoPilot,
            Dual = Dual,
            Instructor = Instructor,
            Day = Day,
            Night = Night,
            InstrumentActual = InstrumentActual,
            InstrumentSimulated = InstrumentSimulated,
            TakeoffsDay = TakeoffsDay,
            TakeoffsNight = TakeoffsNight,
            LandingsDay = LandingsDay,
            LandingsNight = LandingsNight,
            IfrApproaches = IfrApproaches,
            Holding = Holding,
            Rnav = Rnav,
            Circling = Circling,
            CustomFields = customValues
        };
    }
}

public enum MobileActionMessageSurface
{
    Draft,
    Logbook
}

internal enum MobileActionMessageScope
{
    Global,
    Draft
}
