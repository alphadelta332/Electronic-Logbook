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

    [Fact]
    public void ParseAcceptsNoInPlaceAfterInPlaceFlag()
    {
        var output = Path.Combine(_directory, "output.xlsm");

        var options = UpdaterOptions.Parse(
            ["--source", _sourcePath, "--output", output, "--inplace", "--no-inplace"]);

        Assert.False(options.InPlaceSwap);
    }

    [Fact]
    public void ParseAcceptsReportPath()
    {
        var output = Path.Combine(_directory, "output.xlsm");
        var report = Path.Combine(_directory, "diagnostics", "report.json");

        var options = UpdaterOptions.Parse(
            ["--source", _sourcePath, "--output", output, "--report", report]);

        Assert.Equal(Path.GetFullPath(report), options.ReportPath);
    }

    [Theory]
    [InlineData("--source")]
    [InlineData("--output")]
    [InlineData("--master")]
    [InlineData("--report")]
    [InlineData("--repo")]
    public void ParseRejectsMissingOptionValue(string option)
    {
        var exception = Assert.Throws<UpdaterUsageException>(() =>
            UpdaterOptions.Parse([option]));

        Assert.Contains("requires a value", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseRejectsInvalidRepositoryFormat()
    {
        var output = Path.Combine(_directory, "output.xlsm");

        var exception = Assert.Throws<UpdaterUsageException>(() =>
            UpdaterOptions.Parse(["--source", _sourcePath, "--output", output, "--repo", "repo-only"]));

        Assert.Contains("owner/name", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseRejectsMissingSourceWorkbook()
    {
        var output = Path.Combine(_directory, "output.xlsm");
        var missingSource = Path.Combine(_directory, "missing.xlsm");

        var exception = Assert.Throws<UpdaterUsageException>(() =>
            UpdaterOptions.Parse(["--source", missingSource, "--output", output]));

        Assert.Contains("Source workbook not found", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseRejectsMissingMasterWorkbook()
    {
        var output = Path.Combine(_directory, "output.xlsm");
        var missingMaster = Path.Combine(_directory, "missing-master.xlsm");

        var exception = Assert.Throws<UpdaterUsageException>(() =>
            UpdaterOptions.Parse(["--source", _sourcePath, "--output", output, "--master", missingMaster]));

        Assert.Contains("Master workbook not found", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("source.xlsx", "output.xlsm")]
    [InlineData("source.xlsm", "output.xlsx")]
    public void ParseRejectsNonMacroWorkbookExtension(string sourceName, string outputName)
    {
        var source = Path.Combine(_directory, sourceName);
        var output = Path.Combine(_directory, outputName);
        File.WriteAllText(source, "test");

        var exception = Assert.Throws<UpdaterUsageException>(() =>
            UpdaterOptions.Parse(["--source", source, "--output", output]));

        Assert.Contains(".xlsm", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HelpDescribesSeparateOutputAsDefault()
    {
        Assert.Contains("separate file (the default)", UpdaterOptions.HelpText);
        Assert.Contains("source workbook is left unchanged", UpdaterOptions.HelpText);
        Assert.Contains("redacted diagnostic report", UpdaterOptions.HelpText);
    }

    [Fact]
    public async Task RunAsyncReturnsSuccessForHelp()
    {
        using var output = new StringWriter();
        var originalOutput = Console.Out;
        Console.SetOut(output);
        try
        {
            var exitCode = await UpdaterProgram.RunAsync(["--help"]);

            Assert.Equal(0, exitCode);
            Assert.Contains("Usage:", output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOutput);
        }
    }

    [Fact]
    public async Task RunAsyncReturnsUsageCodeForInvalidArguments()
    {
        using var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);
        try
        {
            var exitCode = await UpdaterProgram.RunAsync(["--unknown"]);

            Assert.Equal(2, exitCode);
            Assert.Contains("Unknown argument", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }
}
