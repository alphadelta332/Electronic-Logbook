using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class SupabaseWorkbookConnectionClientTests
{
    [Fact]
    public void ConfigurationLoadsEmbeddedPublishedWizardSettings()
    {
        var loaded = SupabaseHostedSyncConfiguration.TryLoadEmbeddedConfiguration(
            typeof(SupabaseWorkbookConnectionClientTests).Assembly,
            "ElectronicLogbook.Tests.HostedSyncConfiguration.json",
            out var configuration,
            out var unavailableReason);

        Assert.True(loaded, unavailableReason);
        Assert.Equal("https://development-test.supabase.co/", configuration?.SupabaseUrl.AbsoluteUri);
        Assert.Equal("public-anon-key", configuration?.AnonKey);
        Assert.Equal("Excel embedded test", configuration?.PlatformLabel);
    }

    [Fact]
    public async Task InvitedOtpDiscoveryAndManagedWorkbookRecoveryUseBoundedPublicRequests()
    {
        var accountId = Guid.NewGuid();
        var logbookUuid = Guid.NewGuid();
        var deviceUuid = Guid.NewGuid();
        var packageKey = PortableLogbookKey.Generate();
        var handler = new WorkbookConnectionHandler(accountId, logbookUuid, packageKey);
        using var http = new HttpClient(handler);
        var configuration = new SupabaseHostedSyncConfiguration(
            new Uri("http://localhost:54321"),
            "public-anon-key",
            "Excel Test Device");
        using var client = new SupabaseWorkbookConnectionClient(configuration, http);

        var started = await client.StartEmailSignInAsync("pilot@example.com");
        var session = await client.CompleteEmailSignInAsync("123456");
        var logbooks = await client.DiscoverActiveLogbooksAsync();
        using var recoveryKeyPair = PortableWorkbookRecoveryKeyPair.Create();
        var recovered = await client.RestoreWorkbookKeyAsync(
            logbooks.Single().LogbookId,
            new DeviceId("dev_" + deviceUuid.ToString("N")),
            recoveryKeyPair);
        await client.ActivateWorkbookDeviceAsync(
            logbooks.Single().LogbookId,
            new DeviceId("dev_" + deviceUuid.ToString("N")));

        Assert.Equal("p***@example.com", started.DeliveryHint);
        Assert.Equal("acct_" + accountId.ToString("N"), session.AccountId.Value);
        Assert.Equal("Test Logbook", logbooks.Single().DisplayName);
        Assert.Equal(packageKey, recovered);

        var otp = handler.Requests.Single(request => request.Path == "/auth/v1/otp");
        Assert.False(JsonDocument.Parse(otp.Body).RootElement.GetProperty("create_user").GetBoolean());
        var verify = handler.Requests.Single(request => request.Path == "/auth/v1/verify");
        using var verifyJson = JsonDocument.Parse(verify.Body);
        Assert.Equal("pilot@example.com", verifyJson.RootElement.GetProperty("email").GetString());
        Assert.Equal("123456", verifyJson.RootElement.GetProperty("token").GetString());
        Assert.False(verifyJson.RootElement.TryGetProperty("token_hash", out _));
        var restore = handler.Requests.Single(request =>
            request.Path == "/functions/v1/recovery-envelope" &&
            JsonDocument.Parse(request.Body).RootElement.GetProperty("action").GetString() == "restore");
        using var restoreJson = JsonDocument.Parse(restore.Body);
        Assert.Equal("workbook", restoreJson.RootElement.GetProperty("deviceType").GetString());
        Assert.Equal("Excel Test Device", restoreJson.RootElement.GetProperty("platformLabel").GetString());
        Assert.Equal(deviceUuid.ToString("D"), restoreJson.RootElement.GetProperty("deviceId").GetString());
        Assert.DoesNotContain("access-token", restore.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-token", restore.Body, StringComparison.Ordinal);
        Assert.Equal("Bearer", restore.AuthorizationScheme);
    }

    [Fact]
    public async Task WorkbookMigrationLifecycleUsesNarrowAuthenticatedRpcCallsAndStableResources()
    {
        var accountId = Guid.NewGuid();
        var logbookId = Guid.NewGuid();
        var handler = new WorkbookConnectionHandler(accountId, logbookId, PortableLogbookKey.Generate());
        using var http = new HttpClient(handler);
        var configuration = new SupabaseHostedSyncConfiguration(
            new Uri("https://pilot.supabase.co"),
            "public-anon-key",
            "Windows Migration");
        using var client = new SupabaseWorkbookConnectionClient(configuration, http);
        var sourceFingerprint = new string('a', 64);
        var receiptHash = new string('e', 64);

        await client.StartEmailSignInAsync("pilot@example.com");
        await client.CompleteEmailSignInAsync("123456");
        var started = await client.BeginWorkbookMigrationAsync(sourceFingerprint, "Migrated Logbook");
        var status = await client.GetWorkbookMigrationStatusAsync();
        var failed = await client.FailWorkbookMigrationAsync(started.MigrationId, "NETWORK_INTERRUPTED");
        var completed = await client.CompleteWorkbookMigrationAsync(started.MigrationId, 321, receiptHash);

        Assert.Equal(HostedWorkbookMigrationStatus.Pending, started.Status);
        Assert.Equal(started.MigrationId, status.MigrationId);
        Assert.Equal(started.LogbookId, status.LogbookId);
        Assert.Equal(started.DeviceId, status.DeviceId);
        Assert.Equal(HostedWorkbookMigrationStatus.Failed, failed.Status);
        Assert.Equal("NETWORK_INTERRUPTED", failed.FailureCode);
        Assert.Equal(HostedWorkbookMigrationStatus.Completed, completed.Status);
        Assert.Equal(321, completed.VerifiedOperationCount);
        Assert.Equal(receiptHash, completed.VerificationReceiptHash);

        var begin = handler.Requests.Single(request => request.Path.EndsWith("/begin_workbook_migration", StringComparison.Ordinal));
        using var beginJson = JsonDocument.Parse(begin.Body);
        Assert.Equal(sourceFingerprint, beginJson.RootElement.GetProperty("p_source_fingerprint").GetString());
        Assert.Equal("Windows Migration", beginJson.RootElement.GetProperty("p_platform_label").GetString());
        Assert.DoesNotContain("access-token", begin.Body, StringComparison.Ordinal);
        Assert.All(
            handler.Requests.Where(request => request.Path.Contains("workbook_migration", StringComparison.Ordinal)),
            request => Assert.Equal("Bearer", request.AuthorizationScheme));
    }

    [Fact]
    public async Task WorkbookMigrationEnrollsManagedRecoveryWithoutSendingPlaintextKey()
    {
        var accountId = Guid.NewGuid();
        var logbookId = Guid.NewGuid();
        var packageKey = PortableLogbookKey.Generate();
        var handler = new WorkbookConnectionHandler(accountId, logbookId, packageKey);
        using var http = new HttpClient(handler);
        var configuration = new SupabaseHostedSyncConfiguration(
            new Uri("https://pilot.supabase.co"),
            "public-anon-key",
            "Windows Migration");
        using var client = new SupabaseWorkbookConnectionClient(configuration, http);

        await client.StartEmailSignInAsync("pilot@example.com");
        await client.CompleteEmailSignInAsync("123456");
        var migration = await client.BeginWorkbookMigrationAsync(new string('a', 64), "Migrated Logbook");
        using var recoveryKeyPair = PortableWorkbookRecoveryKeyPair.Create();

        await client.EnrollWorkbookRecoveryAsync(
            migration.LogbookId,
            migration.DeviceId,
            packageKey,
            recoveryKeyPair);

        Assert.True(handler.EnrollmentMatchedPackageKey);
        var configurationRequest = handler.Requests.Single(request =>
            request.Path == "/functions/v1/recovery-envelope" &&
            JsonDocument.Parse(request.Body).RootElement.GetProperty("action").GetString() == "configuration");
        var enrollmentRequest = handler.Requests.Single(request =>
            request.Path == "/functions/v1/recovery-envelope" &&
            JsonDocument.Parse(request.Body).RootElement.GetProperty("action").GetString() == "enroll");
        using var enrollmentJson = JsonDocument.Parse(enrollmentRequest.Body);
        Assert.Equal(logbookId.ToString("D"), enrollmentJson.RootElement.GetProperty("logbookId").GetString());
        Assert.Equal(
            recoveryKeyPair.PublicKey,
            enrollmentJson.RootElement.GetProperty("devicePublicKey").GetString());
        Assert.Equal("test-v1", enrollmentJson.RootElement.GetProperty("ingressKeyVersionId").GetString());
        Assert.DoesNotContain(packageKey.ToRecoveryCode(), enrollmentRequest.Body, StringComparison.Ordinal);
        Assert.Equal("Bearer", configurationRequest.AuthorizationScheme);
        Assert.Equal("Bearer", enrollmentRequest.AuthorizationScheme);
    }

    [Fact]
    public async Task WorkbookMigrationRecoveryResumesOneCredentialAndCompletionRemovesIt()
    {
        var handler = new WorkbookConnectionHandler(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PortableLogbookKey.Generate());
        using var http = new HttpClient(handler);
        var configuration = new SupabaseHostedSyncConfiguration(
            new Uri("https://pilot.supabase.co"),
            "public-anon-key",
            "Windows Migration");
        using var client = new SupabaseWorkbookConnectionClient(configuration, http);

        await client.StartEmailSignInAsync("pilot@example.com");
        await client.CompleteEmailSignInAsync("123456");
        var migration = await client.BeginWorkbookMigrationAsync(new string('a', 64), "Migrated Logbook");
        var targetName = PortableWorkbookMigrationRecoveryStore.CreateTargetName(
            migration.LogbookId,
            migration.DeviceId);

        try
        {
            using var prepared = await client.PrepareAndEnrollWorkbookRecoveryAsync(migration);
            using var resumed = await client.PrepareAndEnrollWorkbookRecoveryAsync(migration);

            Assert.False(prepared.Resumed);
            Assert.True(resumed.Resumed);
            Assert.Equal(prepared.LogbookKey, resumed.LogbookKey);
            Assert.Equal(
                prepared.RecoveryKeyPair.Fingerprint,
                resumed.RecoveryKeyPair.Fingerprint);
            Assert.Equal(2, handler.EnrollmentPackageKeyFingerprints.Count);
            Assert.Single(handler.EnrollmentPackageKeyFingerprints.Distinct(StringComparer.Ordinal));
            using (var retained = PortableWorkbookMigrationRecoveryStore.Load(targetName))
            {
                Assert.NotNull(retained);
            }

            await client.CompleteWorkbookMigrationAsync(
                migration.MigrationId,
                expectedOperationCount: 321,
                verificationReceiptHash: new string('e', 64));

            Assert.Null(PortableWorkbookMigrationRecoveryStore.Load(targetName));
        }
        finally
        {
            PortableWorkbookMigrationRecoveryStore.Delete(targetName);
        }
    }

    [Fact]
    public async Task MigrationRecoveryCoordinatorConfirmsAndResumesTheSameHostedIdentityAndKeys()
    {
        var handler = new WorkbookConnectionHandler(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PortableLogbookKey.Generate());
        using var http = new HttpClient(handler);
        var configuration = new SupabaseHostedSyncConfiguration(
            new Uri("https://pilot.supabase.co"),
            "public-anon-key",
            "Windows Migration");
        using var client = new SupabaseWorkbookConnectionClient(configuration, http);
        var coordinator = new WorkbookMigrationRecoveryCoordinator(client);
        var sourceFingerprint = new string('a', 64);

        await client.StartEmailSignInAsync("pilot@example.com");
        await client.CompleteEmailSignInAsync("123456");
        using var first = await coordinator.PrepareAsync(sourceFingerprint, "Migrated Logbook");
        var targetName = first.RecoveryMaterial.CredentialTargetName;

        try
        {
            using var retry = await coordinator.PrepareAsync(sourceFingerprint, "Migrated Logbook");

            Assert.False(first.RecoveryMaterial.Resumed);
            Assert.True(retry.RecoveryMaterial.Resumed);
            Assert.Equal(first.Migration.MigrationId, retry.Migration.MigrationId);
            Assert.Equal(first.Migration.AccountId, retry.Migration.AccountId);
            Assert.Equal(first.Migration.LogbookId, retry.Migration.LogbookId);
            Assert.Equal(first.Migration.DeviceId, retry.Migration.DeviceId);
            Assert.Equal(first.RecoveryMaterial.LogbookKey, retry.RecoveryMaterial.LogbookKey);
            Assert.Equal(
                first.RecoveryMaterial.RecoveryKeyPair.Fingerprint,
                retry.RecoveryMaterial.RecoveryKeyPair.Fingerprint);
            Assert.Equal(2, handler.Requests.Count(request =>
                request.Path.EndsWith("/begin_workbook_migration", StringComparison.Ordinal)));
            Assert.Equal(2, handler.Requests.Count(request =>
                request.Path.EndsWith("/get_workbook_migration_status", StringComparison.Ordinal)));
            Assert.Equal(2, handler.EnrollmentPackageKeyFingerprints.Count);
            Assert.Single(handler.EnrollmentPackageKeyFingerprints.Distinct(StringComparer.Ordinal));
        }
        finally
        {
            PortableWorkbookMigrationRecoveryStore.Delete(targetName);
        }
    }

    [Fact]
    public async Task WorkbookMigrationRejectsUnverifiedRecoveryIngressKeyBeforeEnrollment()
    {
        var packageKey = PortableLogbookKey.Generate();
        var handler = new WorkbookConnectionHandler(
            Guid.NewGuid(),
            Guid.NewGuid(),
            packageKey,
            invalidIngressFingerprint: true);
        using var http = new HttpClient(handler);
        var configuration = new SupabaseHostedSyncConfiguration(
            new Uri("https://pilot.supabase.co"),
            "public-anon-key",
            "Windows Migration");
        using var client = new SupabaseWorkbookConnectionClient(configuration, http);

        await client.StartEmailSignInAsync("pilot@example.com");
        await client.CompleteEmailSignInAsync("123456");
        var migration = await client.BeginWorkbookMigrationAsync(new string('a', 64), "Migrated Logbook");
        var targetName = PortableWorkbookMigrationRecoveryStore.CreateTargetName(
            migration.LogbookId,
            migration.DeviceId);

        try
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                client.PrepareAndEnrollWorkbookRecoveryAsync(migration));

            Assert.Contains("fingerprint", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(handler.Requests, request =>
                request.Path == "/functions/v1/recovery-envelope" &&
                JsonDocument.Parse(request.Body).RootElement.GetProperty("action").GetString() == "enroll");
            using var retained = PortableWorkbookMigrationRecoveryStore.Load(targetName);
            Assert.NotNull(retained);
        }
        finally
        {
            PortableWorkbookMigrationRecoveryStore.Delete(targetName);
        }
    }

    [Fact]
    public void ConfigurationRejectsRemotePlaintextTransportAndAcceptsLoopback()
    {
        Assert.False(SupabaseHostedSyncConfiguration.TryCreate(
            "http://example.com",
            "anon",
            "Excel",
            out _,
            out _));
        Assert.True(SupabaseHostedSyncConfiguration.TryCreate(
            "http://127.0.0.1:54321",
            "anon",
            "Excel",
            out var local,
            out _));
        Assert.Equal("Excel", local?.PlatformLabel);
    }

    [Theory]
    [InlineData("https://pilot.supabase.co/auth/v1/verify?token=hashed-magic-link-token&type=magiclink&redirect_to=http%3A%2F%2Flocalhost%3A3000", "hashed-magic-link-token", "magiclink")]
    [InlineData("https://pilot.supabase.co/auth/v1/verify?token_hash=hashed-email-token&type=email", "hashed-email-token", "email")]
    public async Task ClientAcceptsUnusedSupabaseSignInLinkAsTokenHash(
        string signInLink,
        string expectedTokenHash,
        string expectedType)
    {
        var handler = new WorkbookConnectionHandler(Guid.NewGuid(), Guid.NewGuid(), PortableLogbookKey.Generate());
        using var http = new HttpClient(handler);
        var configuration = new SupabaseHostedSyncConfiguration(
            new Uri("https://pilot.supabase.co"),
            "public-anon-key",
            "Excel Test Device");
        using var client = new SupabaseWorkbookConnectionClient(configuration, http);

        await client.StartEmailSignInAsync("pilot@example.com");
        await client.CompleteEmailSignInAsync(signInLink);

        var verify = handler.Requests.Single(request => request.Path == "/auth/v1/verify");
        using var json = JsonDocument.Parse(verify.Body);
        Assert.Equal(expectedTokenHash, json.RootElement.GetProperty("token_hash").GetString());
        Assert.Equal(expectedType, json.RootElement.GetProperty("type").GetString());
        Assert.False(json.RootElement.TryGetProperty("email", out _));
        Assert.False(json.RootElement.TryGetProperty("token", out _));
    }

    [Fact]
    public async Task ClientUnwrapsOutlookSafeLinkBeforeValidatingSupabaseProject()
    {
        var handler = new WorkbookConnectionHandler(Guid.NewGuid(), Guid.NewGuid(), PortableLogbookKey.Generate());
        using var http = new HttpClient(handler);
        var configuration = new SupabaseHostedSyncConfiguration(
            new Uri("https://pilot.supabase.co"),
            "public-anon-key",
            "Excel Test Device");
        using var client = new SupabaseWorkbookConnectionClient(configuration, http);
        const string safeLink =
            "https://nam01.safelinks.protection.outlook.com/?url=https%3A%2F%2Fpilot.supabase.co%2Fauth%2Fv1%2Fverify%3Ftoken%3Dsafe-link-token%26type%3Dmagiclink%26redirect_to%3Dhttp%3A%2F%2Flocalhost%3A3000&data=redacted&reserved=0";

        await client.StartEmailSignInAsync("pilot@example.com");
        await client.CompleteEmailSignInAsync(safeLink);

        var verify = handler.Requests.Single(request => request.Path == "/auth/v1/verify");
        using var json = JsonDocument.Parse(verify.Body);
        Assert.Equal("safe-link-token", json.RootElement.GetProperty("token_hash").GetString());
        Assert.Equal("magiclink", json.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ClientRejectsSignInLinkForAnotherSupabaseProject()
    {
        var handler = new WorkbookConnectionHandler(Guid.NewGuid(), Guid.NewGuid(), PortableLogbookKey.Generate());
        using var http = new HttpClient(handler);
        var configuration = new SupabaseHostedSyncConfiguration(
            new Uri("https://pilot.supabase.co"),
            "public-anon-key",
            "Excel Test Device");
        using var client = new SupabaseWorkbookConnectionClient(configuration, http);

        await client.StartEmailSignInAsync("pilot@example.com");
        var error = await Assert.ThrowsAsync<HostedSignInException>(() =>
            client.CompleteEmailSignInAsync(
                "https://attacker.supabase.co/auth/v1/verify?token=wrong-project&type=magiclink"));

        Assert.Equal(HostedSignInFailureReason.InvalidVerificationCode, error.Reason);
        Assert.DoesNotContain(handler.Requests, request => request.Path == "/auth/v1/verify");
    }

    [Fact]
    public async Task ActivationFailureIsNotMisreportedAsAccountRecoveryFailure()
    {
        var handler = new WorkbookConnectionHandler(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PortableLogbookKey.Generate(),
            rejectActivation: true);
        using var http = new HttpClient(handler);
        var configuration = new SupabaseHostedSyncConfiguration(
            new Uri("https://pilot.supabase.co"),
            "public-anon-key",
            "Excel Test Device");
        using var client = new SupabaseWorkbookConnectionClient(configuration, http);

        await client.StartEmailSignInAsync("pilot@example.com");
        await client.CompleteEmailSignInAsync("123456");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ActivateWorkbookDeviceAsync(LogbookId.New(), DeviceId.New()));

        Assert.Contains("activation", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("account recovery", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class WorkbookConnectionHandler(
        Guid accountId,
        Guid logbookId,
        PortableLogbookKey packageKey,
        bool rejectActivation = false,
        bool invalidIngressFingerprint = false) : HttpMessageHandler
    {
        private readonly Guid migrationId = Guid.NewGuid();
        private readonly Guid migrationDeviceId = Guid.NewGuid();
        private readonly RSA ingressKey = RSA.Create(2048);

        public List<RecordedRequest> Requests { get; } = [];

        public bool EnrollmentMatchedPackageKey { get; private set; }

        public List<string> EnrollmentPackageKeyFingerprints { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.RequestUri?.AbsolutePath ?? string.Empty,
                body,
                request.Headers.Authorization?.Scheme));

            var path = request.RequestUri?.AbsolutePath;
            if (path == "/auth/v1/otp")
            {
                return Json("{}");
            }

            if (path == "/auth/v1/verify")
            {
                return Json($$"""
                    {
                      "access_token": "access-token",
                      "refresh_token": "refresh-token",
                      "expires_in": 3600,
                      "user": { "id": "{{accountId:D}}" }
                    }
                    """);
            }

            if (path == "/rest/v1/logbook_memberships")
            {
                return Json($$"""
                    [{
                      "logbook_id": "{{logbookId:D}}",
                      "role": "owner",
                      "logbooks": {
                        "display_name": "Test Logbook",
                        "current_schema_version": 2,
                        "operation_format_version": 1
                      }
                    }]
                    """);
            }

            if (path?.StartsWith("/rest/v1/rpc/", StringComparison.Ordinal) == true)
            {
                var status = path.EndsWith("/fail_workbook_migration", StringComparison.Ordinal)
                    ? "failed"
                    : path.EndsWith("/complete_workbook_migration", StringComparison.Ordinal)
                        ? "completed"
                        : "pending";
                return Json(MigrationJson(status, body));
            }

            if (path == "/functions/v1/recovery-envelope")
            {
                using var json = JsonDocument.Parse(body);
                var action = json.RootElement.GetProperty("action").GetString();
                if (action == "configuration")
                {
                    var ingressPublicKey = ingressKey.ExportSubjectPublicKeyInfo();
                    var fingerprint = invalidIngressFingerprint
                        ? new string('0', 64)
                        : Convert.ToHexString(SHA256.HashData(ingressPublicKey)).ToLowerInvariant();
                    return Json(JsonSerializer.Serialize(new
                    {
                        publicKey = Convert.ToBase64String(ingressPublicKey),
                        fingerprint,
                        algorithm = PortableWorkbookRecoveryKeyPair.AlgorithmName,
                        keyVersionId = "test-v1"
                    }));
                }

                if (action == "enroll")
                {
                    var encrypted = Convert.FromBase64String(
                        json.RootElement.GetProperty("wrappedPackageKey").GetString()!);
                    var plaintext = ingressKey.Decrypt(encrypted, RSAEncryptionPadding.OaepSHA256);
                    var expected = packageKey.ToBytes();
                    try
                    {
                        EnrollmentMatchedPackageKey = CryptographicOperations.FixedTimeEquals(
                            plaintext,
                            expected);
                        EnrollmentPackageKeyFingerprints.Add(
                            Convert.ToHexString(SHA256.HashData(plaintext)).ToLowerInvariant());
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(plaintext);
                        CryptographicOperations.ZeroMemory(expected);
                    }

                    return Json("{\"enrolled\":true,\"keyVersionId\":\"test-v1\"}");
                }

                if (action == "activate")
                {
                    if (rejectActivation)
                    {
                        return new HttpResponseMessage(HttpStatusCode.BadRequest)
                        {
                            Content = new StringContent(
                                "{\"code\":\"RECOVERY_SERVICE_FAILED\"}",
                                Encoding.UTF8,
                                "application/json")
                        };
                    }

                    return Json("{\"activated\":true}");
                }

                var publicKey = Convert.FromBase64String(
                    json.RootElement.GetProperty("devicePublicKey").GetString()!);
                using var rsa = RSA.Create();
                rsa.ImportSubjectPublicKeyInfo(publicKey, out _);
                var wrapped = rsa.Encrypt(packageKey.ToBytes(), RSAEncryptionPadding.OaepSHA256);
                return Json(JsonSerializer.Serialize(new
                {
                    wrappedKey = Convert.ToBase64String(wrapped),
                    algorithm = PortableWorkbookRecoveryKeyPair.AlgorithmName,
                    keyVersionId = "test-v1"
                }));
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ingressKey.Dispose();
            }

            base.Dispose(disposing);
        }

        private string MigrationJson(string status, string requestBody)
        {
            using var request = JsonDocument.Parse(string.IsNullOrWhiteSpace(requestBody) ? "{}" : requestBody);
            var sourceFingerprint = request.RootElement.TryGetProperty("p_source_fingerprint", out var source)
                ? source.GetString()
                : new string('a', 64);
            var failureCode = status == "failed"
                ? request.RootElement.GetProperty("p_failure_code").GetString()
                : null;
            var receiptHash = status == "completed"
                ? request.RootElement.GetProperty("p_verification_receipt_hash").GetString()
                : null;
            var verifiedCount = status == "completed"
                ? request.RootElement.GetProperty("p_expected_operation_count").GetInt32()
                : (int?)null;
            return JsonSerializer.Serialize(new
            {
                migration_id = migrationId,
                account_id = accountId,
                logbook_id = logbookId,
                device_id = migrationDeviceId,
                source_fingerprint = sourceFingerprint,
                status,
                attempt_count = status == "pending" ? 1 : 2,
                expected_operation_count = verifiedCount,
                verified_operation_count = verifiedCount,
                verification_receipt_hash = receiptHash,
                failure_code = failureCode,
                started_at = "2026-08-28T00:00:00Z",
                updated_at = "2026-08-28T00:01:00Z",
                completed_at = status == "completed" ? "2026-08-28T00:01:00Z" : null,
                failed_at = status == "failed" ? "2026-08-28T00:01:00Z" : null
            });
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
    }

    private sealed record RecordedRequest(string Path, string Body, string? AuthorizationScheme);
}
