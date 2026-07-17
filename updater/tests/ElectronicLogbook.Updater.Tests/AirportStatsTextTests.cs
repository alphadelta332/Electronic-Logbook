namespace ElectronicLogbook.Updater.Tests;

public sealed class AirportStatsTextTests
{
    [Theory]
    [InlineData("YSSY-YMML", new[] { "YSSY", "YMML" })]
    [InlineData("YSSY WOL, YMML (YSCB)", new[] { "YSSY", "WOL", "YMML", "YSCB" })]
    [InlineData("YSSY|YMML", new[] { "YSSYYMML" })]
    public void TokeniseDetailsSplitsRouteDetails(string details, string[] expected)
    {
        Assert.Equal(expected, AirportStatsText.TokeniseDetails(details));
    }

    [Theory]
    [InlineData("IFR", true)]
    [InlineData("sim", true)]
    [InlineData("Training", true)]
    [InlineData("YSSY", false)]
    public void ShouldIgnoreTokenUsesBuiltInAndKeywordRules(string token, bool expected)
    {
        var keywords = new[] { "Training Area" };

        Assert.Equal(expected, AirportStatsText.ShouldIgnoreToken(token, keywords));
    }

    [Fact]
    public void AddEndpointAirportMatchAddsKnownAliasOnce()
    {
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SYD"] = "YSSY",
            ["YSSY"] = "YSSY"
        };

        AirportStatsText.AddEndpointAirportMatch(matched, aliases, " syd ");
        AirportStatsText.AddEndpointAirportMatch(matched, aliases, "YSSY");
        AirportStatsText.AddEndpointAirportMatch(matched, aliases, "UNKNOWN");

        Assert.Equal(new[] { "YSSY" }, matched);
    }
}
