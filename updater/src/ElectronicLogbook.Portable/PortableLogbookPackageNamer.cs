namespace ElectronicLogbook.Portable;

public static class PortableLogbookPackageNamer
{
    public static string CreateExportFileName(LogbookId logbookId, DateTimeOffset exportedAt)
    {
        var safeLogbookId = new string(logbookId.Value.Select(character =>
            char.IsLetterOrDigit(character) || character is '_' or '-'
                ? character
                : '_').ToArray());
        return $"{safeLogbookId}_{exportedAt:yyyyMMdd_HHmmss}{PortableLogbookPackageFile.Extension}";
    }
}
