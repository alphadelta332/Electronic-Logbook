using System.Text.Json;

namespace ElectronicLogbook.Mobile;

public static class MobileDeviceStateExportWorkflow
{
    public static async ValueTask<MobileDeviceStateExportWorkflowResult> ExportAsync(
        BrowserLogbookState state,
        BrowserFileStore fileStore,
        DateTimeOffset exportedAt)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(fileStore);

        var stored = new BrowserLogbookStoredDocument(
            BrowserLogbookStore.CurrentStoreVersion,
            state.Document.SchemaVersion,
            ElectronicLogbook.Portable.PortableLogbookJson.Serialize(state.Document),
            state.ImportReceipts,
            state.LastSuccessfulExportAt,
            state.LastSuccessfulExport);
        var json = JsonSerializer.Serialize(stored, ElectronicLogbook.Portable.PortableLogbookJson.SerializerOptions);
        var fileName = $"electronic-logbook-device-state-{SafeFileNameToken(state.Document.LogbookId.Value)}-{exportedAt:yyyyMMddTHHmmssZ}.json";

        var transfer = await fileStore.ShareJsonOrDownloadAsync(fileName, json).ConfigureAwait(false);

        return new MobileDeviceStateExportWorkflowResult(fileName, transfer);
    }

    public static async ValueTask<MobileDeviceStateExportWorkflowResult> ExportAsync(
        BrowserLogbookStateV2 state,
        BrowserFileStore fileStore,
        DateTimeOffset exportedAt)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(fileStore);

        var stored = new BrowserLogbookStoredDocument(
            BrowserLogbookStore.CurrentStoreVersion,
            state.Document.SchemaVersion,
            ElectronicLogbook.Portable.PortableLogbookJson.SerializeV2(state.Document),
            state.ImportReceipts,
            state.LastSuccessfulExportAt,
            state.LastSuccessfulExport);
        var json = JsonSerializer.Serialize(stored, ElectronicLogbook.Portable.PortableLogbookJson.SerializerOptions);
        var fileName = $"electronic-logbook-device-state-{SafeFileNameToken(state.Document.LogbookId.Value)}-{exportedAt:yyyyMMddTHHmmssZ}.json";

        var transfer = await fileStore.ShareJsonOrDownloadAsync(fileName, json).ConfigureAwait(false);

        return new MobileDeviceStateExportWorkflowResult(fileName, transfer);
    }

    private static string SafeFileNameToken(string value)
    {
        var token = new string(value
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-')
            .ToArray());

        return string.IsNullOrWhiteSpace(token) ? "logbook" : token;
    }
}

public sealed record MobileDeviceStateExportWorkflowResult(
    string FileName,
    BrowserFileTransferResult Transfer);
