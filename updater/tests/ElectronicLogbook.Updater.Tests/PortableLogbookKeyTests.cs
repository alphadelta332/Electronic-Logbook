using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookKeyTests
{
    [Fact]
    public void GenerateCreatesThirtyTwoByteKey()
    {
        var key = PortableLogbookKey.Generate();

        Assert.Equal(PortableLogbookPackage.KeySizeBytes, key.ToBytes().Length);
    }

    [Fact]
    public void FromBytesCopiesInputAndToBytesReturnsCopy()
    {
        var bytes = Enumerable.Repeat((byte)7, PortableLogbookPackage.KeySizeBytes).ToArray();
        var key = PortableLogbookKey.FromBytes(bytes);

        bytes[0] = 99;
        var exported = key.ToBytes();
        exported[1] = 88;

        Assert.Equal(7, key.ToBytes()[0]);
        Assert.Equal(7, key.ToBytes()[1]);
    }

    [Fact]
    public void RecoveryCodeRoundTripsKey()
    {
        var original = PortableLogbookKey.FromBytes(Enumerable.Range(0, PortableLogbookPackage.KeySizeBytes).Select(i => (byte)i).ToArray());

        var roundTripped = PortableLogbookKey.FromRecoveryCode(original.ToRecoveryCode());

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void FromRecoveryCodeRejectsInvalidText()
    {
        var exception = Assert.Throws<ArgumentException>(() => PortableLogbookKey.FromRecoveryCode("not a key"));

        Assert.Equal("recoveryCode", exception.ParamName);
    }

    [Fact]
    public void PackageAcceptsPortableLogbookKeyObject()
    {
        var key = PortableLogbookKey.Generate();
        var create = new CreateEntryOperation(
            new LogbookId("log_key"),
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
        var document = PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [create]);

        var package = PortableLogbookPackage.Write(document, key);
        var read = PortableLogbookPackage.Read(package, key, document.LogbookId);

        Assert.Equal(document.LogbookId, read.Document.LogbookId);
    }
}
