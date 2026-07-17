namespace ElectronicLogbook.Updater.Tests;

public sealed class MigrationRequestValidatorTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"MigrationRequestValidatorTests-{Guid.NewGuid():N}");
    private readonly string _sourcePath;
    private readonly string _masterPath;
    private readonly string _outputPath;

    public MigrationRequestValidatorTests()
    {
        Directory.CreateDirectory(_directory);
        _sourcePath = Path.Combine(_directory, "source.xlsm");
        _masterPath = Path.Combine(_directory, "master.xlsm");
        _outputPath = Path.Combine(_directory, "output.xlsm");
        File.WriteAllText(_sourcePath, "source");
        File.WriteAllText(_masterPath, "master");
    }

    [Fact]
    public void ValidateAcceptsAvailableDistinctPaths()
    {
        var request = new MigrationRequest(
            _sourcePath,
            _masterPath,
            _outputPath,
            Manifest: null);

        var validated = MigrationRequestValidator.Validate(request);

        Assert.Equal(Path.GetFullPath(_sourcePath), validated.SourcePath);
        Assert.Equal(Path.GetFullPath(_masterPath), validated.MasterPath);
        Assert.Equal(Path.GetFullPath(_outputPath), validated.OutputPath);
    }

    [Fact]
    public void ValidateRejectsMissingSourceWorkbook()
    {
        var request = new MigrationRequest(
            Path.Combine(_directory, "missing-source.xlsm"),
            _masterPath,
            _outputPath,
            Manifest: null);

        Assert.Throws<FileNotFoundException>(() =>
            MigrationRequestValidator.Validate(request));
    }

    [Fact]
    public void ValidateRejectsMissingMasterWorkbook()
    {
        var request = new MigrationRequest(
            _sourcePath,
            Path.Combine(_directory, "missing-master.xlsm"),
            _outputPath,
            Manifest: null);

        Assert.Throws<FileNotFoundException>(() =>
            MigrationRequestValidator.Validate(request));
    }

    [Fact]
    public void ValidateRejectsExistingOutputWorkbook()
    {
        File.WriteAllText(_outputPath, "existing");
        var request = new MigrationRequest(
            _sourcePath,
            _masterPath,
            _outputPath,
            Manifest: null);

        Assert.Throws<IOException>(() =>
            MigrationRequestValidator.Validate(request));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("master")]
    public void ValidateRejectsOutputThatMatchesInputWorkbook(string input)
    {
        var output = input == "source" ? _sourcePath : _masterPath;
        var request = new MigrationRequest(
            _sourcePath,
            _masterPath,
            output,
            Manifest: null);

        Assert.Throws<IOException>(() =>
            MigrationRequestValidator.Validate(request));
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }
}
