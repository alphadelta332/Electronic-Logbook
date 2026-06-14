Attribute VB_Name = "modUpdate"
' ==============================================================
' modUpdate - Auto-update system for Electronic Logbook
' ==============================================================

Option Explicit

Private mResolvedRef As String
Private Const ROUTE_CACHE_DEFINITION_VERSION As Long = 1

' -- GITHUB CONFIG --------------------------------------------
Private Const GITHUB_USER  As String = "alphadelta332"
Private Const GITHUB_REPO  As String = "Electronic-Logbook"
Private Const MASTER_FILE  As String = "Electronic_Logbook_Master.xlsm"
' -------------------------------------------------------------

' ==============================================================
' PUBLIC ENTRY POINTS
' ==============================================================

Public Sub CheckForUpdate()
    On Error GoTo Fail
    Dim remoteVer As String
    Dim localVer  As String
    Dim msg       As String

    remoteVer = FetchRemoteVersion()
    If remoteVer = "" Then Exit Sub

    localVer = GetLocalVersion()

    If IsNewerVersion(remoteVer, localVer) Then
        msg = "A new version of the Electronic Logbook is available!" & vbCrLf & vbCrLf & _
              "  Your version:  " & localVer & vbCrLf & _
              "  New version:   " & remoteVer & vbCrLf & vbCrLf & _
              "Update now? Your flight data will not be affected."
        If MsgBox(msg, vbYesNo + vbInformation, "Logbook Update Available") = vbYes Then
            RunUpdate remoteVer
        End If
    End If
    Exit Sub
Fail:
End Sub

Public Sub CheckForUpdateManual()
    Dim remoteVer As String
    Dim localVer  As String
    Dim msg       As String

    remoteVer = FetchRemoteVersion()
    If remoteVer = "" Then
        MsgBox "Could not reach GitHub. Check your internet connection.", _
               vbExclamation, "No Connection"
        Exit Sub
    End If

    localVer = GetLocalVersion()

    If IsNewerVersion(remoteVer, localVer) Then
        msg = "A new version is available!" & vbCrLf & vbCrLf & _
              "  Your version:  " & localVer & vbCrLf & _
              "  New version:   " & remoteVer & vbCrLf & vbCrLf & _
              "Update now? Your flight data will not be affected."
        If MsgBox(msg, vbYesNo + vbInformation, "Update Available") = vbYes Then
            RunUpdate remoteVer
        End If
    Else
        MsgBox "You are up to date! (version " & localVer & ")", _
               vbInformation, "No Update Needed"
    End If
End Sub

' ==============================================================
' VERSION HELPERS
' ==============================================================

Public Function GetLocalVersion() As String
    On Error Resume Next
    GetLocalVersion = Trim(CStr(ThisWorkbook.Names("LogbookVersion").RefersToRange.Value))
    If GetLocalVersion = "" Or GetLocalVersion = "0" Then GetLocalVersion = "0.0"
    On Error GoTo 0
End Function

Private Function FetchRemoteVersion() As String
    Dim url  As String
    Dim http As Object
    Dim ref  As String

    ref = ResolveGitHubRef()
    mResolvedRef = ref
    url = RawURL("version.txt", ref)

    On Error GoTo Fail
    Set http = CreateObject("MSXML2.XMLHTTP")
    http.Open "GET", url, False
    http.setRequestHeader "Cache-Control", "no-cache"
    http.setRequestHeader "Pragma", "no-cache"
    http.setRequestHeader "User-Agent", "Electronic-Logbook-Updater"
    Dim token As String
    token = GetGitHubToken()
    If token <> "" Then
        http.setRequestHeader "Authorization", "token " & token
    End If
    http.send
    If http.Status <> 200 And token <> "" Then
        ' Existing workbooks may contain a stale private-repo PAT.
        ' Public repo reads should still work after retrying unauthenticated.
        Set http = CreateObject("MSXML2.XMLHTTP")
        http.Open "GET", url, False
        http.setRequestHeader "Cache-Control", "no-cache"
        http.setRequestHeader "Pragma", "no-cache"
        http.setRequestHeader "User-Agent", "Electronic-Logbook-Updater"
        http.send
    End If
    If http.Status = 200 Then
        FetchRemoteVersion = Trim(http.responseText)
    End If
    Exit Function
Fail:
    FetchRemoteVersion = ""
End Function

Private Function IsNewerVersion(remoteVer As String, localVer As String) As Boolean
    Dim rParts() As String
    Dim lParts()  As String
    Dim i         As Integer
    Dim maxLen    As Integer
    Dim rNum      As Long
    Dim lNum      As Long

    rParts = Split(remoteVer, ".")
    lParts = Split(localVer, ".")
    maxLen = IIf(UBound(rParts) > UBound(lParts), UBound(rParts), UBound(lParts))

    For i = 0 To maxLen
        rNum = IIf(i <= UBound(rParts), CLng(rParts(i)), 0)
        lNum = IIf(i <= UBound(lParts), CLng(lParts(i)), 0)
        If rNum > lNum Then
            IsNewerVersion = True
            Exit Function
        ElseIf rNum < lNum Then
            IsNewerVersion = False
            Exit Function
        End If
    Next i

    IsNewerVersion = False
End Function

' ==============================================================
' CORE UPDATE ROUTINE
' ==============================================================
' Strategy: inject user data into a clean master, then replace the
' original filename while keeping the previous workbook as *_Old.xlsm.
' Never copies sheets between workbooks - no external ref issues.
'
' Data preserved from user:
'   Logbook[Year] through Logbook[Circling]  (raw flight entries)
'   Logbook[CurrencyExclusions]               (currency detection opt-outs)
'   Airports[Base]                            (matched by ICAO)
'   Keywords table                            (user detection terms)
'   Routes table and route cache state
'
' Everything else comes from the master.

Private Sub RunUpdate(newVersion As String)
    Dim tempPath      As String
    Dim savePath      As String
    Dim localSavePath As String
    Dim localPath     As String
    Dim originalName  As String
    Dim oldPath       As String
    Dim masterWb      As Workbook
    Dim errMsg        As String
    Dim errNum        As Long
    Dim diagStep      As String
    Dim finalHandoffStarted As Boolean

    tempPath = Environ("TEMP") & "\LB_Master_Temp.xlsm"
    ' Resolve to the local folder the logbook is already in.
    ' ResolveLocalPath handles OneDrive cloud URLs by mapping them to
    ' the local sync folder, so FileCopy always targets a real FS path.
    localPath = ResolveLocalPath(ThisWorkbook)
    originalName = ThisWorkbook.Name
    savePath = localPath & "\" & originalName
    oldPath = BuildOldWorkbookPath(localPath, originalName)

    diagStep = "Downloading master workbook"
    UpdateStatus "Downloading update (version " & newVersion & ")..."
    If Not DownloadFile(RawURL(MASTER_FILE, mResolvedRef), tempPath) Then
        MsgBox "Could not download the update file." & vbCrLf & _
               "Check your internet connection and try again.", _
               vbExclamation, "Download Failed"
        UpdateStatus ""
        Exit Sub
    End If

    Application.ScreenUpdating = False
    Application.EnableEvents = False
    Application.Calculation = xlCalculationManual

    On Error GoTo UpdateFailed

    diagStep = "Opening master workbook"
    Set masterWb = Workbooks.Open(tempPath, ReadOnly:=False, UpdateLinks:=False)

    diagStep = "Copying Logbook data into master"
    UpdateStatus "Copying flight data..."
    InjectLogbookData masterWb

    diagStep = "Copying Keywords data into master"
    CopyKeywordsData masterWb

    diagStep = "Copying Routes data into master"
    UpdateStatus "Copying route cache..."
    CopyRoutesData masterWb
    CopyRouteCacheState masterWb

    diagStep = "Writing extra labels below totals row"
    EnsureExtraLabels masterWb

    diagStep = "Copying table formatting"
    CopyTableFormatting masterWb
    ApplyHiddenHourHeaderFormatting masterWb

    diagStep = "Copying totals area formatting"
    CopyTotalsFormatting masterWb
    NormalizeLogbookFormatting masterWb

    diagStep = "Updating hidden rows"
    Dim wsLog     As Worksheet
    Dim tblLog    As ListObject
    Dim lastDRow  As Long
    Set wsLog  = masterWb.Sheets("Logbook")
    Set tblLog = wsLog.ListObjects("Logbook")
    lastDRow = tblLog.DataBodyRange.Row + tblLog.DataBodyRange.Rows.Count - 1
    wsLog.Rows.Hidden = False
    If lastDRow + 4 <= wsLog.Rows.Count Then
        wsLog.Rows(lastDRow + 4 & ":" & wsLog.Rows.Count).Hidden = True
    End If
    Set wsLog  = Nothing
    Set tblLog = Nothing

    diagStep = "Stamping version number"
    masterWb.Names("LogbookVersion").RefersToRange.Value = newVersion

    ' Force full calculation before pivot/chart steps.
    ' Calculation is manual during the update, so ChartData and
    ' Logbook[Date] formulas need an explicit push before we can
    ' refresh pivots (Date field needs real dates to group) or
    ' detect the chart range (ChartData needs populated rows).
    diagStep = "Calculating formulas"
    UpdateStatus "Calculating..."
    Dim wsCalc As Worksheet
    For Each wsCalc In masterWb.Worksheets
        wsCalc.Calculate
    Next wsCalc

    diagStep = "Refreshing pivot tables"
    UpdateStatus "Refreshing pivot tables..."
    RefreshAndRegroupPivots masterWb

    diagStep = "Updating HoursOverTime chart range"
    UpdateStatus "Updating chart data..."
    Dim wsCharts  As Worksheet
    Dim wsData    As Worksheet
    Dim rnhRange  As Range
    Dim chartRng  As Range
    Dim chartLast As Long
    Set wsCharts = masterWb.Sheets("Charts")
    Set wsData   = masterWb.Sheets("ChartData")
    Set rnhRange = masterWb.Names("RunningTotalHours").RefersToRange
    chartLast = wsData.Cells(wsData.Rows.Count, rnhRange.Columns(1).Column).End(xlUp).Row
    If chartLast >= 2 Then
        Set chartRng = wsData.Range( _
            wsData.Cells(2, rnhRange.Columns(1).Column), _
            wsData.Cells(chartLast, rnhRange.Columns(2).Column))
        wsCharts.ChartObjects("HoursOverTime").Chart.SeriesCollection(1).XValues = chartRng.Columns(1)
        wsCharts.ChartObjects("HoursOverTime").Chart.SeriesCollection(1).Values  = chartRng.Columns(2)
        Set chartRng = Nothing
    End If
    Set wsCharts = Nothing
    Set wsData   = Nothing
    Set rnhRange = Nothing

    diagStep = "Saving updated file"
    UpdateStatus "Saving updated logbook..."
    ' Document Inspector's "remove personal information on save" flag disables
    ' AutoSave/OneDrive collaboration when it leaks into user workbooks.
    On Error Resume Next
    masterWb.RemovePersonalInformation = False
    On Error GoTo UpdateFailed

    ' Save to a local temp path first, then move to destination.
    ' Direct SaveAs to OneDrive paths is unreliable depending on sync state.
    localSavePath = Environ("TEMP") & "\LB_Updated_Staging.xlsm"
    masterWb.Sheets(1).EnableCalculation = True
    Application.Calculation = xlCalculationAutomatic
    Application.DisplayAlerts = False
    masterWb.SaveAs Filename:=localSavePath, FileFormat:=xlOpenXMLWorkbookMacroEnabled
    Application.DisplayAlerts = True
    Application.Calculation = xlCalculationManual

    diagStep = "Closing master"
    masterWb.Close SaveChanges:=False
    Set masterWb = Nothing
    On Error Resume Next
    Kill tempPath
    On Error GoTo 0

    If Dir(localPath, vbDirectory) = "" Then MkDir localPath

    diagStep = "Renaming current file to old copy"
    UpdateStatus "Renaming previous logbook..."
    finalHandoffStarted = True
    Application.DisplayAlerts = False
    ThisWorkbook.SaveAs Filename:=oldPath, FileFormat:=xlOpenXMLWorkbookMacroEnabled
    Application.DisplayAlerts = True

    diagStep = "Moving updated file to original filename"
    UpdateStatus "Saving updated logbook..."
    If Dir(savePath) <> "" Then Kill savePath
    FileCopy localSavePath, savePath
    On Error Resume Next
    Kill localSavePath
    On Error GoTo 0

    Application.Calculation = xlCalculationAutomatic
    Application.ScreenUpdating = True
    Application.EnableEvents = True
    UpdateStatus ""

    MsgBox "Update complete! Your updated logbook has been saved as:" & vbCrLf & vbCrLf & _
           savePath & vbCrLf & vbCrLf & _
           "Your previous logbook has been saved as:" & vbCrLf & vbCrLf & _
           oldPath & vbCrLf & vbCrLf & _
           "Please close this old file, then reopen your logbook from the original filename." & vbCrLf & vbCrLf & _
           "Please verify that your total hours, " & _
           "Charts page, and Currency + Recency page match what you had before.", _
           vbInformation, "Update Ready"
    Exit Sub

UpdateFailed:
    errNum = Err.Number
    errMsg = Err.Description
    Application.Calculation = xlCalculationAutomatic
    Application.ScreenUpdating = True
    Application.EnableEvents = True
    Application.DisplayAlerts = True
    UpdateStatus ""
    On Error Resume Next
    Application.Run "WriteDebugLog", "modUpdate.RunUpdate", errNum, errMsg, diagStep
    On Error GoTo 0
    If Not masterWb Is Nothing Then
        On Error Resume Next
        masterWb.Close SaveChanges:=False
    End If
    On Error Resume Next
    Kill tempPath
    Kill localSavePath
    On Error GoTo 0
    Dim failureNote As String
    If finalHandoffStarted Then
        failureNote = "The update reached the final file handoff. Your previous workbook may have been saved as:" & _
                      vbCrLf & vbCrLf & oldPath & vbCrLf & vbCrLf & _
                      "Check the folder before retrying the update."
    Else
        failureNote = "Your current file has not been changed."
    End If

    MsgBox "Update failed at step: " & diagStep & vbCrLf & vbCrLf & _
           "Error " & errNum & ": " & errMsg & vbCrLf & vbCrLf & _
           failureNote, _
           vbCritical, "Update Failed"
End Sub

Private Function BuildOldWorkbookPath(folderPath As String, workbookName As String) As String
    Dim dotPos    As Long
    Dim baseName  As String
    Dim extension As String
    Dim candidate As String
    Dim counter   As Long

    dotPos = InStrRev(workbookName, ".")
    If dotPos > 0 Then
        baseName = Left(workbookName, dotPos - 1)
        extension = Mid(workbookName, dotPos)
    Else
        baseName = workbookName
        extension = ""
    End If

    candidate = folderPath & "\" & baseName & "_Old" & extension
    If Dir(candidate) = "" Then
        BuildOldWorkbookPath = candidate
        Exit Function
    End If

    candidate = folderPath & "\" & baseName & "_Old_" & _
                Format(Now, "yyyymmdd_hhnnss") & extension
    If Dir(candidate) = "" Then
        BuildOldWorkbookPath = candidate
        Exit Function
    End If

    counter = 1
    Do
        candidate = folderPath & "\" & baseName & "_Old_" & _
                    Format(Now, "yyyymmdd_hhnnss") & "_" & counter & extension
        counter = counter + 1
    Loop While Dir(candidate) <> ""

    BuildOldWorkbookPath = candidate
End Function

' ==============================================================
' DATA INJECTION - LOGBOOK
' ==============================================================

Private Sub InjectLogbookData(masterWb As Workbook)
    Dim loSrc        As ListObject
    Dim loDst        As ListObject
    Dim dataColStart As Long
    Dim dataColEnd   As Long
    Dim numCols      As Long
    Dim userRows     As Long
    Dim masterRows   As Long
    Dim hasTotals    As Boolean

    Set loSrc = ThisWorkbook.Sheets("Logbook").ListObjects("Logbook")
    Set loDst = masterWb.Sheets("Logbook").ListObjects("Logbook")

    If loSrc.DataBodyRange Is Nothing Then Exit Sub
    If loDst.DataBodyRange Is Nothing Then Exit Sub

    dataColStart = loSrc.ListColumns("Year").Index
    dataColEnd   = loSrc.ListColumns("Circling").Index
    numCols      = dataColEnd - dataColStart + 1
    userRows     = loSrc.DataBodyRange.Rows.Count
    masterRows   = loDst.DataBodyRange.Rows.Count
    hasTotals    = loDst.ShowTotals

    If userRows > masterRows Then
        loDst.ShowTotals = False
        loDst.Resize loDst.Range.Resize(userRows + 1, loDst.ListColumns.Count)
        FillLogbookFormulas loDst, masterRows, userRows
        loDst.ShowTotals = hasTotals
    ElseIf userRows < masterRows Then
        loDst.ShowTotals = False
        loDst.DataBodyRange.Rows(userRows + 1).Resize(masterRows - userRows).Delete
        loDst.ShowTotals = hasTotals
    End If
	
	' Copy custom column headers from user into master by position.
    ' The master uses generic names (Custom 1, Custom 2 etc.) which
    ' are placeholders. We replace them with the user's actual names
    ' so the subsequent column-by-name copy can match them correctly.
    Dim customSrcStart As Long
    Dim customDstStart As Long
    Dim customSrcEnd   As Long
    Dim customDstEnd   As Long
    Dim customCount    As Long
    Dim c              As Long

    customSrcStart = loSrc.ListColumns("Details").Index + 1
    customSrcEnd   = loSrc.ListColumns("SeIcusDay").Index - 1
    customDstStart = loDst.ListColumns("Details").Index + 1
    customDstEnd   = loDst.ListColumns("SeIcusDay").Index - 1
    customCount    = customSrcEnd - customSrcStart + 1

    ' Only sync if both tables have the same number of custom columns
    If customCount = (customDstEnd - customDstStart + 1) And customCount > 0 Then
        For c = 0 To customCount - 1
            loDst.ListColumns(customDstStart + c).Name = _
                loSrc.ListColumns(customSrcStart + c).Name
        Next c
    End If

    ' Copy column by column by name - handles schema differences between
    ' user and master (extra/missing columns are safely skipped).
    Dim srcCol    As ListColumn
    Dim dstColIdx As Long
    For Each srcCol In loSrc.ListColumns
        If (srcCol.Index >= dataColStart And srcCol.Index <= dataColEnd) Or _
           srcCol.Name = "CurrencyExclusions" Then
            On Error Resume Next
            dstColIdx = loDst.ListColumns(srcCol.Name).Index
            If Err.Number = 0 Then
                loDst.DataBodyRange.Columns(dstColIdx).Resize(userRows, 1).Value = _
                    srcCol.DataBodyRange.Resize(userRows, 1).Value
            End If
            Err.Clear
            On Error GoTo 0
        End If
    Next srcCol

    ' Clear explicit cell fills and bold accumulated from prior AddToLogbook
    ' PasteSpecial calls. Lets the table's built-in stripe style show cleanly.
    loDst.DataBodyRange.Interior.Pattern = xlNone
    loDst.DataBodyRange.Font.Bold = False
End Sub

Private Sub FillLogbookFormulas(lo As ListObject, fromRow As Long, toRow As Long)
    ' Fills formula columns from fromRow+1 down to toRow.
    ' Only called when user has more rows than master template.
    ' Skips Year:Circling (user data columns, written separately).
    If toRow <= fromRow Then Exit Sub

    Dim dataColStart As Long
    Dim dataColEnd   As Long
    Dim colIdx       As Long
    Dim srcCell      As Range
    Dim dstRng       As Range
    Dim formula      As String
    Dim r            As Long

    dataColStart = lo.ListColumns("Year").Index
    dataColEnd   = lo.ListColumns("Circling").Index

    For colIdx = 1 To lo.ListColumns.Count
        If colIdx >= dataColStart And colIdx <= dataColEnd Then GoTo NextCol
        Set srcCell = lo.DataBodyRange.Cells(fromRow, colIdx)
        If srcCell.HasArray Then
            formula = srcCell.FormulaArray
            If Left(formula, 1) = "=" Then
                For r = fromRow + 1 To toRow
                    lo.DataBodyRange.Cells(r, colIdx).FormulaArray = formula
                Next r
            End If
        ElseIf srcCell.HasFormula Then
            Set dstRng = lo.DataBodyRange.Cells(fromRow, colIdx).Resize(toRow - fromRow + 1, 1)
            dstRng.FillDown
        End If
NextCol:
    Next colIdx
End Sub

Private Sub CopyKeywordsData(masterWb As Workbook)
    Dim loSrc      As ListObject
    Dim loDst      As ListObject
    Dim srcCol     As ListColumn
    Dim dstColIdx  As Long
    Dim sourceRows As Long
    Dim destRows   As Long

    On Error GoTo Fail

    Set loSrc = FindListObject(ThisWorkbook, "Keywords")
    Set loDst = FindListObject(masterWb, "Keywords")
    If loSrc Is Nothing Or loDst Is Nothing Then Exit Sub
    If loSrc.DataBodyRange Is Nothing Then Exit Sub

    sourceRows = loSrc.DataBodyRange.Rows.Count
    If loDst.DataBodyRange Is Nothing Then
        destRows = 0
    Else
        destRows = loDst.DataBodyRange.Rows.Count
    End If

    If destRows = 0 Then
        loDst.ListRows.Add
        destRows = 1
    End If

    If sourceRows > destRows Then
        loDst.Resize loDst.Range.Resize(sourceRows + 1, loDst.ListColumns.Count)
    ElseIf sourceRows < destRows Then
        loDst.DataBodyRange.Rows(sourceRows + 1).Resize(destRows - sourceRows).Delete
    End If

    For Each srcCol In loSrc.ListColumns
        dstColIdx = 0
        On Error Resume Next
        dstColIdx = loDst.ListColumns(srcCol.Name).Index
        On Error GoTo Fail
        If dstColIdx > 0 Then
            loDst.DataBodyRange.Columns(dstColIdx).Resize(sourceRows, 1).Value = _
                srcCol.DataBodyRange.Resize(sourceRows, 1).Value
        End If
    Next srcCol

    Exit Sub
Fail:
    Err.Clear
End Sub

Private Function FindListObject(ByVal wb As Workbook, ByVal tableName As String) As ListObject
    Dim ws As Worksheet

    On Error Resume Next
    For Each ws In wb.Worksheets
        Set FindListObject = ws.ListObjects(tableName)
        If Not FindListObject Is Nothing Then Exit Function
    Next ws
    On Error GoTo 0
End Function

Private Sub CopyRoutesData(masterWb As Workbook)
    Dim loSrc      As ListObject
    Dim loDst      As ListObject
    Dim sourceRows As Long
    Dim destRows   As Long
    Dim srcCol     As ListColumn
    Dim dstColIdx  As Long

    On Error GoTo Fail

    Set loSrc = ThisWorkbook.Sheets("Routes").ListObjects("Routes")
    Set loDst = masterWb.Sheets("Routes").ListObjects("Routes")

    If loDst.DataBodyRange Is Nothing Then
        destRows = 0
    Else
        destRows = loDst.DataBodyRange.Rows.Count
    End If

    If loSrc.DataBodyRange Is Nothing Then
        If destRows > 0 Then loDst.DataBodyRange.Delete
        Exit Sub
    End If

    sourceRows = loSrc.DataBodyRange.Rows.Count

    If destRows = 0 Then
        loDst.ListRows.Add
        destRows = 1
    End If

    If sourceRows > destRows Then
        loDst.Resize loDst.Range.Resize(sourceRows + 1, loDst.ListColumns.Count)
    ElseIf sourceRows < destRows Then
        loDst.DataBodyRange.Rows(sourceRows + 1).Resize(destRows - sourceRows).Delete
    End If

    For Each srcCol In loSrc.ListColumns
        On Error Resume Next
        dstColIdx = loDst.ListColumns(srcCol.Name).Index
        If Err.Number = 0 Then
            If srcCol.DataBodyRange.Cells(1, 1).HasFormula Then
                loDst.DataBodyRange.Columns(dstColIdx).Formula = _
                    srcCol.DataBodyRange.Resize(sourceRows, 1).Formula
            Else
                loDst.DataBodyRange.Columns(dstColIdx).Resize(sourceRows, 1).Value = _
                    srcCol.DataBodyRange.Resize(sourceRows, 1).Value
            End If
        End If
        Err.Clear
        On Error GoTo Fail
    Next srcCol

    Exit Sub
Fail:
    Err.Clear
End Sub

Private Sub CopyRouteCacheState(masterWb As Workbook)
    Dim routesBuilt As Variant
    Dim routesDirty As Variant
    Dim routeVersion As Variant

    routesBuilt = GetWorkbookNameValue(ThisWorkbook, "RoutesBuilt", "")
    routesDirty = GetWorkbookNameValue(ThisWorkbook, "RoutesDirty", False)
    routeVersion = GetWorkbookNameValue(ThisWorkbook, "RoutesDefinitionVersion", 0)

    If Trim(CStr(routesBuilt)) <> "" Then
        If Not IsNumeric(routeVersion) Then
            routeVersion = ROUTE_CACHE_DEFINITION_VERSION
        ElseIf CLng(routeVersion) = 0 Then
            routeVersion = ROUTE_CACHE_DEFINITION_VERSION
        End If
    End If

    SetWorkbookNameValue masterWb, "RoutesBuilt", routesBuilt
    SetWorkbookNameValue masterWb, "RoutesDirty", routesDirty
    SetWorkbookNameValue masterWb, "RoutesDefinitionVersion", routeVersion
End Sub

Private Function GetWorkbookNameValue(wb As Workbook, nameText As String, defaultValue As Variant) As Variant
    On Error GoTo Fail
    GetWorkbookNameValue = wb.Names(nameText).RefersToRange.Value
    Exit Function
Fail:
    GetWorkbookNameValue = defaultValue
End Function

Private Sub SetWorkbookNameValue(wb As Workbook, nameText As String, value As Variant)
    On Error Resume Next
    wb.Names(nameText).RefersToRange.Value = value
    On Error GoTo 0
End Sub

' ==============================================================
' EXTRA LABELS BELOW TOTALS ROW
' ==============================================================
' The "Total Aeronautical Experience" row sits outside the table.
' When the table resizes, it can absorb this row into the data body.
' We always re-write it at totalsRow+1 after all data is in place.

Private Sub EnsureExtraLabels(masterWb As Workbook)
    Dim lo       As ListObject
    Dim ws       As Worksheet
    Dim totRow   As Long
    Dim picCol   As Long
    Dim otherCol As Long

    Set ws = masterWb.Sheets("Logbook")
    Set lo = ws.ListObjects("Logbook")
    If Not lo.ShowTotals Then Exit Sub

    totRow   = lo.TotalsRowRange.Row
    picCol   = lo.ListColumns("PIC").Range.Column
    otherCol = lo.ListColumns("Other Pilot or Crew").Range.Column

    ws.Cells(totRow + 1, picCol).Value = "Total Aeronautical Experience"
    ws.Cells(totRow + 1, otherCol).Formula = _
        "=Logbook[[#Totals],[Other Pilot or Crew]]+Logbook[[#Totals],[IfrSim]]"
End Sub

' ==============================================================
' TABLE FORMATTING
' ==============================================================
' Copies formatting from the user's actual Logbook table into the
' master, preserving custom cell formatting applied prior to the update
' except for the font family, which always comes from the master.
' The multi-row label band above the table is intentionally left as the
' master version so structural header fixes ship with updates.

Private Sub CopyTableFormatting(masterWb As Workbook)
    Dim srcWs             As Worksheet
    Dim dstWs             As Worksheet
    Dim srcLo             As ListObject
    Dim dstLo             As ListObject
    Dim dstCol            As ListColumn
    Dim srcCol            As ListColumn
    Dim srcRng            As Range
    Dim dstRng            As Range
    Dim masterHeaderFont  As String
    Dim masterDataFont    As String
    Dim masterTotalsFont  As String

    On Error GoTo Fail

    Set srcWs = ThisWorkbook.Sheets("Logbook")
    Set dstWs = masterWb.Sheets("Logbook")
    Set srcLo = srcWs.ListObjects("Logbook")
    Set dstLo = dstWs.ListObjects("Logbook")

    ' xlPasteFormats also copies the user's old font. Snapshot the master font
    ' first so workbook-wide typography changes ship with the update.
    masterHeaderFont = dstLo.HeaderRowRange.Cells(1, 1).Font.Name
    If Not dstLo.DataBodyRange Is Nothing Then
        masterDataFont = dstLo.DataBodyRange.Cells(1, 1).Font.Name
    End If
    If dstLo.ShowTotals Then
        masterTotalsFont = dstLo.TotalsRowRange.Cells(1, 1).Font.Name
    End If
		
	' Copy the table style name from user to master
    ' xlPasteFormats does not transfer ListObject.TableStyle
    On Error Resume Next
    dstLo.TableStyle = srcLo.TableStyle
    On Error GoTo Fail

    ' Copy table formats by column name, not by rectangular position.
    ' Data migration is name-based, so formatting must follow the same rule;
    ' otherwise schema changes can shift a neighbouring column's number format
    ' onto a correctly migrated column such as CumAzi.
    For Each dstCol In dstLo.ListColumns
        If dstCol.Index > dstLo.ListColumns("CumAzi").Index Then Exit For

        Set srcCol = Nothing
        On Error Resume Next
        Set srcCol = srcLo.ListColumns(dstCol.Name)
        On Error GoTo Fail

        If Not srcCol Is Nothing Then
            Set srcRng = srcLo.Range.Columns(srcCol.Index)
            Set dstRng = dstLo.Range.Columns(dstCol.Index)

            srcRng.Copy
            dstRng.PasteSpecial xlPasteFormats
            Application.CutCopyMode = False
        End If
    Next dstCol

    If masterHeaderFont <> "" Then dstLo.HeaderRowRange.Font.Name = masterHeaderFont
    If Not dstLo.DataBodyRange Is Nothing And masterDataFont <> "" Then
        dstLo.DataBodyRange.Font.Name = masterDataFont
    End If
    If dstLo.ShowTotals And masterTotalsFont <> "" Then
        dstLo.TotalsRowRange.Font.Name = masterTotalsFont
    End If

    ' CumAzi is a calculated numeric/general column. Reset it explicitly so
    ' a workbook that already inherited a bad date format does not preserve it
    ' through the next update.
    On Error Resume Next
    dstLo.ListColumns("CumAzi").DataBodyRange.NumberFormat = "General"
    dstLo.TotalsRowRange.Cells(1, dstLo.ListColumns("CumAzi").Index).NumberFormat = "General"
    On Error GoTo Fail

    Exit Sub
Fail:
    Application.CutCopyMode = False
    Err.Clear
End Sub

Private Sub ApplyHiddenHourHeaderFormatting(masterWb As Workbook)
    Dim lo          As ListObject
    Dim headerRange As Range
    Dim headerCell  As Range
    Dim startCol    As Long
    Dim endCol      As Long

    On Error GoTo Fail

    Set lo = masterWb.Sheets("Logbook").ListObjects("Logbook")
    startCol = lo.ListColumns("SeIcusDay").Index
    endCol = lo.ListColumns("Circling").Index
    Set headerRange = lo.HeaderRowRange.Cells(1, startCol).Resize(1, endCol - startCol + 1)

    For Each headerCell In headerRange.Cells
        headerCell.Font.Color = headerCell.DisplayFormat.Interior.Color
    Next headerCell
    Exit Sub
Fail:
    Err.Clear
End Sub

' ==============================================================
' TOTALS AREA FORMATTING
' ==============================================================
' Copies formatting from the user's current Logbook totals area
' (which has the correct visual styling) onto the same area in
' the master. The LogbookTotals named range in the master cannot
' be used as source because it still points to the original small
' table position before data injection.

Private Sub CopyTotalsFormatting(masterWb As Workbook)
    Dim srcLo    As ListObject
    Dim dstLo    As ListObject
    Dim srcWs    As Worksheet
    Dim dstWs    As Worksheet
    Dim srcRow   As Long
    Dim dstRow   As Long
    Dim regCol   As Long
    Dim otherCol As Long
    Dim srcRange As Range
    Dim dstRange As Range
    Dim masterFont As String

    On Error GoTo Fail

    Set srcWs = ThisWorkbook.Sheets("Logbook")
    Set dstWs = masterWb.Sheets("Logbook")
    Set srcLo = srcWs.ListObjects("Logbook")
    Set dstLo = dstWs.ListObjects("Logbook")

    If Not srcLo.ShowTotals Then Exit Sub
    If Not dstLo.ShowTotals Then Exit Sub

    ' Source: user's totals row + 1 row below - has the correct formatting
    srcRow   = srcLo.TotalsRowRange.Row
    regCol   = srcLo.ListColumns("Reg").Range.Column
    otherCol = srcLo.ListColumns("Other Pilot or Crew").Range.Column
    Set srcRange = srcWs.Range(srcWs.Cells(srcRow, regCol), _
                               srcWs.Cells(srcRow + 1, otherCol))

    ' Destination: master's totals row + 1 row below (same columns)
    dstRow = dstLo.TotalsRowRange.Row
    Set dstRange = dstWs.Range(dstWs.Cells(dstRow, regCol), _
                               dstWs.Cells(dstRow + 1, otherCol))
    If Not dstLo.DataBodyRange Is Nothing Then
        masterFont = dstLo.DataBodyRange.Cells(1, 1).Font.Name
    End If

    srcRange.Copy
    dstRange.PasteSpecial xlPasteFormats
    Application.CutCopyMode = False
    If masterFont <> "" Then dstRange.Font.Name = masterFont
    Exit Sub
Fail:
    Application.CutCopyMode = False
    Err.Clear
End Sub

Private Sub NormalizeLogbookFormatting(masterWb As Workbook)
    Dim lo As ListObject

    Set lo = masterWb.Sheets("Logbook").ListObjects("Logbook")
    NormalizeLogbookDataFormatting lo
    NormalizeLogbookDataBorders lo
    NormalizeLogbookTotalsFormatting lo
    ApplyLogbookPalette masterWb, lo
    ApplyLogbookTotalsRowBorders lo
    ApplyLogbookTotalsFormatting masterWb, lo
    ApplyVisibleLogbookOutsideBorder lo
End Sub

Private Sub NormalizeLogbookDataFormatting(lo As ListObject)
    Dim templateRow As Range
    Dim dataColumn As Range
    Dim colIndex As Long

    If lo.DataBodyRange Is Nothing Then Exit Sub

    Set templateRow = lo.DataBodyRange.Rows(1)
    lo.DataBodyRange.Font.Name = templateRow.Cells(1, 1).Font.Name
    lo.DataBodyRange.Font.Size = templateRow.Cells(1, 1).Font.Size

    For colIndex = 1 To lo.ListColumns.Count
        Set dataColumn = lo.DataBodyRange.Columns(colIndex)
        With templateRow.Cells(1, colIndex)
            dataColumn.HorizontalAlignment = .HorizontalAlignment
            dataColumn.VerticalAlignment = .VerticalAlignment
            dataColumn.WrapText = .WrapText
            dataColumn.Orientation = .Orientation
            dataColumn.IndentLevel = .IndentLevel
            dataColumn.ShrinkToFit = .ShrinkToFit
            dataColumn.ReadingOrder = .ReadingOrder
        End With
    Next colIndex
End Sub

Private Sub NormalizeLogbookDataBorders(lo As ListObject)
    Dim templateRow As Range
    Dim dataColumn As Range
    Dim colIndex As Long
    Dim leftLineStyle() As Variant
    Dim leftWeight() As Variant
    Dim leftColor() As Variant
    Dim rightLineStyle() As Variant
    Dim rightWeight() As Variant
    Dim rightColor() As Variant

    If lo.DataBodyRange Is Nothing Then Exit Sub

    Set templateRow = lo.DataBodyRange.Rows(1)
    ReDim leftLineStyle(1 To lo.ListColumns.Count)
    ReDim leftWeight(1 To lo.ListColumns.Count)
    ReDim leftColor(1 To lo.ListColumns.Count)
    ReDim rightLineStyle(1 To lo.ListColumns.Count)
    ReDim rightWeight(1 To lo.ListColumns.Count)
    ReDim rightColor(1 To lo.ListColumns.Count)

    For colIndex = 1 To lo.ListColumns.Count
        With templateRow.Cells(1, colIndex).Borders(xlEdgeLeft)
            leftLineStyle(colIndex) = .LineStyle
            leftWeight(colIndex) = .Weight
            leftColor(colIndex) = .Color
        End With
        With templateRow.Cells(1, colIndex).Borders(xlEdgeRight)
            rightLineStyle(colIndex) = .LineStyle
            rightWeight(colIndex) = .Weight
            rightColor(colIndex) = .Color
        End With
    Next colIndex

    lo.DataBodyRange.Borders.LineStyle = xlNone

    For colIndex = 1 To lo.ListColumns.Count
        Set dataColumn = lo.DataBodyRange.Columns(colIndex)
        If leftLineStyle(colIndex) <> xlNone Then
            SetBorderFormat dataColumn.Borders(xlEdgeLeft), _
                            leftLineStyle(colIndex), leftWeight(colIndex), leftColor(colIndex)
        End If
        If rightLineStyle(colIndex) <> xlNone Then
            SetBorderFormat dataColumn.Borders(xlEdgeRight), _
                            rightLineStyle(colIndex), rightWeight(colIndex), rightColor(colIndex)
        End If
    Next colIndex
End Sub

Private Sub SetBorderFormat(ByVal targetBorder As Border, _
                            ByVal lineStyle As Variant, _
                            ByVal weight As Variant, _
                            ByVal color As Variant)
    If lineStyle = xlNone Then
        targetBorder.LineStyle = xlNone
        Exit Sub
    End If

    targetBorder.Weight = weight
    targetBorder.Color = color
    targetBorder.LineStyle = lineStyle
End Sub

Private Sub ApplyLogbookPalette(masterWb As Workbook, lo As ListObject)
    Const SUM_TOTALS_LIGHTNESS As Double = 0.2
    Dim headerRange As Range
    Dim sumTotalsRange As Range
    Dim secondaryColor As Long

    If lo.DataBodyRange Is Nothing Then Exit Sub

    secondaryColor = lo.DataBodyRange.Rows(1).Cells(1, 1).DisplayFormat.Interior.Color

    On Error Resume Next
    Set headerRange = masterWb.Names("LogbookHeaders").RefersToRange
    Set sumTotalsRange = masterWb.Names("LogbookSumTotals").RefersToRange
    On Error GoTo 0

    If Not headerRange Is Nothing Then
        headerRange.Interior.Pattern = xlSolid
        headerRange.Interior.Color = secondaryColor
        headerRange.Font.Color = ContrastingTextColor(secondaryColor)
    End If

    If Not lo.ShowTotals Then Exit Sub

    lo.TotalsRowRange.Interior.Pattern = xlSolid
    lo.TotalsRowRange.Interior.Color = vbBlack
    lo.TotalsRowRange.Font.Color = vbWhite

    If Not sumTotalsRange Is Nothing Then
        sumTotalsRange.Interior.Pattern = xlSolid
        sumTotalsRange.Interior.Color = ColorWithLightness(secondaryColor, SUM_TOTALS_LIGHTNESS)
        sumTotalsRange.Font.Color = vbWhite
    End If
End Sub

Private Function ColorWithLightness(ByVal sourceColor As Long, ByVal targetLightness As Double) As Long
    Dim redValue As Double
    Dim greenValue As Double
    Dim blueValue As Double
    Dim maximumValue As Double
    Dim minimumValue As Double
    Dim hue As Double
    Dim saturation As Double
    Dim lightness As Double
    Dim firstChannel As Double
    Dim secondChannel As Double

    redValue = (sourceColor And &HFF&) / 255
    greenValue = ((sourceColor \ &H100&) And &HFF&) / 255
    blueValue = ((sourceColor \ &H10000) And &HFF&) / 255
    maximumValue = WorksheetFunction.Max(redValue, greenValue, blueValue)
    minimumValue = WorksheetFunction.Min(redValue, greenValue, blueValue)
    lightness = (maximumValue + minimumValue) / 2

    If maximumValue = minimumValue Then
        ColorWithLightness = RGB(targetLightness * 255, targetLightness * 255, targetLightness * 255)
        Exit Function
    End If

    If lightness > 0.5 Then
        saturation = (maximumValue - minimumValue) / (2 - maximumValue - minimumValue)
    Else
        saturation = (maximumValue - minimumValue) / (maximumValue + minimumValue)
    End If

    If maximumValue = redValue Then
        hue = (greenValue - blueValue) / (maximumValue - minimumValue)
        If greenValue < blueValue Then hue = hue + 6
    ElseIf maximumValue = greenValue Then
        hue = (blueValue - redValue) / (maximumValue - minimumValue) + 2
    Else
        hue = (redValue - greenValue) / (maximumValue - minimumValue) + 4
    End If
    hue = hue / 6

    secondChannel = targetLightness * (1 + saturation)
    If targetLightness >= 0.5 Then secondChannel = targetLightness + saturation - targetLightness * saturation
    firstChannel = 2 * targetLightness - secondChannel

    ColorWithLightness = RGB(255 * HueChannel(firstChannel, secondChannel, hue + 1 / 3), _
                             255 * HueChannel(firstChannel, secondChannel, hue), _
                             255 * HueChannel(firstChannel, secondChannel, hue - 1 / 3))
End Function

Private Function HueChannel(ByVal firstChannel As Double, _
                            ByVal secondChannel As Double, _
                            ByVal hue As Double) As Double
    If hue < 0 Then hue = hue + 1
    If hue > 1 Then hue = hue - 1

    If hue < 1 / 6 Then
        HueChannel = firstChannel + (secondChannel - firstChannel) * 6 * hue
    ElseIf hue < 1 / 2 Then
        HueChannel = secondChannel
    ElseIf hue < 2 / 3 Then
        HueChannel = firstChannel + (secondChannel - firstChannel) * (2 / 3 - hue) * 6
    Else
        HueChannel = firstChannel
    End If
End Function

Private Function ContrastingTextColor(ByVal backgroundColor As Long) As Long
    Dim redValue As Long
    Dim greenValue As Long
    Dim blueValue As Long
    Dim perceivedBrightness As Double

    redValue = backgroundColor And &HFF&
    greenValue = (backgroundColor \ &H100&) And &HFF&
    blueValue = (backgroundColor \ &H10000) And &HFF&
    perceivedBrightness = (redValue * 299 + greenValue * 587 + blueValue * 114) / 1000

    If perceivedBrightness >= 150 Then
        ContrastingTextColor = vbBlack
    Else
        ContrastingTextColor = vbWhite
    End If
End Function

Private Sub ApplyLogbookTotalsRowBorders(lo As ListObject)
    Dim totalsRange As Range

    If Not lo.ShowTotals Then Exit Sub

    Set totalsRange = lo.TotalsRowRange
    totalsRange.Borders.LineStyle = xlNone
    SetBorderFormat totalsRange.Borders(xlEdgeTop), xlDouble, xlMedium, vbBlack
    SetBorderFormat totalsRange.Borders(xlEdgeLeft), xlContinuous, xlThin, vbBlack
    SetBorderFormat totalsRange.Borders(xlEdgeRight), xlContinuous, xlThin, vbBlack
    SetBorderFormat totalsRange.Borders(xlEdgeBottom), xlContinuous, xlThin, vbBlack
    SetBorderFormat totalsRange.Borders(xlInsideVertical), xlContinuous, xlThin, vbBlack
End Sub

Private Sub NormalizeLogbookTotalsFormatting(lo As ListObject)
    Dim totalsRange            As Range
    Dim tableStyleName         As String
    Dim tableFontName          As String
    Dim tableFontSize          As Double
    Dim columnCount            As Long
    Dim colIndex               As Long
    Dim numberFormats()        As Variant
    Dim horizontalAlignments() As Variant
    Dim verticalAlignments()   As Variant
    Dim wrapTextValues()       As Variant

    If Not lo.ShowTotals Then Exit Sub

    Set totalsRange = lo.TotalsRowRange
    tableStyleName = lo.TableStyle.Name
    tableFontName = lo.DataBodyRange.Cells(1, 1).Font.Name
    tableFontSize = lo.DataBodyRange.Cells(1, 1).Font.Size
    columnCount = lo.ListColumns.Count

    ReDim numberFormats(1 To columnCount)
    ReDim horizontalAlignments(1 To columnCount)
    ReDim verticalAlignments(1 To columnCount)
    ReDim wrapTextValues(1 To columnCount)

    For colIndex = 1 To columnCount
        With totalsRange.Cells(1, colIndex)
            numberFormats(colIndex) = .NumberFormat
            horizontalAlignments(colIndex) = .HorizontalAlignment
            verticalAlignments(colIndex) = .VerticalAlignment
            wrapTextValues(colIndex) = .WrapText
        End With
    Next colIndex

    totalsRange.ClearFormats

    For colIndex = 1 To columnCount
        With totalsRange.Cells(1, colIndex)
            .NumberFormat = numberFormats(colIndex)
            .HorizontalAlignment = horizontalAlignments(colIndex)
            .VerticalAlignment = verticalAlignments(colIndex)
            .WrapText = wrapTextValues(colIndex)
        End With
    Next colIndex

    lo.TableStyle = tableStyleName
    totalsRange.Font.Name = tableFontName
    totalsRange.Font.Size = tableFontSize
End Sub

Private Sub ApplyLogbookTotalsFormatting(masterWb As Workbook, lo As ListObject)
    Const MASTER_TOTALS_FILL_COLOR As Long = 14277081
    Dim ws As Worksheet
    Dim totalsBlock As Range
    Dim topRow As Range
    Dim bottomRow As Range
    Dim labelCells As Range
    Dim hoursCells As Range
    Dim cellLeftOfBlock As Range
    Dim nameFormula As String
    Dim tableFontName As String
    Dim tableFontSize As Double

    If Not lo.ShowTotals Then Exit Sub

    Set ws = lo.Parent
    Set totalsBlock = ws.Range(ws.Cells(lo.TotalsRowRange.Row, lo.ListColumns("Reg").Range.Column), _
                               ws.Cells(lo.TotalsRowRange.Row + 1, lo.ListColumns("Other Pilot or Crew").Range.Column))
    Set topRow = totalsBlock.Rows(1)
    Set bottomRow = totalsBlock.Rows(2)
    Set labelCells = Union(topRow.Cells(1, 2), bottomRow.Cells(1, 2))
    Set hoursCells = Union(topRow.Cells(1, 3), bottomRow.Cells(1, 3))
    Set cellLeftOfBlock = bottomRow.Cells(1, 1).Offset(0, -1)
    tableFontName = lo.DataBodyRange.Cells(1, 1).Font.Name
    tableFontSize = lo.DataBodyRange.Cells(1, 1).Font.Size

    nameFormula = "='" & Replace(ws.Name, "'", "''") & "'!" & totalsBlock.Address
    On Error Resume Next
    masterWb.Names("LogbookTotals").RefersTo = nameFormula
    If Err.Number <> 0 Then
        Err.Clear
        masterWb.Names.Add Name:="LogbookTotals", RefersTo:=nameFormula
    End If
    On Error GoTo 0

    topRow.Interior.Pattern = xlNone
    topRow.Font.Color = vbBlack
    topRow.Font.Bold = False
    topRow.Cells(1, 3).Font.Bold = True

    bottomRow.Interior.Pattern = xlSolid
    bottomRow.Interior.Color = MASTER_TOTALS_FILL_COLOR
    bottomRow.Font.Color = vbBlack
    bottomRow.Font.Bold = True
    totalsBlock.Font.Name = tableFontName
    totalsBlock.Font.Size = tableFontSize

    labelCells.HorizontalAlignment = xlRight
    labelCells.WrapText = False
    hoursCells.HorizontalAlignment = xlCenter
    hoursCells.VerticalAlignment = xlCenter
    hoursCells.WrapText = False
    bottomRow.Cells(1, 3).NumberFormat = topRow.Cells(1, 3).NumberFormat

    totalsBlock.Borders.LineStyle = xlNone
    SetBorderFormat totalsBlock.Borders(xlEdgeTop), xlContinuous, xlMedium, vbBlack
    SetBorderFormat totalsBlock.Borders(xlEdgeLeft), xlContinuous, xlMedium, vbBlack
    SetBorderFormat totalsBlock.Borders(xlEdgeRight), xlContinuous, xlMedium, vbBlack
    SetBorderFormat totalsBlock.Borders(xlEdgeBottom), xlContinuous, xlMedium, vbBlack
    SetBorderFormat totalsBlock.Borders(xlInsideVertical), xlContinuous, xlThin, vbBlack
    SetBorderFormat totalsBlock.Borders(xlInsideHorizontal), xlContinuous, xlThin, vbBlack
    cellLeftOfBlock.Interior.Pattern = cellLeftOfBlock.Offset(0, -1).Interior.Pattern
    cellLeftOfBlock.Interior.Color = cellLeftOfBlock.Offset(0, -1).Interior.Color
    cellLeftOfBlock.Borders.LineStyle = xlNone
End Sub

Private Sub ApplyVisibleLogbookOutsideBorder(lo As ListObject)
    Dim visibleRange As Range
    Dim ws As Worksheet

    If Not lo.ShowTotals Then Exit Sub

    Set ws = lo.Parent
    Set visibleRange = ws.Range(ws.Cells(2, lo.ListColumns("Date").Range.Column), _
                                ws.Cells(lo.TotalsRowRange.Row, lo.ListColumns("Circling").Range.Column))

    SetBorderFormat visibleRange.Borders(xlEdgeTop), xlContinuous, xlThin, vbBlack
    SetBorderFormat visibleRange.Borders(xlEdgeLeft), xlContinuous, xlThin, vbBlack
    SetBorderFormat visibleRange.Borders(xlEdgeRight), xlContinuous, xlThin, vbBlack
    SetBorderFormat visibleRange.Borders(xlEdgeBottom), xlContinuous, xlThin, vbBlack
End Sub

' ==============================================================
' PIVOT TABLE REFRESH + DATE REGROUPING
' ==============================================================
' Refreshes all pivot tables, then fixes the HoursByYear date
' grouping which is lost when the pivot cache rebuilds.
' The fix mirrors the manual steps: remove Date from row fields,
' refresh, re-add Date, then group by months and years.

Private Sub RefreshAndRegroupPivots(masterWb As Workbook)
    Dim ws        As Worksheet
    Dim pt        As PivotTable
    Dim failedPTs As String

    ' Refresh all pivot tables - collect failures rather than stopping
    For Each ws In masterWb.Worksheets
        For Each pt In ws.PivotTables
            On Error Resume Next
            pt.RefreshTable
            If Err.Number <> 0 Then
                failedPTs = failedPTs & "  " & pt.Name & ": " & Err.Description & vbCrLf
                Err.Clear
            End If
            On Error GoTo 0
        Next pt
    Next ws

    If failedPTs <> "" Then
        MsgBox "Warning: some pivot tables could not be refreshed:" & vbCrLf & vbCrLf & _
               failedPTs & vbCrLf & _
               "You can refresh manually with Ctrl+Alt+F5.", _
               vbExclamation, "Pivot Refresh Warning"
    End If

    ' Fix HoursByYear date grouping.
    ' Mirrors the manual fix: remove Date, refresh, re-add, group.
    On Error GoTo GroupFail
    Set pt = masterWb.Sheets("ChartData").PivotTables("HoursByYear")

    ' Remove Date and refresh so cache rebuilds cleanly with valid dates
    pt.PivotFields("Date").Orientation = xlHidden
    DoEvents
    Application.Calculation = xlCalculationAutomatic
    pt.RefreshTable
    Application.Calculation = xlCalculationManual
    DoEvents

    ' Re-add Date before grouping - LabelRange requires field to be in rows
    pt.PivotFields("Date").Orientation = xlRowField
    pt.PivotFields("Date").Position = 1
    DoEvents

    ' LabelRange.Cells(1) is the header - Cells(2) is the first data cell
    pt.PivotFields("Date").LabelRange.Cells(2).Group _
        Start:=True, End:=True, _
        Periods:=Array(False, False, False, False, True, False, True)

    ' Grouping hides the base Date field - re-add it as the day-level drill
    On Error Resume Next
    pt.PivotFields("Date").Orientation = xlRowField
    On Error GoTo 0

    ' Collapse to Year level so chart shows yearly bars by default
    On Error Resume Next
    pt.PivotFields("Years (Date)").ShowDetail = False
    On Error GoTo 0
    Exit Sub

GroupFail:
    MsgBox "Warning: HoursByYear date grouping could not be automatically restored." & vbCrLf & vbCrLf & _
           "Error " & Err.Number & ": " & Err.Description & vbCrLf & vbCrLf & _
           "To fix manually in the updated file:" & vbCrLf & _
           "  1. Open the HoursByYear pivot table" & vbCrLf & _
           "  2. Remove 'Date' from the Rows field" & vbCrLf & _
           "  3. Press Ctrl+Alt+F5 to refresh all" & vbCrLf & _
           "  4. Re-add 'Date' to the Rows field", _
           vbExclamation, "Pivot Grouping Warning"
    Err.Clear
End Sub

' ==============================================================
' GITHUB TOKEN
' ==============================================================
' The token is stored in a named range in the workbook so it
' never appears in any file that is pushed to GitHub.
' The named range 'GitHubToken' should contain the PAT value.
' If the named range is missing or empty, requests are made
' without authentication (works for public repos only).

Private Function GetGitHubToken() As String
    On Error Resume Next
    GetGitHubToken = Trim(CStr(ThisWorkbook.Names("GitHubToken").RefersToRange.Value))
    On Error GoTo 0
End Function

' ==============================================================
' UTILITIES
' ==============================================================

Private Function RawURL(filename As String, Optional gitRef As String = "") As String
    If gitRef = "" Then gitRef = ResolveGitHubRef()

    ' Use a pinned commit SHA when possible so we bypass the branch-name
    ' cache on raw.githubusercontent.com and fetch the exact latest commit.
    RawURL = "https://raw.githubusercontent.com/" & GITHUB_USER & "/" & _
             GITHUB_REPO & "/" & gitRef & "/" & filename & _
             "?_=" & Format(Now, "yyyymmddhhmmss")
End Function

Private Function ResolveGitHubRef() As String
    Dim branchName As String
    Dim sha        As String

    branchName = GetGitHubBranch()
    sha = GetBranchCommitSha(branchName)
    If sha <> "" Then
        ResolveGitHubRef = sha
    ElseIf LCase$(branchName) <> "main" Then
        sha = GetBranchCommitSha("main")
        If sha <> "" Then
            ResolveGitHubRef = sha
        Else
            ResolveGitHubRef = branchName
        End If
    Else
        ResolveGitHubRef = branchName
    End If
End Function

Private Function GetGitHubBranch() As String
    On Error Resume Next
    GetGitHubBranch = Trim(CStr(ThisWorkbook.Names("GitHubBranch").RefersToRange.Value))
    If GetGitHubBranch = "" Then GetGitHubBranch = "main"
    On Error GoTo 0
End Function

Private Function DownloadFile(url As String, destPath As String) As Boolean
    Dim http   As Object
    Dim stream As Object

    On Error GoTo Fail
    Set http = CreateObject("MSXML2.XMLHTTP")
    http.Open "GET", url, False
    http.setRequestHeader "Cache-Control", "no-cache"
    http.setRequestHeader "Pragma", "no-cache"
    http.setRequestHeader "User-Agent", "Electronic-Logbook-Updater"
    Dim token As String
    token = GetGitHubToken()
    If token <> "" Then
        http.setRequestHeader "Authorization", "token " & token
    End If
    http.send
    If http.Status <> 200 And token <> "" Then
        ' Retry without auth so a revoked PAT in GitHubToken does not block
        ' public update downloads.
        Set http = CreateObject("MSXML2.XMLHTTP")
        http.Open "GET", url, False
        http.setRequestHeader "Cache-Control", "no-cache"
        http.setRequestHeader "Pragma", "no-cache"
        http.setRequestHeader "User-Agent", "Electronic-Logbook-Updater"
        http.send
    End If
    If http.Status <> 200 Then GoTo Fail

    Set stream = CreateObject("ADODB.Stream")
    stream.Type = 1
    stream.Open
    stream.Write http.responseBody
    stream.SaveToFile destPath, 2
    stream.Close

    DownloadFile = True
    Exit Function
Fail:
    DownloadFile = False
End Function

Private Sub UpdateStatus(msg As String)
    If msg = "" Then
        Application.StatusBar = False
    Else
        Application.StatusBar = "Logbook Update: " & msg
    End If
    DoEvents
End Sub

Private Function ResolveLocalPath(wb As Workbook) As String
    ' Self-contained copy so modUpdate does not depend on the user's existing
    ' modLogbook (which may be an older version without this function).
    ' modLogbook carries its own Public copy for use after modUpdate is removed.

    Dim wbPath As String
    wbPath = wb.Path

    If Left(wbPath, 4) <> "http" Then
        ResolveLocalPath = wbPath
        Exit Function
    End If

    Dim oneDrivePaths(2) As String
    oneDrivePaths(0) = Environ("OneDriveConsumer")
    oneDrivePaths(1) = Environ("OneDriveCommercial")
    oneDrivePaths(2) = Environ("OneDrive")

    Dim fso As Object
    Set fso = CreateObject("Scripting.FileSystemObject")

    Dim urlPath As String
    Dim i       As Long
    Dim slashes As Integer
    urlPath = wbPath
    For i = 1 To Len(urlPath)
        If Mid(urlPath, i, 1) = "/" Then
            slashes = slashes + 1
            If slashes = 4 Then
                urlPath = Mid(urlPath, i + 1)
                Exit For
            End If
        End If
    Next i
    urlPath = Replace(urlPath, "/", "\")

    Dim pathParts() As String
    pathParts = Split(urlPath, "\")

    Dim j         As Integer
    Dim k         As Integer
    Dim m         As Integer
    Dim odPath    As String
    Dim candidate As String

    For j = 0 To 2
        odPath = oneDrivePaths(j)
        If odPath = "" Then GoTo NextOD
        For k = 0 To UBound(pathParts)
            Dim partSlice() As String
            ReDim partSlice(UBound(pathParts) - k)
            For m = 0 To UBound(pathParts) - k
                partSlice(m) = pathParts(k + m)
            Next m
            candidate = odPath & "\" & Join(partSlice, "\")
            If fso.FolderExists(candidate) Then
                ResolveLocalPath = candidate
                Set fso = Nothing
                Exit Function
            End If
        Next k
NextOD:
    Next j

    ResolveLocalPath = Environ("USERPROFILE") & "\Documents"
    Set fso = Nothing
End Function

Private Function GetBranchCommitSha(branchName As String) As String
    Dim http     As Object
    Dim apiUrl   As String
    Dim body     As String
    Dim token    As String

    On Error GoTo Fail

    apiUrl = "https://api.github.com/repos/" & GITHUB_USER & "/" & _
             GITHUB_REPO & "/commits/" & branchName & _
             "?_=" & Format(Now, "yyyymmddhhmmss")

    Set http = CreateObject("MSXML2.XMLHTTP")
    http.Open "GET", apiUrl, False
    http.setRequestHeader "Accept", "application/vnd.github+json"
    http.setRequestHeader "Cache-Control", "no-cache"
    http.setRequestHeader "Pragma", "no-cache"
    http.setRequestHeader "User-Agent", "Electronic-Logbook-Updater"
    token = GetGitHubToken()
    If token <> "" Then
        http.setRequestHeader "Authorization", "token " & token
    End If
    http.send
    If http.Status <> 200 And token <> "" Then
        ' Retry without auth so a revoked PAT in GitHubToken does not block
        ' public branch resolution.
        Set http = CreateObject("MSXML2.XMLHTTP")
        http.Open "GET", apiUrl, False
        http.setRequestHeader "Accept", "application/vnd.github+json"
        http.setRequestHeader "Cache-Control", "no-cache"
        http.setRequestHeader "Pragma", "no-cache"
        http.setRequestHeader "User-Agent", "Electronic-Logbook-Updater"
        http.send
    End If
    If http.Status <> 200 Then GoTo Fail

    body = http.responseText
    GetBranchCommitSha = ExtractFirstSha(body)
    Exit Function
Fail:
    GetBranchCommitSha = ""
End Function

Private Function ExtractFirstSha(text As String) As String
    Dim re As Object
    Dim matches As Object

    On Error GoTo Fail
    Set re = CreateObject("VBScript.RegExp")
    re.Pattern = """sha""\s*:\s*""([0-9a-fA-F]{40})"""
    re.Global = False
    re.IgnoreCase = True

    If re.Test(text) Then
        Set matches = re.Execute(text)
        ExtractFirstSha = matches(0).SubMatches(0)
    End If
    Exit Function
Fail:
    ExtractFirstSha = ""
End Function
