using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookKeyRotationTests
{
    [Fact]
    public void RotatePackageKeyReencryptsPackageForNewKey()
    {
        var document = CreateDocument();
        var oldKey = PortableLogbookKey.Generate();
        var newKey = PortableLogbookKey.Generate();
        var packageBytes = PortableLogbookPackage.Write(document, oldKey);

        var rotated = PortableLogbookKeyRotation.RotatePackageKey(packageBytes, oldKey, newKey, document.LogbookId);
        var readWithNewKey = PortableLogbookPackage.Read(rotated.PackageBytes, newKey, document.LogbookId);

        Assert.Equal(document.LogbookId, rotated.LogbookId);
        Assert.Equal(document.Operations.Count, rotated.OperationCount);
        Assert.Equal(document.LogbookId, readWithNewKey.Document.LogbookId);
        Assert.Equal(document.Operations.Select(operation => operation.RevisionId), readWithNewKey.Document.Operations.Select(operation => operation.RevisionId));
    }

    [Fact]
    public void RotatePackageKeyMakesOldKeyUnableToReadRotatedPackage()
    {
        var document = CreateDocument();
        var oldKey = PortableLogbookKey.Generate();
        var newKey = PortableLogbookKey.Generate();
        var packageBytes = PortableLogbookPackage.Write(document, oldKey);

        var rotated = PortableLogbookKeyRotation.RotatePackageKey(packageBytes, oldKey, newKey, document.LogbookId);

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackage.Read(rotated.PackageBytes, oldKey, document.LogbookId));
        Assert.Equal(PortableLogbookPackageError.AuthenticationFailed, exception.Error);
    }

    [Fact]
    public void RotatePackageKeyRejectsWrongOldKey()
    {
        var document = CreateDocument();
        var packageBytes = PortableLogbookPackage.Write(document, PortableLogbookKey.Generate());

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookKeyRotation.RotatePackageKey(
                packageBytes,
                PortableLogbookKey.Generate(),
                PortableLogbookKey.Generate(),
                document.LogbookId));

        Assert.Equal(PortableLogbookPackageError.AuthenticationFailed, exception.Error);
    }

    [Fact]
    public void RotatePackageKeyRejectsWrongLogbookBeforeDecrypting()
    {
        var document = CreateDocument();
        var key = PortableLogbookKey.Generate();
        var packageBytes = PortableLogbookPackage.Write(document, key);

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookKeyRotation.RotatePackageKey(
                packageBytes,
                key,
                PortableLogbookKey.Generate(),
                new LogbookId("log_other")));

        Assert.Equal(PortableLogbookPackageError.WrongLogbook, exception.Error);
    }

    [Fact]
    public void RotatePackageKeyHonoursConfiguredPackageReadLimit()
    {
        var document = CreateDocument();
        var oldKey = PortableLogbookKey.Generate();
        var packageBytes = PortableLogbookPackage.Write(document, oldKey);
        var readOptions = new PortableLogbookPackageReadOptions(packageBytes.Length - 1);

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookKeyRotation.RotatePackageKey(
                packageBytes,
                oldKey,
                PortableLogbookKey.Generate(),
                document.LogbookId,
                readOptions));

        Assert.Equal(PortableLogbookPackageError.PackageTooLarge, exception.Error);
    }

    private static PortableLogbookDocument CreateDocument()
    {
        var create = new CreateEntryOperation(
            new LogbookId("log_rotation"),
            new EntryId("ent_1"),
            new RevisionId("rev_1"),
            new DeviceId("dev_excel"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            PortableLogbookEntry.Empty with
            {
                Date = new DateOnly(2026, 7, 18),
                AircraftType = "C172",
                Registration = "VH-ABC",
                From = "YSBK",
                To = "YSBK",
                PilotInCommand = 1.2m
            });

        return PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [create]);
    }
}
