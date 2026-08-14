using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace ElectronicLogbook.Portable;

public sealed class PortableHostedLogbookSync(
    IHostedLogbookLedger ledger,
    IHostedLogbookAuthenticator authenticator,
    INetworkStatus networkStatus,
    ISyncClock clock)
{
    private const int MaxPullPagesPerRun = 10;

    public async ValueTask<PortableHostedSyncResult> SyncAsync(
        PortableHostedSyncRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Document);
        ArgumentNullException.ThrowIfNull(request.LogbookKey);

        var network = await networkStatus.GetAvailabilityAsync(cancellationToken);
        if (!network.IsOnline)
        {
            return PortableHostedSyncResult.Offline(
                request.Document,
                request.LastAcknowledgedHostedRevision,
                request.Document.Operations.Count);
        }

        try
        {
            var session = await authenticator.GetCurrentSessionAsync(cancellationToken);
            if (session is null)
            {
                return PortableHostedSyncResult.SigningIn(
                    request.Document,
                    request.LastAcknowledgedHostedRevision,
                    request.Document.Operations.Count);
            }

            if (session.AccessTokenExpiresAt <= clock.UtcNow.AddMinutes(2))
            {
                session = await authenticator.RefreshAsync(cancellationToken);
            }

            var localOperations = request.Document.Operations
                .Where(operation => operation.DeviceId == session.DeviceId)
                .Select(operation => HostedOperationCipher.Encrypt(operation, request.LogbookKey))
                .ToArray();
            var appendResult = localOperations.Length > 0
                ? await ledger.AppendOperationsAsync(
                    request.Document.LogbookId,
                    session.DeviceId,
                    localOperations,
                    cancellationToken)
                : new HostedAppendResult([], request.LastAcknowledgedHostedRevision);

            var document = request.Document;
            var throughHostedRevision = request.LastAcknowledgedHostedRevision;
            var downloaded = 0;
            var pagesRead = 0;
            var hasMore = false;

            do
            {
                var page = await ledger.ReadMissingOperationsAsync(
                    request.Document.LogbookId,
                    throughHostedRevision,
                    request.PageSize,
                    cancellationToken);
                pagesRead++;
                hasMore = page.HasMore;

                if (page.Operations.Count > 0)
                {
                    var remoteOperations = page.Operations
                        .Select(operation => HostedOperationCipher.Decrypt(operation, request.LogbookKey))
                        .ToArray();
                    document = MergeOperations(document, remoteOperations);
                    downloaded += remoteOperations.Count(operation =>
                        request.Document.Operations.All(existing => existing.RevisionId != operation.RevisionId));
                }

                throughHostedRevision = Math.Max(throughHostedRevision, page.ThroughHostedRevision);
            }
            while (hasMore && pagesRead < MaxPullPagesPerRun);

            throughHostedRevision = Math.Max(throughHostedRevision, appendResult.ThroughHostedRevision);

            await ledger.RecordAcknowledgementAsync(
                request.Document.LogbookId,
                session.DeviceId,
                throughHostedRevision,
                cancellationToken);

        var uploaded = appendResult.AcceptedOperations.Count;
        var uploadedRevisionIds = appendResult.AcceptedOperations
            .Select(operation => operation.RevisionId)
            .ToArray();
        return hasMore
            ? PortableHostedSyncResult.Waiting(document, throughHostedRevision, uploaded, downloaded, uploadedRevisionIds)
            : PortableHostedSyncResult.Synced(document, throughHostedRevision, uploaded, downloaded, uploadedRevisionIds);
        }
        catch (Exception ex) when (ex is HostedSignInException or HostedLedgerException or HostedOperationCipherException)
        {
            return PortableHostedSyncResult.NeedsAttention(
                request.Document,
                request.LastAcknowledgedHostedRevision,
                request.Document.Operations.Count,
                ex.Message);
        }
    }

    private static PortableLogbookDocumentV2 MergeOperations(
        PortableLogbookDocumentV2 document,
        IEnumerable<PortableLogbookOperationV2> operations)
    {
        var existingRevisionIds = document.Operations
            .Select(operation => operation.RevisionId)
            .ToHashSet();
        var mergedOperations = document.Operations
            .Concat(operations.Where(operation => !existingRevisionIds.Contains(operation.RevisionId)))
            .ToArray();

        return PortableLogbookDocumentV2.CreateAustraliaFirst(
            document.LogbookId,
            document.CustomFieldDefinitions,
            document.CurrencyOverrideDates,
            mergedOperations);
    }
}

public static class HostedOperationCipher
{
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;
    private const string NonceDomain = "electronic-logbook.hosted-operation-nonce.v1";

    public static HostedOperationUpload Encrypt(
        PortableLogbookOperationV2 operation,
        PortableLogbookKey key)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(key);

        var plaintext = Compress(Encoding.UTF8.GetBytes(PortableLogbookJson.SerializeOperationV2(operation)));
        var ciphertext = new byte[plaintext.Length];
        var nonce = DeriveNonce(operation.LogbookId, operation.RevisionId, plaintext);
        var tag = new byte[TagSizeBytes];
        using var aes = new AesGcm(key.ToBytes(), TagSizeBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, AdditionalData(operation));

        return new HostedOperationUpload(
            operation.RevisionId,
            operation.EntryId,
            operation.DeviceId,
            operation.CreatedAt,
            PortableLogbookDocumentV2.CurrentSchemaVersion,
            Convert.ToBase64String(ciphertext),
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(tag),
            Convert.ToHexString(SHA256.HashData(ciphertext)).ToLowerInvariant(),
            operation.ParentRevisionIds.ToArray());
    }

    public static byte[] DeriveNonce(
        LogbookId logbookId,
        RevisionId revisionId,
        ReadOnlySpan<byte> plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logbookId.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(revisionId.Value);

        var identity = Encoding.UTF8.GetBytes(string.Join(
            "\0",
            NonceDomain,
            logbookId.Value,
            revisionId.Value));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(identity);
        hash.AppendData(plaintext);
        return hash.GetHashAndReset()[..NonceSizeBytes];
    }

    public static PortableLogbookOperationV2 Decrypt(
        HostedOperationEnvelope envelope,
        PortableLogbookKey key)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(key);

        var ciphertext = Convert.FromBase64String(envelope.PayloadCiphertext);
        var expectedHash = Convert.ToHexString(SHA256.HashData(ciphertext)).ToLowerInvariant();
        if (!string.Equals(expectedHash, envelope.PayloadHash, StringComparison.Ordinal))
        {
            throw new HostedOperationCipherException("Hosted operation payload hash does not match the ciphertext.");
        }

        var nonce = Convert.FromBase64String(envelope.PayloadNonce);
        var tag = Convert.FromBase64String(envelope.PayloadTag);
        var plaintext = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(key.ToBytes(), TagSizeBytes);
            aes.Decrypt(
                nonce,
                ciphertext,
                tag,
                plaintext,
                AdditionalData(envelope));
        }
        catch (CryptographicException ex)
        {
            throw new HostedOperationCipherException("Hosted operation payload authentication failed.", ex);
        }

        var json = Encoding.UTF8.GetString(Decompress(plaintext));
        var operation = PortableLogbookJson.DeserializeOperationV2(json)
            ?? throw new HostedOperationCipherException("Hosted operation payload is not a valid operation.");

        if (operation.RevisionId != envelope.RevisionId
            || operation.EntryId != envelope.EntryId
            || operation.DeviceId != envelope.DeviceId
            || operation.SchemaVersion() != envelope.SchemaVersion)
        {
            throw new HostedOperationCipherException("Hosted operation metadata does not match its encrypted payload.");
        }

        return operation;
    }

    private static byte[] AdditionalData(PortableLogbookOperationV2 operation) =>
        Encoding.UTF8.GetBytes(string.Join(
            "|",
            operation.EntryId.Value,
            operation.RevisionId.Value,
            operation.DeviceId.Value,
            PortableLogbookDocumentV2.CurrentSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    private static byte[] AdditionalData(HostedOperationEnvelope envelope) =>
        Encoding.UTF8.GetBytes(string.Join(
            "|",
            envelope.EntryId.Value,
            envelope.RevisionId.Value,
            envelope.DeviceId.Value,
            envelope.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    private static int SchemaVersion(this PortableLogbookOperationV2 operation) =>
        PortableLogbookDocumentV2.CurrentSchemaVersion;

    private static byte[] Compress(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(bytes);
        }

        return output.ToArray();
    }

    private static byte[] Decompress(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }
}

public sealed class HostedOperationCipherException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed record PortableHostedSyncRequest(
    PortableLogbookDocumentV2 Document,
    PortableLogbookKey LogbookKey,
    long LastAcknowledgedHostedRevision,
    int PageSize = IHostedLogbookLedger.MaxOperationPageSize);

public sealed record PortableHostedSyncResult(
    PortableHostedSyncStatus Status,
    PortableLogbookDocumentV2 Document,
    long LastAcknowledgedHostedRevision,
    int UploadedOperationCount,
    int DownloadedOperationCount,
    int PendingLocalOperationCount,
    string? AttentionRequiredReason = null,
    IReadOnlyList<RevisionId>? UploadedRevisionIds = null)
{
    public static PortableHostedSyncResult Synced(
        PortableLogbookDocumentV2 document,
        long lastAcknowledgedHostedRevision,
        int uploadedOperationCount,
        int downloadedOperationCount,
        IReadOnlyList<RevisionId>? uploadedRevisionIds = null) =>
        new(
            PortableHostedSyncStatus.Synced,
            document,
            lastAcknowledgedHostedRevision,
            uploadedOperationCount,
            downloadedOperationCount,
            PendingLocalOperationCount: 0,
            UploadedRevisionIds: uploadedRevisionIds ?? []);

    public static PortableHostedSyncResult Waiting(
        PortableLogbookDocumentV2 document,
        long lastAcknowledgedHostedRevision,
        int uploadedOperationCount,
        int downloadedOperationCount,
        IReadOnlyList<RevisionId>? uploadedRevisionIds = null) =>
        new(
            PortableHostedSyncStatus.Waiting,
            document,
            lastAcknowledgedHostedRevision,
            uploadedOperationCount,
            downloadedOperationCount,
            PendingLocalOperationCount: 0,
            UploadedRevisionIds: uploadedRevisionIds ?? []);

    public static PortableHostedSyncResult Offline(
        PortableLogbookDocumentV2 document,
        long lastAcknowledgedHostedRevision,
        int pendingLocalOperationCount) =>
        new(
            PortableHostedSyncStatus.Offline,
            document,
            lastAcknowledgedHostedRevision,
            UploadedOperationCount: 0,
            DownloadedOperationCount: 0,
            pendingLocalOperationCount);

    public static PortableHostedSyncResult SigningIn(
        PortableLogbookDocumentV2 document,
        long lastAcknowledgedHostedRevision,
        int pendingLocalOperationCount) =>
        new(
            PortableHostedSyncStatus.SigningIn,
            document,
            lastAcknowledgedHostedRevision,
            UploadedOperationCount: 0,
            DownloadedOperationCount: 0,
            pendingLocalOperationCount);

    public static PortableHostedSyncResult NeedsAttention(
        PortableLogbookDocumentV2 document,
        long lastAcknowledgedHostedRevision,
        int pendingLocalOperationCount,
        string attentionRequiredReason) =>
        new(
            PortableHostedSyncStatus.NeedsAttention,
            document,
            lastAcknowledgedHostedRevision,
            UploadedOperationCount: 0,
            DownloadedOperationCount: 0,
            pendingLocalOperationCount,
            attentionRequiredReason);
}

public enum PortableHostedSyncStatus
{
    Synced,
    Waiting,
    Offline,
    SigningIn,
    NeedsAttention
}
