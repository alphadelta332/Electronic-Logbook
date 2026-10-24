using System.Text.Json;
using Bunit;
using ElectronicLogbook.Mobile.Pages;
using ElectronicLogbook.Portable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor.Services;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class FlightDetailComponentTests : BunitContext
{
    [Fact]
    public void ExistingEntryShowsEveryFieldOnceInTheMatchingFormSection()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(provider => new MobileLogbookSession(
            new BrowserLogbookStore(provider.GetRequiredService<IJSRuntime>()),
            new BrowserPackageKeyStore(provider.GetRequiredService<IJSRuntime>())));
        var logbook = new LogbookId("log_detail_test");
        var entry = PortableLogbookWorkbookEntry.Empty with
        {
            Year = 2024, Month = 1, Day = 1, Type = "C172", Reg = "VH-TEST", Pic = "Self",
            From = "YMMB", To = "YSSY", FlightReview = true, SeDualDay = 1.2m,
            IfrSim = 0.5m, Remarks = "A long remark retained in the route section."
        };
        var operation = PortableLogbookOperationV2.Create(logbook, new EntryId("ent_detail"),
            new RevisionId("rev_detail"), new DeviceId("dev_detail"), DateTimeOffset.UtcNow, entry);
        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(logbook, MobileLogbookSession.CustomFields,
            PortableLogbookCurrencyOverrideDates.Empty, [operation]);
        JSInterop.Setup<string?>("electronicLogbookStore.load", "portable-document")
            .SetResult(JsonSerializer.Serialize(document, PortableLogbookJson.SerializerOptions));

        var page = Render<FlightDetail>(parameters => parameters.Add(component => component.EntryId, "ent_detail"));

        page.WaitForAssertion(() => Assert.Equal(6, page.FindAll(".record-field-group").Count));
        Assert.Equal(["Flight Details", "Crew", "Route", "Hours", "Landings", "Instrument Approaches"],
            page.FindAll(".record-field-group h3").Select(node => node.TextContent));
        var sections = page.FindAll(".record-field-group");
        var details = Services.GetRequiredService<MobileLogbookSession>().EntryDetails(entry).ToList();
        EntryDetailGroup[][] groups = [[EntryDetailGroup.Flight], [EntryDetailGroup.Crew], [EntryDetailGroup.Route],
            [EntryDetailGroup.LoggedTime, EntryDetailGroup.CustomFields], [EntryDetailGroup.Landings], [EntryDetailGroup.InstrumentApproaches]];
        for (var index = 0; index < groups.Length; index++)
        {
            var expected = details.Where(detail => groups[index].Contains(detail.Group)).ToList();
            Assert.Equal(expected.Select(detail => detail.Label), sections[index].QuerySelectorAll("dt").Select(node => node.TextContent));
            Assert.Equal(expected.Select(detail => detail.Value), sections[index].QuerySelectorAll("dd").Select(node => node.TextContent));
        }
        Assert.Equal(["Single engine", "Multi engine"],
            sections[3].QuerySelectorAll(".entry-hour-table h4").Select(node => node.TextContent));
        Assert.Equal([3, 3], sections[3].QuerySelectorAll(".entry-hour-table")
            .Select(node => node.QuerySelectorAll("[role=rowheader]").Length));
        Assert.Equal(4, sections[3].QuerySelectorAll(".totals-time-header .mud-icon-root[aria-hidden=true]").Length);
        Assert.Equal(["Other time", "Custom fields"],
            sections[3].QuerySelectorAll(".record-hour-group h4").Select(node => node.TextContent));
        Assert.Equal(details.Count, page.FindAll(".record-field").Count);
        Assert.Equal(["Copilot", "Instrument"], sections[3].QuerySelectorAll(".totals-pair-row > strong").Select(node => node.TextContent));
        Assert.Equal(["Day", "Night", "Actual", "Simulator"], sections[3].QuerySelectorAll(".totals-pair-row small").Select(node => node.TextContent));
    }
}
