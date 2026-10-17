using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.JSInterop;

namespace ElectronicLogbook.Mobile;

public sealed class MobileAirportUpdates(
    HttpClient http, IJSRuntime js, TimeProvider clock, MobileAirportCatalog catalog, ILogger<MobileAirportUpdates> logger)
{
    private const string CacheKey = "airport-catalog-v1";
    private const int MaxJsonBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private Task? loadTask;
    private Task? checkTask;
    private AirportCache? cache;
    private DateTimeOffset? lastAttempt;

    public static DateOnly AiracCycle(DateTimeOffset now)
    {
        var epoch = new DateOnly(2024, 1, 25);
        var day = DateOnly.FromDateTime(now.UtcDateTime);
        return epoch.AddDays((int)Math.Floor((day.DayNumber - epoch.DayNumber) / 28d) * 28);
    }

    public Task LoadCachedAsync() => loadTask ??= LoadCoreAsync();

    private async Task LoadCoreAsync()
    {
        try
        {
            var json = await js.InvokeAsync<string?>("electronicLogbookStore.load", CacheKey);
            if (json is null) return;
            if (json.Length > MaxJsonBytes) throw new InvalidDataException("Airport cache too large.");
            var loaded = JsonSerializer.Deserialize<AirportCache>(json, Json) ?? throw new InvalidDataException("Invalid airport cache.");
            if (loaded.SchemaVersion != 1 || loaded.Airports is null || loaded.Airports.Length is < 5000 or > 100000)
                throw new InvalidDataException("Invalid airport cache.");
            var airports = catalog.Merge(loaded.Airports);
            catalog.Replace(airports);
            cache = loaded with { Airports = airports };
        }
        catch (Exception ex) when (Expected(ex)) { LogFailure(ex); }
    }

    public Task CheckAsync() => checkTask is { IsCompleted: false } ? checkTask : checkTask = CheckCoreAsync();

    public Task NetworkRestoredAsync()
    {
        lastAttempt = null;
        return CheckAsync();
    }

    private async Task CheckCoreAsync()
    {
        await LoadCachedAsync();
        var now = clock.GetUtcNow();
        var cycle = AiracCycle(now);
        if (cache?.CheckedCycle == cycle || lastAttempt is { } attempted && now - attempted < TimeSpan.FromMinutes(5)) return;
        lastAttempt = now;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            var config = await http.GetFromJsonAsync<AirportHostConfig>("hosted-sync.local.json", Json, timeout.Token);
            if (!Uri.TryCreate(config?.SupabaseUrl, UriKind.Absolute, out var origin) || origin.Scheme != "https" ||
                !origin.Host.EndsWith(".supabase.co", StringComparison.OrdinalIgnoreCase) || origin.AbsolutePath != "/")
                throw new InvalidDataException("Invalid airport catalogue host.");
            var root = new Uri(origin, "/storage/v1/object/public/airport-catalog/");
            var manifestBytes = await DownloadAsync(new Uri(root, $"manifest.json?cycle={cycle:yyyy-MM-dd}"), 4096, timeout.Token);
            var manifest = JsonSerializer.Deserialize<AirportManifest>(manifestBytes, Json) ?? throw new InvalidDataException("Missing airport manifest.");
            if (manifest.SchemaVersion != 1 || manifest.Cycle != cycle || manifest.Count is < 5000 or > 100000 ||
                manifest.Sha256 is null || manifest.Sha256.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit) ||
                manifest.Path != $"snapshots/{manifest.Sha256}.json.gz")
                throw new InvalidDataException("Invalid or outdated airport manifest.");
            MobileAirport[] airports;
            if (cache?.Sha256 == manifest.Sha256) airports = cache.Airports;
            else
            {
                var compressed = await DownloadAsync(new Uri(root, manifest.Path), 4 * 1024 * 1024, timeout.Token);
                if (!Convert.ToHexString(SHA256.HashData(compressed)).Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Airport catalogue checksum mismatch.");
                using var input = new MemoryStream(compressed);
                using var gzip = new GZipStream(input, CompressionMode.Decompress);
                using var json = new MemoryStream();
                var buffer = new byte[8192];
                int count;
                while ((count = await gzip.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    if (json.Length + count > MaxJsonBytes) throw new InvalidDataException("Airport catalogue too large.");
                    json.Write(buffer, 0, count);
                }
                var downloaded = JsonSerializer.Deserialize<MobileAirport[]>(json.ToArray(), Json) ?? throw new InvalidDataException("Empty airport catalogue.");
                if (downloaded.Length != manifest.Count || downloaded.Select(airport => airport?.Icao).Distinct(StringComparer.OrdinalIgnoreCase).Count() != downloaded.Length)
                    throw new InvalidDataException("Incomplete or duplicate airport catalogue.");
                airports = catalog.Merge(downloaded);
            }
            var next = new AirportCache(1, cycle, manifest.Sha256, airports);
            await js.InvokeVoidAsync("electronicLogbookStore.save", CacheKey, JsonSerializer.Serialize(next, Json));
            cache = next;
            catalog.Replace(airports);
        }
        catch (Exception ex) when (Expected(ex)) { LogFailure(ex); }
    }

    private async Task<byte[]> DownloadAsync(Uri uri, int limit, CancellationToken token)
    {
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(limit, token);
        return await response.Content.ReadAsByteArrayAsync(token);
    }

    private static bool Expected(Exception ex) => ex is HttpRequestException or JSException or JsonException or
        InvalidDataException or OperationCanceledException or ArgumentException or IOException;

    private void LogFailure(Exception ex) => logger.LogWarning("Airport catalogue update unavailable ({FailureType}); previous catalogue retained.", ex.GetType().Name);

    private sealed record AirportHostConfig(string SupabaseUrl);
    private sealed record AirportManifest(int SchemaVersion, DateOnly Cycle, string Sha256, string Path, int Count);
    private sealed record AirportCache(int SchemaVersion, DateOnly CheckedCycle, string Sha256, MobileAirport[] Airports);
}
