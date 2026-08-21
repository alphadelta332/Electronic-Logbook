using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ElectronicLogbook.Portable;

var supabaseUrl = RequiredEnvironment("ELB_REHEARSAL_SUPABASE_URL");
var anonKey = RequiredEnvironment("ELB_REHEARSAL_ANON_KEY");
var serviceRoleKey = RequiredEnvironment("ELB_REHEARSAL_SERVICE_ROLE_KEY");
var evidencePath = Path.GetFullPath(RequiredEnvironment("ELB_REHEARSAL_EVIDENCE_PATH"));
var psqlPath = RequiredEnvironment("ELB_REHEARSAL_PSQL_PATH");
var dbHost = RequiredEnvironment("ELB_REHEARSAL_DB_HOST");
var dbUser = RequiredEnvironment("ELB_REHEARSAL_DB_USER");
var dbPassword = RequiredEnvironment("ELB_REHEARSAL_DB_PASSWORD");
var liveWorkbookOtp = string.Equals(
    Environment.GetEnvironmentVariable("ELB_REHEARSAL_LIVE_WORKBOOK_OTP"),
    "1",
    StringComparison.Ordinal);
var startedAt = DateTimeOffset.UtcNow;
var checks = ExpectedChecks(liveWorkbookOtp).ToDictionary(value => value, _ => false, StringComparer.Ordinal);
var stage = "startup";
var cleanupVerified = false;
Exception? failure = null;
string? failureStage = null;
IReadOnlyDictionary<string, int>? liveWorkbookDeviceStatusCounts = null;

var runSuffix = Guid.NewGuid().ToString("N")[..12];
var email = liveWorkbookOtp
    ? RequiredEnvironment("ELB_REHEARSAL_LIVE_EMAIL")
    : $"hosted-recovery-{runSuffix}@example.invalid";
var accountId = Guid.Empty;
var logbookId = Guid.NewGuid();
var initialDeviceId = Guid.Empty;
var managedDeviceId = Guid.NewGuid();
var codeDeviceId = Guid.NewGuid();
var localReadbackPaths = new List<string>();

using var http = new HttpClient { BaseAddress = new Uri(supabaseUrl.TrimEnd('/') + "/") };
using var packageKey = SensitiveKey.Create();

try
{
    stage = "create-disposable-auth-user";
    accountId = await CreateAuthUserAsync(http, serviceRoleKey, email);
    await InsertRestAsync(http, serviceRoleKey, "accounts", new[]
    {
        new { account_id = accountId, invited_email = email, display_name = "Hosted recovery rehearsal", status = "invited" }
    });
    Pass("disposableAccountInvited");

    stage = "verify-initial-admin-generated-email-otp";
    var initialSession = await GenerateAndVerifyOtpAsync(http, anonKey, serviceRoleKey, email, accountId);
    Pass("adminGeneratedEmailOtpVerified");

    stage = "accept-invitation-and-create-ledger";
    initialDeviceId = await AcceptInvitationAsync(http, anonKey, initialSession.AccessToken);
    await InsertRestAsync(http, initialSession.AccessToken, "logbooks", new[]
    {
        new { logbook_id = logbookId, owner_account_id = accountId, display_name = "Disposable recovery rehearsal" }
    }, anonKey);
    await InsertRestAsync(http, initialSession.AccessToken, "logbook_memberships", new[]
    {
        new
        {
            logbook_id = logbookId,
            account_id = accountId,
            role = "owner",
            granted_by_account_id = accountId,
            accepted_at = DateTimeOffset.UtcNow
        }
    }, anonKey);

    stage = "enroll-managed-and-recovery-code-envelopes";
    using var initialDeviceKey = RSA.Create(2048);
    var configuration = await InvokeRecoveryAsync<RecoveryConfiguration>(
        http, anonKey, initialSession.AccessToken, new { action = "configuration" });
    Expect(configuration.Algorithm == "RSA-OAEP-256", "Recovery service returned an unexpected ingress algorithm.");
    var initialPublicKey = initialDeviceKey.ExportSubjectPublicKeyInfo();
    var managedEnrollment = await InvokeRecoveryAsync<ManagedEnrollment>(
        http,
        anonKey,
        initialSession.AccessToken,
        new
        {
            action = "enroll",
            logbookId,
            deviceId = initialDeviceId,
            devicePublicKey = Convert.ToBase64String(initialPublicKey),
            devicePublicKeyFingerprint = Sha256Hex(initialPublicKey),
            devicePublicKeyAlgorithm = "RSA-OAEP-256",
            wrappedPackageKey = WrapForPublicKey(configuration.PublicKey, packageKey.Bytes),
            ingressKeyVersionId = configuration.KeyVersionId
        });
    Expect(managedEnrollment.Enrolled, "Managed recovery enrollment did not complete.");
    Pass("managedEnvelopeEnrolled");

    var recoveryCode = Base64Url(RandomNumberGenerator.GetBytes(32));
    var recoveryCodeEnvelope = WrapForRecoveryCode(packageKey.Bytes, recoveryCode, KeyName(logbookId));
    var confirmedKey = UnwrapRecoveryCode(recoveryCodeEnvelope, recoveryCode, KeyName(logbookId));
    Expect(confirmedKey.SequenceEqual(packageKey.Bytes), "Recovery-code confirm test did not recover the package key.");
    CryptographicOperations.ZeroMemory(confirmedKey);
    var codeEnrollment = await InvokeRecoveryAsync<CodeEnrollment>(
        http,
        anonKey,
        initialSession.AccessToken,
        new
        {
            action = "enroll-code",
            logbookId,
            deviceId = initialDeviceId,
            recoveryCiphertext = recoveryCodeEnvelope.Ciphertext,
            recoveryNonce = recoveryCodeEnvelope.Nonce,
            recoverySalt = recoveryCodeEnvelope.Salt,
            recoveryAlgorithm = recoveryCodeEnvelope.Algorithm,
            recoveryKeyVersionId = recoveryCodeEnvelope.KeyVersionId
        });
    Expect(codeEnrollment.Enrolled, "Recovery-code enrollment did not complete.");
    Pass("recoveryCodeConfirmTestedAndEnrolled");

    stage = "append-non-empty-encrypted-ledger";
    var portableLogbookId = new LogbookId("log_" + logbookId.ToString("N"));
    var portableInitialDeviceId = new DeviceId("dev_" + initialDeviceId.ToString("N"));
    var entryId = new EntryId("ent_" + Guid.NewGuid().ToString("N"));
    var revisionGuid = Guid.NewGuid();
    var revisionId = new RevisionId("rev_" + revisionGuid.ToString("N"));
    var operation = PortableLogbookOperationV2.Create(
        portableLogbookId,
        entryId,
        revisionId,
        portableInitialDeviceId,
        DateTimeOffset.UtcNow,
        PortableLogbookWorkbookEntry.Empty with
        {
            Year = 2026,
            Month = 8,
            Day = 11,
            Type = "C172",
            Reg = "VH-DSP",
            From = "YSBK",
            To = "YSCN",
            Pic = "Disposable pilot",
            SeCommandDay = 1.2m
        });
    var upload = HostedOperationCipher.Encrypt(operation, PortableLogbookKey.FromBytes(packageKey.Bytes));
    var appended = await RpcAsync<JsonElement>(
        http,
        anonKey,
        initialSession.AccessToken,
        "append_hosted_operation",
        new
        {
            p_logbook_id = logbookId,
            p_device_id = initialDeviceId,
            p_operation_id = revisionGuid,
            p_portable_revision_id = revisionId.Value,
            p_entry_id = entryId.Value,
            p_base_revision = (long?)null,
            p_parent_revision_ids = Array.Empty<string>(),
            p_operation_type = "operation",
            p_operation_format_version = 1,
            p_payload_ciphertext = upload.PayloadCiphertext,
            p_payload_nonce = upload.PayloadNonce,
            p_payload_tag = upload.PayloadTag,
            p_payload_hash = upload.PayloadHash,
            p_client_created_at = operation.CreatedAt,
            p_redacted_routing_hints = new { }
        });
    var hostedRevision = appended.GetProperty("revision").GetInt64();
    Expect(hostedRevision > 0, "The hosted ledger did not assign a positive revision.");
    Pass("nonEmptyEncryptedLedgerAppended");

    if (liveWorkbookOtp)
    {
        stage = "await-live-workbook-otp";
        Console.WriteLine("LIVE_WORKBOOK_OTP_READY");
        Console.WriteLine("Complete the workbook email-code connection, then press Enter here to verify and clean up.");
        _ = await Console.In.ReadLineAsync();

        stage = "verify-live-workbook-device";
        JsonElement[] workbookDevices = [];
        for (var attempt = 0; attempt < 10; attempt++)
        {
            workbookDevices = await GetRestAsync<JsonElement[]>(
                http,
                serviceRoleKey,
                $"devices?select=device_id,status&account_id=eq.{accountId}&device_type=eq.workbook");
            if (workbookDevices.Count(device =>
                    string.Equals(device.GetProperty("status").GetString(), "active", StringComparison.Ordinal)) == 1)
            {
                break;
            }

            await Task.Delay(500);
        }

        liveWorkbookDeviceStatusCounts = workbookDevices
            .GroupBy(device => device.GetProperty("status").GetString() ?? "unknown", StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var activeWorkbookDevices = workbookDevices
            .Where(device => string.Equals(
                device.GetProperty("status").GetString(),
                "active",
                StringComparison.Ordinal))
            .ToArray();
        Expect(
            activeWorkbookDevices.Length == 1,
            $"Live OTP pairing did not leave exactly one active workbook device " +
            $"(active={activeWorkbookDevices.Length}, total={workbookDevices.Length}).");
        var workbookDeviceId = activeWorkbookDevices[0].GetProperty("device_id").GetGuid();
        Pass("liveEmailOtpWorkbookDeviceActivated");

        stage = "verify-live-workbook-acknowledgement";
        Expect(
            await ReadAcknowledgementAsync(http, serviceRoleKey, logbookId, workbookDeviceId) >= hostedRevision,
            "Live OTP pairing did not durably acknowledge the hosted revision.");
        Pass("liveWorkbookAcknowledgedHostedRevision");
    }
    else
    {
        stage = "managed-replacement-authentication";
        var managedSession = await GenerateAndVerifyOtpAsync(http, anonKey, serviceRoleKey, email, accountId);
        Pass("managedReplacementEmailOtpVerified");

    stage = "managed-replacement-restore";
    using var managedDeviceKey = RSA.Create(2048);
    var managedPublic = managedDeviceKey.ExportSubjectPublicKeyInfo();
    var managedRestore = await InvokeRecoveryAsync<ManagedRestore>(
        http,
        anonKey,
        managedSession.AccessToken,
        new
        {
            action = "restore",
            logbookId,
            deviceId = managedDeviceId,
            deviceType = "android",
            platformLabel = "Disposable managed replacement",
            devicePublicKey = Convert.ToBase64String(managedPublic),
            devicePublicKeyFingerprint = Sha256Hex(managedPublic),
            devicePublicKeyAlgorithm = "RSA-OAEP-256"
        });
    var managedPackageKey = managedDeviceKey.Decrypt(
        Convert.FromBase64String(managedRestore.WrappedKey), RSAEncryptionPadding.OaepSHA256);
    Expect(managedPackageKey.SequenceEqual(packageKey.Bytes), "Managed replacement recovered a different package key.");
    Pass("managedReplacementPackageKeyRecovered");
    var managedDocument = await PullDecryptMaterializeAsync(
        http, anonKey, managedSession.AccessToken, logbookId, managedPackageKey, hostedRevision);
    Pass("managedReplacementLedgerMaterialized");
    var managedReadback = await PersistAndReadBackAsync(managedDocument, localReadbackPaths);
    Expect(PortableLogbookJson.SerializeV2(managedReadback) == PortableLogbookJson.SerializeV2(managedDocument),
        "Managed replacement local read-back differed from the saved document.");
    Pass("managedReplacementDurableLocalReadBack");
    await AcknowledgeAsync(http, anonKey, managedSession.AccessToken, logbookId, managedDeviceId, hostedRevision);
    Expect(await ReadAcknowledgementAsync(http, serviceRoleKey, logbookId, managedDeviceId) == hostedRevision,
        "Managed replacement acknowledgement was not durable.");
    Pass("managedReplacementAcknowledged");
    var managedActivation = await InvokeRecoveryAsync<ActivationResponse>(
        http, anonKey, managedSession.AccessToken, new { action = "activate", logbookId, deviceId = managedDeviceId });
    Expect(managedActivation.Activated, "Managed replacement did not activate.");
    Expect(await ReadDeviceStatusAsync(http, serviceRoleKey, managedDeviceId) == "active",
        "Managed replacement activation was not durable.");
    Pass("managedReplacementActivated");
    CryptographicOperations.ZeroMemory(managedPackageKey);

    stage = "recovery-code-replacement-authentication";
    var codeSession = await GenerateAndVerifyOtpAsync(http, anonKey, serviceRoleKey, email, accountId);
    Pass("recoveryCodeReplacementEmailOtpVerified");

    stage = "recovery-code-replacement-restore";
    using var codeDeviceKey = RSA.Create(2048);
    var codePublic = codeDeviceKey.ExportSubjectPublicKeyInfo();
    var restoredCodeEnvelope = await InvokeRecoveryAsync<RecoveryCodeEnvelope>(
        http,
        anonKey,
        codeSession.AccessToken,
        new
        {
            action = "restore-code",
            logbookId,
            deviceId = codeDeviceId,
            deviceType = "android",
            platformLabel = "Disposable recovery-code replacement",
            devicePublicKey = Convert.ToBase64String(codePublic),
            devicePublicKeyFingerprint = Sha256Hex(codePublic),
            devicePublicKeyAlgorithm = "RSA-OAEP-256"
        });
    var codePackageKey = UnwrapRecoveryCode(restoredCodeEnvelope, recoveryCode, KeyName(logbookId));
    Expect(codePackageKey.SequenceEqual(packageKey.Bytes), "Recovery-code fallback recovered a different package key.");
    Pass("recoveryCodeFallbackPackageKeyRecovered");
    var codeDocument = await PullDecryptMaterializeAsync(
        http, anonKey, codeSession.AccessToken, logbookId, codePackageKey, hostedRevision);
    Pass("recoveryCodeFallbackLedgerMaterialized");
    var codeReadback = await PersistAndReadBackAsync(codeDocument, localReadbackPaths);
    Expect(PortableLogbookJson.SerializeV2(codeReadback) == PortableLogbookJson.SerializeV2(codeDocument),
        "Recovery-code replacement local read-back differed from the saved document.");
    Pass("recoveryCodeFallbackDurableLocalReadBack");
    await AcknowledgeAsync(http, anonKey, codeSession.AccessToken, logbookId, codeDeviceId, hostedRevision);
    Expect(await ReadAcknowledgementAsync(http, serviceRoleKey, logbookId, codeDeviceId) == hostedRevision,
        "Recovery-code replacement acknowledgement was not durable.");
    Pass("recoveryCodeFallbackAcknowledged");
    var codeActivation = await InvokeRecoveryAsync<ActivationResponse>(
        http, anonKey, codeSession.AccessToken, new { action = "activate", logbookId, deviceId = codeDeviceId });
    Expect(codeActivation.Activated, "Recovery-code replacement did not activate.");
    Expect(await ReadDeviceStatusAsync(http, serviceRoleKey, codeDeviceId) == "active",
        "Recovery-code replacement activation was not durable.");
    Pass("recoveryCodeFallbackActivated");
    CryptographicOperations.ZeroMemory(codePackageKey);
        recoveryCode = string.Empty;
    }
}
catch (Exception ex)
{
    failure = ex;
    failureStage = stage;
}
finally
{
    foreach (var path in localReadbackPaths)
    {
        TryDelete(path);
    }

    try
    {
        stage = "cleanup-disposable-identity";
        if (accountId != Guid.Empty)
        {
            await CleanupAsync(
                http, serviceRoleKey, accountId, logbookId, psqlPath, dbHost, dbUser, dbPassword);
            cleanupVerified = await VerifyCleanupAsync(http, serviceRoleKey, accountId, logbookId);
            if (!cleanupVerified)
            {
                throw new InvalidOperationException("Disposable hosted rows or Auth identity remained after cleanup.");
            }
            Pass("disposableIdentityCleaned");
        }
    }
    catch (Exception cleanupFailure)
    {
        failureStage ??= stage;
        failure = failure is null
            ? cleanupFailure
            : new AggregateException(failure, cleanupFailure);
    }

    var evidence = new
    {
        schemaVersion = 1,
        rehearsal = liveWorkbookOtp
            ? "hosted-development-live-workbook-otp"
            : "hosted-development-disposable-recovery",
        startedAtUtc = startedAt,
        completedAtUtc = DateTimeOffset.UtcNow,
        passed = failure is null && cleanupVerified && checks.Values.All(value => value),
        cleanupVerified,
        checks,
        liveWorkbookDeviceStatusCounts,
        failureStage,
        failure = failure is null ? null : SanitizeFailure(failure)
    };
    Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
    var evidenceJson = JsonSerializer.Serialize(evidence, JsonOptions(indented: true));
    EnsureEvidenceRedacted(evidenceJson, email, accountId, logbookId, initialDeviceId, managedDeviceId, codeDeviceId,
        anonKey, serviceRoleKey, dbPassword, packageKey.Bytes);
    await File.WriteAllTextAsync(evidencePath, evidenceJson + Environment.NewLine, Encoding.UTF8);
}

if (failure is not null)
{
    Console.Error.WriteLine($"Hosted recovery rehearsal failed at {failureStage}: {SanitizeFailure(failure)}");
    return 1;
}

Console.WriteLine(liveWorkbookOtp
    ? "Hosted live workbook OTP rehearsal passed."
    : "Hosted recovery rehearsal passed.");
Console.WriteLine($"- {checks.Count} redacted checks passed");
Console.WriteLine("- disposable Auth identity and hosted rows were removed");
return 0;

void Pass(string name) => checks[name] = true;

static IEnumerable<string> ExpectedChecks(bool liveWorkbookOtp) => liveWorkbookOtp
    ?
    [
        "disposableAccountInvited",
        "adminGeneratedEmailOtpVerified",
        "managedEnvelopeEnrolled",
        "recoveryCodeConfirmTestedAndEnrolled",
        "nonEmptyEncryptedLedgerAppended",
        "liveEmailOtpWorkbookDeviceActivated",
        "liveWorkbookAcknowledgedHostedRevision",
        "disposableIdentityCleaned"
    ]
    :
    [
        "disposableAccountInvited",
        "adminGeneratedEmailOtpVerified",
        "managedEnvelopeEnrolled",
        "recoveryCodeConfirmTestedAndEnrolled",
        "nonEmptyEncryptedLedgerAppended",
        "managedReplacementEmailOtpVerified",
        "managedReplacementPackageKeyRecovered",
        "managedReplacementLedgerMaterialized",
        "managedReplacementDurableLocalReadBack",
        "managedReplacementAcknowledged",
        "managedReplacementActivated",
        "recoveryCodeReplacementEmailOtpVerified",
        "recoveryCodeFallbackPackageKeyRecovered",
        "recoveryCodeFallbackLedgerMaterialized",
        "recoveryCodeFallbackDurableLocalReadBack",
        "recoveryCodeFallbackAcknowledged",
        "recoveryCodeFallbackActivated",
        "disposableIdentityCleaned"
    ];

static string RequiredEnvironment(string name) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value.Trim()
        : throw new InvalidOperationException($"Required environment variable {name} is missing.");

static async Task<Guid> CreateAuthUserAsync(HttpClient http, string serviceRoleKey, string email)
{
    var result = await SendJsonAsync<JsonElement>(
        http,
        HttpMethod.Post,
        "auth/v1/admin/users",
        new { email, email_confirm = true },
        serviceRoleKey,
        serviceRoleKey);
    return Guid.Parse(result.GetProperty("id").GetString()!);
}

static async Task<AuthSession> GenerateAndVerifyOtpAsync(
    HttpClient http,
    string anonKey,
    string serviceRoleKey,
    string email,
    Guid expectedAccountId)
{
    var generated = await SendJsonAsync<JsonElement>(
        http,
        HttpMethod.Post,
        "auth/v1/admin/generate_link",
        new { type = "magiclink", email },
        serviceRoleKey,
        serviceRoleKey);
    var otp = generated.GetProperty("email_otp").GetString();
    Expect(!string.IsNullOrWhiteSpace(otp), "Supabase did not generate an email OTP.");
    var session = await SendJsonAsync<AuthSession>(
        http,
        HttpMethod.Post,
        "auth/v1/verify",
        new { email, token = otp, type = "email" },
        anonKey);
    Expect(session.User?.Id == expectedAccountId, "Verified OTP session belonged to a different Auth user.");
    return session;
}

static async Task<Guid> AcceptInvitationAsync(HttpClient http, string anonKey, string accessToken)
{
    var result = await RpcAsync<JsonElement>(
        http,
        anonKey,
        accessToken,
        "accept_hosted_invitation",
        new
        {
            p_display_name = "Hosted recovery rehearsal",
            p_device_type = "android",
            p_platform_label = "Disposable initial device",
            p_public_signing_key = (string?)null,
            p_signing_key_fingerprint = (string?)null
        });
    return Guid.Parse(result.GetProperty("device_id").GetString()!);
}

static async Task InsertRestAsync<T>(
    HttpClient http,
    string bearerToken,
    string table,
    T payload,
    string? apiKey = null)
{
    ArgumentNullException.ThrowIfNull(payload);
    using var request = NewRequest(HttpMethod.Post, "rest/v1/" + table, apiKey ?? bearerToken, bearerToken);
    request.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
    request.Content = JsonContent(payload);
    using var response = await http.SendAsync(request);
    if (!response.IsSuccessStatusCode)
    {
        throw await HttpFailureAsync(response, $"insert {table}");
    }
}

static async Task<T> RpcAsync<T>(
    HttpClient http,
    string apiKey,
    string bearerToken,
    string function,
    object payload) =>
    await SendJsonAsync<T>(http, HttpMethod.Post, "rest/v1/rpc/" + function, payload, apiKey, bearerToken);

static async Task<T> InvokeRecoveryAsync<T>(
    HttpClient http,
    string anonKey,
    string accessToken,
    object payload) =>
    await SendJsonAsync<T>(http, HttpMethod.Post, "functions/v1/recovery-envelope", payload, anonKey, accessToken);

static async Task<T> SendJsonAsync<T>(
    HttpClient http,
    HttpMethod method,
    string path,
    object payload,
    string apiKey,
    string? bearerToken = null)
{
    using var request = NewRequest(method, path, apiKey, bearerToken);
    request.Content = JsonContent(payload);
    using var response = await http.SendAsync(request);
    var body = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
    {
        throw HttpFailure(response.StatusCode, body, path);
    }
    return JsonSerializer.Deserialize<T>(body, JsonOptions())
        ?? throw new InvalidOperationException($"{path} returned no JSON payload.");
}

static HttpRequestMessage NewRequest(
    HttpMethod method,
    string path,
    string apiKey,
    string? bearerToken)
{
    var request = new HttpRequestMessage(method, path);
    request.Headers.TryAddWithoutValidation("apikey", apiKey);
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    if (!string.IsNullOrWhiteSpace(bearerToken))
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
    }
    return request;
}

static StringContent JsonContent(object value) =>
    new(JsonSerializer.Serialize(value, JsonOptions()), Encoding.UTF8, "application/json");

static async Task<PortableLogbookDocumentV2> PullDecryptMaterializeAsync(
    HttpClient http,
    string anonKey,
    string accessToken,
    Guid logbookId,
    byte[] packageKey,
    long expectedHighestRevision)
{
    var rows = await RpcAsync<JsonElement[]>(
        http,
        anonKey,
        accessToken,
        "read_missing_operations",
        new { p_logbook_id = logbookId, p_after_revision = 0, p_page_size = 200 });
    Expect(rows.Length == 1, "Replacement pull did not return exactly one hosted operation.");
    var row = rows[0];
    Expect(row.GetProperty("highest_revision").GetInt64() == expectedHighestRevision,
        "Replacement pull returned an unexpected hosted high-water mark.");
    var envelope = new HostedOperationEnvelope(
        row.GetProperty("revision").GetInt64(),
        new RevisionId(row.GetProperty("portable_revision_id").GetString()!),
        new EntryId(row.GetProperty("entry_id").GetString()!),
        new DeviceId("dev_" + Guid.Parse(row.GetProperty("author_device_id").GetString()!).ToString("N")),
        row.GetProperty("client_created_at").GetDateTimeOffset(),
        row.GetProperty("operation_format_version").GetInt32() + 1,
        row.GetProperty("payload_ciphertext").GetString()!,
        row.GetProperty("payload_nonce").GetString()!,
        row.GetProperty("payload_tag").GetString()!,
        row.GetProperty("payload_hash").GetString()!,
        row.GetProperty("parent_revision_ids").EnumerateArray()
            .Select(value => new RevisionId(value.GetString()!)).ToArray());
    var operation = HostedOperationCipher.Decrypt(envelope, PortableLogbookKey.FromBytes(packageKey));
    var document = PortableLogbookDocumentV2.CreateAustraliaFirst(
        new LogbookId("log_" + logbookId.ToString("N")),
        [],
        PortableLogbookCurrencyOverrideDates.Empty,
        [operation]);
    var materialized = PortableLogbookWorkbookProjection.MergeV2(document.Operations);
    Expect(materialized.OperationCount == 1 && materialized.Entries.Count == 1 && materialized.Conflicts.Count == 0,
        "Replacement ledger did not materialize exactly one conflict-free entry.");
    var entry = materialized.Entries.Values.Single().Entry;
    Expect(entry is not null && entry.Reg == "VH-DSP" && entry.SeCommandDay == 1.2m,
        "Materialized entry did not contain the disposable flight values.");
    return document;
}

static async Task<PortableLogbookDocumentV2> PersistAndReadBackAsync(
    PortableLogbookDocumentV2 document,
    List<string> paths)
{
    var path = Path.Combine(Path.GetTempPath(), "elb-hosted-recovery-" + Guid.NewGuid().ToString("N") + ".json");
    paths.Add(path);
    await File.WriteAllTextAsync(path, PortableLogbookJson.SerializeV2(document), Encoding.UTF8);
    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
    var saved = await reader.ReadToEndAsync();
    return PortableLogbookJson.DeserializeV2(saved)
        ?? throw new InvalidOperationException("Durable local read-back returned no document.");
}

static async Task AcknowledgeAsync(
    HttpClient http,
    string anonKey,
    string accessToken,
    Guid logbookId,
    Guid deviceId,
    long revision)
{
    _ = await RpcAsync<JsonElement>(
        http,
        anonKey,
        accessToken,
        "record_operation_ack",
        new
        {
            p_logbook_id = logbookId,
            p_device_id = deviceId,
            p_highest_contiguous_revision = revision,
            p_last_upload_revision = revision,
            p_last_pull_revision = revision,
            p_local_queue_state = "synced"
        });
}

static async Task<long> ReadAcknowledgementAsync(
    HttpClient http,
    string serviceRoleKey,
    Guid logbookId,
    Guid deviceId)
{
    var rows = await GetRestAsync<JsonElement[]>(http, serviceRoleKey,
        $"operation_acks?select=highest_contiguous_revision&logbook_id=eq.{logbookId}&device_id=eq.{deviceId}");
    return rows.Length == 1 ? rows[0].GetProperty("highest_contiguous_revision").GetInt64() : -1;
}

static async Task<string?> ReadDeviceStatusAsync(HttpClient http, string serviceRoleKey, Guid deviceId)
{
    var rows = await GetRestAsync<JsonElement[]>(http, serviceRoleKey,
        $"devices?select=status&device_id=eq.{deviceId}");
    return rows.Length == 1 ? rows[0].GetProperty("status").GetString() : null;
}

static async Task<T> GetRestAsync<T>(HttpClient http, string serviceRoleKey, string query)
{
    using var request = NewRequest(HttpMethod.Get, "rest/v1/" + query, serviceRoleKey, serviceRoleKey);
    using var response = await http.SendAsync(request);
    var body = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
    {
        throw HttpFailure(response.StatusCode, body, "read hosted verification state");
    }
    return JsonSerializer.Deserialize<T>(body, JsonOptions())
        ?? throw new InvalidOperationException("Hosted verification read returned no JSON payload.");
}

static async Task CleanupAsync(
    HttpClient http,
    string serviceRoleKey,
    Guid accountId,
    Guid logbookId,
    string psqlPath,
    string dbHost,
    string dbUser,
    string dbPassword)
{
    await DeleteRestAsync(http, serviceRoleKey, $"security_events?account_id=eq.{accountId}");
    await DeleteRestAsync(http, serviceRoleKey, $"key_envelopes?logbook_id=eq.{logbookId}");
    await DeleteRestAsync(http, serviceRoleKey, $"operation_acks?logbook_id=eq.{logbookId}");
    await DeleteAppendOnlyOperationsAsync(psqlPath, dbHost, dbUser, dbPassword, logbookId);
    await DeleteRestAsync(http, serviceRoleKey, $"logbook_memberships?logbook_id=eq.{logbookId}");
    await DeleteRestAsync(http, serviceRoleKey, $"devices?account_id=eq.{accountId}");
    await DeleteRestAsync(http, serviceRoleKey, $"logbooks?logbook_id=eq.{logbookId}");
    await DeleteRestAsync(http, serviceRoleKey, $"accounts?account_id=eq.{accountId}");
    using var request = NewRequest(HttpMethod.Delete, "auth/v1/admin/users/" + accountId, serviceRoleKey, serviceRoleKey);
    using var response = await http.SendAsync(request);
    if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
    {
        throw await HttpFailureAsync(response, "delete disposable Auth user");
    }
}

static async Task DeleteAppendOnlyOperationsAsync(
    string psqlPath,
    string dbHost,
    string dbUser,
    string dbPassword,
    Guid logbookId)
{
    using var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = psqlPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        }
    };
    process.StartInfo.ArgumentList.Add("-h");
    process.StartInfo.ArgumentList.Add(dbHost);
    process.StartInfo.ArgumentList.Add("-p");
    process.StartInfo.ArgumentList.Add("5432");
    process.StartInfo.ArgumentList.Add("-U");
    process.StartInfo.ArgumentList.Add(dbUser);
    process.StartInfo.ArgumentList.Add("-d");
    process.StartInfo.ArgumentList.Add("postgres");
    process.StartInfo.ArgumentList.Add("-v");
    process.StartInfo.ArgumentList.Add("ON_ERROR_STOP=1");
    process.StartInfo.ArgumentList.Add("-q");
    process.StartInfo.Environment["PGPASSWORD"] = dbPassword;
    process.Start();
    await process.StandardInput.WriteLineAsync("begin;");
    await process.StandardInput.WriteLineAsync("set local session_replication_role = replica;");
    await process.StandardInput.WriteLineAsync($"delete from public.operations where logbook_id = '{logbookId}';");
    await process.StandardInput.WriteLineAsync("commit;");
    process.StandardInput.Close();
    _ = await process.StandardOutput.ReadToEndAsync();
    _ = await process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException(
            $"Trigger-safe disposable operation cleanup failed with psql exit code {process.ExitCode}; output redacted.");
    }
}

static async Task DeleteRestAsync(HttpClient http, string serviceRoleKey, string query)
{
    using var request = NewRequest(HttpMethod.Delete, "rest/v1/" + query, serviceRoleKey, serviceRoleKey);
    request.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
    using var response = await http.SendAsync(request);
    if (!response.IsSuccessStatusCode)
    {
        throw await HttpFailureAsync(response, "delete disposable hosted rows");
    }
}

static async Task<bool> VerifyCleanupAsync(HttpClient http, string serviceRoleKey, Guid accountId, Guid logbookId)
{
    foreach (var query in new[]
    {
        $"accounts?select=account_id&account_id=eq.{accountId}",
        $"logbooks?select=logbook_id&logbook_id=eq.{logbookId}",
        $"devices?select=device_id&account_id=eq.{accountId}",
        $"operations?select=operation_row_id&logbook_id=eq.{logbookId}",
        $"key_envelopes?select=key_envelope_id&logbook_id=eq.{logbookId}",
        $"security_events?select=security_event_id&account_id=eq.{accountId}"
    })
    {
        var rows = await GetRestAsync<JsonElement[]>(http, serviceRoleKey, query);
        if (rows.Length != 0) return false;
    }
    using var request = NewRequest(HttpMethod.Get, "auth/v1/admin/users/" + accountId, serviceRoleKey, serviceRoleKey);
    using var response = await http.SendAsync(request);
    return response.StatusCode == HttpStatusCode.NotFound;
}

static RecoveryCodeEnvelope WrapForRecoveryCode(byte[] packageKey, string recoveryCode, string keyName)
{
    var salt = RandomNumberGenerator.GetBytes(16);
    var nonce = RandomNumberGenerator.GetBytes(12);
    var derivedKey = Rfc2898DeriveBytes.Pbkdf2(
        recoveryCode.Replace(" ", string.Empty, StringComparison.Ordinal),
        salt,
        600_000,
        HashAlgorithmName.SHA256,
        32);
    var ciphertext = new byte[packageKey.Length];
    var tag = new byte[16];
    try
    {
        using var aes = new AesGcm(derivedKey, 16);
        aes.Encrypt(nonce, packageKey, ciphertext, tag, Encoding.UTF8.GetBytes(keyName));
        return new RecoveryCodeEnvelope(
            Convert.ToBase64String(ciphertext.Concat(tag).ToArray()),
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(salt),
            "PBKDF2-SHA256-600000+A256GCM",
            "recovery-code-v1");
    }
    finally
    {
        CryptographicOperations.ZeroMemory(derivedKey);
        CryptographicOperations.ZeroMemory(ciphertext);
        CryptographicOperations.ZeroMemory(tag);
    }
}

static byte[] UnwrapRecoveryCode(RecoveryCodeEnvelope envelope, string recoveryCode, string keyName)
{
    Expect(envelope.Algorithm == "PBKDF2-SHA256-600000+A256GCM" && envelope.KeyVersionId == "recovery-code-v1",
        "Recovery-code envelope format was unexpected.");
    var combined = Convert.FromBase64String(envelope.Ciphertext);
    var nonce = Convert.FromBase64String(envelope.Nonce);
    var salt = Convert.FromBase64String(envelope.Salt);
    Expect(combined.Length == 48 && nonce.Length == 12 && salt.Length == 16,
        "Recovery-code envelope lengths were invalid.");
    var derivedKey = Rfc2898DeriveBytes.Pbkdf2(
        recoveryCode.Replace(" ", string.Empty, StringComparison.Ordinal),
        salt,
        600_000,
        HashAlgorithmName.SHA256,
        32);
    var plaintext = new byte[32];
    try
    {
        using var aes = new AesGcm(derivedKey, 16);
        aes.Decrypt(
            nonce,
            combined.AsSpan(0, 32),
            combined.AsSpan(32, 16),
            plaintext,
            Encoding.UTF8.GetBytes(keyName));
        return plaintext;
    }
    finally
    {
        CryptographicOperations.ZeroMemory(derivedKey);
        CryptographicOperations.ZeroMemory(combined);
    }
}

static string WrapForPublicKey(string publicKeyBase64, byte[] packageKey)
{
    using var rsa = RSA.Create();
    rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
    return Convert.ToBase64String(rsa.Encrypt(packageKey, RSAEncryptionPadding.OaepSHA256));
}

static string KeyName(Guid logbookId) => "package-key:log_" + logbookId.ToString("N");
static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
static string Sha256Hex(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

static void Expect(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static async Task<Exception> HttpFailureAsync(HttpResponseMessage response, string operation) =>
    HttpFailure(response.StatusCode, await response.Content.ReadAsStringAsync(), operation);

static Exception HttpFailure(HttpStatusCode status, string body, string operation)
{
    var code = "body-redacted";
    try
    {
        using var json = JsonDocument.Parse(body);
        foreach (var name in new[] { "code", "error_code", "error" })
        {
            if (json.RootElement.TryGetProperty(name, out var value))
            {
                code = value.ToString();
                break;
            }
        }
    }
    catch (JsonException)
    {
        // Keep the response body redacted.
    }
    return new InvalidOperationException($"{operation} failed with HTTP {(int)status}; code={SanitizeToken(code)}.");
}

static string SanitizeFailure(Exception exception)
{
    var message = exception.GetBaseException().Message;
    message = Regex.Replace(message, @"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", "[uuid]", RegexOptions.IgnoreCase);
    message = Regex.Replace(message, @"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", "[email]", RegexOptions.IgnoreCase);
    message = Regex.Replace(message, @"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+", "[jwt]", RegexOptions.IgnoreCase);
    return message.Length > 240 ? message[..240] : message;
}

static string SanitizeToken(string value) =>
    Regex.IsMatch(value, "^[A-Za-z0-9_.-]{1,64}$") ? value : "redacted";

static void EnsureEvidenceRedacted(
    string evidence,
    string email,
    params object[] sensitiveValues)
{
    if (evidence.Contains(email, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Evidence redaction rejected a disposable email address.");
    }
    foreach (var value in sensitiveValues)
    {
        var text = value switch
        {
            Guid guid when guid != Guid.Empty => guid.ToString(),
            string textValue => textValue,
            byte[] bytes => Convert.ToBase64String(bytes),
            _ => string.Empty
        };
        if (!string.IsNullOrWhiteSpace(text) && evidence.Contains(text, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Evidence redaction rejected sensitive material.");
        }
    }
}

static void TryDelete(string path)
{
    try { if (File.Exists(path)) File.Delete(path); }
    catch { /* The hosted cleanup result is independent of best-effort temp-file deletion. */ }
}

static JsonSerializerOptions JsonOptions(bool indented = false) => new(JsonSerializerDefaults.Web)
{
    WriteIndented = indented
};

sealed class SensitiveKey : IDisposable
{
    private SensitiveKey(byte[] bytes) => Bytes = bytes;
    public byte[] Bytes { get; }
    public static SensitiveKey Create() => new(RandomNumberGenerator.GetBytes(32));
    public void Dispose() => CryptographicOperations.ZeroMemory(Bytes);
}

sealed record AuthUser([property: JsonPropertyName("id")] Guid Id);
sealed record AuthSession(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("user")] AuthUser? User);
sealed record RecoveryConfiguration(string PublicKey, string Fingerprint, string Algorithm, string KeyVersionId);
sealed record ManagedEnrollment(bool Enrolled, string KeyVersionId);
sealed record ManagedRestore(string WrappedKey, string Algorithm, string KeyVersionId);
sealed record CodeEnrollment(bool Enrolled);
sealed record RecoveryCodeEnvelope(string Ciphertext, string Nonce, string Salt, string Algorithm, string KeyVersionId);
sealed record ActivationResponse(bool Activated);
