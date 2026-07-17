namespace ElectronicLogbook.Updater;

internal static class MigrationRequestValidator
{
    public static MigrationRequest Validate(MigrationRequest request)
    {
        request = request with
        {
            SourcePath = Path.GetFullPath(request.SourcePath),
            MasterPath = Path.GetFullPath(request.MasterPath),
            OutputPath = Path.GetFullPath(request.OutputPath)
        };

        if (!File.Exists(request.SourcePath))
        {
            throw new FileNotFoundException("Source workbook not found.", request.SourcePath);
        }
        if (!File.Exists(request.MasterPath))
        {
            throw new FileNotFoundException("Master workbook not found.", request.MasterPath);
        }
        if (File.Exists(request.OutputPath))
        {
            throw new IOException($"Output path already exists: {request.OutputPath}");
        }
        if (string.Equals(request.SourcePath, request.OutputPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(request.MasterPath, request.OutputPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Output path must differ from source and master paths.");
        }

        return request;
    }
}
