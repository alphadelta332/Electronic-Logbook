using System.Text.Json;

namespace ElectronicLogbook.Updater;

public sealed record SupabaseHostedSyncConfiguration(
    Uri SupabaseUrl,
    string AnonKey,
    string PlatformLabel)
{
    private const string UrlEnvironmentVariable = "ELECTRONIC_LOGBOOK_SUPABASE_URL";
    private const string AnonKeyEnvironmentVariable = "ELECTRONIC_LOGBOOK_SUPABASE_ANON_KEY";

    public static bool TryLoad(out SupabaseHostedSyncConfiguration? configuration, out string? unavailableReason)
    {
        var url = Environment.GetEnvironmentVariable(UrlEnvironmentVariable);
        var anonKey = Environment.GetEnvironmentVariable(AnonKeyEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(url) || !string.IsNullOrWhiteSpace(anonKey))
        {
            return TryCreate(url, anonKey, Environment.MachineName, out configuration, out unavailableReason);
        }

        foreach (var path in CandidateDevelopmentConfigPaths())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var json = JsonSerializer.Deserialize<HostedSyncConfigFile>(
                    File.ReadAllText(path),
                    JsonDefaults.Web);
                if (json is not null &&
                    TryCreate(json.SupabaseUrl, json.AnonKey, json.PlatformLabel, out configuration, out unavailableReason))
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                configuration = null;
                unavailableReason = "Hosted sync configuration could not be read.";
                return false;
            }
        }

        configuration = null;
        unavailableReason = "Hosted transport is not configured in this updater build; workbook changes are queued locally.";
        return false;
    }

    internal static bool TryCreate(
        string? url,
        string? anonKey,
        string? platformLabel,
        out SupabaseHostedSyncConfiguration? configuration,
        out string? unavailableReason)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(anonKey))
        {
            configuration = null;
            unavailableReason = "Hosted Supabase configuration is incomplete.";
            return false;
        }

        if (!Uri.TryCreate(url.TrimEnd('/'), UriKind.Absolute, out var supabaseUrl) ||
            (supabaseUrl.Scheme != Uri.UriSchemeHttps && !supabaseUrl.IsLoopback))
        {
            configuration = null;
            unavailableReason = "Hosted Supabase URL is not a valid secure absolute URI.";
            return false;
        }

        configuration = new SupabaseHostedSyncConfiguration(
            supabaseUrl,
            anonKey.Trim(),
            string.IsNullOrWhiteSpace(platformLabel)
                ? $"Excel on {Environment.MachineName}"
                : platformLabel.Trim());
        unavailableReason = null;
        return true;
    }

    private static IEnumerable<string> CandidateDevelopmentConfigPaths()
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(Path.GetFullPath(root));
            for (var depth = 0; directory is not null && depth < 10; depth++, directory = directory.Parent)
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    "mobile",
                    "src",
                    "ElectronicLogbook.Mobile",
                    "wwwroot",
                    "hosted-sync.local.json");
                if (yielded.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }
    }

    private sealed record HostedSyncConfigFile(
        string SupabaseUrl,
        string AnonKey,
        string? PlatformLabel = null);
}
