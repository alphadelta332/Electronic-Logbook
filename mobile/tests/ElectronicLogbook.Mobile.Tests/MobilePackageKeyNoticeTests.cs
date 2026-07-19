using ElectronicLogbook.Mobile;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobilePackageKeyNoticeTests
{
    [Theory]
    [InlineData("Ready", "stored only on this device")]
    [InlineData("Not set", "cannot be recovered")]
    [InlineData("Unavailable", "cannot create")]
    [InlineData("Checking", "Checking")]
    public void CreateReturnsStatusSpecificPackageKeyNotice(string status, string expectedText)
    {
        var notice = MobilePackageKeyNotice.Create(status);

        Assert.Contains(expectedText, notice, StringComparison.OrdinalIgnoreCase);
    }
}
