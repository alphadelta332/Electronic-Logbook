using ElectronicLogbook.Portable;
using Microsoft.JSInterop;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class BrowserAnalyticsBasesTests
{
    [Fact]
    public async Task CanonicalBasesSurviveNewStoreInstancesAndRemainScopedToTheirLogbook()
    {
        var js = new LocalStorageRuntime();
        js.Values["portable-document"] = "retained logbook";
        var first = new LogbookId("log_first");
        var second = new LogbookId("log_second");
        var store = new BrowserLogbookStore(js);
        Assert.Empty(await store.LoadBaseAirportsAsync(first));
        await store.SaveBaseAirportsAsync(first, [" sy ", "YSSY", "ml"]);
        await store.SaveBaseAirportsAsync(second, ["BN"]);

        var restarted = new BrowserLogbookStore(js);
        Assert.Equal(new[] { "YMML", "YSSY" }, await restarted.LoadBaseAirportsAsync(first));
        Assert.Equal(new[] { "YBBN" }, await restarted.LoadBaseAirportsAsync(second));
        await restarted.SaveBaseAirportsAsync(first, []);
        Assert.Empty(await new BrowserLogbookStore(js).LoadBaseAirportsAsync(first));
        Assert.Equal(new[] { "YBBN" }, await restarted.LoadBaseAirportsAsync(second));
        Assert.Equal("retained logbook", js.Values["portable-document"]);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[null]")]
    [InlineData("[\"UNKNOWN\"]")]
    public async Task InvalidSavedBasesReportFailureWithoutOverwritingStoredData(string saved)
    {
        var js = new LocalStorageRuntime();
        js.Values["analytics-bases:log_first"] = saved;
        await Assert.ThrowsAnyAsync<Exception>(async () => await new BrowserLogbookStore(js).LoadBaseAirportsAsync(new("log_first")));
        Assert.Equal(saved, js.Values["analytics-bases:log_first"]);
    }

    [Fact]
    public async Task InvalidAirportCannotReplaceSavedBases()
    {
        var js = new LocalStorageRuntime();
        var store = new BrowserLogbookStore(js);
        var logbook = new LogbookId("log_first");
        await store.SaveBaseAirportsAsync(logbook, ["YSSY"]);
        await Assert.ThrowsAsync<FormatException>(async () => await store.SaveBaseAirportsAsync(logbook, ["UNKNOWN"]));
        Assert.Equal(new[] { "YSSY" }, await store.LoadBaseAirportsAsync(logbook));
    }

    private sealed class LocalStorageRuntime : IJSRuntime
    {
        public Dictionary<string, string> Values { get; } = [];
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            var key = Assert.IsType<string>(args![0]);
            if (identifier == "electronicLogbookStore.load")
                return ValueTask.FromResult((TValue)(object?)Values.GetValueOrDefault(key)!);
            Assert.Equal("electronicLogbookStore.save", identifier);
            Values[key] = Assert.IsType<string>(args[1]);
            return ValueTask.FromResult(default(TValue)!);
        }
    }
}
