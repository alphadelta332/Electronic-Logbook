namespace ElectronicLogbook.Mobile.Tests;

public sealed class PwaPageWiringTests
{
    [Fact]
    public void HomePageWiresUserFacingPackageExchangeControls()
    {
        var page = ReadMobilePage("Home.razor");

        Assert.Contains("Set up key", page, StringComparison.Ordinal);
        Assert.Contains("Preview package", page, StringComparison.Ordinal);
        Assert.Contains("Import package", page, StringComparison.Ordinal);
        Assert.Contains("Export package", page, StringComparison.Ordinal);
        Assert.Contains("SetupPackageKeyAsync", page, StringComparison.Ordinal);
        Assert.Contains("ApplyPackageAsync", page, StringComparison.Ordinal);
        Assert.Contains("ExportPackageAsync", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Package import and export are pending secure exchange wiring.", page, StringComparison.Ordinal);
    }

    [Fact]
    public void HomePageWiresCustomFieldDefinitionResolutionControls()
    {
        var page = ReadMobilePage("Home.razor");

        Assert.Contains("custom-field-conflicts", page, StringComparison.Ordinal);
        Assert.Contains("Keep local labels", page, StringComparison.Ordinal);
        Assert.Contains("Use imported labels", page, StringComparison.Ordinal);
        Assert.Contains("ResolveCustomFieldsAsync", page, StringComparison.Ordinal);
        Assert.Contains("PortableLogbookCustomFieldDefinitionChoice.KeepLocal", page, StringComparison.Ordinal);
        Assert.Contains("PortableLogbookCustomFieldDefinitionChoice.UseIncoming", page, StringComparison.Ordinal);
    }

    [Fact]
    public void HomePageShowsSharedDraftValidationAfterDraftEditingStarts()
    {
        var page = ReadMobilePage("Home.razor");

        Assert.Contains("@oninput=\"MarkDraftEdited\"", page, StringComparison.Ordinal);
        Assert.Contains("@onchange=\"MarkDraftEdited\"", page, StringComparison.Ordinal);
        Assert.Contains("ShouldShowDraftErrors", page, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void HomePageOffersRecentFlightNumberAndRouteSuggestions()
    {
        var page = ReadMobilePage("Home.razor");

        Assert.Contains("list=\"flight-numbers\"", page, StringComparison.Ordinal);
        Assert.Contains("datalist id=\"flight-numbers\"", page, StringComparison.Ordinal);
        Assert.Contains("RecentValues(entry => entry.FlightNumber)", page, StringComparison.Ordinal);
        Assert.Contains("list=\"routes\"", page, StringComparison.Ordinal);
        Assert.Contains("datalist id=\"routes\"", page, StringComparison.Ordinal);
        Assert.Contains("RecentValues(entry => entry.Route)", page, StringComparison.Ordinal);
    }

    private static string ReadMobilePage(string relativePath) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "ElectronicLogbook.Mobile",
            "Pages",
            relativePath)));
}
