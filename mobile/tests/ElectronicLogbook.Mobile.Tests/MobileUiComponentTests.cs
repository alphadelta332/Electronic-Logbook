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
