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
    private const int XlPasteFormats = -4122;
    private const int XlUp = -4162;

    private static readonly string[] PreservedNames =
    [
        "RoutesBuilt",
        "RoutesDirty",
        "RoutesDefinitionVersion",
        "DateAfterExport",
        "suppressWarningsUntil"
    ];

    private readonly IUpdaterProgressSink? _progressSink;

    public ExcelWorkbookMigrator(IUpdaterProgressSink? progressSink = null)
    {
        _progressSink = progressSink;
    }

    public MigrationReport Migrate(MigrationRequest request)
    {
        string phaseId = UpdaterPhaseIds.StartExcel;

        string SetStep(string newPhaseId, string message)
        {
            phaseId = newPhaseId;
            _progressSink?.Report(new UpdaterProgressEvent(
                UpdaterProgressEventTypes.PhaseStarted,
                phaseId,
                message,
                Percent: null,
                DateTimeOffset.UtcNow));
            return message;
        }

        request = ValidateRequest(request);

        var outputDirectory = Path.GetDirectoryName(request.OutputPath)!;
        Directory.CreateDirectory(outputDirectory);
        File.Copy(request.MasterPath, request.OutputPath, overwrite: false);

        dynamic? excel = null;
        dynamic? sourceWorkbook = null;
        dynamic? outputWorkbook = null;
        var excelProcessId = 0;
        var migrationSucceeded = false;
        var step = SetStep(UpdaterPhaseIds.StartExcel, "starting Excel");

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

            step = SetStep(UpdaterPhaseIds.OpenSourceWorkbook, "opening source workbook");
            sourceWorkbook = excel.Workbooks.Open(request.SourcePath, 0, true);
            step = SetStep(UpdaterPhaseIds.OpenMasterCopy, "opening master copy");
            outputWorkbook = excel.Workbooks.Open(request.OutputPath, 0, false);
            step = SetStep(UpdaterPhaseIds.PrepareMasterCopy, "preparing master copy for migration");
            UnprotectWorkbookForMigration((object)outputWorkbook);
            excel.Calculation = XlCalculationManual;

            step = SetStep(UpdaterPhaseIds.ReadSourceValidationData, "reading source validation data");
            var sourceVersion = ReadName((object)sourceWorkbook, "LogbookVersion");
            IReadOnlyDictionary<string, string> sourceFingerprints =
                ReadPreservedFingerprints((object)sourceWorkbook);

            step = SetStep(UpdaterPhaseIds.CopyLogbookData, "copying Logbook data");
            CopyLogbook((object)sourceWorkbook, (object)outputWorkbook);
            step = SetStep(UpdaterPhaseIds.CopyKeywordsData, "copying Keywords data");
            CopyTableByMatchingColumns((object)sourceWorkbook, (object)outputWorkbook, "Keywords");
            step = SetStep(UpdaterPhaseIds.CopyRoutesData, "copying Routes data");
            CopyTableByMatchingColumns((object)sourceWorkbook, (object)outputWorkbook, "Routes");
            step = SetStep(UpdaterPhaseIds.CopyAirportBaseFlags, "copying airport base flags");
            CopyAirportBaseFlags((object)sourceWorkbook, (object)outputWorkbook);
            step = SetStep(UpdaterPhaseIds.CopyNamedPreferences, "copying named preferences");
            CopyNamedPreferences((object)sourceWorkbook, (object)outputWorkbook);
            step = SetStep(UpdaterPhaseIds.RestoreLogbookPresentation, "restoring Logbook presentation");
            RestoreLogbookPresentation((object)sourceWorkbook, (object)outputWorkbook);
            step = SetStep(UpdaterPhaseIds.RefreshAirportVisitStats, "refreshing airport visit stats");
            RefreshAirportVisitStats((object)outputWorkbook);

            var outputVersion = ReadName((object)outputWorkbook, "LogbookVersion");
            if (request.Manifest is not null &&
                !string.Equals(outputVersion, request.Manifest.Version, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Output workbook version {outputVersion} does not match verified release " +
                    $"{request.Manifest!.Version}.");
            }

            step = SetStep(UpdaterPhaseIds.CalculateOutputWorkbook, "calculating output workbook");
            excel.Calculation = XlCalculationAutomatic;
            foreach (dynamic worksheet in outputWorkbook.Worksheets)
            {
                worksheet.Calculate();
            }
            step = SetStep(UpdaterPhaseIds.RefreshPivotTables, "refreshing pivot tables");
            RefreshPivots((object)outputWorkbook);
            step = SetStep(UpdaterPhaseIds.UpdateHoursOverTimeChart, "updating Hours Over Time chart");
            UpdateHoursOverTimeChart((object)outputWorkbook);

            step = SetStep(UpdaterPhaseIds.ValidatePreservedData, "validating preserved data");
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
            ValidateLogbookStructure((object)sourceWorkbook, (object)outputWorkbook);

            step = SetStep(UpdaterPhaseIds.SaveOutputWorkbook, "saving output workbook");
            outputWorkbook.RemovePersonalInformation = false;
            ActivatePrimaryWorksheetForSave((object)outputWorkbook);
            outputWorkbook.Save();

            _progressSink?.Report(new UpdaterProgressEvent(
                UpdaterProgressEventTypes.UpdateCompleted,
                UpdaterPhaseIds.Completed,
                "migration completed",
                Percent: 100,
                DateTimeOffset.UtcNow));

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
            _progressSink?.Report(new UpdaterProgressEvent(
                UpdaterProgressEventTypes.PhaseFailed,
                phaseId,
                ex.Message,
                Percent: null,
                DateTimeOffset.UtcNow));

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

    private static void UnprotectWorkbookForMigration(object workbookObject)
    {
        dynamic workbook = workbookObject;

        // Release workbooks can be saved in a protected state.
        // Migration needs table resize/copy operations, so force an editable
        // state in the temporary output workbook.
        try
        {
            workbook.Unprotect("");
        }
        catch
        {
            // Continue: some workbook states may not require workbook-level unprotect.
        }

        foreach (dynamic worksheet in workbook.Worksheets)
        {
            try
            {
                worksheet.Unprotect("");
            }
            catch
            {
                // Continue: migration will surface a specific failure if protection remains.
            }
            finally
            {
                ReleaseComObject(worksheet);
            }
        }
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

    private static void RefreshAirportVisitStats(object workbookObject)
    {
        dynamic airports = GetTable(workbookObject, "Airports");
        dynamic logbook = GetTable(workbookObject, "Logbook");
        var airportRows = (int)airports.ListRows.Count;
        var logbookRows = (int)logbook.ListRows.Count;
        if (airportRows <= 0)
        {
            return;
        }

        var airportIcao = ReadColumnValues(airports, "ICAO", airportRows);
        var airportTwo = ReadColumnValues(airports, "Two", airportRows);
        var airportThree = ReadColumnValues(airports, "Three", airportRows);
        var aliasLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var stats = new Dictionary<string, AirportVisitStats>(StringComparer.OrdinalIgnoreCase);

        for (var row = 0; row < airportRows; row++)
        {
            var icao = StableValue(airportIcao[row]).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(icao))
            {
                continue;
            }

            stats[icao] = new AirportVisitStats();
            aliasLookup.TryAdd(icao, icao);

            var two = StableValue(airportTwo[row]).Trim().ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(two))
            {
                aliasLookup.TryAdd(two, icao);
            }

            var three = StableValue(airportThree[row]).Trim().ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(three))
            {
                aliasLookup.TryAdd(three, icao);
            }
        }

        var keywords = ReadAirportStatsKeywords(workbookObject);
        if (logbookRows > 0)
        {
            var years = ReadColumnValues(logbook, "Year", logbookRows);
            var months = ReadColumnValues(logbook, "Month", logbookRows);
            var days = ReadColumnValues(logbook, "Day", logbookRows);
            var details = ReadColumnValues(logbook, "Details", logbookRows);
            var ifrSim = ReadColumnValues(logbook, "IfrSim", logbookRows);
            var firstHourColumn = GetColumnIndex(logbook, "SeIcusDay");
            var lastOtherHourColumn = GetColumnIndex(logbook, "IfrIf");
            var hourColumns = new List<object?[]>();
            for (var column = firstHourColumn; column <= lastOtherHourColumn; column++)
            {
                var name = (string)logbook.ListColumns.Item(column).Name;
                hourColumns.Add(ReadColumnValues(logbook, name, logbookRows));
            }

            for (var row = 0; row < logbookRows; row++)
            {
                if (AirportStatsLogbookRowIsSimOnly(row, ifrSim, hourColumns))
                {
                    continue;
                }

                var detailsText = StableValue(details[row]).Trim();
                if (string.IsNullOrWhiteSpace(detailsText))
                {
                    continue;
                }

                var matchedIcaos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string token in TokeniseAirportDetails(detailsText))
                {
                    if (AirportStatsIgnoreToken(token, keywords))
                    {
                        continue;
                    }
                    string? matchedIcao;
                    if (aliasLookup.TryGetValue(token, out matchedIcao) &&
                        !string.IsNullOrWhiteSpace(matchedIcao))
                    {
                        matchedIcaos.Add(matchedIcao);
                    }
                }

                foreach (var icao in matchedIcaos)
                {
                    if (stats.TryGetValue(icao, out var stat))
                    {
                        stat.AddVisit(ToLogbookDate(years[row], months[row], days[row]));
                    }
                }
            }
        }

        var ranks = stats
            .Where(pair => pair.Value.Visits > 0)
            .OrderByDescending(pair => pair.Value.Visits)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select((pair, index) => new { pair.Key, Rank = index + 1 })
            .ToDictionary(pair => pair.Key, pair => pair.Rank, StringComparer.OrdinalIgnoreCase);

        var firstVisited = NewColumnArray(airportRows);
        var lastVisited = NewColumnArray(airportRows);
        var visits = NewColumnArray(airportRows);
        var rankValues = NewColumnArray(airportRows);

        for (var row = 0; row < airportRows; row++)
        {
            var icao = StableValue(airportIcao[row]).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(icao))
            {
                continue;
            }

            if (!stats.TryGetValue(icao, out AirportVisitStats? stat))
            {
                continue;
            }

            var visitStats = stat ?? throw new InvalidDataException($"Airport stats missing for {icao}.");
            if (visitStats.Visits <= 0)
            {
                continue;
            }

            firstVisited[row, 0] = visitStats.FirstVisited.HasValue ? visitStats.FirstVisited.Value : "";
            lastVisited[row, 0] = visitStats.LastVisited.HasValue ? visitStats.LastVisited.Value : "";
            visits[row, 0] = visitStats.Visits;
            rankValues[row, 0] = ranks[icao];
        }

        SetColumnValues(airports, "First Visited", firstVisited, airportRows);
        SetColumnValues(airports, "Last Visited", lastVisited, airportRows);
        SetColumnValues(airports, "Visits", visits, airportRows);
        SetColumnValues(airports, "Rank", rankValues, airportRows);
    }

    private sealed class AirportVisitStats
    {
        public int Visits { get; private set; }
        public double? FirstVisited { get; private set; }
        public double? LastVisited { get; private set; }

        public void AddVisit(double? flightDate)
        {
            Visits++;
            if (flightDate is null)
            {
                return;
            }

            FirstVisited = FirstVisited is null ? flightDate : Math.Min(FirstVisited.Value, flightDate.Value);
            LastVisited = LastVisited is null ? flightDate : Math.Max(LastVisited.Value, flightDate.Value);
        }
    }

    private static bool AirportStatsLogbookRowIsSimOnly(
        int row,
        object?[] ifrSim,
        IReadOnlyCollection<object?[]> hourColumns)
    {
        var simHours = ToDouble(ifrSim[row]);
        var otherHours = hourColumns.Sum(column => ToDouble(column[row]));
        return simHours > 0 && otherHours == 0;
    }

    private static IEnumerable<string> TokeniseAirportDetails(string details)
    {
        var normalised = details.Replace("|", "", StringComparison.Ordinal);
        foreach (var delimiter in new[] { '-', ' ', ',', '(', ')' })
        {
            normalised = normalised.Replace(delimiter, '|');
        }
        return normalised
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.ToUpperInvariant());
    }

    private static bool AirportStatsIgnoreToken(string token, IReadOnlyCollection<string> keywords)
    {
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "IPC", "OPC", "FR", "IR", "IFR", "VFR", "TEST", "CHECK", "CIRCLING", "SIM"
        };
        if (ignored.Contains(token))
        {
            return true;
        }
        return keywords.Any(keyword => keyword.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyCollection<string> ReadAirportStatsKeywords(object workbookObject)
    {
        try
        {
            dynamic keywords = GetTable(workbookObject, "Keywords");
            var rows = (int)keywords.ListRows.Count;
            var values = new List<string>();
            for (var column = 1; column <= (int)keywords.ListColumns.Count; column++)
            {
                var name = (string)keywords.ListColumns.Item(column).Name;
                foreach (var rawValue in ReadColumnValues(keywords, name, rows))
                {
                    var value = StableValue(rawValue).Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        values.Add(value);
                    }
                }
            }
            return values;
        }
        catch
        {
            return [];
        }
    }

    private static object[,] NewColumnArray(int rows)
    {
        var values = new object[rows, 1];
        for (var row = 0; row < rows; row++)
        {
            values[row, 0] = "";
        }
        return values;
    }

    private static void SetColumnValues(dynamic table, string name, object[,] values, int rows)
    {
        var index = GetColumnIndex(table, name);
        table.DataBodyRange.Columns.Item(index).Resize[rows, 1].Value2 = values;
    }

    private static double ToDouble(object? value)
    {
        return value switch
        {
            null => 0,
            double number => number,
            float number => number,
            int number => number,
            decimal number => (double)number,
            string text when double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var number) => number,
            _ => 0
        };
    }

    private static double? ToLogbookDate(object? yearValue, object? monthValue, object? dayValue)
    {
        var year = (int)ToDouble(yearValue);
        var day = ResolveLogbookDay(dayValue);
        var monthText = StableValue(monthValue).Trim();
        var month = ResolveLogbookMonth(monthValue, monthText);
        if (year <= 0 || day <= 0 || string.IsNullOrWhiteSpace(monthText))
        {
            return null;
        }

        if (month <= 0)
        {
            return null;
        }

        try
        {
            return new DateTime(year, month, day).ToOADate();
        }
        catch
        {
            return null;
        }
    }

    private static int ResolveLogbookMonth(object? monthValue, string monthText)
    {
        var monthNumber = ToDouble(monthValue);
        if (monthNumber >= 1 && monthNumber <= 12)
        {
            return (int)monthNumber;
        }
        if (monthNumber > 31)
        {
            try
            {
                return DateTime.FromOADate(monthNumber).Month;
            }
            catch
            {
                return 0;
            }
        }

        if (int.TryParse(monthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericMonth))
        {
            if (numericMonth >= 1 && numericMonth <= 12)
            {
                return numericMonth;
            }
            if (numericMonth > 31)
            {
                try
                {
                    return DateTime.FromOADate(numericMonth).Month;
                }
                catch
                {
                    return 0;
                }
            }
        }

        var format = CultureInfo.InvariantCulture.DateTimeFormat;
        for (var month = 1; month <= 12; month++)
        {
            if (string.Equals(monthText, format.AbbreviatedMonthNames[month - 1], StringComparison.OrdinalIgnoreCase) ||
                string.Equals(monthText, format.MonthNames[month - 1], StringComparison.OrdinalIgnoreCase))
            {
                return month;
            }
        }

        return 0;
    }

    private static int ResolveLogbookDay(object? dayValue)
    {
        var dayNumber = ToDouble(dayValue);
        if (dayNumber >= 1 && dayNumber <= 31)
        {
            return (int)dayNumber;
        }
        if (dayNumber > 31)
        {
            try
            {
                return DateTime.FromOADate(dayNumber).Day;
            }
            catch
            {
                return 0;
            }
        }
        return 0;
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

    private static void ActivatePrimaryWorksheetForSave(object workbookObject)
    {
        dynamic workbook = workbookObject;
        workbook.Activate();

        try
        {
            workbook.Worksheets.Item("New Entry").Activate();
        }
        catch
        {
            workbook.Worksheets.Item(1).Activate();
        }
    }

    private static void RestoreLogbookPresentation(
        object sourceWorkbookObject,
        object outputWorkbookObject)
    {
        dynamic sourceWorkbook = sourceWorkbookObject;
        dynamic outputWorkbook = outputWorkbookObject;
        dynamic source = GetTable(sourceWorkbookObject, "Logbook");
        dynamic destination = GetTable(outputWorkbookObject, "Logbook");
        dynamic worksheet = destination.Parent;

        var masterHeaderFont = (string)destination.HeaderRowRange.Cells.Item(1, 1).Font.Name;
        var masterDataFont = (string)destination.DataBodyRange.Cells.Item(1, 1).Font.Name;
        var masterTotalsFont = (string)destination.TotalsRowRange.Cells.Item(1, 1).Font.Name;

        destination.TableStyle = source.TableStyle.Name;
        destination.ShowTableStyleRowStripes = source.ShowTableStyleRowStripes;
        destination.ShowTableStyleColumnStripes = source.ShowTableStyleColumnStripes;
        destination.ShowTableStyleFirstColumn = source.ShowTableStyleFirstColumn;
        destination.ShowTableStyleLastColumn = source.ShowTableStyleLastColumn;

        var lastUserFormatColumn = GetColumnIndex(destination, "CumAzi");
        for (var destinationIndex = 1; destinationIndex <= lastUserFormatColumn; destinationIndex++)
        {
            var name = (string)destination.ListColumns.Item(destinationIndex).Name;
            if (!HasColumn((object)source, name))
            {
                continue;
            }

            var sourceIndex = GetColumnIndex(source, name);
            source.Range.Columns.Item(sourceIndex).Copy();
            destination.Range.Columns.Item(destinationIndex).PasteSpecial(XlPasteFormats);
        }
        outputWorkbook.Application.CutCopyMode = false;

        destination.HeaderRowRange.Font.Name = masterHeaderFont;
        destination.DataBodyRange.Font.Name = masterDataFont;
        destination.TotalsRowRange.Font.Name = masterTotalsFont;
        destination.ListColumns.Item(GetColumnIndex(destination, "CumAzi"))
            .DataBodyRange.NumberFormat = "General";
        destination.TotalsRowRange.Cells.Item(
            1,
            GetColumnIndex(destination, "CumAzi")).NumberFormat = "General";

        CopyTotalsAreaFormatting(source, destination);
        EnsureTotalsArea(outputWorkbookObject, destination);
        RestoreHeaderPalette(sourceWorkbook, outputWorkbook, destination);

        var lastDataRow =
            (int)destination.DataBodyRange.Row + (int)destination.DataBodyRange.Rows.Count - 1;
        worksheet.Rows.Hidden = false;
        if (lastDataRow + 4 <= (int)worksheet.Rows.Count)
        {
            worksheet.Rows[$"{lastDataRow + 4}:{worksheet.Rows.Count}"].Hidden = true;
        }
    }

    private static void CopyTotalsAreaFormatting(dynamic source, dynamic destination)
    {
        dynamic sourceWorksheet = source.Parent;
        dynamic destinationWorksheet = destination.Parent;
        var sourceTotalsRow = (int)source.TotalsRowRange.Row;
        var destinationTotalsRow = (int)destination.TotalsRowRange.Row;
        var regColumn = (int)source.ListColumns.Item(GetColumnIndex(source, "Reg")).Range.Column;
        var otherColumn = (int)source.ListColumns
            .Item(GetColumnIndex(source, "Other Pilot or Crew")).Range.Column;

        dynamic sourceRange = sourceWorksheet.Range[
            sourceWorksheet.Cells.Item(sourceTotalsRow, regColumn),
            sourceWorksheet.Cells.Item(sourceTotalsRow + 1, otherColumn)];
        dynamic destinationRange = destinationWorksheet.Range[
            destinationWorksheet.Cells.Item(destinationTotalsRow, regColumn),
            destinationWorksheet.Cells.Item(destinationTotalsRow + 1, otherColumn)];
        sourceRange.Copy();
        destinationRange.PasteSpecial(XlPasteFormats);
        destinationWorksheet.Application.CutCopyMode = false;
    }

    private static void EnsureTotalsArea(object workbookObject, dynamic table)
    {
        dynamic workbook = workbookObject;
        dynamic worksheet = table.Parent;
        var totalsRow = (int)table.TotalsRowRange.Row;
        var picColumn = (int)table.ListColumns.Item(GetColumnIndex(table, "PIC")).Range.Column;
        var otherColumn = (int)table.ListColumns
            .Item(GetColumnIndex(table, "Other Pilot or Crew")).Range.Column;
        var regColumn = (int)table.ListColumns.Item(GetColumnIndex(table, "Reg")).Range.Column;
        var firstCustomIndex = GetColumnIndex(table, "Details") + 1;
        var sumStartColumn = (int)table.ListColumns.Item(firstCustomIndex).Range.Column;
        var sumEndColumn = (int)table.ListColumns.Item(GetColumnIndex(table, "TotalApps")).Range.Column;

        worksheet.Cells.Item(totalsRow + 1, picColumn).Value2 = "Total Aeronautical Experience";
        worksheet.Cells.Item(totalsRow + 1, otherColumn).Formula =
            "=Logbook[[#Totals],[Other Pilot or Crew]]+Logbook[[#Totals],[IfrSim]]";

        SetWorkbookName(
            workbook,
            "LogbookTotals",
            worksheet.Range[
                worksheet.Cells.Item(totalsRow, regColumn),
                worksheet.Cells.Item(totalsRow + 1, otherColumn)]);
        SetWorkbookName(
            workbook,
            "LogbookSumTotals",
            worksheet.Range[
                worksheet.Cells.Item(totalsRow, sumStartColumn),
                worksheet.Cells.Item(totalsRow, sumEndColumn)]);
    }

    private static void RestoreHeaderPalette(
        dynamic sourceWorkbook,
        dynamic outputWorkbook,
        dynamic destination)
    {
        dynamic sourceHeaders = sourceWorkbook.Names.Item("LogbookHeaders").RefersToRange;
        dynamic outputHeaders = outputWorkbook.Names.Item("LogbookHeaders").RefersToRange;
        outputHeaders.Interior.Pattern = sourceHeaders.Interior.Pattern;
        outputHeaders.Interior.Color = sourceHeaders.Interior.Color;
        outputHeaders.Font.Color = sourceHeaders.Font.Color;

        var start = GetColumnIndex(destination, "SeIcusDay");
        var end = GetColumnIndex(destination, "Circling");
        dynamic hourHeaders = destination.HeaderRowRange.Cells.Item(1, start).Resize[
            1,
            end - start + 1];
        foreach (dynamic cell in hourHeaders.Cells)
        {
            cell.Font.Color = cell.DisplayFormat.Interior.Color;
        }
    }

    private static void RefreshPivots(object workbookObject)
    {
        try
        {
            dynamic workbook = workbookObject;
            foreach (dynamic worksheet in workbook.Worksheets)
            {
                try
                {
                    dynamic pivots = worksheet.PivotTables();
                    for (var index = 1; index <= (int)pivots.Count; index++)
                    {
                        try
                        {
                            dynamic pivot = pivots.Item(index);
                            pivot.RefreshTable();
                        }
                        catch
                        {
                            // Some Excel builds intermittently fail COM pivot refresh calls.
                            // Continue so migration can complete and users can refresh manually later.
                        }
                    }
                }
                catch
                {
                    // Accessing pivot collections can also fail in unstable COM sessions.
                }
            }

            try
            {
                dynamic hoursByYear = workbook.Worksheets.Item("ChartData")
                    .PivotTables("HoursByYear");
                hoursByYear.PivotFields("Quarters (Date)").Orientation = 0;
                hoursByYear.RefreshTable();
            }
            catch
            {
                // Older workbooks may not have this pivot/grouping, and some Excel builds can fail COM refresh calls.
            }
        }
        catch
        {
            // Keep migration non-blocking if Excel COM becomes unstable during pivot refresh.
        }
    }

    private static void UpdateHoursOverTimeChart(object workbookObject)
    {
        dynamic workbook = workbookObject;
        dynamic chartData = workbook.Worksheets.Item("ChartData");
        dynamic charts = workbook.Worksheets.Item("Charts");
        dynamic runningTotal = workbook.Names.Item("RunningTotalHours").RefersToRange;
        var firstColumn = (int)runningTotal.Columns.Item(1).Column;
        var lastRow = (int)chartData.Cells.Item(chartData.Rows.Count, firstColumn).End(XlUp).Row;
        if (lastRow < 2)
        {
            return;
        }

        dynamic chartRange = chartData.Range[
            chartData.Cells.Item(2, firstColumn),
            chartData.Cells.Item(lastRow, firstColumn + 1)];
        dynamic series = charts.ChartObjects("HoursOverTime").Chart.SeriesCollection(1);
        series.XValues = chartRange.Columns.Item(1);
        series.Values = chartRange.Columns.Item(2);
    }

    private static void SetWorkbookName(dynamic workbook, string name, dynamic range)
    {
        var refersTo = $"='{range.Worksheet.Name}'!{range.Address}";
        try
        {
            workbook.Names.Item(name).RefersTo = refersTo;
        }
        catch
        {
            workbook.Names.Add(name, refersTo);
        }
    }

    private static void ValidateLogbookStructure(
        object sourceWorkbookObject,
        object outputWorkbookObject)
    {
        dynamic source = GetTable(sourceWorkbookObject, "Logbook");
        dynamic outputWorkbook = outputWorkbookObject;
        dynamic destination = GetTable(outputWorkbookObject, "Logbook");

        if (!string.Equals(
                (string)source.TableStyle.Name,
                (string)destination.TableStyle.Name,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Logbook table style was not preserved.");
        }

        for (var row = 1; row <= (int)destination.Range.Rows.Count; row++)
        {
            if ((bool)destination.Range.Rows.Item(row).Hidden)
            {
                throw new InvalidDataException("Logbook table contains hidden rows.");
            }
        }

        dynamic totals = outputWorkbook.Names.Item("LogbookTotals").RefersToRange;
        if ((int)totals.Rows.Count != 2 ||
            (int)totals.Row != (int)destination.TotalsRowRange.Row)
        {
            throw new InvalidDataException("LogbookTotals is not anchored to the live two-row totals area.");
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
