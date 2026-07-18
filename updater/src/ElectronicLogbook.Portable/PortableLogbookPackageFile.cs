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

        var fileInfo = new FileInfo(path);
        options ??= PortableLogbookPackageReadOptions.Default;
        if (fileInfo.Length > options.MaxPackageBytes)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.PackageTooLarge,
                $"Package is larger than the configured {options.MaxPackageBytes} byte limit.");
        }

        return PortableLogbookPackage.Read(File.ReadAllBytes(path), key, expectedLogbookId, options);
    }

    public static PortableLogbookPackageManifest ReadManifest(
        string path,
        PortableLogbookPackageReadOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        EnsurePackageExtension(path);
        options ??= PortableLogbookPackageReadOptions.Default;

        var fileInfo = new FileInfo(path);
        if (fileInfo.Length > options.MaxPackageBytes)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.PackageTooLarge,
                $"Package is larger than the configured {options.MaxPackageBytes} byte limit.");
        }

        return PortableLogbookPackage.ReadManifest(File.ReadAllBytes(path), options);
    }

    private static void EnsurePackageExtension(string path)
    {
        if (!string.Equals(Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Portable logbook package files must use the '{Extension}' extension.", nameof(path));
        }
    }
}
