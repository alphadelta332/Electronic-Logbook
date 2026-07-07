Attribute VB_Name = "modAirports"
Option Explicit

Private Const AIRPORT_DATA_URL As String = "https://davidmegginson.github.io/ourairports-data/airports.csv"
Private Const AIRPORT_DATA_SOURCE As String = "OurAirports airports.csv"
Private Const AIRAC_EPOCH_DATE As Date = #1/25/2024#
Private Const AIRAC_CYCLE_DAYS As Long = 28
Private Const BASE_AIRPORTS_TABLE As String = "BaseAirportsTop10"
Private Const BASE_AIRPORTS_TOP_COUNT As Long = 10

Public Function AirportDatasetUpdateAvailable(ByVal wb As Workbook, Optional ByVal forceCheck As Boolean = False) As Boolean
    Dim remoteVersion As String
    Dim storedVersion As String
    Dim currentAiracCycle As String

    If Not forceCheck Then
        currentAiracCycle = CurrentAiracCycleKey()
        If StrComp(ReadWorkbookNameText(wb, "AirportDatasetLastAiracChecked", ""), currentAiracCycle, vbTextCompare) = 0 Then Exit Function
    End If

    remoteVersion = FetchAirportDatasetVersion()
    If remoteVersion = "" Then Err.Raise 5, "AirportDatasetUpdateAvailable", "The airport dataset did not provide a usable version header."

    storedVersion = ReadWorkbookNameText(wb, "AirportDatasetVersion", "")
    WriteWorkbookNameText wb, "AirportDatasetLastChecked", Format$(Now, "yyyy-mm-dd hh:nn:ss")
    WriteWorkbookNameText wb, "AirportDatasetLastAiracChecked", CurrentAiracCycleKey()
    WriteWorkbookNameText wb, "AirportDatasetAvailableVersion", remoteVersion

    AirportDatasetUpdateAvailable = (storedVersion = "" Or StrComp(storedVersion, remoteVersion, vbTextCompare) <> 0)
End Function

Public Function RefreshAirportDataset(ByVal wb As Workbook, Optional ByVal forceCheck As Boolean = False) As Boolean
    Dim remoteVersion As String
    Dim storedVersion As String
    Dim currentAiracCycle As String
    Dim csvText As String
    Dim remoteRecords As Object
    Dim mergedRecords As Object
    Dim sortedKeys As Variant
    Dim tbl As ListObject
    Dim oldSignature As String
    Dim newSignature As String

    Set tbl = wb.Worksheets("Airports").ListObjects("Airports")

    If Not forceCheck Then
        currentAiracCycle = CurrentAiracCycleKey()
        If StrComp(ReadWorkbookNameText(wb, "AirportDatasetLastAiracChecked", ""), currentAiracCycle, vbTextCompare) = 0 Then Exit Function
    End If

    remoteVersion = FetchAirportDatasetVersion()
    If remoteVersion = "" Then Err.Raise 5, "RefreshAirportDataset", "The airport dataset did not provide a usable version header."

    storedVersion = ReadWorkbookNameText(wb, "AirportDatasetVersion", "")
    WriteWorkbookNameText wb, "AirportDatasetLastChecked", Format$(Now, "yyyy-mm-dd hh:nn:ss")
    WriteWorkbookNameText wb, "AirportDatasetLastAiracChecked", CurrentAiracCycleKey()

    If Not forceCheck Then
        If storedVersion <> "" And StrComp(storedVersion, remoteVersion, vbTextCompare) = 0 Then Exit Function
    End If

    csvText = DownloadAirportDataset()
    Set remoteRecords = ParseAirportDataset(csvText)
    Set mergedRecords = MergeAirportRecords(tbl, remoteRecords)
    sortedKeys = SortedDictionaryKeys(mergedRecords)

    oldSignature = AirportTableSignature(tbl)
    newSignature = AirportRecordsSignature(mergedRecords, sortedKeys)

    If oldSignature <> newSignature Then
        ReplaceAirportTable tbl, mergedRecords, sortedKeys
        RefreshAirportDataset = True
    End If

    RefreshAirportVisitStats wb

    WriteWorkbookNameText wb, "AirportDatasetSource", AIRPORT_DATA_SOURCE
    WriteWorkbookNameText wb, "AirportDatasetVersion", remoteVersion
    WriteWorkbookNameText wb, "AirportDatasetLastUpdated", Format$(Now, "yyyy-mm-dd hh:nn:ss")
End Function

Public Sub CheckAirportDatasetNow()
    If modLogbook.RefreshAirportDatasetWithWorkbookProtection(ThisWorkbook, True) Then
        MsgBox "Airport dataset updated successfully.", vbInformation, "Airport Dataset Updated"
    Else
        MsgBox "Airport dataset is already up to date.", vbInformation, "Airport Dataset"
    End If
End Sub

Public Sub RefreshAirportVisitStats(ByVal wb As Workbook)
    Dim tblAirports As ListObject
    Dim tblLog As ListObject
    Dim airportRows As Object
    Dim airportStats As Object
    Dim aliasLookup As Object
    Dim rowIndex As Long
    Dim icao As String
    Dim stat As Variant
    Dim output As Variant
    Dim rankLookup As Object
    Dim airportCount As Long
    Dim firstVisitedCol As Long
    Dim lastVisitedCol As Long
    Dim visitsCol As Long
    Dim rankCol As Long

    Set tblAirports = wb.Worksheets("Airports").ListObjects("Airports")
    Set tblLog = wb.Worksheets("Logbook").ListObjects("Logbook")
    If tblAirports.DataBodyRange Is Nothing Then Exit Sub

    Set airportRows = CreateObject("Scripting.Dictionary")
    airportRows.CompareMode = 1
    Set airportStats = CreateObject("Scripting.Dictionary")
    airportStats.CompareMode = 1
    Set aliasLookup = BuildAirportAliasLookup(tblAirports, airportRows, airportStats)

    If Not tblLog.DataBodyRange Is Nothing Then AccumulateAirportVisits tblLog, aliasLookup, airportStats

    airportCount = tblAirports.DataBodyRange.Rows.Count
    Set rankLookup = BuildAirportVisitRankLookup(airportStats)
    ReDim output(1 To airportCount, 1 To 4)

    For rowIndex = 1 To airportCount
        icao = UCase$(Trim$(CStr(tblAirports.DataBodyRange.Cells(rowIndex, tblAirports.ListColumns("ICAO").Index).Value)))
        If icao <> "" And airportStats.Exists(icao) Then
            stat = airportStats(icao)
            If CLng(stat(0)) > 0 Then
                output(rowIndex, 1) = stat(1)
                output(rowIndex, 2) = stat(2)
                output(rowIndex, 3) = stat(0)
                If rankLookup.Exists(icao) Then output(rowIndex, 4) = rankLookup(icao)
            End If
        End If
    Next rowIndex

    firstVisitedCol = tblAirports.ListColumns("First Visited").Index
    lastVisitedCol = tblAirports.ListColumns("Last Visited").Index
    visitsCol = tblAirports.ListColumns("Visits").Index
    rankCol = tblAirports.ListColumns("Rank").Index

    tblAirports.ListColumns(firstVisitedCol).DataBodyRange.Value = Application.Index(output, 0, 1)
    tblAirports.ListColumns(lastVisitedCol).DataBodyRange.Value = Application.Index(output, 0, 2)
    tblAirports.ListColumns(visitsCol).DataBodyRange.Value = Application.Index(output, 0, 3)
    tblAirports.ListColumns(rankCol).DataBodyRange.Value = Application.Index(output, 0, 4)

    RefreshBaseAirportSelector wb, tblLog, tblAirports, aliasLookup
End Sub

Private Function BuildAirportAliasLookup(ByVal tblAirports As ListObject, ByVal airportRows As Object, ByVal airportStats As Object) As Object
    Dim lookup As Object
    Dim rowIndex As Long
    Dim icao As String
    Dim twoCode As String
    Dim threeCode As String
    Dim stat As Variant

    Set lookup = CreateObject("Scripting.Dictionary")
    lookup.CompareMode = 1

    For rowIndex = 1 To tblAirports.DataBodyRange.Rows.Count
        icao = UCase$(Trim$(CStr(tblAirports.DataBodyRange.Cells(rowIndex, tblAirports.ListColumns("ICAO").Index).Value)))
        twoCode = UCase$(Trim$(CStr(tblAirports.DataBodyRange.Cells(rowIndex, tblAirports.ListColumns("Two").Index).Value)))
        threeCode = UCase$(Trim$(CStr(tblAirports.DataBodyRange.Cells(rowIndex, tblAirports.ListColumns("Three").Index).Value)))

        If icao <> "" Then
            If Not airportRows.Exists(icao) Then airportRows.Add icao, rowIndex
            If Not lookup.Exists(icao) Then lookup.Add icao, icao
            If twoCode <> "" And Not lookup.Exists(twoCode) Then lookup.Add twoCode, icao
            If threeCode <> "" And Not lookup.Exists(threeCode) Then lookup.Add threeCode, icao
            stat = Array(0&, Empty, Empty)
            airportStats(icao) = stat
        End If
    Next rowIndex

    Set BuildAirportAliasLookup = lookup
End Function

Private Sub AccumulateAirportVisits(ByVal tblLog As ListObject, ByVal aliasLookup As Object, ByVal airportStats As Object)
    Dim yearCol As Long
    Dim monthCol As Long
    Dim dayCol As Long
    Dim remarksCol As Long
    Dim fromCol As Long
    Dim toCol As Long
    Dim hasRemarksCol As Boolean
    Dim hasFromCol As Boolean
    Dim hasToCol As Boolean
    Dim rowIndex As Long
    Dim remarks As String
    Dim endpoint As String
    Dim tokens As Variant
    Dim token As String
    Dim icao As String
    Dim flightDate As Variant
    Dim rowMatches As Object
    Dim key As Variant

    yearCol = tblLog.ListColumns("Year").Index
    monthCol = tblLog.ListColumns("Month").Index
    dayCol = tblLog.ListColumns("Day").Index
    If AirportTableColumnExists(tblLog, "Remarks") Then
        hasRemarksCol = True
        remarksCol = tblLog.ListColumns("Remarks").Index
    ElseIf AirportTableColumnExists(tblLog, "Details") Then
        hasRemarksCol = True
        remarksCol = tblLog.ListColumns("Details").Index
    End If
    If AirportTableColumnExists(tblLog, "From") Then
        hasFromCol = True
        fromCol = tblLog.ListColumns("From").Index
    End If
    If AirportTableColumnExists(tblLog, "To") Then
        hasToCol = True
        toCol = tblLog.ListColumns("To").Index
    End If

    For rowIndex = 1 To tblLog.DataBodyRange.Rows.Count
        If AirportStatsLogbookRowIsSimOnly(tblLog, rowIndex) Then GoTo NextRow

        flightDate = ResolveAirportStatsLogbookDate( _
            tblLog.DataBodyRange.Cells(rowIndex, yearCol).Value2, _
            tblLog.DataBodyRange.Cells(rowIndex, monthCol).Value2, _
            tblLog.DataBodyRange.Cells(rowIndex, dayCol).Value2)
        Set rowMatches = CreateObject("Scripting.Dictionary")
        rowMatches.CompareMode = 1

        If hasFromCol Then
            endpoint = UCase$(Trim$(CStr(tblLog.DataBodyRange.Cells(rowIndex, fromCol).Value)))
            If endpoint <> "" And aliasLookup.Exists(endpoint) Then rowMatches(CStr(aliasLookup(endpoint))) = True
        End If
        If hasToCol Then
            endpoint = UCase$(Trim$(CStr(tblLog.DataBodyRange.Cells(rowIndex, toCol).Value)))
            If endpoint <> "" And aliasLookup.Exists(endpoint) Then rowMatches(CStr(aliasLookup(endpoint))) = True
        End If

        If hasRemarksCol Then
            remarks = Trim$(CStr(tblLog.DataBodyRange.Cells(rowIndex, remarksCol).Value))
            If remarks <> "" Then
                tokens = Split(TokeniseAirportDetails(remarks), "|")
                For Each key In tokens
                    token = UCase$(Trim$(CStr(key)))
                    If token <> "" Then
                        If Not AirportStatsIgnoreToken(token) Then
                            If aliasLookup.Exists(token) Then
                                icao = CStr(aliasLookup(token))
                                If Not rowMatches.Exists(icao) Then rowMatches.Add icao, True
                            End If
                        End If
                    End If
                Next key
            End If
        End If

        For Each key In rowMatches.Keys
            AddAirportVisit airportStats, CStr(key), flightDate
        Next key

NextRow:
    Next rowIndex
End Sub

Private Function ResolveAirportStatsLogbookDate(ByVal yearValue As Variant, _
                                                ByVal monthValue As Variant, _
                                                ByVal dayValue As Variant) As Variant
    Dim yearNumber As Long
    Dim monthNumber As Long
    Dim dayNumber As Long

    If Not IsNumeric(yearValue) Then Exit Function
    yearNumber = CLng(yearValue)
    monthNumber = ResolveAirportStatsMonth(monthValue)
    dayNumber = ResolveAirportStatsDay(dayValue)

    If yearNumber <= 0 Or monthNumber <= 0 Or dayNumber <= 0 Then Exit Function

    On Error GoTo InvalidDate
    ResolveAirportStatsLogbookDate = CDbl(DateSerial(yearNumber, monthNumber, dayNumber))
    Exit Function

InvalidDate:
End Function

Private Function ResolveAirportStatsMonth(ByVal monthValue As Variant) As Long
    Dim monthText As String
    Dim monthIndex As Long

    If IsNumeric(monthValue) Then
        If CDbl(monthValue) >= 1 And CDbl(monthValue) <= 12 Then
            ResolveAirportStatsMonth = CLng(monthValue)
            Exit Function
        End If
        If CDbl(monthValue) > 31 Then
            ResolveAirportStatsMonth = Month(CDate(CDbl(monthValue)))
            Exit Function
        End If
    End If

    monthText = UCase$(Trim$(CStr(monthValue)))
    For monthIndex = 1 To 12
        If monthText = UCase$(Format$(DateSerial(2000, monthIndex, 1), "mmm")) Or _
           monthText = UCase$(Format$(DateSerial(2000, monthIndex, 1), "mmmm")) Then
            ResolveAirportStatsMonth = monthIndex
            Exit Function
        End If
    Next monthIndex
End Function

Private Function ResolveAirportStatsDay(ByVal dayValue As Variant) As Long
    If IsNumeric(dayValue) Then
        If CDbl(dayValue) >= 1 And CDbl(dayValue) <= 31 Then
            ResolveAirportStatsDay = CLng(dayValue)
        ElseIf CDbl(dayValue) > 31 Then
            ResolveAirportStatsDay = Day(CDate(CDbl(dayValue)))
        End If
    End If
End Function

Private Function TokeniseAirportDetails(ByVal details As String) As String
    Dim delimiter As Variant

    details = Replace(details, "|", "")
    For Each delimiter In Array("-", " ", ",", "(", ")")
        details = Join(Split(details, CStr(delimiter)), "|")
    Next delimiter
    TokeniseAirportDetails = details
End Function

Private Sub AddAirportVisit(ByVal airportStats As Object, ByVal icao As String, ByVal flightDate As Variant)
    Dim stat As Variant
    Dim flightSerial As Double

    If Not airportStats.Exists(icao) Then Exit Sub
    stat = airportStats(icao)
    stat(0) = CLng(stat(0)) + 1
    If IsNumeric(flightDate) Then
        flightSerial = CDbl(flightDate)
        If flightSerial <= 0 Then GoTo StoreStat
        If IsEmpty(stat(1)) Then
            stat(1) = flightSerial
            stat(2) = flightSerial
        Else
            If flightSerial < CDbl(stat(1)) Then stat(1) = flightSerial
            If flightSerial > CDbl(stat(2)) Then stat(2) = flightSerial
        End If
    End If

StoreStat:
    airportStats(icao) = stat
End Sub

Private Function BuildAirportVisitRankLookup(ByVal airportStats As Object) As Object
    Dim ranks As Object
    Dim visitedKeys As Collection
    Dim sortedKeys As Variant
    Dim key As Variant
    Dim stat As Variant
    Dim index As Long

    Set ranks = CreateObject("Scripting.Dictionary")
    ranks.CompareMode = 1
    Set visitedKeys = New Collection

    For Each key In airportStats.Keys
        stat = airportStats(CStr(key))
        If CLng(stat(0)) > 0 Then visitedKeys.Add CStr(key)
    Next key

    If visitedKeys.Count = 0 Then
        Set BuildAirportVisitRankLookup = ranks
        Exit Function
    End If

    ReDim sortedKeys(0 To visitedKeys.Count - 1)
    For index = 1 To visitedKeys.Count
        sortedKeys(index - 1) = visitedKeys(index)
    Next index

    If UBound(sortedKeys) > LBound(sortedKeys) Then QuickSortAirportRanks sortedKeys, airportStats, LBound(sortedKeys), UBound(sortedKeys)

    For index = LBound(sortedKeys) To UBound(sortedKeys)
        ranks(CStr(sortedKeys(index))) = index - LBound(sortedKeys) + 1
    Next index

    Set BuildAirportVisitRankLookup = ranks
End Function

Private Sub QuickSortAirportRanks(ByRef values As Variant, ByVal airportStats As Object, ByVal first As Long, ByVal last As Long)
    Dim low As Long
    Dim high As Long
    Dim pivot As String
    Dim temp As Variant

    low = first
    high = last
    pivot = CStr(values((first + last) \ 2))

    Do While low <= high
        Do While AirportRankComesBefore(CStr(values(low)), pivot, airportStats)
            low = low + 1
        Loop
        Do While AirportRankComesBefore(pivot, CStr(values(high)), airportStats)
            high = high - 1
        Loop
        If low <= high Then
            temp = values(low)
            values(low) = values(high)
            values(high) = temp
            low = low + 1
            high = high - 1
        End If
    Loop

    If first < high Then QuickSortAirportRanks values, airportStats, first, high
    If low < last Then QuickSortAirportRanks values, airportStats, low, last
End Sub

Private Function AirportRankComesBefore(ByVal leftIcao As String, ByVal rightIcao As String, ByVal airportStats As Object) As Boolean
    Dim leftStat As Variant
    Dim rightStat As Variant
    Dim leftVisits As Long
    Dim rightVisits As Long

    leftStat = airportStats(leftIcao)
    rightStat = airportStats(rightIcao)
    leftVisits = CLng(leftStat(0))
    rightVisits = CLng(rightStat(0))

    If leftVisits <> rightVisits Then
        AirportRankComesBefore = (leftVisits > rightVisits)
    Else
        AirportRankComesBefore = (StrComp(leftIcao, rightIcao, vbTextCompare) < 0)
    End If
End Function

Private Sub RefreshBaseAirportSelector(ByVal wb As Workbook, _
                                       ByVal tblLog As ListObject, _
                                       ByVal tblAirports As ListObject, _
                                       ByVal aliasLookup As Object)
    Dim tblBase As ListObject
    Dim savedSelections As Object
    Dim endpointStats As Object
    Dim topIcaos As Variant
    Dim topUpperBound As Long
    Dim rowIndex As Long
    Dim icao As String
    Dim defaultValue As Boolean

    Set tblBase = EnsureBaseAirportsTable(wb)
    Set savedSelections = ReadBaseAirportSelections(tblBase, tblAirports)
    Set endpointStats = BuildEndpointAirportStats(tblLog, aliasLookup)
    topIcaos = TopEndpointAirportIcaos(endpointStats, BASE_AIRPORTS_TOP_COUNT)
    topUpperBound = -1
    On Error Resume Next
    topUpperBound = UBound(topIcaos)
    Err.Clear
    On Error GoTo 0

    ResizeBaseAirportsTable tblBase, BASE_AIRPORTS_TOP_COUNT
    If Not tblBase.DataBodyRange Is Nothing Then tblBase.DataBodyRange.ClearContents

    For rowIndex = 1 To BASE_AIRPORTS_TOP_COUNT
        icao = ""
        If rowIndex - 1 <= topUpperBound Then icao = CStr(topIcaos(rowIndex - 1))

        tblBase.DataBodyRange.Cells(rowIndex, tblBase.ListColumns("ICAO").Index).Value = icao
        tblBase.DataBodyRange.Cells(rowIndex, tblBase.ListColumns("Airport").Index).Formula = _
            "=IFERROR(INDEX(Airports[Airport],MATCH([@ICAO],Airports[ICAO],0)),"""")"

        If icao = "" Then
            tblBase.DataBodyRange.Cells(rowIndex, tblBase.ListColumns("Base").Index).ClearContents
        ElseIf savedSelections.Exists(icao) Then
            tblBase.DataBodyRange.Cells(rowIndex, tblBase.ListColumns("Base").Index).Value = CBool(savedSelections(icao))
        Else
            defaultValue = (rowIndex = 1)
            tblBase.DataBodyRange.Cells(rowIndex, tblBase.ListColumns("Base").Index).Value = defaultValue
        End If
    Next rowIndex

    ApplyBaseAirportCheckboxes tblBase
End Sub

Private Function EnsureBaseAirportsTable(ByVal wb As Workbook) As ListObject
    Dim ws As Worksheet
    Dim tbl As ListObject
    Dim tblStats As ListObject
    Dim targetRange As Range
    Dim startRow As Long
    Dim startCol As Long

    Set tbl = FindWorkbookTable(wb, BASE_AIRPORTS_TABLE)
    If Not tbl Is Nothing Then
        EnsureBaseAirportsTableColumns tbl
        Set EnsureBaseAirportsTable = tbl
        Exit Function
    End If

    Set ws = wb.Worksheets("Stats")
    Set tblStats = FindWorkbookTable(wb, "Stats")
    If tblStats Is Nothing Then
        startRow = 2
        startCol = 5
    Else
        startRow = tblStats.Range.Row
        startCol = tblStats.Range.Column + tblStats.Range.Columns.Count + 2
    End If

    Set targetRange = ws.Range(ws.Cells(startRow, startCol), _
                               ws.Cells(startRow + BASE_AIRPORTS_TOP_COUNT, startCol + 2))
    targetRange.Clear
    targetRange.Rows(1).Value = Array("Airport", "Base", "ICAO")
    Set tbl = ws.ListObjects.Add(xlSrcRange, targetRange, , xlYes)
    tbl.Name = BASE_AIRPORTS_TABLE
    tbl.TableStyle = "TableStyleMedium2"
    ws.Columns(tbl.ListColumns("ICAO").Range.Column).Hidden = True

    Set EnsureBaseAirportsTable = tbl
End Function

Private Sub EnsureBaseAirportsTableColumns(ByVal tbl As ListObject)
    If Not AirportTableColumnExists(tbl, "Airport") Then tbl.ListColumns.Add.Name = "Airport"
    If Not AirportTableColumnExists(tbl, "Base") Then tbl.ListColumns.Add.Name = "Base"
    If Not AirportTableColumnExists(tbl, "ICAO") Then tbl.ListColumns.Add.Name = "ICAO"
    tbl.Parent.Columns(tbl.ListColumns("ICAO").Range.Column).Hidden = True
End Sub

Private Function ReadBaseAirportSelections(ByVal tblBase As ListObject, ByVal tblAirports As ListObject) As Object
    Dim selections As Object
    Dim rowIndex As Long
    Dim icao As String
    Dim airportName As String
    Dim baseValue As Variant
    Dim legacyBaseCol As Long
    Dim airportIcaoCol As Long
    Dim airportBaseValue As Variant

    Set selections = CreateObject("Scripting.Dictionary")
    selections.CompareMode = 1

    If Not tblBase Is Nothing Then
        If Not tblBase.DataBodyRange Is Nothing Then
            For rowIndex = 1 To tblBase.DataBodyRange.Rows.Count
                icao = UCase$(Trim$(CStr(tblBase.DataBodyRange.Cells(rowIndex, tblBase.ListColumns("ICAO").Index).Value)))
                If icao = "" Then
                    airportName = Trim$(CStr(tblBase.DataBodyRange.Cells(rowIndex, tblBase.ListColumns("Airport").Index).Value))
                    icao = AirportIcaoForName(tblAirports, airportName)
                End If

                If icao <> "" Then
                    baseValue = tblBase.DataBodyRange.Cells(rowIndex, tblBase.ListColumns("Base").Index).Value
                    selections(icao) = AirportBooleanValue(baseValue)
                End If
            Next rowIndex
        End If
    End If

    If AirportTableColumnExists(tblAirports, "Base") And Not tblAirports.DataBodyRange Is Nothing Then
        legacyBaseCol = tblAirports.ListColumns("Base").Index
        airportIcaoCol = tblAirports.ListColumns("ICAO").Index
        For rowIndex = 1 To tblAirports.DataBodyRange.Rows.Count
            airportBaseValue = tblAirports.DataBodyRange.Cells(rowIndex, legacyBaseCol).Value
            If AirportBooleanValue(airportBaseValue) Then
                icao = UCase$(Trim$(CStr(tblAirports.DataBodyRange.Cells(rowIndex, airportIcaoCol).Value)))
                If icao <> "" And Not selections.Exists(icao) Then selections(icao) = True
            End If
        Next rowIndex
    End If

    Set ReadBaseAirportSelections = selections
End Function

Private Function BuildEndpointAirportStats(ByVal tblLog As ListObject, ByVal aliasLookup As Object) As Object
    Dim stats As Object
    Dim rowMatches As Object
    Dim rowIndex As Long
    Dim fromText As String
    Dim toText As String
    Dim icao As String
    Dim key As Variant

    Set stats = CreateObject("Scripting.Dictionary")
    stats.CompareMode = 1
    If tblLog.DataBodyRange Is Nothing Then
        Set BuildEndpointAirportStats = stats
        Exit Function
    End If
    If Not AirportTableColumnExists(tblLog, "From") Or Not AirportTableColumnExists(tblLog, "To") Then
        Set BuildEndpointAirportStats = stats
        Exit Function
    End If

    For rowIndex = 1 To tblLog.DataBodyRange.Rows.Count
        If AirportStatsLogbookRowIsSimOnly(tblLog, rowIndex) Then GoTo NextRow

        Set rowMatches = CreateObject("Scripting.Dictionary")
        rowMatches.CompareMode = 1

        fromText = UCase$(Trim$(CStr(tblLog.DataBodyRange.Cells(rowIndex, tblLog.ListColumns("From").Index).Value)))
        toText = UCase$(Trim$(CStr(tblLog.DataBodyRange.Cells(rowIndex, tblLog.ListColumns("To").Index).Value)))

        If fromText <> "" And aliasLookup.Exists(fromText) Then rowMatches(CStr(aliasLookup(fromText))) = True
        If toText <> "" And aliasLookup.Exists(toText) Then rowMatches(CStr(aliasLookup(toText))) = True

        For Each key In rowMatches.Keys
            icao = CStr(key)
            If stats.Exists(icao) Then
                stats(icao) = CLng(stats(icao)) + 1
            Else
                stats.Add icao, 1&
            End If
        Next key

NextRow:
    Next rowIndex

    Set BuildEndpointAirportStats = stats
End Function

Private Function TopEndpointAirportIcaos(ByVal endpointStats As Object, ByVal maxCount As Long) As Variant
    Dim keys As Collection
    Dim key As Variant
    Dim sortedKeys() As String
    Dim result() As String
    Dim index As Long
    Dim resultCount As Long

    Set keys = New Collection
    For Each key In endpointStats.Keys
        If CLng(endpointStats(key)) > 0 Then keys.Add CStr(key)
    Next key

    If keys.Count = 0 Then
        TopEndpointAirportIcaos = Array()
        Exit Function
    End If

    ReDim sortedKeys(0 To keys.Count - 1)
    For index = 1 To keys.Count
        sortedKeys(index - 1) = CStr(keys(index))
    Next index

    If UBound(sortedKeys) > LBound(sortedKeys) Then QuickSortEndpointAirportRanks sortedKeys, endpointStats, LBound(sortedKeys), UBound(sortedKeys)

    resultCount = WorksheetFunction.Min(maxCount, UBound(sortedKeys) - LBound(sortedKeys) + 1)
    ReDim result(0 To resultCount - 1)
    For index = 0 To resultCount - 1
        result(index) = sortedKeys(index)
    Next index

    TopEndpointAirportIcaos = result
End Function

Private Sub QuickSortEndpointAirportRanks(ByRef values() As String, ByVal endpointStats As Object, ByVal first As Long, ByVal last As Long)
    Dim low As Long
    Dim high As Long
    Dim pivot As String
    Dim temp As String

    low = first
    high = last
    pivot = values((first + last) \ 2)

    Do While low <= high
        Do While EndpointAirportRankComesBefore(values(low), pivot, endpointStats)
            low = low + 1
        Loop
        Do While EndpointAirportRankComesBefore(pivot, values(high), endpointStats)
            high = high - 1
        Loop
        If low <= high Then
            temp = values(low)
            values(low) = values(high)
            values(high) = temp
            low = low + 1
            high = high - 1
        End If
    Loop

    If first < high Then QuickSortEndpointAirportRanks values, endpointStats, first, high
    If low < last Then QuickSortEndpointAirportRanks values, endpointStats, low, last
End Sub

Private Function EndpointAirportRankComesBefore(ByVal leftIcao As String, ByVal rightIcao As String, ByVal endpointStats As Object) As Boolean
    Dim leftVisits As Long
    Dim rightVisits As Long

    leftVisits = CLng(endpointStats(leftIcao))
    rightVisits = CLng(endpointStats(rightIcao))

    If leftVisits <> rightVisits Then
        EndpointAirportRankComesBefore = (leftVisits > rightVisits)
    Else
        EndpointAirportRankComesBefore = (StrComp(leftIcao, rightIcao, vbTextCompare) < 0)
    End If
End Function

Private Sub ResizeBaseAirportsTable(ByVal tbl As ListObject, ByVal rows As Long)
    Dim currentRows As Long

    If tbl.DataBodyRange Is Nothing Then
        currentRows = 0
    Else
        currentRows = tbl.DataBodyRange.Rows.Count
    End If

    If currentRows = 0 Then
        tbl.ListRows.Add
        currentRows = 1
    End If

    If currentRows <> rows Then tbl.Resize tbl.Range.Resize(rows + 1, tbl.ListColumns.Count)
End Sub

Private Sub ApplyBaseAirportCheckboxes(ByVal tbl As ListObject)
    On Error Resume Next
    tbl.ListColumns("Base").DataBodyRange.CellControl.SetCheckbox
    Err.Clear
    On Error GoTo 0
End Sub

Private Function FindWorkbookTable(ByVal wb As Workbook, ByVal tableName As String) As ListObject
    Dim ws As Worksheet

    On Error Resume Next
    For Each ws In wb.Worksheets
        Set FindWorkbookTable = ws.ListObjects(tableName)
        If Not FindWorkbookTable Is Nothing Then Exit Function
    Next ws
    On Error GoTo 0
End Function

Private Function AirportTableColumnExists(ByVal tbl As ListObject, ByVal columnName As String) As Boolean
    On Error Resume Next
    AirportTableColumnExists = Not tbl.ListColumns(columnName) Is Nothing
    On Error GoTo 0
End Function

Private Function AirportIcaoForName(ByVal tblAirports As ListObject, ByVal airportName As String) As String
    Dim rowIndex As Long

    If airportName = "" Or tblAirports.DataBodyRange Is Nothing Then Exit Function

    For rowIndex = 1 To tblAirports.DataBodyRange.Rows.Count
        If StrComp(Trim$(CStr(tblAirports.DataBodyRange.Cells(rowIndex, tblAirports.ListColumns("Airport").Index).Value)), _
                   airportName, vbTextCompare) = 0 Then
            AirportIcaoForName = UCase$(Trim$(CStr(tblAirports.DataBodyRange.Cells(rowIndex, tblAirports.ListColumns("ICAO").Index).Value)))
            Exit Function
        End If
    Next rowIndex
End Function

Private Function AirportBooleanValue(ByVal value As Variant) As Boolean
    If VarType(value) = vbBoolean Then
        AirportBooleanValue = CBool(value)
    ElseIf IsNumeric(value) Then
        AirportBooleanValue = (CDbl(value) <> 0)
    Else
        Select Case LCase$(Trim$(CStr(value)))
            Case "true", "yes", "y", "1", "x"
                AirportBooleanValue = True
        End Select
    End If
End Function

Private Function AirportStatsLogbookRowIsSimOnly(ByVal tbl As ListObject, ByVal rowIndex As Long) As Boolean
    Dim simHours As Double
    Dim otherHours As Double
    Dim firstHourCol As Long
    Dim lastOtherHourCol As Long
    Dim colIndex As Long

    simHours = Val(tbl.ListColumns("IfrSim").DataBodyRange.Cells(rowIndex, 1).Value)
    firstHourCol = tbl.ListColumns("SeIcusDay").Index
    lastOtherHourCol = tbl.ListColumns("IfrIf").Index

    For colIndex = firstHourCol To lastOtherHourCol
        If IsNumeric(tbl.DataBodyRange.Cells(rowIndex, colIndex).Value) Then
            otherHours = otherHours + CDbl(tbl.DataBodyRange.Cells(rowIndex, colIndex).Value)
        End If
    Next colIndex

    AirportStatsLogbookRowIsSimOnly = (simHours > 0 And otherHours = 0)
End Function

Private Function AirportStatsIgnoreToken(ByVal token As String) As Boolean
    Select Case UCase$(Trim$(token))
        Case "IPC", "OPC", "FR", "IR", "IFR", "VFR", "TEST", "CHECK", "CIRCLING", "SIM"
            AirportStatsIgnoreToken = True
        Case Else
            AirportStatsIgnoreToken = AirportStatsKeywordTableContainsToken(token)
    End Select
End Function

Private Function AirportStatsKeywordTableContainsToken(ByVal token As String) As Boolean
    Dim tblKeywords As ListObject
    Dim keywordColumn As ListColumn
    Dim keywordCell As Range
    Dim normalisedToken As String

    Set tblKeywords = FindAirportStatsKeywordTable()
    If tblKeywords Is Nothing Then Exit Function

    normalisedToken = AirportStatsNormaliseKeywordText(token)
    For Each keywordColumn In tblKeywords.ListColumns
        If Not keywordColumn.DataBodyRange Is Nothing Then
            For Each keywordCell In keywordColumn.DataBodyRange.Cells
                If Not IsError(keywordCell.Value) Then
                    If Trim$(CStr(keywordCell.Value)) <> "" Then
                        If InStr(1, AirportStatsNormaliseKeywordText(CStr(keywordCell.Value)), _
                                   normalisedToken, vbBinaryCompare) > 0 Then
                            AirportStatsKeywordTableContainsToken = True
                            Exit Function
                        End If
                    End If
                End If
            Next keywordCell
        End If
    Next keywordColumn
End Function

Private Function FindAirportStatsKeywordTable() As ListObject
    Dim ws As Worksheet

    For Each ws In ThisWorkbook.Worksheets
        On Error Resume Next
        Set FindAirportStatsKeywordTable = ws.ListObjects("Keywords")
        On Error GoTo 0
        If Not FindAirportStatsKeywordTable Is Nothing Then Exit Function
    Next ws
End Function

Private Function AirportStatsNormaliseKeywordText(ByVal value As String) As String
    AirportStatsNormaliseKeywordText = UCase$(Trim$(value))
End Function

Public Function AirportDatasetRoutesStateNeedsRefresh(ByVal wb As Workbook) As Boolean
    Dim datasetVersion As String
    Dim routesVersion As String

    datasetVersion = ReadWorkbookNameText(wb, "AirportDatasetVersion", "")
    If datasetVersion = "" Then Exit Function

    routesVersion = ReadWorkbookNameText(wb, "AirportDatasetRoutesVersion", "")
    AirportDatasetRoutesStateNeedsRefresh = (StrComp(datasetVersion, routesVersion, vbTextCompare) <> 0)
End Function

Public Sub MarkAirportDatasetRoutesStateCurrent(ByVal wb As Workbook)
    Dim datasetVersion As String

    datasetVersion = ReadWorkbookNameText(wb, "AirportDatasetVersion", "")
    If datasetVersion <> "" Then WriteWorkbookNameText wb, "AirportDatasetRoutesVersion", datasetVersion
End Sub

Private Function CurrentAiracCycleKey(Optional ByVal asOfDate As Date = 0) As String
    Dim cycleIndex As Long
    Dim cycleStart As Date

    If asOfDate = 0 Then asOfDate = Date
    cycleIndex = CLng(Int(DateDiff("d", AIRAC_EPOCH_DATE, asOfDate) / AIRAC_CYCLE_DAYS))
    cycleStart = DateAdd("d", cycleIndex * AIRAC_CYCLE_DAYS, AIRAC_EPOCH_DATE)
    CurrentAiracCycleKey = Format$(cycleStart, "yyyy-mm-dd")
End Function

Private Function FetchAirportDatasetVersion() As String
    Dim http As Object
    Dim version As String

    Set http = CreateObject("MSXML2.XMLHTTP")
    http.Open "HEAD", AIRPORT_DATA_URL, False
    http.setRequestHeader "Cache-Control", "no-cache"
    http.setRequestHeader "Pragma", "no-cache"
    http.setRequestHeader "User-Agent", "Electronic-Logbook-Airport-Updater"
    http.send

    If http.Status < 200 Or http.Status >= 300 Then
        Err.Raise 5, "FetchAirportDatasetVersion", "Could not check the airport dataset. HTTP status " & http.Status & "."
    End If

    version = Trim$(CStr(http.getResponseHeader("ETag")))
    If version = "" Then version = Trim$(CStr(http.getResponseHeader("Last-Modified")))
    If version = "" Then version = Trim$(CStr(http.getResponseHeader("Content-Length")))
    FetchAirportDatasetVersion = version
End Function

Private Function DownloadAirportDataset() As String
    Dim http As Object

    Set http = CreateObject("MSXML2.XMLHTTP")
    http.Open "GET", AIRPORT_DATA_URL, False
    http.setRequestHeader "Cache-Control", "no-cache"
    http.setRequestHeader "Pragma", "no-cache"
    http.setRequestHeader "User-Agent", "Electronic-Logbook-Airport-Updater"
    http.send

    If http.Status < 200 Or http.Status >= 300 Then
        Err.Raise 5, "DownloadAirportDataset", "Could not download the airport dataset. HTTP status " & http.Status & "."
    End If

    DownloadAirportDataset = CStr(http.responseText)
    If Len(DownloadAirportDataset) < 100000 Then
        Err.Raise 5, "DownloadAirportDataset", "The downloaded airport dataset was unexpectedly small."
    End If
End Function

Private Function ParseAirportDataset(ByVal csvText As String) As Object
    Dim records As Object
    Dim lines As Variant
    Dim headers As Variant
    Dim headerMap As Object
    Dim i As Long
    Dim fields As Variant
    Dim icao As String
    Dim airportType As String
    Dim scheduledService As String
    Dim record As Variant

    Set records = CreateObject("Scripting.Dictionary")
    records.CompareMode = 1

    csvText = Replace(csvText, vbCrLf, vbLf)
    csvText = Replace(csvText, vbCr, vbLf)
    lines = Split(csvText, vbLf)
    If UBound(lines) < 2 Then Err.Raise 5, "ParseAirportDataset", "The airport dataset did not contain any rows."

    headers = ParseCsvLine(CStr(lines(0)))
    Set headerMap = BuildHeaderMap(headers)
    ValidateAirportHeaders headerMap

    For i = 1 To UBound(lines)
        If Trim$(CStr(lines(i))) = "" Then GoTo NextLine

        fields = ParseCsvLine(CStr(lines(i)))
        airportType = LCase$(FieldByName(fields, headerMap, "type"))
        scheduledService = LCase$(FieldByName(fields, headerMap, "scheduled_service"))

        If Not ShouldImportAirportType(airportType, scheduledService) Then GoTo NextLine

        icao = ResolveAirportIcao(fields, headerMap)
        If icao = "" Then GoTo NextLine

        record = BuildAirportRecord(icao, fields, headerMap)
        If Not records.Exists(icao) Then records.Add icao, record

NextLine:
    Next i

    If records.Count < 5000 Then
        Err.Raise 5, "ParseAirportDataset", "The airport dataset contained fewer airports than expected after filtering."
    End If

    Set ParseAirportDataset = records
End Function

Private Function ShouldImportAirportType(ByVal airportType As String, ByVal scheduledService As String) As Boolean
    Select Case airportType
        Case "large_airport", "medium_airport"
            ShouldImportAirportType = True
        Case "small_airport"
            ShouldImportAirportType = (scheduledService = "yes")
    End Select
End Function

Private Function ResolveAirportIcao(ByVal fields As Variant, ByVal headerMap As Object) As String
    Dim candidate As String

    candidate = UCase$(Trim$(FieldByName(fields, headerMap, "icao_code")))
    If Len(candidate) = 4 Then
        ResolveAirportIcao = candidate
        Exit Function
    End If

    candidate = UCase$(Trim$(FieldByName(fields, headerMap, "gps_code")))
    If Len(candidate) = 4 Then
        ResolveAirportIcao = candidate
        Exit Function
    End If

    candidate = UCase$(Trim$(FieldByName(fields, headerMap, "ident")))
    If Len(candidate) = 4 Then ResolveAirportIcao = candidate
End Function

Private Function BuildAirportRecord(ByVal icao As String, ByVal fields As Variant, ByVal headerMap As Object) As Variant
    Dim nameText As String
    Dim latitude As String
    Dim longitude As String
    Dim threeCode As String
    Dim twoCode As String

    nameText = NormaliseAirportName(Trim$(FieldByName(fields, headerMap, "name")))
    latitude = Trim$(FieldByName(fields, headerMap, "latitude_deg"))
    longitude = Trim$(FieldByName(fields, headerMap, "longitude_deg"))

    If nameText = "" Or Not IsNumeric(latitude) Or Not IsNumeric(longitude) Then
        Err.Raise 5, "BuildAirportRecord", "Airport " & icao & " has invalid required data."
    End If

    BuildAirportRecord = Array(icao, nameText, CDbl(latitude), CDbl(longitude), threeCode, twoCode, "")
End Function

Private Function NormaliseAirportName(ByVal airportName As String) As String
    Dim suffix As Variant
    Dim candidate As String

    airportName = Trim$(airportName)
    For Each suffix In Array("International Airport", "Air Field", "Air Port", _
                             "Air Base", "Aerodrome", "Airfield", "Airstrip", _
                             "Airport", "Runway")
        candidate = RemoveAirportNameSuffix(airportName, CStr(suffix))
        If candidate <> airportName Then
            NormaliseAirportName = candidate
            Exit Function
        End If
    Next suffix

    NormaliseAirportName = airportName
End Function

Private Function RemoveAirportNameSuffix(ByVal airportName As String, ByVal suffix As String) As String
    Dim suffixStart As Long
    Dim candidate As String

    If Len(airportName) <= Len(suffix) Then
        RemoveAirportNameSuffix = airportName
        Exit Function
    End If

    suffixStart = Len(airportName) - Len(suffix) + 1
    If StrComp(Mid$(airportName, suffixStart), suffix, vbTextCompare) <> 0 Then
        RemoveAirportNameSuffix = airportName
        Exit Function
    End If

    If Mid$(airportName, suffixStart - 1, 1) <> " " Then
        RemoveAirportNameSuffix = airportName
        Exit Function
    End If

    candidate = Trim$(Left$(airportName, suffixStart - 2))
    If candidate = "" Then
        RemoveAirportNameSuffix = airportName
    Else
        RemoveAirportNameSuffix = candidate
    End If
End Function

Private Function MergeAirportRecords(ByVal tbl As ListObject, ByVal remoteRecords As Object) As Object
    Dim merged As Object
    Dim currentRecords As Object
    Dim key As Variant
    Dim currentRecord As Variant
    Dim remoteRecord As Variant

    Set merged = CreateObject("Scripting.Dictionary")
    merged.CompareMode = 1
    Set currentRecords = ReadCurrentAirportRecords(tbl)

    For Each key In remoteRecords.Keys
        remoteRecord = remoteRecords(key)
        If currentRecords.Exists(CStr(key)) Then
            currentRecord = currentRecords(CStr(key))
            remoteRecord(1) = PreferNonBlank(currentRecord(1), remoteRecord(1))
            remoteRecord(4) = PreferNonBlank(remoteRecord(4), currentRecord(4))
            remoteRecord(5) = PreferNonBlank(remoteRecord(5), currentRecord(5))
        End If
        merged.Add CStr(key), remoteRecord
    Next key

    For Each key In currentRecords.Keys
        If Not merged.Exists(CStr(key)) Then merged.Add CStr(key), currentRecords(CStr(key))
    Next key

    Set MergeAirportRecords = merged
End Function

Private Function ReadCurrentAirportRecords(ByVal tbl As ListObject) As Object
    Dim records As Object
    Dim rowIndex As Long
    Dim icao As String
    Dim record As Variant
    Dim icaoCol As Long
    Dim airportCol As Long
    Dim latCol As Long
    Dim lonCol As Long
    Dim threeCol As Long
    Dim twoCol As Long

    Set records = CreateObject("Scripting.Dictionary")
    records.CompareMode = 1
    If tbl.DataBodyRange Is Nothing Then
        Set ReadCurrentAirportRecords = records
        Exit Function
    End If

    icaoCol = tbl.ListColumns("ICAO").Index
    airportCol = tbl.ListColumns("Airport").Index
    latCol = tbl.ListColumns("Latitude").Index
    lonCol = tbl.ListColumns("Longitude").Index
    threeCol = tbl.ListColumns("Three").Index
    twoCol = tbl.ListColumns("Two").Index

    For rowIndex = 1 To tbl.DataBodyRange.Rows.Count
        icao = UCase$(Trim$(CStr(tbl.DataBodyRange.Cells(rowIndex, icaoCol).Value)))
        If icao <> "" Then
            record = Array( _
                icao, _
                Trim$(CStr(tbl.DataBodyRange.Cells(rowIndex, airportCol).Value)), _
                tbl.DataBodyRange.Cells(rowIndex, latCol).Value, _
                tbl.DataBodyRange.Cells(rowIndex, lonCol).Value, _
                UCase$(Trim$(CStr(tbl.DataBodyRange.Cells(rowIndex, threeCol).Value))), _
                UCase$(Trim$(CStr(tbl.DataBodyRange.Cells(rowIndex, twoCol).Value))))
            If Not records.Exists(icao) Then records.Add icao, record
        End If
    Next rowIndex

    Set ReadCurrentAirportRecords = records
End Function

Private Sub ReplaceAirportTable(ByVal tbl As ListObject, ByVal records As Object, ByVal sortedKeys As Variant)
    Dim formulaColumns As Object
    Dim rowCount As Long
    Dim targetRange As Range
    Dim values As Variant
    Dim rowIndex As Long
    Dim record As Variant
    Dim key As Variant
    Dim columnName As Variant
    Dim aliasCells As Object

    rowCount = UBound(sortedKeys) - LBound(sortedKeys) + 1
    If rowCount < 1 Then Err.Raise 5, "ReplaceAirportTable", "No airport rows were available to write."

    Set formulaColumns = CaptureAirportFormulaColumns(tbl)
    Set aliasCells = CaptureAirportAliasCells(tbl)
    Set targetRange = tbl.Range.Resize(rowCount + 1, tbl.ListColumns.Count)
    tbl.Resize targetRange

    ReDim values(1 To rowCount, 1 To 6)
    rowIndex = 1
    For Each key In sortedKeys
        record = records(CStr(key))
        values(rowIndex, 1) = record(0)
        values(rowIndex, 2) = record(1)
        values(rowIndex, 3) = record(2)
        values(rowIndex, 4) = record(3)
        values(rowIndex, 5) = record(4)
        values(rowIndex, 6) = record(5)
        rowIndex = rowIndex + 1
    Next key

    WriteAirportColumn tbl, "ICAO", values, 1
    WriteAirportColumn tbl, "Airport", values, 2
    WriteAirportColumn tbl, "Latitude", values, 3
    WriteAirportColumn tbl, "Longitude", values, 4
    ApplyAirportAliases tbl, aliasCells

    For Each columnName In formulaColumns.Keys
        ApplyAirportColumnFormula tbl, CStr(columnName), CStr(formulaColumns(columnName))
    Next columnName
End Sub

Private Function CaptureAirportAliasCells(ByVal tbl As ListObject) As Object
    Dim aliases As Object
    Dim rowIndex As Long
    Dim icao As String
    Dim icaoCol As Long
    Dim threeCol As Long
    Dim twoCol As Long
    Dim threeCell As Range
    Dim twoCell As Range

    Set aliases = CreateObject("Scripting.Dictionary")
    aliases.CompareMode = 1
    If tbl.DataBodyRange Is Nothing Then
        Set CaptureAirportAliasCells = aliases
        Exit Function
    End If

    icaoCol = tbl.ListColumns("ICAO").Index
    threeCol = tbl.ListColumns("Three").Index
    twoCol = tbl.ListColumns("Two").Index

    For rowIndex = 1 To tbl.DataBodyRange.Rows.Count
        icao = UCase$(Trim$(CStr(tbl.DataBodyRange.Cells(rowIndex, icaoCol).Value)))
        If icao <> "" Then
            Set threeCell = tbl.DataBodyRange.Cells(rowIndex, threeCol)
            Set twoCell = tbl.DataBodyRange.Cells(rowIndex, twoCol)
            aliases(icao) = Array( _
                threeCell.HasFormula, threeCell.Formula, threeCell.Value, _
                twoCell.HasFormula, twoCell.Formula, twoCell.Value)
        End If
    Next rowIndex

    Set CaptureAirportAliasCells = aliases
End Function

Private Sub ApplyAirportAliases(ByVal tbl As ListObject, ByVal aliasCells As Object)
    Dim rowIndex As Long
    Dim icao As String
    Dim savedAlias As Variant
    Dim icaoCol As Long
    Dim threeCol As Long
    Dim twoCol As Long

    icaoCol = tbl.ListColumns("ICAO").Index
    threeCol = tbl.ListColumns("Three").Index
    twoCol = tbl.ListColumns("Two").Index

    tbl.ListColumns("Three").DataBodyRange.ClearContents
    tbl.ListColumns("Two").DataBodyRange.ClearContents

    For rowIndex = 1 To tbl.DataBodyRange.Rows.Count
        icao = UCase$(Trim$(CStr(tbl.DataBodyRange.Cells(rowIndex, icaoCol).Value)))
        If Left$(icao, 1) <> "Y" Then GoTo NextRow

        If aliasCells.Exists(icao) Then
            savedAlias = aliasCells(icao)
            If CBool(savedAlias(0)) Then
                tbl.DataBodyRange.Cells(rowIndex, threeCol).Formula = CStr(savedAlias(1))
            Else
                tbl.DataBodyRange.Cells(rowIndex, threeCol).Value = savedAlias(2)
            End If

            If CBool(savedAlias(3)) Then
                tbl.DataBodyRange.Cells(rowIndex, twoCol).Formula = CStr(savedAlias(4))
            Else
                tbl.DataBodyRange.Cells(rowIndex, twoCol).Value = savedAlias(5)
            End If
        Else
            tbl.DataBodyRange.Cells(rowIndex, threeCol).Formula = "=RIGHT([@ICAO],3)"
            tbl.DataBodyRange.Cells(rowIndex, twoCol).Formula = _
                "=IF(OR(LEFT([@ICAO],2)=""YM"",LEFT([@ICAO],2)=""YP"",LEFT([@ICAO],2)=""YB"",LEFT([@ICAO],2)=""YS""),RIGHT([@ICAO],2),"""")"
        End If

NextRow:
    Next rowIndex

    ClearNonAustralianAirportAliases tbl
End Sub

Private Sub ClearNonAustralianAirportAliases(ByVal tbl As ListObject)
    Dim rowIndex As Long
    Dim icao As String
    Dim icaoCol As Long
    Dim threeCol As Long
    Dim twoCol As Long

    icaoCol = tbl.ListColumns("ICAO").Index
    threeCol = tbl.ListColumns("Three").Index
    twoCol = tbl.ListColumns("Two").Index

    For rowIndex = 1 To tbl.DataBodyRange.Rows.Count
        icao = UCase$(Trim$(CStr(tbl.DataBodyRange.Cells(rowIndex, icaoCol).Value)))
        If Left$(icao, 1) <> "Y" Then
            tbl.DataBodyRange.Cells(rowIndex, threeCol).ClearContents
            tbl.DataBodyRange.Cells(rowIndex, twoCol).ClearContents
        End If
    Next rowIndex
End Sub

Private Function CaptureAirportFormulaColumns(ByVal tbl As ListObject) As Object
    Dim formulas As Object
    Dim columnName As Variant
    Dim cell As Range

    Set formulas = CreateObject("Scripting.Dictionary")
    For Each columnName In Array("First Visited", "Last Visited", "Visits", "Rank")
        Set cell = tbl.ListColumns(CStr(columnName)).DataBodyRange.Cells(1, 1)
        If cell.HasFormula Then formulas.Add CStr(columnName), cell.Formula2
    Next columnName
    Set CaptureAirportFormulaColumns = formulas
End Function

Private Sub WriteAirportColumn(ByVal tbl As ListObject, ByVal columnName As String, ByVal values As Variant, ByVal valueIndex As Long)
    Dim rowCount As Long
    Dim columnValues As Variant
    Dim rowIndex As Long

    rowCount = UBound(values, 1)
    ReDim columnValues(1 To rowCount, 1 To 1)
    For rowIndex = 1 To rowCount
        columnValues(rowIndex, 1) = values(rowIndex, valueIndex)
    Next rowIndex

    tbl.ListColumns(columnName).DataBodyRange.Value = columnValues
End Sub

Private Sub ApplyAirportColumnFormula(ByVal tbl As ListObject, ByVal columnName As String, ByVal formulaText As String)
    On Error GoTo FormulaFallback
    tbl.ListColumns(columnName).DataBodyRange.Formula2 = formulaText
    Exit Sub

FormulaFallback:
    Err.Clear
    tbl.ListColumns(columnName).DataBodyRange.Formula = formulaText
End Sub

Private Function AirportTableSignature(ByVal tbl As ListObject) As String
    Dim records As Object
    Dim sortedKeys As Variant

    Set records = ReadCurrentAirportRecords(tbl)
    If records.Count = 0 Then Exit Function
    sortedKeys = SortedDictionaryKeys(records)
    AirportTableSignature = AirportRecordsSignature(records, sortedKeys)
End Function

Private Function AirportRecordsSignature(ByVal records As Object, ByVal sortedKeys As Variant) As String
    Dim parts As Collection
    Dim key As Variant
    Dim record As Variant
    Dim builder As String

    Set parts = New Collection
    For Each key In sortedKeys
        record = records(CStr(key))
        parts.Add CStr(record(0)) & Chr$(30) & _
                  CStr(record(1)) & Chr$(30) & _
                  NormaliseCoordinate(record(2)) & Chr$(30) & _
                  NormaliseCoordinate(record(3)) & Chr$(30) & _
                  CStr(record(4)) & Chr$(30) & _
                  CStr(record(5))
    Next key

    builder = JoinCollection(parts, Chr$(31))
    AirportRecordsSignature = CStr(records.Count) & Chr$(31) & builder
End Function

Private Function NormaliseCoordinate(ByVal value As Variant) As String
    If IsNumeric(value) Then
        NormaliseCoordinate = Format$(CDbl(value), "0.000000")
    Else
        NormaliseCoordinate = Trim$(CStr(value))
    End If
End Function

Private Function ParseCsvLine(ByVal lineText As String) As Variant
    Dim fields As Collection
    Dim current As String
    Dim inQuotes As Boolean
    Dim i As Long
    Dim ch As String
    Dim result As Variant

    Set fields = New Collection
    For i = 1 To Len(lineText)
        ch = Mid$(lineText, i, 1)
        If ch = """" Then
            If inQuotes And i < Len(lineText) And Mid$(lineText, i + 1, 1) = """" Then
                current = current & """"
                i = i + 1
            Else
                inQuotes = Not inQuotes
            End If
        ElseIf ch = "," And Not inQuotes Then
            fields.Add current
            current = ""
        Else
            current = current & ch
        End If
    Next i
    fields.Add current

    ReDim result(0 To fields.Count - 1)
    For i = 1 To fields.Count
        result(i - 1) = fields(i)
    Next i
    ParseCsvLine = result
End Function

Private Function BuildHeaderMap(ByVal headers As Variant) As Object
    Dim map As Object
    Dim i As Long

    Set map = CreateObject("Scripting.Dictionary")
    map.CompareMode = 1
    For i = LBound(headers) To UBound(headers)
        map(Trim$(CStr(headers(i)))) = i
    Next i
    Set BuildHeaderMap = map
End Function

Private Sub ValidateAirportHeaders(ByVal headerMap As Object)
    Dim headerName As Variant

    For Each headerName In Array("ident", "type", "name", "latitude_deg", "longitude_deg", _
                                 "scheduled_service", "icao_code", "iata_code", "gps_code", "local_code")
        If Not headerMap.Exists(CStr(headerName)) Then
            Err.Raise 5, "ValidateAirportHeaders", "The airport dataset is missing required column '" & CStr(headerName) & "'."
        End If
    Next headerName
End Sub

Private Function FieldByName(ByVal fields As Variant, ByVal headerMap As Object, ByVal headerName As String) As String
    Dim index As Long

    If Not headerMap.Exists(headerName) Then Exit Function
    index = CLng(headerMap(headerName))
    If index < LBound(fields) Or index > UBound(fields) Then Exit Function
    FieldByName = CStr(fields(index))
End Function

Private Function SortedDictionaryKeys(ByVal dict As Object) As Variant
    Dim keys As Variant

    keys = dict.Keys
    If dict.Count > 1 Then QuickSortStrings keys, LBound(keys), UBound(keys)
    SortedDictionaryKeys = keys
End Function

Private Sub QuickSortStrings(ByRef values As Variant, ByVal first As Long, ByVal last As Long)
    Dim low As Long
    Dim high As Long
    Dim pivot As String
    Dim temp As Variant

    low = first
    high = last
    pivot = CStr(values((first + last) \ 2))

    Do While low <= high
        Do While CStr(values(low)) < pivot
            low = low + 1
        Loop
        Do While CStr(values(high)) > pivot
            high = high - 1
        Loop
        If low <= high Then
            temp = values(low)
            values(low) = values(high)
            values(high) = temp
            low = low + 1
            high = high - 1
        End If
    Loop

    If first < high Then QuickSortStrings values, first, high
    If low < last Then QuickSortStrings values, low, last
End Sub

Private Function PreferNonBlank(ByVal preferred As Variant, ByVal fallback As Variant) As Variant
    If Trim$(CStr(preferred)) <> "" Then
        PreferNonBlank = preferred
    Else
        PreferNonBlank = fallback
    End If
End Function

Private Function JoinCollection(ByVal values As Collection, ByVal delimiter As String) As String
    Dim parts() As String
    Dim i As Long

    If values.Count = 0 Then Exit Function
    ReDim parts(1 To values.Count)
    For i = 1 To values.Count
        parts(i) = CStr(values(i))
    Next i
    JoinCollection = Join(parts, delimiter)
End Function

Private Function ReadWorkbookNameText(ByVal wb As Workbook, ByVal nameText As String, ByVal defaultValue As String) As String
    Dim value As Variant

    On Error GoTo Fail
    value = wb.Application.Evaluate(wb.Names(nameText).RefersTo)
    If IsError(value) Then GoTo Fail
    ReadWorkbookNameText = CStr(value)
    Exit Function

Fail:
    ReadWorkbookNameText = defaultValue
End Function

Private Sub WriteWorkbookNameText(ByVal wb As Workbook, ByVal nameText As String, ByVal value As String)
    Dim refersTo As String

    refersTo = "=""" & Replace(value, """", """""") & """"
    On Error GoTo AddName
    wb.Names(nameText).RefersTo = refersTo
    Exit Sub

AddName:
    Err.Clear
    wb.Names.Add Name:=nameText, RefersTo:=refersTo
End Sub
