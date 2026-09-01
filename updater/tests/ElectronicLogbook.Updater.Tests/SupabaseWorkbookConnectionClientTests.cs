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

        var started = await client.StartEmailSignInAsync("preview@example.com");
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
        Assert.Equal("preview@example.com", verifyJson.RootElement.GetProperty("email").GetString());
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
    public async Task GoogleSignInUsesSystemBrowserPkceAndReturnsExactAccountEmail()
    {
        var accountId = Guid.NewGuid();
        var handler = new WorkbookConnectionHandler(
            accountId,
            Guid.NewGuid(),
            PortableLogbookKey.Generate());
        using var http = new HttpClient(handler);
        var configuration = new SupabaseHostedSyncConfiguration(
            new Uri("https://preview.supabase.co"),
            "public-anon-key",
            "Windows Migration");
        using var client = new SupabaseWorkbookConnectionClient(configuration, http);
        var browser = new RecordingGoogleOAuthFlow("code=one-time-google-code");

        var session = await client.SignInWithGoogleAsync(browser);

        Assert.Equal("acct_" + accountId.ToString("N"), session.AccountId.Value);
        Assert.Equal("preview@example.com", session.AccountDisplay);
        Assert.NotNull(browser.AuthorizationUri);
        var authorization = QueryValues(browser.AuthorizationUri!.Query);
        Assert.Equal("google", authorization["provider"]);
        Assert.Equal("s256", authorization["code_challenge_method"]);
        Assert.Equal(browser.CallbackUri, new Uri(authorization["redirect_to"]));
        Assert.DoesNotContain("public-anon-key", browser.AuthorizationUri.AbsoluteUri, StringComparison.Ordinal);

        var exchange = handler.Requests.Single(request => request.Path == "/auth/v1/token");
        Assert.Equal("grant_type=pkce", exchange.Query.TrimStart('?'));
        Assert.Null(exchange.AuthorizationScheme);
        using var payload = JsonDocument.Parse(exchange.Body);
        Assert.Equal("one-time-google-code", payload.RootElement.GetProperty("auth_code").GetString());
        var verifier = payload.RootElement.GetProperty("code_verifier").GetString();
        Assert.NotNull(verifier);
        Assert.InRange(verifier.Length, 43, 128);
        var expectedChallenge = SystemBrowserGoogleOAuthFlow.Base64UrlEncode(
            SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        Assert.Equal(expectedChallenge, authorization["code_challenge"]);
        Assert.DoesNotContain(verifier, browser.AuthorizationUri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GoogleSignInCancellationDoesNotExchangeOrCreateSession()
    {
        var handler = new WorkbookConnectionHandler(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PortableLogbookKey.Generate());
        using var http = new HttpClient(handler);
        var configuration = new SupabaseHostedSyncConfiguration(
            new Uri("https://preview.supabase.co"),
            "public-anon-key",
            "Windows Migration");
        using var client = new SupabaseWorkbookConnectionClient(configuration, http);
        var browser = new RecordingGoogleOAuthFlow("error=access_denied");

        var error = await Assert.ThrowsAsync<HostedSignInException>(() =>
            client.SignInWithGoogleAsync(browser));

        Assert.Equal(HostedSignInFailureReason.InvitationRequired, error.Reason);
        Assert.DoesNotContain("Supabase", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("database", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("code", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(handler.Requests, request => request.Path == "/auth/v1/token");
        await Assert.ThrowsAsync<HostedSignInException>(() => client.DiscoverActiveLogbooksAsync());
    }

    [Fact]
    public async Task WorkbookMigrationLifecycleUsesNarrowAuthenticatedRpcCallsAndStableResources()
    {
        var accountId = Guid.NewGuid();
        var logbookId = Guid.NewGuid();
        var handler = new WorkbookConnectionHandler(accountId, logbookId, PortableLogbookKey.Generate());
        using var http = new HttpClient(handler);
        var configuration = new SupabaseHostedSyncConfiguration(
            new Uri("https://preview.supabase.co"),
            "public-anon-key",
            "Windows Migration");
        using var client = new SupabaseWorkbookConnectionClient(configuration, http);
        var sourceFingerprint = new string('a', 64);
        var receiptHash = new string('e', 64);

        await client.StartEmailSignInAsync("preview@example.com");
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
    public async Task WorkbookMigrationConfigurationUploadAndReadbackAreScopedAndRetryStable()
    {
        var accountId = Guid.NewGuid();
        var logbookId = Guid.NewGuid();
        var handler = new WorkbookConnectionHandler(accountId, logbookId, PortableLogbookKey.Generate());
        using var http = new HttpClient(handler);
        var configuration = new SupabaseHostedSyncConfiguration(
            new Uri("https://preview.supabase.co"),
            "public-anon-key",
            "Windows Migration");
        using var client = new SupabaseWorkbookConnectionClient(configuration, http);

        await client.StartEmailSignInAsync("preview@example.com");
        await client.CompleteEmailSignInAsync("123456");
        var migration = await client.BeginWorkbookMigrationAsync(new string('a', 64), "Migrated Logbook");
        var revision = new HostedConfigurationRevisionUpload(
            new RevisionId("rev_" + Guid.NewGuid().ToString("N")),
            migration.DeviceId,
            DateTimeOffset.Parse("2026-08-29T01:02:03Z"),
            SchemaVersion: 2,
            PayloadCiphertext: new string('c', 32),
            PayloadNonce: new string('n', 24),
            PayloadTag: new string('t', 32),
            PayloadHash: new string('d', 64));

        var appended = await client.AppendWorkbookConfigurationRevisionAsync(migration, revision);
        var retry = await client.AppendWorkbookConfigurationRevisionAsync(migration, revision);
        var readback = await client.ReadWorkbookConfigurationRevisionsAsync(migration);

        Assert.Equal(appended, retry);
        Assert.Equal(revision.RevisionId, appended.RevisionId);
        Assert.Equal(migration.DeviceId, appended.DeviceId);
        Assert.Equal(2, appended.SchemaVersion);
        Assert.Equal(appended, Assert.Single(readback.Revisions));
        Assert.Equal(1, readback.ThroughHostedRevision);
        Assert.False(readback.HasMore);

        var uploads = handler.Requests.Where(request =>
            request.Path.EndsWith("/append_hosted_configuration_revision", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, uploads.Length);
        Assert.Equal(uploads[0].Body, uploads[1].Body);
        using var uploadJson = JsonDocument.Parse(uploads[0].Body);
        Assert.Equal(logbookId.ToString("D"), uploadJson.RootElement.GetProperty("p_logbook_id").GetString());
        Assert.Equal(1, uploadJson.RootElement.GetProperty("p_configuration_format_version").GetInt32());
        Assert.DoesNotContain("custom field", uploads[0].Body, StringComparison.OrdinalIgnoreCase);
        Assert.All(
            handler.Requests.Where(request => request.Path.Contains("configuration_revision", StringComparison.Ordinal)),
            request => Assert.Equal("Bearer", request.AuthorizationScheme));

        var wrongDevice = revision with { DeviceId = DeviceId.New() };
        var requestCount = handler.Requests.Count;
        var scopeError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.AppendWorkbookConfigurationRevisionAsync(migration, wrongDevice));
        Assert.Contains("temporary spreadsheet device", scopeError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(requestCount, handler.Requests.Count);
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
            new Uri("https://preview.supabase.co"),
            "public-anon-key",
            "Windows Migration");
        using var client = new SupabaseWorkbookConnectionClient(configuration, http);

        await client.StartEmailSignInAsync("preview@example.com");
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
            new Uri("https://preview.supabase.co"),
            "public-anon-key",
            "Windows Migration");
        using var client = new SupabaseWorkbookConnectionClient(configuration, http);

        await client.StartEmailSignInAsync("preview@example.com");
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
    public async Task CompletedMigrationRetryRemovesCredentialRetainedAfterLostCompletionResponse()
    {
        var handler = new WorkbookConnectionHandler(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PortableLogbookKey.Generate());
        using var http = new HttpClient(handler);
        var configuration = new SupabaseHostedSyncConfiguration(
            new Uri("https://preview.supabase.co"),
            "public-anon-key",
            "Windows Migration");
        using var client = new SupabaseWorkbookConnectionClient(configuration, http);
        var coordinator = new WorkbookMigrationRecoveryCoordinator(client);
        var sourceFingerprint = new string('a', 64);

        await client.StartEmailSignInAsync("preview@example.com");
        await client.CompleteEmailSignInAsync("123456");
        var migration = await client.BeginWorkbookMigrationAsync(
            sourceFingerprint,
            "Migrated Logbook");
        var targetName = PortableWorkbookMigrationRecoveryStore.CreateTargetName(
            migration.LogbookId,
            migration.DeviceId);

        try
        {
            await client.CompleteWorkbookMigrationAsync(
                migration.MigrationId,
                expectedOperationCount: 1,
                verificationReceiptHash: new string('e', 64));

            using (PortableWorkbookMigrationRecoveryStore.LoadOrCreate(
                migration.LogbookId,
                migration.DeviceId))
            {
                using var retained = PortableWorkbookMigrationRecoveryStore.Load(targetName);
                Assert.NotNull(retained);
            }

            using var resumed = await coordinator.BeginOrResumeAsync(
                sourceFingerprint,
                "Migrated Logbook");

            Assert.True(resumed.IsAlreadyCompleted);
            Assert.Null(resumed.RecoveryMaterial);
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
            new Uri("https://preview.supabase.co"),
            "public-anon-key",
            "Windows Migration");
        using var client = new SupabaseWorkbookConnectionClient(configuration, http);
        var coordinator = new WorkbookMigrationRecoveryCoordinator(client);
        var sourceFingerprint = new string('a', 64);

        await client.StartEmailSignInAsync("preview@example.com");
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
            new Uri("https://preview.supabase.co"),
            "public-anon-key",
            "Windows Migration");
        using var client = new SupabaseWorkbookConnectionClient(configuration, http);

        await client.StartEmailSignInAsync("preview@example.com");
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
    [InlineData("https://preview.supabase.co/auth/v1/verify?token=hashed-magic-link-token&type=magiclink&redirect_to=http%3A%2F%2Flocalhost%3A3000", "hashed-magic-link-token", "magiclink")]
    [InlineData("https://preview.supabase.co/auth/v1/verify?token_hash=hashed-email-token&type=email", "hashed-email-token", "email")]
    public async Task ClientAcceptsUnusedSupabaseSignInLinkAsTokenHash(
        string signInLink,
        string expectedTokenHash,
        string expectedType)
    {
        var handler = new WorkbookConnectionHandler(Guid.NewGuid(), Guid.NewGuid(), PortableLogbookKey.Generate());
        using var http = new HttpClient(handler);
        var configuration = new SupabaseHostedSyncConfiguration(
            new Uri("https://preview.supabase.co"),
            "public-anon-key",
            "Excel Test Device");
        using var client = new SupabaseWorkbookConnectionClient(configuration, http);

        await client.StartEmailSignInAsync("preview@example.com");
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
            new Uri("https://preview.supabase.co"),
            "public-anon-key",
            "Excel Test Device");
        using var client = new SupabaseWorkbookConnectionClient(configuration, http);
        const string safeLink =
            "https://nam01.safelinks.protection.outlook.com/?url=https%3A%2F%2Fpreview.supabase.co%2Fauth%2Fv1%2Fverify%3Ftoken%3Dsafe-link-token%26type%3Dmagiclink%26redirect_to%3Dhttp%3A%2F%2Flocalhost%3A3000&data=redacted&reserved=0";

        await client.StartEmailSignInAsync("preview@example.com");
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
            new Uri("https://preview.supabase.co"),
            "public-anon-key",
            "Excel Test Device");
        using var client = new SupabaseWorkbookConnectionClient(configuration, http);

        await client.StartEmailSignInAsync("preview@example.com");
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
            new Uri("https://preview.supabase.co"),
            "public-anon-key",
            "Excel Test Device");
        using var client = new SupabaseWorkbookConnectionClient(configuration, http);

        await client.StartEmailSignInAsync("preview@example.com");
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
        private string migrationStatus = "pending";
        private string migrationSourceFingerprint = new string('a', 64);
        private string? migrationFailureCode;
        private string? migrationReceiptHash;
        private int? migrationVerifiedCount;

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
                request.RequestUri?.Query ?? string.Empty,
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

            if (path == "/auth/v1/token")
            {
                return Json($$"""
                    {
                      "access_token": "google-access-token",
                      "refresh_token": "google-refresh-token",
                      "expires_in": 3600,
                      "user": {
                        "id": "{{accountId:D}}",
                        "email": "preview@example.com"
                      }
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

            if (path?.EndsWith("/append_hosted_configuration_revision", StringComparison.Ordinal) == true)
            {
                return Json(ConfigurationRevisionJson(body));
            }

            if (path?.EndsWith("/read_hosted_configuration_revisions", StringComparison.Ordinal) == true)
            {
                var upload = Requests.Last(request =>
                    request.Path.EndsWith("/append_hosted_configuration_revision", StringComparison.Ordinal));
                return Json("[" + ConfigurationRevisionJson(upload.Body) + "]");
            }

            if (path?.StartsWith("/rest/v1/rpc/", StringComparison.Ordinal) == true)
            {
                using var requestJson = JsonDocument.Parse(
                    string.IsNullOrWhiteSpace(body) ? "{}" : body);
                if (requestJson.RootElement.TryGetProperty("p_source_fingerprint", out var source))
                {
                    migrationSourceFingerprint = source.GetString() ?? migrationSourceFingerprint;
                    if (migrationStatus == "failed")
                    {
                        migrationStatus = "pending";
                        migrationFailureCode = null;
                    }
                }
                if (path.EndsWith("/fail_workbook_migration", StringComparison.Ordinal))
                {
                    migrationStatus = "failed";
                    migrationFailureCode = requestJson.RootElement.GetProperty("p_failure_code").GetString();
                }
                else if (path.EndsWith("/complete_workbook_migration", StringComparison.Ordinal))
                {
                    migrationStatus = "completed";
                    migrationFailureCode = null;
                    migrationReceiptHash = requestJson.RootElement
                        .GetProperty("p_verification_receipt_hash")
                        .GetString();
                    migrationVerifiedCount = requestJson.RootElement
                        .GetProperty("p_expected_operation_count")
                        .GetInt32();
                }
                return Json(MigrationJson());
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

        private string MigrationJson()
        {
            return JsonSerializer.Serialize(new
            {
                migration_id = migrationId,
                account_id = accountId,
                logbook_id = logbookId,
                device_id = migrationDeviceId,
                source_fingerprint = migrationSourceFingerprint,
                status = migrationStatus,
                attempt_count = migrationStatus == "pending" ? 1 : 2,
                expected_operation_count = migrationVerifiedCount,
                verified_operation_count = migrationVerifiedCount,
                verification_receipt_hash = migrationReceiptHash,
                failure_code = migrationFailureCode,
                started_at = "2026-08-28T00:00:00Z",
                updated_at = "2026-08-28T00:01:00Z",
                completed_at = migrationStatus == "completed" ? "2026-08-28T00:01:00Z" : null,
                failed_at = migrationStatus == "failed" ? "2026-08-28T00:01:00Z" : null
            });
        }

        private string ConfigurationRevisionJson(string requestBody)
        {
            using var request = JsonDocument.Parse(requestBody);
            var root = request.RootElement;
            return JsonSerializer.Serialize(new
            {
                revision = 1,
                configuration_id = root.GetProperty("p_configuration_id").GetString(),
                portable_revision_id = root.GetProperty("p_portable_revision_id").GetString(),
                author_device_id = migrationDeviceId,
                configuration_format_version = root.GetProperty("p_configuration_format_version").GetInt32(),
                payload_ciphertext = root.GetProperty("p_payload_ciphertext").GetString(),
                payload_nonce = root.GetProperty("p_payload_nonce").GetString(),
                payload_tag = root.GetProperty("p_payload_tag").GetString(),
                payload_hash = root.GetProperty("p_payload_hash").GetString(),
                client_created_at = root.GetProperty("p_client_created_at").GetString(),
                received_at = "2026-08-29T01:02:04Z",
                highest_revision = 1,
                has_more = false
            });
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
    }

    private static Dictionary<string, string> QueryValues(string query) =>
        query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Split('=', 2))
            .ToDictionary(
                item => Uri.UnescapeDataString(item[0]),
                item => item.Length == 2 ? Uri.UnescapeDataString(item[1]) : string.Empty,
                StringComparer.OrdinalIgnoreCase);

    private sealed class RecordingGoogleOAuthFlow(string callbackQuery) : ISystemBrowserGoogleOAuthFlow
    {
        public Uri? AuthorizationUri { get; private set; }

        public Uri? CallbackUri { get; private set; }

        public Task<Uri> AuthorizeAsync(
            Func<Uri, Uri> createAuthorizationUri,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallbackUri = new Uri("http://127.0.0.1:49152/flightlogx-auth/test-token/");
            AuthorizationUri = createAuthorizationUri(CallbackUri);
            return Task.FromResult(new Uri(CallbackUri, "?" + callbackQuery));
        }
    }

    private sealed record RecordedRequest(
        string Path,
        string Query,
        string Body,
        string? AuthorizationScheme);
}
