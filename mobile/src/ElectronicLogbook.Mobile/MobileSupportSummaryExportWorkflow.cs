using ElectronicLogbook.Portable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ElectronicLogbook.Mobile;

public static class MobileSupportSummaryExportWorkflow
{
    public static async ValueTask<MobileSupportSummaryExportWorkflowResult> ExportAsync(
        PortableLogbookDocument document,
        BrowserFileStore fileStore,
        DateTimeOffset exportedAt)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(fileStore);

        var summary = PortableLogbookSummary.Create(document);
        var json = JsonSerializer.Serialize(summary, PortableLogbookJson.SerializerOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        var fileName = $"electronic-logbook-summary-{SafeFileNameToken(document.LogbookId.Value)}-{exportedAt:yyyyMMddTHHmmssZ}.json";

        await fileStore.DownloadJsonAsync(fileName, bytes).ConfigureAwait(false);

        return new MobileSupportSummaryExportWorkflowResult(
            fileName,
            BrowserFileStore.JsonContentType,
            exportedAt,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            bytes);
    }

    public static async ValueTask<MobileSupportSummaryExportWorkflowResult> ExportAsync(
        PortableLogbookDocumentV2 document,
        BrowserFileStore fileStore,
        DateTimeOffset exportedAt)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(fileStore);

        var summary = PortableLogbookSummary.Create(document);
        var json = JsonSerializer.Serialize(summary, PortableLogbookJson.SerializerOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        var fileName = $"electronic-logbook-summary-{SafeFileNameToken(document.LogbookId.Value)}-{exportedAt:yyyyMMddTHHmmssZ}.json";

        await fileStore.DownloadJsonAsync(fileName, bytes).ConfigureAwait(false);

        return new MobileSupportSummaryExportWorkflowResult(
            fileName,
            BrowserFileStore.JsonContentType,
            exportedAt,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            bytes);
    }

    private static string SafeFileNameToken(string value) =>
        new(value.Select(character =>
            char.IsLetterOrDigit(character) || character is '_' or '-'
                ? character
                : '_').ToArray());
}

public sealed record MobileSupportSummaryExportWorkflowResult(
    string FileName,
    string ContentType,
    DateTimeOffset ExportedAt,
    string SummarySha256,
    byte[] SummaryBytes);
