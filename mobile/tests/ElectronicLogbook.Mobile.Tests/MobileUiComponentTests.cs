using Bunit;
using ElectronicLogbook.Mobile.Pages;

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
}
