using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public enum MobileConnectionStage
{
    CONFIG_LOAD,
    CREDENTIAL_LOAD,
    ACCESS_TOKEN_VALIDATE,
    AUTH_USER_READ,
    ACCOUNT_READ,
    DEVICE_READ,
    ANDROID_KEYSTORE_PROBE,
    INDEXEDDB_PROBE,
    LOCAL_PLAN_CREATE,
    PACKAGE_KEY_IMPORT,
    LOCAL_STATE_SAVE,
    LOCAL_STATE_READBACK,
    COMPLETE
}

public enum MobileCredentialState
{
    Missing,
    Pending,
    Registered
}

public sealed record MobileHostedCredentialSnapshot(
    MobileCredentialState State,
    HostedAccountId? AccountId,
    DeviceId? DeviceId,
    DateTimeOffset? AccessTokenExpiresAt);

public sealed record MobileHostedPrincipal(HostedAccountId AccountId);

public sealed record MobileHostedAccountCheck(bool Exists, bool IsActive, bool MatchesCredential);

public sealed record MobileHostedDeviceCheck(bool Exists, bool IsActive, bool MatchesAccountAndCredential);

public interface IMobileHostedRecoveryClient
{
    ValueTask ValidateConfigAsync(CancellationToken cancellationToken = default);

    ValueTask<MobileHostedCredentialSnapshot> LoadCredentialSnapshotAsync(CancellationToken cancellationToken = default);

    ValueTask ValidateAccessTokenAsync(CancellationToken cancellationToken = default);

    ValueTask<MobileHostedPrincipal> ReadAuthUserAsync(CancellationToken cancellationToken = default);

    ValueTask<MobileHostedAccountCheck> ReadAccountAsync(CancellationToken cancellationToken = default);

    ValueTask<MobileHostedDeviceCheck> ReadDeviceAsync(CancellationToken cancellationToken = default);

    ValueTask<HostedSyncSession> GetRegisteredSessionAsync(CancellationToken cancellationToken = default);
}

public sealed class MobileHostedDiagnosticException(
    string errorCode,
    string message,
    HttpStatusCode? httpStatus = null,
    string? supabaseCode = null,
    string? supabaseMessage = null,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    public string ErrorCode { get; } = errorCode;
    public HttpStatusCode? HttpStatus { get; } = httpStatus;
    public string? SupabaseCode { get; } = supabaseCode;
    public string? SupabaseMessage { get; } = supabaseMessage;
}

public sealed record MobileConnectionStageResult(
    MobileConnectionStage Stage,
    bool Passed,
    string ErrorCode,
    string Summary,
    string? ExceptionType = null,
    string? ExceptionMessage = null,
    IReadOnlyList<string>? InnerExceptions = null,
    string? StackTrace = null,
    int? HttpStatus = null,
    string? SupabaseCode = null,
    string? SupabaseMessage = null);

public sealed record MobileConnectionDiagnosticReport(
    string AttemptId,
    DateTimeOffset AttemptedAt,
    MobileConnectionStage CurrentStage,
    bool Passed,
    string ErrorCode,
    MobileCredentialState CredentialState,
    bool? AccountMatched,
    bool? DeviceMatched,
    bool? AndroidKeystorePassed,
    bool? IndexedDbPassed,
    bool? LocalPlanPassed,
    bool? PackageKeyImported,
    bool? LocalStateSaved,
    bool? LocalStateReadBack,
    IReadOnlyList<MobileConnectionStageResult> Stages)
{
    public string ToRedactedJson()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return JsonSerializer.Serialize(this, options);
    }
}

public sealed class MobileConnectionRecoveryWorkflow(
    IMobileHostedRecoveryClient remote,
    BrowserLogbookStore logbookStore,
    BrowserPackageKeyStore packageKeyStore,
    ISyncClock clock,
    Func<MobileConnectionStage, Exception?>? faultInjector = null)
{
    public async Task<MobileConnectionDiagnosticReport> RunPreflightAsync(
        CancellationToken cancellationToken = default)
    {
        var run = new DiagnosticRun(clock.UtcNow, faultInjector);
        MobileHostedCredentialSnapshot? credential = null;
        MobileHostedPrincipal? principal = null;

        if (!await run.TryAsync(MobileConnectionStage.CONFIG_LOAD, remote.ValidateConfigAsync, cancellationToken))
        {
            return run.Report();
        }

        if (!await run.TryAsync(MobileConnectionStage.CREDENTIAL_LOAD, async token =>
            {
                credential = await remote.LoadCredentialSnapshotAsync(token);
                run.CredentialState = credential.State;
                if (credential.State == MobileCredentialState.Missing)
                {
                    throw new MobileHostedDiagnosticException("CREDENTIAL_MISSING", "No retained hosted credential was found.");
                }

                if (credential.State == MobileCredentialState.Pending)
                {
                    throw new MobileHostedDiagnosticException("CREDENTIAL_PENDING", "The retained credential has no final registered device.");
                }
            }, cancellationToken))
        {
            return run.Report();
        }

        if (!await run.TryAsync(MobileConnectionStage.ACCESS_TOKEN_VALIDATE, remote.ValidateAccessTokenAsync, cancellationToken)
            || !await run.TryAsync(MobileConnectionStage.AUTH_USER_READ, async token =>
            {
                principal = await remote.ReadAuthUserAsync(token);
                if (credential?.AccountId != principal.AccountId)
                {
                    throw new MobileHostedDiagnosticException("AUTH_USER_MISMATCH", "The authenticated user does not match the retained account.");
                }
            }, cancellationToken)
            || !await run.TryAsync(MobileConnectionStage.ACCOUNT_READ, async token =>
            {
                var account = await remote.ReadAccountAsync(token);
                run.AccountMatched = account.MatchesCredential;
                if (!account.Exists || !account.IsActive || !account.MatchesCredential)
                {
                    throw new MobileHostedDiagnosticException(
                        !account.Exists ? "ACCOUNT_NOT_FOUND" : !account.IsActive ? "ACCOUNT_INACTIVE" : "ACCOUNT_MISMATCH",
                        "The active hosted account does not match the retained credential.");
                }
            }, cancellationToken)
            || !await run.TryAsync(MobileConnectionStage.DEVICE_READ, async token =>
            {
                var device = await remote.ReadDeviceAsync(token);
                run.DeviceMatched = device.MatchesAccountAndCredential;
                if (!device.Exists || !device.IsActive || !device.MatchesAccountAndCredential)
                {
                    throw new MobileHostedDiagnosticException(
                        !device.Exists ? "DEVICE_NOT_FOUND" : !device.IsActive ? "DEVICE_INACTIVE" : "DEVICE_MISMATCH",
                        "The active hosted device does not match the retained credential.");
                }
            }, cancellationToken)
            || !await run.TryAsync(MobileConnectionStage.ANDROID_KEYSTORE_PROBE, async token =>
            {
                await packageKeyStore.RunDisposableProbeAsync(token);
                run.AndroidKeystorePassed = true;
            }, cancellationToken)
            || !await run.TryAsync(MobileConnectionStage.INDEXEDDB_PROBE, async token =>
            {
                await logbookStore.RunDisposableProbeAsync(token);
                run.IndexedDbPassed = true;
            }, cancellationToken)
            || !await run.TryAsync(MobileConnectionStage.LOCAL_PLAN_CREATE, token =>
            {
                token.ThrowIfCancellationRequested();
                var session = new HostedSyncSession(credential!.AccountId!, credential.DeviceId!.Value, credential.AccessTokenExpiresAt!.Value);
                var plan = CreatePlan(session);
                var serialized = PortableLogbookJson.SerializeV2(plan.InitialDocument);
                var roundTrip = PortableLogbookJson.DeserializeV2(serialized);
                if (roundTrip?.LogbookId != plan.LogbookId || roundTrip.SchemaVersion != plan.InitialDocument.SchemaVersion)
                {
                    throw new MobileHostedDiagnosticException("LOCAL_PLAN_ROUNDTRIP_MISMATCH", "The disposable local-logbook serialization round trip did not match.");
                }

                run.LocalPlanPassed = true;
                return ValueTask.CompletedTask;
            }, cancellationToken))
        {
            return run.Report();
        }

        return run.Report(preflightPassed: true);
    }

    public async Task<MobileConnectionRecoveryResult> RecoverAsync(
        MobileConnectionDiagnosticReport? verifiedPreflight = null,
        CancellationToken cancellationToken = default)
    {
        var preflight = verifiedPreflight ?? await RunPreflightAsync(cancellationToken);
        if (!preflight.Passed)
        {
            return new MobileConnectionRecoveryResult(preflight, null, null);
        }

        var run = new DiagnosticRun(preflight, faultInjector);
        var previous = await logbookStore.LoadStateV2Async();
        var session = await remote.GetRegisteredSessionAsync(cancellationToken);
        var plan = CreatePlan(session);
        var keyImported = false;
        var stateSaved = false;
        try
        {
            if (!await run.TryAsync(MobileConnectionStage.PACKAGE_KEY_IMPORT, async token =>
                {
                    token.ThrowIfCancellationRequested();
                    var imported = await packageKeyStore.ImportRecoveryCodeAsync(plan.LogbookId, plan.Key.ToRecoveryCode());
                    keyImported = imported;
                    if (!imported || !await packageKeyStore.HasPackageKeyAsync(plan.LogbookId))
                    {
                        throw new MobileHostedDiagnosticException("PACKAGE_KEY_IMPORT_NOT_RETAINED", "The package key import was not retained.");
                    }

                    await packageKeyStore.VerifyPackageKeyAsync(plan.LogbookId, cancellationToken: token);
                    run.PackageKeyImported = true;
                }, cancellationToken))
            {
                return new MobileConnectionRecoveryResult(run.Report(), null, null);
            }

            var hosted = new BrowserHostedSyncState(
                session.AccountId,
                plan.LogbookId,
                session.DeviceId,
                0,
                PortableHostedSyncStatus.Synced,
                clock.UtcNow,
                clock.UtcNow);
            var state = new BrowserLogbookStateV2(plan.InitialDocument, [], null, null, hosted);
            if (!await run.TryAsync(MobileConnectionStage.LOCAL_STATE_SAVE, async token =>
                {
                    token.ThrowIfCancellationRequested();
                    await logbookStore.SaveStateAsync(state);
                    stateSaved = true;
                    run.LocalStateSaved = true;
                }, cancellationToken)
                || !await run.TryAsync(MobileConnectionStage.LOCAL_STATE_READBACK, async token =>
                {
                    token.ThrowIfCancellationRequested();
                    var reloaded = await logbookStore.LoadStateV2Async();
                    if (reloaded?.Document.LogbookId != plan.LogbookId
                        || reloaded.HostedSync?.LogbookId != plan.LogbookId
                        || reloaded.HostedSync.AccountId != session.AccountId
                        || reloaded.HostedSync.DeviceId != session.DeviceId
                        || !await packageKeyStore.HasPackageKeyAsync(plan.LogbookId))
                    {
                        throw new MobileHostedDiagnosticException("LOCAL_STATE_READBACK_MISMATCH", "Saved local identifiers did not match on readback.");
                    }

                    run.LocalStateReadBack = true;
                }, cancellationToken)
                || !await run.TryAsync(MobileConnectionStage.COMPLETE, _ => ValueTask.CompletedTask, cancellationToken))
            {
                return new MobileConnectionRecoveryResult(run.Report(), null, null);
            }

            return new MobileConnectionRecoveryResult(run.Report(recoveryComplete: true), plan.InitialDocument, hosted);
        }
        finally
        {
            if (!run.Completed)
            {
                if (stateSaved)
                {
                    await logbookStore.RestoreStateV2Async(previous);
                }

                if (keyImported)
                {
                    await packageKeyStore.DeletePackageKeyAsync(plan.LogbookId);
                }
            }
        }
    }

    private MobileAppOnlyLogbookPlan CreatePlan(HostedSyncSession session) =>
        MobileAppOnlyLogbookPlan.Create(session.DeviceId);

    private sealed class DiagnosticRun
    {
        private readonly List<MobileConnectionStageResult> stages = [];

        private readonly Func<MobileConnectionStage, Exception?>? faultInjector;

        public DiagnosticRun(
            DateTimeOffset attemptedAt,
            Func<MobileConnectionStage, Exception?>? faultInjector)
        {
            AttemptedAt = attemptedAt;
            AttemptId = Guid.NewGuid().ToString("N");
            this.faultInjector = faultInjector;
        }

        public DiagnosticRun(
            MobileConnectionDiagnosticReport preflight,
            Func<MobileConnectionStage, Exception?>? faultInjector)
        {
            AttemptedAt = preflight.AttemptedAt;
            AttemptId = preflight.AttemptId;
            stages.AddRange(preflight.Stages);
            CredentialState = preflight.CredentialState;
            AccountMatched = preflight.AccountMatched;
            DeviceMatched = preflight.DeviceMatched;
            AndroidKeystorePassed = preflight.AndroidKeystorePassed;
            IndexedDbPassed = preflight.IndexedDbPassed;
            LocalPlanPassed = preflight.LocalPlanPassed;
            this.faultInjector = faultInjector;
        }

        public string AttemptId { get; }
        public DateTimeOffset AttemptedAt { get; }
        public MobileCredentialState CredentialState { get; set; }
        public bool? AccountMatched { get; set; }
        public bool? DeviceMatched { get; set; }
        public bool? AndroidKeystorePassed { get; set; }
        public bool? IndexedDbPassed { get; set; }
        public bool? LocalPlanPassed { get; set; }
        public bool? PackageKeyImported { get; set; }
        public bool? LocalStateSaved { get; set; }
        public bool? LocalStateReadBack { get; set; }
        public bool Completed => stages.LastOrDefault()?.Stage == MobileConnectionStage.COMPLETE && stages[^1].Passed;

        public async Task<bool> TryAsync(
            MobileConnectionStage stage,
            Func<CancellationToken, ValueTask> action,
            CancellationToken cancellationToken)
        {
            try
            {
                if (faultInjector?.Invoke(stage) is { } injected)
                {
                    throw injected;
                }

                await action(cancellationToken);
                stages.Add(new MobileConnectionStageResult(stage, true, "OK", "Passed"));
                return true;
            }
            catch (Exception ex)
            {
                var diagnostic = ex as MobileHostedDiagnosticException;
                stages.Add(new MobileConnectionStageResult(
                    stage,
                    false,
                    diagnostic?.ErrorCode ?? $"UNEXPECTED_{ex.GetType().Name}",
                    "Failed",
                    ex.GetType().FullName,
                    MobileDiagnosticRedactor.Redact(ex.Message),
                    ExceptionChain(ex.InnerException),
                    MobileDiagnosticRedactor.Redact(ex.StackTrace),
                    diagnostic?.HttpStatus is null ? null : (int)diagnostic.HttpStatus,
                    MobileDiagnosticRedactor.Redact(diagnostic?.SupabaseCode),
                    MobileDiagnosticRedactor.Redact(diagnostic?.SupabaseMessage)));
                return false;
            }
        }

        public MobileConnectionDiagnosticReport Report(bool preflightPassed = false, bool recoveryComplete = false)
        {
            var failed = stages.LastOrDefault(result => !result.Passed);
            var current = stages.LastOrDefault()?.Stage ?? MobileConnectionStage.CONFIG_LOAD;
            return new MobileConnectionDiagnosticReport(
                AttemptId,
                AttemptedAt,
                current,
                recoveryComplete || preflightPassed,
                failed?.ErrorCode ?? (recoveryComplete ? "COMPLETE" : "PREFLIGHT_COMPLETE"),
                CredentialState,
                AccountMatched,
                DeviceMatched,
                AndroidKeystorePassed,
                IndexedDbPassed,
                LocalPlanPassed,
                PackageKeyImported,
                LocalStateSaved,
                LocalStateReadBack,
                stages.ToArray());
        }

        private static IReadOnlyList<string> ExceptionChain(Exception? exception)
        {
            var chain = new List<string>();
            while (exception is not null)
            {
                chain.Add(MobileDiagnosticRedactor.Redact($"{exception.GetType().FullName}: {exception.Message}")!);
                exception = exception.InnerException;
            }

            return chain;
        }
    }
}

public sealed record MobileConnectionRecoveryResult(
    MobileConnectionDiagnosticReport Diagnostics,
    PortableLogbookDocumentV2? Document,
    BrowserHostedSyncState? HostedSync);

public sealed record MobileAppOnlyLogbookPlan(
    LogbookId LogbookId,
    DeviceId DeviceId,
    PortableLogbookKey Key,
    PortableLogbookDocumentV2 InitialDocument)
{
    public static MobileAppOnlyLogbookPlan Create(DeviceId deviceId)
    {
        var logbookId = LogbookId.New();
        var key = PortableLogbookKey.Generate();
        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(
            logbookId,
            MobileLogbookSession.CustomFields,
            PortableLogbookCurrencyOverrideDates.Empty,
            []);
        return new MobileAppOnlyLogbookPlan(logbookId, deviceId, key, document);
    }
}

public static partial class MobileDiagnosticRedactor
{
    public static string? Redact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var redacted = EmailRegex().Replace(value, "[email-redacted]");
        redacted = SensitiveFieldRegex().Replace(redacted, match => $"{match.Groups[1].Value}[secret-redacted]");
        redacted = UrlRegex().Replace(redacted, "[project-url-redacted]");
        redacted = JwtRegex().Replace(redacted, "[token-redacted]");
        redacted = RecoveryCodeRegex().Replace(redacted, "[recovery-code-redacted]");
        redacted = GuidRegex().Replace(redacted, "[identifier-redacted]");
        redacted = IdentifierRegex().Replace(redacted, match => $"{match.Groups[1].Value}[identifier-redacted]");
        redacted = LongEncodedValueRegex().Replace(redacted, "[secret-redacted]");
        return redacted;
    }

    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"https://[^\s\""']+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b")]
    private static partial Regex JwtRegex();

    [GeneratedRegex(@"\b(?:[A-Z2-7]{4}[ -]){7}[A-Z2-7]{4}\b", RegexOptions.IgnoreCase)]
    private static partial Regex RecoveryCodeRegex();

    [GeneratedRegex(@"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b", RegexOptions.IgnoreCase)]
    private static partial Regex GuidRegex();

    [GeneratedRegex(@"\b(acct_|dev_|log_|rev_|ent_)[A-Za-z0-9_-]{8,}\b")]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex(@"(?i)(\b(?:access_token|refresh_token|anon_key|apikey|recovery_code|payload(?:_ciphertext)?|package_key)\b\s*[:=]\s*[\""']?)[^\s,}\""']+")]
    private static partial Regex SensitiveFieldRegex();

    [GeneratedRegex(@"\b[A-Za-z0-9+/=_-]{48,}\b")]
    private static partial Regex LongEncodedValueRegex();
}
