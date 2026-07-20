namespace ElectronicLogbook.Mobile;

public static class MobilePackageKeyNotice
{
    public static string Create(string packageKeyStatus) =>
        packageKeyStatus switch
        {
            "Ready" => "Browser package key is stored only on this device. Keep separate backups; restore from the workbook recovery code if browser storage is cleared.",
            "Not set" => "Set up a new browser package key or restore one from a workbook recovery code before importing or exporting encrypted packages.",
            "Unavailable" => "This browser cannot create the non-exportable package key required for encrypted package exchange.",
            _ => "Checking browser package-key support."
        };
}
