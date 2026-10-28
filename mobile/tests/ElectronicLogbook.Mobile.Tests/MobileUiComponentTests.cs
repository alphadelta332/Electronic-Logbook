using Bunit;
using ElectronicLogbook.Mobile.Layout;
using ElectronicLogbook.Mobile.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor.Services;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileUiComponentTests : BunitContext
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SettingsRemovesRoutineCopyButKeepsStorageErrorsAndThemeControls(bool storageBlocked)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(provider => new BrowserLogbookStore(provider.GetRequiredService<IJSRuntime>()));
        Services.AddSingleton(provider => new BrowserFileStore(provider.GetRequiredService<IJSRuntime>()));
        Services.AddSingleton(provider => new MobileUiPreferenceState(new BrowserUiPreferencesStore(provider.GetRequiredService<IJSRuntime>())));
        Services.AddSingleton(provider => new MobileLogbookSession(
            provider.GetRequiredService<BrowserLogbookStore>(),
            new BrowserPackageKeyStore(provider.GetRequiredService<IJSRuntime>())));
        JSInterop.Setup<string?>("electronicLogbookStore.load", "portable-document")
            .SetResult(storageBlocked ? "invalid json" : null);

        var page = Render<Settings>();

        page.WaitForAssertion(() => Assert.Equal(3, page.FindAll("[aria-label=Appearance] button").Count));
        Assert.Equal("true", page.Find("[aria-label=Appearance] button").GetAttribute("aria-pressed"));
        Assert.Empty(page.FindAll("[aria-label=Appearance]").Single().ParentElement!.QuerySelectorAll(".draft-pill"));
        Assert.DoesNotContain("New file", page.Markup);
        Assert.DoesNotContain("Create a new dated file", page.Markup);
        Assert.DoesNotContain("Workbook-faithful data is available offline", page.Markup);
        Assert.DoesNotContain("Sync is automatic.", page.Markup);
        var storage = page.FindAll(".device-health-item").Single(item => item.FirstElementChild?.TextContent == "Local storage");
        Assert.Equal(storageBlocked ? "Needs attention" : "Ready", storage.QuerySelector("strong")!.TextContent);
        if (storageBlocked)
            Assert.Equal(Services.GetRequiredService<MobileLogbookSession>().StorageError, storage.QuerySelector("small")!.TextContent);
        else
            Assert.Null(storage.QuerySelector("small"));
    }

    [Fact]
    public void StatusMetricMarksAttentionState()
    {
        var component = Render<StatusMetric>(parameters => parameters
            .Add(metric => metric.Label, "Unexported")
            .Add(metric => metric.Value, "3")
            .Add(metric => metric.Detail, "local operations")
            .Add(metric => metric.Attention, true));

        Assert.Contains("status-metric-attention", component.Markup);
        component.Find("strong").MarkupMatches("<strong>3</strong>");
    }

    [Fact]
    public void ActionFeedbackNavigatesUsingTheSharedActionButton()
    {
        Services.AddMudServices();
        Services.AddSingleton(serviceProvider =>
        {
            var jsRuntime = serviceProvider.GetRequiredService<IJSRuntime>();
            return new MobileLogbookSession(
                new BrowserLogbookStore(jsRuntime),
                new BrowserPackageKeyStore(jsRuntime));
        });
        var session = Services.GetRequiredService<MobileLogbookSession>();
        session.ShowActionFeedback("Unable to remove field.", "Show me", "/flights?view=entries");

        var component = Render<MobileActionFeedback>();

        component.Find("button").Click();

        Assert.EndsWith("/flights?view=entries", Services.GetRequiredService<NavigationManager>().Uri);
        Assert.False(session.HasPendingActionFeedback);
    }
}
