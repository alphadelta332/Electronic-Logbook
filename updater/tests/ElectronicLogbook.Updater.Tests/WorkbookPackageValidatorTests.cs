namespace ElectronicLogbook.Updater.Tests;

public sealed class WorkbookPackageValidatorTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"WorkbookPackageValidatorTests-{Guid.NewGuid():N}");

    public WorkbookPackageValidatorTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void ValidateStagedWorkbookAcceptsPackageWithRequiredVersionNamesAndTables()
    {
        var path = TestRepo.CreateMinimalWorkbookPackage(_directory, TestRepo.Version);

        WorkbookPackageValidator.ValidateStagedWorkbook(path, TestRepo.Version);
    }

    [Fact]
    public void ValidateStagedWorkbookRejectsVersionMismatch()
    {
        var path = TestRepo.CreateMinimalWorkbookPackage(_directory, "0.0.1");

        var exception = Assert.Throws<InvalidDataException>(() =>
            WorkbookPackageValidator.ValidateStagedWorkbook(path, TestRepo.Version));

        Assert.Contains("version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateStagedWorkbookRejectsMissingRequiredTable()
    {
        var path = TestRepo.CreateMinimalWorkbookPackage(
            _directory,
            TestRepo.Version,
            includeAirportsTable: false);

        var exception = Assert.Throws<InvalidDataException>(() =>
            WorkbookPackageValidator.ValidateStagedWorkbook(path, TestRepo.Version));

        Assert.Contains("Airports", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateStagedWorkbookRejectsNonPackageFile()
    {
        var path = Path.Combine(_directory, "not-a-workbook.xlsm");
        File.WriteAllText(path, "not a zip package");

        Assert.Throws<InvalidDataException>(() =>
            WorkbookPackageValidator.ValidateStagedWorkbook(path, TestRepo.Version));
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

}
