using System.Text.Json;
using Bunit;
using ElectronicLogbook.Mobile.Pages;
using ElectronicLogbook.Portable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor.Services;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class DeletedFlightComponentTests : BunitContext, IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;
    async Task IAsyncLifetime.DisposeAsync() => await base.DisposeAsync();

    [Theory]
    [InlineData("C172", "ABC", "C172 · ABC", "C172 ABC")]
    [InlineData("", "ABC", "ABC", "ABC")]
    [InlineData("C172", "", "C172", "C172")]
    public async Task DeletedSummaryShowsAircraftAndRegistrationOnce(
        string type, string registration, string activeAircraft, string deletedAircraft)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(provider => new MobileLogbookSession(
            new BrowserLogbookStore(provider.GetRequiredService<IJSRuntime>()),
            new BrowserPackageKeyStore(provider.GetRequiredService<IJSRuntime>())));
        var logbook = new LogbookId("log_deleted_summary");
        var flight = PortableLogbookWorkbookEntry.Empty with
        {
            Year = 2026, Month = 8, Day = 6, Type = type, Reg = registration,
            From = "YMMB", To = "YMMB"
        };
        var operation = PortableLogbookOperationV2.Create(logbook, new EntryId("ent_deleted_summary"),
            new RevisionId("rev_deleted_summary"), new DeviceId("dev_summary"), DateTimeOffset.UtcNow, flight);
        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(logbook, MobileLogbookSession.CustomFields,
            PortableLogbookCurrencyOverrideDates.Empty, [operation]);
        JSInterop.Setup<string?>("electronicLogbookStore.load", "portable-document")
            .SetResult(JsonSerializer.Serialize(document, PortableLogbookJson.SerializerOptions));

        var page = Render<Logbook>();
        page.WaitForAssertion(() => Assert.Equal(activeAircraft, page.Find(".logbook-row-aircraft").TextContent));
        var session = Services.GetRequiredService<MobileLogbookSession>();
        await page.InvokeAsync(() => session.DeleteWorkbookEntryAsync(Assert.Single(session.CurrentEntriesV2)));
        page.FindAll(".segmented-control button").Single(button => button.TextContent == "Deleted").Click();

        var row = Assert.Single(page.FindAll(".deleted-entry-row"));
        Assert.Equal("YMMB-YMMB", row.QuerySelector("strong")!.TextContent);
        Assert.Equal($"{MobileLogbookSession.FormatDate(flight.Date)} · {deletedAircraft}",
            row.QuerySelector(".deleted-entry-summary > span")!.TextContent);
        Assert.Equal("/flights/ent_deleted_summary", row.GetAttribute("href"));
        Assert.Equal(registration, session.FindLatestWorkbookEntryPayload(Assert.Single(session.DeletedEntriesV2))!.Reg);
    }
}
