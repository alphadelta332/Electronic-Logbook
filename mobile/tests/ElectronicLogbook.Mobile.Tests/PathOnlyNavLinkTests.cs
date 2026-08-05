using Bunit;
using ElectronicLogbook.Mobile.Layout;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class PathOnlyNavLinkTests : TestContext
{
    [Fact]
    public void ExactPathMatchIgnoresQueryAndFragmentButNotChildRoutes()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/flights?view=totals#summary");

        var component = RenderComponent<PathOnlyNavLink>(parameters => parameters
            .Add(link => link.Href, "/flights")
            .AddChildContent("Logbook"));

        Assert.Contains("active", component.Find("a").ClassList);

        navigation.NavigateTo("/flights/new");

        component.WaitForAssertion(() =>
            Assert.DoesNotContain("active", component.Find("a").ClassList));
    }
}
