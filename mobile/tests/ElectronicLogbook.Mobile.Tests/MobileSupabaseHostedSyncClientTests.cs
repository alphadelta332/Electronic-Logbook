using System.Net;
using System.Text;
using System.Text.Json;
using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;
using Microsoft.JSInterop;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileSupabaseHostedSyncClientTests
{
    [Fact]
    public async Task RecoveryPreflightValidatesConfigAccessUserAccountAndExistingDeviceWithoutRefreshOrRegistration()
    {
        var handler = new RecordingHandler
        {
            ConfigJson = Config(CreateJwt(new { role = "anon", @ref = "pilot", exp = 4_102_444_800L }))
        };
        var jsRuntime = new MemoryJsRuntime();
        await new BrowserHostedCredentialStore(jsRuntime).SaveAsync(new BrowserHostedCredential(
            new HostedAccountId("acct_10000000000000000000000000000001"),
            new DeviceId("dev_40000000000000000000000000000001"),
            CreateJwt(new { iss = "https://pilot.supabase.co/auth/v1", exp = 4_102_444_800L }),
            "retained-refresh-token",
            DateTimeOffset.Parse("2099-01-01T00:00:00Z")));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://app.local/") };
        var client = new MobileSupabaseHostedSyncClient(http, new BrowserHostedCredentialStore(jsRuntime), new ManualSyncClock(DateTimeOffset.Parse("2026-08-07T00:00:00Z")));

        await client.ValidateConfigAsync();
        var credential = await client.LoadCredentialSnapshotAsync();
        await client.ValidateAccessTokenAsync();
        var user = await client.ReadAuthUserAsync();
        var account = await client.ReadAccountAsync();
        var device = await client.ReadDeviceAsync();

        Assert.Equal(MobileCredentialState.Registered, credential.State);
        Assert.Equal(credential.AccountId, user.AccountId);
        Assert.True(account is { Exists: true, IsActive: true, MatchesCredential: true });
        Assert.True(device is { Exists: true, IsActive: true, MatchesAccountAndCredential: true });
        Assert.DoesNotContain(handler.Requests, request => request.Path == "/auth/v1/token");
        Assert.DoesNotContain(handler.Requests, request => request.Path == "/auth/v1/otp");
        Assert.DoesNotContain(handler.Requests, request => request.Path == "/rest/v1/rpc/accept_hosted_invitation");
    }

    [Theory]
    [InlineData("other", 4102444800L, "ANON_KEY_PROJECT_MISMATCH")]
    [InlineData("pilot", 1L, "ANON_KEY_EXPIRED")]
    public async Task RecoveryConfigRejectsWrongProjectAndExpiredAnonCredentials(string projectRef, long expiry, string expectedCode)
    {
        var handler = new RecordingHandler { ConfigJson = Config(CreateJwt(new { role = "anon", @ref = projectRef, exp = expiry })) };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://app.local/") };
        var client = new MobileSupabaseHostedSyncClient(http, new BrowserHostedCredentialStore(new MemoryJsRuntime()), new ManualSyncClock(DateTimeOffset.Parse("2026-08-07T00:00:00Z")));

        var error = await Assert.ThrowsAsync<MobileHostedDiagnosticException>(async () => await client.ValidateConfigAsync());

        Assert.Equal(expectedCode, error.ErrorCode);
    }

    [Fact]
    public async Task RecoveryPreflightUsesRetainedRefreshCredentialOnlyWhenAccessIsExpired()
    {
        var handler = new RecordingHandler { ConfigJson = Config(CreateJwt(new { role = "anon", @ref = "pilot", exp = 4_102_444_800L })) };
        var store = new BrowserHostedCredentialStore(new MemoryJsRuntime());
        await store.SaveAsync(new BrowserHostedCredential(
            new HostedAccountId("acct_10000000000000000000000000000001"),
            new DeviceId("dev_40000000000000000000000000000001"),
            CreateJwt(new { iss = "https://pilot.supabase.co/auth/v1", exp = 1L }),
            "retained-refresh-token",
            DateTimeOffset.Parse("2020-01-01T00:00:00Z")));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://app.local/") };
        var client = new MobileSupabaseHostedSyncClient(http, store, new ManualSyncClock(DateTimeOffset.Parse("2026-08-07T00:00:00Z")));

        await client.ValidateAccessTokenAsync();

        Assert.Single(handler.Requests, request => request.Path == "/auth/v1/token");
        Assert.Equal("retained-refresh-token", (await store.LoadAsync())?.RefreshToken);
    }

    [Fact]
    public async Task RecoveryPreflightRejectsAccessTokenFromAnotherProject()
    {
        var handler = new RecordingHandler { ConfigJson = Config(CreateJwt(new { role = "anon", @ref = "pilot", exp = 4_102_444_800L })) };
        var store = new BrowserHostedCredentialStore(new MemoryJsRuntime());
        await store.SaveAsync(ValidCredential() with
        {
            AccessToken = CreateJwt(new { iss = "https://other.supabase.co/auth/v1", exp = 4_102_444_800L })
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://app.local/") };
        var client = new MobileSupabaseHostedSyncClient(http, store, new ManualSyncClock(DateTimeOffset.Parse("2026-08-07T00:00:00Z")));

        var error = await Assert.ThrowsAsync<MobileHostedDiagnosticException>(async () => await client.ValidateAccessTokenAsync());

        Assert.Equal("ACCESS_TOKEN_PROJECT_MISMATCH", error.ErrorCode);
    }

    [Fact]
    public async Task RecoveryPreflightReportsRejectedAccessAndSanitizedSupabaseError()
    {
        var handler = new RecordingHandler { ConfigJson = Config(CreateJwt(new { role = "anon", @ref = "pilot", exp = 4_102_444_800L })) };
        handler.ResponseOverrides["/auth/v1/user"] = (HttpStatusCode.Unauthorized, """{"error_code":"bad_jwt","msg":"token owner@example.com rejected"}""");
        var store = new BrowserHostedCredentialStore(new MemoryJsRuntime());
        await store.SaveAsync(ValidCredential());
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://app.local/") };
        var client = new MobileSupabaseHostedSyncClient(http, store, new ManualSyncClock(DateTimeOffset.Parse("2026-08-07T00:00:00Z")));

        var error = await Assert.ThrowsAsync<MobileHostedDiagnosticException>(async () => await client.ReadAuthUserAsync());

        Assert.Equal("ACCESS_TOKEN_REJECTED", error.ErrorCode);
        Assert.Equal(HttpStatusCode.Unauthorized, error.HttpStatus);
        Assert.Equal("bad_jwt", error.SupabaseCode);
        Assert.DoesNotContain("owner@example.com", error.SupabaseMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectedRefreshCredentialIsRetainedForDiagnostics()
    {
        var handler = new RecordingHandler { ConfigJson = Config(CreateJwt(new { role = "anon", @ref = "pilot", exp = 4_102_444_800L })) };
        handler.ResponseOverrides["/auth/v1/token"] = (HttpStatusCode.BadRequest, """{"error_code":"refresh_token_not_found","msg":"invalid refresh"}""");
        var store = new BrowserHostedCredentialStore(new MemoryJsRuntime());
        await store.SaveAsync(ValidCredential());
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://app.local/") };
        var client = new MobileSupabaseHostedSyncClient(http, store, new ManualSyncClock(DateTimeOffset.Parse("2026-08-07T00:00:00Z")));

        await Assert.ThrowsAsync<HostedSignInException>(async () => await client.RefreshAsync());

        Assert.NotNull(await store.LoadAsync());
    }

    [Fact]
    public async Task RecoveryEnvelopeContractMapsConfigurationEnrollmentAndRestore()
    {
        var handler = new RecordingHandler();
        var store = new BrowserHostedCredentialStore(new MemoryJsRuntime());
        await store.SaveAsync(ValidCredential());
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://app.local/") };
        IMobileRecoveryEnvelopeService service = new MobileSupabaseHostedSyncClient(
            http,
            store,
            new ManualSyncClock(DateTimeOffset.Parse("2026-08-07T00:00:00Z")));
        var logbookId = new LogbookId("log_20000000000000000000000000000001");
        var deviceId = new DeviceId("dev_40000000000000000000000000000001");
        var deviceKey = new MobileRecoveryDeviceKey(
            "device-public-key",
            new string('a', 64),
            "RSA-OAEP-256");

        var configuration = await service.GetConfigurationAsync();
        var enrollment = await service.EnrollAsync(new MobileRecoveryEnvelopeEnrollmentRequest(
            logbookId,
            deviceId,
            deviceKey,
            "ingress-wrapped-package-key",
            configuration.KeyVersionId));
        var setupStatus = await service.GetRecoverySetupStatusAsync(
            new MobileRecoverySetupStatusRequest(logbookId, deviceId));
        var restore = await service.RestoreAsync(new MobileRecoveryEnvelopeRestoreRequest(
            logbookId,
            deviceId,
            deviceKey,
            "Pixel 8 Pro"));
        var codeEnvelope = new MobileRecoveryCodeEnvelopePayload(
            "code-ciphertext",
            "code-nonce",
            "code-salt",
            MobileRecoveryCodeEnvelope.Algorithm,
            MobileRecoveryCodeEnvelope.KeyVersionId);
        var codeEnrollment = await service.EnrollRecoveryCodeAsync(
            new MobileRecoveryCodeEnrollmentRequest(logbookId, deviceId, codeEnvelope));
        var codeRestore = await service.RestoreWithRecoveryCodeAsync(
            new MobileRecoveryCodeRestoreRequest(logbookId, deviceId, "Pixel 8 Pro", deviceKey));
        var activation = await service.ActivateAsync(new MobileRecoveryDeviceActivationRequest(logbookId, deviceId));

        Assert.Equal("service-public-key", configuration.PublicKey);
        Assert.True(enrollment.Enrolled);
        Assert.True(setupStatus.ManagedEnvelopeConfigured);
        Assert.Equal("device-wrapped-package-key", restore.WrappedKey);
        Assert.True(codeEnrollment.Enrolled);
        Assert.Equal("code-ciphertext", codeRestore.Ciphertext);
        Assert.True(activation.Activated);
        var requests = handler.Requests
            .Where(request => request.Path == "/functions/v1/recovery-envelope")
            .ToArray();
        Assert.Equal(["configuration", "enroll", "status", "restore", "enroll-code", "restore-code", "activate"], requests.Select(request => request.Body.GetProperty("action").GetString()));
        Assert.All(requests, request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("anon-key", request.ApiKey);
            Assert.Equal("Bearer " + ValidCredential().AccessToken, request.Authorization);
        });
        Assert.False(requests[0].Body.TryGetProperty("logbookId", out _));
        Assert.Equal("20000000-0000-0000-0000-000000000001", requests[1].Body.GetProperty("logbookId").GetString());
        Assert.Equal("40000000-0000-0000-0000-000000000001", requests[1].Body.GetProperty("deviceId").GetString());
        Assert.Equal("ingress-wrapped-package-key", requests[1].Body.GetProperty("wrappedPackageKey").GetString());
        Assert.False(requests[2].Body.TryGetProperty("wrappedPackageKey", out _));
        Assert.Equal("Pixel 8 Pro", requests[3].Body.GetProperty("platformLabel").GetString());
        Assert.Equal("android", requests[3].Body.GetProperty("deviceType").GetString());
        Assert.Equal("code-ciphertext", requests[4].Body.GetProperty("recoveryCiphertext").GetString());
        Assert.False(requests[4].Body.TryGetProperty("recoveryCode", out _));
        Assert.Equal("Pixel 8 Pro", requests[5].Body.GetProperty("platformLabel").GetString());
        Assert.Equal("android", requests[5].Body.GetProperty("deviceType").GetString());
        Assert.Equal("device-public-key", requests[5].Body.GetProperty("devicePublicKey").GetString());
        Assert.False(requests[6].Body.TryGetProperty("devicePublicKey", out _));
    }

    [Fact]
    public async Task RecoveryEnvelopeContractRefreshesBeforeSendingUserAuthorization()
    {
        var handler = new RecordingHandler();
        var store = new BrowserHostedCredentialStore(new MemoryJsRuntime());
        var expiring = ValidCredential() with
        {
            AccessToken = "expiring-access-token",
            AccessTokenExpiresAt = DateTimeOffset.Parse("2026-08-07T00:01:00Z")
        };
        await store.SaveAsync(expiring);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://app.local/") };
        IMobileRecoveryEnvelopeService service = new MobileSupabaseHostedSyncClient(
            http,
            store,
            new ManualSyncClock(DateTimeOffset.Parse("2026-08-07T00:00:00Z")));

        await service.GetConfigurationAsync();

        var refresh = Assert.Single(handler.Requests, request => request.Path == "/auth/v1/token");
        var envelope = Assert.Single(handler.Requests, request => request.Path == "/functions/v1/recovery-envelope");
        var refreshedAccessToken = CreateJwt(new { iss = "https://pilot.supabase.co/auth/v1", exp = 4_102_444_800L });
        Assert.Null(refresh.Authorization);
        Assert.Equal("Bearer " + refreshedAccessToken, envelope.Authorization);
        Assert.NotEqual("Bearer " + expiring.AccessToken, envelope.Authorization);
        Assert.True(handler.Requests.IndexOf(refresh) < handler.Requests.IndexOf(envelope));
    }

    [Fact]
    public async Task RecoveryEnvelopeContractRequiresRetainedCredentialBeforeNetworkRequest()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://app.local/") };
        IMobileRecoveryEnvelopeService service = new MobileSupabaseHostedSyncClient(
            http,
            new BrowserHostedCredentialStore(new MemoryJsRuntime()),
            new ManualSyncClock(DateTimeOffset.Parse("2026-08-07T00:00:00Z")));

        var error = await Assert.ThrowsAsync<MobileHostedDiagnosticException>(async () =>
            await service.GetConfigurationAsync());

        Assert.Equal("RECOVERY_AUTH_REQUIRED", error.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RecoveryEnvelopeContractStopsBeforeFunctionWhenExpiredCredentialCannotRefresh()
    {
        var handler = new RecordingHandler();
        handler.ResponseOverrides["/auth/v1/token"] = (HttpStatusCode.BadRequest, """{"error_code":"refresh_token_not_found","msg":"refresh token rejected"}""");
        var store = new BrowserHostedCredentialStore(new MemoryJsRuntime());
        await store.SaveAsync(ValidCredential() with
        {
            AccessToken = "expired-access-token",
            AccessTokenExpiresAt = DateTimeOffset.Parse("2020-01-01T00:00:00Z")
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://app.local/") };
        IMobileRecoveryEnvelopeService service = new MobileSupabaseHostedSyncClient(
            http,
            store,
            new ManualSyncClock(DateTimeOffset.Parse("2026-08-07T00:00:00Z")));

        var error = await Assert.ThrowsAsync<MobileHostedDiagnosticException>(async () =>
            await service.GetConfigurationAsync());

        Assert.Equal("RECOVERY_AUTH_REQUIRED", error.ErrorCode);
        Assert.IsType<HostedSignInException>(error.InnerException);
        Assert.Single(handler.Requests, request => request.Path == "/auth/v1/token");
        Assert.DoesNotContain(handler.Requests, request => request.Path == "/functions/v1/recovery-envelope");
        Assert.NotNull(await store.LoadAsync());
    }

    [Theory]
    [InlineData("configuration", "{ \"fingerprint\": \"service-key-fingerprint\", \"algorithm\": \"RSA-OAEP-256\", \"keyVersionId\": \"recovery-key-v1\" }")]
    [InlineData("configuration", "{ not json")]
    [InlineData("enroll", "{ \"enrolled\": false, \"keyVersionId\": \"recovery-key-v1\" }")]
    [InlineData("restore", "{ \"algorithm\": \"RSA-OAEP-256\", \"keyVersionId\": \"recovery-key-v1\" }")]
    public async Task RecoveryEnvelopeContractRejectsInvalidFunctionResponses(string action, string body)
    {
        var handler = new RecordingHandler();
        handler.ResponseOverrides["/functions/v1/recovery-envelope"] = (HttpStatusCode.OK, body);
        var store = new BrowserHostedCredentialStore(new MemoryJsRuntime());
        await store.SaveAsync(ValidCredential());
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://app.local/") };
        IMobileRecoveryEnvelopeService service = new MobileSupabaseHostedSyncClient(
            http,
            store,
            new ManualSyncClock(DateTimeOffset.Parse("2026-08-07T00:00:00Z")));

        var error = await Assert.ThrowsAsync<MobileHostedDiagnosticException>(async () =>
        {
            if (action == "configuration")
            {
                await service.GetConfigurationAsync();
            }
            else if (action == "enroll")
            {
                await service.EnrollAsync(new MobileRecoveryEnvelopeEnrollmentRequest(
                    new LogbookId("log_20000000000000000000000000000001"),
                    new DeviceId("dev_40000000000000000000000000000001"),
                    ValidRecoveryDeviceKey(),
                    "ingress-wrapped-package-key",
                    "recovery-key-v1"));
            }
            else
            {
                await service.RestoreAsync(new MobileRecoveryEnvelopeRestoreRequest(
                    new LogbookId("log_20000000000000000000000000000001"),
                    new DeviceId("dev_40000000000000000000000000000001"),
                    ValidRecoveryDeviceKey(),
                    "Pixel 8 Pro"));
            }
        });

        Assert.Equal("RECOVERY_RESPONSE_INVALID", error.ErrorCode);
        Assert.Single(handler.Requests, request => request.Path == "/functions/v1/recovery-envelope");
    }

    [Fact]
    public async Task RecoveryEnvelopeContractRedactsRejectedServiceDetails()
    {
        var handler = new RecordingHandler();
        handler.ResponseOverrides["/functions/v1/recovery-envelope"] = (HttpStatusCode.Forbidden, """
            {
              "error_code": "permission_denied",
              "message": "owner@example.com https://pilot.supabase.co bearer eyJhbGciOiJub25lIn0.eyJzdWIiOiIxMDAwMDAwMC0wMDAwLTAwMDAtMDAwMC0wMDAwMDAwMDAwMDEifQ.signaturesig package_key=super-secret-value log_20000000000000000000000000000001"
            }
            """);
        var store = new BrowserHostedCredentialStore(new MemoryJsRuntime());
        await store.SaveAsync(ValidCredential());
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://app.local/") };
        IMobileRecoveryEnvelopeService service = new MobileSupabaseHostedSyncClient(
            http,
            store,
            new ManualSyncClock(DateTimeOffset.Parse("2026-08-07T00:00:00Z")));

        var error = await Assert.ThrowsAsync<MobileHostedDiagnosticException>(async () =>
            await service.GetConfigurationAsync());

        Assert.Equal("permission_denied", error.ErrorCode);
        Assert.Equal(HttpStatusCode.Forbidden, error.HttpStatus);
        Assert.Equal("permission_denied", error.SupabaseCode);
        Assert.DoesNotContain("owner@example.com", error.SupabaseMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("pilot.supabase.co", error.SupabaseMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJ", error.SupabaseMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-value", error.SupabaseMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("log_20000000000000000000000000000001", error.SupabaseMessage, StringComparison.Ordinal);
    }

    private static BrowserHostedCredential ValidCredential() => new(
        new HostedAccountId("acct_10000000000000000000000000000001"),
        new DeviceId("dev_40000000000000000000000000000001"),
        CreateJwt(new { iss = "https://pilot.supabase.co/auth/v1", exp = 4_102_444_800L }),
        "retained-refresh-token",
        DateTimeOffset.Parse("2099-01-01T00:00:00Z"));

    private static MobileRecoveryDeviceKey ValidRecoveryDeviceKey() => new(
        "device-public-key",
        new string('a', 64),
        "RSA-OAEP-256");

    [Fact]
    public async Task GoogleSignInExchangesIdTokenWithRawNonceAndRegistersOnlyAfterMembershipDiscovery()
    {
        var handler = new RecordingHandler();
        handler.ResponseOverrides["/auth/v1/token"] = (HttpStatusCode.OK, AuthSessionJson("google-refresh-token"));
        var jsRuntime = new MemoryJsRuntime { GoogleIdToken = "google-id-token" };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://app.local/") };
        var client = new MobileSupabaseHostedSyncClient(
            http,
            new BrowserHostedCredentialStore(jsRuntime),
            new ManualSyncClock(DateTimeOffset.Parse("2026-08-07T00:00:00Z")),
            new BrowserGoogleCredentialProvider(jsRuntime));

        var session = await client.SignInWithGoogleAsync();

        var tokenRequest = Assert.Single(handler.Requests, request => request.Path == "/auth/v1/token");
        Assert.Equal("?grant_type=id_token", tokenRequest.Query);
        Assert.Null(tokenRequest.Authorization);
        Assert.Equal("google", tokenRequest.Body.GetProperty("provider").GetString());
        Assert.Equal("google-id-token", tokenRequest.Body.GetProperty("id_token").GetString());
        Assert.False(tokenRequest.Body.TryGetProperty("link_identity", out _));
        var rawNonce = tokenRequest.Body.GetProperty("nonce").GetString();
        Assert.NotNull(rawNonce);
        var nativeOptions = Assert.IsType<JsonElement>(jsRuntime.LastGoogleOptions);
        Assert.Equal("web-client-id.apps.googleusercontent.com", nativeOptions.GetProperty("webClientId").GetString());
        var expectedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(rawNonce))).ToLowerInvariant();
        Assert.Equal(expectedHash, nativeOptions.GetProperty("nonce").GetString());
        Assert.Equal(new DeviceId("dev_40000000000000000000000000000001"), session.DeviceId);
        Assert.True(handler.Requests.FindIndex(request => request.Path == "/rest/v1/logbook_memberships")
            < handler.Requests.FindIndex(request => request.Path == "/rest/v1/rpc/accept_hosted_invitation"));
    }

    [Fact]
    public async Task GoogleIdentityLinkUsesExistingSessionAndKeepsRegisteredDevice()
    {
        var handler = new RecordingHandler();
        handler.ResponseOverrides["/auth/v1/token"] = (HttpStatusCode.OK, AuthSessionJson("linked-refresh-token"));
        var jsRuntime = new MemoryJsRuntime { GoogleIdToken = "google-id-token" };
        var store = new BrowserHostedCredentialStore(jsRuntime);
        await store.SaveAsync(ValidCredential());
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://app.local/") };
        var client = new MobileSupabaseHostedSyncClient(
            http,
            store,
            new ManualSyncClock(DateTimeOffset.Parse("2026-08-07T00:00:00Z")),
            new BrowserGoogleCredentialProvider(jsRuntime));

        var session = await client.LinkGoogleIdentityAsync();

        var tokenRequest = Assert.Single(handler.Requests, request => request.Path == "/auth/v1/token");
        Assert.Equal("Bearer " + ValidCredential().AccessToken, tokenRequest.Authorization);
        Assert.True(tokenRequest.Body.GetProperty("link_identity").GetBoolean());
        Assert.Equal(ValidCredential().DeviceId, session.DeviceId);
        Assert.Equal("linked-refresh-token", (await store.LoadAsync())?.RefreshToken);
        Assert.DoesNotContain(handler.Requests, request => request.Path == "/rest/v1/rpc/accept_hosted_invitation");
    }

    [Fact]
    public async Task GoogleSignInFailsBeforeOAuthExchangeWhenPlatformCredentialProviderIsUnavailable()
    {
        var handler = new RecordingHandler();
        var store = new BrowserHostedCredentialStore(new MemoryJsRuntime());
        await store.SaveAsync(ValidCredential());
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://app.local/") };
        var client = new MobileSupabaseHostedSyncClient(
            http,
            store,
            new ManualSyncClock(DateTimeOffset.Parse("2026-08-07T00:00:00Z")));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.SignInWithGoogleAsync());

        Assert.Contains("not configured", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(handler.Requests, request => request.Path == "/auth/v1/token");
        Assert.DoesNotContain(handler.Requests, request => request.Path == "/rest/v1/rpc/accept_hosted_invitation");
        Assert.Equal(ValidCredential(), await store.LoadAsync());
    }

    private static string AuthSessionJson(string refreshToken) => $$"""
        {
          "access_token": "linked-access-token",
          "refresh_token": "{{refreshToken}}",
          "expires_in": 3600,
          "user": { "id": "10000000-0000-0000-0000-000000000001" }
        }
        """;

    private static string Config(string anonKey) => $$"""
        {
          "supabaseUrl": "https://pilot.supabase.co",
          "anonKey": "{{anonKey}}",
          "platformLabel": "Pixel 8 Pro",
          "displayName": "Project owner",
          "googleWebClientId": "web-client-id.apps.googleusercontent.com"
        }
        """;

    private static string CreateJwt(object claims)
    {
        static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{Encode("{\"alg\":\"none\"}")}.{Encode(JsonSerializer.Serialize(claims))}.signature";
    }

    [Fact]
    public async Task ClientUsesInvitedOtpAcceptsInvitationCreatesHostedLogbookAndSyncsOperations()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://app.local/") };
        var jsRuntime = new MemoryJsRuntime();
        var clock = new ManualSyncClock(DateTimeOffset.Parse("2026-08-07T00:00:00Z"));
        var client = new MobileSupabaseHostedSyncClient(
            http,
            new BrowserHostedCredentialStore(jsRuntime),
            clock);
        var logbookId = new LogbookId("log_20000000000000000000000000000001");
        var deviceId = new DeviceId("dev_40000000000000000000000000000001");
        var revisionId = new RevisionId("rev_50000000000000000000000000000001");
        var upload = new HostedOperationUpload(
            revisionId,
            new EntryId("ent_entry_one"),
            deviceId,
            clock.UtcNow,
            PortableLogbookDocumentV2.CurrentSchemaVersion,
            Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }),
            Convert.ToBase64String(new byte[12]),
            Convert.ToBase64String(new byte[16]),
            new string('0', 64),
            []);

        var start = await client.StartEmailSignInAsync("owner@example.com");
        var session = await client.CompleteEmailSignInAsync("123456");
        var append = await client.AppendOperationsAsync(logbookId, session.DeviceId, [upload]);
        var page = await client.ReadMissingOperationsAsync(logbookId, 0, 200);
        await client.RecordAcknowledgementAsync(logbookId, session.DeviceId, 1);

        Assert.Equal("o***@example.com", start.DeliveryHint);
        Assert.Equal(new HostedAccountId("acct_10000000000000000000000000000001"), session.AccountId);
        Assert.Equal(deviceId, session.DeviceId);
        Assert.NotNull(await client.GetCurrentSessionAsync());
        Assert.Single(append.AcceptedOperations);
        Assert.Single(page.Operations);
        Assert.Contains("hosted-credential", jsRuntime.StoredValues.Keys);
        Assert.Contains(handler.Requests, request =>
            request.Method == HttpMethod.Post
            && request.Path == "/auth/v1/otp"
            && request.Body.GetProperty("email").GetString() == "owner@example.com"
            && request.Body.GetProperty("create_user").GetBoolean() == false
            && !request.Body.TryGetProperty("should_create_user", out _));
        Assert.Contains(handler.Requests, request =>
            request.Method == HttpMethod.Post
            && request.Path == "/auth/v1/verify"
            && request.Body.GetProperty("email").GetString() == "owner@example.com"
            && request.Body.GetProperty("token").GetString() == "123456"
            && request.Body.GetProperty("type").GetString() == "email"
            && !request.Body.TryGetProperty("token_hash", out _));
        Assert.Contains(handler.Requests, request =>
            request.Method == HttpMethod.Post
            && request.Path == "/rest/v1/rpc/accept_hosted_invitation"
            && request.Authorization == "Bearer access-token"
            && request.Accept == "application/vnd.pgrst.object+json"
            && request.Body.GetProperty("p_device_type").GetString() == "android");
        Assert.Contains(handler.Requests, request =>
            request.Method == HttpMethod.Post
            && request.Path == "/rest/v1/logbooks"
            && request.Authorization == "Bearer access-token"
            && request.Body[0].GetProperty("logbook_id").GetString() == "20000000-0000-0000-0000-000000000001");
        Assert.Contains(handler.Requests, request =>
            request.Method == HttpMethod.Post
            && request.Path == "/rest/v1/logbook_memberships"
            && request.Body[0].GetProperty("role").GetString() == "owner");
        Assert.Contains(handler.Requests, request =>
            request.Method == HttpMethod.Post
            && request.Path == "/rest/v1/rpc/append_hosted_operation"
            && request.Accept == "application/vnd.pgrst.object+json"
            && request.Body.GetProperty("p_operation_format_version").GetInt32() == 1);
        Assert.Contains(handler.Requests, request =>
            request.Method == HttpMethod.Post
            && request.Path == "/rest/v1/rpc/read_missing_operations"
            && request.Accept == "application/json"
            && request.Body.GetProperty("p_page_size").GetInt32() == 200);
        Assert.Contains(handler.Requests, request =>
            request.Method == HttpMethod.Post
            && request.Path == "/rest/v1/rpc/record_operation_ack"
            && request.Accept == "application/vnd.pgrst.object+json");
        Assert.Contains(handler.Requests, request =>
            request.Method == HttpMethod.Post
            && request.Path == "/rest/v1/rpc/record_operation_ack"
            && request.Body.GetProperty("p_highest_contiguous_revision").GetInt64() == 1);
    }

    [Fact]
    public async Task ReadMissingOperationsUsesTheLastReturnedRevisionAsItsPageCursor()
    {
        var handler = new RecordingHandler();
        handler.ResponseOverrides["/rest/v1/rpc/read_missing_operations"] =
            (HttpStatusCode.OK, """
                [{
                  "revision": 200,
                  "portable_revision_id": "rev_50000000000000000000000000000200",
                  "entry_id": "ent_page_boundary",
                  "author_device_id": "40000000-0000-0000-0000-000000000001",
                  "client_created_at": "2026-08-22T00:00:00Z",
                  "operation_format_version": 1,
                  "payload_ciphertext": "AQIDBA==",
                  "payload_nonce": "AAAAAAAAAAAAAAAA",
                  "payload_tag": "AAAAAAAAAAAAAAAAAAAAAA==",
                  "payload_hash": "0000000000000000000000000000000000000000000000000000000000000000",
                  "parent_revision_ids": [],
                  "highest_revision": 344,
                  "has_more": true
                }]
                """);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://app.local/") };
        var jsRuntime = new MemoryJsRuntime();
        var clock = new ManualSyncClock(DateTimeOffset.Parse("2026-08-22T00:00:00Z"));
        await new BrowserHostedCredentialStore(jsRuntime).SaveAsync(new BrowserHostedCredential(
            new HostedAccountId("acct_10000000000000000000000000000001"),
            new DeviceId("dev_40000000000000000000000000000001"),
            "access-token",
            "refresh-token",
            clock.UtcNow.AddHours(1)));
        var client = new MobileSupabaseHostedSyncClient(
            http,
            new BrowserHostedCredentialStore(jsRuntime),
            clock);

        var page = await client.ReadMissingOperationsAsync(
            new LogbookId("log_20000000000000000000000000000001"),
            afterHostedRevision: 0,
            pageSize: 200);

        Assert.Single(page.Operations);
        Assert.Equal(200, page.ThroughHostedRevision);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task ClientPersistsVerifiedCredentialAndResumesInterruptedDeviceRegistrationWithoutAnotherEmail()
    {
        var handler = new RecordingHandler();
        handler.ResponseOverrides["/rest/v1/rpc/accept_hosted_invitation"] =
            (HttpStatusCode.InternalServerError, "device registration interrupted");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://app.local/") };
        var jsRuntime = new MemoryJsRuntime();
        var store = new BrowserHostedCredentialStore(jsRuntime);
        var client = new MobileSupabaseHostedSyncClient(
            http,
            store,
            new ManualSyncClock(DateTimeOffset.Parse("2026-08-07T00:00:00Z")));

        await client.StartEmailSignInAsync("owner@example.com");
        await Assert.ThrowsAsync<HostedLedgerException>(async () =>
            await client.CompleteEmailSignInAsync("123456"));

        var pending = await store.LoadAsync();
        Assert.NotNull(pending);
        Assert.Equal(new DeviceId("dev_pending"), pending.DeviceId);
        Assert.Null(await client.GetCurrentSessionAsync());

        handler.ResponseOverrides.Remove("/rest/v1/rpc/accept_hosted_invitation");
        var resumed = await client.ResumeEmailSignInAsync();

        Assert.Equal(new DeviceId("dev_40000000000000000000000000000001"), resumed.DeviceId);
        Assert.NotNull(await client.GetCurrentSessionAsync());
        Assert.Single(handler.Requests, request => request.Path == "/auth/v1/verify");
        Assert.Equal(2, handler.Requests.Count(request => request.Path == "/rest/v1/rpc/accept_hosted_invitation"));
    }

    [Fact]
    public async Task ReturningUserDiscoveryStopsBeforeDuplicateDeviceOrLogbookCreation()
    {
        var handler = new RecordingHandler();
        handler.ResponseOverrides["/rest/v1/logbook_memberships"] =
            (HttpStatusCode.OK, """
                [{
                  "logbook_id": "20000000-0000-0000-0000-000000000001",
                  "role": "owner",
                  "logbooks": {
                    "logbook_id": "20000000-0000-0000-0000-000000000001",
                    "current_schema_version": 2,
                    "operation_format_version": 1,
                    "deletion_requested_at": null,
                    "deleted_at": null
                  }
                }]
                """);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://app.local/") };
        var store = new BrowserHostedCredentialStore(new MemoryJsRuntime());
        var client = new MobileSupabaseHostedSyncClient(
            http,
            store,
            new ManualSyncClock(DateTimeOffset.Parse("2026-08-07T00:00:00Z")));

        await client.StartEmailSignInAsync("owner@example.com");
        var error = await Assert.ThrowsAsync<HostedSignInException>(async () =>
            await client.CompleteEmailSignInAsync("123456"));

        Assert.Equal(HostedSignInFailureReason.AccountRecoveryRequired, error.Reason);
        Assert.Contains("existing logbook", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("key", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("recovery", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(handler.Requests, request => request.Path == "/rest/v1/rpc/accept_hosted_invitation");
        Assert.DoesNotContain(handler.Requests, request => request.Method == HttpMethod.Post && request.Path == "/rest/v1/logbooks");
        Assert.DoesNotContain(handler.Requests, request => request.Method == HttpMethod.Post && request.Path == "/rest/v1/logbook_memberships");
        Assert.Contains(handler.Requests, request =>
            request.Method == HttpMethod.Get
            && request.Path == "/rest/v1/logbook_memberships"
            && request.Authorization == "Bearer access-token");

        var pending = await store.LoadAsync();
        Assert.NotNull(pending);
        Assert.Equal(new DeviceId("dev_pending"), pending.DeviceId);
    }

    [Fact]
    public async Task ReplacementRecoveryReusesStablePendingDeviceAndActivatesOnlyAfterCompletion()
    {
        var handler = new RecordingHandler();
        handler.ResponseOverrides["/rest/v1/logbook_memberships"] =
            (HttpStatusCode.OK, """
                [{
                  "logbook_id": "20000000-0000-0000-0000-000000000001",
                  "role": "owner",
                  "logbooks": {
                    "logbook_id": "20000000-0000-0000-0000-000000000001",
                    "current_schema_version": 2,
                    "operation_format_version": 1,
                    "deletion_requested_at": null,
                    "deleted_at": null
                  }
                }]
                """);
        var store = new BrowserHostedCredentialStore(new MemoryJsRuntime());
        await store.SaveAsync(new BrowserHostedCredential(
            new HostedAccountId("acct_10000000000000000000000000000001"),
            new DeviceId("dev_pending"),
            "access-token",
            "refresh-token",
            DateTimeOffset.Parse("2099-01-01T00:00:00Z"),
            DeviceRegistrationPending: true));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://app.local/") };
        var client = new MobileSupabaseHostedSyncClient(
            http,
            store,
            new ManualSyncClock(DateTimeOffset.Parse("2026-08-07T00:00:00Z")));
        var logbookId = new LogbookId("log_20000000000000000000000000000001");

        var first = await client.PrepareReplacementRecoveryAsync(logbookId);
        var retry = await client.PrepareReplacementRecoveryAsync(logbookId);

        Assert.Equal(first.Session.DeviceId, retry.Session.DeviceId);
        Assert.StartsWith("dev_", first.Session.DeviceId.Value, StringComparison.Ordinal);
        Assert.Equal(36, first.Session.DeviceId.Value.Length);
        Assert.Null(await client.GetCurrentSessionAsync());

        await client.CompleteReplacementRecoveryAsync(logbookId, first.Session.DeviceId);

        Assert.Equal(first.Session.DeviceId, (await client.GetCurrentSessionAsync())?.DeviceId);
        var activation = Assert.Single(handler.Requests, request =>
            request.Path == "/functions/v1/recovery-envelope"
            && request.Body.GetProperty("action").GetString() == "activate");
        Assert.Equal(
            first.Session.DeviceId.Value[4..].Insert(8, "-").Insert(13, "-").Insert(18, "-").Insert(23, "-"),
            activation.Body.GetProperty("deviceId").GetString());
    }

    [Fact]
    public async Task ResumeReusesRegisteredDeviceWhenLocalSetupFailedAfterRegistration()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://app.local/") };
        var jsRuntime = new MemoryJsRuntime();
        var store = new BrowserHostedCredentialStore(jsRuntime);
        var expiresAt = DateTimeOffset.Parse("2026-08-07T01:00:00Z");
        await store.SaveAsync(new BrowserHostedCredential(
            new HostedAccountId("acct_10000000000000000000000000000001"),
            new DeviceId("dev_40000000000000000000000000000001"),
            "access-token",
            "refresh-token",
            expiresAt));
        var client = new MobileSupabaseHostedSyncClient(
            http,
            store,
            new ManualSyncClock(DateTimeOffset.Parse("2026-08-07T00:00:00Z")));

        var resumed = await client.ResumeEmailSignInAsync();

        Assert.Equal(new HostedAccountId("acct_10000000000000000000000000000001"), resumed.AccountId);
        Assert.Equal(new DeviceId("dev_40000000000000000000000000000001"), resumed.DeviceId);
        Assert.Equal(expiresAt, resumed.AccessTokenExpiresAt);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("https://pilot.supabase.co/auth/v1/verify?token=hashed-magic-link-token&type=magiclink&redirect_to=http%3A%2F%2Flocalhost%3A3000", "hashed-magic-link-token", "magiclink")]
    [InlineData("https://pilot.supabase.co/auth/v1/verify?token_hash=hashed-email-token&type=email", "hashed-email-token", "email")]
    public async Task ClientAcceptsUnusedSupabaseSignInLinkAsTokenHash(
        string signInLink,
        string expectedTokenHash,
        string expectedType)
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://app.local/") };
        var client = new MobileSupabaseHostedSyncClient(
            http,
            new BrowserHostedCredentialStore(new MemoryJsRuntime()),
            new ManualSyncClock(DateTimeOffset.Parse("2026-08-07T00:00:00Z")));

        await client.StartEmailSignInAsync("owner@example.com");
        await client.CompleteEmailSignInAsync(signInLink);

        Assert.Contains(handler.Requests, request =>
            request.Method == HttpMethod.Post
            && request.Path == "/auth/v1/verify"
            && request.Body.GetProperty("token_hash").GetString() == expectedTokenHash
            && request.Body.GetProperty("type").GetString() == expectedType
            && !request.Body.TryGetProperty("email", out _)
            && !request.Body.TryGetProperty("token", out _));
    }

    [Fact]
    public async Task ClientAcceptsUnusedSupabaseSignInLinkWithoutPendingEmailRequest()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://app.local/") };
        var client = new MobileSupabaseHostedSyncClient(
            http,
            new BrowserHostedCredentialStore(new MemoryJsRuntime()),
            new ManualSyncClock(DateTimeOffset.Parse("2026-08-07T00:00:00Z")));

        await client.CompleteEmailSignInAsync(
            "https://pilot.supabase.co/auth/v1/verify?token=existing-token-hash&type=magiclink");

        Assert.DoesNotContain(handler.Requests, request => request.Path == "/auth/v1/otp");
        Assert.Contains(handler.Requests, request =>
            request.Path == "/auth/v1/verify"
            && request.Body.GetProperty("token_hash").GetString() == "existing-token-hash");
    }

    [Fact]
    public async Task ClientUnwrapsOutlookSafeLinkBeforeValidatingSupabaseProject()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://app.local/") };
        var client = new MobileSupabaseHostedSyncClient(
            http,
            new BrowserHostedCredentialStore(new MemoryJsRuntime()),
            new ManualSyncClock(DateTimeOffset.Parse("2026-08-07T00:00:00Z")));
        const string safeLink =
            "https://nam01.safelinks.protection.outlook.com/?url=https%3A%2F%2Fpilot.supabase.co%2Fauth%2Fv1%2Fverify%3Ftoken%3Dsafe-link-token%26type%3Dmagiclink%26redirect_to%3Dhttp%3A%2F%2Flocalhost%3A3000&data=redacted&reserved=0";

        await client.CompleteEmailSignInAsync(safeLink);

        Assert.DoesNotContain(handler.Requests, request => request.Path == "/auth/v1/otp");
        Assert.Contains(handler.Requests, request =>
            request.Path == "/auth/v1/verify"
            && request.Body.GetProperty("token_hash").GetString() == "safe-link-token"
            && request.Body.GetProperty("type").GetString() == "magiclink");
    }

    [Fact]
    public async Task ClientRedactsEmailRateLimitResponseAndGivesActionableGuidance()
    {
        var handler = new RecordingHandler();
        handler.ResponseOverrides["/auth/v1/otp"] = (
            HttpStatusCode.TooManyRequests,
            """{"code":"429","error_code":"over_email_send_rate_limit","msg":"email rate limit exceeded"}""");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://app.local/") };
        var client = new MobileSupabaseHostedSyncClient(
            http,
            new BrowserHostedCredentialStore(new MemoryJsRuntime()),
            new ManualSyncClock(DateTimeOffset.Parse("2026-08-07T00:00:00Z")));

        var error = await Assert.ThrowsAsync<HostedSignInException>(async () =>
            await client.StartEmailSignInAsync("owner@example.com"));

        Assert.Contains("email limit", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("send once", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error_code", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("owner@example.com", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClientReportsExpiredMagicLinkWithoutMisclassifyingInvitation()
    {
        var handler = new RecordingHandler();
        handler.ResponseOverrides["/auth/v1/verify"] = (
            HttpStatusCode.Forbidden,
            """{"code":403,"error_code":"otp_expired","msg":"Email link is invalid or has expired"}""");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://app.local/") };
        var client = new MobileSupabaseHostedSyncClient(
            http,
            new BrowserHostedCredentialStore(new MemoryJsRuntime()),
            new ManualSyncClock(DateTimeOffset.Parse("2026-08-07T00:00:00Z")));

        var error = await Assert.ThrowsAsync<HostedSignInException>(async () =>
            await client.CompleteEmailSignInAsync(
                "https://pilot.supabase.co/auth/v1/verify?token=expired-token-hash&type=magiclink"));

        Assert.Equal(HostedSignInFailureReason.VerificationExpired, error.Reason);
        Assert.Contains("expired", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("otp_expired", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://pilot.supabase.co/auth/v1/verify?token=hash&type=magiclink")]
    [InlineData("https://other.supabase.co/auth/v1/verify?token=hash&type=magiclink")]
    [InlineData("https://pilot.supabase.co/not-auth?token=hash&type=magiclink")]
    [InlineData("https://pilot.supabase.co/auth/v1/verify?token=hash&type=invite")]
    [InlineData("https://nam01.safelinks.protection.outlook.com/?url=https%3A%2F%2Fevil.example%2Fauth%2Fv1%2Fverify%3Ftoken%3Dhash%26type%3Dmagiclink")]
    [InlineData("https://links.example/?url=https%3A%2F%2Fpilot.supabase.co%2Fauth%2Fv1%2Fverify%3Ftoken%3Dhash%26type%3Dmagiclink")]
    public async Task ClientRejectsLinksOutsideCurrentPilotPasswordlessFlow(string signInLink)
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://app.local/") };
        var client = new MobileSupabaseHostedSyncClient(
            http,
            new BrowserHostedCredentialStore(new MemoryJsRuntime()),
            new ManualSyncClock(DateTimeOffset.Parse("2026-08-07T00:00:00Z")));

        await client.StartEmailSignInAsync("owner@example.com");
        var error = await Assert.ThrowsAsync<HostedSignInException>(async () =>
            await client.CompleteEmailSignInAsync(signInLink));

        Assert.Equal(HostedSignInFailureReason.InvalidVerificationCode, error.Reason);
        Assert.Contains("this pilot project", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(handler.Requests, request => request.Path == "/auth/v1/verify");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];
        public Dictionary<string, (HttpStatusCode StatusCode, string Body)> ResponseOverrides { get; } = [];
        public string? ConfigJson { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var bodyText = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            using var parsedBody = string.IsNullOrWhiteSpace(bodyText)
                ? JsonDocument.Parse("{}")
                : JsonDocument.Parse(bodyText);
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri?.AbsolutePath ?? string.Empty,
                request.RequestUri?.Query ?? string.Empty,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("apikey", out var apiKeys) ? apiKeys.Single() : null,
                string.Join(",", request.Headers.Accept.Select(value => value.MediaType)),
                parsedBody.RootElement.Clone()));

            return ResponseFor(request);
        }

        private HttpResponseMessage ResponseFor(HttpRequestMessage request)
        {
            var path = request.RequestUri?.AbsolutePath;
            if (path is not null && ResponseOverrides.TryGetValue(path, out var responseOverride))
            {
                return new HttpResponseMessage(responseOverride.StatusCode)
                {
                    Content = new StringContent(responseOverride.Body)
                };
            }

            var json = path switch
            {
                "/hosted-sync.local.json" => ConfigJson ?? """
                    {
                      "supabaseUrl": "https://pilot.supabase.co",
                      "anonKey": "anon-key",
                      "platformLabel": "Pixel 8 Pro",
                      "displayName": "Project owner",
                      "googleWebClientId": "web-client-id.apps.googleusercontent.com"
                    }
                    """,
                "/auth/v1/otp" => "{}",
                "/auth/v1/verify" => """
                    {
                      "access_token": "access-token",
                      "refresh_token": "refresh-token",
                      "expires_in": 3600,
                      "user": { "id": "10000000-0000-0000-0000-000000000001" }
                    }
                    """,
                "/auth/v1/token" => $$"""
                    {
                      "access_token": "{{CreateJwt(new { iss = "https://pilot.supabase.co/auth/v1", exp = 4_102_444_800L })}}",
                      "refresh_token": "retained-refresh-token",
                      "expires_in": 3600
                    }
                    """,
                "/auth/v1/user" => """{ "id": "10000000-0000-0000-0000-000000000001" }""",
                "/rest/v1/accounts" => """[{ "account_id": "10000000-0000-0000-0000-000000000001", "status": "active" }]""",
                "/rest/v1/devices" => """[{ "device_id": "40000000-0000-0000-0000-000000000001", "account_id": "10000000-0000-0000-0000-000000000001", "status": "active" }]""",
                "/rest/v1/rpc/accept_hosted_invitation" => """
                    { "device_id": "40000000-0000-0000-0000-000000000001" }
                    """,
                "/rest/v1/logbooks" => "{}",
                "/rest/v1/logbook_memberships" => "[]",
                "/rest/v1/rpc/append_hosted_operation" => OperationJson,
                "/rest/v1/rpc/read_missing_operations" => $"[{OperationJson}]",
                "/rest/v1/rpc/record_operation_ack" => "{}",
                "/functions/v1/recovery-envelope" => Requests[^1].Body.GetProperty("action").GetString() switch
                {
                    "configuration" => """
                        {
                          "publicKey": "service-public-key",
                          "fingerprint": "service-key-fingerprint",
                          "algorithm": "RSA-OAEP-256",
                          "keyVersionId": "recovery-key-v1"
                        }
                        """,
                    "enroll" => """{ "enrolled": true, "keyVersionId": "recovery-key-v1" }""",
                    "status" => """{ "managedEnvelopeConfigured": true, "recoveryCodeConfigured": true }""",
                    "restore" => """
                        {
                          "wrappedKey": "device-wrapped-package-key",
                          "algorithm": "RSA-OAEP-256",
                          "keyVersionId": "recovery-key-v1"
                        }
                        """,
                    "enroll-code" => """{ "enrolled": true }""",
                    "restore-code" => """
                        {
                          "ciphertext": "code-ciphertext",
                          "nonce": "code-nonce",
                          "salt": "code-salt",
                          "algorithm": "PBKDF2-SHA256-600000+A256GCM",
                          "keyVersionId": "recovery-code-v1"
                        }
                        """,
                    "activate" => """{ "activated": true }""",
                    _ => throw new InvalidOperationException("Unexpected recovery-envelope action.")
                },
                _ => throw new InvalidOperationException($"Unexpected request path: {path}")
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            };
        }

        private const string OperationJson = """
            {
              "revision": 1,
              "portable_revision_id": "rev_50000000000000000000000000000001",
              "entry_id": "ent_entry_one",
              "author_device_id": "40000000-0000-0000-0000-000000000001",
              "client_created_at": "2026-08-07T00:00:00Z",
              "operation_format_version": 1,
              "payload_ciphertext": "AQIDBA==",
              "payload_nonce": "AAAAAAAAAAAAAAAA",
              "payload_tag": "AAAAAAAAAAAAAAAAAAAAAA==",
              "payload_hash": "0000000000000000000000000000000000000000000000000000000000000000",
              "parent_revision_ids": [],
              "highest_revision": 1,
              "has_more": false
            }
            """;
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        string Path,
        string Query,
        string? Authorization,
        string? ApiKey,
        string Accept,
        JsonElement Body);

    private sealed class MemoryJsRuntime : IJSRuntime
    {
        public Dictionary<string, string> StoredValues { get; } = [];
        public string? GoogleIdToken { get; init; }
        public object? LastGoogleOptions { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

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
                "electronicLogbookStore.delete" => Delete<TValue>(args),
                "electronicLogbookCredentials.getGoogleIdToken" => GetGoogleIdToken<TValue>(args),
                _ => throw new JSException($"Unexpected JS call: {identifier}")
            };
        }

        private ValueTask<TValue> GetGoogleIdToken<TValue>(object?[]? args)
        {
            Assert.False(string.IsNullOrWhiteSpace(GoogleIdToken));
            Assert.NotNull(args);
            LastGoogleOptions = JsonSerializer.SerializeToElement(args[0]);
            var result = JsonSerializer.Deserialize<TValue>(
                JsonSerializer.Serialize(new { idToken = GoogleIdToken, email = "pilot@example.com" }),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return new ValueTask<TValue>(Assert.IsType<TValue>(result));
        }

        private ValueTask<TValue> Load<TValue>(object?[]? args)
        {
            Assert.NotNull(args);
            var key = Assert.IsType<string>(args[0]);
            StoredValues.TryGetValue(key, out var value);
            return new ValueTask<TValue>((TValue)(object?)value!);
        }

        private ValueTask<TValue> Save<TValue>(object?[]? args)
        {
            Assert.NotNull(args);
            StoredValues[Assert.IsType<string>(args[0])] = Assert.IsType<string>(args[1]);
            return new ValueTask<TValue>(default(TValue)!);
        }

        private ValueTask<TValue> Delete<TValue>(object?[]? args)
        {
            Assert.NotNull(args);
            StoredValues.Remove(Assert.IsType<string>(args[0]));
            return new ValueTask<TValue>(default(TValue)!);
        }
    }
}
