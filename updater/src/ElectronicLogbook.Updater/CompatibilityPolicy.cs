using System.Text.Json;
using System.Text.RegularExpressions;

namespace ElectronicLogbook.Updater;

public sealed record CompatibilityPolicy(
    string MinimumSupportedVersion,
    string Source)
{
    public static CompatibilityPolicy Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Compatibility policy not found.", path);
        }

        var policy = JsonSerializer.Deserialize<CompatibilityPolicy>(
            File.ReadAllText(path),
            JsonDefaults.Web) ?? throw new InvalidDataException("Compatibility policy could not be parsed.");

        _ = SemVer.Parse(policy.MinimumSupportedVersion);
        if (!string.Equals(policy.Source, "git-tags", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Unsupported compatibility policy source: {policy.Source}.");
        }

        return policy;
    }

    public IReadOnlyList<string> SupportedTags(
        IEnumerable<string> tags,
        string currentVersion)
    {
        var minimum = SemVer.Parse(MinimumSupportedVersion);
        var current = SemVer.Parse(currentVersion);

        return tags
            .Select(tag => (Tag: tag, Version: SemVer.TryParseTag(tag)))
            .Where(item => item.Version is not null)
            .Select(item => (item.Tag, Version: item.Version!.Value))
            .Where(item => item.Version.CompareTo(minimum) >= 0 &&
                item.Version.CompareTo(current) < 0)
            .OrderBy(item => item.Version)
            .Select(item => item.Tag)
            .ToArray();
    }

    private readonly record struct SemVer(int Major, int Minor, int Patch)
        : IComparable<SemVer>
    {
        private static readonly Regex VersionPattern = new(
            @"^(?:v)?(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public static SemVer Parse(string value)
        {
            var parsed = TryParseTag(value);
            if (parsed is null)
            {
                throw new InvalidDataException(
                    $"Version must use semantic version format X.Y.Z or vX.Y.Z: {value}");
            }

            return parsed.Value;
        }

        public static SemVer? TryParseTag(string value)
        {
            var match = VersionPattern.Match(value.Trim());
            if (!match.Success)
            {
                return null;
            }

            return new(
                int.Parse(match.Groups["major"].Value),
                int.Parse(match.Groups["minor"].Value),
                int.Parse(match.Groups["patch"].Value));
        }

        public int CompareTo(SemVer other)
        {
            var major = Major.CompareTo(other.Major);
            if (major != 0)
            {
                return major;
            }

            var minor = Minor.CompareTo(other.Minor);
            return minor != 0
                ? minor
                : Patch.CompareTo(other.Patch);
        }
    }
}
