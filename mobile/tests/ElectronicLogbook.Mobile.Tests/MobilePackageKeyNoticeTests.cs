using ElectronicLogbook.Mobile;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobilePackageKeyNoticeTests
{
    [Theory]
    [InlineData("Ready", "restore from the workbook recovery code")]
    [InlineData("Not set", "restore one from a workbook recovery code")]
    [InlineData("Unavailable", "cannot create")]
    [InlineData("Checking", "Checking")]
    public void CreateReturnsStatusSpecificPackageKeyNotice(string status, string expectedText)
    {
        var notice = MobilePackageKeyNotice.Create(status);

        Assert.Contains(expectedText, notice, StringComparison.OrdinalIgnoreCase);
    }
}
