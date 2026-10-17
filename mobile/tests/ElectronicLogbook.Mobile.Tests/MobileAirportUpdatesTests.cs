using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ElectronicLogbook.Mobile;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileAirportUpdatesTests
{
    [Theory]
    [InlineData("2024-01-24T23:59:59Z", "2023-12-28")]
    [InlineData("2024-01-25T00:00:00Z", "2024-01-25")]
    [InlineData("2024-02-21T23:59:59Z", "2024-01-25")]
    [InlineData("2024-02-22T00:00:00Z", "2024-02-22")]
    public void AiracDatesUseCycleBoundariesInsteadOfTimeSinceLastCheck(string now, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), MobileAirportUpdates.AiracCycle(DateTimeOffset.Parse(now)));

    [Fact]
    public async Task UpdatedCoordinatesKeepNamesAliasesAndMissingAirportsAcrossRestart()
    {
        var fixture = new Fixture();
        await fixture.Service.CheckAsync();
        Assert.True(fixture.Catalog.TryFind("SY", out var sydney));
        Assert.Equal("Our Sydney", sydney.Name);
        Assert.Equal(-34, sydney.Latitude);
        Assert.Equal("SYD", sydney.ThreeLetterCode);
        Assert.True(fixture.Catalog.TryFind("YOLD", out _));
        Assert.True(fixture.Catalog.TryFind("X0001", out _));
        Assert.Equal("retained logbook", fixture.Js.Values["portable-document"]);
        Assert.Equal(1, fixture.Changed);
        var restarted = Fixture.Seed();
        var service = new MobileAirportUpdates(fixture.Http, fixture.Js, fixture.Clock, restarted, NullLogger<MobileAirportUpdates>.Instance);
        await service.LoadCachedAsync();
        Assert.True(restarted.TryFind("SY", out var restored));
        Assert.Equal(-34, restored.Latitude);
        Assert.Equal("Our Sydney", restored.Name);
        var requests = fixture.Handler.Requests.Count;
        await service.CheckAsync();
        Assert.Equal(requests, fixture.Handler.Requests.Count);
    }

    [Fact]
    public async Task ConcurrentChecksDownloadOnceAndUnchangedNextCycleOnlyFetchesManifest()
    {
        var fixture = new Fixture();
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => fixture.Service.CheckAsync()));
        Assert.Equal(1, fixture.Handler.Requests.Count(path => path.EndsWith(".json.gz")));
        fixture.Clock.Now = fixture.Clock.Now.AddDays(28);
        await fixture.Service.CheckAsync();
        Assert.Equal(1, fixture.Handler.Requests.Count(path => path.EndsWith(".json.gz")));
        Assert.Equal(2, fixture.Handler.Requests.Count(path => path.Contains("manifest.json")));
    }

    [Fact]
    public async Task ReconnectionRetriesAnOfflineFailureWithoutWaitingForTheBackoff()
    {
        var fixture = new Fixture { Failure = "offline" };
        await fixture.Service.CheckAsync();
        Assert.False(fixture.Catalog.TryFind("X0001", out _));
        fixture.Failure = "";
        await fixture.Service.NetworkRestoredAsync();
        Assert.True(fixture.Catalog.TryFind("X0001", out _));
    }

    [Theory]
    [InlineData("offline")]
    [InlineData("checksum")]
    [InlineData("future-schema")]
    [InlineData("stale-cycle")]
    [InlineData("path")]
    [InlineData("count")]
    [InlineData("coordinates")]
    [InlineData("duplicates")]
    [InlineData("storage")]
    public async Task FailedRefreshLeavesSavedAndLiveCatalogueUntouchedAndCanRetry(string failure)
    {
        var fixture = new Fixture();
        await fixture.Service.CheckAsync();
        var before = fixture.Js.Values["airport-catalog-v1"];
        fixture.Clock.Now = fixture.Clock.Now.AddDays(28);
        fixture.Failure = failure;
        fixture.Js.FailSave = failure == "storage";
        fixture.Payload = Fixture.PayloadFor(-35, failure);
        await fixture.Service.CheckAsync();
        Assert.Equal(before, fixture.Js.Values["airport-catalog-v1"]);
        Assert.True(fixture.Catalog.TryFind("YSSY", out var retained));
        Assert.Equal(-34, retained.Latitude);
        fixture.Failure = "";
        fixture.Js.FailSave = false;
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(6);
        fixture.Payload = Fixture.PayloadFor(-35);
        await fixture.Service.CheckAsync();
        Assert.True(fixture.Catalog.TryFind("YSSY", out var updated));
        Assert.Equal(-35, updated.Latitude);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("not json")]
    public async Task InvalidCacheDoesNotOverwriteItselfOrBlockEmbeddedCatalogue(string json)
    {
        var fixture = new Fixture();
        fixture.Js.Values["airport-catalog-v1"] = json;
        await fixture.Service.LoadCachedAsync();
        Assert.Equal(json, fixture.Js.Values["airport-catalog-v1"]);
        Assert.True(fixture.Catalog.TryFind("SY", out _));
        Assert.Equal("retained logbook", fixture.Js.Values["portable-document"]);
    }

    private sealed class Fixture
    {
        public readonly LocalStore Js = new();
        public readonly TestClock Clock = new();
        public readonly MobileAirportCatalog Catalog = Seed();
        public readonly Handler Handler;
        public readonly HttpClient Http;
        public readonly MobileAirportUpdates Service;
        public string Failure = "";
        public byte[] Payload = PayloadFor(-34);
        public int Changed;
        public Fixture()
        {
            Js.Values["portable-document"] = "retained logbook";
            Catalog.Changed += () => Changed++;
            Handler = new Handler(request => {
                Assert.Null(request.Headers.Authorization);
                if (Failure == "offline") throw new HttpRequestException("Offline");
                if (request.RequestUri!.AbsolutePath.EndsWith("hosted-sync.local.json"))
                    return Json(new { supabaseUrl = "https://airport-test.supabase.co" });
                var hash = Convert.ToHexString(SHA256.HashData(Payload)).ToLowerInvariant();
                if (request.RequestUri.AbsolutePath.EndsWith("manifest.json"))
                    return Json(new { schemaVersion = Failure == "future-schema" ? 2 : 1,
                        cycle = MobileAirportUpdates.AiracCycle(Clock.Now.AddDays(Failure == "stale-cycle" ? -28 : 0)),
                        count = Failure == "count" ? 6000 : 5000,
                        sha256 = Failure == "checksum" ? new string('0', 64) : hash,
                        path = Failure == "path" ? "../private-data" : $"snapshots/{(Failure == "checksum" ? new string('0', 64) : hash)}.json.gz" });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) };
            });
            Http = new HttpClient(Handler) { BaseAddress = new Uri("https://localhost/") };
            Service = new MobileAirportUpdates(Http, Js, Clock, Catalog, NullLogger<MobileAirportUpdates>.Instance);
        }
        public static MobileAirportCatalog Seed() => MobileAirportCatalog.Create([
            new("YSSY", "Our Sydney", "SYD", "SY", -33, 151), new("YOLD", "Old airport", null, null, 0, 0)]);
        public static byte[] PayloadFor(double latitude, string failure = "")
        {
            var airports = Enumerable.Range(0, 5000).Select(index => index == 0
                ? new MobileAirport("YSSY", "Replaced remote name", null, null, failure == "coordinates" ? 91 : latitude, 151)
                : new MobileAirport(failure == "duplicates" ? "YSSY" : $"X{index:D4}", $"Airport {index}", null, null, 0, 0)).ToArray();
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionMode.Compress, true))
                gzip.Write(JsonSerializer.SerializeToUtf8Bytes(airports));
            return output.ToArray();
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) {
            Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(response(request));
        }
    }
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.Parse("2026-10-17T00:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class LocalStore : IJSRuntime
    {
        public Dictionary<string, string> Values { get; } = [];
        public bool FailSave;
        public ValueTask<T> InvokeAsync<T>(string id, object?[]? args) => InvokeAsync<T>(id, default, args);
        public ValueTask<T> InvokeAsync<T>(string id, CancellationToken cancellationToken, object?[]? args)
        {
            var key = (string)args![0]!;
            if (id == "electronicLogbookStore.load") return ValueTask.FromResult((T)(object?)Values.GetValueOrDefault(key)!);
            Assert.Equal("electronicLogbookStore.save", id);
            if (FailSave) throw new JSException("Storage unavailable");
            Values[key] = (string)args[1]!;
            return ValueTask.FromResult(default(T)!);
        }
    }
}
