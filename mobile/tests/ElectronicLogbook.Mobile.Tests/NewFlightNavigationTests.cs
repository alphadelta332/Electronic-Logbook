using System.Text.Json;
using Bunit;
using ElectronicLogbook.Mobile.Pages;
using ElectronicLogbook.Portable;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor.Services;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class NewFlightNavigationTests : BunitContext, IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;
    async Task IAsyncLifetime.DisposeAsync() => await base.DisposeAsync();

    [Theory]
    [InlineData("dashboard")]
    [InlineData("back")]
    [InlineData("direct")]
    public async Task NewRouteAfterEditingStartsANewFlight(string exit)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(provider => new BrowserLogbookStore(provider.GetRequiredService<IJSRuntime>()));
        Services.AddSingleton(provider => new MobileLogbookSession(
            provider.GetRequiredService<BrowserLogbookStore>(),
            new BrowserPackageKeyStore(provider.GetRequiredService<IJSRuntime>())));
        var logbookId = new LogbookId("log_navigation_test");
        var original = PortableLogbookWorkbookEntry.Empty with
        {
            Year = 2024, Month = 1, Day = 1, Type = "C172", Reg = "VH-TEST", Pic = "Self",
            From = "YMMB", To = "YSSY", SeCommandDay = 1.2m, Remarks = "Original saved entry"
        };
        var operation = PortableLogbookOperationV2.Create(logbookId, new EntryId("ent_navigation"),
            new RevisionId("rev_navigation"), new DeviceId("dev_navigation"), DateTimeOffset.UtcNow, original);
        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(logbookId, MobileLogbookSession.CustomFields,
            PortableLogbookCurrencyOverrideDates.Empty, [operation]);
        JSInterop.Setup<string?>("electronicLogbookStore.load", "portable-document")
            .SetResult(JsonSerializer.Serialize(document, PortableLogbookJson.SerializerOptions));
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/flights/ent_navigation/edit");
        RenderFragment<RouteData> found = route => builder =>
        {
            if (route.PageType == typeof(NewFlight))
            {
                builder.OpenComponent<RouteView>(0);
                builder.AddAttribute(1, nameof(RouteView.RouteData), route);
                builder.CloseComponent();
            }
        };
        var router = Render<Router>(parameters => parameters
            .Add(component => component.AppAssembly, typeof(NewFlight).Assembly)
            .Add(component => component.Found, found));
        var session = Services.GetRequiredService<MobileLogbookSession>();
        router.WaitForAssertion(() => Assert.Equal(new EntryId("ent_navigation"), session.EditingEntryId));
        Assert.Equal("Original saved entry", session.WorkbookDraft.Remarks);
        var savedEntry = Assert.Single(session.CurrentEntriesV2).Entry;

        if (exit == "dashboard")
            router.Find("a[aria-label='Back to dashboard']").Click();
        else if (exit == "back")
            await router.InvokeAsync(() => navigation.NavigateTo("/"));
        if (exit != "direct")
            router.WaitForAssertion(() => Assert.Empty(router.FindComponents<NewFlight>()));
        await router.InvokeAsync(() => navigation.NavigateTo("/flights/new"));

        router.WaitForAssertion(() => Assert.Null(router.FindComponent<NewFlight>().Instance.EntryId));
        router.WaitForAssertion(() => Assert.Null(session.EditingEntryId));
        Assert.Equal("Add flight", session.SaveLabel);
        Assert.False(session.ShouldShowLastActionMessage(MobileActionMessageSurface.Draft));
        Assert.DoesNotContain("Correction draft opened.", router.Markup);
        Assert.NotEqual("Original saved entry", session.WorkbookDraft.Remarks);
        Assert.Same(savedEntry, Assert.Single(session.CurrentEntriesV2).Entry);
        Assert.Single(session.DocumentV2.Operations);

        await router.InvokeAsync(() => navigation.NavigateTo("/flights/ent_navigation/edit"));
        router.WaitForAssertion(() => Assert.Equal(new EntryId("ent_navigation"), session.EditingEntryId));
        Assert.Equal("Original saved entry", session.WorkbookDraft.Remarks);

        await router.InvokeAsync(() => navigation.NavigateTo("/"));
        session.CloneWorkbookEntry(original);
        await router.InvokeAsync(() => navigation.NavigateTo("/flights/new"));
        router.WaitForAssertion(() => Assert.NotEmpty(router.FindComponents<NewFlight>()));
        Assert.Null(session.EditingEntryId);
        Assert.Equal("Original saved entry", session.WorkbookDraft.Remarks);
        Assert.Equal("Draft started from recent flight.", session.LastActionMessage);
        session.WorkbookDraft.Remarks = "Unfinished new flight";
        await router.InvokeAsync(() => navigation.NavigateTo("/"));
        await router.InvokeAsync(() => navigation.NavigateTo("/flights/new"));
        router.WaitForAssertion(() => Assert.NotEmpty(router.FindComponents<NewFlight>()));
        Assert.Equal("Unfinished new flight", session.WorkbookDraft.Remarks);
    }
}
