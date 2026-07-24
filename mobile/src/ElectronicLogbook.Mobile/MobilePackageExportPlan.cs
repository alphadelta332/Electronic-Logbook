using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public static class MobilePackageExportPlan
{
    public static MobilePackageExportPlanResult Create(
        PortableLogbookDocumentV2 document,
        bool hasPackageKey,
        DateTimeOffset exportedAt)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!hasPackageKey)
        {
            throw new MobilePackageExportPlanException(
                "A browser-held package key is required before exporting a portable logbook package.");
        }

        return new MobilePackageExportPlanResult(
            PortableLogbookPackageNamer.CreateExportFileName(document.LogbookId, exportedAt),
            BrowserFileStore.ElogbookContentType,
            exportedAt);
    }

    public static MobilePackageExportPlanResult Create(
        PortableLogbookDocument document,
        bool hasPackageKey,
        DateTimeOffset exportedAt)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!hasPackageKey)
        {
            throw new MobilePackageExportPlanException(
                "A browser-held package key is required before exporting a portable logbook package.");
        }

        return new MobilePackageExportPlanResult(
            PortableLogbookPackageNamer.CreateExportFileName(document.LogbookId, exportedAt),
            BrowserFileStore.ElogbookContentType,
            exportedAt);
    }
}

public sealed record MobilePackageExportPlanResult(
    string FileName,
    string ContentType,
    DateTimeOffset ExportedAt);

public sealed class MobilePackageExportPlanException(string message) : Exception(message);
