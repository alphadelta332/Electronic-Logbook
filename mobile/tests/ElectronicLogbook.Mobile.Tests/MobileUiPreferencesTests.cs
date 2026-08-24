using ElectronicLogbook.Mobile;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileUiPreferencesTests
{
    [Theory]
    [InlineData("Red")]
    [InlineData("Orange")]
    [InlineData("Amber")]
    [InlineData("Gold")]
    public void ReservedSemanticColoursAreNotAccentOptions(string accent)
    {
        Assert.DoesNotContain(accent, MobileUiPreferences.AccentOptions);
        Assert.Equal(
            MobileUiPreferences.DefaultAccent,
            MobileUiPreferences.Parse($"Dark|{accent}").Accent);
    }
}
