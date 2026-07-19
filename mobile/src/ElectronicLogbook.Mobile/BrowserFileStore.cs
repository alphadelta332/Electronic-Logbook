using Microsoft.JSInterop;

namespace ElectronicLogbook.Mobile;

public sealed class BrowserFileStore(IJSRuntime jsRuntime)
{
    public const string ElogbookContentType = "application/vnd.electronic-logbook";
    public const string ElogbookExtension = ".elogbook";
    public const int MaxElogbookBytes = 64 * 1024 * 1024;

    public ValueTask<BrowserFile?> PickAsync(string accept = ".elogbook") =>
        jsRuntime.InvokeAsync<BrowserFile?>("electronicLogbookFiles.pick", accept);

    public async ValueTask<BrowserFile?> PickElogbookAsync()
    {
        var file = await PickAsync(ElogbookExtension).ConfigureAwait(false);
        if (file is null)
        {
            return null;
        }

        ValidateElogbookFile(file);
        return file;
    }

    public ValueTask<bool> CanShareAsync(
        string fileName,
        byte[] bytes,
        string contentType = ElogbookContentType)
    {
        ValidateExportArguments(fileName, bytes, contentType);
        return jsRuntime.InvokeAsync<bool>(
            "electronicLogbookFiles.canShare",
            fileName,
            bytes,
            contentType);
    }

    public static bool IsElogbookFile(BrowserFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return file.FileName.EndsWith(ElogbookExtension, StringComparison.OrdinalIgnoreCase);
    }

    public static void ValidateElogbookFile(BrowserFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Bytes is null)
        {
            throw new BrowserFileStoreException("Selected file did not include package bytes.");
        }

        if (!IsElogbookFile(file))
        {
            throw new BrowserFileStoreException("Selected file must use the .elogbook extension.");
        }

        if (file.Bytes.Length > MaxElogbookBytes)
        {
            throw new BrowserFileStoreException(
                $"Selected file is larger than the {MaxElogbookBytes} byte package limit.");
        }
    }

    public ValueTask DownloadAsync(
        string fileName,
        byte[] bytes,
        string contentType = ElogbookContentType)
    {
        ValidateExportArguments(fileName, bytes, contentType);

        return jsRuntime.InvokeVoidAsync(
            "electronicLogbookFiles.download",
            fileName,
            bytes,
            contentType);
    }

    public ValueTask ShareAsync(
        string fileName,
        byte[] bytes,
        string contentType = ElogbookContentType)
    {
        ValidateExportArguments(fileName, bytes, contentType);

        return jsRuntime.InvokeVoidAsync(
            "electronicLogbookFiles.share",
            fileName,
            bytes,
            contentType);
    }

    public async ValueTask ShareOrDownloadAsync(
        string fileName,
        byte[] bytes,
        string contentType = ElogbookContentType)
    {
        ValidateExportArguments(fileName, bytes, contentType);
        if (await CanShareAsync(fileName, bytes, contentType).ConfigureAwait(false))
        {
            await ShareAsync(fileName, bytes, contentType).ConfigureAwait(false);
            return;
        }

        await DownloadAsync(fileName, bytes, contentType).ConfigureAwait(false);
    }

    private static void ValidateExportArguments(string fileName, byte[] bytes, string contentType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        if (!fileName.EndsWith(ElogbookExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new BrowserFileStoreException($"Exported package file names must use the {ElogbookExtension} extension.");
        }

        if (bytes.Length > MaxElogbookBytes)
        {
            throw new BrowserFileStoreException(
                $"Exported package is larger than the {MaxElogbookBytes} byte package limit.");
        }
    }
}

public sealed record BrowserFile(
    string FileName,
    string ContentType,
    byte[] Bytes);

public sealed class BrowserFileStoreException(string message) : Exception(message);
