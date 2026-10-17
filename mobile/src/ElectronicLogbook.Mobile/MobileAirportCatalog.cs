using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

namespace ElectronicLogbook.Mobile;

public sealed class MobileAirportCatalog
{
    private const string EmbeddedResourceName = "ElectronicLogbook.Mobile.Data.airports.json.gz";
    private static readonly Lazy<MobileAirportCatalog> Embedded = new(LoadEmbedded);
    private IReadOnlyDictionary<string, MobileAirport> airportsByAlias;
    public event Action? Changed;
    public IReadOnlyList<MobileAirport> Airports => airportsByAlias.Values.DistinctBy(airport => airport.Icao).ToArray();

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

    public MobileAirport[] Merge(IEnumerable<MobileAirport> updates)
    {
        var merged = Airports.ToDictionary(airport => airport.Icao, StringComparer.OrdinalIgnoreCase);
        foreach (var airport in updates)
        {
            if (airport is null || string.IsNullOrWhiteSpace(airport.Icao) || airport.Icao.Length > 16 ||
                string.IsNullOrWhiteSpace(airport.Name) || airport.Name.Length > 500 ||
                !double.IsFinite(airport.Latitude) || Math.Abs(airport.Latitude) > 90 ||
                !double.IsFinite(airport.Longitude) || Math.Abs(airport.Longitude) > 180 ||
                airport.ThreeLetterCode?.Length > 16 || airport.TwoLetterCode?.Length > 16)
                throw new InvalidDataException("Invalid airport catalogue record.");
            merged[airport.Icao] = merged.TryGetValue(airport.Icao, out var prior)
                ? prior with { Latitude = airport.Latitude, Longitude = airport.Longitude }
                : airport;
        }
        return merged.Values.ToArray();
    }

    public void Replace(IEnumerable<MobileAirport> airports)
    {
        var next = new MobileAirportCatalog(airports);
        airportsByAlias = next.airportsByAlias;
        Changed?.Invoke();
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

    public IReadOnlyList<MobileAirport> Search(string query, int limit)
    {
        var text = query.Trim();
        if (text.Length == 0 || limit <= 0) return [];
        return airportsByAlias.Values.DistinctBy(airport => airport.Icao)
            .Where(airport => airport.Icao.StartsWith(text, StringComparison.OrdinalIgnoreCase) ||
                airport.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(airport => airport.Icao.Equals(text, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(airport => airport.Icao.StartsWith(text, StringComparison.OrdinalIgnoreCase))
            .ThenBy(airport => airport.Icao, StringComparer.OrdinalIgnoreCase)
            .Take(limit).ToArray();
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
