using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var repoRoot = GetOption(args, "--repo-root") ?? Directory.GetCurrentDirectory();
repoRoot = Path.GetFullPath(repoRoot);
var functionSupabaseUrl = GetOption(args, "--function-supabase-url") ?? "http://host.docker.internal:54321";
var supabaseCli = GetOption(args, "--supabase-cli") ?? "supabase";
var psqlCli = GetOption(args, "--psql-cli") ?? "psql";
var dockerCli = GetOption(args, "--docker-cli") ?? "docker";
var skipFunctionServe = HasOption(args, "--skip-function-serve");

var status = ReadSupabaseStatus(repoRoot, supabaseCli);
var apiUrl = Required(status, "API_URL");
var dbUrl = Required(status, "DB_URL");
var anonKey = Required(status, "ANON_KEY");
var serviceRoleKey = Required(status, "SERVICE_ROLE_KEY");
var jwtSecret = Required(status, "JWT_SECRET");

using var ingressRsa = RSA.Create(2048);
using var deviceRsa = RSA.Create(2048);
var ingressPublic = ingressRsa.ExportSubjectPublicKeyInfo();
var ingressPrivate = ingressRsa.ExportPkcs8PrivateKey();
var devicePublic = deviceRsa.ExportSubjectPublicKeyInfo();
var recoveryKek = RandomNumberGenerator.GetBytes(32);
var packageKey = RandomNumberGenerator.GetBytes(32);
var secondPackageKey = RandomNumberGenerator.GetBytes(32);
var keyVersionId = "local-harness-" + Guid.NewGuid().ToString("N")[..12];
var ownerAccountId = Guid.NewGuid();
var outsiderAccountId = Guid.NewGuid();
var logbookId = Guid.NewGuid();
var ownerDeviceId = Guid.NewGuid();
var outsiderDeviceId = Guid.NewGuid();
var ownerSessionId = Guid.NewGuid();
var outsiderSessionId = Guid.NewGuid();
var runSuffix = Guid.NewGuid().ToString("N")[..12];
var ownerEmail = $"owner-recovery-harness-{runSuffix}@example.invalid";
var outsiderEmail = $"outsider-recovery-harness-{runSuffix}@example.invalid";
var ownerToken = CreateJwt(jwtSecret, ownerAccountId, ownerEmail, ownerSessionId);
var outsiderToken = CreateJwt(jwtSecret, outsiderAccountId, outsiderEmail, outsiderSessionId);
var checks = new List<string>();

var envPath = Path.Combine(Path.GetTempPath(), "elb-recovery-envelope-" + Guid.NewGuid().ToString("N") + ".env");
Process? functionProcess = null;
try
{
    using var http = new HttpClient { BaseAddress = new Uri(apiUrl.TrimEnd('/') + "/") };
    SeedFixtures(
        psqlCli,
        dbUrl,
        ownerAccountId,
        outsiderAccountId,
        logbookId,
        ownerDeviceId,
        outsiderDeviceId,
        ownerSessionId,
        outsiderSessionId,
        ownerEmail,
        outsiderEmail);
    checks.Add("seeded disposable local Auth users, sessions, account, logbook, membership, and devices");

    await AssertAuthUserEndpointAsync(http, anonKey, ownerToken);
    checks.Add("validated disposable Auth token through local Auth user endpoint");

    WriteFunctionEnv(
        envPath,
        functionSupabaseUrl,
        anonKey,
        serviceRoleKey,
        Convert.ToBase64String(ingressPublic),
        Convert.ToBase64String(ingressPrivate),
        Convert.ToBase64String(recoveryKek),
        keyVersionId);

    if (!skipFunctionServe)
    {
        StopLocalEdgeRuntime(dockerCli);
        functionProcess = StartFunctionServe(repoRoot, supabaseCli, envPath);
    }

    await WaitForFunctionAsync(http, anonKey, ownerToken);

    var configuration = await InvokeRecoveryAsync<ConfigurationResponse>(
        http,
        anonKey,
        ownerToken,
        new { action = "configuration" },
        HttpStatusCode.OK);
    Expect(configuration.algorithm == "RSA-OAEP-256", "configuration returned RSA-OAEP-256");
    Expect(configuration.keyVersionId == keyVersionId, "configuration returned the temporary key version");
    Expect(configuration.publicKey == Convert.ToBase64String(ingressPublic), "configuration returned the temporary ingress public key");
    Expect(configuration.fingerprint == Sha256Hex(ingressPublic), "configuration fingerprint matches the ingress public key");
    checks.Add("authenticated configuration returned verified temporary public material");

    var deviceFingerprint = Sha256Hex(devicePublic);
    var wrappedPackageKey = WrapForPublicKey(configuration.publicKey, packageKey);
    var enroll = await InvokeRecoveryAsync<EnrollmentResponse>(
        http,
        anonKey,
        ownerToken,
        new
        {
            action = "enroll",
            logbookId,
            deviceId = ownerDeviceId,
            devicePublicKey = Convert.ToBase64String(devicePublic),
            devicePublicKeyFingerprint = deviceFingerprint,
            devicePublicKeyAlgorithm = "RSA-OAEP-256",
            wrappedPackageKey,
            ingressKeyVersionId = configuration.keyVersionId
        },
        HttpStatusCode.OK);
    Expect(enroll.enrolled, "enrollment returned enrolled=true");
    Expect(enroll.keyVersionId == keyVersionId, "enrollment returned the temporary key version");
    var firstManaged = ReadEnvelopeSnapshot(psqlCli, dbUrl, logbookId, ownerDeviceId);
    var restoredManagedPlaintext = DecryptManagedEnvelope(firstManaged.ManagedCiphertext, firstManaged.ManagedNonce, recoveryKek, logbookId, keyVersionId);
    Expect(restoredManagedPlaintext.SequenceEqual(packageKey), "managed AES-GCM envelope decrypts to the enrolled package key");
    Expect(firstManaged.ManagedCount == 1, "managed enrollment created exactly one active envelope");
    Expect(firstManaged.ManagedAlgorithm == "AES-256-GCM", "managed envelope uses AES-256-GCM");
    Expect(firstManaged.DeviceRecoveryPublicKey == Convert.ToBase64String(devicePublic), "device public key was bound without private material");
    Expect(firstManaged.ManagedAuditCount == 1, "managed enrollment emitted one audit event");
    Expect(!ContainsSensitiveMaterial(firstManaged.AuditDetails, wrappedPackageKey, Convert.ToBase64String(packageKey), firstManaged.ManagedCiphertext), "managed audit details are redacted");
    Expect(!firstManaged.ManagedCiphertext.Contains(Convert.ToBase64String(packageKey), StringComparison.Ordinal), "stored managed envelope is not plaintext package-key material");
    checks.Add("create-once RSA-OAEP-to-AES-GCM enrollment decrypted locally and stored only ciphertext");

    var secondWrappedPackageKey = WrapForPublicKey(configuration.publicKey, secondPackageKey);
    _ = await InvokeRecoveryAsync<EnrollmentResponse>(
        http,
        anonKey,
        ownerToken,
        new
        {
            action = "enroll",
            logbookId,
            deviceId = ownerDeviceId,
            devicePublicKey = Convert.ToBase64String(devicePublic),
            devicePublicKeyFingerprint = deviceFingerprint,
            devicePublicKeyAlgorithm = "RSA-OAEP-256",
            wrappedPackageKey = secondWrappedPackageKey,
            ingressKeyVersionId = configuration.keyVersionId
        },
        HttpStatusCode.OK);
    var secondManaged = ReadEnvelopeSnapshot(psqlCli, dbUrl, logbookId, ownerDeviceId);
    Expect(secondManaged.ManagedCount == 1, "idempotent retry retained one managed envelope");
    Expect(secondManaged.ManagedCiphertext == firstManaged.ManagedCiphertext, "idempotent retry did not replace the managed ciphertext");
    Expect(secondManaged.ManagedAuditCount == 1, "idempotent retry did not emit a duplicate managed audit event");
    checks.Add("create-once enrollment retry preserved the original managed envelope");

    var restore = await InvokeRecoveryAsync<RestoreResponse>(
        http,
        anonKey,
        ownerToken,
        new
        {
            action = "restore",
            logbookId,
            deviceId = ownerDeviceId,
            platformLabel = "Recovery Harness Owner Device",
            devicePublicKey = Convert.ToBase64String(devicePublic),
            devicePublicKeyFingerprint = deviceFingerprint,
            devicePublicKeyAlgorithm = "RSA-OAEP-256"
        },
        HttpStatusCode.OK);
    var restoredPackageKey = deviceRsa.Decrypt(Convert.FromBase64String(restore.wrappedKey), RSAEncryptionPadding.OaepSHA256);
    Expect(restoredPackageKey.SequenceEqual(packageKey), "device restore envelope decrypts to the original package key");
    var restoredSnapshot = ReadEnvelopeSnapshot(psqlCli, dbUrl, logbookId, ownerDeviceId);
    Expect(restoredSnapshot.DeviceEnvelopeCount == 1, "restore stored one short-lived device envelope");
    Expect(restoredSnapshot.DeviceCiphertext == restore.wrappedKey, "stored device envelope matches the returned restore envelope");
    Expect(restoredSnapshot.DeviceAlgorithm == "RSA-OAEP-256", "device restore envelope uses RSA-OAEP-256");
    Expect(restoredSnapshot.DeviceEnvelopeExpiresInFuture, "device restore envelope expires in the future");
    Expect(restoredSnapshot.DeviceAuditCount == 1, "restore emitted one device-envelope audit event");
    Expect(!ContainsSensitiveMaterial(restoredSnapshot.AuditDetails, restore.wrappedKey, Convert.ToBase64String(packageKey), restoredSnapshot.DeviceCiphertext), "restore audit details are redacted");
    checks.Add("device-envelope restore decrypted locally, stored short-lived ciphertext, and redacted audit details");

    await InvokeRecoveryAsync<ErrorResponse>(
        http,
        anonKey,
        outsiderToken,
        new
        {
            action = "restore",
            logbookId,
            deviceId = outsiderDeviceId,
            platformLabel = "Recovery Harness Outsider Device",
            devicePublicKey = Convert.ToBase64String(devicePublic),
            devicePublicKeyFingerprint = deviceFingerprint,
            devicePublicKeyAlgorithm = "RSA-OAEP-256"
        },
        HttpStatusCode.Forbidden);
    checks.Add("outsider restore was denied");

    await InvokeRecoveryAsync<ErrorResponse>(
        http,
        anonKey,
        ownerToken,
        new
        {
            action = "restore",
            logbookId,
            deviceId = outsiderDeviceId,
            platformLabel = "Recovery Harness Outsider Device",
            devicePublicKey = Convert.ToBase64String(devicePublic),
            devicePublicKeyFingerprint = deviceFingerprint,
            devicePublicKeyAlgorithm = "RSA-OAEP-256"
        },
        HttpStatusCode.Forbidden);
    checks.Add("wrong-device restore was denied");

    await InvokeRestRpcDeniedAsync(http, anonKey, ownerToken, logbookId, ownerDeviceId, ownerAccountId);
    checks.Add("authenticated direct recovery RPC call was denied");
}
finally
{
    if (functionProcess is not null)
    {
        TryStop(functionProcess);
        StopLocalEdgeRuntime(dockerCli);
    }

    TryDeleteFile(envPath);
    if (ownerAccountId != Guid.Empty && outsiderAccountId != Guid.Empty)
    {
        CleanupFixtures(psqlCli, dbUrl, ownerAccountId, outsiderAccountId, logbookId, ownerDeviceId, outsiderDeviceId);
        var cleanup = ReadCleanupCounts(psqlCli, dbUrl, ownerAccountId, outsiderAccountId, logbookId, ownerDeviceId, outsiderDeviceId);
        if (cleanup != 0)
        {
            throw new InvalidOperationException("Recovery-envelope harness cleanup left disposable rows behind.");
        }
    }
}

Console.WriteLine("Recovery envelope cryptographic harness passed.");
foreach (var check in checks)
{
    Console.WriteLine("- " + check);
}

static string? GetOption(string[] args, string name)
{
    for (var index = 0; index < args.Length - 1; index++)
    {
        if (string.Equals(args[index], name, StringComparison.Ordinal))
        {
            return args[index + 1];
        }
    }

    return null;
}

static bool HasOption(string[] args, string name) =>
    args.Any(arg => string.Equals(arg, name, StringComparison.Ordinal));

static IReadOnlyDictionary<string, string> ReadSupabaseStatus(string repoRoot, string supabaseCli)
{
    var output = RunProcess(supabaseCli, "status --output json", repoRoot, captureSecretOutput: true);
    using var document = JsonDocument.Parse(output);
    return document.RootElement.EnumerateObject()
        .Where(property => property.Value.ValueKind == JsonValueKind.String)
        .ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty);
}

static string Required(IReadOnlyDictionary<string, string> values, string name)
{
    if (!values.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"Local Supabase status did not include {name}.");
    }

    return value;
}

static void SeedFixtures(
    string psqlCli,
    string dbUrl,
    Guid ownerAccountId,
    Guid outsiderAccountId,
    Guid logbookId,
    Guid ownerDeviceId,
    Guid outsiderDeviceId,
    Guid ownerSessionId,
    Guid outsiderSessionId,
    string ownerEmail,
    string outsiderEmail)
{
    ExecuteSql(psqlCli, dbUrl, $"""
        insert into auth.users (
            id, instance_id, aud, role, email, encrypted_password, email_confirmed_at,
            confirmation_token, recovery_token, email_change_token_new, email_change,
            phone_change, phone_change_token, email_change_token_current, reauthentication_token,
            raw_app_meta_data, raw_user_meta_data, is_sso_user,
            is_anonymous, created_at, updated_at
        )
        values
            ('{ownerAccountId}', '00000000-0000-0000-0000-000000000000', 'authenticated', 'authenticated',
             '{ownerEmail}', '', now(),
             '', '', '', '', '', '', '', '',
             jsonb_build_object('provider', 'email', 'providers', jsonb_build_array('email')),
             jsonb_build_object(), false, false, now(), now()),
            ('{outsiderAccountId}', '00000000-0000-0000-0000-000000000000', 'authenticated', 'authenticated',
             '{outsiderEmail}', '', now(),
             '', '', '', '', '', '', '', '',
             jsonb_build_object('provider', 'email', 'providers', jsonb_build_array('email')),
             jsonb_build_object(), false, false, now(), now());

        insert into auth.sessions (id, user_id, created_at, updated_at, aal, not_after)
        values
            ('{ownerSessionId}', '{ownerAccountId}', now(), now(), 'aal1', now() + interval '2 hours'),
            ('{outsiderSessionId}', '{outsiderAccountId}', now(), now(), 'aal1', now() + interval '2 hours');

        insert into auth.identities (
            provider_id, user_id, identity_data, provider, last_sign_in_at, created_at, updated_at
        )
        values
            (
                '{ownerAccountId}', '{ownerAccountId}',
                jsonb_build_object('sub', '{ownerAccountId}', 'email', '{ownerEmail}', 'email_verified', true),
                'email', now(), now(), now()
            ),
            (
                '{outsiderAccountId}', '{outsiderAccountId}',
                jsonb_build_object('sub', '{outsiderAccountId}', 'email', '{outsiderEmail}', 'email_verified', true),
                'email', now(), now(), now()
            );

        insert into public.accounts (account_id, invited_email, display_name, status)
        values
            ('{ownerAccountId}', '{ownerEmail}', 'Recovery Harness Owner', 'active'),
            ('{outsiderAccountId}', '{outsiderEmail}', 'Recovery Harness Outsider', 'active');

        insert into public.logbooks (logbook_id, owner_account_id, display_name)
        values ('{logbookId}', '{ownerAccountId}', 'Recovery Harness Logbook');

        insert into public.logbook_memberships (
            logbook_id, account_id, role, granted_by_account_id, accepted_at
        )
        values ('{logbookId}', '{ownerAccountId}', 'owner', '{ownerAccountId}', now());

        insert into public.devices (device_id, account_id, device_type, platform_label, status)
        values
            ('{ownerDeviceId}', '{ownerAccountId}', 'android', 'Recovery Harness Owner Device', 'active'),
            ('{outsiderDeviceId}', '{outsiderAccountId}', 'android', 'Recovery Harness Outsider Device', 'active');
        """);
}

static void CleanupFixtures(
    string psqlCli,
    string dbUrl,
    Guid ownerAccountId,
    Guid outsiderAccountId,
    Guid logbookId,
    Guid ownerDeviceId,
    Guid outsiderDeviceId)
{
    ExecuteSql(psqlCli, dbUrl, $"""
        delete from public.security_events
        where logbook_id = '{logbookId}'
           or account_id in ('{ownerAccountId}', '{outsiderAccountId}')
           or device_id in ('{ownerDeviceId}', '{outsiderDeviceId}');
        delete from public.key_envelopes
        where logbook_id = '{logbookId}'
           or recipient_device_id in ('{ownerDeviceId}', '{outsiderDeviceId}')
           or created_by_device_id in ('{ownerDeviceId}', '{outsiderDeviceId}');
        delete from public.operation_acks where logbook_id = '{logbookId}';
        delete from public.operations where logbook_id = '{logbookId}';
        delete from public.devices where device_id in ('{ownerDeviceId}', '{outsiderDeviceId}');
        delete from public.logbook_memberships
        where logbook_id = '{logbookId}'
           or account_id in ('{ownerAccountId}', '{outsiderAccountId}');
        delete from public.logbooks where logbook_id = '{logbookId}';
        delete from public.accounts where account_id in ('{ownerAccountId}', '{outsiderAccountId}');
        delete from auth.sessions where user_id in ('{ownerAccountId}', '{outsiderAccountId}');
        delete from auth.identities where user_id in ('{ownerAccountId}', '{outsiderAccountId}');
        delete from auth.users where id in ('{ownerAccountId}', '{outsiderAccountId}');
        """);
}

static int ReadCleanupCounts(
    string psqlCli,
    string dbUrl,
    Guid ownerAccountId,
    Guid outsiderAccountId,
    Guid logbookId,
    Guid ownerDeviceId,
    Guid outsiderDeviceId)
{
    var json = QueryScalar(psqlCli, dbUrl, $"""
        select jsonb_build_object(
            'count',
            (select count(*) from public.security_events where logbook_id = '{logbookId}' or account_id in ('{ownerAccountId}', '{outsiderAccountId}') or device_id in ('{ownerDeviceId}', '{outsiderDeviceId}'))
            + (select count(*) from public.key_envelopes where logbook_id = '{logbookId}' or recipient_device_id in ('{ownerDeviceId}', '{outsiderDeviceId}') or created_by_device_id in ('{ownerDeviceId}', '{outsiderDeviceId}'))
            + (select count(*) from public.operation_acks where logbook_id = '{logbookId}')
            + (select count(*) from public.operations where logbook_id = '{logbookId}')
            + (select count(*) from public.devices where device_id in ('{ownerDeviceId}', '{outsiderDeviceId}'))
            + (select count(*) from public.logbook_memberships where logbook_id = '{logbookId}' or account_id in ('{ownerAccountId}', '{outsiderAccountId}'))
            + (select count(*) from public.logbooks where logbook_id = '{logbookId}')
            + (select count(*) from public.accounts where account_id in ('{ownerAccountId}', '{outsiderAccountId}'))
            + (select count(*) from auth.sessions where user_id in ('{ownerAccountId}', '{outsiderAccountId}'))
            + (select count(*) from auth.identities where user_id in ('{ownerAccountId}', '{outsiderAccountId}'))
            + (select count(*) from auth.users where id in ('{ownerAccountId}', '{outsiderAccountId}'))
        )::text;
        """);
    using var document = JsonDocument.Parse(json);
    return document.RootElement.GetProperty("count").GetInt32();
}

static EnvelopeSnapshot ReadEnvelopeSnapshot(string psqlCli, string dbUrl, Guid logbookId, Guid deviceId)
{
    var json = QueryScalar(psqlCli, dbUrl, $"""
        select jsonb_build_object(
            'managedCount', (select count(*) from public.key_envelopes where logbook_id = '{logbookId}' and recovery_method = 'managed-service-v1' and recipient_device_id is null and revoked_at is null),
            'managedCiphertext', coalesce((select ciphertext from public.key_envelopes where logbook_id = '{logbookId}' and recovery_method = 'managed-service-v1' and recipient_device_id is null and revoked_at is null limit 1), ''),
            'managedNonce', coalesce((select nonce from public.key_envelopes where logbook_id = '{logbookId}' and recovery_method = 'managed-service-v1' and recipient_device_id is null and revoked_at is null limit 1), ''),
            'managedAlgorithm', coalesce((select wrapping_algorithm from public.key_envelopes where logbook_id = '{logbookId}' and recovery_method = 'managed-service-v1' and recipient_device_id is null and revoked_at is null limit 1), ''),
            'deviceEnvelopeCount', (select count(*) from public.key_envelopes where logbook_id = '{logbookId}' and recipient_device_id = '{deviceId}' and revoked_at is null),
            'deviceCiphertext', coalesce((select ciphertext from public.key_envelopes where logbook_id = '{logbookId}' and recipient_device_id = '{deviceId}' and revoked_at is null limit 1), ''),
            'deviceAlgorithm', coalesce((select wrapping_algorithm from public.key_envelopes where logbook_id = '{logbookId}' and recipient_device_id = '{deviceId}' and revoked_at is null limit 1), ''),
            'deviceEnvelopeExpiresInFuture', coalesce((select expires_at > now() from public.key_envelopes where logbook_id = '{logbookId}' and recipient_device_id = '{deviceId}' and revoked_at is null limit 1), false),
            'deviceRecoveryPublicKey', coalesce((select recovery_public_key from public.devices where device_id = '{deviceId}' limit 1), ''),
            'managedAuditCount', (select count(*) from public.security_events where logbook_id = '{logbookId}' and event_type = 'managed_recovery_envelope_created'),
            'deviceAuditCount', (select count(*) from public.security_events where logbook_id = '{logbookId}' and event_type = 'device_recovery_envelope_issued'),
            'auditDetails', coalesce((select string_agg(redacted_details::text, E'\n') from public.security_events where logbook_id = '{logbookId}'), '')
        )::text;
        """);
    return JsonSerializer.Deserialize<EnvelopeSnapshot>(json, JsonOptions())
        ?? throw new InvalidOperationException("Envelope snapshot query returned no data.");
}

static void ExecuteSql(string psqlCli, string dbUrl, string sql)
{
    var path = Path.Combine(Path.GetTempPath(), "elb-recovery-envelope-" + Guid.NewGuid().ToString("N") + ".sql");
    try
    {
        File.WriteAllText(path, sql, Encoding.UTF8);
        RunProcess(psqlCli, $"{Quote(dbUrl)} -v ON_ERROR_STOP=1 -q -f {Quote(path)}", Directory.GetCurrentDirectory(), captureSecretOutput: true);
    }
    finally
    {
        TryDeleteFile(path);
    }
}

static string QueryScalar(string psqlCli, string dbUrl, string sql) =>
    RunProcess(psqlCli, $"{Quote(dbUrl)} -v ON_ERROR_STOP=1 -t -A -q -c {Quote(sql)}", Directory.GetCurrentDirectory(), captureSecretOutput: true).Trim();

static void WriteFunctionEnv(
    string path,
    string supabaseUrl,
    string anonKey,
    string serviceRoleKey,
    string ingressPublicKey,
    string ingressPrivateKey,
    string recoveryKek,
    string keyVersionId)
{
    File.WriteAllLines(path, [
        "SUPABASE_URL=" + supabaseUrl,
        "SUPABASE_ANON_KEY=" + anonKey,
        "SUPABASE_SERVICE_ROLE_KEY=" + serviceRoleKey,
        "RECOVERY_INGRESS_PUBLIC_KEY_SPKI_BASE64=" + ingressPublicKey,
        "RECOVERY_INGRESS_PRIVATE_KEY_PKCS8_BASE64=" + ingressPrivateKey,
        "RECOVERY_KEK_BASE64=" + recoveryKek,
        "RECOVERY_KEY_VERSION_ID=" + keyVersionId
    ]);
}

static Process StartFunctionServe(string repoRoot, string supabaseCli, string envPath)
{
    var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = supabaseCli,
            Arguments = $"functions serve recovery-envelope --no-verify-jwt --env-file {Quote(envPath)}",
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        }
    };
    process.Start();
    return process;
}

static void StopLocalEdgeRuntime(string dockerCli)
{
    try
    {
        RunProcess(dockerCli, "rm -f supabase_edge_runtime_Electronic-Logbook", Directory.GetCurrentDirectory(), captureSecretOutput: true);
    }
    catch
    {
        // The container may not exist yet. The harness only needs a best-effort stale-env cleanup.
    }
}

static async Task WaitForFunctionAsync(HttpClient http, string anonKey, string ownerToken)
{
    Exception? last = null;
    for (var attempt = 0; attempt < 60; attempt++)
    {
        try
        {
            _ = await InvokeRecoveryAsync<ConfigurationResponse>(
                http,
                anonKey,
                ownerToken,
                new { action = "configuration" },
                HttpStatusCode.OK);
            return;
        }
        catch (Exception ex)
        {
            last = ex;
            await Task.Delay(1000);
        }
    }

    throw new InvalidOperationException("The local recovery-envelope function did not become ready.", last);
}

static async Task<T> InvokeRecoveryAsync<T>(
    HttpClient http,
    string anonKey,
    string bearerToken,
    object body,
    HttpStatusCode expectedStatus)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "functions/v1/recovery-envelope")
    {
                Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions()), Encoding.UTF8, "application/json")
    };
    request.Headers.TryAddWithoutValidation("apikey", anonKey);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
    using var response = await http.SendAsync(request);
    var responseBody = await response.Content.ReadAsStringAsync();
    if (response.StatusCode != expectedStatus)
    {
        throw new InvalidOperationException(
            $"Unexpected recovery-envelope status {(int)response.StatusCode}; {SanitizedErrorCode(responseBody)}.");
    }

    return JsonSerializer.Deserialize<T>(responseBody, JsonOptions())
        ?? throw new InvalidOperationException("Recovery-envelope returned an empty JSON body.");
}

static async Task AssertAuthUserEndpointAsync(HttpClient http, string anonKey, string bearerToken)
{
    using var request = new HttpRequestMessage(HttpMethod.Get, "auth/v1/user");
    request.Headers.TryAddWithoutValidation("apikey", anonKey);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
    using var response = await http.SendAsync(request);
    var body = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
    {
        throw new InvalidOperationException(
            $"Disposable Auth token was rejected by local Auth; status={(int)response.StatusCode}; {SanitizedErrorCode(body)}.");
    }
}

static string SanitizedErrorCode(string body)
{
    try
    {
        using var document = JsonDocument.Parse(body);
        if (document.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String)
        {
            return "code=" + code.GetString();
        }

        if (document.RootElement.TryGetProperty("error_code", out var errorCode) && errorCode.ValueKind == JsonValueKind.String)
        {
            return "code=" + errorCode.GetString();
        }

        if (document.RootElement.TryGetProperty("msg", out var msg) && msg.ValueKind == JsonValueKind.String)
        {
            var value = msg.GetString() ?? string.Empty;
            return "message=" + value[..Math.Min(value.Length, 80)];
        }
    }
    catch
    {
        // Fall through to a redacted generic message.
    }

    return "body redacted";
}

static async Task InvokeRestRpcDeniedAsync(
    HttpClient http,
    string anonKey,
    string ownerToken,
    Guid logbookId,
    Guid deviceId,
    Guid accountId)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "rest/v1/rpc/elb_read_managed_recovery_envelope")
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            p_actor_account_id = accountId,
            p_logbook_id = logbookId,
            p_device_id = deviceId
        }, JsonOptions()), Encoding.UTF8, "application/json")
    };
    request.Headers.TryAddWithoutValidation("apikey", anonKey);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
    using var response = await http.SendAsync(request);
    if (response.IsSuccessStatusCode)
    {
        throw new InvalidOperationException("Authenticated client could call the managed recovery RPC directly.");
    }
}

static string WrapForPublicKey(string publicKeyBase64, byte[] packageKey)
{
    using var rsa = RSA.Create();
    rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
    return Convert.ToBase64String(rsa.Encrypt(packageKey, RSAEncryptionPadding.OaepSHA256));
}

static string CreateJwt(string secret, Guid subject, string email, Guid sessionId)
{
    var issuedAt = DateTimeOffset.UtcNow;
    var header = Base64Url(Encoding.UTF8.GetBytes("""{"alg":"HS256","typ":"JWT"}"""));
    var payload = Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    {
        iss = "supabase-demo",
        sub = subject,
        aud = "authenticated",
        role = "authenticated",
        email,
        phone = "",
        app_metadata = new { provider = "email", providers = new[] { "email" } },
        user_metadata = new { },
        aal = "aal1",
        session_id = sessionId,
        is_anonymous = false,
        iat = issuedAt.ToUnixTimeSeconds(),
        nbf = issuedAt.AddSeconds(-5).ToUnixTimeSeconds(),
        exp = issuedAt.AddHours(2).ToUnixTimeSeconds()
    }, JsonOptions())));
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
    var signature = Base64Url(hmac.ComputeHash(Encoding.ASCII.GetBytes(header + "." + payload)));
    return header + "." + payload + "." + signature;
}

static string Base64Url(byte[] value) =>
    Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

static byte[] DecryptManagedEnvelope(
    string ciphertextBase64,
    string nonceBase64,
    byte[] key,
    Guid logbookId,
    string keyVersionId)
{
    var encrypted = Convert.FromBase64String(ciphertextBase64);
    var nonce = Convert.FromBase64String(nonceBase64);
    var ciphertext = encrypted[..^16];
    var tag = encrypted[^16..];
    var plaintext = new byte[32];
    var additionalData = Encoding.UTF8.GetBytes($"electronic-logbook|managed-service-v1|{logbookId}|{keyVersionId}");
    using var aes = new AesGcm(key, 16);
    aes.Decrypt(nonce, ciphertext, tag, plaintext, additionalData);
    return plaintext;
}

static bool ContainsSensitiveMaterial(string value, params string[] sensitiveValues) =>
    sensitiveValues
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .Any(item => value.Contains(item, StringComparison.Ordinal));

static string Sha256Hex(byte[] value) =>
    Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

static string RunProcess(string fileName, string arguments, string workingDirectory, bool captureSecretOutput)
{
    using var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        }
    };
    process.Start();
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        var detail = captureSecretOutput ? SanitizeProcessError(stdout + stderr) : (stdout + stderr);
        throw new InvalidOperationException($"{fileName} failed with exit code {process.ExitCode}; {detail}");
    }

    return stdout;
}

static string SanitizeProcessError(string value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return "output redacted";
    }

    var line = value
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .FirstOrDefault(item => item.Contains("ERROR:", StringComparison.OrdinalIgnoreCase)
            || item.Contains("FATAL:", StringComparison.OrdinalIgnoreCase)
            || item.Contains("failed", StringComparison.OrdinalIgnoreCase))
        ?? value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).First();
    line = System.Text.RegularExpressions.Regex.Replace(
        line,
        @"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}",
        "[uuid]",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    line = System.Text.RegularExpressions.Regex.Replace(
        line,
        @"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}",
        "[email]",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    return line.Length > 200 ? line[..200] : line;
}

static string Quote(string value) =>
    "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

static void Expect(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException("Harness assertion failed: " + message);
    }
}

static void TryStop(Process process)
{
    try
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
    }
    catch
    {
        // Best-effort cleanup. The harness reports data cleanup separately.
    }
}

static void TryDeleteFile(string path)
{
    try
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
    catch
    {
        // Best-effort cleanup of secret-bearing temp files.
    }
}

static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web);

sealed record ConfigurationResponse(
    string publicKey,
    string fingerprint,
    string algorithm,
    string keyVersionId);

sealed record EnrollmentResponse(
    bool enrolled,
    string keyVersionId);

sealed record RestoreResponse(
    string wrappedKey,
    string algorithm,
    string keyVersionId);

sealed record ErrorResponse(
    string code,
    string message);

sealed record EnvelopeSnapshot(
    int ManagedCount,
    string ManagedCiphertext,
    string ManagedNonce,
    string ManagedAlgorithm,
    int DeviceEnvelopeCount,
    string DeviceCiphertext,
    string DeviceAlgorithm,
    bool DeviceEnvelopeExpiresInFuture,
    string DeviceRecoveryPublicKey,
    int ManagedAuditCount,
    int DeviceAuditCount,
    string AuditDetails);
