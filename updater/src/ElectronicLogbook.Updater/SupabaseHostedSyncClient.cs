using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater;

public sealed class SupabaseHostedSyncClient(
    Uri supabaseUrl,
    string anonKey,
    HostedAccountId accountId,
    DeviceId deviceId,
    PortableHostedCredential credential,
    Action<PortableHostedCredential>? credentialUpdated = null)
    : IHostedLogbookLedger, IHostedLogbookAuthenticator, INetworkStatus, IDisposable
{
    private readonly HttpClient http = new()
    {
        BaseAddress = new Uri(supabaseUrl.GetLeftPart(UriPartial.Authority))
    };

    private PortableHostedCredential credential = credential;

    public static bool TryCreate(
        HostedAccountId accountId,
        DeviceId deviceId,
        PortableHostedCredential credential,
        Action<PortableHostedCredential>? credentialUpdated,
        out SupabaseHostedSyncClient? client,
        out string? unavailableReason)
    {
        if (!SupabaseHostedSyncConfiguration.TryLoad(out var configuration, out unavailableReason))
        {
            client = null;
            return false;
        }

        var resolved = configuration ?? throw new InvalidOperationException("Hosted configuration was not resolved.");
        client = new SupabaseHostedSyncClient(
            resolved.SupabaseUrl,
            resolved.AnonKey,
            accountId,
            deviceId,
            credential,
            credentialUpdated);
        unavailableReason = null;
        return true;
    }

    public ValueTask<NetworkAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new NetworkAvailability(true));

    public ValueTask<HostedSyncSession?> GetCurrentSessionAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<HostedSyncSession?>(new HostedSyncSession(accountId, deviceId, credential.AccessTokenExpiresAt));

    public ValueTask<HostedSignInStart> StartEmailSignInAsync(
        string email,
        bool shouldCreateUser = false,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Interactive hosted sign-in is not available from the hidden workbook sync command.");

    public ValueTask<HostedSyncSession> CompleteEmailSignInAsync(
        string verificationCode,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Interactive hosted sign-in is not available from the hidden workbook sync command.");

    public ValueTask<HostedSyncSession> ResumeEmailSignInAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Interactive hosted sign-in recovery is not available from the hidden workbook sync command.");

    public async ValueTask<HostedSyncSession> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/v1/token?grant_type=refresh_token");
        AddBaseHeaders(request, includeAuthorization: false);
        request.Content = JsonContent(new RefreshRequest(credential.RefreshToken));
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HostedSignInException(
                HostedSignInFailureReason.RefreshTokenRevoked,
                $"Hosted sign-in refresh failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var refreshed = JsonSerializer.Deserialize<RefreshResponse>(body, JsonDefaults.Web)
            ?? throw new HostedSignInException(HostedSignInFailureReason.RefreshTokenRevoked, "Hosted sign-in refresh returned no session.");
        credential = new PortableHostedCredential(
            refreshed.AccessToken,
            string.IsNullOrWhiteSpace(refreshed.RefreshToken) ? credential.RefreshToken : refreshed.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(refreshed.ExpiresIn, 60)));
        credentialUpdated?.Invoke(credential);
        return new HostedSyncSession(accountId, deviceId, credential.AccessTokenExpiresAt);
    }

    public ValueTask SignOutAsync(CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public async ValueTask<HostedAppendResult> AppendOperationsAsync(
        LogbookId logbookId,
        DeviceId deviceId,
        IReadOnlyList<HostedOperationUpload> operations,
        CancellationToken cancellationToken = default)
    {
        var accepted = new List<HostedOperationEnvelope>();
        var throughHostedRevision = 0L;
        foreach (var operation in operations)
        {
            var row = await RpcAsync<AppendOperationRequest, HostedOperationRow>(
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
        var rows = await RpcAsync<ReadMissingOperationsRequest, HostedOperationRow[]>(
            "read_missing_operations",
            new ReadMissingOperationsRequest(
                ToHostedUuid(logbookId.Value, "log_"),
                afterHostedRevision,
                pageSize),
            cancellationToken);
        return new HostedOperationPage(
            rows.Select(ToEnvelope).ToArray(),
            rows.Length == 0 ? afterHostedRevision : rows.Max(row => row.HighestRevision),
            rows.Any(row => row.HasMore));
    }

    public async ValueTask RecordAcknowledgementAsync(
        LogbookId logbookId,
        DeviceId deviceId,
        long throughHostedRevision,
        CancellationToken cancellationToken = default)
    {
        _ = await RpcAsync<RecordAckRequest, JsonElement>(
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

    public void Dispose() => http.Dispose();

    private async Task<TResponse> RpcAsync<TRequest, TResponse>(
        string functionName,
        TRequest payload,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/rest/v1/rpc/{functionName}");
        AddBaseHeaders(request, includeAuthorization: true);
        request.Content = JsonContent(payload);
        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ToHostedException(response.StatusCode, body);
        }

        return JsonSerializer.Deserialize<TResponse>(body, JsonDefaults.Web)
            ?? throw new HostedLedgerException(HostedLedgerFailureReason.InvalidPayloadEnvelope, $"Hosted RPC '{functionName}' returned no payload.");
    }

    private void AddBaseHeaders(HttpRequestMessage request, bool includeAuthorization)
    {
        request.Headers.Add("apikey", anonKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (includeAuthorization)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        }
    }

    private static StringContent JsonContent<T>(T payload) =>
        new(
            JsonSerializer.Serialize(payload, JsonDefaults.Web),
            Encoding.UTF8,
            "application/json");

    private static HostedLedgerException ToHostedException(HttpStatusCode statusCode, string body)
    {
        var message = string.IsNullOrWhiteSpace(body)
            ? $"Hosted RPC failed with HTTP {(int)statusCode}."
            : $"Hosted RPC failed with HTTP {(int)statusCode}: {body}";
        return new HostedLedgerException(HostedLedgerFailureReason.InvalidPayloadEnvelope, message);
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

    private static string FormatOperationKind(HostedOperationUpload operation)
    {
        var value = operation.RevisionId.Value;
        return value.Contains("delete", StringComparison.OrdinalIgnoreCase)
            ? "deletion"
            : "operation";
    }

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

    private static DeviceId FromHostedUuid(string value, string prefix) =>
        Guid.TryParse(value, out var parsed)
            ? new DeviceId(prefix + parsed.ToString("N"))
            : new DeviceId(value);

    private sealed record RefreshRequest(
        [property: JsonPropertyName("refresh_token")] string RefreshToken);

    private sealed record RefreshResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record AppendOperationRequest(
        [property: JsonPropertyName("p_logbook_id")]
        string PLogbookId,
        [property: JsonPropertyName("p_device_id")]
        string PDeviceId,
        [property: JsonPropertyName("p_operation_id")]
        string POperationId,
        [property: JsonPropertyName("p_portable_revision_id")]
        string PPortableRevisionId,
        [property: JsonPropertyName("p_entry_id")]
        string PEntryId,
        [property: JsonPropertyName("p_base_revision")]
        long? PBaseRevision,
        [property: JsonPropertyName("p_parent_revision_ids")]
        IReadOnlyList<string> PParentRevisionIds,
        [property: JsonPropertyName("p_operation_type")]
        string POperationType,
        [property: JsonPropertyName("p_operation_format_version")]
        int POperationFormatVersion,
        [property: JsonPropertyName("p_payload_ciphertext")]
        string PPayloadCiphertext,
        [property: JsonPropertyName("p_payload_nonce")]
        string PPayloadNonce,
        [property: JsonPropertyName("p_payload_tag")]
        string PPayloadTag,
        [property: JsonPropertyName("p_payload_hash")]
        string PPayloadHash,
        [property: JsonPropertyName("p_client_created_at")]
        DateTimeOffset PClientCreatedAt,
        [property: JsonPropertyName("p_redacted_routing_hints")]
        IReadOnlyDictionary<string, string> PRedactedRoutingHints);

    private sealed record ReadMissingOperationsRequest(
        [property: JsonPropertyName("p_logbook_id")]
        string PLogbookId,
        [property: JsonPropertyName("p_after_revision")]
        long PAfterRevision,
        [property: JsonPropertyName("p_page_size")]
        int PPageSize);

    private sealed record RecordAckRequest(
        [property: JsonPropertyName("p_logbook_id")]
        string PLogbookId,
        [property: JsonPropertyName("p_device_id")]
        string PDeviceId,
        [property: JsonPropertyName("p_highest_contiguous_revision")]
        long PHighestContiguousRevision,
        [property: JsonPropertyName("p_last_upload_revision")]
        long PLastUploadRevision,
        [property: JsonPropertyName("p_last_pull_revision")]
        long PLastPullRevision,
        [property: JsonPropertyName("p_local_queue_state")]
        string PLocalQueueState);

    private sealed record HostedOperationRow(
        [property: JsonPropertyName("revision")]
        long Revision,
        [property: JsonPropertyName("operation_id")]
        string OperationId,
        [property: JsonPropertyName("portable_revision_id")]
        string PortableRevisionId,
        [property: JsonPropertyName("entry_id")]
        string? EntryId,
        [property: JsonPropertyName("base_revision")]
        long? BaseRevision,
        [property: JsonPropertyName("parent_revision_ids")]
        IReadOnlyList<string> ParentRevisionIds,
        [property: JsonPropertyName("author_device_id")]
        string AuthorDeviceId,
        [property: JsonPropertyName("operation_type")]
        string OperationType,
        [property: JsonPropertyName("operation_format_version")]
        int OperationFormatVersion,
        [property: JsonPropertyName("payload_ciphertext")]
        string PayloadCiphertext,
        [property: JsonPropertyName("payload_nonce")]
        string PayloadNonce,
        [property: JsonPropertyName("payload_tag")]
        string PayloadTag,
        [property: JsonPropertyName("payload_hash")]
        string PayloadHash,
        [property: JsonPropertyName("client_created_at")]
        DateTimeOffset ClientCreatedAt,
        [property: JsonPropertyName("received_at")]
        DateTimeOffset? ReceivedAt,
        [property: JsonPropertyName("highest_revision")]
        long HighestRevision,
        [property: JsonPropertyName("has_more")]
        bool HasMore);
}
