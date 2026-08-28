using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater;

public sealed class SupabaseWorkbookConnectionClient : IDisposable, IWorkbookMigrationRecoveryClient
{
    private const string RecoveryEnvelopePath = "/functions/v1/recovery-envelope";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private readonly SupabaseHostedSyncConfiguration configuration;
    private readonly HttpClient http;
    private readonly bool ownsHttpClient;
    private string? pendingEmail;
    private SupabaseWorkbookSession? session;

    public SupabaseWorkbookConnectionClient(SupabaseHostedSyncConfiguration configuration)
        : this(configuration, new HttpClient(), ownsHttpClient: true)
    {
    }

    internal SupabaseWorkbookConnectionClient(
        SupabaseHostedSyncConfiguration configuration,
        HttpClient http,
        bool ownsHttpClient = false)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(http);
        this.configuration = configuration;
        this.http = http;
        this.ownsHttpClient = ownsHttpClient;
    }

    public async Task<HostedSignInStart> StartEmailSignInAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        using var request = NewRequest(HttpMethod.Post, "/auth/v1/otp", includeAuthorization: false);
        request.Content = JsonContent(new OtpRequest(email.Trim(), CreateUser: false));
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ToSignInException(response.StatusCode);
        }

        pendingEmail = email.Trim();
        return new HostedSignInStart(
            new HostedAccountId("acct_pending"),
            MaskEmail(pendingEmail),
            DateTimeOffset.UtcNow.AddMinutes(10));
    }

    public async Task<SupabaseWorkbookSession> CompleteEmailSignInAsync(
        string verificationInput,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verificationInput);
        if (string.IsNullOrWhiteSpace(pendingEmail))
        {
            throw new HostedSignInException(
                HostedSignInFailureReason.VerificationExpired,
                "Request a new sign-in code before entering the six-digit code from the email.");
        }

        using var request = NewRequest(HttpMethod.Post, "/auth/v1/verify", includeAuthorization: false);
        request.Content = CreateVerifyContent(configuration.SupabaseUrl, pendingEmail, verificationInput);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ToSignInException(response.StatusCode);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var verified = JsonSerializer.Deserialize<AuthSessionResponse>(body, WebJson)
            ?? throw new HostedSignInException(
                HostedSignInFailureReason.InvalidVerificationCode,
                "Hosted sign-in returned no session.");
        if (!Guid.TryParse(verified.User?.Id, out var accountUuid) ||
            string.IsNullOrWhiteSpace(verified.AccessToken) ||
            string.IsNullOrWhiteSpace(verified.RefreshToken))
        {
            throw new HostedSignInException(
                HostedSignInFailureReason.InvalidVerificationCode,
                "Hosted sign-in returned an invalid account session.");
        }

        var credential = new PortableHostedCredential(
            verified.AccessToken,
            verified.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(verified.ExpiresIn, 60)));
        session = new SupabaseWorkbookSession(
            new HostedAccountId("acct_" + accountUuid.ToString("N")),
            credential,
            MaskEmail(pendingEmail));
        pendingEmail = null;
        return session;
    }

    public async Task<IReadOnlyList<SupabaseWorkbookLogbook>> DiscoverActiveLogbooksAsync(
        CancellationToken cancellationToken = default)
    {
        var current = RequireSession();
        var accountUuid = ToUuid(current.AccountId.Value, "acct_");
        var path = "/rest/v1/logbook_memberships"
            + "?select=logbook_id,role,logbooks!inner(logbook_id,display_name,current_schema_version,operation_format_version,deletion_requested_at,deleted_at)"
            + $"&account_id=eq.{accountUuid}"
            + "&accepted_at=not.is.null&revoked_at=is.null"
            + "&logbooks.deletion_requested_at=is.null&logbooks.deleted_at=is.null"
            + "&order=granted_at.asc";
        using var request = NewRequest(HttpMethod.Get, path, includeAuthorization: true);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("The invited account's logbooks could not be verified.");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var rows = JsonSerializer.Deserialize<MembershipRow[]>(body, WebJson)
            ?? throw new InvalidOperationException("The invited account returned no logbook list.");
        return rows.Select(row => new SupabaseWorkbookLogbook(
            new LogbookId("log_" + Guid.Parse(row.LogbookId).ToString("N")),
            string.IsNullOrWhiteSpace(row.Logbook.DisplayName) ? "Electronic Logbook" : row.Logbook.DisplayName,
            row.Role,
            row.Logbook.CurrentSchemaVersion,
            row.Logbook.OperationFormatVersion)).ToArray();
    }

    public async Task<HostedWorkbookMigration> BeginWorkbookMigrationAsync(
        string sourceFingerprint,
        string logbookDisplayName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(logbookDisplayName);
        return ToWorkbookMigration(await SendMigrationRpcAsync(
            "begin_workbook_migration",
            new BeginWorkbookMigrationRequest(
                sourceFingerprint,
                logbookDisplayName,
                configuration.PlatformLabel,
                null,
                null),
            cancellationToken));
    }

    public async Task<HostedWorkbookMigration> GetWorkbookMigrationStatusAsync(
        CancellationToken cancellationToken = default) =>
        ToWorkbookMigration(await SendMigrationRpcAsync(
            "get_workbook_migration_status",
            new { },
            cancellationToken));

    public async Task<HostedWorkbookMigration> FailWorkbookMigrationAsync(
        WorkbookMigrationId migrationId,
        string failureCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationId.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        return ToWorkbookMigration(await SendMigrationRpcAsync(
            "fail_workbook_migration",
            new FailWorkbookMigrationRequest(
                ToUuid(migrationId.Value, "mig_"),
                failureCode),
            cancellationToken));
    }

    public async Task<HostedWorkbookMigration> CompleteWorkbookMigrationAsync(
        WorkbookMigrationId migrationId,
        int expectedOperationCount,
        string verificationReceiptHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationId.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(verificationReceiptHash);
        var completed = ToWorkbookMigration(await SendMigrationRpcAsync(
            "complete_workbook_migration",
            new CompleteWorkbookMigrationRequest(
                ToUuid(migrationId.Value, "mig_"),
                expectedOperationCount,
                verificationReceiptHash),
            cancellationToken));
        if (completed.Status != HostedWorkbookMigrationStatus.Completed)
        {
            throw new InvalidDataException(
                "The spreadsheet migration did not return a completed hosted state.");
        }

        PortableWorkbookMigrationRecoveryStore.Delete(
            PortableWorkbookMigrationRecoveryStore.CreateTargetName(
                completed.LogbookId,
                completed.DeviceId));
        return completed;
    }

    public async Task<PortableLogbookKey> RestoreWorkbookKeyAsync(
        LogbookId logbookId,
        DeviceId deviceId,
        PortableWorkbookRecoveryKeyPair recoveryKeyPair,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recoveryKeyPair);

        var restored = await SendRecoveryAsync<RecoveryRestoreResponse>(
            new RecoveryRequest(
                "restore",
                ToUuid(logbookId.Value, "log_"),
                ToUuid(deviceId.Value, "dev_"),
                recoveryKeyPair.PublicKey,
                recoveryKeyPair.Fingerprint,
                recoveryKeyPair.Algorithm,
                configuration.PlatformLabel,
                "workbook"),
            cancellationToken);
        if (!string.Equals(restored.Algorithm, PortableWorkbookRecoveryKeyPair.AlgorithmName, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(restored.WrappedKey))
        {
            throw new InvalidDataException("The hosted service returned an invalid workbook recovery envelope.");
        }

        return recoveryKeyPair.DecryptPackageKey(restored.WrappedKey);
    }

    public async Task EnrollWorkbookRecoveryAsync(
        LogbookId logbookId,
        DeviceId deviceId,
        PortableLogbookKey logbookKey,
        PortableWorkbookRecoveryKeyPair recoveryKeyPair,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logbookKey);
        ArgumentNullException.ThrowIfNull(recoveryKeyPair);

        var configuration = await SendRecoveryAsync<RecoveryConfigurationResponse>(
            new RecoveryRequest("configuration"),
            cancellationToken);
        using var ingressKey = ImportVerifiedRecoveryPublicKey(configuration);
        var plaintextKey = logbookKey.ToBytes();
        byte[] wrappedKey;
        try
        {
            wrappedKey = ingressKey.Encrypt(plaintextKey, RSAEncryptionPadding.OaepSHA256);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextKey);
        }

        RecoveryEnrollmentResponse enrolled;
        try
        {
            enrolled = await SendRecoveryAsync<RecoveryEnrollmentResponse>(
                new RecoveryRequest(
                    "enroll",
                    ToUuid(logbookId.Value, "log_"),
                    ToUuid(deviceId.Value, "dev_"),
                    recoveryKeyPair.PublicKey,
                    recoveryKeyPair.Fingerprint,
                    recoveryKeyPair.Algorithm,
                    WrappedPackageKey: Convert.ToBase64String(wrappedKey),
                    IngressKeyVersionId: configuration.KeyVersionId),
                cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappedKey);
        }

        if (!enrolled.Enrolled ||
            !string.Equals(enrolled.KeyVersionId, configuration.KeyVersionId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Workbook account recovery enrollment returned an invalid result.");
        }
    }

    public async Task<PortableWorkbookMigrationRecoveryMaterial> PrepareAndEnrollWorkbookRecoveryAsync(
        HostedWorkbookMigration migration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(migration);
        if (migration.Status != HostedWorkbookMigrationStatus.Pending)
        {
            throw new InvalidOperationException(
                "Temporary recovery keys can be prepared only for a pending spreadsheet migration.");
        }
        if (migration.AccountId != RequireSession().AccountId)
        {
            throw new InvalidOperationException(
                "The spreadsheet migration does not belong to the signed-in account.");
        }

        var material = PortableWorkbookMigrationRecoveryStore.LoadOrCreate(
            migration.LogbookId,
            migration.DeviceId);
        try
        {
            await EnrollWorkbookRecoveryAsync(
                migration.LogbookId,
                migration.DeviceId,
                material.LogbookKey,
                material.RecoveryKeyPair,
                cancellationToken);
            return material;
        }
        catch
        {
            material.Dispose();
            throw;
        }
    }

    public async Task ActivateWorkbookDeviceAsync(
        LogbookId logbookId,
        DeviceId deviceId,
        CancellationToken cancellationToken = default)
    {
        var activated = await SendRecoveryAsync<RecoveryActivationResponse>(
            new RecoveryRequest(
                "activate",
                ToUuid(logbookId.Value, "log_"),
                ToUuid(deviceId.Value, "dev_")),
            cancellationToken);
        if (!activated.Activated)
        {
            throw new InvalidOperationException("The workbook device could not be activated after durable key recovery.");
        }
    }

    public void Dispose()
    {
        if (ownsHttpClient)
        {
            http.Dispose();
        }
    }

    private async Task<TResponse> SendRecoveryAsync<TResponse>(
        RecoveryRequest payload,
        CancellationToken cancellationToken)
    {
        using var request = NewRequest(HttpMethod.Post, RecoveryEnvelopePath, includeAuthorization: true);
        request.Content = JsonContent(payload);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var message = payload.Action switch
            {
                "activate" => "The synced workbook device was not accepted for activation.",
                _ when response.StatusCode == HttpStatusCode.Conflict =>
                    "Workbook account recovery is not ready. Sync the existing app device and try again.",
                _ => "Workbook account recovery was not accepted."
            };
            throw new InvalidOperationException(message);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<TResponse>(body, WebJson)
            ?? throw new InvalidDataException("Workbook account recovery returned an empty response.");
    }

    private async Task<WorkbookMigrationRow> SendMigrationRpcAsync<TRequest>(
        string functionName,
        TRequest payload,
        CancellationToken cancellationToken)
    {
        using var request = NewRequest(
            HttpMethod.Post,
            $"/rest/v1/rpc/{functionName}",
            includeAuthorization: true);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.pgrst.object+json"));
        request.Content = JsonContent(payload);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                "The spreadsheet migration could not update its resumable hosted state.");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<WorkbookMigrationRow>(body, WebJson)
            ?? throw new InvalidDataException("The spreadsheet migration returned no resumable hosted state.");
    }

    private static HostedWorkbookMigration ToWorkbookMigration(WorkbookMigrationRow row) =>
        new(
            new WorkbookMigrationId("mig_" + Guid.Parse(row.MigrationId).ToString("N")),
            new HostedAccountId("acct_" + Guid.Parse(row.AccountId).ToString("N")),
            new LogbookId("log_" + Guid.Parse(row.LogbookId).ToString("N")),
            new DeviceId("dev_" + Guid.Parse(row.DeviceId).ToString("N")),
            row.SourceFingerprint,
            row.Status switch
            {
                "pending" => HostedWorkbookMigrationStatus.Pending,
                "completed" => HostedWorkbookMigrationStatus.Completed,
                "failed" => HostedWorkbookMigrationStatus.Failed,
                _ => throw new InvalidDataException("The spreadsheet migration returned an unknown state.")
            },
            row.AttemptCount,
            row.ExpectedOperationCount,
            row.VerifiedOperationCount,
            row.VerificationReceiptHash,
            row.FailureCode,
            row.StartedAt,
            row.UpdatedAt,
            row.CompletedAt,
            row.FailedAt);

    private static RSA ImportVerifiedRecoveryPublicKey(RecoveryConfigurationResponse configuration)
    {
        if (!string.Equals(
                configuration.Algorithm,
                PortableWorkbookRecoveryKeyPair.AlgorithmName,
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(configuration.PublicKey) ||
            string.IsNullOrWhiteSpace(configuration.Fingerprint) ||
            string.IsNullOrWhiteSpace(configuration.KeyVersionId))
        {
            throw new InvalidDataException("Workbook account recovery configuration is invalid.");
        }

        byte[] publicKey;
        byte[] expectedFingerprint;
        try
        {
            publicKey = Convert.FromBase64String(configuration.PublicKey);
            expectedFingerprint = Convert.FromHexString(configuration.Fingerprint);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("Workbook account recovery configuration is invalid.", ex);
        }

        try
        {
            var actualFingerprint = SHA256.HashData(publicKey);
            if (expectedFingerprint.Length != actualFingerprint.Length ||
                !CryptographicOperations.FixedTimeEquals(expectedFingerprint, actualFingerprint))
            {
                throw new InvalidDataException("Workbook account recovery configuration fingerprint does not match.");
            }

            var rsa = RSA.Create();
            try
            {
                rsa.ImportSubjectPublicKeyInfo(publicKey, out var bytesRead);
                if (bytesRead != publicKey.Length || rsa.KeySize < 2048)
                {
                    throw new InvalidDataException("Workbook account recovery configuration public key is invalid.");
                }

                return rsa;
            }
            catch
            {
                rsa.Dispose();
                throw;
            }
        }
        catch (CryptographicException ex)
        {
            throw new InvalidDataException("Workbook account recovery configuration public key is invalid.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(publicKey);
            CryptographicOperations.ZeroMemory(expectedFingerprint);
        }
    }

    private HttpRequestMessage NewRequest(HttpMethod method, string path, bool includeAuthorization)
    {
        var request = new HttpRequestMessage(method, new Uri(configuration.SupabaseUrl, path));
        request.Headers.Add("apikey", configuration.AnonKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (includeAuthorization)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                RequireSession().Credential.AccessToken);
        }

        return request;
    }

    private SupabaseWorkbookSession RequireSession() =>
        session ?? throw new HostedSignInException(
            HostedSignInFailureReason.SignedOut,
            "Sign in to the invited account before connecting this workbook.");

    private static StringContent JsonContent<T>(T payload) =>
        new(JsonSerializer.Serialize(payload, WebJson), Encoding.UTF8, "application/json");

    private static StringContent CreateVerifyContent(
        Uri supabaseUrl,
        string email,
        string verificationInput)
    {
        var trimmed = WebUtility.HtmlDecode(verificationInput).Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var link))
        {
            return JsonContent(new VerifyOtpRequest(email, trimmed, "email"));
        }

        link = ResolveSupabaseVerificationLink(supabaseUrl, link);
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

    private static Uri ResolveSupabaseVerificationLink(Uri supabaseUrl, Uri link)
    {
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

    private static HostedSignInException ToSignInException(HttpStatusCode statusCode) =>
        new(
            statusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity
                ? HostedSignInFailureReason.InvalidVerificationCode
                : HostedSignInFailureReason.InvitationRequired,
            "Sign-in could not be verified. Check the invited email and request a new code if necessary.");

    private static string ToUuid(string value, string prefix)
    {
        var raw = value.StartsWith(prefix, StringComparison.Ordinal) ? value[prefix.Length..] : value;
        if (Guid.TryParseExact(raw, "N", out var parsed) || Guid.TryParse(raw, out parsed))
        {
            return parsed.ToString("D");
        }

        throw new InvalidDataException("Workbook account connection identifiers are invalid.");
    }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@', StringComparison.Ordinal);
        return at <= 0 ? "***" : $"{email[0]}***{email[at..]}";
    }

    private sealed record OtpRequest(
        string Email,
        [property: JsonPropertyName("create_user")] bool CreateUser);

    private sealed record VerifyOtpRequest(string Email, string Token, string Type);

    private sealed record VerifyTokenHashRequest(
        [property: JsonPropertyName("token_hash")] string TokenHash,
        string Type);

    private sealed record AuthSessionResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        AuthUser? User);

    private sealed record AuthUser(string Id);

    private sealed record MembershipRow(
        [property: JsonPropertyName("logbook_id")] string LogbookId,
        string Role,
        [property: JsonPropertyName("logbooks")] LogbookRow Logbook);

    private sealed record LogbookRow(
        [property: JsonPropertyName("display_name")] string? DisplayName,
        [property: JsonPropertyName("current_schema_version")] int CurrentSchemaVersion,
        [property: JsonPropertyName("operation_format_version")] int OperationFormatVersion);

    private sealed record RecoveryRequest(
        string Action,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LogbookId = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DeviceId = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DevicePublicKey = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DevicePublicKeyFingerprint = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DevicePublicKeyAlgorithm = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PlatformLabel = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DeviceType = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? WrappedPackageKey = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? IngressKeyVersionId = null);

    private sealed record RecoveryConfigurationResponse(
        string PublicKey,
        string Fingerprint,
        string Algorithm,
        string KeyVersionId);

    private sealed record RecoveryEnrollmentResponse(bool Enrolled, string KeyVersionId);

    private sealed record RecoveryRestoreResponse(string WrappedKey, string Algorithm, string KeyVersionId);

    private sealed record RecoveryActivationResponse(bool Activated);

    private sealed record BeginWorkbookMigrationRequest(
        [property: JsonPropertyName("p_source_fingerprint")] string SourceFingerprint,
        [property: JsonPropertyName("p_logbook_display_name")] string LogbookDisplayName,
        [property: JsonPropertyName("p_platform_label")] string PlatformLabel,
        [property: JsonPropertyName("p_public_signing_key")] string? PublicSigningKey,
        [property: JsonPropertyName("p_signing_key_fingerprint")] string? SigningKeyFingerprint);

    private sealed record FailWorkbookMigrationRequest(
        [property: JsonPropertyName("p_migration_id")] string MigrationId,
        [property: JsonPropertyName("p_failure_code")] string FailureCode);

    private sealed record CompleteWorkbookMigrationRequest(
        [property: JsonPropertyName("p_migration_id")] string MigrationId,
        [property: JsonPropertyName("p_expected_operation_count")] int ExpectedOperationCount,
        [property: JsonPropertyName("p_verification_receipt_hash")] string VerificationReceiptHash);

    private sealed record WorkbookMigrationRow(
        [property: JsonPropertyName("migration_id")] string MigrationId,
        [property: JsonPropertyName("account_id")] string AccountId,
        [property: JsonPropertyName("logbook_id")] string LogbookId,
        [property: JsonPropertyName("device_id")] string DeviceId,
        [property: JsonPropertyName("source_fingerprint")] string SourceFingerprint,
        string Status,
        [property: JsonPropertyName("attempt_count")] int AttemptCount,
        [property: JsonPropertyName("expected_operation_count")] int? ExpectedOperationCount,
        [property: JsonPropertyName("verified_operation_count")] int? VerifiedOperationCount,
        [property: JsonPropertyName("verification_receipt_hash")] string? VerificationReceiptHash,
        [property: JsonPropertyName("failure_code")] string? FailureCode,
        [property: JsonPropertyName("started_at")] DateTimeOffset StartedAt,
        [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
        [property: JsonPropertyName("completed_at")] DateTimeOffset? CompletedAt,
        [property: JsonPropertyName("failed_at")] DateTimeOffset? FailedAt);

}

public sealed record SupabaseWorkbookSession(
    HostedAccountId AccountId,
    PortableHostedCredential Credential,
    string AccountDisplay);

public sealed record SupabaseWorkbookLogbook(
    LogbookId LogbookId,
    string DisplayName,
    string Role,
    int CurrentSchemaVersion,
    int OperationFormatVersion);
