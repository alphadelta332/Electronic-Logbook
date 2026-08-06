using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public sealed class MobileHostedSyncWorkflow(
    BrowserPackageKeyStore keyStore,
    IHostedLogbookLedger ledger,
    IHostedLogbookAuthenticator authenticator,
    INetworkStatus networkStatus,
    ISyncClock clock)
{
    private const int MaxPullPagesPerRun = 10;

    public async ValueTask<PortableHostedSyncResult> SyncAsync(
        PortableHostedSyncRequestContext request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Document);
        ArgumentNullException.ThrowIfNull(request.HostedSync);

        var network = await networkStatus.GetAvailabilityAsync(cancellationToken);
        var uploadedRevisionIds = (request.HostedSync.UploadedRevisionIds ?? []).ToHashSet();
        var pendingLocalOperationCount = request.Document.Operations.Count(operation =>
            !uploadedRevisionIds.Contains(operation.RevisionId));
        if (!network.IsOnline)
        {
            return PortableHostedSyncResult.Offline(
                request.Document,
                request.HostedSync.LastAcknowledgedHostedRevision,
                pendingLocalOperationCount);
        }

        try
        {
            var session = await authenticator.GetCurrentSessionAsync(cancellationToken);
            if (session is null)
            {
                return PortableHostedSyncResult.SigningIn(
                    request.Document,
                    request.HostedSync.LastAcknowledgedHostedRevision,
                    pendingLocalOperationCount);
            }

            if (session.AccessTokenExpiresAt <= clock.UtcNow.AddMinutes(2))
            {
                session = await authenticator.RefreshAsync(cancellationToken);
            }

            var localOperations = new List<HostedOperationUpload>();
            foreach (var operation in request.Document.Operations.Where(operation => operation.DeviceId == session.DeviceId))
            {
                if (uploadedRevisionIds.Contains(operation.RevisionId))
                {
                    continue;
                }

                localOperations.Add(await EncryptOperationAsync(request.Document.LogbookId, operation));
            }

            var appendResult = localOperations.Count > 0
                ? await ledger.AppendOperationsAsync(
                    request.Document.LogbookId,
                    session.DeviceId,
                    localOperations,
                    cancellationToken)
                : new HostedAppendResult([], request.HostedSync.LastAcknowledgedHostedRevision);

            var document = request.Document;
            var throughHostedRevision = request.HostedSync.LastAcknowledgedHostedRevision;
            var downloaded = 0;
            var pagesRead = 0;
            var hasMore = false;
            do
            {
                var page = await ledger.ReadMissingOperationsAsync(
                    request.Document.LogbookId,
                    throughHostedRevision,
                    IHostedLogbookLedger.MaxOperationPageSize,
                    cancellationToken);
                pagesRead++;
                hasMore = page.HasMore;

                if (page.Operations.Count > 0)
                {
                    var remoteOperations = new List<PortableLogbookOperationV2>();
                    foreach (var operation in page.Operations)
                    {
                        remoteOperations.Add(await DecryptOperationAsync(request.Document.LogbookId, operation));
                    }

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

            return hasMore
                ? PortableHostedSyncResult.Waiting(
                    document,
                    throughHostedRevision,
                    appendResult.AcceptedOperations.Count,
                    downloaded,
                    appendResult.AcceptedOperations.Select(operation => operation.RevisionId).ToArray())
                : PortableHostedSyncResult.Synced(
                    document,
                    throughHostedRevision,
                    appendResult.AcceptedOperations.Count,
                    downloaded,
                    appendResult.AcceptedOperations.Select(operation => operation.RevisionId).ToArray());
        }
        catch (Exception ex) when (ex is HostedSignInException or HostedLedgerException or CryptographicException or FormatException)
        {
            return PortableHostedSyncResult.NeedsAttention(
                request.Document,
                request.HostedSync.LastAcknowledgedHostedRevision,
                request.Document.Operations.Count,
                ex.Message);
        }
    }

    private async ValueTask<HostedOperationUpload> EncryptOperationAsync(
        LogbookId logbookId,
        PortableLogbookOperationV2 operation)
    {
        var plaintext = Compress(Encoding.UTF8.GetBytes(PortableLogbookJson.SerializeOperationV2(operation)));
        var nonce = RandomNumberGenerator.GetBytes(12);
        var encrypted = await keyStore.EncryptAsync(
            logbookId,
            nonce,
            plaintext,
            AdditionalData(operation));

        return new HostedOperationUpload(
            operation.RevisionId,
            operation.EntryId,
            operation.DeviceId,
            operation.CreatedAt,
            PortableLogbookDocumentV2.CurrentSchemaVersion,
            Convert.ToBase64String(encrypted.Ciphertext),
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(encrypted.Tag),
            Convert.ToHexString(SHA256.HashData(encrypted.Ciphertext)).ToLowerInvariant(),
            operation.ParentRevisionIds.ToArray());
    }

    private async ValueTask<PortableLogbookOperationV2> DecryptOperationAsync(
        LogbookId logbookId,
        HostedOperationEnvelope envelope)
    {
        var ciphertext = Convert.FromBase64String(envelope.PayloadCiphertext);
        var expectedHash = Convert.ToHexString(SHA256.HashData(ciphertext)).ToLowerInvariant();
        if (!string.Equals(expectedHash, envelope.PayloadHash, StringComparison.Ordinal))
        {
            throw new CryptographicException("Hosted operation payload hash does not match the ciphertext.");
        }

        var plaintext = await keyStore.DecryptAsync(
            logbookId,
            Convert.FromBase64String(envelope.PayloadNonce),
            ciphertext,
            Convert.FromBase64String(envelope.PayloadTag),
            AdditionalData(envelope));
        var json = Encoding.UTF8.GetString(Decompress(plaintext));
        var operation = PortableLogbookJson.DeserializeOperationV2(json)
            ?? throw new FormatException("Hosted operation payload is not a valid operation.");

        if (operation.RevisionId != envelope.RevisionId
            || operation.EntryId != envelope.EntryId
            || operation.DeviceId != envelope.DeviceId)
        {
            throw new CryptographicException("Hosted operation metadata does not match its encrypted payload.");
        }

        return operation;
    }

    private static PortableLogbookDocumentV2 MergeOperations(
        PortableLogbookDocumentV2 document,
        IEnumerable<PortableLogbookOperationV2> operations)
    {
        var existingRevisionIds = document.Operations
            .Select(operation => operation.RevisionId)
            .ToHashSet();
        return PortableLogbookDocumentV2.CreateAustraliaFirst(
            document.LogbookId,
            document.CustomFieldDefinitions,
            document.CurrencyOverrideDates,
            document.Operations.Concat(operations.Where(operation => !existingRevisionIds.Contains(operation.RevisionId))));
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

public sealed record PortableHostedSyncRequestContext(
    PortableLogbookDocumentV2 Document,
    BrowserHostedSyncState HostedSync,
    BackgroundSyncReason Reason);
