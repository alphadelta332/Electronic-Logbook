using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

namespace ElectronicLogbook.Mobile;

public sealed class MobileAirportCatalog
{
    private const string EmbeddedResourceName = "ElectronicLogbook.Mobile.Data.airports.json.gz";
    private static readonly Lazy<MobileAirportCatalog> Embedded = new(LoadEmbedded);
    private readonly IReadOnlyDictionary<string, MobileAirport> airportsByAlias;

    private MobileAirportCatalog(IEnumerable<MobileAirport> airports)
    {
        var aliases = new Dictionary<string, MobileAirport>(StringComparer.OrdinalIgnoreCase);
        foreach (var airport in airports)
        {
            AddAlias(aliases, airport.Icao, airport);
            AddAlias(aliases, airport.ThreeLetterCode, airport);
            AddAlias(aliases, airport.TwoLetterCode, airport);
        }

        airportsByAlias = aliases;
    }

    public static MobileAirportCatalog Default => Embedded.Value;

    public static MobileAirportCatalog Create(IEnumerable<MobileAirport> airports)
    {
        ArgumentNullException.ThrowIfNull(airports);
        return new MobileAirportCatalog(airports);
    }

    public bool TryFind(string? code, out MobileAirport airport)
    {
        var candidate = code?.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            airport = default!;
            return false;
        }

        return airportsByAlias.TryGetValue(candidate, out airport!);
    }

    public static double GreatCircleDistanceNm(MobileAirport first, MobileAirport second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        const double earthRadiusNm = 3440.065;
        var firstLatitude = DegreesToRadians(first.Latitude);
        var secondLatitude = DegreesToRadians(second.Latitude);
        var latitudeDelta = DegreesToRadians(second.Latitude - first.Latitude);
        var longitudeDelta = DegreesToRadians(second.Longitude - first.Longitude);
        var haversine =
            Math.Pow(Math.Sin(latitudeDelta / 2), 2) +
            Math.Cos(firstLatitude) * Math.Cos(secondLatitude) *
            Math.Pow(Math.Sin(longitudeDelta / 2), 2);
        var angularDistance = haversine switch
        {
            <= 0 => 0,
            >= 1 => Math.PI,
            _ => 2 * Math.Atan2(Math.Sqrt(haversine), Math.Sqrt(1 - haversine))
        };
        return earthRadiusNm * angularDistance;
    }

    private static MobileAirportCatalog LoadEmbedded()
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException($"Embedded airport catalog '{EmbeddedResourceName}' was not found.");
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        var airports = JsonSerializer.Deserialize<MobileAirport[]>(gzip)
            ?? throw new InvalidOperationException("The embedded airport catalog was empty.");
        return new MobileAirportCatalog(airports);
    }

    private static void AddAlias(
        IDictionary<string, MobileAirport> aliases,
        string? alias,
        MobileAirport airport)
    {
        if (!string.IsNullOrWhiteSpace(alias))
        {
            aliases.TryAdd(alias.Trim(), airport);
        }
    }

    private static double DegreesToRadians(double degrees) => degrees * (Math.PI / 180);
}

public sealed record MobileAirport(
    [property: System.Text.Json.Serialization.JsonPropertyName("i")] string Icao,
    [property: System.Text.Json.Serialization.JsonPropertyName("n")] string Name,
    [property: System.Text.Json.Serialization.JsonPropertyName("a")] string? ThreeLetterCode,
    [property: System.Text.Json.Serialization.JsonPropertyName("b")] string? TwoLetterCode,
    [property: System.Text.Json.Serialization.JsonPropertyName("y")] double Latitude,
    [property: System.Text.Json.Serialization.JsonPropertyName("x")] double Longitude);
