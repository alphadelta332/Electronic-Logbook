using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace ElectronicLogbook.Updater;

public static class WorkbookPackageValidator
{
    private static readonly XNamespace Spreadsheet =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace OfficeRelationships =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationships =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    private static readonly string[] RequiredNames =
    [
        "LogbookVersion",
        "GitHubBranch",
        "DateAfterExport",
        "RoutesBuilt",
        "RoutesDirty",
        "RoutesDefinitionVersion",
        "suppressWarningsUntil"
    ];

    private static readonly string[] RequiredTables =
    [
        "Logbook",
        "Keywords",
        "Airports"
    ];

    public static void ValidateStagedWorkbook(string workbookPath, string expectedVersion)
    {
        ValidateWorkbookPackage(workbookPath, expectedVersion);
    }

    public static string ReadWorkbookDefinedNameValue(string workbookPath, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        workbookPath = Path.GetFullPath(workbookPath);
        if (!File.Exists(workbookPath))
        {
            throw new FileNotFoundException("Workbook not found.", workbookPath);
        }

        using var stream = new FileStream(
            workbookPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var workbook = ReadXmlEntry(archive, "xl/workbook.xml");
        var workbookRelationships = ReadXmlEntry(archive, "xl/_rels/workbook.xml.rels");
        return ReadDefinedNameValue(archive, workbook, workbookRelationships, name);
    }

    public static string ValidateWorkbookPackage(string workbookPath, string? expectedVersion = null)
    {
        workbookPath = Path.GetFullPath(workbookPath);
        if (!File.Exists(workbookPath))
        {
            throw new FileNotFoundException("Staged workbook not found.", workbookPath);
        }

        if (!string.Equals(Path.GetExtension(workbookPath), ".xlsm", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Staged workbook must use the .xlsm extension.");
        }

        using var stream = new FileStream(
            workbookPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        var workbook = ReadXmlEntry(archive, "xl/workbook.xml");
        var workbookRelationships = ReadXmlEntry(archive, "xl/_rels/workbook.xml.rels");

        ValidateRequiredNames(workbook);
        ValidateRequiredTables(archive, workbook, workbookRelationships);

        var actualVersion = ReadDefinedNameValue(
            archive,
            workbook,
            workbookRelationships,
            "LogbookVersion");
        if (expectedVersion is not null &&
            !string.Equals(actualVersion, expectedVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Staged workbook version {actualVersion} does not match expected version {expectedVersion}.");
        }

        return actualVersion;
    }

    private static void ValidateRequiredNames(XDocument workbook)
    {
        var definedNames = workbook
            .Descendants(Spreadsheet + "definedName")
            .Select(name => (string?)name.Attribute("name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var name in RequiredNames)
        {
            if (!definedNames.Contains(name))
            {
                throw new InvalidDataException($"Required workbook name missing: {name}");
            }
        }
    }

    private static void ValidateRequiredTables(
        ZipArchive archive,
        XDocument workbook,
        XDocument workbookRelationships)
    {
        var tables = ReadTableNames(archive, workbook, workbookRelationships);
        foreach (var table in RequiredTables)
        {
            if (!tables.Contains(table))
            {
                throw new InvalidDataException($"Required workbook table missing: {table}");
            }
        }
    }

    private static HashSet<string> ReadTableNames(
        ZipArchive archive,
        XDocument workbook,
        XDocument workbookRelationships)
    {
        var tableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var relationships = workbookRelationships
            .Descendants(PackageRelationships + "Relationship")
            .ToDictionary(
                relationship => (string?)relationship.Attribute("Id") ?? string.Empty,
                relationship => (string?)relationship.Attribute("Target") ?? string.Empty,
                StringComparer.Ordinal);

        foreach (var sheet in workbook.Descendants(Spreadsheet + "sheet"))
        {
            var relationshipId = (string?)sheet.Attribute(OfficeRelationships + "id");
            if (string.IsNullOrWhiteSpace(relationshipId) ||
                !relationships.TryGetValue(relationshipId, out var sheetTarget))
            {
                continue;
            }

            var worksheetPath = ResolveWorkbookRelationshipTarget(sheetTarget);
            var worksheet = ReadXmlEntryOrNull(archive, worksheetPath);
            if (worksheet is null)
            {
                continue;
            }

            var tablePartIds = worksheet
                .Descendants(Spreadsheet + "tablePart")
                .Select(part => (string?)part.Attribute(OfficeRelationships + "id"))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToArray();
            if (tablePartIds.Length == 0)
            {
                continue;
            }

            var worksheetRelationships = ReadXmlEntryOrNull(
                archive,
                BuildRelationshipPath(worksheetPath));
            if (worksheetRelationships is null)
            {
                continue;
            }

            var worksheetDirectory = Path.GetDirectoryName(worksheetPath)?.Replace('\\', '/') ?? "xl/worksheets";
            var tableRelationships = worksheetRelationships
                .Descendants(PackageRelationships + "Relationship")
                .ToDictionary(
                    relationship => (string?)relationship.Attribute("Id") ?? string.Empty,
                    relationship => (string?)relationship.Attribute("Target") ?? string.Empty,
                    StringComparer.Ordinal);
            foreach (var tablePartId in tablePartIds)
            {
                if (string.IsNullOrWhiteSpace(tablePartId) ||
                    !tableRelationships.TryGetValue(tablePartId, out var tableTarget))
                {
                    continue;
                }

                var tablePath = ResolveRelationshipTarget(worksheetDirectory, tableTarget);
                var table = ReadXmlEntryOrNull(archive, tablePath);
                var tableName = (string?)table?.Root?.Attribute("name") ??
                    (string?)table?.Root?.Attribute("displayName");
                if (!string.IsNullOrWhiteSpace(tableName))
                {
                    tableNames.Add(tableName);
                }
            }
        }

        return tableNames;
    }

    private static string ReadDefinedNameValue(
        ZipArchive archive,
        XDocument workbook,
        XDocument workbookRelationships,
        string name)
    {
        var definedName = workbook
            .Descendants(Spreadsheet + "definedName")
            .FirstOrDefault(candidate => string.Equals(
                (string?)candidate.Attribute("name"),
                name,
                StringComparison.OrdinalIgnoreCase));
        if (definedName is null ||
            !TryParseSingleCellReference(definedName.Value, out var sheetName, out var cellReference))
        {
            throw new InvalidDataException($"Required workbook name is invalid: {name}");
        }

        var sheet = workbook
            .Descendants(Spreadsheet + "sheet")
            .FirstOrDefault(candidate => string.Equals(
                (string?)candidate.Attribute("name"),
                sheetName,
                StringComparison.OrdinalIgnoreCase));
        var relationshipId = (string?)sheet?.Attribute(OfficeRelationships + "id");
        if (string.IsNullOrWhiteSpace(relationshipId))
        {
            throw new InvalidDataException($"Workbook sheet not found for name: {name}");
        }

        var target = workbookRelationships
            .Descendants(PackageRelationships + "Relationship")
            .Where(relationship => string.Equals(
                (string?)relationship.Attribute("Id"),
                relationshipId,
                StringComparison.Ordinal))
            .Select(relationship => (string?)relationship.Attribute("Target"))
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new InvalidDataException($"Workbook relationship not found for name: {name}");
        }

        var worksheet = ReadXmlEntry(archive, ResolveWorkbookRelationshipTarget(target));
        var cell = worksheet
            .Descendants(Spreadsheet + "c")
            .FirstOrDefault(candidate => string.Equals(
                (string?)candidate.Attribute("r"),
                cellReference,
                StringComparison.OrdinalIgnoreCase));
        if (cell is null)
        {
            throw new InvalidDataException($"Workbook cell not found for name: {name}");
        }

        return ReadCellValue(archive, cell);
    }

    private static string ReadCellValue(ZipArchive archive, XElement cell)
    {
        var cellType = (string?)cell.Attribute("t");
        if (string.Equals(cellType, "s", StringComparison.Ordinal))
        {
            var sharedStringIndex = (int?)cell.Element(Spreadsheet + "v");
            var sharedStrings = ReadXmlEntry(archive, "xl/sharedStrings.xml");
            return sharedStringIndex.HasValue
                ? sharedStrings
                    .Descendants(Spreadsheet + "si")
                    .ElementAtOrDefault(sharedStringIndex.Value)?
                    .Descendants(Spreadsheet + "t")
                    .Select(text => text.Value)
                    .Aggregate(string.Empty, static (value, text) => value + text) ?? string.Empty
                : string.Empty;
        }

        if (string.Equals(cellType, "inlineStr", StringComparison.Ordinal))
        {
            return string.Concat(cell.Descendants(Spreadsheet + "t").Select(text => text.Value));
        }

        return ((string?)cell.Element(Spreadsheet + "v"))?.Trim() ??
            Convert.ToString(cell.Value, CultureInfo.InvariantCulture)?.Trim() ??
            string.Empty;
    }

    private static XDocument ReadXmlEntry(ZipArchive archive, string entryName)
    {
        return ReadXmlEntryOrNull(archive, entryName) ??
            throw new InvalidDataException($"Workbook package entry missing: {entryName}");
    }

    private static XDocument? ReadXmlEntryOrNull(ZipArchive archive, string entryName)
    {
        var normalisedName = entryName.TrimStart('/').Replace('\\', '/');
        var entry = archive.GetEntry(normalisedName);
        if (entry is null)
        {
            return null;
        }

        using var entryStream = entry.Open();
        return XDocument.Load(entryStream);
    }

    private static bool TryParseSingleCellReference(
        string formula,
        out string sheetName,
        out string cellReference)
    {
        sheetName = string.Empty;
        cellReference = string.Empty;

        var separator = formula.LastIndexOf('!');
        if (separator <= 0 || separator == formula.Length - 1)
        {
            return false;
        }

        sheetName = formula[..separator].Trim().TrimStart('=');
        if (sheetName.Length >= 2 && sheetName[0] == '\'' && sheetName[^1] == '\'')
        {
            sheetName = sheetName[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }

        cellReference = formula[(separator + 1)..].Trim().Replace("$", string.Empty, StringComparison.Ordinal);
        return !string.IsNullOrWhiteSpace(sheetName) &&
            System.Text.RegularExpressions.Regex.IsMatch(cellReference, "^[A-Za-z]+[0-9]+$");
    }

    private static string ResolveWorkbookRelationshipTarget(string target)
    {
        return target.StartsWith("/", StringComparison.Ordinal)
            ? target[1..]
            : $"xl/{target}";
    }

    private static string BuildRelationshipPath(string partPath)
    {
        var directory = Path.GetDirectoryName(partPath)?.Replace('\\', '/') ?? string.Empty;
        var fileName = Path.GetFileName(partPath);
        return string.IsNullOrWhiteSpace(directory)
            ? $"_rels/{fileName}.rels"
            : $"{directory}/_rels/{fileName}.rels";
    }

    private static string ResolveRelationshipTarget(string sourceDirectory, string target)
    {
        if (target.StartsWith("/", StringComparison.Ordinal))
        {
            return target[1..];
        }

        var combined = $"{sourceDirectory}/{target}";
        var parts = new Stack<string>();
        foreach (var part in combined.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
            {
                continue;
            }
            if (part == "..")
            {
                if (parts.Count > 0)
                {
                    parts.Pop();
                }
                continue;
            }
            parts.Push(part);
        }

        return string.Join('/', parts.Reverse());
    }
}
