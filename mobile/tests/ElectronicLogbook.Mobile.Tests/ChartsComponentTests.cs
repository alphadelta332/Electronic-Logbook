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

public sealed class ChartsComponentTests : BunitContext, IAsyncLifetime
{
    public ChartsComponentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton(provider => new BrowserLogbookStore(provider.GetRequiredService<IJSRuntime>()));
        Services.AddSingleton(provider => new MobileLogbookSession(
            new BrowserLogbookStore(provider.GetRequiredService<IJSRuntime>()),
            new BrowserPackageKeyStore(provider.GetRequiredService<IJSRuntime>())));
    }

    public Task InitializeAsync() => Task.CompletedTask;
    async Task IAsyncLifetime.DisposeAsync() => await base.DisposeAsync();

    [Theory]
    [InlineData("entries")]
    [InlineData("totals")]
    public void FilterButtonMatchesLogbookAndTogglesWithoutLosingSelection(string view)
    {
        Load(Flight(1, 2));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/flights?view={view}");
        var logbook = Render<Logbook>();
        Assert.Single(logbook.FindComponents<FilterToggleButton>());
        logbook.Find(".logbook-filter-button").Click();
        Assert.Equal("true", logbook.Find(".logbook-filter-button").GetAttribute("aria-expanded"));
        Assert.Single(logbook.FindAll(".totals-filter-sheet"));
        logbook.Find(".logbook-filter-button").Click();
        Assert.Empty(logbook.FindAll(".totals-filter-sheet"));
        var expected = logbook.FindComponents<MudButton>().Single(button => button.Instance.Class == "logbook-filter-button").Instance;
        var page = Render<Charts>();
        Assert.Single(page.FindComponents<FilterToggleButton>());
        var actual = page.FindComponents<MudButton>().Single(button => button.Instance.Class == "logbook-filter-button").Instance;
        Assert.Equal(expected.Variant, actual.Variant);
        Assert.Equal(expected.Size, actual.Size);
        Assert.Equal(expected.StartIcon, actual.StartIcon);
        Assert.Equal("Filters", page.Find(".logbook-filter-button").TextContent.Trim());
        Assert.Equal("false", page.Find(".logbook-filter-button").GetAttribute("aria-expanded"));
        Assert.True(page.Find(".analytics-filter-panel").HasAttribute("hidden"));
        page.Find(".logbook-filter-button").Click();
        Assert.Equal("true", page.Find(".logbook-filter-button").GetAttribute("aria-expanded"));
        Assert.False(page.Find(".analytics-filter-panel").HasAttribute("hidden"));
        page.Find("#analytics-from").Change("2024-01-01");
        page.Find("#analytics-to").Change("2024-01-31");
        Assert.Equal("2024-01-31", page.Find("#analytics-from").GetAttribute("max"));
        Assert.Equal("2024-01-01", page.Find("#analytics-to").GetAttribute("min"));
        Assert.Equal(new[] { "Start date", "End date" }, page.FindAll(".date-range-fields label").Select(label => label.TextContent.Trim()));
        Assert.Equal("Filters (1)", page.Find(".logbook-filter-button").TextContent.Trim());
        SetFilter(page, "Aircraft type", "TYPE");
        Assert.Equal("Filters (2)", page.Find(".logbook-filter-button").TextContent.Trim());
        SetFilter(page, "Registration", "VH-TEST");
        Assert.Equal("Filters (3)", page.Find(".logbook-filter-button").TextContent.Trim());
        page.Find(".logbook-filter-button").Click();
        page.Find(".logbook-filter-button").Click();
        Assert.Equal("2024-01-01", page.Find("#analytics-from").GetAttribute("value"));
        Assert.Contains("2.0 flying hours", page.Markup);
        page.Find(".analytics-filter-summary button").Click();
        Assert.Equal("Filters", page.Find(".logbook-filter-button").TextContent.Trim());
        Assert.Null(page.Find("#analytics-from").GetAttribute("max"));
        Assert.Null(page.Find("#analytics-to").GetAttribute("min"));
    }

    [Fact]
    public void ChartHeadingsHaveNoSubtitlesAndRankingsStayTogether()
    {
        Load(Flight(1, 2));
        var page = Render<Charts>();
        Assert.Equal(new[] { "Hours by year", "Total hours over time", "Hours by category",
            "Top five registrations", "Top five aircraft types", "Top ten airports" },
            page.FindAll(".analytics-chart h2").Select(heading => heading.TextContent));
        Assert.Equal(new[] { "2.0 flying hours · 1 flights.", "2.0 total flying hours." },
            page.FindAll(".analytics-chart h2 + p").Select(paragraph => paragraph.TextContent));
        page.FindAll(".mud-tab")[1].Click();
        Assert.Empty(page.FindAll(".analytics-statistics h2 + p"));
    }

    [Fact]
    public void PieTooltipFormatsImportedPrecisionWithoutChangingHours()
    {
        const decimal importedHours = 566.400000000000020m;
        Load(Flight(1, importedHours));
        var page = Render<Charts>();
        var pie = page.FindComponents<AnalyticsPlot>().Single(chart => chart.Instance.Data.Kind == "pie");
        Assert.Equal(importedHours, pie.Instance.Data.Series.Single().Values.Single());
        Assert.Equal("566.4", page.Find(".analytics-legend strong").TextContent);
    }

    [Fact]
    public void SixChartsReconcileToFlightTotalsAndKeepRealDates()
    {
        Load(Flight(1, 2), Flight(10, 3) with { MeDualDay = 4, IfrSim = 9 });
        var page = Render<Charts>();
        Assert.Equal(6, page.FindAll(".analytics-chart").Count);
        Assert.Empty(page.FindAll(".analytics-chart details"));
        Assert.DoesNotContain("Show values for", page.Markup);
        Assert.Equal(3, page.FindAll(".analytics-chart .visually-hidden table").Count);
        Assert.Contains("9.0 flying hours", page.Markup);
        var charts = page.FindComponents<AnalyticsPlot>();
        Assert.Equal(6, charts.Count);
        var year = charts.Single(chart => chart.Instance.Data.Kind == "year").Instance.Data;
        Assert.Equal(2, year.Series.Length);
        Assert.Equal(2, page.FindAll(".analytics-year-legend li").Count);
        Assert.Equal(15, page.Find(".analytics-chart table tbody").Children.Length);
        Assert.Equal(9m, year.Series.Sum(series => series.Values.Sum()));
        var line = charts.Single(chart => chart.Instance.Data.Kind == "time").Instance.Data;
        Assert.Equal(new[] { 0m, 2m, 9m }, line.Series.Single().Values);
        Assert.Equal(new[] { "2023-12-31", "2024-01-01", "2024-01-10" }, line.Labels);
        var pie = charts.Single(chart => chart.Instance.Data.Kind == "pie").Instance.Data;
        Assert.Equal(9m, pie.Series.Sum(series => series.Values.Sum()));
        var rankings = page.FindComponents<AnalyticsRankingChart>();
        Assert.Equal(9m, rankings[0].Instance.Values.Single().Value);
        Assert.Equal(9m, rankings[1].Instance.Values.Single().Value);
        Assert.Equal(2m, rankings[2].Instance.Values.Single(value => value.Name.StartsWith("YSSY")).Value);
        Assert.Contains("YSSY", rankings[2].Markup);
        Assert.Contains("Sydney", rankings[2].Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailedRendererShowsExactValuesAndRetriesOnChangedData()
    {
        var draw = JSInterop.SetupVoid("electronicLogbookCharts.update", _ => true);
        draw.SetException(new JSException("renderer unavailable"));
        var data = new MobileAnalyticsPlotData("pie", ["Command", "Dual"],
            [new("Command", [7m], "#397e63"), new("Dual", [2m], "#63b9bb")]);
        var plot = Render<AnalyticsPlot>(parameters => parameters.Add(component => component.Title, "Hours by category")
            .Add(component => component.Data, data));
        Assert.Contains("Chart could not be drawn", plot.Markup);
        Assert.Equal(2, plot.FindAll("tbody tr").Count);
        Assert.Contains("7.0", plot.Find("table").TextContent);
        draw.SetVoidResult();
        plot.Render(parameters => parameters.Add(component => component.Data, data with { Labels = ["Command", "Dual"] }));
        Assert.Empty(plot.FindAll("table"));
        Assert.DoesNotContain("Chart could not be drawn", plot.Markup);
    }

    [Fact]
    public async Task UnchangedRendersDoNotRedrawAndDisposalDisconnectsTheChart()
    {
        var data = new MobileAnalyticsPlotData("time", ["2024-01-01"], [new("Flying hours", [2m], "")]);
        var plot = Render<AnalyticsPlot>(parameters => parameters.Add(component => component.Title, "Total hours over time")
            .Add(component => component.Data, data));
        var draws = JSInterop.Invocations.Count(invocation => invocation.Identifier == "electronicLogbookCharts.update");
        plot.Render();
        Assert.Equal(draws, JSInterop.Invocations.Count(invocation => invocation.Identifier == "electronicLogbookCharts.update"));
        await plot.Instance.DisposeAsync();
        Assert.Contains(JSInterop.Invocations, invocation => invocation.Identifier == "electronicLogbookCharts.dispose");
    }

    [Fact]
    public void CombinedFiltersTabSwitchAndClearKeepTheSameSelection()
    {
        Load(Flight(1, 1), Flight(10, 2), Flight(10, 4) with { Type = "OTHER" },
            Flight(10, 8) with { Reg = "VH-OTHER" });
        var page = Render<Charts>();
        page.Find("#analytics-from").Change("2024-01-10");
        page.Find("#analytics-to").Change("2024-01-10");
        SetFilter(page, "Aircraft type", "TYPE");
        SetFilter(page, "Registration", "VH-TEST");
        Assert.Contains("2.0 flying hours", page.Markup);
        Assert.Contains("Selected period", page.Find(".analytics-filter-summary").TextContent);
        Assert.Equal("Charts", page.Find(".mud-tab-active").TextContent);
        page.FindAll(".mud-tab")[1].Click();
        Assert.Equal("Stats", page.Find(".mud-tab-active").TextContent);
        Assert.Equal("2.0 h", page.FindComponent<AnalyticsStatistics>().Find("dd strong").TextContent);
        Assert.Equal("2.0 h", Statistic(page.FindComponent<AnalyticsStatistics>(), "Average hours per flight"));
        Assert.Equal("Unavailable for this selection", Statistic(page.FindComponent<AnalyticsStatistics>(), "IFR percentage · Last 365 days"));
        Assert.Contains("Average flying days per active month · Selected period", page.Markup);
        page.FindAll(".mud-tab")[0].Click();
        Assert.Equal("Charts", page.Find(".mud-tab-active").TextContent);
        Assert.Contains("2.0 flying hours", page.Markup);
        page.Find(".analytics-filter-summary button").Click();
        Assert.Contains("15.0 flying hours", page.Markup);
        Assert.Empty(page.FindAll(".analytics-filter-summary"));
        Assert.Equal("Filters", page.Find(".logbook-filter-button").TextContent.Trim());
        Assert.True(string.IsNullOrEmpty(page.Find("#analytics-from").GetAttribute("value")));
    }

    [Fact]
    public void InvalidDatesHideOldResultsAndRecoverWhenCleared()
    {
        Load(Flight(1, 2));
        var page = Render<Charts>();
        page.Find("#analytics-from").Change("2024-02-01");
        page.Find("#analytics-to").Change("2024-01-01");
        Assert.Contains("Choose a Start date", page.Markup);
        Assert.Empty(page.FindAll(".analytics-chart"));
        Assert.Empty(page.FindComponents<AnalyticsStatistics>());
        page.Find(".analytics-filter-summary button").Click();
        Assert.Equal(6, page.FindAll(".analytics-chart").Count);
    }

    [Fact]
    public void EmptyFilteredAndSimulatorOnlySelectionsHaveNoCharts()
    {
        Load(Flight(1, 0) with { IfrSim = 3 });
        var page = Render<Charts>();
        Assert.Contains("No flying entries in this selection", page.Markup);
        Assert.Empty(page.FindComponents<AnalyticsPlot>());
        page.Find("#analytics-from").Change("2025-01-01");
        Assert.Contains("No flying entries in this selection", page.Markup);
    }

    [Fact]
    public void LoadingStateWaitsForLocalLogbook()
    {
        var load = JSInterop.Setup<string?>("electronicLogbookStore.load", "portable-document");
        var page = Render<Charts>();
        Assert.Contains("Loading Charts", page.Markup);
        Assert.Empty(page.FindAll(".analytics-filters"));
        load.SetResult(null);
        page.WaitForAssertion(() => Assert.Contains("No flying entries", page.Markup));
    }

    [Fact]
    public void StorageFailureDoesNotShowAnEmptyLogbookAsValid()
    {
        JSInterop.Setup<string?>("electronicLogbookStore.load", "portable-document").SetResult("invalid json");
        var page = Render<Charts>();
        Assert.Contains("Stored logbook state is not valid JSON", page.Markup);
        Assert.Empty(page.FindAll(".analytics-filters"));
        Assert.DoesNotContain("No flying entries", page.Markup);
    }

    [Fact]
    public void MissingDatesAreExplainedAndUnresolvedAirportsAreSilentlyOmitted()
    {
        Load(Flight(1, 2) with { From = "UNKNOWN", To = "UNKNOWN" }, Flight(1, 4) with { Year = null });
        var page = Render<Charts>();
        Assert.Contains("1 entries have no valid date", page.Markup);
        Assert.Empty(page.FindAll(".analytics-data-quality"));
        Assert.DoesNotContain("Unmatched", page.Markup);
        Assert.Contains("No resolved airports", page.Markup);
        Assert.Empty(page.FindAll(".analytics-chart details"));
        Assert.Contains("2.0 flying hours", page.Markup);
    }

    [Fact]
    public void StatsContainsAllResultsAndUnavailableValuesWithoutHidingGenuineZeroCounts()
    {
        Load(Flight(1, 0) with { IfrSim = 3, Ils = 2 });
        var page = Render<Charts>();
        page.FindAll(".mud-tab")[1].Click();
        var stats = page.FindComponent<AnalyticsStatistics>();
        Assert.Equal(24, stats.FindAll("dt").Count);
        Assert.Equal("Unavailable for this selection", Statistic(stats, "Average hours per flight"));
        Assert.Equal("0", Statistic(stats, "Distinct crew flown with"));
        Assert.Equal("0", Statistic(stats, "Number of airports visited"));
        Assert.Equal("2", Statistic(stats, "Most approaches in a day"));
        Assert.Equal("Choose base airports", Statistic(stats, "Most visited airport excluding bases"));
        Assert.Contains("1 Jan 2024", stats.Markup);
        page.Find("#analytics-from").Change("2025-01-01");
        Assert.Equal("Unavailable for this selection", Statistic(stats, "Most approaches in a day"));
    }

    [Fact]
    public void MultipleBasesUseCanonicalCodesSurviveFiltersAndReloadAndCanBeRemoved()
    {
        Load(Flight(1, 2) with { To = "YMML" }, Flight(10, 3) with { To = "YBBN" });
        var saved = JSInterop.SetupVoid("electronicLogbookStore.save", invocation =>
            Equals(invocation.Arguments[0], "analytics-bases:log_charts_test"));
        saved.SetVoidResult();
        var page = Render<Charts>();
        page.FindAll(".mud-tab")[1].Click();
        page.Find("#analytics-base-code").Change("sy");
        page.Find(".analytics-base-controls button").Click();
        Assert.Equal("[\"YSSY\"]", saved.Invocations.Last().Arguments[1]);
        Assert.StartsWith("YBBN", Statistic(page.FindComponent<AnalyticsStatistics>(), "Most visited airport excluding bases"));
        page.Find("#analytics-base-code").Change("BN");
        page.Find(".analytics-base-controls button").Click();
        Assert.Equal("[\"YBBN\",\"YSSY\"]", saved.Invocations.Last().Arguments[1]);
        Assert.StartsWith("YMML", Statistic(page.FindComponent<AnalyticsStatistics>(), "Most visited airport excluding bases"));
        page.Find("#analytics-from").Change("2024-01-10");
        Assert.Equal("Unavailable for this selection", Statistic(page.FindComponent<AnalyticsStatistics>(), "Most visited airport excluding bases"));
        page.Find(".analytics-filter-summary button").Click();
        Assert.Equal(2, page.FindAll(".analytics-base-list li").Count);
        Assert.StartsWith("YMML", Statistic(page.FindComponent<AnalyticsStatistics>(), "Most visited airport excluding bases"));

        JSInterop.Setup<string?>("electronicLogbookStore.load", "analytics-bases:log_charts_test")
            .SetResult(Assert.IsType<string>(saved.Invocations.Last().Arguments[1]));
        page.Dispose();
        var reopened = Render<Charts>();
        reopened.FindAll(".mud-tab")[1].Click();
        Assert.Equal(2, reopened.FindAll(".analytics-base-list li").Count);
        reopened.Find(".analytics-base-list button").Click();
        reopened.Find(".analytics-base-list button").Click();
        Assert.Empty(reopened.FindAll(".analytics-base-list li"));
        Assert.Equal("[]", saved.Invocations.Last().Arguments[1]);
        Assert.Equal("Choose base airports", Statistic(reopened.FindComponent<AnalyticsStatistics>(), "Most visited airport excluding bases"));
        Assert.Equal(6, reopened.FindAll(".analytics-chart").Count);
    }

    [Fact]
    public void InvalidBaseCodesDoNotSaveAndDuplicateAliasesDoNotCreateExtraBases()
    {
        Load(Flight(1, 2));
        var page = Render<Charts>();
        page.FindAll(".mud-tab")[1].Click();
        page.Find("#analytics-base-code").Change("UNKNOWN");
        page.Find(".analytics-base-controls button").Click();
        Assert.Contains("Airport code not found", page.Markup);
        Assert.DoesNotContain(JSInterop.Invocations, invocation => invocation.Identifier == "electronicLogbookStore.save");
        foreach (var code in new[] { "SY", "YSSY" })
        {
            page.Find("#analytics-base-code").Change(code);
            page.Find(".analytics-base-controls button").Click();
        }
        Assert.Single(page.FindAll(".analytics-base-list li"));
    }

    [Fact]
    public void FailedBaseSaveKeepsThePreviousSelectionAndExclusion()
    {
        Load(Flight(1, 2) with { To = "YMML" });
        JSInterop.Setup<string?>("electronicLogbookStore.load", "analytics-bases:log_charts_test").SetResult("[\"YSSY\"]");
        JSInterop.SetupVoid("electronicLogbookStore.save", invocation => Equals(invocation.Arguments[0], "analytics-bases:log_charts_test"))
            .SetException(new JSException("storage full"));
        var page = Render<Charts>();
        page.FindAll(".mud-tab")[1].Click();
        page.Find("#analytics-base-code").Change("ML");
        page.Find(".analytics-base-controls button").Click();
        Assert.Contains("Base airports could not be saved", page.Markup);
        Assert.Single(page.FindAll(".analytics-base-list li"));
        Assert.StartsWith("YMML", Statistic(page.FindComponent<AnalyticsStatistics>(), "Most visited airport excluding bases"));
    }

    [Fact]
    public void FailedBaseLoadLeavesChartsAndOtherStatsUsableAndCanRetry()
    {
        Load(Flight(1, 2));
        var load = JSInterop.Setup<string?>("electronicLogbookStore.load", "analytics-bases:log_charts_test");
        load.SetException(new JSException("storage unavailable"));
        var page = Render<Charts>();
        page.FindAll(".mud-tab")[1].Click();
        Assert.Contains("Saved base airports could not be loaded", page.Markup);
        Assert.Equal("Base airports unavailable", Statistic(page.FindComponent<AnalyticsStatistics>(), "Most visited airport excluding bases"));
        Assert.Equal(6, page.FindAll(".analytics-chart").Count);
        Assert.True(page.Find("#analytics-base-code").HasAttribute("disabled"));
        load.SetResult("[\"YSSY\"]");
        page.Find(".analytics-bases > button").Click();
        Assert.False(page.Find("#analytics-base-code").HasAttribute("disabled"));
        Assert.Single(page.FindAll(".analytics-base-list li"));
        Assert.DoesNotContain("Saved base airports could not be loaded", page.Markup);
    }

    private static string Statistic(IRenderedComponent<AnalyticsStatistics> stats, string label) =>
        stats.FindAll(".analytics-statistic").Single(row => row.QuerySelector("dt")!.TextContent == label)
            .QuerySelector("dd strong")!.TextContent;

    [Fact]
    public async Task OpenScreenRefreshesAfterEditDeletionAndRestorationWithoutLosingFiltersOrBases()
    {
        Load(Flight(1, 2));
        JSInterop.Setup<string?>("electronicLogbookStore.load", "analytics-bases:log_charts_test").SetResult("[\"YSSY\"]");
        var page = Render<Charts>();
        SetFilter(page, "Aircraft type", "TYPE");
        page.FindAll(".mud-tab")[1].Click();
        var session = Services.GetRequiredService<MobileLogbookSession>();
        await page.InvokeAsync(async () =>
        {
            session.EditWorkbookEntry(Assert.Single(session.CurrentEntriesV2));
            session.WorkbookDraft.SeCommandDay = 5;
            Assert.Empty(session.WorkbookDraftErrors);
            await session.SaveWorkbookEntryAsync();
        });
        page.WaitForAssertion(() => Assert.Equal("5.0 h", Statistic(page.FindComponent<AnalyticsStatistics>(), "Average hours per flight")));
        await page.InvokeAsync(() => session.DeleteWorkbookEntryAsync(Assert.Single(session.CurrentEntriesV2)));
        page.WaitForAssertion(() => Assert.Equal("Unavailable for this selection", Statistic(page.FindComponent<AnalyticsStatistics>(), "Average hours per flight")));
        await page.InvokeAsync(() => session.RestoreWorkbookEntryAsync(Assert.Single(session.DeletedEntriesV2)));
        page.WaitForAssertion(() => Assert.Equal("5.0 h", Statistic(page.FindComponent<AnalyticsStatistics>(), "Average hours per flight")));
        Assert.Equal(new[] { "TYPE" }, Select(page, "Aircraft type").Instance.GetState(select => select.SelectedValues));
        Assert.Single(page.FindAll(".analytics-base-list li"));
        Assert.Equal("Stats", page.Find(".mud-tab-active").TextContent);
    }

    private void Load(params PortableLogbookWorkbookEntry[] entries)
    {
        var logbook = new LogbookId("log_charts_test");
        var operations = entries.Select((entry, index) => PortableLogbookOperationV2.Create(
            logbook, new EntryId($"ent_{index}"), new RevisionId($"rev_{index}"), new DeviceId("dev_charts_test"),
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"), entry));
        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(logbook, MobileLogbookSession.CustomFields,
            PortableLogbookCurrencyOverrideDates.Empty, operations);
        JSInterop.Setup<string?>("electronicLogbookStore.load", "portable-document")
            .SetResult(JsonSerializer.Serialize(document, PortableLogbookJson.SerializerOptions));
    }

    [Fact]
    public async Task LocalMidnightRefreshesFutureFlightsAndRecentIntersectionWithoutRecalculatingOnRenders()
    {
        var clock = new AnalyticsTestClock(new DateTimeOffset(2026, 10, 11, 12, 59, 30, TimeSpan.Zero));
        Services.AddSingleton<TimeProvider>(clock);
        var today = new DateOnly(2026, 10, 11);
        var cutoff = today.AddDays(-364);
        Load(OnDate(cutoff, 2) with { IfrIf = 1 }, OnDate(today, 2),
            OnDate(today.AddDays(1), 2) with { IfrIf = 2 }, OnDate(today, 8) with { Type = "OTHER" });
        var page = Render<Charts>();
        SetFilter(page, "Aircraft type", "TYPE");
        SetFilter(page, "Registration", "VH-TEST");
        page.Find("#analytics-from").Change(cutoff.ToString("yyyy-MM-dd"));
        page.Find("#analytics-to").Change(today.AddDays(1).ToString("yyyy-MM-dd"));
        page.FindAll(".mud-tab")[1].Click();
        var before = page.FindComponent<AnalyticsStatistics>().Instance.Analytics;
        Assert.Equal(4m, before.FlyingHours);
        Assert.Equal(25m, before.Statistics.IfrPercentageRecent);
        page.Render();
        await page.InvokeAsync(() => clock.Tick(TimeSpan.FromSeconds(10)));
        Assert.Same(before, page.FindComponent<AnalyticsStatistics>().Instance.Analytics);
        await page.InvokeAsync(() => clock.Tick(TimeSpan.FromMinutes(1)));
        page.WaitForAssertion(() => Assert.Equal(6m, page.FindComponent<AnalyticsStatistics>().Instance.Analytics.FlyingHours));
        var after = page.FindComponent<AnalyticsStatistics>().Instance.Analytics;
        Assert.Equal(50m, after.Statistics.IfrPercentageRecent);
        Assert.Equal(2m, after.Statistics.AverageFlyingDaysPerActiveMonthRecent);
        Assert.Equal("Stats", page.Find(".mud-tab-active").TextContent);
        page.Find("#analytics-to").Change(today.ToString("yyyy-MM-dd"));
        Assert.Equal(0m, page.FindComponent<AnalyticsStatistics>().Instance.Analytics.Statistics.IfrPercentageRecent);
        await DisposeComponentsAsync();
        Assert.True(clock.TimerDisposed);
        await Services.GetRequiredService<MobileLogbookSession>().DeleteWorkbookEntryAsync(
            Services.GetRequiredService<MobileLogbookSession>().CurrentEntriesV2[0]);
        // A queued callback must also be harmless after the page closes.
        clock.Tick(TimeSpan.FromDays(1));
    }

    [Fact]
    public async Task NewEntriesRefreshFilterChoicesAndChartsWhileOpen()
    {
        Load(Flight(1, 2));
        var page = Render<Charts>();
        var session = Services.GetRequiredService<MobileLogbookSession>();
        await page.InvokeAsync(async () =>
        {
            session.WorkbookDraft.Date = new DateOnly(2024, 1, 2);
            session.WorkbookDraft.Type = "NEW TYPE";
            session.WorkbookDraft.Reg = "VH-NEW";
            session.WorkbookDraft.From = session.WorkbookDraft.To = "YSSY";
            session.WorkbookDraft.SeCommandDay = 3;
            Assert.Empty(session.WorkbookDraftErrors);
            await session.SaveWorkbookEntryAsync();
        });
        page.WaitForAssertion(() => Assert.Contains("5.0 flying hours", page.Markup));
        Assert.Contains(Select(page, "Aircraft type").FindComponents<MudSelectItem<string>>(), item => item.Instance.Value == "NEW TYPE");
        Assert.Contains(Select(page, "Registration").FindComponents<MudSelectItem<string>>(), item => item.Instance.Value == "VH-NEW");
        SetFilter(page, "Aircraft type", "NEW TYPE");
        await page.InvokeAsync(() => session.DeleteWorkbookEntryAsync(session.CurrentEntriesV2.Single(entry => entry.Entry!.Type == "NEW TYPE")));
        page.WaitForAssertion(() => Assert.Contains("No flying entries in this selection", page.Markup));
        Assert.Contains(Select(page, "Aircraft type").FindComponents<MudSelectItem<string>>(), item => item.Instance.Value == "NEW TYPE");
    }

    [Fact]
    public async Task OpenScreenReloadsBasesForAnAdoptedLogbookAndIgnoresOldPendingReads()
    {
        Load();
        JSInterop.Setup<string?>("electronicLogbookStore.load", "analytics-bases:log_charts_test").SetResult("[\"YSSY\"]");
        var pending = JSInterop.Setup<string?>("electronicLogbookStore.load", "analytics-bases:log_next");
        JSInterop.Setup<string?>("electronicLogbookStore.load", "analytics-bases:log_final").SetResult("[\"YBBN\"]");
        var page = Render<Charts>();
        page.FindAll(".mud-tab")[1].Click();
        var session = Services.GetRequiredService<MobileLogbookSession>();
        await page.InvokeAsync(() => session.AdoptWorkbookLogbookAsync(new LogbookId("log_next")));
        await page.InvokeAsync(() => session.AdoptWorkbookLogbookAsync(new LogbookId("log_final")));
        pending.SetResult("[\"YMML\"]");
        page.WaitForAssertion(() => Assert.Contains("YBBN", page.Find(".analytics-base-list").TextContent));
        Assert.DoesNotContain("YMML", page.Find(".analytics-base-list").TextContent);
        Assert.DoesNotContain("YSSY", page.Find(".analytics-base-list").TextContent);
    }

    private static IRenderedComponent<MudSelect<string>> Select(IRenderedComponent<Charts> page, string label) =>
        page.FindComponents<MudSelect<string>>().Single(select => select.Instance.Label == label);

    private static void SetFilter(IRenderedComponent<Charts> page, string label, params string[] values) =>
        page.InvokeAsync(() => Select(page, label).Instance.SelectedValuesChanged.InvokeAsync(values)).GetAwaiter().GetResult();

    [Fact]
    public void MultipleAircraftAndRegistrationsApplyAcrossBothTabsAndClearTogether()
    {
        Load(Flight(1, 1), Flight(2, 2) with { Type = "OTHER", Reg = "VH-OTHER" },
            Flight(3, 4) with { Type = "THIRD" }, Flight(4, 8) with { Reg = "VH-THIRD" });
        var page = Render<Charts>();
        Assert.All(page.FindComponents<MudSelect<string>>(), select =>
        {
            Assert.True(select.Instance.MultiSelection);
            Assert.True(select.Instance.SelectAll);
            Assert.True(select.Instance.Clearable);
        });
        SetFilter(page, "Aircraft type", "TYPE", "OTHER");
        SetFilter(page, "Registration", "VH-TEST", "VH-OTHER");
        Assert.Contains("3.0 flying hours", page.Markup);
        Assert.Contains("TYPE, OTHER", page.Find(".analytics-filter-summary").TextContent);
        page.FindAll(".mud-tab")[1].Click();
        Assert.Equal("1.5 h", Statistic(page.FindComponent<AnalyticsStatistics>(), "Average hours per flight"));
        page.Find(".analytics-filter-summary button").Click();
        Assert.Equal("3.8 h", Statistic(page.FindComponent<AnalyticsStatistics>(), "Average hours per flight"));
        Assert.All(page.FindComponents<MudSelect<string>>(), select => Assert.Empty(select.Instance.GetState(component => component.SelectedValues)!));
    }

    private static PortableLogbookWorkbookEntry OnDate(DateOnly date, decimal hours) =>
        Flight(date.Day, hours) with { Year = date.Year, Month = date.Month };

    private sealed class AnalyticsTestClock(DateTimeOffset utcNow) : TimeProvider, ITimer
    {
        private TimerCallback callback = _ => { };
        private object? state;
        public bool TimerDisposed { get; private set; }
        public override DateTimeOffset GetUtcNow() => utcNow;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("test-local", TimeSpan.FromHours(11), "test-local", "test-local");
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            // MudSelect also uses the injected clock for its own UI timers.
            if (dueTime != TimeSpan.FromMinutes(1) || period != dueTime)
                return base.CreateTimer(callback, state, dueTime, period);
            this.callback = callback;
            this.state = state;
            return this;
        }
        public void Tick(TimeSpan elapsed) { utcNow += elapsed; callback(state); }
        public bool Change(TimeSpan dueTime, TimeSpan period) => !TimerDisposed;
        public void Dispose() => TimerDisposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }

    private static PortableLogbookWorkbookEntry Flight(int day, decimal hours) => PortableLogbookWorkbookEntry.Empty with
    {
        Year = 2024, Month = 1, Day = day, SeCommandDay = hours, Type = "TYPE", Reg = "VH-TEST", Pic = "Self", From = "YSSY", To = "YSSY"
    };
}
