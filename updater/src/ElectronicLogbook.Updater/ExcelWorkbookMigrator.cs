using System.Globalization;
using System.Diagnostics;
using Microsoft.CSharp.RuntimeBinder;
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
    private const int BaseAirportsTopCount = 10;

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
        AirportVisitStatsDiagnostics? airportVisitStats = null;
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
            step = SetStep(UpdaterPhaseIds.CopyNamedPreferences, "copying named preferences");
            CopyNamedPreferences((object)sourceWorkbook, (object)outputWorkbook);
            step = SetStep(UpdaterPhaseIds.RestoreLogbookPresentation, "restoring Logbook presentation");
            RestoreLogbookPresentation((object)sourceWorkbook, (object)outputWorkbook);
            step = SetStep(UpdaterPhaseIds.RefreshAirportVisitStats, "refreshing airport visit stats");
            airportVisitStats = RefreshAirportVisitStats((object)outputWorkbook);
            if (airportVisitStats.LogbookRowsWithRecognisedAirports > 0 &&
                airportVisitStats.WrittenVisitedAirportRows <= 0)
            {
                throw new InvalidDataException(
                    "Airport visit stats refresh found recognised airports in the Logbook, " +
                    "but did not write any Airports[Visits] values.");
            }
            step = SetStep(UpdaterPhaseIds.CopyBaseAirportSelections, "copying base airport selections");
            ApplyBaseAirportSelections((object)sourceWorkbook, (object)outputWorkbook);

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
            var savedNonBlankVisitRows = CountNonBlankAirportVisitRows((object)outputWorkbook);
            airportVisitStats = airportVisitStats with
            {
                SavedNonBlankVisitRows = savedNonBlankVisitRows
            };
            if (airportVisitStats.WrittenVisitedAirportRows > 0 && savedNonBlankVisitRows <= 0)
            {
                throw new InvalidDataException(
                    "Airport visit stats were written before save, but Airports[Visits] is blank after save.");
            }

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
                airportVisitStats ?? EmptyAirportVisitStatsDiagnostics(),
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
        if (workbook is null)
        {
            throw new InvalidDataException("Output workbook was not opened.");
        }

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

        if (workbook.Worksheets is null)
        {
            throw new InvalidDataException("Output workbook does not expose a Worksheets collection.");
        }

        var worksheetCount = (int)workbook.Worksheets.Count;
        for (var index = 1; index <= worksheetCount; index++)
        {
            dynamic worksheet = workbook.Worksheets.Item(index);
            if (worksheet is null)
            {
                throw new InvalidDataException($"Output workbook worksheet {index} could not be opened.");
            }
            try
            {
                worksheet.Unprotect("");
            }
            catch
            {
                // Continue: migration will surface a specific failure if protection remains.
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

        var sourceDataStart = GetColumnIndex(source, "Year");
        var sourceDataEnd = GetColumnIndex(source, "Circling");
        var destinationDataStart = GetColumnIndex(destination, "Year");
        var destinationDataEnd = GetColumnIndex(destination, "Circling");
        var airportLookup = BuildAirportAliasLookup(outputWorkbookObject);

        var sourceCustomStart = GetLogbookCustomStartColumn(source);
        var destinationCustomStart = GetLogbookCustomStartColumn(destination);
        var sourceCustomCount = GetColumnIndex(source, "SeIcusDay") - sourceCustomStart;
        var destinationCustomCount = GetColumnIndex(destination, "SeIcusDay") - destinationCustomStart;
        if (sourceCustomCount != destinationCustomCount)
        {
            throw new InvalidDataException(
                "Source and master workbooks have different custom-column counts.");
        }

        for (var offset = 1; offset <= sourceCustomCount; offset++)
        {
            destination.ListColumns.Item(destinationCustomStart + offset - 1).Name =
                source.ListColumns.Item(sourceCustomStart + offset - 1).Name;
        }

        ResizeTable(destination, sourceRows);
        FillFormulaColumns(destination, destinationDataStart, destinationDataEnd);

        for (var sourceIndex = sourceDataStart; sourceIndex <= sourceDataEnd; sourceIndex++)
        {
            var name = (string)source.ListColumns.Item(sourceIndex).Name;
            var destinationName = DestinationLogbookColumnName((object)destination, name);
            if (destinationName is not null)
            {
                CopyColumn(source, destination, name, destinationName, sourceRows);
            }
        }

        SplitLegacyDetailsIntoNewEntryColumns(source, destination, sourceRows, airportLookup);
        ConvertLegacyCurrencyColumns(source, destination, sourceRows);
    }

    private static void CopyTableByMatchingColumns(
        object sourceWorkbook,
        object outputWorkbook,
        string tableName)
    {
        dynamic? source = GetTableOrNull(sourceWorkbook, tableName);
        if (source is null)
        {
            return;
        }

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

    private static int GetLogbookCustomStartColumn(dynamic table)
    {
        var firstHoursColumn = GetColumnIndex(table, "SeIcusDay");
        if (HasColumn((object)table, "OPC") &&
            GetColumnIndex(table, "OPC") < firstHoursColumn)
        {
            return GetColumnIndex(table, "OPC") + 1;
        }
        if (HasColumn((object)table, "Details"))
        {
            return GetColumnIndex(table, "Details") + 1;
        }
        if (HasColumn((object)table, "Remarks"))
        {
            return GetColumnIndex(table, "Remarks") + 1;
        }
        throw new InvalidDataException($"Table {table.Name} has no recognised custom-column anchor.");
    }

    private static void SplitLegacyDetailsIntoNewEntryColumns(
        dynamic source,
        dynamic destination,
        int rows,
        IReadOnlyDictionary<string, string> airportLookup)
    {
        if (rows <= 0 ||
            !HasColumn((object)destination, "From") ||
            !HasColumn((object)destination, "To") ||
            !HasColumn((object)destination, "Via") ||
            !HasColumn((object)destination, "Remarks"))
        {
            return;
        }

        if (HasColumn((object)source, "Remarks"))
        {
            CopyColumnIfPresent(source, destination, "From", rows);
            CopyColumnIfPresent(source, destination, "To", rows);
            CopyColumnIfPresent(source, destination, "Via", rows);
            CopyColumnIfPresent(source, destination, "Remarks", rows);
            return;
        }

        if (!HasColumn((object)source, "Details"))
        {
            return;
        }

        var details = ReadColumnValues(source, "Details", rows);
        var fromValues = NewColumnArray(rows);
        var toValues = NewColumnArray(rows);
        var routeValues = NewColumnArray(rows);
        var remarksValues = NewColumnArray(rows);
        for (var row = 0; row < rows; row++)
        {
            var split = SplitLegacyDetailsText(StableValue(details[row]), airportLookup);
            fromValues[row, 0] = split.From;
            toValues[row, 0] = split.To;
            routeValues[row, 0] = split.Route;
            remarksValues[row, 0] = split.Remarks;
        }

        SetColumnValues(destination, "From", fromValues, rows);
        SetColumnValues(destination, "To", toValues, rows);
        SetColumnValues(destination, "Via", routeValues, rows);
        SetColumnValues(destination, "Remarks", remarksValues, rows);
    }

    private static void ConvertLegacyCurrencyColumns(dynamic source, dynamic destination, int rows)
    {
        if (rows <= 0 ||
            !HasColumn((object)destination, "FR") ||
            !HasColumn((object)destination, "IPC") ||
            !HasColumn((object)destination, "OPC"))
        {
            return;
        }

        var frValues = NewColumnArray(rows);
        var ipcValues = NewColumnArray(rows);
        var opcValues = NewColumnArray(rows);
        var exclusionSource = ReadColumnValuesIfPresent(source, "CurrencyExclusions", rows);
        var frSource = ReadColumnValuesIfPresent(source, "FR", rows);
        var flightReviewSource = ReadColumnValuesIfPresent(source, "FlightReview", rows);
        var ipcSource = ReadColumnValuesIfPresent(source, "IPC", rows);
        var opcSource = ReadColumnValuesIfPresent(source, "OPC", rows);

        for (var row = 0; row < rows; row++)
        {
            var excluded = ToBoolean(ReadOptionalColumnValue(exclusionSource, row));
            if (!excluded)
            {
                var ipcChecked = ToCurrencyCheckValue(ReadOptionalColumnValue(ipcSource, row));
                frValues[row, 0] =
                    ToCurrencyCheckValue(ReadOptionalColumnValue(frSource, row)) ||
                    ToCurrencyCheckValue(ReadOptionalColumnValue(flightReviewSource, row)) ||
                    ipcChecked;
                ipcValues[row, 0] = ipcChecked;
                opcValues[row, 0] = ToCurrencyCheckValue(ReadOptionalColumnValue(opcSource, row));
            }
        }

        SetColumnValues(destination, "FR", frValues, rows);
        SetColumnValues(destination, "IPC", ipcValues, rows);
        SetColumnValues(destination, "OPC", opcValues, rows);
    }

    private static void CopyColumnIfPresent(dynamic source, dynamic destination, string name, int rows)
    {
        if (HasColumn((object)source, name) && HasColumn((object)destination, name))
        {
            CopyColumn(source, destination, name, rows);
        }
    }

    private static object?[]? ReadColumnValuesIfPresent(dynamic table, string name, int rows)
    {
        if (!HasColumn((object)table, name) || rows <= 0)
        {
            return null;
        }

        return ReadColumnValues(table, name, rows);
    }

    private static object? ReadOptionalColumnValue(object?[]? values, int zeroBasedRow)
    {
        if (values is null || zeroBasedRow < 0 || zeroBasedRow >= values.Length)
        {
            return null;
        }

        return values[zeroBasedRow];
    }

    private static Dictionary<string, string> BuildAirportAliasLookup(object workbookObject)
    {
        dynamic airports = GetTable(workbookObject, "Airports");
        var rows = (int)airports.ListRows.Count;
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (rows <= 0)
        {
            return lookup;
        }

        var airportIcao = ReadColumnValues(airports, "ICAO", rows);
        var airportTwo = ReadColumnValues(airports, "Two", rows);
        var airportThree = ReadColumnValues(airports, "Three", rows);
        for (var row = 0; row < rows; row++)
        {
            var icao = StableValue(airportIcao[row]).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(icao))
            {
                continue;
            }

            lookup.TryAdd(icao, icao);
            AddAirportAlias(lookup, StableValue(airportTwo[row]), icao);
            AddAirportAlias(lookup, StableValue(airportThree[row]), icao);
        }

        return lookup;
    }

    private static void AddAirportAlias(
        IDictionary<string, string> lookup,
        string alias,
        string icao)
    {
        alias = alias.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(alias))
        {
            lookup.TryAdd(alias, icao);
        }
    }

    private static LegacyDetailsSplit SplitLegacyDetailsText(
        string details,
        IReadOnlyDictionary<string, string> airportLookup)
    {
        var matchedIcaos = new List<string>();
        var remarkTokens = new List<string>();
        foreach (var rawToken in TokeniseAirportDetails(details))
        {
            if (airportLookup.TryGetValue(rawToken.ToUpperInvariant(), out var icao))
            {
                matchedIcaos.Add(icao);
            }
            else
            {
                remarkTokens.Add(rawToken);
            }
        }

        var from = matchedIcaos.Count >= 1 ? matchedIcaos[0] : "";
        var to = matchedIcaos.Count >= 2 ? matchedIcaos[^1] : "";
        var route = matchedIcaos.Count > 2
            ? string.Join("-", matchedIcaos.Skip(1).Take(matchedIcaos.Count - 2))
            : "";
        var remarks = string.Join(" ", remarkTokens);
        return new LegacyDetailsSplit(from, to, route, remarks);
    }

    private sealed record LegacyDetailsSplit(string From, string To, string Route, string Remarks);

    private static void ApplyBaseAirportSelections(object sourceWorkbook, object outputWorkbook)
    {
        dynamic? destination = GetTableOrNull(outputWorkbook, "BaseAirportsTop10");
        if (destination is null)
        {
            return;
        }

        var selections = ReadSourceBaseAirportSelections(sourceWorkbook);
        var rows = (int)destination.ListRows.Count;
        if (rows > 0 &&
            HasColumn((object)destination, "ICAO") &&
            HasColumn((object)destination, "Base"))
        {
            var icaos = ReadColumnValues(destination, "ICAO", rows);
            for (var row = 0; row < rows; row++)
            {
                var icao = StableValue(icaos[row]).Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(icao))
                {
                    continue;
                }
                if (selections.TryGetValue(icao, out bool isBase))
                {
                    destination.DataBodyRange.Cells.Item(
                        row + 1,
                        GetColumnIndex(destination, "Base")).Value2 = isBase;
                }
            }
        }
        ApplyNativeCheckboxesIfAvailable(destination, "Base");
    }

    private static Dictionary<string, bool> ReadSourceBaseAirportSelections(object workbookObject)
    {
        var selections = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        dynamic? table = GetTableOrNull(workbookObject, "BaseAirportsTop10");
        if (table is not null &&
            HasColumn((object)table, "ICAO") &&
            HasColumn((object)table, "Base"))
        {
            var rows = (int)table.ListRows.Count;
            var icaos = ReadColumnValues(table, "ICAO", rows);
            var bases = ReadColumnValues(table, "Base", rows);
            for (var row = 0; row < rows; row++)
            {
                var icao = StableValue(icaos[row]).Trim().ToUpperInvariant();
                if (!string.IsNullOrWhiteSpace(icao))
                {
                    selections[icao] = ToBoolean(bases[row]);
                }
            }
        }

        foreach (var legacySelection in ReadLegacyAirportBaseSelections(workbookObject))
        {
            selections.TryAdd(legacySelection.Key, true);
        }

        return selections;
    }

    private static AirportVisitStatsDiagnostics RefreshAirportVisitStats(object workbookObject)
    {
        dynamic airports = GetTable(workbookObject, "Airports");
        dynamic logbook = GetTable(workbookObject, "Logbook");
        var airportRows = (int)airports.ListRows.Count;
        var logbookRows = (int)logbook.ListRows.Count;
        if (airportRows <= 0)
        {
            return EmptyAirportVisitStatsDiagnostics() with
            {
                AirportRows = airportRows,
                LogbookRows = logbookRows
            };
        }

        var airportIcao = ReadColumnValues(airports, "ICAO", airportRows);
        var airportTwo = ReadColumnValues(airports, "Two", airportRows);
        var airportThree = ReadColumnValues(airports, "Three", airportRows);
        var aliasLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var stats = new Dictionary<string, AirportVisitStats>(StringComparer.OrdinalIgnoreCase);
        var logbookRowsWithDetails = 0;
        var simOnlyRowsSkipped = 0;
        var tokensScanned = 0;
        var tokensIgnored = 0;
        var tokensMatched = 0;
        var logbookRowsWithRecognisedAirports = 0;
        var writtenVisitedAirportRows = 0;

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
            var routeSourceValues = ReadLogbookRouteSourceValues(logbook, logbookRows);
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
                    simOnlyRowsSkipped++;
                    continue;
                }

                var detailsText = routeSourceValues[row].Trim();
                if (string.IsNullOrWhiteSpace(detailsText))
                {
                    continue;
                }
                logbookRowsWithDetails++;

                var matchedIcaos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string token in TokeniseAirportDetails(detailsText))
                {
                    tokensScanned++;
                    if (AirportStatsIgnoreToken(token, keywords))
                    {
                        tokensIgnored++;
                        continue;
                    }
                    string? matchedIcao;
                    if (aliasLookup.TryGetValue(token, out matchedIcao) &&
                        !string.IsNullOrWhiteSpace(matchedIcao))
                    {
                        tokensMatched++;
                        matchedIcaos.Add(matchedIcao);
                    }
                }

                if (matchedIcaos.Count > 0)
                {
                    logbookRowsWithRecognisedAirports++;
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
            writtenVisitedAirportRows++;
        }

        SetColumnValues(airports, "First Visited", firstVisited, airportRows);
        SetColumnValues(airports, "Last Visited", lastVisited, airportRows);
        SetColumnValues(airports, "Visits", visits, airportRows);
        SetColumnValues(airports, "Rank", rankValues, airportRows);

        RefreshBaseAirportSelector(workbookObject, airports, logbook, aliasLookup);

        var topVisitedAirports = stats
            .Where(pair => pair.Value.Visits > 0)
            .OrderByDescending(pair => pair.Value.Visits)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Visits, StringComparer.OrdinalIgnoreCase);

        return new AirportVisitStatsDiagnostics(
            airportRows,
            logbookRows,
            aliasLookup.Count,
            keywords.Count,
            logbookRowsWithDetails,
            simOnlyRowsSkipped,
            tokensScanned,
            tokensIgnored,
            tokensMatched,
            logbookRowsWithRecognisedAirports,
            ranks.Count,
            writtenVisitedAirportRows,
            CountNonBlankAirportVisitRows(workbookObject),
            topVisitedAirports);
    }

    private static void RefreshBaseAirportSelector(
        object workbookObject,
        dynamic airports,
        dynamic logbook,
        IReadOnlyDictionary<string, string> aliasLookup)
    {
        dynamic table = EnsureBaseAirportsTable(workbookObject);
        Dictionary<string, bool> savedSelections = ReadBaseAirportSelections(
            workbookObject,
            (object)airports,
            (object)table);
        Dictionary<string, int> endpointCounts = BuildEndpointAirportCounts((object)logbook, aliasLookup);
        string[] topIcaos = endpointCounts
            .Where(pair => pair.Value > 0)
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Take(BaseAirportsTopCount)
            .Select(pair => pair.Key)
            .ToArray();

        ResizeTable(table, BaseAirportsTopCount);
        table.DataBodyRange.ClearContents();
        for (var row = 1; row <= BaseAirportsTopCount; row++)
        {
            var icao = row <= topIcaos.Length ? topIcaos[row - 1] : "";
            table.DataBodyRange.Cells.Item(row, GetColumnIndex(table, "ICAO")).Value2 = icao;
            table.DataBodyRange.Cells.Item(row, GetColumnIndex(table, "Airport")).Formula =
                "=IFERROR(INDEX(Airports[Airport],MATCH([@ICAO],Airports[ICAO],0)),\"\")";

            dynamic baseCell = table.DataBodyRange.Cells.Item(row, GetColumnIndex(table, "Base"));
            if (string.IsNullOrWhiteSpace(icao))
            {
                baseCell.ClearContents();
            }
            else if (savedSelections.TryGetValue(icao, out bool savedValue))
            {
                baseCell.Value2 = savedValue;
            }
            else
            {
                baseCell.Value2 = row == 1;
            }
        }

        ApplyNativeCheckboxesIfAvailable(table, "Base");
        table.Parent.Columns.Item(table.ListColumns.Item(GetColumnIndex(table, "ICAO")).Range.Column)
            .Hidden = true;
    }

    private static dynamic EnsureBaseAirportsTable(object workbookObject)
    {
        dynamic? table = GetTableOrNull(workbookObject, "BaseAirportsTop10");
        if (table is not null)
        {
            EnsureBaseAirportsTableColumns(table);
            return table;
        }

        dynamic workbook = workbookObject;
        dynamic worksheet = workbook.Worksheets.Item("Stats");
        dynamic? statsTable = GetTableOrNull(workbookObject, "Stats");
        var startRow = 2;
        var startColumn = 5;
        if (statsTable is not null)
        {
            startRow = (int)statsTable.Range.Row;
            startColumn = (int)statsTable.Range.Column + (int)statsTable.Range.Columns.Count + 2;
        }

        dynamic startCell = worksheet.Cells.Item(startRow, startColumn);
        dynamic endCell = worksheet.Cells.Item(startRow + 10, startColumn + 2);
        dynamic range = worksheet.Range[startCell, endCell];
        range.Clear();
        range.Rows.Item(1).Value2 = new object[,] { { "Airport", "Base", "ICAO" } };
        table = worksheet.ListObjects.Add(1, range, Type.Missing, 1);
        table.Name = "BaseAirportsTop10";
        table.TableStyle = "TableStyleMedium2";
        worksheet.Columns.Item(table.ListColumns.Item(GetColumnIndex(table, "ICAO")).Range.Column)
            .Hidden = true;
        return table;
    }

    private static void EnsureBaseAirportsTableColumns(dynamic table)
    {
        if (!HasColumn((object)table, "Airport"))
        {
            table.ListColumns.Add().Name = "Airport";
        }
        if (!HasColumn((object)table, "Base"))
        {
            table.ListColumns.Add().Name = "Base";
        }
        if (!HasColumn((object)table, "ICAO"))
        {
            table.ListColumns.Add().Name = "ICAO";
        }
    }

    private static Dictionary<string, bool> ReadBaseAirportSelections(
        object workbookObject,
        object airportsObject,
        object tableObject)
    {
        dynamic airports = airportsObject;
        dynamic table = tableObject;
        var selections = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var rows = (int)table.ListRows.Count;
        if (rows > 0)
        {
            object?[] icaos = HasColumn((object)table, "ICAO")
                ? ReadColumnValues(table, "ICAO", rows)
                : Array.Empty<object?>();
            object?[] names = HasColumn((object)table, "Airport")
                ? ReadColumnValues(table, "Airport", rows)
                : Array.Empty<object?>();
            object?[] bases = HasColumn((object)table, "Base")
                ? ReadColumnValues(table, "Base", rows)
                : Array.Empty<object?>();
            for (var row = 0; row < rows; row++)
            {
                var icao = row < icaos.Length ? StableValue(icaos[row]).Trim() : "";
                if (string.IsNullOrWhiteSpace(icao) && row < names.Length)
                {
                    icao = AirportIcaoForName(airports, StableValue(names[row]));
                }
                if (!string.IsNullOrWhiteSpace(icao) && row < bases.Length)
                {
                    selections[icao] = ToBoolean(bases[row]);
                }
            }
        }

        foreach (var legacySelection in ReadLegacyAirportBaseSelections(workbookObject))
        {
            selections.TryAdd(legacySelection.Key, true);
        }

        return selections;
    }

    private static Dictionary<string, int> BuildEndpointAirportCounts(
        object logbookObject,
        IReadOnlyDictionary<string, string> aliasLookup)
    {
        dynamic logbook = logbookObject;
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var rows = (int)logbook.ListRows.Count;
        if (rows <= 0 ||
            !HasColumn((object)logbook, "From") ||
            !HasColumn((object)logbook, "To"))
        {
            return counts;
        }

        var fromValues = ReadColumnValues(logbook, "From", rows);
        var toValues = ReadColumnValues(logbook, "To", rows);
        var ifrSim = ReadColumnValues(logbook, "IfrSim", rows);
        var firstHourColumn = GetColumnIndex(logbook, "SeIcusDay");
        var lastOtherHourColumn = GetColumnIndex(logbook, "IfrIf");
        var hourColumns = new List<object?[]>();
        for (var column = firstHourColumn; column <= lastOtherHourColumn; column++)
        {
            var name = (string)logbook.ListColumns.Item(column).Name;
            hourColumns.Add(ReadColumnValues(logbook, name, rows));
        }

        for (var row = 0; row < rows; row++)
        {
            if (AirportStatsLogbookRowIsSimOnly(row, ifrSim, hourColumns))
            {
                continue;
            }

            var matchedIcaos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddEndpointAirportMatch(matchedIcaos, aliasLookup, StableValue(fromValues[row]));
            AddEndpointAirportMatch(matchedIcaos, aliasLookup, StableValue(toValues[row]));

            foreach (var icao in matchedIcaos)
            {
                counts[icao] = counts.TryGetValue(icao, out var current) ? current + 1 : 1;
            }
        }

        return counts;
    }

    private static void AddEndpointAirportMatch(
        ISet<string> matchedIcaos,
        IReadOnlyDictionary<string, string> aliasLookup,
        string rawValue)
    {
        var token = rawValue.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }
        if (aliasLookup.TryGetValue(token, out var icao) && !string.IsNullOrWhiteSpace(icao))
        {
            matchedIcaos.Add(icao);
        }
    }

    private static Dictionary<string, string> ReadLegacyAirportBaseSelections(object workbookObject)
    {
        dynamic? airports = GetTableOrNull(workbookObject, "Airports");
        var selections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (airports is null ||
            !HasColumn((object)airports, "Base") ||
            !HasColumn((object)airports, "ICAO"))
        {
            return selections;
        }

        var rows = (int)airports.ListRows.Count;
        var icaos = ReadColumnValues(airports, "ICAO", rows);
        object?[] names = HasColumn((object)airports, "Airport")
            ? ReadColumnValues(airports, "Airport", rows)
            : Array.Empty<object?>();
        var bases = ReadColumnValues(airports, "Base", rows);
        for (var row = 0; row < rows; row++)
        {
            if (!ToBoolean(bases[row]))
            {
                continue;
            }

            var icao = StableValue(icaos[row]).Trim().ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(icao))
            {
                selections[icao] = row < names.Length ? StableValue(names[row]) : "";
            }
        }

        return selections;
    }

    private static string AirportIcaoForName(dynamic airports, string airportName)
    {
        if (string.IsNullOrWhiteSpace(airportName))
        {
            return "";
        }

        var rows = (int)airports.ListRows.Count;
        var icaos = ReadColumnValues(airports, "ICAO", rows);
        var names = ReadColumnValues(airports, "Airport", rows);
        for (var row = 0; row < rows; row++)
        {
            if (string.Equals(
                    StableValue(names[row]).Trim(),
                    airportName.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return StableValue(icaos[row]).Trim().ToUpperInvariant();
            }
        }

        return "";
    }

    private static AirportVisitStatsDiagnostics EmptyAirportVisitStatsDiagnostics()
    {
        return new AirportVisitStatsDiagnostics(
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
    }

    private static int CountNonBlankAirportVisitRows(object workbookObject)
    {
        dynamic airports = GetTable(workbookObject, "Airports");
        var airportRows = (int)airports.ListRows.Count;
        if (airportRows <= 0)
        {
            return 0;
        }

        object?[] visits = ReadColumnValues(airports, "Visits", airportRows);
        return visits.Count(value => !IsBlankValue(value));
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
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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

    private static string[] ReadLogbookRouteSourceValues(dynamic logbook, int rows)
    {
        var result = new string[rows];
        object?[]? fromValues = HasColumn((object)logbook, "From")
            ? ReadColumnValues(logbook, "From", rows)
            : null;
        object?[]? routeValues = HasColumn((object)logbook, "Via")
            ? ReadColumnValues(logbook, "Via", rows)
            : null;
        object?[]? toValues = HasColumn((object)logbook, "To")
            ? ReadColumnValues(logbook, "To", rows)
            : null;
        object?[]? remarksValues = null;
        if (HasColumn((object)logbook, "Remarks"))
        {
            remarksValues = ReadColumnValues(logbook, "Remarks", rows);
        }
        else if (HasColumn((object)logbook, "Details"))
        {
            remarksValues = ReadColumnValues(logbook, "Details", rows);
        }

        for (var row = 0; row < rows; row++)
        {
            var routeText = JoinNonBlank(
                " ",
                fromValues is null ? "" : StableValue(fromValues[row]),
                routeValues is null ? "" : StableValue(routeValues[row]),
                toValues is null ? "" : StableValue(toValues[row]));
            if (string.IsNullOrWhiteSpace(routeText) && remarksValues is not null)
            {
                routeText = StableValue(remarksValues[row]);
            }
            result[row] = routeText;
        }

        return result;
    }

    private static string JoinNonBlank(string separator, params string[] values)
    {
        return string.Join(
            separator,
            values.Select(value => value.Trim()).Where(value => !string.IsNullOrWhiteSpace(value)));
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

    private static bool ToBoolean(object? value)
    {
        return value switch
        {
            null => false,
            bool flag => flag,
            double number => Math.Abs(number) > double.Epsilon,
            float number => Math.Abs(number) > float.Epsilon,
            int number => number != 0,
            decimal number => number != 0,
            string text when bool.TryParse(text.Trim(), out var flag) => flag,
            string text when double.TryParse(text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var number) => Math.Abs(number) > double.Epsilon,
            string text when string.Equals(text.Trim(), "yes", StringComparison.OrdinalIgnoreCase) => true,
            string text when string.Equals(text.Trim(), "y", StringComparison.OrdinalIgnoreCase) => true,
            string text when string.Equals(text.Trim(), "x", StringComparison.OrdinalIgnoreCase) => true,
            _ => false
        };
    }

    private static bool ToCurrencyCheckValue(object? value)
    {
        return value switch
        {
            null => false,
            bool flag => flag,
            DateTime => true,
            double number => number > 0,
            float number => number > 0,
            int number => number > 0,
            decimal number => number > 0,
            string text when bool.TryParse(text.Trim(), out var flag) => flag,
            string text when DateTime.TryParse(
                text.Trim(),
                CultureInfo.CurrentCulture,
                DateTimeStyles.None,
                out _) => true,
            string text when double.TryParse(
                text.Trim(),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var number) => number > 0,
            string text when string.Equals(text.Trim(), "yes", StringComparison.OrdinalIgnoreCase) => true,
            string text when string.Equals(text.Trim(), "y", StringComparison.OrdinalIgnoreCase) => true,
            string text when string.Equals(text.Trim(), "x", StringComparison.OrdinalIgnoreCase) => true,
            _ => false
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
            var sourceFormatName = LogbookSourceFormatColumnName(source, name);
            if (string.IsNullOrEmpty(sourceFormatName))
            {
                continue;
            }

            var sourceIndex = GetColumnIndex(source, sourceFormatName);
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
        CopySumTotalsFormatting(source, destination);
        ApplyLogbookSumTotalsFormatting(outputWorkbookObject, destination);
        ApplyLogbookTotalsAreaFormatting(destination);
        SetLogbookFilterHeadersName(outputWorkbookObject, destination);
        RestoreHeaderPalette(sourceWorkbook, outputWorkbook, destination);
        ApplyHiddenHourHeaderFormatting(destination);
        ApplyNativeCheckboxesIfAvailable(destination, "FR", "IPC", "OPC");

        var lastDataRow =
            (int)destination.DataBodyRange.Row + (int)destination.DataBodyRange.Rows.Count - 1;
        worksheet.Rows.Hidden = false;
        if (lastDataRow + 4 <= (int)worksheet.Rows.Count)
        {
            worksheet.Rows[$"{lastDataRow + 4}:{worksheet.Rows.Count}"].Hidden = true;
        }
    }

    private static string? LogbookSourceFormatColumnName(dynamic source, string name)
    {
        if (string.Equals(name, "Details", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "FlightReview", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "CurrencyExclusions", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "RNAV", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "CumRNAV", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.Equals(name, "Flight ID", StringComparison.OrdinalIgnoreCase))
        {
            return HasColumn((object)source, "Flight ID") ? "Flight ID" : "Reg";
        }
        if (string.Equals(name, "From", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "To", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "Via", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "Remarks", StringComparison.OrdinalIgnoreCase))
        {
            return HasColumn((object)source, "Details") ? "Details" : name;
        }
        if (string.Equals(name, "FR", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "IPC", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "OPC", StringComparison.OrdinalIgnoreCase))
        {
            return HasColumn((object)source, "Custom 1") ? "Custom 1" : "Details";
        }
        if (string.Equals(name, "RNP", StringComparison.OrdinalIgnoreCase))
        {
            if (HasColumn((object)source, "RNP"))
            {
                return "RNP";
            }
            return HasColumn((object)source, "RNAV") ? "RNAV" : null;
        }
        if (string.Equals(name, "CumRNP", StringComparison.OrdinalIgnoreCase))
        {
            if (HasColumn((object)source, "CumRNP"))
            {
                return "CumRNP";
            }
            return HasColumn((object)source, "CumRNAV") ? "CumRNAV" : null;
        }

        return HasColumn((object)source, name) ? name : null;
    }

    private static void CopyTotalsAreaFormatting(dynamic source, dynamic destination)
    {
        dynamic sourceWorksheet = source.Parent;
        dynamic destinationWorksheet = destination.Parent;
        var sourceTotalsRow = (int)source.TotalsRowRange.Row;
        var destinationTotalsRow = (int)destination.TotalsRowRange.Row;
        var sourceFirstColumn = HasColumn((object)source, "Flight ID")
            ? (int)source.ListColumns.Item(GetColumnIndex(source, "Flight ID")).Range.Column
            : (int)source.ListColumns.Item(GetColumnIndex(source, "Reg")).Range.Column;
        var sourceOtherColumn = (int)source.ListColumns
            .Item(GetColumnIndex(source, "Other Pilot or Crew")).Range.Column;
        var destinationFirstColumn = (int)destination.ListColumns
            .Item(GetColumnIndex(destination, "Flight ID")).Range.Column;
        var destinationOtherColumn = (int)destination.ListColumns
            .Item(GetColumnIndex(destination, "Other Pilot or Crew")).Range.Column;

        dynamic sourceRange = sourceWorksheet.Range[
            sourceWorksheet.Cells.Item(sourceTotalsRow, sourceFirstColumn),
            sourceWorksheet.Cells.Item(sourceTotalsRow + 1, sourceOtherColumn)];
        dynamic destinationRange = destinationWorksheet.Range[
            destinationWorksheet.Cells.Item(destinationTotalsRow, destinationFirstColumn),
            destinationWorksheet.Cells.Item(destinationTotalsRow + 1, destinationOtherColumn)];
        sourceRange.Copy();
        destinationRange.PasteSpecial(XlPasteFormats);
        destinationWorksheet.Application.CutCopyMode = false;
        destinationRange.WrapText = false;
    }

    private static void CopySumTotalsFormatting(dynamic source, dynamic destination)
    {
        dynamic sourceWorksheet = source.Parent;
        dynamic destinationWorksheet = destination.Parent;
        var sourceTotalsRow = (int)source.TotalsRowRange.Row;
        var destinationTotalsRow = (int)destination.TotalsRowRange.Row;
        var sourceStartColumn = (int)source.ListColumns
            .Item(GetLogbookCustomStartColumn(source)).Range.Column;
        var sourceEndColumn = (int)source.ListColumns.Item(GetColumnIndex(source, "TotalApps")).Range.Column;
        var destinationStartColumn = (int)destination.ListColumns
            .Item(GetLogbookCustomStartColumn(destination)).Range.Column;
        var destinationEndColumn = (int)destination.ListColumns
            .Item(GetColumnIndex(destination, "TotalApps")).Range.Column;

        dynamic sourceRange = sourceWorksheet.Range[
            sourceWorksheet.Cells.Item(sourceTotalsRow, sourceStartColumn),
            sourceWorksheet.Cells.Item(sourceTotalsRow, sourceEndColumn)];
        dynamic destinationRange = destinationWorksheet.Range[
            destinationWorksheet.Cells.Item(destinationTotalsRow, destinationStartColumn),
            destinationWorksheet.Cells.Item(destinationTotalsRow, destinationEndColumn)];
        sourceRange.Copy();
        destinationRange.PasteSpecial(XlPasteFormats);
        destinationWorksheet.Application.CutCopyMode = false;
        destinationRange.WrapText = false;
    }

    private static void EnsureTotalsArea(object workbookObject, dynamic table)
    {
        dynamic workbook = workbookObject;
        dynamic worksheet = table.Parent;
        var totalsRow = (int)table.TotalsRowRange.Row;
        var picColumn = (int)table.ListColumns.Item(GetColumnIndex(table, "PIC")).Range.Column;
        var otherColumn = (int)table.ListColumns
            .Item(GetColumnIndex(table, "Other Pilot or Crew")).Range.Column;
        var flightIdColumn = (int)table.ListColumns.Item(GetColumnIndex(table, "Flight ID")).Range.Column;
        var firstCustomIndex = GetLogbookCustomStartColumn(table);
        var sumStartColumn = (int)table.ListColumns.Item(firstCustomIndex).Range.Column;
        var sumEndColumn = (int)table.ListColumns.Item(GetColumnIndex(table, "TotalApps")).Range.Column;

        worksheet.Cells.Item(totalsRow + 1, picColumn).Value2 = "Total Aeronautical Experience";
        worksheet.Cells.Item(totalsRow + 1, otherColumn).Formula =
            "=Logbook[[#Totals],[Other Pilot or Crew]]+Logbook[[#Totals],[IfrSim]]";

        SetWorkbookName(
            workbook,
            "LogbookTotals",
            worksheet.Range[
                worksheet.Cells.Item(totalsRow, flightIdColumn),
                worksheet.Cells.Item(totalsRow + 1, otherColumn)]);
        SetWorkbookName(
            workbook,
            "LogbookSumTotals",
            worksheet.Range[
                worksheet.Cells.Item(totalsRow, sumStartColumn),
                worksheet.Cells.Item(totalsRow, sumEndColumn)]);
    }

    private static void SetLogbookFilterHeadersName(object workbookObject, dynamic table)
    {
        dynamic workbook = workbookObject;
        dynamic worksheet = table.Parent;
        var headerRow = (int)table.HeaderRowRange.Row;
        var dateColumn = (int)table.ListColumns.Item(GetColumnIndex(table, "Date")).Range.Column;
        var typeColumn = (int)table.ListColumns.Item(GetColumnIndex(table, "Type")).Range.Column;
        var circlingColumn =
            (int)table.ListColumns.Item(GetColumnIndex(table, "Circling")).Range.Column;

        dynamic dateHeader = worksheet.Cells.Item(headerRow, dateColumn);
        dynamic entryHeaders = worksheet.Range[
            worksheet.Cells.Item(headerRow, typeColumn),
            worksheet.Cells.Item(headerRow, circlingColumn)];
        dynamic filterHeaders = workbook.Application.Union(dateHeader, entryHeaders);

        SetWorkbookName(workbook, "LogbookFilterHeaders", filterHeaders);
    }

    private static void ApplyLogbookSumTotalsFormatting(object workbookObject, dynamic table)
    {
        dynamic workbook = workbookObject;
        dynamic sumTotalsRange = workbook.Names.Item("LogbookSumTotals").RefersToRange;
        var secondaryColor = (int)table.DataBodyRange.Rows.Item(1).Cells.Item(1, 1)
            .DisplayFormat.Interior.Color;
        sumTotalsRange.Interior.Pattern = 1;
        sumTotalsRange.Interior.Color = ColorWithLightness(secondaryColor, 0.2);
        sumTotalsRange.Font.Color = 0xFFFFFF;
        dynamic labelCell = sumTotalsRange.Cells.Item(1, 1).Offset[0, -1];
        labelCell.HorizontalAlignment = -4152;
        labelCell.WrapText = false;
    }

    private static void ApplyLogbookTotalsAreaFormatting(dynamic table)
    {
        dynamic worksheet = table.Parent;
        var totalsRow = (int)table.TotalsRowRange.Row;
        var flightIdColumn = (int)table.ListColumns.Item(GetColumnIndex(table, "Flight ID")).Range.Column;
        var picColumn = (int)table.ListColumns.Item(GetColumnIndex(table, "PIC")).Range.Column;
        var otherColumn = (int)table.ListColumns
            .Item(GetColumnIndex(table, "Other Pilot or Crew")).Range.Column;
        var secondaryColor = (int)table.DataBodyRange.Rows.Item(1).Cells.Item(1, 1)
            .DisplayFormat.Interior.Color;
        var textColor = ContrastingTextColor(secondaryColor);
        var fontName = (string)table.DataBodyRange.Cells.Item(1, 1).Font.Name;
        var fontSize = (double)table.DataBodyRange.Cells.Item(1, 1).Font.Size;

        dynamic totalsBlock = worksheet.Range[
            worksheet.Cells.Item(totalsRow, flightIdColumn),
            worksheet.Cells.Item(totalsRow + 1, otherColumn)];
        dynamic topRow = totalsBlock.Rows.Item(1);
        dynamic bottomRow = totalsBlock.Rows.Item(2);
        dynamic firstColumnCells = worksheet.Range[
            worksheet.Cells.Item(totalsRow, flightIdColumn),
            worksheet.Cells.Item(totalsRow + 1, flightIdColumn)];
        dynamic labelCells = worksheet.Range[
            worksheet.Cells.Item(totalsRow, picColumn),
            worksheet.Cells.Item(totalsRow + 1, picColumn)];
        dynamic hoursCells = worksheet.Range[
            worksheet.Cells.Item(totalsRow, otherColumn),
            worksheet.Cells.Item(totalsRow + 1, otherColumn)];
        dynamic totalsCellLeftOfBlock = worksheet.Cells.Item(totalsRow, flightIdColumn - 1);
        dynamic experienceCellLeftOfBlock = worksheet.Cells.Item(totalsRow + 1, flightIdColumn - 1);
        dynamic experienceCellLeftFillSource = experienceCellLeftOfBlock.Offset[0, -1];

        topRow.Interior.Pattern = -4142;
        topRow.Font.Color = 0;
        topRow.Font.Bold = false;
        topRow.Cells.Item(1, 3).Font.Bold = true;

        bottomRow.Interior.Pattern = 1;
        bottomRow.Interior.Color = secondaryColor;
        bottomRow.Font.Color = textColor;
        bottomRow.Font.Bold = true;
        totalsBlock.Font.Name = fontName;
        totalsBlock.Font.Size = fontSize;

        firstColumnCells.HorizontalAlignment = -4152;
        firstColumnCells.WrapText = false;
        labelCells.HorizontalAlignment = -4152;
        labelCells.WrapText = false;
        hoursCells.HorizontalAlignment = -4108;
        hoursCells.VerticalAlignment = -4108;
        hoursCells.WrapText = false;
        bottomRow.Cells.Item(1, 3).NumberFormat = topRow.Cells.Item(1, 3).NumberFormat;
        totalsCellLeftOfBlock.Interior.Pattern = 1;
        totalsCellLeftOfBlock.Interior.Color = 0;
        totalsCellLeftOfBlock.Font.Color = 0xFFFFFF;
        totalsCellLeftOfBlock.Font.Bold = false;
        totalsCellLeftOfBlock.HorizontalAlignment = -4152;
        totalsCellLeftOfBlock.WrapText = false;
        experienceCellLeftOfBlock.Interior.Pattern = experienceCellLeftFillSource.Interior.Pattern;
        experienceCellLeftOfBlock.Interior.Color = experienceCellLeftFillSource.Interior.Color;
        experienceCellLeftOfBlock.Font.Color = experienceCellLeftFillSource.Font.Color;
        experienceCellLeftOfBlock.Font.Bold = experienceCellLeftFillSource.Font.Bold;
        experienceCellLeftOfBlock.HorizontalAlignment = -4152;
        experienceCellLeftOfBlock.WrapText = false;
    }

    private static void ApplyNativeCheckboxesIfAvailable(dynamic table, params string[] columnNames)
    {
        foreach (var columnName in columnNames)
        {
            if (!HasColumn((object)table, columnName))
            {
                continue;
            }

            try
            {
                table.ListColumns.Item(GetColumnIndex(table, columnName))
                    .DataBodyRange.CellControl.SetCheckbox();
            }
            catch (COMException)
            {
                // Older Excel builds do not expose native in-cell checkbox controls.
            }
            catch (RuntimeBinderException)
            {
                // Some COM wrappers surface the missing CellControl member this way.
            }
        }
    }

    private static void ApplyHiddenHourHeaderFormatting(dynamic destination)
    {
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

    private static int ColorWithLightness(int sourceColor, double targetLightness)
    {
        var red = (sourceColor & 0xFF) / 255.0;
        var green = ((sourceColor >> 8) & 0xFF) / 255.0;
        var blue = ((sourceColor >> 16) & 0xFF) / 255.0;
        var maximum = Math.Max(red, Math.Max(green, blue));
        var minimum = Math.Min(red, Math.Min(green, blue));

        if (Math.Abs(maximum - minimum) < double.Epsilon)
        {
            var grey = (int)Math.Round(targetLightness * 255);
            return grey + (grey << 8) + (grey << 16);
        }

        var lightness = (maximum + minimum) / 2.0;
        var saturation = lightness > 0.5
            ? (maximum - minimum) / (2.0 - maximum - minimum)
            : (maximum - minimum) / (maximum + minimum);

        double hue;
        if (Math.Abs(maximum - red) < double.Epsilon)
        {
            hue = (green - blue) / (maximum - minimum);
            if (green < blue)
            {
                hue += 6.0;
            }
        }
        else if (Math.Abs(maximum - green) < double.Epsilon)
        {
            hue = (blue - red) / (maximum - minimum) + 2.0;
        }
        else
        {
            hue = (red - green) / (maximum - minimum) + 4.0;
        }

        hue /= 6.0;
        var second = targetLightness < 0.5
            ? targetLightness * (1.0 + saturation)
            : targetLightness + saturation - (targetLightness * saturation);
        var first = (2.0 * targetLightness) - second;
        var outRed = (int)Math.Round(255 * HueChannel(first, second, hue + (1.0 / 3.0)));
        var outGreen = (int)Math.Round(255 * HueChannel(first, second, hue));
        var outBlue = (int)Math.Round(255 * HueChannel(first, second, hue - (1.0 / 3.0)));
        return outRed + (outGreen << 8) + (outBlue << 16);
    }

    private static double HueChannel(double first, double second, double hue)
    {
        if (hue < 0)
        {
            hue += 1.0;
        }
        if (hue > 1)
        {
            hue -= 1.0;
        }

        if (hue < 1.0 / 6.0)
        {
            return first + ((second - first) * 6.0 * hue);
        }
        if (hue < 1.0 / 2.0)
        {
            return second;
        }
        if (hue < 2.0 / 3.0)
        {
            return first + ((second - first) * ((2.0 / 3.0) - hue) * 6.0);
        }
        return first;
    }

    private static int ContrastingTextColor(int backgroundColor)
    {
        var red = backgroundColor & 0xFF;
        var green = (backgroundColor >> 8) & 0xFF;
        var blue = (backgroundColor >> 16) & 0xFF;
        var brightness = ((red * 299) + (green * 587) + (blue * 114)) / 1000.0;
        return brightness >= 150 ? 0 : 0xFFFFFF;
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
        var refersTo = BuildRangeRefersToFormula(range);
        try
        {
            workbook.Names.Item(name).RefersTo = refersTo;
        }
        catch
        {
            workbook.Names.Add(name, refersTo);
        }
    }

    private static string BuildRangeRefersToFormula(dynamic range)
    {
        var areas = range.Areas;
        var areaCount = (int)areas.Count;
        var parts = new string[areaCount];
        for (var index = 1; index <= areaCount; index++)
        {
            dynamic area = areas.Item(index);
            string worksheetName = area.Worksheet.Name;
            parts[index - 1] = $"'{worksheetName.Replace("'", "''")}'!{area.Address}";
        }

        return "=" + string.Join(",", parts);
    }

    private static void ValidateLogbookStructure(
        object sourceWorkbookObject,
        object outputWorkbookObject)
    {
        dynamic outputWorkbook = outputWorkbookObject;
        dynamic destination = GetTable(outputWorkbookObject, "Logbook");

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
            ["Preferences"] = FingerprintNames(workbook, PreservedNames)
        };
        if (GetTableOrNull(workbook, "Keywords") is not null)
        {
            fingerprints["Keywords"] = FingerprintTable(workbook, "Keywords");
        }
        if (GetTableOrNull(workbook, "Routes") is not null)
        {
            fingerprints["Routes"] = FingerprintColumns(
                workbook,
                "Routes",
                new[] { "DepAirport", "ArrAirport" });
        }
        if (GetTableOrNull(workbook, "BaseAirportsTop10") is not null)
        {
            fingerprints["BaseAirports"] = FingerprintBaseAirportSelections(workbook);
        }
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
        var columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = start; index <= end; index++)
        {
            var name = (string)table.ListColumns.Item(index).Name;
            if (LogbookFingerprintColumnIsPreserved(name))
            {
                columns.TryAdd(CanonicalLogbookFingerprintColumnName(name), name);
            }
        }

        fingerprints["LogbookHeaders"] = Sha256(string.Join('\u001f', columns.Keys));
        foreach (var column in columns)
        {
            fingerprints[$"Logbook:{column.Key}"] = FingerprintLogbookColumn(
                workbook,
                column.Value,
                column.Key);
        }
    }

    private static bool LogbookFingerprintColumnIsPreserved(string name)
    {
        return !string.Equals(name, "Details", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(name, "Flight ID", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(name, "FlightReview", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(name, "CurrencyExclusions", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(name, "From", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(name, "To", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(name, "Via", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(name, "Remarks", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(name, "FR", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(name, "IPC", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(name, "OPC", StringComparison.OrdinalIgnoreCase);
    }

    private static string CanonicalLogbookFingerprintColumnName(string name)
    {
        if (string.Equals(name, "RNAV", StringComparison.OrdinalIgnoreCase))
        {
            return "RNP";
        }

        if (string.Equals(name, "CumRNAV", StringComparison.OrdinalIgnoreCase))
        {
            return "CumRNP";
        }

        return name;
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

    private static string FingerprintLogbookColumn(
        dynamic workbook,
        string actualColumn,
        string canonicalColumn)
    {
        dynamic table = GetTable(workbook, "Logbook");
        var rows = (int)table.ListRows.Count;
        var builder = new StringBuilder();
        builder.Append("Logbook").Append('|').Append(rows);
        builder.Append('|').Append(canonicalColumn);
        foreach (var value in ReadColumnValues(table, actualColumn, rows))
        {
            builder.Append('\u001f').Append(StableValue(value));
        }

        return Sha256(builder.ToString());
    }

    private static string FingerprintBaseAirportSelections(dynamic workbook)
    {
        var selections = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        dynamic? table = GetTableOrNull((object)workbook, "BaseAirportsTop10");
        if (table is not null && HasColumn((object)table, "ICAO") && HasColumn((object)table, "Base"))
        {
            var rows = (int)table.ListRows.Count;
            var icaoValues = ReadColumnValues(table, "ICAO", rows);
            var baseValues = ReadColumnValues(table, "Base", rows);
            for (var row = 0; row < rows; row++)
            {
                var icao = StableValue(icaoValues[row]).Trim();
                if (!string.IsNullOrEmpty(icao) && ToBoolean(baseValues[row]))
                {
                    selections[icao] = "TRUE";
                }
            }
        }
        else
        {
            foreach (var selection in ReadLegacyAirportBaseSelections((object)workbook))
            {
                selections[selection.Key] = "TRUE";
            }
        }

        var builder = new StringBuilder("BaseAirports");
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
        CopyColumn(source, destination, name, name, rows);
    }

    private static void CopyColumn(
        dynamic source,
        dynamic destination,
        string sourceName,
        string destinationName,
        int rows)
    {
        var sourceIndex = GetColumnIndex(source, sourceName);
        var destinationIndex = GetColumnIndex(destination, destinationName);
        destination.DataBodyRange.Columns.Item(destinationIndex).Resize[rows, 1].Value2 =
            source.DataBodyRange.Columns.Item(sourceIndex).Resize[rows, 1].Value2;
    }

    private static string? DestinationLogbookColumnName(object destinationTable, string sourceName)
    {
        if (HasColumn(destinationTable, sourceName))
        {
            return sourceName;
        }

        if (string.Equals(sourceName, "RNAV", StringComparison.OrdinalIgnoreCase) &&
            HasColumn(destinationTable, "RNP"))
        {
            return "RNP";
        }

        if (string.Equals(sourceName, "CumRNAV", StringComparison.OrdinalIgnoreCase) &&
            HasColumn(destinationTable, "CumRNP"))
        {
            return "CumRNP";
        }

        return null;
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

    private static dynamic? GetTableOrNull(object workbookObject, string tableName)
    {
        try
        {
            return GetTable(workbookObject, tableName);
        }
        catch (InvalidDataException)
        {
            return null;
        }
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
            null => "",
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
