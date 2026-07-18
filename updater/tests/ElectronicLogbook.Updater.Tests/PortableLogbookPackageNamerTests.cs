using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookPackageNamerTests
{
    [Fact]
    public void CreateExportFileNameUsesLogbookIdTimestampAndElogbookExtension()
    {
        var fileName = PortableLogbookPackageNamer.CreateExportFileName(
            new LogbookId("log_abc"),
            DateTimeOffset.Parse("2026-07-18T03:04:05Z"));

        Assert.Equal("log_abc_20260718_030405.elogbook", fileName);
    }

    [Fact]
    public void CreateExportFileNameSanitizesUnexpectedLogbookIdCharacters()
    {
        var fileName = PortableLogbookPackageNamer.CreateExportFileName(
            new LogbookId("log:abc/def"),
            DateTimeOffset.Parse("2026-07-18T03:04:05Z"));

        Assert.Equal("log_abc_def_20260718_030405.elogbook", fileName);
    }
}
