using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ElectronicLogbook.Portable;

public sealed record PortableHostedConfigurationRevision(
    int SchemaVersion,
    LogbookId LogbookId,
    RevisionId RevisionId,
    DeviceId DeviceId,
    DateTimeOffset CreatedAt,
    IReadOnlyList<CustomFieldDefinition> CustomFieldDefinitions,
    PortableLogbookCurrencyOverrideDates CurrencyOverrideDates)
{
    public const int CurrentSchemaVersion = PortableLogbookDocumentV2.CurrentSchemaVersion;

    public static PortableHostedConfigurationRevision Create(
        PortableLogbookDocumentV2 document,
        RevisionId revisionId,
        DeviceId deviceId,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(revisionId.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId.Value);

        return new PortableHostedConfigurationRevision(
            CurrentSchemaVersion,
            document.LogbookId,
            revisionId,
            deviceId,
            createdAt.ToUniversalTime(),
            document.CustomFieldDefinitions
                .OrderBy(field => field.Order)
                .ThenBy(field => field.Id.Value, StringComparer.Ordinal)
                .ToArray(),
            document.CurrencyOverrideDates);
    }
}

public static class HostedConfigurationRevisionCipher
{
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;
    private const string NonceDomain = "electronic-logbook.hosted-configuration-nonce.v1";

    public static HostedConfigurationRevisionUpload Encrypt(
        PortableHostedConfigurationRevision revision,
        PortableLogbookKey key)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(key);
        ValidateRevision(revision);

        var serialized = JsonSerializer.SerializeToUtf8Bytes(
            revision,
            PortableLogbookJson.SerializerOptions);
        var plaintext = Compress(serialized);
        var ciphertext = new byte[plaintext.Length];
        var nonce = DeriveNonce(revision.LogbookId, revision.RevisionId, plaintext);
        var tag = new byte[TagSizeBytes];
        var keyBytes = key.ToBytes();
        try
        {
            using var aes = new AesGcm(keyBytes, TagSizeBytes);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AdditionalData(revision));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
            CryptographicOperations.ZeroMemory(plaintext);
        }

        return new HostedConfigurationRevisionUpload(
            revision.RevisionId,
            revision.DeviceId,
            revision.CreatedAt,
            revision.SchemaVersion,
            Convert.ToBase64String(ciphertext),
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(tag),
            Convert.ToHexString(SHA256.HashData(ciphertext)).ToLowerInvariant());
    }

    public static PortableHostedConfigurationRevision Decrypt(
        LogbookId logbookId,
        HostedConfigurationRevisionEnvelope envelope,
        PortableLogbookKey key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logbookId.Value);
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(key);

        try
        {
            var ciphertext = Convert.FromBase64String(envelope.PayloadCiphertext);
            var expectedHash = Convert.ToHexString(SHA256.HashData(ciphertext)).ToLowerInvariant();
            if (!string.Equals(expectedHash, envelope.PayloadHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new HostedConfigurationRevisionCipherException(
                    "Hosted configuration payload hash does not match the ciphertext.");
            }

            var nonce = Convert.FromBase64String(envelope.PayloadNonce);
            var tag = Convert.FromBase64String(envelope.PayloadTag);
            var plaintext = new byte[ciphertext.Length];
            var keyBytes = key.ToBytes();
            try
            {
                using var aes = new AesGcm(keyBytes, TagSizeBytes);
                aes.Decrypt(
                    nonce,
                    ciphertext,
                    tag,
                    plaintext,
                    CreateAdditionalData(logbookId, envelope));

                return DeserializeDecryptedPayload(logbookId, envelope, plaintext);
            }
            catch (CryptographicException ex)
            {
                throw new HostedConfigurationRevisionCipherException(
                    "Hosted configuration payload authentication failed.",
                    ex);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(keyBytes);
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        catch (HostedConfigurationRevisionCipherException)
        {
            throw;
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or JsonException)
        {
            throw new HostedConfigurationRevisionCipherException(
                "Hosted configuration payload is invalid.",
                ex);
        }
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

    public static byte[] CreateAdditionalData(
        LogbookId logbookId,
        HostedConfigurationRevisionEnvelope envelope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logbookId.Value);
        ArgumentNullException.ThrowIfNull(envelope);
        return AdditionalData(
            logbookId,
            envelope.RevisionId,
            envelope.DeviceId,
            envelope.CreatedAt,
            envelope.SchemaVersion);
    }

    public static PortableHostedConfigurationRevision DeserializeDecryptedPayload(
        LogbookId logbookId,
        HostedConfigurationRevisionEnvelope envelope,
        ReadOnlySpan<byte> compressedPlaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logbookId.Value);
        ArgumentNullException.ThrowIfNull(envelope);

        try
        {
            var json = Encoding.UTF8.GetString(Decompress(compressedPlaintext));
            var revision = JsonSerializer.Deserialize<PortableHostedConfigurationRevision>(
                json,
                PortableLogbookJson.SerializerOptions)
                ?? throw new HostedConfigurationRevisionCipherException(
                    "Hosted configuration payload is not a valid revision.");
            ValidateRevision(revision);
            if (revision.LogbookId != logbookId ||
                revision.RevisionId != envelope.RevisionId ||
                revision.DeviceId != envelope.DeviceId ||
                revision.SchemaVersion != envelope.SchemaVersion ||
                revision.CreatedAt.ToUniversalTime() != envelope.CreatedAt.ToUniversalTime())
            {
                throw new HostedConfigurationRevisionCipherException(
                    "Hosted configuration metadata does not match its encrypted payload.");
            }

            return revision;
        }
        catch (HostedConfigurationRevisionCipherException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException)
        {
            throw new HostedConfigurationRevisionCipherException(
                "Hosted configuration payload is invalid.",
                ex);
        }
    }

    private static void ValidateRevision(PortableHostedConfigurationRevision revision)
    {
        if (revision.SchemaVersion != PortableHostedConfigurationRevision.CurrentSchemaVersion)
        {
            throw new HostedConfigurationRevisionCipherException(
                "Hosted configuration revision uses an unsupported schema version.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(revision.LogbookId.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(revision.RevisionId.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(revision.DeviceId.Value);
        ArgumentNullException.ThrowIfNull(revision.CustomFieldDefinitions);
        ArgumentNullException.ThrowIfNull(revision.CurrencyOverrideDates);
    }

    private static byte[] AdditionalData(PortableHostedConfigurationRevision revision) =>
        AdditionalData(
            revision.LogbookId,
            revision.RevisionId,
            revision.DeviceId,
            revision.CreatedAt,
            revision.SchemaVersion);

    private static byte[] AdditionalData(
        LogbookId logbookId,
        RevisionId revisionId,
        DeviceId deviceId,
        DateTimeOffset createdAt,
        int schemaVersion) =>
        Encoding.UTF8.GetBytes(string.Join(
            "|",
            logbookId.Value,
            revisionId.Value,
            deviceId.Value,
            createdAt.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            schemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    private static byte[] Compress(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(bytes);
        }

        return output.ToArray();
    }

    private static byte[] Decompress(ReadOnlySpan<byte> bytes)
    {
        using var input = new MemoryStream(bytes.ToArray());
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }
}

public sealed class HostedConfigurationRevisionCipherException(
    string message,
    Exception? innerException = null)
    : Exception(message, innerException);
