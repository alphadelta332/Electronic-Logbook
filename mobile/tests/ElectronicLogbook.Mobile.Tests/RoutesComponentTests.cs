using System.Text.Json;
using Bunit;
using ElectronicLogbook.Mobile.Pages;
using ElectronicLogbook.Portable;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Extensions;
using MudBlazor.Services;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class RoutesComponentTests : BunitContext, IAsyncLifetime
{
    public RoutesComponentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./js/routeMap.mjs").Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton(provider => new BrowserLogbookStore(provider.GetRequiredService<IJSRuntime>()));
        Services.AddSingleton(provider => new MobileLogbookSession(
            provider.GetRequiredService<BrowserLogbookStore>(), new BrowserPackageKeyStore(provider.GetRequiredService<IJSRuntime>())));
    }

    public Task InitializeAsync() => Task.CompletedTask;
    async Task IAsyncLifetime.DisposeAsync() => await base.DisposeAsync();

    [Fact]
    public void FiltersMatchChartsAndSelectionSurvivesCollapseUntilCleared()
    {
        Load(Flight(), Flight() with { Type = "OTHER", Reg = "VH-OTHER" });
        var charts = Render<Charts>();
        var page = Render<Routes>();
        var expected = charts.FindComponent<FilterToggleButton>().FindComponent<MudButton>().Instance;
        var actual = page.FindComponent<FilterToggleButton>().FindComponent<MudButton>().Instance;
        Assert.Equal(expected.Variant, actual.Variant);
        Assert.Equal(expected.StartIcon, actual.StartIcon);
        Assert.Equal(expected.Size, actual.Size);
        Assert.Equal(new[] { "Start date", "End date" }, page.FindAll(".date-range-fields label").Select(label => label.TextContent.Trim()));
        Assert.True(page.Find(".routes-filter-panel").HasAttribute("hidden"));
        page.Find(".logbook-filter-button").Click();
        page.Find("#routes-from").Change("2024-01-01");
        page.Find("#routes-to").Change("2024-12-31");
        Assert.Equal("2024-12-31", page.Find("#routes-from").GetAttribute("max"));
        Assert.Equal("2024-01-01", page.Find("#routes-to").GetAttribute("min"));
        SetFilter(page, "Aircraft type", " type ");
        SetFilter(page, "Registration", "vh-test");
        Assert.Single(page.FindComponent<RouteMapCanvas>().Instance.Data.Flights);
        Assert.Contains("Filters (3)", page.Find(".logbook-filter-button").TextContent);
        page.Find(".logbook-filter-button").Click();
        page.Find(".logbook-filter-button").Click();
        Assert.Equal("2024-01-01", page.Find("#routes-from").GetAttribute("value"));
        page.Find(".routes-filter-summary button").Click();
        Assert.Equal(2, page.FindComponent<RouteMapCanvas>().Instance.Data.Flights.Count);
        Assert.Equal("Filters", page.Find(".logbook-filter-button").TextContent.Trim());
        Assert.All(page.FindComponents<MudSelect<string>>(), select => Assert.Empty(select.Instance.GetState(component => component.SelectedValues)!));
    }

    [Fact]
    public void RouteFrequencyUsesFullLogbookAcrossDateAndAircraftFilters()
    {
        Load(Flight(), Flight() with { Year = 2023, Type = "OTHER", Reg = "VH-OTHER" },
            Flight() with { To = "YMML", Type = "OTHER" }, Flight() with { To = "YMML", Type = "OTHER" },
            Flight() with { To = "YMML", Type = "OTHER" });
        var page = Render<Routes>();
        page.Find("#routes-from").Change("2024-01-01");
        SetFilter(page, "Aircraft type", "TYPE");
        SetFilter(page, "Registration", "VH-TEST");
        Assert.Single(page.FindComponent<RouteMapCanvas>().Instance.Data.Flights);
        var call = JSInterop.Invocations.Last(invocation => invocation.Identifier == "update");
        var payload = JsonSerializer.SerializeToElement(call.Arguments[1], new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(3, payload.GetProperty("maximumRouteCount").GetInt32());
        var connection = Assert.Single(payload.GetProperty("connections").EnumerateArray());
        Assert.Equal(1, connection.GetProperty("count").GetInt32());
        Assert.Equal(2, connection.GetProperty("frequencyCount").GetInt32());
        page.Find(".routes-filter-summary button").Click();
        call = JSInterop.Invocations.Last(invocation => invocation.Identifier == "update");
        payload = JsonSerializer.SerializeToElement(call.Arguments[1], new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(3, payload.GetProperty("maximumRouteCount").GetInt32());
        Assert.Equal(2, payload.GetProperty("connections").EnumerateArray()
            .Single(line => line.GetProperty("to").GetString() == "YSSY").GetProperty("frequencyCount").GetInt32());
    }

    [Fact]
    public void MapHasOnlyRequestedControlsAndAirportPopupData()
    {
        Load(Flight() with { Via = "YSSY YMMB" }, Flight() with { From = "YSSY", To = "YMMB" }, Flight() with { From = "YMMB", To = "YMMB" });
        var page = Render<Routes>();
        Assert.Single(page.FindComponent<RouteMapCanvas>().Instance.Data.Connections);
        Assert.Empty(page.FindAll(".routes-summary, .routes-help, .route-list, .route-selection, .route-flights"));
        Assert.DoesNotContain("Lines connect", page.Markup);
        Assert.DoesNotContain("Offline world map", page.Markup);
        Assert.DoesNotContain("Incomplete routes", page.Markup);
        var call = JSInterop.Invocations.Single(invocation => invocation.Identifier == "update");
        var payload = JsonSerializer.SerializeToElement(call.Arguments[1], new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var airport = payload.GetProperty("markers").EnumerateArray().Single(marker => marker.GetProperty("icao").GetString() == "YMMB");
        Assert.False(string.IsNullOrWhiteSpace(airport.GetProperty("name").GetString()));
        Assert.Equal(3, airport.GetProperty("count").GetInt32());
        Assert.Equal(new[] { "ent_0", "ent_1", "ent_2" }, airport.GetProperty("flightIds").EnumerateArray().Select(id => id.GetString()).Order());
        var connection = Assert.Single(payload.GetProperty("connections").EnumerateArray());
        Assert.Equal(new[] { "ent_0", "ent_1" }, connection.GetProperty("flightIds").EnumerateArray().Select(id => id.GetString()).Order());
    }

    [Fact]
    public void EmptyUnknownAndInvalidDateSelectionsHaveUsableStates()
    {
        Load(Flight() with { From = "UNKNOWN", To = null, Via = "OTHER" });
        var page = Render<Routes>();
        Assert.Empty(page.FindComponents<RouteMapCanvas>());
        Assert.Contains("No recognised airports", page.Markup);
        Assert.Empty(page.FindAll(".route-incomplete, a[href^='/flights/']"));
        page.Find("#routes-from").Change("2025-01-01");
        Assert.Contains("No flights match", page.Markup);
        page.Find("#routes-to").Change("2024-01-01");
        Assert.Contains("Choose a Start date on or before", page.Find("[role=alert]").TextContent);
        Assert.Empty(page.FindComponents<RouteMapCanvas>());
        page.Find(".routes-filter-summary button").Click();
        Assert.Contains("No recognised airports", page.Markup);
    }

    [Fact]
    public void SavedChartsBasesMarkOnlyMatchingMapAirports()
    {
        Load(Flight());
        JSInterop.Setup<string?>("electronicLogbookStore.load", "analytics-bases:log_routes_test").SetResult("[\"ymmb\"]");
        var page = Render<Routes>();
        var call = JSInterop.Invocations.Single(invocation => invocation.Identifier == "update");
        var payload = JsonSerializer.SerializeToElement(call.Arguments[1], new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var airports = payload.GetProperty("markers").EnumerateArray().ToArray();
        Assert.True(airports.Single(airport => airport.GetProperty("icao").GetString() == "YMMB").GetProperty("isBase").GetBoolean());
        Assert.False(airports.Single(airport => airport.GetProperty("icao").GetString() == "YSSY").GetProperty("isBase").GetBoolean());
    }

    [Theory]
    [InlineData("invalid json")]
    [InlineData("[\"UNKNOWN\"]")]
    public void UnreadableBasesKeepMapAvailableWithWarning(string savedBases)
    {
        Load(Flight());
        JSInterop.Setup<string?>("electronicLogbookStore.load", "analytics-bases:log_routes_test").SetResult(savedBases);
        var page = Render<Routes>();
        Assert.Contains("Saved bases could not be loaded", page.Markup);
        Assert.Empty(page.FindComponent<RouteMapCanvas>().Instance.BaseAirports);
        Assert.Single(page.FindComponent<RouteMapCanvas>().Instance.Data.Flights);
    }

    [Fact]
    public async Task DeletionUpdatesMap()
    {
        Load(Flight());
        var page = Render<Routes>();
        var session = Services.GetRequiredService<MobileLogbookSession>();
        await page.InvokeAsync(() => session.DeleteWorkbookEntryAsync(Assert.Single(session.CurrentEntriesV2)));
        page.WaitForAssertion(() => Assert.Empty(page.FindComponents<RouteMapCanvas>()));
        Assert.Contains("No flights match", page.Markup);
    }

    [Fact]
    public async Task RendererFailureIsConciseAndDisposalReleasesResources()
    {
        Load(Flight());
        var module = JSInterop.SetupModule("./js/routeMap.mjs");
        module.SetupVoid("update", _ => true).SetException(new JSException("WebGL unavailable"));
        var page = Render<Routes>();
        page.WaitForAssertion(() => Assert.Contains("Map unavailable. Reopen this screen to retry.", page.Markup));
        Assert.Empty(page.FindAll(".route-list"));
        await page.FindComponent<RouteMapCanvas>().Instance.DisposeAsync();
        Assert.Contains(module.Invocations, invocation => invocation.Identifier == "dispose");
    }

    [Fact]
    public void BlockedStorageDoesNotShowPlausibleEmptyMapOrFilters()
    {
        JSInterop.Setup<string?>("electronicLogbookStore.load", "portable-document").SetResult("invalid json");
        var page = Render<Routes>();
        Assert.Empty(page.FindComponents<FilterToggleButton>());
        Assert.Empty(page.FindComponents<RouteMapCanvas>());
        Assert.Empty(page.FindAll(".routes-summary"));
        Assert.Contains("mud-alert", page.Markup);
    }

    private static void SetFilter(IRenderedComponent<Routes> page, string label, params string[] values) =>
        page.FindComponents<MudSelect<string>>().Single(select => select.Instance.Label == label)
            .InvokeAsync(() => page.FindComponents<MudSelect<string>>().Single(select => select.Instance.Label == label).Instance.SelectedValuesChanged.InvokeAsync(values)).GetAwaiter().GetResult();

    private void Load(params PortableLogbookWorkbookEntry[] entries)
    {
        var logbook = new LogbookId("log_routes_test");
        var operations = entries.Select((entry, index) => PortableLogbookOperationV2.Create(logbook,
            new EntryId($"ent_{index}"), new RevisionId($"rev_{index}"), new DeviceId("dev_routes_test"),
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"), entry));
        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(logbook, MobileLogbookSession.CustomFields,
            PortableLogbookCurrencyOverrideDates.Empty, operations);
        JSInterop.Setup<string?>("electronicLogbookStore.load", "portable-document").SetResult(JsonSerializer.Serialize(document, PortableLogbookJson.SerializerOptions));
    }
    private static PortableLogbookWorkbookEntry Flight() => PortableLogbookWorkbookEntry.Empty with
    { Year = 2024, Month = 1, Day = 2, SeCommandDay = 1, Type = "TYPE", Reg = "VH-TEST", From = "YMMB", To = "YSSY" };
}
