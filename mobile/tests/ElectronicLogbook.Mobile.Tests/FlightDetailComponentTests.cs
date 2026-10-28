using System.Text.Json;
using Bunit;
using ElectronicLogbook.Mobile.Pages;
using ElectronicLogbook.Portable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor.Services;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class FlightDetailComponentTests : BunitContext, IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;
    async Task IAsyncLifetime.DisposeAsync() => await base.DisposeAsync();

    [Fact]
    public void ExistingEntryShowsEveryFieldOnceInTheMatchingFormSection()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(provider => new BrowserLogbookStore(provider.GetRequiredService<IJSRuntime>()));
        Services.AddSingleton(provider => new MobileLogbookSession(
            new BrowserLogbookStore(provider.GetRequiredService<IJSRuntime>()),
            new BrowserPackageKeyStore(provider.GetRequiredService<IJSRuntime>())));
        var logbook = new LogbookId("log_detail_test");
        var entry = PortableLogbookWorkbookEntry.Empty with
        {
            Year = 2024, Month = 1, Day = 1, Type = "C172", Reg = "VH-TEST", Pic = "Self",
            From = "YMMB", To = "YSSY", FlightReview = true, SeDualDay = 1.2m,
            IfrSim = 0.5m, LandingsDay = 3, LandingsNight = 2, Remarks = "A long remark retained in the route section."
        };
        var operation = PortableLogbookOperationV2.Create(logbook, new EntryId("ent_detail"),
            new RevisionId("rev_detail"), new DeviceId("dev_detail"), DateTimeOffset.UtcNow, entry);
        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(logbook, MobileLogbookSession.CustomFields,
            PortableLogbookCurrencyOverrideDates.Empty, [operation]);
        JSInterop.Setup<string?>("electronicLogbookStore.load", "portable-document")
            .SetResult(JsonSerializer.Serialize(document, PortableLogbookJson.SerializerOptions));

        var page = Render<FlightDetail>(parameters => parameters.Add(component => component.EntryId, "ent_detail"));

        page.WaitForAssertion(() => Assert.Equal(9, page.FindAll(".entry-field-card").Count));
        string[] categories = ["Flight", "Hours", "Recency"];
        string[] headings = ["Details", "Crew", "Route", "Single Engine", "Multi Engine", "Other time", "Custom", "Landings", "Instrument Approaches"];
        Assert.Equal(categories, page.FindAll(".entry-category > h2").Select(node => node.TextContent));
        Assert.Equal(headings, page.FindAll(".entry-field-card h3").Select(node => node.TextContent));
        var details = Services.GetRequiredService<MobileLogbookSession>().EntryDetails(entry).ToList();
        EntryDetailGroup[] groups = [EntryDetailGroup.Flight, EntryDetailGroup.Crew, EntryDetailGroup.Route,
            EntryDetailGroup.LoggedTime, EntryDetailGroup.CustomFields, EntryDetailGroup.Landings, EntryDetailGroup.InstrumentApproaches];
        var expected = groups.SelectMany(group => details.Where(detail => detail.Group == group)).ToList();
        Assert.Equal(expected.Select(detail => detail.Label), page.FindAll(".entry-fields-layout dt").Select(node => node.TextContent));
        Assert.Equal(expected.Select(detail => detail.Value), page.FindAll(".entry-fields-layout dd").Select(node => node.TextContent));
        Assert.Equal([3, 3], page.FindAll(".entry-hour-table").Select(node => node.QuerySelectorAll("[role=rowheader]").Length));
        Assert.Equal(4, page.FindAll(".totals-time-header .mud-icon-root[aria-hidden=true]").Count);
        Assert.Equal(details.Count, page.FindAll(".record-field").Count);
        Assert.Equal(["Date", "Via", "Remarks", "Circling"],
            page.FindAll(".record-field.field-span dt").Select(node => node.TextContent));
        Assert.Equal([2, 3], page.FindAll("[aria-label=Crew] .entry-responsive-grid")
            .Select(node => node.Children.Length));
        Assert.Contains("--entry-field-columns: 1", page.FindAll("[aria-label=Crew] .entry-responsive-grid")[1].GetAttribute("style"));
        Assert.Equal(["ILS", "VOR", "RNP", "NDB", "DGA (CDI)", "DGA (Azi)", "Circling"],
            page.FindAll("[aria-label='Instrument Approaches'] dt").Select(node => node.TextContent));
        Assert.Equal(["Copilot", "Instrument", "Landings"], page.FindAll(".totals-pair-row > strong").Select(node => node.TextContent));
        Assert.Equal(["Day", "Night", "In Flight", "Simulator", "Day", "Night"], page.FindAll(".totals-pair-row small").Select(node => node.TextContent.Trim()));
        Assert.Equal([MudBlazor.Icons.Material.Filled.LightMode, MudBlazor.Icons.Material.Filled.DarkMode,
            MudBlazor.Icons.Material.Filled.Flight, MudBlazor.Icons.Material.Filled.Computer,
            MudBlazor.Icons.Material.Filled.LightMode, MudBlazor.Icons.Material.Filled.DarkMode],
            page.FindComponents<TimePeriodLabel>().TakeLast(6).Select(component =>
                component.FindComponent<MudBlazor.MudIcon>().Instance.Icon));

        var edit = Render<NewFlight>(parameters => parameters.Add(component => component.EntryId, "ent_detail"));
        edit.WaitForAssertion(() => Assert.Equal(9, edit.FindAll(".entry-field-card").Count));
        Assert.Empty(edit.FindAll(".entry-draft-status"));
        Assert.Equal(categories, edit.FindAll(".entry-category > h2").Select(node => node.TextContent));
        Assert.Equal(headings, edit.FindAll(".entry-field-card h3").Select(node => node.TextContent));
        Assert.Equal("1.2", edit.FindAll("input[type=number]")[2].GetAttribute("value"));
        Assert.Equal(["3", "2"], edit.FindAll("[aria-label=Landings] input").Select(node => node.GetAttribute("value")));
        edit.FindAll("[aria-label=Landings] input")[0].Change("4");
        Assert.Equal(4, Services.GetRequiredService<MobileLogbookSession>().WorkbookDraft.LandingsDay);
        Services.GetRequiredService<MobileLogbookSession>().MarkDraftEdited();
        edit.Render();
        Assert.Equal("Draft edited", edit.Find(".entry-draft-status").TextContent.Trim());

        var add = Render<NewFlight>();
        add.WaitForAssertion(() => Assert.Equal(headings, add.FindAll(".entry-field-card h3").Select(node => node.TextContent)));
        Assert.Empty(add.FindAll(".entry-draft-status"));
        Assert.Equal(categories, add.FindAll(".entry-category > h2").Select(node => node.TextContent));
        foreach (var layout in new[] { page.FindComponent<EntryFieldsLayout>(), edit.FindComponent<EntryFieldsLayout>(), add.FindComponent<EntryFieldsLayout>() })
        {
            Assert.Equal([MudBlazor.Icons.Material.Filled.Flight, MudBlazor.Icons.Material.Filled.People,
                MudBlazor.Icons.Material.Filled.Route, MobileAircraftIcons.SingleEngine,
                MobileAircraftIcons.MultiEngine, MudBlazor.Icons.Material.Filled.AvTimer,
                MudBlazor.Icons.Material.Filled.DashboardCustomize, MudBlazor.Icons.Material.Filled.FlightLand,
                MudBlazor.Icons.Material.Filled.GpsFixed],
                layout.FindComponents<MudBlazor.MudIcon>()
                    .Where(icon => icon.Instance.Class == "entry-field-card-icon")
                    .Select(icon => icon.Instance.Icon));
            Assert.Equal(9, layout.FindAll(".entry-field-card-heading > .mud-icon-root[aria-hidden=true] + h3").Count);
            foreach (var name in new[] { "Single Engine", "Multi Engine" })
            {
                var aircraft = layout.Find($"[aria-label='{name}'] header svg");
                Assert.NotEmpty(aircraft.QuerySelectorAll("path"));
                Assert.Empty(aircraft.QuerySelectorAll("line, rect"));
                Assert.Equal("rotate(45 12 12)",
                    Assert.Single(aircraft.QuerySelectorAll("g[transform]")).GetAttribute("transform"));
            }
        }
        Assert.Equal(5, add.FindAll(".entry-responsive-grid").Count);
        foreach (var form in new[] { add, edit })
        {
            Assert.Equal("Landings", form.Find("[aria-label=Landings] .totals-pair-row > strong").TextContent);
            Assert.Equal(["Day", "Night"], form.FindAll("[aria-label=Landings] small").Select(node => node.TextContent.Trim()));
            Assert.Equal(2, form.FindAll("[aria-label=Landings] small .mud-icon-root[aria-hidden=true]").Count);
            Assert.All(form.FindAll("[aria-label=Landings] input"), input =>
            {
                Assert.Equal("number", input.GetAttribute("type"));
                Assert.Equal("numeric", input.GetAttribute("inputmode"));
                Assert.Equal("0", input.GetAttribute("min"));
            });
        }
        Assert.Equal(["Date", "Circling"], add.FindAll("label.field-span")
            .Select(node => node.ChildNodes[0].TextContent.Trim()));

        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo("/flights?view=totals");
        var totals = Render<Logbook>();
        var entryHeadings = add.FindAll(".entry-field-card-heading").Skip(3).ToArray();
        Assert.Equal(entryHeadings.Select(heading => heading.QuerySelector("h3")!.TextContent),
            totals.FindAll(".totals-group-heading h3").Select(heading => heading.TextContent));
        Assert.Equal(entryHeadings.Select(heading => heading.QuerySelector("svg")!.InnerHtml),
            totals.FindAll(".totals-group-heading > svg[aria-hidden=true] + h3")
                .Select(heading => heading.PreviousElementSibling!.InnerHtml));
    }
}
