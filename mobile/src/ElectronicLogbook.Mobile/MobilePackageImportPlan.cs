using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public static class MobilePackageImportPlan
{
    public static MobilePackageImportPlanResult Inspect(BrowserFile file)
    {
        BrowserFileStore.ValidateElogbookFile(file);
        var manifest = PortableLogbookPackage.ReadManifestForInspection(
            file.Bytes,
            new PortableLogbookPackageReadOptions(BrowserFileStore.MaxElogbookBytes));
        return new MobilePackageImportPlanResult(
            file.FileName,
            manifest.LogbookId,
            manifest.OperationCount,
            manifest.CreatedAt,
            manifest.SchemaVersion);
    }

    public static MobilePackageImportCompatibility CheckCompatibility(
        MobilePackageImportPlanResult plan,
        LogbookId localLogbookId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.LogbookId != localLogbookId)
        {
            return MobilePackageImportCompatibility.WrongLogbook;
        }

        return plan.SchemaVersion == PortableLogbookDocument.CurrentSchemaVersion
            ? MobilePackageImportCompatibility.Compatible
            : MobilePackageImportCompatibility.UnsupportedSchema;
    }
}

public sealed record MobilePackageImportPlanResult(
    string FileName,
    LogbookId LogbookId,
    int OperationCount,
    DateTimeOffset PackageCreatedAt,
    int SchemaVersion);

public enum MobilePackageImportCompatibility
{
    Compatible,
    WrongLogbook,
    UnsupportedSchema
}
