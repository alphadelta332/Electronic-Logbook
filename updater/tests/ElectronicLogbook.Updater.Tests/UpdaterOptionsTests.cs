namespace ElectronicLogbook.Updater.Tests;

public sealed class UpdaterOptionsTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"ElectronicLogbookUpdaterTests-{Guid.NewGuid():N}");
    private readonly string _sourcePath;

    public UpdaterOptionsTests()
    {
        Directory.CreateDirectory(_directory);
        _sourcePath = Path.Combine(_directory, "source.xlsm");
        File.WriteAllText(_sourcePath, "test");
    }

    [Fact]
    public void ParseRejectsSourceAsOutput()
    {
        var exception = Assert.Throws<UpdaterUsageException>(() =>
            UpdaterOptions.Parse(["--source", _sourcePath, "--output", _sourcePath]));

        Assert.Contains("must differ", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseRejectsExistingOutput()
    {
        var output = Path.Combine(_directory, "output.xlsm");
        File.WriteAllText(output, "test");

        var exception = Assert.Throws<UpdaterUsageException>(() =>
            UpdaterOptions.Parse(["--source", _sourcePath, "--output", output]));

        Assert.Contains("already exists", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseAcceptsLocalMasterMode()
    {
        var master = Path.Combine(_directory, "master.xlsm");
        var output = Path.Combine(_directory, "output.xlsm");
        File.WriteAllText(master, "test");

        var options = UpdaterOptions.Parse(
            ["--source", _sourcePath, "--output", output, "--master", master]);

        Assert.Equal(Path.GetFullPath(_sourcePath), options.SourcePath);
        Assert.Equal(Path.GetFullPath(output), options.OutputPath);
        Assert.Equal(Path.GetFullPath(master), options.MasterPath);
        Assert.False(options.InPlaceSwap);
    }

    [Fact]
    public void ParseAcceptsInPlaceFlag()
    {
        var output = Path.Combine(_directory, "output.xlsm");

        var options = UpdaterOptions.Parse(
            ["--source", _sourcePath, "--output", output, "--inplace"]);

        Assert.True(options.InPlaceSwap);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }
}
