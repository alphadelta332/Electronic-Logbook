using System.Globalization;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ElectronicLogbook.Updater;

public sealed class ExcelWorkbookMigrator
{
    private const int AutomationSecurityForceDisable = 3;
    private const int XlCalculationManual = -4135;
    private const int XlCalculationAutomatic = -4105;

    private static readonly string[] PreservedNames =
    [
        "RoutesBuilt",
        "RoutesDirty",
        "RoutesDefinitionVersion",
        "DateAfterExport",
        "suppressWarningsUntil"
    ];

    public MigrationReport Migrate(MigrationRequest request)
    {
        request = ValidateRequest(request);

        var outputDirectory = Path.GetDirectoryName(request.OutputPath)!;
        Directory.CreateDirectory(outputDirectory);
        File.Copy(request.MasterPath, request.OutputPath, overwrite: false);

        dynamic? excel = null;
        dynamic? sourceWorkbook = null;
        dynamic? outputWorkbook = null;
        var excelProcessId = 0;
        var migrationSucceeded = false;
        var step = "starting Excel";

        try
        {
            var excelType = Type.GetTypeFromProgID("Excel.Application") ??
                throw new InvalidOperationException("Microsoft Excel is not installed.");
            excel = Activator.CreateInstance(excelType) ??
                throw new InvalidOperationException("Microsoft Excel could not be started.");
            GetWindowThreadProcessId((IntPtr)excel.Hwnd, out excelProcessId);

            excel.Visible = false;
            excel.DisplayAlerts = false;
            excel.EnableEvents = false;
            excel.ScreenUpdating = false;
            excel.AutomationSecurity = AutomationSecurityForceDisable;

            step = "opening source workbook";
            sourceWorkbook = excel.Workbooks.Open(request.SourcePath, 0, true);
            step = "opening master copy";
            outputWorkbook = excel.Workbooks.Open(request.OutputPath, 0, false);
            excel.Calculation = XlCalculationManual;

            step = "reading source validation data";
            var sourceVersion = ReadName((object)sourceWorkbook, "LogbookVersion");
            IReadOnlyDictionary<string, string> sourceFingerprints =
                ReadPreservedFingerprints((object)sourceWorkbook);

            step = "copying Logbook data";
            CopyLogbook((object)sourceWorkbook, (object)outputWorkbook);
            step = "copying Keywords data";
            CopyTableByMatchingColumns((object)sourceWorkbook, (object)outputWorkbook, "Keywords");
            step = "copying Routes data";
            CopyTableByMatchingColumns((object)sourceWorkbook, (object)outputWorkbook, "Routes");
            step = "copying airport base flags";
            CopyAirportBaseFlags((object)sourceWorkbook, (object)outputWorkbook);
            step = "copying named preferences";
            CopyNamedPreferences((object)sourceWorkbook, (object)outputWorkbook);

            var outputVersion = ReadName((object)outputWorkbook, "LogbookVersion");
            if (request.Manifest is not null &&
                !string.Equals(outputVersion, request.Manifest.Version, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Output workbook version {outputVersion} does not match verified release " +
                    $"{request.Manifest!.Version}.");
            }

            step = "calculating output workbook";
            excel.Calculation = XlCalculationAutomatic;
            foreach (dynamic worksheet in outputWorkbook.Worksheets)
            {
                worksheet.Calculate();
            }

            step = "validating preserved data";
            IReadOnlyDictionary<string, string> outputFingerprints =
                ReadPreservedFingerprints((object)outputWorkbook);
            foreach (var expected in sourceFingerprints)
            {
                if (!outputFingerprints.TryGetValue(expected.Key, out var actual) ||
                    !string.Equals(expected.Value, actual, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Preserved data validation failed for {expected.Key}.");
                }
            }

            step = "saving output workbook";
            outputWorkbook.RemovePersonalInformation = false;
            outputWorkbook.Save();

            dynamic outputLogbook = GetTable((object)outputWorkbook, "Logbook");
            var logbookRows = (int)outputLogbook.ListRows.Count;
            migrationSucceeded = true;
            return new MigrationReport(
                request.SourcePath,
                request.MasterPath,
                request.OutputPath,
                sourceVersion,
                outputVersion,
                logbookRows,
                sourceFingerprints,
                DateTimeOffset.UtcNow,
                "validated");
        }
        catch (Exception ex)
        {
            CloseWorkbook(outputWorkbook);
            outputWorkbook = null;
            CloseWorkbook(sourceWorkbook);
            sourceWorkbook = null;
            throw new InvalidOperationException(
                $"Migration failed at {step}: {ex.Message}",
                ex);
        }
        finally
        {
            CloseWorkbook(outputWorkbook);
            CloseWorkbook(sourceWorkbook);
            if (excel is not null)
            {
                try { excel.Calculation = XlCalculationAutomatic; } catch { }
                try { excel.EnableEvents = true; } catch { }
                try { excel.ScreenUpdating = true; } catch { }
                try { excel.Quit(); } catch { }
                ReleaseComObject(excel);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            EnsureProcessExited(excelProcessId);
            if (!migrationSucceeded)
            {
                TryDelete(request.OutputPath);
            }
        }
    }

    private static MigrationRequest ValidateRequest(MigrationRequest request)
    {
        request = request with
        {
            SourcePath = Path.GetFullPath(request.SourcePath),
            MasterPath = Path.GetFullPath(request.MasterPath),
            OutputPath = Path.GetFullPath(request.OutputPath)
        };

        if (!File.Exists(request.SourcePath))
        {
            throw new FileNotFoundException("Source workbook not found.", request.SourcePath);
        }
        if (!File.Exists(request.MasterPath))
        {
            throw new FileNotFoundException("Master workbook not found.", request.MasterPath);
        }
        if (File.Exists(request.OutputPath))
        {
            throw new IOException($"Output path already exists: {request.OutputPath}");
        }
        if (string.Equals(request.SourcePath, request.OutputPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(request.MasterPath, request.OutputPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Output path must differ from source and master paths.");
        }

        return request;
    }

    private static void CopyLogbook(object sourceWorkbookObject, object outputWorkbookObject)
    {
        dynamic source = GetTable(sourceWorkbookObject, "Logbook");
        dynamic destination = GetTable(outputWorkbookObject, "Logbook");
        var sourceRows = (int)source.ListRows.Count;
        if (sourceRows <= 0)
        {
            throw new InvalidDataException("Source Logbook table does not contain rows.");
        }

        var sourceDetails = GetColumnIndex(source, "Details");
        var sourceDataStart = GetColumnIndex(source, "Year");
        var sourceDataEnd = GetColumnIndex(source, "Circling");
        var destinationDetails = GetColumnIndex(destination, "Details");
        var destinationDataStart = GetColumnIndex(destination, "Year");
        var destinationDataEnd = GetColumnIndex(destination, "Circling");

        var sourceCustomCount = GetColumnIndex(source, "SeIcusDay") - sourceDetails - 1;
        var destinationCustomCount = GetColumnIndex(destination, "SeIcusDay") - destinationDetails - 1;
        if (sourceCustomCount != destinationCustomCount)
        {
            throw new InvalidDataException(
                "Source and master workbooks have different custom-column counts.");
        }

        for (var offset = 1; offset <= sourceCustomCount; offset++)
        {
            destination.ListColumns.Item(destinationDetails + offset).Name =
                source.ListColumns.Item(sourceDetails + offset).Name;
        }

        ResizeTable(destination, sourceRows);
        FillFormulaColumns(destination, destinationDataStart, destinationDataEnd);

        for (var sourceIndex = sourceDataStart; sourceIndex <= sourceDataEnd; sourceIndex++)
        {
            var name = (string)source.ListColumns.Item(sourceIndex).Name;
            CopyColumn(source, destination, name, sourceRows);
        }
        CopyColumn(source, destination, "CurrencyExclusions", sourceRows);
    }

    private static void CopyTableByMatchingColumns(
        object sourceWorkbook,
        object outputWorkbook,
        string tableName)
    {
        dynamic source = GetTable(sourceWorkbook, tableName);
        dynamic destination = GetTable(outputWorkbook, tableName);
        var rows = (int)source.ListRows.Count;
        ResizeTable(destination, rows);
        if (rows <= 0)
        {
            return;
        }

        for (var index = 1; index <= (int)source.ListColumns.Count; index++)
        {
            var name = (string)source.ListColumns.Item(index).Name;
            if (HasColumn((object)destination, name))
            {
                CopyColumnPreservingFormula(source, destination, name, rows);
            }
        }
    }

    private static void CopyAirportBaseFlags(object sourceWorkbook, object outputWorkbook)
    {
        dynamic source = GetTable(sourceWorkbook, "Airports");
        dynamic destination = GetTable(outputWorkbook, "Airports");
        var sourceRows = (int)source.ListRows.Count;
        var destinationRows = (int)destination.ListRows.Count;
        var sourceIcao = ReadColumnValues((object)source, "ICAO", sourceRows);
        var sourceBase = ReadColumnValues((object)source, "Base", sourceRows);
        var bases = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var row = 0; row < sourceRows; row++)
        {
            var icao = Convert.ToString(sourceIcao[row], CultureInfo.InvariantCulture)?.Trim();
            if (!string.IsNullOrEmpty(icao) && !IsBlankValue(sourceBase[row]))
            {
                bases[icao] = sourceBase[row];
            }
        }

        var destinationIcao = ReadColumnValues((object)destination, "ICAO", destinationRows);
        dynamic destinationBase = destination.ListColumns.Item(GetColumnIndex(destination, "Base"))
            .DataBodyRange;
        destinationBase.ClearContents();
        for (var row = 0; row < destinationRows; row++)
        {
            var icao = Convert.ToString(destinationIcao[row], CultureInfo.InvariantCulture)?.Trim();
            if (icao is not null && bases.TryGetValue(icao, out var value))
            {
                destinationBase.Cells.Item(row + 1, 1).Value2 = value;
            }
        }
    }

    private static void CopyNamedPreferences(object sourceWorkbookObject, object outputWorkbookObject)
    {
        dynamic sourceWorkbook = sourceWorkbookObject;
        dynamic outputWorkbook = outputWorkbookObject;
        foreach (var name in PreservedNames)
        {
            try
            {
                outputWorkbook.Names.Item(name).RefersToRange.Value2 =
                    sourceWorkbook.Names.Item(name).RefersToRange.Value2;
            }
            catch
            {
                // Preferences added in newer versions may not exist in old workbooks.
            }
        }
    }

    private static IReadOnlyDictionary<string, string> ReadPreservedFingerprints(object workbook)
    {
        var fingerprints = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Keywords"] = FingerprintTable(workbook, "Keywords"),
            ["Routes"] = FingerprintColumns(
                workbook,
                "Routes",
                new[] { "DepAirport", "ArrAirport" }),
            ["AirportBases"] = FingerprintAirportBases(workbook),
            ["Preferences"] = FingerprintNames(workbook, PreservedNames)
        };
        AddLogbookFingerprints(workbook, fingerprints);
        return fingerprints;
    }

    private static void AddLogbookFingerprints(
        dynamic workbook,
        IDictionary<string, string> fingerprints)
    {
        dynamic table = GetTable(workbook, "Logbook");
        var start = GetColumnIndex(table, "Year");
        var end = GetColumnIndex(table, "Circling");
        var names = new List<string>();
        for (var index = start; index <= end; index++)
        {
            names.Add((string)table.ListColumns.Item(index).Name);
        }
        names.Add("CurrencyExclusions");

        fingerprints["LogbookHeaders"] = Sha256(string.Join('\u001f', names));
        foreach (var name in names)
        {
            fingerprints[$"Logbook:{name}"] = FingerprintColumns(
                workbook,
                "Logbook",
                new[] { name });
        }
    }

    private static string FingerprintTable(dynamic workbook, string tableName)
    {
        dynamic table = GetTable(workbook, tableName);
        var columns = new List<string>();
        for (var index = 1; index <= (int)table.ListColumns.Count; index++)
        {
            columns.Add((string)table.ListColumns.Item(index).Name);
        }
        return FingerprintColumns(workbook, tableName, columns);
    }

    private static string FingerprintAirportBases(dynamic workbook)
    {
        dynamic table = GetTable(workbook, "Airports");
        var rows = (int)table.ListRows.Count;
        var icaoValues = ReadColumnValues((object)table, "ICAO", rows);
        var baseValues = ReadColumnValues((object)table, "Base", rows);
        var selections = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var row = 0; row < rows; row++)
        {
            var icao = StableValue(icaoValues[row]).Trim();
            var baseValue = StableValue(baseValues[row]);
            if (!string.IsNullOrEmpty(icao) && !IsBlankValue(baseValues[row]))
            {
                selections[icao] = baseValue;
            }
        }

        var builder = new StringBuilder("AirportBases");
        foreach (var selection in selections)
        {
            builder.Append('|').Append(selection.Key).Append('=').Append(selection.Value);
        }
        return Sha256(builder.ToString());
    }

    private static string FingerprintColumns(
        dynamic workbook,
        string tableName,
        IReadOnlyCollection<string> columns)
    {
        dynamic table = GetTable(workbook, tableName);
        var rows = (int)table.ListRows.Count;
        var builder = new StringBuilder();
        builder.Append(tableName).Append('|').Append(rows);
        foreach (var column in columns)
        {
            builder.Append('|').Append(column);
            foreach (var value in ReadColumnValues(table, column, rows))
            {
                builder.Append('\u001f').Append(StableValue(value));
            }
        }
        return Sha256(builder.ToString());
    }

    private static string FingerprintNames(dynamic workbook, IEnumerable<string> names)
    {
        var builder = new StringBuilder();
        foreach (var name in names)
        {
            builder.Append(name).Append('=').Append(ReadName(workbook, name)).Append('|');
        }
        return Sha256(builder.ToString());
    }

    private static void CopyColumn(dynamic source, dynamic destination, string name, int rows)
    {
        var sourceIndex = GetColumnIndex(source, name);
        var destinationIndex = GetColumnIndex(destination, name);
        destination.DataBodyRange.Columns.Item(destinationIndex).Resize[rows, 1].Value2 =
            source.DataBodyRange.Columns.Item(sourceIndex).Resize[rows, 1].Value2;
    }

    private static void CopyColumnPreservingFormula(
        dynamic source,
        dynamic destination,
        string name,
        int rows)
    {
        var sourceIndex = GetColumnIndex(source, name);
        var destinationIndex = GetColumnIndex(destination, name);
        dynamic sourceRange = source.DataBodyRange.Columns.Item(sourceIndex).Resize[rows, 1];
        dynamic destinationRange =
            destination.DataBodyRange.Columns.Item(destinationIndex).Resize[rows, 1];
        dynamic firstCell = source.DataBodyRange.Cells.Item(1, sourceIndex);
        if ((bool)firstCell.HasFormula)
        {
            destinationRange.Formula = sourceRange.Formula;
        }
        else
        {
            destinationRange.Value2 = sourceRange.Value2;
        }
    }

    private static object?[] ReadColumnValues(dynamic table, string name, int rows)
    {
        if (rows <= 0)
        {
            return [];
        }

        var index = GetColumnIndex(table, name);
        object? raw = table.DataBodyRange.Columns.Item(index).Resize[rows, 1].Value2;
        if (raw is Array array)
        {
            var values = new object?[rows];
            for (var row = 1; row <= rows; row++)
            {
                values[row - 1] = array.GetValue(row, 1);
            }
            return values;
        }
        return [raw];
    }

    private static void ResizeTable(dynamic table, int rows)
    {
        var currentRows = (int)table.ListRows.Count;
        if (rows <= 0)
        {
            if (table.DataBodyRange is not null)
            {
                table.DataBodyRange.Delete();
            }
            return;
        }
        if (currentRows == 0)
        {
            for (var index = 0; index < rows; index++)
            {
                table.ListRows.Add();
            }
            return;
        }
        var totalsRows = (bool)table.ShowTotals ? 1 : 0;
        table.Resize(table.Range.Resize[rows + 1 + totalsRows, table.ListColumns.Count]);
    }

    private static void FillFormulaColumns(dynamic table, int dataStart, int dataEnd)
    {
        var rows = (int)table.ListRows.Count;
        if (rows <= 1)
        {
            return;
        }
        for (var index = 1; index <= (int)table.ListColumns.Count; index++)
        {
            if (index >= dataStart && index <= dataEnd)
            {
                continue;
            }
            dynamic firstCell = table.DataBodyRange.Cells.Item(1, index);
            if ((bool)firstCell.HasFormula)
            {
                table.DataBodyRange.Columns.Item(index).FillDown();
            }
        }
    }

    private static dynamic GetTable(object workbookObject, string tableName)
    {
        dynamic workbook = workbookObject;
        foreach (dynamic worksheet in workbook.Worksheets)
        {
            foreach (dynamic table in worksheet.ListObjects)
            {
                if (string.Equals(
                    (string)table.Name,
                    tableName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return table;
                }
            }
        }
        throw new InvalidDataException($"Workbook table not found: {tableName}");
    }

    private static int GetColumnIndex(dynamic table, string name)
    {
        for (var index = 1; index <= (int)table.ListColumns.Count; index++)
        {
            if (string.Equals(
                (string)table.ListColumns.Item(index).Name,
                name,
                StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }
        throw new InvalidDataException($"Table {table.Name} is missing column {name}.");
    }

    private static bool HasColumn(object tableObject, string name)
    {
        dynamic table = tableObject;
        for (var index = 1; index <= (int)table.ListColumns.Count; index++)
        {
            if (string.Equals(
                (string)table.ListColumns.Item(index).Name,
                name,
                StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string ReadName(dynamic workbook, string name)
    {
        try
        {
            return StableValue(workbook.Names.Item(name).RefersToRange.Value2);
        }
        catch
        {
            return "";
        }
    }

    private static string StableValue(object? value)
    {
        return value switch
        {
            null => "<null>",
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            float number => number.ToString("R", CultureInfo.InvariantCulture),
            DateTime date => date.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            bool flag => flag ? "true" : "false",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
        };
    }

    private static bool IsBlankValue(object? value)
    {
        return value is null ||
            (value is string text && string.IsNullOrWhiteSpace(text));
    }

    private static string Sha256(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void CloseWorkbook(dynamic? workbook)
    {
        if (workbook is null)
        {
            return;
        }
        try { workbook.Close(false); } catch { }
        ReleaseComObject(workbook);
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            try { Marshal.FinalReleaseComObject(value); } catch { }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Preserve the original exception; the incomplete output is never trusted.
        }
    }

    private static void EnsureProcessExited(int processId)
    {
        if (processId <= 0)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.WaitForExit(3000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
        }
        catch (ArgumentException)
        {
            // The Excel process already exited normally.
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out int processId);
}
