namespace ElectronicLogbook.Mobile;

public static class MobilePackageKeyNotice
{
    public static string Create(string packageKeyStatus) =>
        packageKeyStatus switch
        {
            "Ready" => "Browser package key is stored only on this device. Keep separate backups; packages made with this browser key depend on this browser storage until recovery/enrolment is available.",
            "Not set" => "Set up the browser package key before importing or exporting encrypted packages. This preview key is device-local and cannot be recovered if browser storage is cleared.",
            "Unavailable" => "This browser cannot create the non-exportable package key required for encrypted package exchange.",
            _ => "Checking browser package-key support."
        };
}
