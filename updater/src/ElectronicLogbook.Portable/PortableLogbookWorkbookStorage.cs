using System.Text.Json;

namespace ElectronicLogbook.Portable;

public static class PortableLogbookWorkbookStorage
{
    public const int CurrentStorageVersion = 1;

    public static PortableLogbookWorkbookStorageEnvelope CreateEnvelope(
        PortableLogbookDocument document,
        byte[] encryptedHistoryPackage,
        IEnumerable<PortableLogbookPackageReceipt> importReceipts)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(encryptedHistoryPackage);
        ArgumentNullException.ThrowIfNull(importReceipts);

        return new PortableLogbookWorkbookStorageEnvelope(
            CurrentStorageVersion,
            document.LogbookId,
            document.SchemaVersion,
            Convert.ToBase64String(encryptedHistoryPackage),
            PortableLogbookSummary.Create(document),
            importReceipts.ToArray());
    }

    public static string Serialize(PortableLogbookWorkbookStorageEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return JsonSerializer.Serialize(envelope, PortableLogbookJson.SerializerOptions);
    }

    public static PortableLogbookWorkbookStorageEnvelope Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var envelope = JsonSerializer.Deserialize<PortableLogbookWorkbookStorageEnvelope>(json, PortableLogbookJson.SerializerOptions)
            ?? throw new ArgumentException("Workbook portable-logbook storage envelope is invalid.", nameof(json));
        if (envelope.StorageVersion != CurrentStorageVersion)
        {
            throw new PortableLogbookWorkbookStorageException(
                PortableLogbookWorkbookStorageError.UnsupportedStorageVersion,
                $"Workbook portable-logbook storage version {envelope.StorageVersion} is not supported.");
        }

        try
        {
            _ = Convert.FromBase64String(envelope.EncryptedHistoryPackageBase64);
        }
        catch (FormatException ex)
        {
            throw new PortableLogbookWorkbookStorageException(
                PortableLogbookWorkbookStorageError.InvalidEncryptedHistoryPackage,
                "Workbook portable-logbook encrypted history package is invalid.",
                ex);
        }

        return envelope;
    }

    public static byte[] GetEncryptedHistoryPackage(PortableLogbookWorkbookStorageEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return Convert.FromBase64String(envelope.EncryptedHistoryPackageBase64);
    }

    public static PortableLogbookWorkbookStorageState OpenEnvelope(
        PortableLogbookWorkbookStorageEnvelope envelope,
        PortableLogbookKey key)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(key);

        var encryptedHistoryPackage = GetEncryptedHistoryPackage(envelope);
        var read = PortableLogbookPackage.Read(encryptedHistoryPackage, key, envelope.LogbookId);
        if (read.Document.SchemaVersion != envelope.SchemaVersion)
        {
            throw new PortableLogbookWorkbookStorageException(
                PortableLogbookWorkbookStorageError.EnvelopeDocumentMismatch,
                "Workbook storage schema version does not match the encrypted history package.");
        }

        var expectedSummary = PortableLogbookSummary.Create(read.Document);
        if (expectedSummary != envelope.Summary)
        {
            throw new PortableLogbookWorkbookStorageException(
                PortableLogbookWorkbookStorageError.EnvelopeSummaryMismatch,
                "Workbook storage summary does not match the encrypted history package.");
        }

        return new PortableLogbookWorkbookStorageState(read.Document, envelope.ImportReceipts);
    }
}

public sealed record PortableLogbookWorkbookStorageEnvelope(
    int StorageVersion,
    LogbookId LogbookId,
    int SchemaVersion,
    string EncryptedHistoryPackageBase64,
    PortableLogbookRedactedSummary Summary,
    IReadOnlyList<PortableLogbookPackageReceipt> ImportReceipts);

public sealed record PortableLogbookWorkbookStorageState(
    PortableLogbookDocument Document,
    IReadOnlyList<PortableLogbookPackageReceipt> ImportReceipts);

public sealed class PortableLogbookWorkbookStorageException : Exception
{
    public PortableLogbookWorkbookStorageException(
        PortableLogbookWorkbookStorageError error,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
    }

    public PortableLogbookWorkbookStorageError Error { get; }
}

public enum PortableLogbookWorkbookStorageError
{
    UnsupportedStorageVersion,
    InvalidEncryptedHistoryPackage,
    EnvelopeDocumentMismatch,
    EnvelopeSummaryMismatch
}
