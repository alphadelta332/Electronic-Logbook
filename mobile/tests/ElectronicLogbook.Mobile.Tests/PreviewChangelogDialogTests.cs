using Bunit;
using ElectronicLogbook.Mobile.Layout;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class PreviewChangelogDialogTests : BunitContext
{
    [Fact]
    public void UpdatedPreviewShowsHeadlinesAndNewIconAndCanBeAcknowledged()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<MobilePreviewInstallationInfo?>("electronicLogbookPreviewUpdates.getInstallationInfo")
            .SetResult(new(true, MobilePreviewChangelog.Current.VersionName,
                MobilePreviewChangelog.Current.VersionCode, true, 0));
        Services.AddMudServices();
        Services.AddScoped<BrowserPreviewUpdateState>();

        var dialog = Render<PreviewChangelogDialog>();

        dialog.WaitForAssertion(() => Assert.Equal(3, dialog.FindAll(".preview-changelog-list li").Count));
        Assert.Equal(new[] { "Charts & Stats", "Smarter airport suggestions", "Automatic airport database updates" },
            dialog.FindAll(".preview-changelog-list strong").Select(item => item.TextContent));
        Assert.Equal(3, dialog.FindAll(".preview-changelog-list li > svg").Count);
        Assert.Equal(3, dialog.FindComponents<MudIcon>().Count(icon => icon.Instance.Icon == Icons.Material.Filled.CheckCircle));
        var badge = Assert.Single(dialog.FindAll(".preview-changelog-list span"));
        Assert.Equal("New", badge.TextContent.Trim());
        Assert.Equal("Charts & Stats", badge.PreviousElementSibling!.TextContent);
        Assert.Equal(2, dialog.FindComponents<MudIcon>().Count(icon => icon.Instance.Icon == Icons.Material.Filled.AutoAwesome));

        dialog.Find("button").Click();

        dialog.WaitForAssertion(() => Assert.Empty(dialog.FindAll("[role='dialog']")));
        JSInterop.VerifyInvoke("electronicLogbookPreviewUpdates.acknowledgeInstalledVersion");
    }
}
