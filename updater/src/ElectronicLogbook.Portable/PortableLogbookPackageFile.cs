namespace ElectronicLogbook.Portable;

public static class PortableLogbookPackageFile
{
    public const string Extension = ".elogbook";

    public static void Write(
        string path,
        PortableLogbookDocument document,
        PortableLogbookKey key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        EnsurePackageExtension(path);

        var packageBytes = PortableLogbookPackage.Write(document, key);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        File.WriteAllBytes(tempPath, packageBytes);
        File.Move(tempPath, path, overwrite: true);
    }

    public static PortableLogbookPackageReadResult Read(
        string path,
        PortableLogbookKey key,
        LogbookId? expectedLogbookId = null,
        PortableLogbookPackageReadOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        EnsurePackageExtension(path);

        options ??= PortableLogbookPackageReadOptions.Default;
        return PortableLogbookPackage.Read(ReadPackageFileBytes(path, options), key, expectedLogbookId, options);
    }

    public static PortableLogbookPackageManifest ReadManifest(
        string path,
        PortableLogbookPackageReadOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        EnsurePackageExtension(path);
        options ??= PortableLogbookPackageReadOptions.Default;

        return PortableLogbookPackage.ReadManifest(ReadPackageFileBytes(path, options), options);
    }

    public static PortableLogbookPackageManifest ReadManifestForInspection(
        string path,
        PortableLogbookPackageReadOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        EnsurePackageExtension(path);
        options ??= PortableLogbookPackageReadOptions.Default;

        return PortableLogbookPackage.ReadManifestForInspection(ReadPackageFileBytes(path, options), options);
    }

    private static byte[] ReadPackageFileBytes(string path, PortableLogbookPackageReadOptions options)
    {
        var fileInfo = new FileInfo(path);
        if (fileInfo.Length > options.MaxPackageBytes)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.PackageTooLarge,
                $"Package is larger than the configured {options.MaxPackageBytes} byte limit.");
        }

        return File.ReadAllBytes(path);
    }

    private static void EnsurePackageExtension(string path)
    {
        if (!string.Equals(Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Portable logbook package files must use the '{Extension}' extension.", nameof(path));
        }
    }
}
