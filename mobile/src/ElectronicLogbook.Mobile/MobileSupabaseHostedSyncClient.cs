using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public sealed class MobileSupabaseHostedSyncClient(
    HttpClient http,
    BrowserHostedCredentialStore credentialStore,
    ISyncClock clock,
    BrowserGoogleCredentialProvider? googleCredentialProvider = null)
    : IHostedLogbookAuthenticator, IHostedLogbookLedger, IMobileHostedRecoveryClient,
      IMobileGoogleHostedAuthenticator, IMobileRecoveryEnvelopeService,
      IMobileReplacementRecoveryClient
{
    private const string ConfigPath = "hosted-sync.local.json";
    private const string RecoveryEnvelopePath = "/functions/v1/recovery-envelope";
    private const string PendingDeviceIdValue = "dev_pending";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private readonly HashSet<LogbookId> ensuredLogbooks = [];
    private MobileHostedSyncConfig? config;
    private BrowserHostedCredential? credential;
    private string? pendingEmail;

    public async ValueTask ValidateConfigAsync(CancellationToken cancellationToken = default)
    {
        var options = await GetConfigAsync(cancellationToken);
        var projectRef = ProjectRef(options);
        var claims = ReadJwtClaims(options.AnonKey, "ANON_KEY_INVALID");
        var role = claims.TryGetProperty("role", out var roleValue) ? roleValue.GetString() : null;
        var reference = claims.TryGetProperty("ref", out var refValue) ? refValue.GetString() : null;
        if (!string.Equals(role, "anon", StringComparison.Ordinal)
            && !string.Equals(role, "publishable", StringComparison.Ordinal))
        {
            throw new MobileHostedDiagnosticException("ANON_KEY_ROLE_MISMATCH", "The packaged Supabase key does not have the anonymous role.");
        }

        if (!string.IsNullOrWhiteSpace(reference)
            && !string.Equals(reference, projectRef, StringComparison.Ordinal))
        {
            throw new MobileHostedDiagnosticException("ANON_KEY_PROJECT_MISMATCH", "The packaged Supabase key targets a different project.");
        }

        if (claims.TryGetProperty("exp", out var expiryValue)
            && expiryValue.TryGetInt64(out var expiry)
            && DateTimeOffset.FromUnixTimeSeconds(expiry) <= clock.UtcNow)
        {
            throw new MobileHostedDiagnosticException("ANON_KEY_EXPIRED", "The packaged Supabase key is expired.");
        }
    }

    public async ValueTask<MobileHostedCredentialSnapshot> LoadCredentialSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        credential = await credentialStore.LoadAsync();
        return credential is null
            ? new MobileHostedCredentialSnapshot(MobileCredentialState.Missing, null, null, null)
            : new MobileHostedCredentialSnapshot(
                IsPendingCredential(credential) ? MobileCredentialState.Pending : MobileCredentialState.Registered,
                credential.AccountId,
                credential.DeviceId,
                credential.AccessTokenExpiresAt);
    }

    public async ValueTask ValidateAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        credential ??= await credentialStore.LoadAsync();
        if (credential is null)
        {
            throw new MobileHostedDiagnosticException("CREDENTIAL_MISSING", "No retained hosted credential was found.");
        }

        if (credential.AccessTokenExpiresAt <= clock.UtcNow)
        {
            try
            {
                await RefreshAsync(cancellationToken);
            }
            catch (HostedSignInException ex)
            {
                throw new MobileHostedDiagnosticException(
                    "REFRESH_TOKEN_REJECTED",
                    "The expired access token could not be refreshed from the retained session.",
                    innerException: ex);
            }
        }

        var options = await GetConfigAsync(cancellationToken);
        var claims = ReadJwtClaims(credential.AccessToken, "ACCESS_TOKEN_INVALID");
        if (claims.TryGetProperty("iss", out var issuerValue)
            && Uri.TryCreate(issuerValue.GetString(), UriKind.Absolute, out var issuer)
            && !string.Equals(issuer.Host, new Uri(options.SupabaseUrl).Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new MobileHostedDiagnosticException("ACCESS_TOKEN_PROJECT_MISMATCH", "The retained access token targets a different Supabase project.");
        }
    }

    public async ValueTask<MobileHostedPrincipal> ReadAuthUserAsync(CancellationToken cancellationToken = default)
    {
        credential ??= await credentialStore.LoadAsync();
        if (credential is null)
        {
            throw new MobileHostedDiagnosticException("CREDENTIAL_MISSING", "No retained hosted credential was found.");
        }

        var options = await GetConfigAsync(cancellationToken);
        using var request = NewRequest(options, HttpMethod.Get, "/auth/v1/user", includeAuthorization: true);
        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ToDiagnosticException("ACCESS_TOKEN_REJECTED", response.StatusCode, body);
        }

        var user = JsonSerializer.Deserialize<AuthUserResponse>(body, WebJson);
        return new MobileHostedPrincipal(new HostedAccountId("acct_" + ParseRequiredGuid(user?.Id, "account id").ToString("N")));
    }

    public async ValueTask<MobileHostedAccountCheck> ReadAccountAsync(CancellationToken cancellationToken = default)
    {
        credential ??= await credentialStore.LoadAsync();
        if (credential is null)
        {
            throw new MobileHostedDiagnosticException("CREDENTIAL_MISSING", "No retained hosted credential was found.");
        }

        var options = await GetConfigAsync(cancellationToken);
        var accountUuid = FromHostedAccountId(credential.AccountId);
        var rows = await GetRestAsync<HostedAccountRow[]>(
            options,
            $"/rest/v1/accounts?select=account_id,status&account_id=eq.{accountUuid}",
            cancellationToken);
        var row = rows.SingleOrDefault();
        return new MobileHostedAccountCheck(
            row is not null,
            string.Equals(row?.Status, "active", StringComparison.OrdinalIgnoreCase),
            row is not null && string.Equals(row.AccountId, accountUuid, StringComparison.OrdinalIgnoreCase));
    }

    public async ValueTask<MobileHostedDeviceCheck> ReadDeviceAsync(CancellationToken cancellationToken = default)
    {
        credential ??= await credentialStore.LoadAsync();
        if (credential is null)
        {
            throw new MobileHostedDiagnosticException("CREDENTIAL_MISSING", "No retained hosted credential was found.");
        }

        var options = await GetConfigAsync(cancellationToken);
        var accountUuid = FromHostedAccountId(credential.AccountId);
        var deviceUuid = ToHostedUuid(credential.DeviceId.Value, "dev_");
        var rows = await GetRestAsync<HostedDeviceRow[]>(
            options,
            $"/rest/v1/devices?select=device_id,account_id,status&device_id=eq.{deviceUuid}",
            cancellationToken);
        var row = rows.SingleOrDefault();
        return new MobileHostedDeviceCheck(
            row is not null,
            string.Equals(row?.Status, "active", StringComparison.OrdinalIgnoreCase),
            row is not null
                && string.Equals(row.DeviceId, deviceUuid, StringComparison.OrdinalIgnoreCase)
                && string.Equals(row.AccountId, accountUuid, StringComparison.OrdinalIgnoreCase));
    }

    public async ValueTask<HostedSyncSession> GetRegisteredSessionAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await LoadCredentialSnapshotAsync(cancellationToken);
        if (snapshot.State != MobileCredentialState.Registered)
        {
            throw new MobileHostedDiagnosticException("CREDENTIAL_NOT_REGISTERED", "A final registered credential is required for recovery.");
        }

        return new HostedSyncSession(snapshot.AccountId!, snapshot.DeviceId!.Value, snapshot.AccessTokenExpiresAt!.Value);
    }

    public async ValueTask<HostedSyncSession?> GetCurrentSessionAsync(CancellationToken cancellationToken = default)
    {
        credential ??= await credentialStore.LoadAsync();
        return credential is null || IsPendingCredential(credential)
            ? null
            : new HostedSyncSession(credential.AccountId, credential.DeviceId, credential.AccessTokenExpiresAt);
    }

    public async ValueTask<HostedSignInStart> StartEmailSignInAsync(
        string email,
        bool shouldCreateUser = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        if (shouldCreateUser)
        {
            throw new HostedSignInException(
                HostedSignInFailureReason.PublicRegistrationBlocked,
                "Public account registration is disabled for the private pilot.");
        }

        var options = await GetConfigAsync(cancellationToken);
        using var request = NewRequest(options, HttpMethod.Post, "/auth/v1/otp", includeAuthorization: false);
        request.Content = JsonContent(new OtpRequest(email.Trim(), CreateUser: false));
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ToSignInException(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        }

        pendingEmail = email.Trim();
        return new HostedSignInStart(
            new HostedAccountId("acct_pending"),
            MaskEmail(pendingEmail),
            clock.UtcNow.AddMinutes(10));
    }

    public async ValueTask<HostedSyncSession> CompleteEmailSignInAsync(
        string verificationCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verificationCode);
        var options = await GetConfigAsync(cancellationToken);
        using var verify = NewRequest(options, HttpMethod.Post, "/auth/v1/verify", includeAuthorization: false);
        verify.Content = CreateVerifyContent(options, pendingEmail, verificationCode);
        using var response = await http.SendAsync(verify, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ToSignInException(response.StatusCode, body);
        }

        var verified = JsonSerializer.Deserialize<AuthSessionResponse>(body, WebJson)
            ?? throw new HostedSignInException(HostedSignInFailureReason.InvalidVerificationCode, "Hosted sign-in returned no session.");
        var accountId = new HostedAccountId("acct_" + ParseRequiredGuid(verified.User?.Id, "account id").ToString("N"));
        var expiresAt = clock.UtcNow.AddSeconds(Math.Max(verified.ExpiresIn, 60));

        var temporaryCredential = new BrowserHostedCredential(
            accountId,
            new DeviceId(PendingDeviceIdValue),
            verified.AccessToken,
            verified.RefreshToken,
            expiresAt,
            DeviceRegistrationPending: true);
        credential = temporaryCredential;
        await credentialStore.SaveAsync(temporaryCredential);

        return await CompletePendingDeviceRegistrationAsync(options, cancellationToken);
    }

    public async ValueTask<HostedSyncSession> ResumeEmailSignInAsync(
        CancellationToken cancellationToken = default)
    {
        credential = await credentialStore.LoadAsync();
        if (credential is null)
        {
            throw new HostedSignInException(
                HostedSignInFailureReason.SignedOut,
                "No verified sign-in is available to resume.");
        }

        if (credential.AccessTokenExpiresAt <= clock.UtcNow.AddMinutes(1))
        {
            await RefreshAsync(cancellationToken);
        }

        if (!IsPendingCredential(credential))
        {
            return new HostedSyncSession(
                credential.AccountId,
                credential.DeviceId,
                credential.AccessTokenExpiresAt);
        }

        var options = await GetConfigAsync(cancellationToken);
        return await CompletePendingDeviceRegistrationAsync(options, cancellationToken);
    }

    public async ValueTask<HostedSyncSession> SignInWithGoogleAsync(
        CancellationToken cancellationToken = default)
    {
        var options = await GetConfigAsync(cancellationToken);
        var googleCredential = await GetGoogleCredentialAsync(options, cancellationToken);
        var verified = await ExchangeGoogleIdTokenAsync(
            options,
            googleCredential,
            linkIdentity: false,
            cancellationToken);
        var accountId = new HostedAccountId("acct_" + ParseRequiredGuid(verified.User?.Id, "account id").ToString("N"));
        credential = new BrowserHostedCredential(
            accountId,
            new DeviceId(PendingDeviceIdValue),
            verified.AccessToken,
            verified.RefreshToken,
            clock.UtcNow.AddSeconds(Math.Max(verified.ExpiresIn, 60)),
            DeviceRegistrationPending: true);
        await credentialStore.SaveAsync(credential);
        return await CompletePendingDeviceRegistrationAsync(options, cancellationToken);
    }

    public async ValueTask<HostedSyncSession> LinkGoogleIdentityAsync(
        CancellationToken cancellationToken = default)
    {
        credential ??= await credentialStore.LoadAsync();
        if (credential is null || IsPendingCredential(credential))
        {
            throw new HostedSignInException(
                HostedSignInFailureReason.SignedOut,
                "Connect the invited account before adding Google sign-in.");
        }

        var options = await GetConfigAsync(cancellationToken);
        var googleCredential = await GetGoogleCredentialAsync(options, cancellationToken);
        var linked = await ExchangeGoogleIdTokenAsync(
            options,
            googleCredential,
            linkIdentity: true,
            cancellationToken);
        var linkedAccountId = new HostedAccountId(
            "acct_" + ParseRequiredGuid(linked.User?.Id, "account id").ToString("N"));
        if (linkedAccountId != credential.AccountId)
        {
            throw new HostedSignInException(
                HostedSignInFailureReason.AccountDisabled,
                "Google sign-in did not link to the connected invited account.");
        }

        credential = credential with
        {
            AccessToken = linked.AccessToken,
            RefreshToken = string.IsNullOrWhiteSpace(linked.RefreshToken) ? credential.RefreshToken : linked.RefreshToken,
            AccessTokenExpiresAt = clock.UtcNow.AddSeconds(Math.Max(linked.ExpiresIn, 60))
        };
        await credentialStore.SaveAsync(credential);
        return new HostedSyncSession(credential.AccountId, credential.DeviceId, credential.AccessTokenExpiresAt);
    }

    private async ValueTask<HostedSyncSession> CompletePendingDeviceRegistrationAsync(
        MobileHostedSyncConfig options,
        CancellationToken cancellationToken)
    {
        if (credential is null || !IsPendingCredential(credential))
        {
            throw new HostedSignInException(
                HostedSignInFailureReason.SignedOut,
                "No verified sign-in is available to resume.");
        }

        var existingLogbooks = await DiscoverActiveLogbooksAsync(cancellationToken);
        if (existingLogbooks.Count > 0)
        {
            throw new HostedSignInException(
                HostedSignInFailureReason.AccountRecoveryRequired,
                "Your existing logbook cannot yet be opened on this phone. No new device or logbook was created.");
        }

        var device = await AcceptInvitationAsync(options, cancellationToken);
        credential = credential with { DeviceId = device.DeviceId, DeviceRegistrationPending = false };
        await credentialStore.SaveAsync(credential);
        pendingEmail = null;
        return new HostedSyncSession(credential.AccountId, device.DeviceId, credential.AccessTokenExpiresAt);
    }

    private static bool IsPendingCredential(BrowserHostedCredential value) =>
        value.DeviceRegistrationPending
        || string.Equals(value.DeviceId.Value, PendingDeviceIdValue, StringComparison.Ordinal);

    public async ValueTask<MobileReplacementRecoveryContext> PrepareReplacementRecoveryAsync(
        LogbookId logbookId,
        CancellationToken cancellationToken = default)
    {
        credential = await credentialStore.LoadAsync();
        if (credential is null || !IsPendingCredential(credential))
        {
            throw new MobileHostedDiagnosticException(
                "RECOVERY_REGISTRATION_NOT_PENDING",
                "No authenticated replacement-device recovery is pending.");
        }

        var memberships = await DiscoverActiveLogbooksAsync(cancellationToken);
        var membership = memberships.SingleOrDefault(value => value.LogbookId == logbookId)
            ?? throw new MobileHostedDiagnosticException(
                "RECOVERY_LOGBOOK_ACCESS_DENIED",
                "The selected logbook is not available to this account.");
        if (memberships.Count != 1)
        {
            throw new MobileHostedDiagnosticException(
                "RECOVERY_LOGBOOK_SELECTION_REQUIRED",
                "Select exactly one logbook before continuing account recovery.");
        }

        if (string.Equals(credential.DeviceId.Value, PendingDeviceIdValue, StringComparison.Ordinal))
        {
            credential = credential with
            {
                DeviceId = DeviceId.New(),
                DeviceRegistrationPending = true
            };
            await credentialStore.SaveAsync(credential);
        }

        var options = await GetConfigAsync(cancellationToken);
        return new MobileReplacementRecoveryContext(
            new HostedSyncSession(credential.AccountId, credential.DeviceId, credential.AccessTokenExpiresAt),
            membership,
            options.PlatformLabel ?? "Android");
    }

    public async ValueTask CompleteReplacementRecoveryAsync(
        LogbookId logbookId,
        DeviceId deviceId,
        CancellationToken cancellationToken = default)
    {
        credential = await credentialStore.LoadAsync();
        if (credential is null || credential.DeviceId != deviceId)
        {
            throw new MobileHostedDiagnosticException(
                "RECOVERY_DEVICE_MISMATCH",
                "The recovered device does not match the authenticated recovery attempt.");
        }

        var activated = await ActivateAsync(
            new MobileRecoveryDeviceActivationRequest(logbookId, deviceId),
            cancellationToken);
        if (!activated.Activated)
        {
            throw new MobileHostedDiagnosticException(
                "RECOVERY_ACTIVATION_INCOMPLETE",
                "Account recovery is not complete.");
        }

        credential = credential with { DeviceRegistrationPending = false };
        await credentialStore.SaveAsync(credential);
    }

    private async ValueTask<GoogleIdTokenCredential> GetGoogleCredentialAsync(
        MobileHostedSyncConfig options,
        CancellationToken cancellationToken)
    {
        if (googleCredentialProvider is null || string.IsNullOrWhiteSpace(options.GoogleWebClientId))
        {
            throw new InvalidOperationException("Google sign-in is not configured in this Android build.");
        }

        return await googleCredentialProvider.GetAsync(options.GoogleWebClientId, cancellationToken);
    }

    private async ValueTask<AuthSessionResponse> ExchangeGoogleIdTokenAsync(
        MobileHostedSyncConfig options,
        GoogleIdTokenCredential googleCredential,
        bool linkIdentity,
        CancellationToken cancellationToken)
    {
        using var request = NewRequest(
            options,
            HttpMethod.Post,
            "/auth/v1/token?grant_type=id_token",
            includeAuthorization: linkIdentity);
        request.Content = JsonContent(new GoogleIdTokenRequest(
            "google",
            googleCredential.IdToken,
            googleCredential.Nonce,
            linkIdentity));
        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ToSignInException(response.StatusCode, body);
        }

        return JsonSerializer.Deserialize<AuthSessionResponse>(body, WebJson)
            ?? throw new HostedSignInException(
                HostedSignInFailureReason.InvalidVerificationCode,
                "Google sign-in returned no Supabase session.");
    }

    public async ValueTask<IReadOnlyList<MobileHostedLogbookMembership>> DiscoverActiveLogbooksAsync(
        CancellationToken cancellationToken = default)
    {
        credential ??= await credentialStore.LoadAsync();
        if (credential is null)
        {
            throw new HostedSignInException(HostedSignInFailureReason.SignedOut, "Hosted sync is signed out.");
        }

        var options = await GetConfigAsync(cancellationToken);
        var accountUuid = FromHostedAccountId(credential.AccountId);
        var rows = await GetRestAsync<HostedLogbookMembershipRow[]>(
            options,
            "/rest/v1/logbook_memberships"
            + "?select=logbook_id,role,logbooks!inner(logbook_id,current_schema_version,operation_format_version,deletion_requested_at,deleted_at)"
            + $"&account_id=eq.{accountUuid}"
            + "&accepted_at=not.is.null&revoked_at=is.null"
            + "&logbooks.deletion_requested_at=is.null&logbooks.deleted_at=is.null"
            + "&order=granted_at.asc",
            cancellationToken);
        return rows
            .Select(row => new MobileHostedLogbookMembership(
                FromHostedLogbookUuid(row.LogbookId),
                row.Role,
                row.Logbook.CurrentSchemaVersion,
                row.Logbook.OperationFormatVersion))
            .ToArray();
    }

    public async ValueTask<HostedSyncSession> RefreshAsync(CancellationToken cancellationToken = default)
    {
        credential ??= await credentialStore.LoadAsync();
        if (credential is null)
        {
            throw new HostedSignInException(HostedSignInFailureReason.SignedOut, "Hosted sync is signed out.");
        }

        var options = await GetConfigAsync(cancellationToken);
        using var request = NewRequest(options, HttpMethod.Post, "/auth/v1/token?grant_type=refresh_token", includeAuthorization: false);
        request.Content = JsonContent(new RefreshRequest(credential.RefreshToken));
        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HostedSignInException(
                HostedSignInFailureReason.RefreshTokenRevoked,
                $"Hosted sign-in refresh failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        var refreshed = JsonSerializer.Deserialize<AuthSessionResponse>(body, WebJson)
            ?? throw new HostedSignInException(HostedSignInFailureReason.RefreshTokenRevoked, "Hosted sign-in refresh returned no session.");
        credential = credential with
        {
            AccessToken = refreshed.AccessToken,
            RefreshToken = string.IsNullOrWhiteSpace(refreshed.RefreshToken) ? credential.RefreshToken : refreshed.RefreshToken,
            AccessTokenExpiresAt = clock.UtcNow.AddSeconds(Math.Max(refreshed.ExpiresIn, 60))
        };
        await credentialStore.SaveAsync(credential);
        return new HostedSyncSession(credential.AccountId, credential.DeviceId, credential.AccessTokenExpiresAt);
    }

    public ValueTask<MobileRecoveryEnvelopeConfiguration> GetConfigurationAsync(
        CancellationToken cancellationToken = default) =>
        SendRecoveryEnvelopeAsync<MobileRecoveryEnvelopeConfiguration>(
            new RecoveryEnvelopeRequest("configuration"),
            cancellationToken);

    public ValueTask<MobileRecoverySetupStatus> GetRecoverySetupStatusAsync(
        MobileRecoverySetupStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendRecoveryEnvelopeAsync<MobileRecoverySetupStatus>(
            new RecoveryEnvelopeRequest(
                "status",
                ToRecoveryUuid(request.LogbookId.Value, "log_"),
                ToRecoveryUuid(request.DeviceId.Value, "dev_")),
            cancellationToken);
    }

    public ValueTask<MobileRecoveryEnvelopeEnrollmentResult> EnrollAsync(
        MobileRecoveryEnvelopeEnrollmentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.DeviceKey);
        ValidateRecoveryValue(request.DeviceKey.PublicKey, nameof(request.DeviceKey.PublicKey));
        ValidateRecoveryValue(request.DeviceKey.Fingerprint, nameof(request.DeviceKey.Fingerprint));
        ValidateRecoveryValue(request.DeviceKey.Algorithm, nameof(request.DeviceKey.Algorithm));
        ValidateRecoveryValue(request.WrappedPackageKey, nameof(request.WrappedPackageKey));
        ValidateRecoveryValue(request.IngressKeyVersionId, nameof(request.IngressKeyVersionId));

        return SendRecoveryEnvelopeAsync<MobileRecoveryEnvelopeEnrollmentResult>(
            new RecoveryEnvelopeRequest(
                "enroll",
                ToRecoveryUuid(request.LogbookId.Value, "log_"),
                ToRecoveryUuid(request.DeviceId.Value, "dev_"),
                request.DeviceKey.PublicKey,
                request.DeviceKey.Fingerprint,
                request.DeviceKey.Algorithm,
                request.WrappedPackageKey,
                request.IngressKeyVersionId),
            cancellationToken);
    }

    public ValueTask<MobileRecoveryEnvelopeRestoreResult> RestoreAsync(
        MobileRecoveryEnvelopeRestoreRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.DeviceKey);
        ValidateRecoveryValue(request.DeviceKey.PublicKey, nameof(request.DeviceKey.PublicKey));
        ValidateRecoveryValue(request.DeviceKey.Fingerprint, nameof(request.DeviceKey.Fingerprint));
        ValidateRecoveryValue(request.DeviceKey.Algorithm, nameof(request.DeviceKey.Algorithm));
        ValidateRecoveryValue(request.PlatformLabel, nameof(request.PlatformLabel));

        return SendRecoveryEnvelopeAsync<MobileRecoveryEnvelopeRestoreResult>(
            new RecoveryEnvelopeRequest(
                "restore",
                ToRecoveryUuid(request.LogbookId.Value, "log_"),
                ToRecoveryUuid(request.DeviceId.Value, "dev_"),
                request.DeviceKey.PublicKey,
                request.DeviceKey.Fingerprint,
                request.DeviceKey.Algorithm,
                PlatformLabel: request.PlatformLabel,
                DeviceType: "android"),
            cancellationToken);
    }

    public ValueTask<MobileRecoveryCodeEnrollmentResult> EnrollRecoveryCodeAsync(
        MobileRecoveryCodeEnrollmentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Envelope);
        ValidateRecoveryValue(request.Envelope.Ciphertext, nameof(request.Envelope.Ciphertext));
        ValidateRecoveryValue(request.Envelope.Nonce, nameof(request.Envelope.Nonce));
        ValidateRecoveryValue(request.Envelope.Salt, nameof(request.Envelope.Salt));
        ValidateRecoveryValue(request.Envelope.Algorithm, nameof(request.Envelope.Algorithm));
        ValidateRecoveryValue(request.Envelope.KeyVersionId, nameof(request.Envelope.KeyVersionId));
        return SendRecoveryEnvelopeAsync<MobileRecoveryCodeEnrollmentResult>(
            new RecoveryEnvelopeRequest(
                "enroll-code",
                ToRecoveryUuid(request.LogbookId.Value, "log_"),
                ToRecoveryUuid(request.DeviceId.Value, "dev_"),
                RecoveryCiphertext: request.Envelope.Ciphertext,
                RecoveryNonce: request.Envelope.Nonce,
                RecoverySalt: request.Envelope.Salt,
                RecoveryAlgorithm: request.Envelope.Algorithm,
                RecoveryKeyVersionId: request.Envelope.KeyVersionId),
            cancellationToken);
    }

    public ValueTask<MobileRecoveryCodeEnvelopePayload> RestoreWithRecoveryCodeAsync(
        MobileRecoveryCodeRestoreRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.DeviceKey);
        ValidateRecoveryValue(request.PlatformLabel, nameof(request.PlatformLabel));
        ValidateRecoveryValue(request.DeviceKey.PublicKey, nameof(request.DeviceKey.PublicKey));
        ValidateRecoveryValue(request.DeviceKey.Fingerprint, nameof(request.DeviceKey.Fingerprint));
        ValidateRecoveryValue(request.DeviceKey.Algorithm, nameof(request.DeviceKey.Algorithm));
        return SendRecoveryEnvelopeAsync<MobileRecoveryCodeEnvelopePayload>(
            new RecoveryEnvelopeRequest(
                "restore-code",
                ToRecoveryUuid(request.LogbookId.Value, "log_"),
                ToRecoveryUuid(request.DeviceId.Value, "dev_"),
                request.DeviceKey.PublicKey,
                request.DeviceKey.Fingerprint,
                request.DeviceKey.Algorithm,
                PlatformLabel: request.PlatformLabel,
                DeviceType: "android"),
            cancellationToken);
    }

    public ValueTask<MobileRecoveryDeviceActivationResult> ActivateAsync(
        MobileRecoveryDeviceActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendRecoveryEnvelopeAsync<MobileRecoveryDeviceActivationResult>(
            new RecoveryEnvelopeRequest(
                "activate",
                ToRecoveryUuid(request.LogbookId.Value, "log_"),
                ToRecoveryUuid(request.DeviceId.Value, "dev_")),
            cancellationToken);
    }

    public async ValueTask SignOutAsync(CancellationToken cancellationToken = default)
    {
        await credentialStore.DeleteAsync();
        credential = null;
        pendingEmail = null;
    }

    public async ValueTask<HostedAppendResult> AppendOperationsAsync(
        LogbookId logbookId,
        DeviceId deviceId,
        IReadOnlyList<HostedOperationUpload> operations,
        CancellationToken cancellationToken = default)
    {
        var options = await GetConfigAsync(cancellationToken);
        await EnsureHostedLogbookAsync(options, logbookId, cancellationToken);
        var accepted = new List<HostedOperationEnvelope>();
        var throughHostedRevision = 0L;
        foreach (var operation in operations)
        {
            var row = await RpcSingleAsync<AppendOperationRequest, HostedOperationRow>(
                options,
                "append_hosted_operation",
                new AppendOperationRequest(
                    ToHostedUuid(logbookId.Value, "log_"),
                    ToHostedUuid(deviceId.Value, "dev_"),
                    ToHostedUuid(operation.RevisionId.Value, "rev_"),
                    operation.RevisionId.Value,
                    operation.EntryId.Value,
                    null,
                    operation.ParentRevisionIds.Select(id => id.Value).ToArray(),
                    FormatOperationKind(operation),
                    operation.SchemaVersion - 1,
                    operation.PayloadCiphertext,
                    operation.PayloadNonce,
                    operation.PayloadTag,
                    operation.PayloadHash,
                    operation.CreatedAt,
                    new Dictionary<string, string>()),
                cancellationToken);
            var envelope = ToEnvelope(row);
            accepted.Add(envelope);
            throughHostedRevision = Math.Max(throughHostedRevision, envelope.HostedRevision);
        }

        return new HostedAppendResult(accepted, throughHostedRevision);
    }

    public async ValueTask<HostedOperationPage> ReadMissingOperationsAsync(
        LogbookId logbookId,
        long afterHostedRevision,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var options = await GetConfigAsync(cancellationToken);
        await EnsureHostedLogbookAsync(options, logbookId, cancellationToken);
        var rows = await RpcAsync<ReadMissingOperationsRequest, HostedOperationRow[]>(
            options,
            "read_missing_operations",
            new ReadMissingOperationsRequest(ToHostedUuid(logbookId.Value, "log_"), afterHostedRevision, pageSize),
            cancellationToken);
        return new HostedOperationPage(
            rows.Select(ToEnvelope).ToArray(),
            rows.Length == 0 ? afterHostedRevision : rows.Max(row => row.Revision),
            rows.Any(row => row.HasMore));
    }

    public async ValueTask RecordAcknowledgementAsync(
        LogbookId logbookId,
        DeviceId deviceId,
        long throughHostedRevision,
        CancellationToken cancellationToken = default)
    {
        var options = await GetConfigAsync(cancellationToken);
        await EnsureHostedLogbookAsync(options, logbookId, cancellationToken);
        _ = await RpcSingleAsync<RecordAckRequest, JsonElement>(
            options,
            "record_operation_ack",
            new RecordAckRequest(
                ToHostedUuid(logbookId.Value, "log_"),
                ToHostedUuid(deviceId.Value, "dev_"),
                throughHostedRevision,
                throughHostedRevision,
                throughHostedRevision,
                "synced"),
            cancellationToken);
    }

    private async ValueTask<MobileHostedSyncConfig> GetConfigAsync(CancellationToken cancellationToken)
    {
        if (config is not null)
        {
            return config;
        }

        using var response = await http.GetAsync(ConfigPath, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException("Hosted sync is not configured on this device.");
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        config = JsonSerializer.Deserialize<MobileHostedSyncConfig>(json, WebJson)
            ?? throw new InvalidOperationException("Hosted sync configuration is empty.");
        if (!Uri.TryCreate(config.SupabaseUrl, UriKind.Absolute, out _)
            || string.IsNullOrWhiteSpace(config.AnonKey))
        {
            throw new InvalidOperationException("Hosted sync configuration is incomplete.");
        }

        return config;
    }

    private async ValueTask<AcceptedDeviceResponse> AcceptInvitationAsync(
        MobileHostedSyncConfig options,
        CancellationToken cancellationToken)
    {
        var row = await RpcSingleAsync<AcceptInvitationRequest, AcceptedDeviceResponse>(
            options,
            "accept_hosted_invitation",
            new AcceptInvitationRequest(
                options.DisplayName ?? string.Empty,
                "android",
                options.PlatformLabel ?? "Android",
                null,
                null),
            cancellationToken,
            ToInvitationAcceptanceException);
        return row;
    }

    private async ValueTask EnsureHostedLogbookAsync(
        MobileHostedSyncConfig options,
        LogbookId logbookId,
        CancellationToken cancellationToken)
    {
        if (ensuredLogbooks.Contains(logbookId))
        {
            return;
        }

        credential ??= await credentialStore.LoadAsync();
        if (credential is null)
        {
            throw new HostedSignInException(HostedSignInFailureReason.SignedOut, "Hosted sync is signed out.");
        }

        await PostRestAsync(
            options,
            "/rest/v1/logbooks",
            new[]
            {
                new LogbookInsert(
                    ToHostedUuid(logbookId.Value, "log_"),
                    FromHostedAccountId(credential.AccountId),
                    "Electronic Logbook")
            },
            ignoreConflict: true,
            cancellationToken);
        await PostRestAsync(
            options,
            "/rest/v1/logbook_memberships",
            new[]
            {
                new MembershipInsert(
                    ToHostedUuid(logbookId.Value, "log_"),
                    FromHostedAccountId(credential.AccountId),
                    "owner",
                    FromHostedAccountId(credential.AccountId),
                    clock.UtcNow)
            },
            ignoreConflict: true,
            cancellationToken);
        ensuredLogbooks.Add(logbookId);
    }

    private async Task PostRestAsync<TRequest>(
        MobileHostedSyncConfig options,
        string path,
        TRequest payload,
        bool ignoreConflict,
        CancellationToken cancellationToken)
    {
        using var request = NewRequest(options, HttpMethod.Post, path, includeAuthorization: true);
        request.Headers.Add("Prefer", "return=minimal");
        request.Content = JsonContent(payload);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode || (ignoreConflict && response.StatusCode == HttpStatusCode.Conflict))
        {
            return;
        }

        throw ToHostedException(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private async ValueTask<TResponse> RpcAsync<TRequest, TResponse>(
        MobileHostedSyncConfig options,
        string functionName,
        TRequest payload,
        CancellationToken cancellationToken)
    {
        using var request = NewRequest(options, HttpMethod.Post, $"/rest/v1/rpc/{functionName}", includeAuthorization: true);
        request.Content = JsonContent(payload);
        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ToHostedException(response.StatusCode, body);
        }

        return JsonSerializer.Deserialize<TResponse>(body, WebJson)
            ?? throw new HostedLedgerException(HostedLedgerFailureReason.InvalidPayloadEnvelope, $"Hosted RPC '{functionName}' returned no payload.");
    }

    private async ValueTask<TResponse> SendRecoveryEnvelopeAsync<TResponse>(
        RecoveryEnvelopeRequest payload,
        CancellationToken cancellationToken)
    {
        await EnsureFreshRecoveryCredentialAsync(cancellationToken);
        var options = await GetConfigAsync(cancellationToken);
        using var request = NewRequest(options, HttpMethod.Post, RecoveryEnvelopePath, includeAuthorization: true);
        request.Content = JsonContent(payload);
        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = ReadError(body);
            throw new MobileHostedDiagnosticException(
                MobileDiagnosticRedactor.Redact(error?.ErrorCode) ?? "RECOVERY_SERVICE_REJECTED",
                $"Account recovery service request failed with HTTP {(int)response.StatusCode}.",
                response.StatusCode,
                MobileDiagnosticRedactor.Redact(error?.ErrorCode),
                MobileDiagnosticRedactor.Redact(error?.Message));
        }

        try
        {
            var result = JsonSerializer.Deserialize<TResponse>(body, WebJson);
            ValidateRecoveryResponse(result);
            return result!;
        }
        catch (JsonException ex)
        {
            throw new MobileHostedDiagnosticException(
                "RECOVERY_RESPONSE_INVALID",
                "Account recovery returned an invalid response.",
                response.StatusCode,
                innerException: ex);
        }
    }

    private async ValueTask EnsureFreshRecoveryCredentialAsync(CancellationToken cancellationToken)
    {
        credential ??= await credentialStore.LoadAsync();
        if (credential is null)
        {
            throw new MobileHostedDiagnosticException("RECOVERY_AUTH_REQUIRED", "Sign in again to continue.");
        }

        if (credential.AccessTokenExpiresAt > clock.UtcNow.AddMinutes(2))
        {
            return;
        }

        try
        {
            await RefreshAsync(cancellationToken);
        }
        catch (HostedSignInException ex)
        {
            throw new MobileHostedDiagnosticException(
                "RECOVERY_AUTH_REQUIRED",
                "Sign in again to continue.",
                innerException: ex);
        }
    }

    private static void ValidateRecoveryResponse<TResponse>(TResponse? response)
    {
        var valid = response switch
        {
            MobileRecoveryEnvelopeConfiguration value =>
                RequiredRecoveryValues(value.PublicKey, value.Fingerprint, value.Algorithm, value.KeyVersionId),
            MobileRecoverySetupStatus => true,
            MobileRecoveryEnvelopeEnrollmentResult value =>
                value.Enrolled && RequiredRecoveryValues(value.KeyVersionId),
            MobileRecoveryEnvelopeRestoreResult value =>
                RequiredRecoveryValues(value.WrappedKey, value.Algorithm, value.KeyVersionId),
            MobileRecoveryCodeEnrollmentResult value => value.Enrolled,
            MobileRecoveryCodeEnvelopePayload value => RequiredRecoveryValues(
                value.Ciphertext, value.Nonce, value.Salt, value.Algorithm, value.KeyVersionId),
            MobileRecoveryDeviceActivationResult value => value.Activated,
            _ => response is not null
        };
        if (!valid)
        {
            throw new MobileHostedDiagnosticException(
                "RECOVERY_RESPONSE_INVALID",
                "Account recovery returned an incomplete response.");
        }
    }

    private static bool RequiredRecoveryValues(params string?[] values) =>
        values.All(value => !string.IsNullOrWhiteSpace(value));

    private static void ValidateRecoveryValue(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Recovery key material is required.", parameterName);
        }
    }

    private async ValueTask<TResponse> GetRestAsync<TResponse>(
        MobileHostedSyncConfig options,
        string path,
        CancellationToken cancellationToken)
    {
        using var request = NewRequest(options, HttpMethod.Get, path, includeAuthorization: true);
        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ToDiagnosticException("HOSTED_READ_REJECTED", response.StatusCode, body);
        }

        try
        {
            return JsonSerializer.Deserialize<TResponse>(body, WebJson)
                ?? throw new MobileHostedDiagnosticException("HOSTED_READ_EMPTY", "The hosted read returned no payload.");
        }
        catch (JsonException ex)
        {
            throw new MobileHostedDiagnosticException("HOSTED_READ_INVALID", "The hosted read returned an invalid payload.", innerException: ex);
        }
    }

    private async ValueTask<TResponse> RpcSingleAsync<TRequest, TResponse>(
        MobileHostedSyncConfig options,
        string functionName,
        TRequest payload,
        CancellationToken cancellationToken,
        Func<HttpStatusCode, string, Exception>? exceptionFactory = null)
    {
        using var request = NewRequest(options, HttpMethod.Post, $"/rest/v1/rpc/{functionName}", includeAuthorization: true);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.pgrst.object+json"));
        request.Content = JsonContent(payload);
        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw exceptionFactory?.Invoke(response.StatusCode, body)
                ?? ToHostedException(response.StatusCode, body);
        }

        try
        {
            return JsonSerializer.Deserialize<TResponse>(body, WebJson)
                ?? throw new HostedLedgerException(
                    HostedLedgerFailureReason.InvalidPayloadEnvelope,
                    $"Hosted RPC '{functionName}' returned no payload.");
        }
        catch (JsonException)
        {
            throw new HostedLedgerException(
                HostedLedgerFailureReason.InvalidPayloadEnvelope,
                $"Hosted RPC '{functionName}' returned an unexpected response.");
        }
    }

    private HttpRequestMessage NewRequest(
        MobileHostedSyncConfig options,
        HttpMethod method,
        string path,
        bool includeAuthorization)
    {
        var baseUri = new Uri(options.SupabaseUrl, UriKind.Absolute);
        var request = new HttpRequestMessage(method, new Uri(baseUri, path));
        request.Headers.Add("apikey", options.AnonKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (includeAuthorization)
        {
            if (credential is null)
            {
                throw new HostedSignInException(HostedSignInFailureReason.SignedOut, "Hosted sync is signed out.");
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        }

        return request;
    }

    private static StringContent JsonContent<T>(T payload) =>
        new(
            JsonSerializer.Serialize(payload, WebJson),
            Encoding.UTF8,
            new MediaTypeHeaderValue("application/json"));

    private static StringContent CreateVerifyContent(
        MobileHostedSyncConfig options,
        string? email,
        string verificationInput)
    {
        var trimmed = WebUtility.HtmlDecode(verificationInput).Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var link))
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                throw new HostedSignInException(
                    HostedSignInFailureReason.VerificationExpired,
                    "Request a new sign-in code before entering the six-digit code from the email.");
            }

            return JsonContent(new VerifyOtpRequest(email, trimmed, Type: "email"));
        }

        link = ResolveSupabaseVerificationLink(options, link);

        var query = ParseQuery(link.Query);
        var tokenHash = query.GetValueOrDefault("token_hash") ?? query.GetValueOrDefault("token");
        var type = query.GetValueOrDefault("type");
        if (string.IsNullOrWhiteSpace(tokenHash)
            || type is null
            || (type is not "email" && type is not "magiclink"))
        {
            throw InvalidSignInLink();
        }

        return JsonContent(new VerifyTokenHashRequest(tokenHash, type));
    }

    private static Uri ResolveSupabaseVerificationLink(MobileHostedSyncConfig options, Uri link)
    {
        if (!Uri.TryCreate(options.SupabaseUrl, UriKind.Absolute, out var supabaseUrl))
        {
            throw InvalidSignInLink();
        }

        if (IsSupabaseVerificationLink(link, supabaseUrl))
        {
            return link;
        }

        if (!string.Equals(link.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !IsOutlookSafeLinksHost(link.Host))
        {
            throw InvalidSignInLink();
        }

        var wrapperQuery = ParseQuery(link.Query);
        if (!wrapperQuery.TryGetValue("url", out var wrappedUrl)
            || !Uri.TryCreate(wrappedUrl, UriKind.Absolute, out var unwrappedLink)
            || !IsSupabaseVerificationLink(unwrappedLink, supabaseUrl))
        {
            throw InvalidSignInLink();
        }

        return unwrappedLink;
    }

    private static bool IsSupabaseVerificationLink(Uri link, Uri supabaseUrl) =>
        string.Equals(link.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && string.Equals(link.Host, supabaseUrl.Host, StringComparison.OrdinalIgnoreCase)
        && string.Equals(link.AbsolutePath.TrimEnd('/'), "/auth/v1/verify", StringComparison.OrdinalIgnoreCase);

    private static bool IsOutlookSafeLinksHost(string host) =>
        string.Equals(host, "safelinks.protection.outlook.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".safelinks.protection.outlook.com", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = item.Split('=', 2);
            try
            {
                values[Uri.UnescapeDataString(parts[0].Replace('+', ' '))] =
                    parts.Length == 2 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : string.Empty;
            }
            catch (UriFormatException)
            {
                throw InvalidSignInLink();
            }
        }

        return values;
    }

    private static HostedSignInException InvalidSignInLink() =>
        new(
            HostedSignInFailureReason.InvalidVerificationCode,
            "The pasted sign-in link is not an unused Supabase sign-in link for this pilot project.");

    private static HostedSignInException ToSignInException(HttpStatusCode statusCode, string body)
    {
        AuthErrorResponse? error = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                error = JsonSerializer.Deserialize<AuthErrorResponse>(body, WebJson);
            }
            catch (JsonException)
            {
                // Fall through to a generic redacted message.
            }
        }

        if (string.Equals(error?.ErrorCode, "over_email_send_rate_limit", StringComparison.OrdinalIgnoreCase))
        {
            return new HostedSignInException(
                HostedSignInFailureReason.InvalidVerificationCode,
                "Supabase's email limit has been reached. Do not request another email yet; wait for the limit to reset, then send once.");
        }

        if (string.Equals(error?.ErrorCode, "otp_expired", StringComparison.OrdinalIgnoreCase))
        {
            return new HostedSignInException(
                HostedSignInFailureReason.VerificationExpired,
                "The sign-in code is invalid or has expired. Request a new code and try again.");
        }

        var message = $"Hosted sign-in failed with HTTP {(int)statusCode}.";
        var reason = statusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound
            ? HostedSignInFailureReason.InvitationRequired
            : HostedSignInFailureReason.InvalidVerificationCode;
        return new HostedSignInException(reason, message);
    }

    private static Exception ToInvitationAcceptanceException(HttpStatusCode statusCode, string body)
    {
        var error = ReadError(body);
        if (error?.Message?.Contains("workbook migration required", StringComparison.OrdinalIgnoreCase) == true)
        {
            return new HostedSignInException(
                HostedSignInFailureReason.WorkbookMigrationRequired,
                "Finish moving your spreadsheet to FlightLogX on Windows, then sign in on this phone.");
        }

        return ToHostedException(statusCode, body);
    }

    private static HostedLedgerException ToHostedException(HttpStatusCode statusCode, string body)
    {
        var error = ReadError(body);
        var message = error?.Message is { Length: > 0 } safeMessage
            ? $"Hosted RPC failed with HTTP {(int)statusCode}: {MobileDiagnosticRedactor.Redact(safeMessage)}"
            : $"Hosted RPC failed with HTTP {(int)statusCode}.";
        return new HostedLedgerException(HostedLedgerFailureReason.InvalidPayloadEnvelope, message);
    }

    private static MobileHostedDiagnosticException ToDiagnosticException(
        string errorCode,
        HttpStatusCode statusCode,
        string body)
    {
        var error = ReadError(body);
        return new MobileHostedDiagnosticException(
            errorCode,
            $"Hosted request failed with HTTP {(int)statusCode}.",
            statusCode,
            MobileDiagnosticRedactor.Redact(error?.ErrorCode),
            MobileDiagnosticRedactor.Redact(error?.Message));
    }

    private static SupabaseError? ReadError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            var code = root.TryGetProperty("error_code", out var authCode)
                ? authCode.GetString()
                : root.TryGetProperty("code", out var restCode) ? restCode.ToString() : null;
            var message = root.TryGetProperty("msg", out var authMessage)
                ? authMessage.GetString()
                : root.TryGetProperty("message", out var restMessage) ? restMessage.GetString() : null;
            return new SupabaseError(code, message);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ProjectRef(MobileHostedSyncConfig options)
    {
        var uri = new Uri(options.SupabaseUrl, UriKind.Absolute);
        var projectRef = uri.Host.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(projectRef) || !uri.AbsolutePath.Trim('/').Equals(string.Empty, StringComparison.Ordinal))
        {
            throw new MobileHostedDiagnosticException("CONFIG_PROJECT_REF_INVALID", "The packaged Supabase URL is not a project root URL.");
        }

        return projectRef;
    }

    private static JsonElement ReadJwtClaims(string token, string errorCode)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
            {
                throw new FormatException();
            }

            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');
            using var json = JsonDocument.Parse(Convert.FromBase64String(payload));
            return json.RootElement.Clone();
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw new MobileHostedDiagnosticException(errorCode, "The packaged credential is not a valid JWT.", innerException: ex);
        }
    }

    private static string FormatOperationKind(HostedOperationUpload operation)
    {
        var value = operation.RevisionId.Value;
        return value.Contains("delete", StringComparison.OrdinalIgnoreCase)
            ? "deletion"
            : "operation";
    }

    private static HostedOperationEnvelope ToEnvelope(HostedOperationRow row) =>
        new(
            row.Revision,
            new RevisionId(row.PortableRevisionId),
            string.IsNullOrWhiteSpace(row.EntryId) ? new EntryId("ent_unknown") : new EntryId(row.EntryId),
            FromHostedUuid(row.AuthorDeviceId, "dev_"),
            row.ClientCreatedAt,
            row.OperationFormatVersion + 1,
            row.PayloadCiphertext,
            row.PayloadNonce,
            row.PayloadTag,
            row.PayloadHash,
            row.ParentRevisionIds.Select(id => new RevisionId(id)).ToArray());

    private static string ToHostedUuid(string value, string prefix)
    {
        var raw = value.StartsWith(prefix, StringComparison.Ordinal) ? value[prefix.Length..] : value;
        if (Guid.TryParseExact(raw, "N", out var compact) || Guid.TryParse(raw, out compact))
        {
            return compact.ToString("D");
        }

        throw new HostedLedgerException(
            HostedLedgerFailureReason.InvalidIdentifier,
            $"Identifier '{value}' cannot be sent to hosted sync because it is not backed by a UUID.");
    }

    private static string ToRecoveryUuid(string value, string prefix)
    {
        var raw = value.StartsWith(prefix, StringComparison.Ordinal) ? value[prefix.Length..] : value;
        if (Guid.TryParseExact(raw, "N", out var compact) || Guid.TryParse(raw, out compact))
        {
            return compact.ToString("D");
        }

        throw new MobileHostedDiagnosticException(
            "RECOVERY_REQUEST_INVALID",
            "Account recovery identifiers are invalid.");
    }

    private static string FromHostedAccountId(HostedAccountId accountId) =>
        ToHostedUuid(accountId.Value, "acct_");

    private static DeviceId FromHostedUuid(string value, string prefix) =>
        Guid.TryParse(value, out var parsed)
            ? new DeviceId(prefix + parsed.ToString("N"))
            : new DeviceId(value);

    private static LogbookId FromHostedLogbookUuid(string value) =>
        Guid.TryParse(value, out var parsed)
            ? new LogbookId("log_" + parsed.ToString("N"))
            : new LogbookId(value);

    private static Guid ParseRequiredGuid(string? value, string label) =>
        Guid.TryParse(value, out var parsed)
            ? parsed
            : throw new HostedSignInException(HostedSignInFailureReason.InvalidVerificationCode, $"Hosted sign-in returned an invalid {label}.");

    private static string MaskEmail(string email)
    {
        var trimmed = email.Trim();
        var at = trimmed.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0)
        {
            return "***";
        }

        return $"{trimmed[0]}***{trimmed[at..]}";
    }

    private sealed record MobileHostedSyncConfig(
        string SupabaseUrl,
        string AnonKey,
        string? PlatformLabel = null,
        string? DisplayName = null,
        string? GoogleWebClientId = null);

    private sealed record RecoveryEnvelopeRequest(
        string Action,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? LogbookId = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? DeviceId = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? DevicePublicKey = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? DevicePublicKeyFingerprint = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? DevicePublicKeyAlgorithm = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? WrappedPackageKey = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? IngressKeyVersionId = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? PlatformLabel = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? DeviceType = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? RecoveryCiphertext = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? RecoveryNonce = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? RecoverySalt = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? RecoveryAlgorithm = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? RecoveryKeyVersionId = null);

    private sealed record OtpRequest(
        string Email,
        [property: JsonPropertyName("create_user")] bool CreateUser);

    private sealed record VerifyOtpRequest(
        string Email,
        string Token,
        string Type);

    private sealed record VerifyTokenHashRequest(
        [property: JsonPropertyName("token_hash")] string TokenHash,
        string Type);

    private sealed record RefreshRequest(
        [property: JsonPropertyName("refresh_token")] string RefreshToken);

    private sealed record GoogleIdTokenRequest(
        string Provider,
        [property: JsonPropertyName("id_token")] string IdToken,
        string Nonce,
        [property: JsonPropertyName("link_identity")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool LinkIdentity);

    private sealed record AuthSessionResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("user")] AuthUserResponse? User);

    private sealed record AuthErrorResponse(
        [property: JsonPropertyName("error_code")] string? ErrorCode);

    private sealed record SupabaseError(string? ErrorCode, string? Message);

    private sealed record HostedAccountRow(
        [property: JsonPropertyName("account_id")] string AccountId,
        string Status);

    private sealed record HostedDeviceRow(
        [property: JsonPropertyName("device_id")] string DeviceId,
        [property: JsonPropertyName("account_id")] string AccountId,
        string Status);

    private sealed record HostedLogbookMembershipRow(
        [property: JsonPropertyName("logbook_id")] string LogbookId,
        string Role,
        [property: JsonPropertyName("logbooks")] HostedLogbookRow Logbook);

    private sealed record HostedLogbookRow(
        [property: JsonPropertyName("logbook_id")] string LogbookId,
        [property: JsonPropertyName("current_schema_version")] int CurrentSchemaVersion,
        [property: JsonPropertyName("operation_format_version")] int OperationFormatVersion,
        [property: JsonPropertyName("deletion_requested_at")] DateTimeOffset? DeletionRequestedAt,
        [property: JsonPropertyName("deleted_at")] DateTimeOffset? DeletedAt);

    private sealed record AuthUserResponse(
        [property: JsonPropertyName("id")] string? Id);

    private sealed record AcceptInvitationRequest(
        [property: JsonPropertyName("p_display_name")] string DisplayName,
        [property: JsonPropertyName("p_device_type")] string DeviceType,
        [property: JsonPropertyName("p_platform_label")] string PlatformLabel,
        [property: JsonPropertyName("p_public_signing_key")] string? PublicSigningKey,
        [property: JsonPropertyName("p_signing_key_fingerprint")] string? SigningKeyFingerprint);

    private sealed record AcceptedDeviceResponse(
        [property: JsonPropertyName("device_id")] string HostedDeviceId)
    {
        public DeviceId DeviceId => FromHostedUuid(HostedDeviceId, "dev_");
    }

    private sealed record LogbookInsert(
        [property: JsonPropertyName("logbook_id")] string LogbookId,
        [property: JsonPropertyName("owner_account_id")] string OwnerAccountId,
        [property: JsonPropertyName("display_name")] string DisplayName);

    private sealed record MembershipInsert(
        [property: JsonPropertyName("logbook_id")] string LogbookId,
        [property: JsonPropertyName("account_id")] string AccountId,
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("granted_by_account_id")] string GrantedByAccountId,
        [property: JsonPropertyName("accepted_at")] DateTimeOffset AcceptedAt);

    private sealed record AppendOperationRequest(
        [property: JsonPropertyName("p_logbook_id")] string LogbookId,
        [property: JsonPropertyName("p_device_id")] string DeviceId,
        [property: JsonPropertyName("p_operation_id")] string OperationId,
        [property: JsonPropertyName("p_portable_revision_id")] string PortableRevisionId,
        [property: JsonPropertyName("p_entry_id")] string EntryId,
        [property: JsonPropertyName("p_base_revision")] long? BaseRevision,
        [property: JsonPropertyName("p_parent_revision_ids")] IReadOnlyList<string> ParentRevisionIds,
        [property: JsonPropertyName("p_operation_type")] string OperationType,
        [property: JsonPropertyName("p_operation_format_version")] int OperationFormatVersion,
        [property: JsonPropertyName("p_payload_ciphertext")] string PayloadCiphertext,
        [property: JsonPropertyName("p_payload_nonce")] string PayloadNonce,
        [property: JsonPropertyName("p_payload_tag")] string PayloadTag,
        [property: JsonPropertyName("p_payload_hash")] string PayloadHash,
        [property: JsonPropertyName("p_client_created_at")] DateTimeOffset ClientCreatedAt,
        [property: JsonPropertyName("p_redacted_routing_hints")] IReadOnlyDictionary<string, string> RedactedRoutingHints);

    private sealed record ReadMissingOperationsRequest(
        [property: JsonPropertyName("p_logbook_id")] string LogbookId,
        [property: JsonPropertyName("p_after_revision")] long AfterHostedRevision,
        [property: JsonPropertyName("p_page_size")] int PageSize);

    private sealed record RecordAckRequest(
        [property: JsonPropertyName("p_logbook_id")] string LogbookId,
        [property: JsonPropertyName("p_device_id")] string DeviceId,
        [property: JsonPropertyName("p_highest_contiguous_revision")] long HighestContiguousRevision,
        [property: JsonPropertyName("p_last_upload_revision")] long LastUploadRevision,
        [property: JsonPropertyName("p_last_pull_revision")] long LastPullRevision,
        [property: JsonPropertyName("p_local_queue_state")] string LocalQueueState);

    private sealed record HostedOperationRow(
        [property: JsonPropertyName("revision")] long Revision,
        [property: JsonPropertyName("portable_revision_id")] string PortableRevisionId,
        [property: JsonPropertyName("entry_id")] string? EntryId,
        [property: JsonPropertyName("author_device_id")] string AuthorDeviceId,
        [property: JsonPropertyName("client_created_at")] DateTimeOffset ClientCreatedAt,
        [property: JsonPropertyName("operation_format_version")] int OperationFormatVersion,
        [property: JsonPropertyName("payload_ciphertext")] string PayloadCiphertext,
        [property: JsonPropertyName("payload_nonce")] string PayloadNonce,
        [property: JsonPropertyName("payload_tag")] string PayloadTag,
        [property: JsonPropertyName("payload_hash")] string PayloadHash,
        [property: JsonPropertyName("parent_revision_ids")] IReadOnlyList<string> ParentRevisionIds,
        [property: JsonPropertyName("highest_revision")] long HighestRevision,
        [property: JsonPropertyName("has_more")] bool HasMore);
}

public sealed record MobileHostedLogbookMembership(
    LogbookId LogbookId,
    string Role,
    int CurrentSchemaVersion,
    int OperationFormatVersion);
