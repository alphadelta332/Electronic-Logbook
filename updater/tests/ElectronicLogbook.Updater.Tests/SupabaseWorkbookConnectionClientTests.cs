using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class SupabaseWorkbookConnectionClientTests
{
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
        bool rejectActivation = false) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

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

            if (path == "/functions/v1/recovery-envelope")
            {
                using var json = JsonDocument.Parse(body);
                var action = json.RootElement.GetProperty("action").GetString();
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

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
    }

    private sealed record RecordedRequest(string Path, string Body, string? AuthorizationScheme);
}
