namespace ElectronicLogbook.Portable;

public static class PortableLogbookSetup
{
    public static PortableLogbookSetupPlan CreateInitialSetupPlan(
        IEnumerable<PortableLogbookEntry> existingEntries,
        IEnumerable<CustomFieldDefinition> customFieldDefinitions,
        DateTimeOffset createdAt,
        LogbookId? logbookId = null,
        DeviceId? deviceId = null,
        PortableLogbookKey? key = null,
        PortableLogbookIdFactory? idFactory = null)
    {
        var resolvedLogbookId = logbookId ?? LogbookId.New();
        var resolvedDeviceId = deviceId ?? DeviceId.New();
        var resolvedKey = key ?? PortableLogbookKey.Generate();
        var document = PortableLogbookInitializer.CreateInitialDocument(
            existingEntries,
            customFieldDefinitions,
            resolvedLogbookId,
            resolvedDeviceId,
            createdAt,
            idFactory);
        var packageBytes = PortableLogbookPackage.Write(document, resolvedKey);
        return new PortableLogbookSetupPlan(resolvedLogbookId, resolvedDeviceId, resolvedKey, document, packageBytes);
    }
}

public sealed record PortableLogbookSetupPlan(
    LogbookId LogbookId,
    DeviceId DeviceId,
    PortableLogbookKey Key,
    PortableLogbookDocument InitialDocument,
    byte[] InitialPackageBytes);
