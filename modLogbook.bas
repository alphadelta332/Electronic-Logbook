Attribute VB_Name = "modLogbook"
Public Const ROUTE_DEFINITION_VERSION As Long = 3

Sub AddToLogbook()

    On Error GoTo Cleanup

    '--- Save workbook before making any changes (safeguard against mid-run crashes)
    On Error Resume Next
    ThisWorkbook.Save
    On Error GoTo Cleanup

    '===============================
    ' STEP 1: OPTIMISE PERFORMANCE
    '===============================
        Application.ScreenUpdating = False      ' Prevent screen flicker
        Application.EnableEvents = False        ' Prevent event triggers during execution
        Application.Calculation = xlCalculationManual   ' Disable auto-calc for speed

    '===============================
    ' STEP 2: DECLARE VARIABLES
    '===============================
    Dim wsEntry As Worksheet
    Dim wsLog As Worksheet
    Dim tbl As ListObject
    Dim newRow As ListRow
    Dim col As Range
    Dim response As VbMsgBoxResult
    Dim headerName As String
    Dim todayDate As Date
    Dim entryDate As Date
    Dim diagStep As String
    Dim ipcDetected As Boolean
    Dim opcDetected As Boolean
    Dim flightReviewDetected As Boolean

        Set wsEntry = ThisWorkbook.Sheets("New Entry")
        Set wsLog = ThisWorkbook.Sheets("Logbook")
        Set tbl = wsLog.ListObjects("Logbook")
        If Not ListColumnExists(tbl, "IPC") Or _
           Not ListColumnExists(tbl, "OPC") Or _
           Not ListColumnExists(tbl, "FlightReview") Then
            MsgBox "ERROR: The Logbook table is missing one or more currency helper columns. Please update the workbook structure before adding entries.", vbCritical
            GoTo Cleanup
        End If
        If Not KeywordTableIsValid() Then
            MsgBox "ERROR: The Keywords table is missing or does not contain the Flight Review, IPC and OPC columns. Please update the workbook structure before adding entries.", vbCritical
            GoTo Cleanup
        End If
        todayDate = Range("today").Value
        ipcDetected = KeywordDetected(CStr(Range("neDetails").Value), "IPC")
        opcDetected = KeywordDetected(CStr(Range("neDetails").Value), "OPC")
        flightReviewDetected = ipcDetected Or _
                               KeywordDetected(CStr(Range("neDetails").Value), "Flight Review")
        RefreshDateCalculationFormulas tbl

    '===============================
    ' STEP 3: HARD VALIDATION (STOP ERRORS)
    '===============================
        diagStep = "Step 3: Hard Validation"

    '--- 3a. Date Validity Check
        'Check 1: Ensure the date field doesn't contain a formula error (e.g. invalid month string)
        If IsError(Range("neDate").Value) Then
            MsgBox "ERROR: Formatting error in the Date field. Ensure the month is entered in the correct 3 letter format, the date is a valid one, and the date is not in the future.", vbCritical
            GoTo Cleanup
        End If

        'Check 2: Ensure the resolved value is actually a recognisable date
        If Not IsDate(Range("neDate").Value) Then
            MsgBox "ERROR: Formatting error in the Date field. Ensure the month is entered in the correct 3 letter format, the date is a valid one, and the date is not in the future.", vbCritical
            GoTo Cleanup
        End If

        'Check 3: Ensure the date is not in the future
        If Range("neDate").Value > todayDate Then
            MsgBox "ERROR: Formatting error in the Date field. Ensure the month is entered in the correct 3 letter format, the date is a valid one, and the date is not in the future.", vbCritical
            GoTo Cleanup
        End If

        entryDate = CDate(Range("neDate").Value)

    '--- 3b. Registration Check (skipped for sim entries)
        If Range("neIfrSim").Value = 0 Or Range("neIfrSim").Value = "" Then

            'Check 1: Aircraft registration must not be blank
            If Range("neReg").Value = "" Then
                MsgBox "ERROR: Registration cannot be blank.", vbCritical
                GoTo Cleanup
            End If

        End If

    '--- 3c. Zero Hours Check
        If Application.WorksheetFunction.CountIf( _
            Range("neSeIcusDay", "neIfrSim"), ">0") = 0 Then
            MsgBox "ERROR: Total hours cannot be zero.", vbCritical
            GoTo Cleanup
        End If

    '--- 3d. IFR Hours vs Total Hours Check
        If Range("neIfrIf").Value > Application.WorksheetFunction.Sum(Range("neSeIcusDay", "neCopilotNight")) Then
            MsgBox "ERROR: In Flight Instrument Hours cannot be greater than Total Hours.", vbCritical
            GoTo Cleanup
        End If

    '--- 3e. Required Blank Field Checks
        'Check 1: Loop through core required fields (Year, Month, Day, Type, PIC)
        Dim requiredFields As Variant
        Dim fieldNames As Variant
        Dim i As Integer

        requiredFields = Array("neYear", "neMonth", "neDay", "neType", "nePIC")
        fieldNames = Array("Year", "Month", "Day", "Type", "PIC")

        For i = 0 To UBound(requiredFields)
            If Range(requiredFields(i)).Value = "" Then
                MsgBox "ERROR: " & fieldNames(i) & " cannot be blank.", vbCritical
                GoTo Cleanup
            End If
        Next i

        'Check 2: Details field must not be blank (checked separately as it falls outside the loop range)
        If Range("neDetails").Value = "" Then
            MsgBox "ERROR: Details cannot be blank.", vbCritical
            GoTo Cleanup
        End If

    '--- 3f. Non-Numeric Value Check (Hours, Landings, Approaches)
        Dim checkCell As Range

        For Each checkCell In Range("neSI1", "neCircling")
            If checkCell.Value <> "" Then
                If Not IsNumeric(checkCell.Value) Then
                    MsgBox "ERROR: Non-numerical value found in Hours, Landings, or Approaches.", vbCritical
                    GoTo Cleanup
                End If
            End If
        Next checkCell

    '===============================
    ' STEP 4: WARNING CHECKS (CONTINUE OR CANCEL)
    '===============================
        diagStep = "Step 4: Warning Checks"
    ' All warnings use vbOKCancel: OK = proceed, Cancel = go back.
    ' Warnings can be suppressed for 24 hours via the dedicated sheet button (ToggleSuppressWarnings).

    '--- 4a. Initialise Warning Suppression State
        Dim suppressWarnings As Boolean
        suppressWarnings = False

        If Range("suppressWarningsUntil").Value <> "" Then
            If IsDate(Range("suppressWarningsUntil").Value) Then
                If Now < CDate(Range("suppressWarningsUntil").Value) Then suppressWarnings = True
            End If
        End If

    '--- 4b. No Landings Recorded (Non-Sim Entries Only)
        If Not suppressWarnings Then
            If Range("neIfrSim").Value = 0 Or Range("neIfrSim").Value = "" Then
                If (Range("neLandingsDay").Value = 0 Or Range("neLandingsDay").Value = "") And _
                   (Range("neLandingsNight").Value = 0 Or Range("neLandingsNight").Value = "") Then
                    response = MsgBox("Warning: No Landings Recorded. Proceed?", vbOKCancel + vbExclamation, "No Landings")
                    If response = vbCancel Then GoTo Cleanup
                End If
            End If
        End If

    '--- 4c. Earlier Than Latest Existing Logbook Entry Check
        If Not suppressWarnings Then
            Dim latestLogbookDate As Date
            latestLogbookDate = GetLatestLogbookEntryDate(tbl)

            If latestLogbookDate <> 0 Then
                If CDate(Range("neDate").Value) < latestLogbookDate Then
                    response = MsgBox("Warning: This entry is dated before the latest existing Logbook entry (" & _
                                      Format(latestLogbookDate, "dd mmm yyyy") & "). Continue?", _
                                      vbOKCancel + vbExclamation, "Earlier Than Latest Logbook Entry")
                    If response = vbCancel Then GoTo Cleanup
                End If
            End If
        End If

    '--- 4d. Day Hours vs Day Landings Cross-Check
        Dim dayHours As Double
        dayHours = Application.WorksheetFunction.Sum( _
            Range("neSeIcusDay"), Range("neSeDualDay"), Range("neSeCommandDay"), _
            Range("neMeIcusDay"), Range("neMeDualDay"), Range("neMeCommandDay"), _
            Range("neCopilotDay"))

        If Not suppressWarnings Then
            'Check 1: Day hours recorded but no day landings
            If dayHours > 0 Then
                If Range("neLandingsDay").Value = 0 Or Range("neLandingsDay").Value = "" Then
                    response = MsgBox("Warning: Day hours recorded, but no Day Landings recorded. Continue?", vbOKCancel + vbExclamation, "Day Hours Warning")
                    If response = vbCancel Then GoTo Cleanup
                End If
            End If

            'Check 2: Day landings recorded but no day hours
            If Range("neLandingsDay").Value > 0 Then
                If dayHours = 0 Then
                    response = MsgBox("Warning: Day Landings recorded, but no Day hours recorded. Continue?", vbOKCancel + vbExclamation, "Day Landings Warning")
                    If response = vbCancel Then GoTo Cleanup
                End If
            End If
        End If

    '--- 4e. Night Hours vs Night Landings Cross-Check
        Dim nightHours As Double
        nightHours = Application.WorksheetFunction.Sum( _
            Range("neSeIcusNight"), Range("neSeDualNight"), Range("neSeCommandNight"), _
            Range("neMeIcusNight"), Range("neMeDualNight"), Range("neMeCommandNight"), _
            Range("neCopilotNight"))

        If Not suppressWarnings Then
            'Check 1: Night hours recorded but no night landings
            If nightHours > 0 Then
                If Range("neLandingsNight").Value = 0 Or Range("neLandingsNight").Value = "" Then
                    response = MsgBox("Warning: Night hours recorded, but no Night Landings recorded. Continue?", vbOKCancel + vbExclamation, "Night Hours Warning")
                    If response = vbCancel Then GoTo Cleanup
                End If
            End If

            'Check 2: Night landings recorded but no night hours
            If Range("neLandingsNight").Value > 0 Then
                If nightHours = 0 Then
                    response = MsgBox("Warning: Night Landings recorded, but no Night hours recorded. Continue?", vbOKCancel + vbExclamation, "Night Landings Warning")
                    If response = vbCancel Then GoTo Cleanup
                End If
            End If
        End If

    '--- 4f. IPC / OPC / Flight Review Detection Check
        If Not suppressWarnings Then
            If ipcDetected Or opcDetected Or flightReviewDetected Then
                Dim detectionMessage As String
                detectionMessage = "This entry will be counted as " & _
                                   DetectedCurrencyItemsText(ipcDetected, opcDetected, flightReviewDetected) & _
                                   "."
                If opcDetected Then
                    detectionMessage = detectionMessage & vbCrLf & vbCrLf & _
                                       "Only use an OPC keyword if this was a qualifying operator proficiency check covering IFR operations."
                End If
                detectionMessage = detectionMessage & vbCrLf & vbCrLf & "Continue?"
                response = MsgBox( _
                    detectionMessage, _
                    vbOKCancel + vbInformation, _
                    "Currency Detection")
                If response = vbCancel Then GoTo Cleanup
            End If
        End If

    '--- 4g. OPC Without Instrument Hours / Approaches Check
        If Not suppressWarnings Then
            If opcDetected Then
                If (Range("neIfrIf").Value = 0 Or Range("neIfrIf").Value = "") And _
                   (Range("neIfrSim").Value = 0 Or Range("neIfrSim").Value = "") And _
                   Application.WorksheetFunction.CountIf(Range("neILS", "neCircling"), ">0") = 0 Then
                    response = MsgBox("OPC detected but no instrument hours/approaches recorded. Continue?", vbOKCancel + vbExclamation, "OPC Validation")
                    If response = vbCancel Then GoTo Cleanup
                End If
            End If
        End If

    '--- 4h. Approaches Without Instrument Hours Check
        If Not suppressWarnings Then
            If Application.WorksheetFunction.CountIf(Range("neILS", "neCircling"), ">0") > 0 Then
                If (Range("neIfrIf").Value = 0 Or Range("neIfrIf").Value = "") And _
                   (Range("neIfrSim").Value = 0 Or Range("neIfrSim").Value = "") Then
                    response = MsgBox("Warning: Approaches recorded with no Instrument hours. Continue?", vbOKCancel + vbExclamation, "Approaches Warning")
                    If response = vbCancel Then GoTo Cleanup
                End If
            End If
        End If

    '--- 4i. High Landings vs Hours Check
        'Total landings (day + night) should not exceed 6x total flight hours
        If Not suppressWarnings Then
            If (Range("neLandingsDay").Value + Range("neLandingsNight").Value) > _
                6 * Application.WorksheetFunction.Sum(Range("neSeIcusDay", "neCopilotNight")) Then
                response = MsgBox("Warning: Number of Landings seems high compared to number of hours. Continue?", vbOKCancel + vbExclamation, "High Landings Warning")
                If response = vbCancel Then GoTo Cleanup
            End If
        End If

    '--- 4j. High Approaches vs Hours Check
        'Total approaches should not exceed 3x total flight hours
        If Not suppressWarnings Then
            If Application.WorksheetFunction.Sum(Range("neILS", "neCircling")) > _
                3 * Application.WorksheetFunction.Sum(Range("neSeIcusDay", "neCopilotNight")) Then
                response = MsgBox("Warning: Number of Approaches seems high compared to number of hours. Continue?", vbOKCancel + vbExclamation, "High Approaches Warning")
                If response = vbCancel Then GoTo Cleanup
            End If
        End If

    '--- 4k. Dual / ICUS / Copilot Without Other Crew Check
        If Not suppressWarnings Then
            If Range("neOtherCrew").Value = "" Then
                If Application.WorksheetFunction.Sum( _
                    Range("neSeIcusDay"), Range("neSeIcusNight"), _
                    Range("neSeDualDay"), Range("neSeDualNight"), _
                    Range("neMeIcusDay"), Range("neMeIcusNight"), _
                    Range("neMeDualDay"), Range("neMeDualNight"), _
                    Range("neCopilotDay"), Range("neCopilotNight")) > 0 Then
                    response = MsgBox("Warning: Dual, ICUS, or Copilot hours recorded, but no Other Pilot or Crew recorded. Continue?", vbOKCancel + vbExclamation, "Other Crew Warning")
                    If response = vbCancel Then GoTo Cleanup
                End If
            End If
        End If

    '--- 4l. Duplicate Entry Check
        'Warn if an entry with the same Date, Type, Reg and Details already exists in the logbook
        Dim dateCol     As Long
        Dim detailsCol  As Long
        Dim typeCol     As Long
        Dim regCol      As Long
        Dim dupFound    As Boolean
        Dim rr          As Long

        dateCol = tbl.ListColumns("Date").Index
        detailsCol = tbl.ListColumns("Details").Index
        typeCol = tbl.ListColumns("Type").Index
        regCol = tbl.ListColumns("Reg").Index
        dupFound = False

        For rr = 1 To tbl.DataBodyRange.Rows.Count
            If tbl.DataBodyRange.cells(rr, dateCol).Value = Range("neDate").Value And _
               LCase(Trim(tbl.DataBodyRange.cells(rr, typeCol).Value)) = LCase(Trim(Range("neType").Value)) And _
               LCase(Trim(tbl.DataBodyRange.cells(rr, regCol).Value)) = LCase(Trim(Range("neReg").Value)) And _
               LCase(Trim(tbl.DataBodyRange.cells(rr, detailsCol).Value)) = LCase(Trim(Range("neDetails").Value)) Then
                dupFound = True
                Exit For
            End If
        Next rr

        If Not suppressWarnings Then
            If dupFound Then
                response = MsgBox("Warning: An entry with the same Date, Type, Registration and Details already exists in the Logbook. This may be a duplicate. Continue?", vbOKCancel + vbExclamation, "Duplicate Entry")
                If response = vbCancel Then GoTo Cleanup
            End If
        End If

    '===============================
    ' STEP 5: ADD NEW ROW TO TABLE
    '===============================
        diagStep = "Step 5a: Add Row"
        WriteCrumb diagStep

    '--- 5a. Add New Row & Copy Year, Month, Day
        Set newRow = tbl.ListRows.Add
        newRow.Range.cells(1, 2).Resize(1, 3).Value = Range("neYear", "neDay").Value

    '--- 5b. Fill Down Formula Columns from Previous Row
        diagStep = "Step 5b: Fill Formulas"
        WriteCrumb diagStep
        If tbl.ListRows.Count > 1 Then
            Dim iPrevRow As Long
            Dim iCol As Long
            iPrevRow = tbl.ListRows.Count - 1
            For iCol = 1 To tbl.ListColumns.Count
                If tbl.DataBodyRange.cells(iPrevRow, iCol).HasFormula Then
                    tbl.DataBodyRange.cells(iPrevRow, iCol).Resize(2, 1).FillDown
                End If
            Next iCol
        End If

    '--- 5c. Copy Remaining Data (Type through Circling)
        diagStep = "Step 5c: Copy Data"
        WriteCrumb diagStep
        Dim dataCols As Long
        dataCols = Range("neType", "neCircling").Columns.Count
        newRow.Range.cells(1, 5).Resize(1, dataCols).Value = Range("neType", "neCircling").Value

    '--- 5d. Fix Month Formatting (always Proper Case e.g. Mar)
        If VarType(newRow.Range.cells(1, 3).Value) = vbString Then
            newRow.Range.cells(1, 3).Value = StrConv(newRow.Range.cells(1, 3).Value, vbProperCase)
        End If

    '--- 5e. Fix Year Column Formatting (copy number format from previous row)
        diagStep = "Step 5e: Format Row"
        WriteCrumb diagStep
        newRow.Range.cells(1, 2).NumberFormat = _
            tbl.DataBodyRange.cells(tbl.ListRows.Count - 1, 2).NumberFormat

    '--- 5f. Fix Row Formatting (copy number formats from previous row, column by column)
        ' Avoids clipboard-based PasteSpecial which causes intermittent Excel crashes.
        ' Table stripe/fill/bold is controlled by the table style, not cell-level formats.
        Dim fmtCol As Long
        For fmtCol = 1 To tbl.ListColumns.Count
            newRow.Range.Cells(1, fmtCol).NumberFormat = _
                tbl.DataBodyRange.Cells(tbl.ListRows.Count - 1, fmtCol).NumberFormat
        Next fmtCol

    '--- 5g. Sort Logbook by Date
        diagStep = "Step 5g: Sort Logbook"
        WriteCrumb diagStep
        Application.Calculate
        SortLogbookByDate tbl

    '--- Persist the entry now so it survives a crash in the chart/pivot steps below
        diagStep = "Step 5: Save Entry"
        WriteCrumb diagStep
        On Error Resume Next
        ThisWorkbook.Save
        On Error GoTo Cleanup

    '===============================
    ' STEP 6: UPDATE CHART DATA
    '===============================
        diagStep = "Step 6a: Calculate"
        WriteCrumb diagStep
        Application.Calculate

        diagStep = "Step 6b: Update Chart"
        WriteCrumb diagStep
        UpdateHoursOverTimeChart ThisWorkbook

    '===============================
    ' STEP 7: RESET INPUT FORM
    '===============================
    ' Entry is already saved - errors here must not surface as failures to the user.
        diagStep = "Step 7: Reset Form"
        WriteCrumb diagStep
        On Error Resume Next

    '--- 7a. Reset Date Fields
        Select Case Range("DateAfterExport").Value
            Case 1
                ResetNewEntryDateFields DateAdd("d", 1, entryDate)
            Case 3
                ResetNewEntryDateFields todayDate
        End Select

    '--- 7b. Reset PIC to Default
        Range("nePIC").Value = "Self"

    '--- 7c. Clear Remaining Input Fields
        Union(Range("neType", "neReg"), Range("neOtherCrew", "neDetails"), _
              Range("neSI1", "neCircling")).ClearContents

    '--- 7d. Update Hidden Rows
        UpdateHiddenRows ThisWorkbook

        On Error GoTo Cleanup

    '===============================
    ' STEP 8: REFRESH DATA
    '===============================
        diagStep = "Step 8a: Add Routes"
        WriteCrumb diagStep
        On Error Resume Next
        Call AddNewRoutes
        On Error GoTo Cleanup

        '--- Refresh all pivot tables in the workbook
        diagStep = "Step 8b: Refresh Pivots"
        WriteCrumb diagStep
        DoEvents
        Dim ws  As Worksheet
        Dim pt  As PivotTable
        On Error Resume Next
        For Each ws In ThisWorkbook.Worksheets
            For Each pt In ws.PivotTables
                pt.RefreshTable
            Next pt
        Next ws
        FixHoursByYearPivotLayout ThisWorkbook
        On Error GoTo Cleanup

    '===============================
    ' STEP 9: SUCCESS MESSAGE
    '===============================
        diagStep = "Step 9: Success"

        MsgBox "Entry successfully added to Logbook!", vbInformation

    '===============================
    ' STEP 10: CLEANUP & RESTORE SETTINGS
    '===============================

Cleanup:
        '--- Capture error details before restoring settings (restoring clears Err object)
        Dim errNum      As Long
        Dim errDesc     As String
        errNum = Err.Number
        errDesc = Err.Description

        Application.ScreenUpdating = True
        Application.EnableEvents = True
        Application.Calculation = xlCalculationAutomatic
        Application.CutCopyMode = False

        '--- Report any unexpected error (errNum 0 means clean exit via GoTo Cleanup on cancel)
        If errNum <> 0 Then
            On Error Resume Next
            WriteDebugLog "AddToLogbook", errNum, errDesc, diagStep
            On Error GoTo 0
            MsgBox "An unexpected error occurred and the entry may not have been saved." & vbNewLine & vbNewLine & _
                   "Error " & errNum & ": " & errDesc & vbNewLine & vbNewLine & _
                   "Please check the logbook before adding another entry.", vbCritical, "Unexpected Error"
        End If

        Exit Sub

End Sub

Private Function ListColumnExists(ByVal tbl As ListObject, ByVal columnName As String) As Boolean
    On Error Resume Next
    ListColumnExists = Not tbl.ListColumns(columnName) Is Nothing
    On Error GoTo 0
End Function

Private Function KeywordTableIsValid() As Boolean
    Dim tblKeywords As ListObject

    Set tblKeywords = FindListObject(ThisWorkbook, "Keywords")
    If tblKeywords Is Nothing Then Exit Function

    KeywordTableIsValid = _
        ListColumnExists(tblKeywords, "Flight Review") And _
        ListColumnExists(tblKeywords, "IPC") And _
        ListColumnExists(tblKeywords, "OPC")
End Function

Private Function KeywordDetected(ByVal details As String, ByVal keywordColumn As String) As Boolean
    Dim tblKeywords As ListObject
    Dim keywordRange As Range
    Dim keywordCell As Range
    Dim normalizedDetails As String
    Dim normalizedKeyword As String

    Set tblKeywords = FindListObject(ThisWorkbook, "Keywords")
    If tblKeywords Is Nothing Then Exit Function

    On Error Resume Next
    Set keywordRange = tblKeywords.ListColumns(keywordColumn).DataBodyRange
    On Error GoTo 0
    If keywordRange Is Nothing Then Exit Function

    normalizedDetails = NormalizeKeywordText(details, True)
    For Each keywordCell In keywordRange.Cells
        If Not IsError(keywordCell.Value) Then
            If Trim$(CStr(keywordCell.Value)) <> "" Then
                normalizedKeyword = NormalizeKeywordText(CStr(keywordCell.Value))
                If InStr(1, normalizedDetails, normalizedKeyword, vbBinaryCompare) > 0 Then
                    KeywordDetected = True
                    Exit Function
                End If
            End If
        End If
    Next keywordCell
End Function

Private Function NormalizeKeywordText(ByVal value As String, _
                                      Optional ByVal removeSeparator As Boolean = False) As String
    If removeSeparator Then value = Replace(value, "|", "")
    value = LCase$(value)
    value = Replace(value, "(", "|")
    value = Replace(value, ")", "|")
    value = Replace(value, "-", "|")
    value = Replace(value, ",", "|")
    value = Replace(value, " ", "|")
    value = Replace(value, "&", "|")
    NormalizeKeywordText = "|" & value & "|"
End Function

Private Function DetectedCurrencyItemsText(ByVal ipcDetected As Boolean, _
                                           ByVal opcDetected As Boolean, _
                                           ByVal flightReviewDetected As Boolean) As String
    Dim items(1 To 3) As String
    Dim itemCount As Long

    If ipcDetected Then
        itemCount = itemCount + 1
        items(itemCount) = "an IPC"
    End If
    If opcDetected Then
        itemCount = itemCount + 1
        items(itemCount) = "an OPC"
    End If
    If flightReviewDetected Then
        itemCount = itemCount + 1
        items(itemCount) = "a Flight Review"
    End If

    Select Case itemCount
        Case 1
            DetectedCurrencyItemsText = items(1)
        Case 2
            DetectedCurrencyItemsText = items(1) & " and " & items(2)
        Case 3
            DetectedCurrencyItemsText = items(1) & ", " & items(2) & " and " & items(3)
    End Select
End Function

Private Function FindListObject(ByVal wb As Workbook, ByVal tableName As String) As ListObject
    Dim ws As Worksheet

    On Error Resume Next
    For Each ws In wb.Worksheets
        Set FindListObject = ws.ListObjects(tableName)
        If Not FindListObject Is Nothing Then Exit Function
    Next ws
    On Error GoTo 0
End Function

Private Sub FixHoursByYearPivotLayout(Optional ByVal wb As Workbook = Nothing)
    If wb Is Nothing Then Set wb = ThisWorkbook

    On Error GoTo CleanExit

    Dim pt As PivotTable
    Set pt = wb.Worksheets("ChartData").PivotTables("HoursByYear")

    pt.ManualUpdate = True

    ' If Excel ever loses the grouped Years field, try to regroup Date by Years only.
    If Not PivotFieldExists(pt, "Years (Date)") Then
        On Error Resume Next
        pt.PivotFields("Date").DataRange.Cells(1).Group _
            Start:=True, End:=True, _
            Periods:=Array(False, False, False, False, False, False, True)
        On Error GoTo CleanExit
    End If

    ' Hide lower-level date fields that cause labels like "May 2026".
    HidePivotFieldIfExists pt, "Date"
    HidePivotFieldIfExists pt, "Months (Date)"
    HidePivotFieldIfExists pt, "Days (Date)"
    HidePivotFieldIfExists pt, "Quarters (Date)"

    ' Keep only Years on the category axis.
    With pt.PivotFields("Years (Date)")
        .Orientation = xlRowField
        .Position = 1
    End With

CleanExit:
    On Error Resume Next
    pt.ManualUpdate = False
End Sub

Private Sub HidePivotFieldIfExists(ByVal pt As PivotTable, ByVal fieldName As String)
    On Error Resume Next
    pt.PivotFields(fieldName).Orientation = xlHidden
    On Error GoTo 0
End Sub

Private Function PivotFieldExists(ByVal pt As PivotTable, ByVal fieldName As String) As Boolean
    On Error Resume Next
    PivotFieldExists = Not pt.PivotFields(fieldName) Is Nothing
    On Error GoTo 0
End Function

Private Sub ResetNewEntryDateFields(ByVal targetDate As Date)
    Range("neYear").Value = Year(targetDate)
    Range("neMonth").Value = Format$(targetDate, "mmm")
    Range("neDay").Value = Day(targetDate)

    Range("neYear").NumberFormat = "0"
    Range("neMonth").NumberFormat = "@"
    Range("neDay").NumberFormat = "0"

    Range("neDate").Formula = NewEntryDateFormula()
End Sub

Private Sub RefreshDateCalculationFormulas(ByVal tbl As ListObject)
    Range("neDate").Formula = NewEntryDateFormula()

    If Not tbl.DataBodyRange Is Nothing Then
        tbl.ListColumns("Date").DataBodyRange.Formula = LogbookDateFormula()
    End If
End Sub

Private Sub SortLogbookByDate(ByVal tbl As ListObject)
    If tbl.DataBodyRange Is Nothing Then Exit Sub

    With tbl.Sort
        .SortFields.Clear
        .SortFields.Add Key:=tbl.ListColumns("Date").DataBodyRange, _
                        SortOn:=xlSortOnValues, _
                        Order:=xlAscending, _
                        DataOption:=xlSortNormal
        .Header = xlYes
        .MatchCase = False
        .Apply
    End With
End Sub

Private Function NewEntryDateFormula() As String
    NewEntryDateFormula = _
        "=IFERROR(IF(DAY(DATE(neYear," & _
        "IF(ISNUMBER(neMonth),MONTH(neMonth),MONTH(DATEVALUE(neMonth&"" 1"")))," & _
        "IF(ISNUMBER(neDay),IF(neDay>31,DAY(neDay),neDay),VALUE(neDay))))<>" & _
        "IF(ISNUMBER(neDay),IF(neDay>31,DAY(neDay),neDay),VALUE(neDay))," & _
        "NA(),DATE(neYear," & _
        "IF(ISNUMBER(neMonth),MONTH(neMonth),MONTH(DATEVALUE(neMonth&"" 1"")))," & _
        "IF(ISNUMBER(neDay),IF(neDay>31,DAY(neDay),neDay),VALUE(neDay))))," & _
        "NA())"
End Function

Private Function LogbookDateFormula() As String
    LogbookDateFormula = _
        "=IFERROR(IF(DAY(DATE([@Year]," & _
        "IF(ISNUMBER([@Month]),MONTH([@Month]),MONTH(DATEVALUE([@Month]&"" 1"")))," & _
        "IF(ISNUMBER([@Day]),IF([@Day]>31,DAY([@Day]),[@Day]),VALUE([@Day]))))<>" & _
        "IF(ISNUMBER([@Day]),IF([@Day]>31,DAY([@Day]),[@Day]),VALUE([@Day]))," & _
        "NA(),DATE([@Year]," & _
        "IF(ISNUMBER([@Month]),MONTH([@Month]),MONTH(DATEVALUE([@Month]&"" 1"")))," & _
        "IF(ISNUMBER([@Day]),IF([@Day]>31,DAY([@Day]),[@Day]),VALUE([@Day]))))," & _
        "NA())"
End Function

Private Function GetLatestLogbookEntryDate(ByVal tbl As ListObject) As Date
    Dim dateCol As Long
    Dim dateCell As Range
    Dim latestDate As Date

    latestDate = 0
    dateCol = tbl.ListColumns("Date").Index

    If Not tbl.DataBodyRange Is Nothing Then
        For Each dateCell In tbl.ListColumns(dateCol).DataBodyRange.Cells
            If IsDate(dateCell.Value) Then
                If CDate(dateCell.Value) > latestDate Then
                    latestDate = CDate(dateCell.Value)
                End If
            End If
        Next dateCell
    End If

    GetLatestLogbookEntryDate = latestDate
End Function

Private Function IsLogbookRowSimOnly(ByVal tbl As ListObject, ByVal rowIndex As Long) As Boolean
    Dim simHours As Double
    Dim otherHours As Double
    Dim firstHourCol As Long
    Dim lastOtherHourCol As Long

    If tbl.DataBodyRange Is Nothing Then Exit Function
    If rowIndex < 1 Or rowIndex > tbl.DataBodyRange.Rows.Count Then Exit Function

    simHours = Val(tbl.ListColumns("IfrSim").DataBodyRange.cells(rowIndex, 1).Value)
    firstHourCol = tbl.ListColumns("SeIcusDay").Index
    lastOtherHourCol = tbl.ListColumns("IfrIf").Index

    otherHours = Application.WorksheetFunction.Sum( _
        tbl.DataBodyRange.cells(rowIndex, firstHourCol).Resize(1, lastOtherHourCol - firstHourCol + 1))

    IsLogbookRowSimOnly = (simHours > 0 And otherHours = 0)
End Function

Public Sub UpdateHiddenRows(wb As Workbook)
    Dim wsLog       As Worksheet
    Dim tbl         As ListObject
    Dim lastDataRow As Long

    Set wsLog = wb.Sheets("Logbook")
    Set tbl = wsLog.ListObjects("Logbook")

    lastDataRow = tbl.DataBodyRange.row + tbl.DataBodyRange.Rows.Count - 1
    wsLog.Rows.Hidden = False
    If lastDataRow + 4 <= wsLog.Rows.Count Then
        wsLog.Rows(lastDataRow + 4 & ":" & wsLog.Rows.Count).Hidden = True
    End If
End Sub

Public Sub UpdateHoursOverTimeChart(wb As Workbook)
    Dim wsCharts As Worksheet
    Dim wsData   As Worksheet
    Dim rnhRange As Range
    Dim rng      As Range
    Dim lastRow  As Long

    Set wsCharts = wb.Sheets("Charts")
    Set wsData = wb.Sheets("ChartData")
    Set rnhRange = wb.Names("RunningTotalHours").RefersToRange

    lastRow = wsData.cells(wsData.Rows.Count, rnhRange.Columns(1).Column).End(xlUp).row
    If lastRow < 2 Then Exit Sub
    Set rng = wsData.Range( _
        wsData.cells(2, rnhRange.Columns(1).Column), _
        wsData.cells(lastRow, rnhRange.Columns(2).Column))

    wsCharts.ChartObjects("HoursOverTime").Chart.SeriesCollection(1).XValues = rng.Columns(1)
    wsCharts.ChartObjects("HoursOverTime").Chart.SeriesCollection(1).Values = rng.Columns(2)
End Sub

Public Sub MarkRoutesDirty(Optional wb As Workbook = Nothing)
    If wb Is Nothing Then Set wb = ThisWorkbook
    SetWorkbookNameValue wb, "RoutesDirty", True
End Sub

Public Sub MarkRoutesClean(Optional wb As Workbook = Nothing)
    If wb Is Nothing Then Set wb = ThisWorkbook
    SetWorkbookNameValue wb, "RoutesBuilt", True
    SetWorkbookNameValue wb, "RoutesDirty", False
    SetWorkbookNameValue wb, "RoutesDefinitionVersion", ROUTE_DEFINITION_VERSION
End Sub

Public Sub RebuildRoutesTable(Optional wb As Workbook = Nothing)
    If wb Is Nothing Then Set wb = ThisWorkbook
    BuildRoutesTable wb
    MsgBox "Routes table rebuilt successfully.", vbInformation, "Routes Rebuilt"
End Sub

Public Sub RebuildRoutesTableNow()
    RebuildRoutesTable ThisWorkbook
End Sub

Public Sub CheckRoutesTableOnOpen(Optional wb As Workbook = Nothing)
    If wb Is Nothing Then Set wb = ThisWorkbook

    If RoutesBuiltState(wb) = "" Then
        If MsgBox("Your Routes table needs to be built for the first time." & vbCrLf & vbCrLf & _
                  "This scans your logbook and builds your route map data." & vbCrLf & _
                  "It takes around 60 seconds per 300 entries. Build now?", _
                  vbYesNo + vbInformation, "Build Routes Table") = vbYes Then
            BuildRoutesTable wb
            MsgBox "Routes table built successfully.", vbInformation
        End If
        Exit Sub
    End If

    If RouteDefinitionNeedsRebuild(wb) Then
        If MsgBox("The route definition has changed in this version of the logbook." & vbCrLf & vbCrLf & _
                  "Rebuild the Routes table now so route map exports use the latest parser?", _
                  vbYesNo + vbInformation, "Rebuild Routes Table") = vbYes Then
            BuildRoutesTable wb
            MsgBox "Routes table rebuilt successfully.", vbInformation
        End If
    End If
End Sub

Public Function EnsureRoutesReadyForExport(Optional wb As Workbook = Nothing) As Boolean
    If wb Is Nothing Then Set wb = ThisWorkbook
    EnsureRoutesReadyForExport = True

    If RoutesBuiltState(wb) = "" Then
        If MsgBox("Your Routes table has not been built yet." & vbCrLf & vbCrLf & _
                  "Build it now before exporting the route map?", _
                  vbYesNo + vbInformation, "Build Routes Table") = vbYes Then
            BuildRoutesTable wb
        Else
            EnsureRoutesReadyForExport = False
        End If
        Exit Function
    End If

    If RouteDefinitionNeedsRebuild(wb) Then
        If MsgBox("The route definition has changed since this Routes table was built." & vbCrLf & vbCrLf & _
                  "Rebuild now before exporting?", _
                  vbYesNo + vbExclamation, "Routes May Be Out Of Date") = vbYes Then
            BuildRoutesTable wb
        End If
    ElseIf RoutesDirtyState(wb) Then
        If MsgBox("Your Routes table may be out of date because existing logbook entries or airport codes were changed." & vbCrLf & vbCrLf & _
                  "Rebuild now before exporting?", _
                  vbYesNo + vbExclamation, "Routes May Be Out Of Date") = vbYes Then
            BuildRoutesTable wb
        End If
    End If
End Function

Private Function RoutesBuiltState(wb As Workbook) As String
    RoutesBuiltState = Trim(CStr(GetWorkbookNameValue(wb, "RoutesBuilt", "")))
End Function

Private Function RoutesDirtyState(wb As Workbook) As Boolean
    Dim value As Variant
    value = GetWorkbookNameValue(wb, "RoutesDirty", False)
    RoutesDirtyState = (LCase$(Trim$(CStr(value))) = "true" Or Trim$(CStr(value)) = "1")
End Function

Private Function RouteDefinitionNeedsRebuild(wb As Workbook) As Boolean
    Dim value As Variant
    value = GetWorkbookNameValue(wb, "RoutesDefinitionVersion", 0)
    If Not IsNumeric(value) Then
        RouteDefinitionNeedsRebuild = True
    Else
        RouteDefinitionNeedsRebuild = (CLng(value) <> ROUTE_DEFINITION_VERSION)
    End If
End Function

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

Public Sub BuildRoutesTable(wb As Workbook)

    '===============================
    ' STEP 1: SETUP
    '===============================
        Dim wsLog       As Worksheet
        Dim wsRoutes    As Worksheet
        Dim tblLog      As ListObject
        Dim tblAirports As ListObject
        Dim tblRoutes   As ListObject

        Set wsLog = wb.Sheets("Logbook")
        Set wsRoutes = wb.Sheets("Routes")
        Set tblLog = wsLog.ListObjects("Logbook")
        Set tblAirports = wb.Sheets("Airports").ListObjects("Airports")
        Set tblRoutes = wsRoutes.ListObjects("Routes")

    '===============================
    ' STEP 2: BUILD AIRPORT LOOKUP DICTIONARY
    '===============================
        Dim dict As Object
        Set dict = CreateObject("Scripting.Dictionary")
        dict.CompareMode = 1

        Dim icaoCol  As Long
        Dim twoCol   As Long
        Dim threeCol As Long
        icaoCol = tblAirports.ListColumns("ICAO").Index
        twoCol = tblAirports.ListColumns("Two").Index
        threeCol = tblAirports.ListColumns("Three").Index

        Dim r As Long
        For r = 1 To tblAirports.DataBodyRange.Rows.Count
            Dim icao  As String
            Dim two   As String
            Dim three As String
            icao = Trim(tblAirports.DataBodyRange.cells(r, icaoCol).Value)
            two = Trim(tblAirports.DataBodyRange.cells(r, twoCol).Value)
            three = Trim(tblAirports.DataBodyRange.cells(r, threeCol).Value)
            If icao <> "" Then
                If Not dict.Exists(icao) Then dict.Add icao, icao
                If two <> "" And Not dict.Exists(two) Then dict.Add two, icao
                If three <> "" And Not dict.Exists(three) Then dict.Add three, icao
            End If
        Next r

    '===============================
    ' STEP 3: EXTRACT ROUTES FROM LOGBOOK
    '===============================
        Dim detailsCol As Long
        detailsCol = tblLog.ListColumns("Details").Index

        Dim allRoutes() As String
        Dim routeCount  As Long
        routeCount = 0
        ReDim allRoutes(1 To 10000, 1 To 2)

        Dim delimiters As Variant
        delimiters = Array("-", " ", ",", "(", ")")

        Dim row As Long
        For row = 1 To tblLog.DataBodyRange.Rows.Count
            If IsLogbookRowSimOnly(tblLog, row) Then GoTo NextRow

            Dim details As String
            details = Trim(tblLog.DataBodyRange.cells(row, detailsCol).Value)
            If details = "" Then GoTo NextRow

            Dim d As Long
            For d = 0 To UBound(delimiters)
                details = Join(Split(details, delimiters(d)), "|")
            Next d

            Dim tokens() As String
            tokens = Split(details, "|")

            Dim matchedCodes() As String
            Dim matchCount As Long
            matchCount = 0
            ReDim matchedCodes(1 To UBound(tokens) + 1)

            Dim t As Long
            For t = 0 To UBound(tokens)
                Dim token As String
                token = Trim(tokens(t))
                If token <> "" Then
                    If Not IsRouteParserIgnoreToken(token) Then
                        If dict.Exists(token) Then
                            matchCount = matchCount + 1
                            matchedCodes(matchCount) = dict(token)
                        End If
                    End If
                End If
            Next t

            Dim p As Long
            For p = 1 To matchCount - 1
                If matchedCodes(p) <> matchedCodes(p + 1) Then
                    routeCount = routeCount + 1
                    allRoutes(routeCount, 1) = matchedCodes(p)
                    allRoutes(routeCount, 2) = matchedCodes(p + 1)
                End If
            Next p

NextRow:
        Next row

    '===============================
    ' STEP 4: CLEAR AND REPOPULATE ROUTES TABLE
    '===============================
        If Not tblRoutes.DataBodyRange Is Nothing Then
            tblRoutes.DataBodyRange.Delete
        End If

        If routeCount = 0 Then GoTo Done

        Dim depCol    As Long
        Dim arrCol    As Long
        Dim depLatCol As Long
        Dim depLonCol As Long
        Dim arrLatCol As Long
        Dim arrLonCol As Long

        depCol = tblRoutes.ListColumns("DepAirport").Index
        arrCol = tblRoutes.ListColumns("ArrAirport").Index
        depLatCol = tblRoutes.ListColumns("DepLat").Index
        depLonCol = tblRoutes.ListColumns("DepLon").Index
        arrLatCol = tblRoutes.ListColumns("ArrLat").Index
        arrLonCol = tblRoutes.ListColumns("ArrLon").Index

        Dim latCol As Long
        Dim lonCol As Long
        latCol = tblAirports.ListColumns("Latitude").Index
        lonCol = tblAirports.ListColumns("Longitude").Index

        For p = 1 To routeCount
            Dim newRoute As ListRow
            Set newRoute = tblRoutes.ListRows.Add

            Dim depICAO As String
            Dim arrICAO As String
            depICAO = allRoutes(p, 1)
            arrICAO = allRoutes(p, 2)

            newRoute.Range.cells(1, depCol).Value = depICAO
            newRoute.Range.cells(1, arrCol).Value = arrICAO

            newRoute.Range.cells(1, depLatCol).formula = "=IFERROR(INDEX(Airports[Latitude],MATCH([@DepAirport],Airports[ICAO],0)),"""")"
            newRoute.Range.cells(1, depLonCol).formula = "=IFERROR(INDEX(Airports[Longitude],MATCH([@DepAirport],Airports[ICAO],0)),"""")"
            newRoute.Range.cells(1, arrLatCol).formula = "=IFERROR(INDEX(Airports[Latitude],MATCH([@ArrAirport],Airports[ICAO],0)),"""")"
            newRoute.Range.cells(1, arrLonCol).formula = "=IFERROR(INDEX(Airports[Longitude],MATCH([@ArrAirport],Airports[ICAO],0)),"""")"
            newRoute.Range.cells(1, tblRoutes.ListColumns("Distance").Index).formula = _
                "=2*3440.065*ASIN(SQRT(SIN(RADIANS(([@ArrLat]-[@DepLat])/2))^2+COS(RADIANS([@DepLat]))*COS(RADIANS([@ArrLat]))*SIN(RADIANS(([@ArrLon]-[@DepLon])/2))^2))"
        Next p

Done:
        MarkRoutesClean wb
End Sub

Sub AddNewRoutes()

    '===============================
    ' STEP 1: SETUP
    '===============================
        Dim wsLog       As Worksheet
        Dim wsRoutes    As Worksheet
        Dim tblLog      As ListObject
        Dim tblAirports As ListObject
        Dim tblRoutes   As ListObject

        Set wsLog = ThisWorkbook.Sheets("Logbook")
        Set wsRoutes = ThisWorkbook.Sheets("Routes")
        Set tblLog = wsLog.ListObjects("Logbook")
        Set tblAirports = ThisWorkbook.Sheets("Airports").ListObjects("Airports")
        Set tblRoutes = wsRoutes.ListObjects("Routes")

    '===============================
    ' STEP 2: BUILD AIRPORT LOOKUP DICTIONARY
    '===============================
        Dim dict As Object
        Set dict = CreateObject("Scripting.Dictionary")
        dict.CompareMode = 1

        Dim icaoCol  As Long
        Dim twoCol   As Long
        Dim threeCol As Long
        icaoCol = tblAirports.ListColumns("ICAO").Index
        twoCol = tblAirports.ListColumns("Two").Index
        threeCol = tblAirports.ListColumns("Three").Index

        Dim r As Long
        For r = 1 To tblAirports.DataBodyRange.Rows.Count
            Dim icao  As String
            Dim two   As String
            Dim three As String
            icao = Trim(tblAirports.DataBodyRange.cells(r, icaoCol).Value)
            two = Trim(tblAirports.DataBodyRange.cells(r, twoCol).Value)
            three = Trim(tblAirports.DataBodyRange.cells(r, threeCol).Value)
            If icao <> "" Then
                If Not dict.Exists(icao) Then dict.Add icao, icao
                If two <> "" And Not dict.Exists(two) Then dict.Add two, icao
                If three <> "" And Not dict.Exists(three) Then dict.Add three, icao
            End If
        Next r

    '===============================
    ' STEP 3: GET DETAILS FROM LAST LOGBOOK ROW ONLY
    '===============================
        Dim lastLogRow As Long
        lastLogRow = tblLog.DataBodyRange.Rows.Count
        If IsLogbookRowSimOnly(tblLog, lastLogRow) Then Exit Sub

        Dim detailsCol As Long
        detailsCol = tblLog.ListColumns("Details").Index

        Dim details As String
        details = Trim(tblLog.DataBodyRange.cells(lastLogRow, detailsCol).Value)
        If details = "" Then Exit Sub

    '===============================
    ' STEP 4: TOKENISE AND MATCH
    '===============================
        Dim delimiters As Variant
        delimiters = Array("-", " ", ",", "(", ")")

        Dim d As Long
        For d = 0 To UBound(delimiters)
            details = Join(Split(details, delimiters(d)), "|")
        Next d

        Dim tokens() As String
        tokens = Split(details, "|")

        Dim matchedCodes() As String
        Dim matchCount As Long
        matchCount = 0
        ReDim matchedCodes(1 To UBound(tokens) + 1)

        Dim t As Long
        For t = 0 To UBound(tokens)
            Dim token As String
            token = Trim(tokens(t))
            If token <> "" Then
                If Not IsRouteParserIgnoreToken(token) Then
                    If dict.Exists(token) Then
                        matchCount = matchCount + 1
                        matchedCodes(matchCount) = dict(token)
                    End If
                End If
            End If
        Next t

    '===============================
    ' STEP 5: APPEND NEW ROUTES ONLY
    '===============================
        Dim depCol    As Long
        Dim arrCol    As Long
        Dim depLatCol As Long
        Dim depLonCol As Long
        Dim arrLatCol As Long
        Dim arrLonCol As Long

        depCol = tblRoutes.ListColumns("DepAirport").Index
        arrCol = tblRoutes.ListColumns("ArrAirport").Index
        depLatCol = tblRoutes.ListColumns("DepLat").Index
        depLonCol = tblRoutes.ListColumns("DepLon").Index
        arrLatCol = tblRoutes.ListColumns("ArrLat").Index
        arrLonCol = tblRoutes.ListColumns("ArrLon").Index

        Dim p As Long
        For p = 1 To matchCount - 1
            If matchedCodes(p) <> matchedCodes(p + 1) Then
                Dim newRoute As ListRow
                Set newRoute = tblRoutes.ListRows.Add

                newRoute.Range.cells(1, depCol).Value = matchedCodes(p)
                newRoute.Range.cells(1, arrCol).Value = matchedCodes(p + 1)
                newRoute.Range.cells(1, depLatCol).formula = "=IFERROR(INDEX(Airports[Latitude],MATCH([@DepAirport],Airports[ICAO],0)),"""")"
                newRoute.Range.cells(1, depLonCol).formula = "=IFERROR(INDEX(Airports[Longitude],MATCH([@DepAirport],Airports[ICAO],0)),"""")"
                newRoute.Range.cells(1, arrLatCol).formula = "=IFERROR(INDEX(Airports[Latitude],MATCH([@ArrAirport],Airports[ICAO],0)),"""")"
                newRoute.Range.cells(1, arrLonCol).formula = "=IFERROR(INDEX(Airports[Longitude],MATCH([@ArrAirport],Airports[ICAO],0)),"""")"
                newRoute.Range.cells(1, tblRoutes.ListColumns("Distance").Index).formula = _
                "=2*3440.065*ASIN(SQRT(SIN(RADIANS(([@ArrLat]-[@DepLat])/2))^2+COS(RADIANS([@DepLat]))*COS(RADIANS([@ArrLat]))*SIN(RADIANS(([@ArrLon]-[@DepLon])/2))^2))"
            End If
        Next p

End Sub

Private Function IsRouteParserIgnoreToken(ByVal token As String) As Boolean
    Select Case UCase$(Trim$(token))
        Case "IPC", "OPC", "FR", "IR", "IFR", "VFR", "TEST", "CHECK", "CIRCLING", "SIM"
            IsRouteParserIgnoreToken = True
        Case Else
            IsRouteParserIgnoreToken = KeywordTableContainsToken(token)
    End Select
End Function

Private Function KeywordTableContainsToken(ByVal token As String) As Boolean
    Dim tblKeywords As ListObject
    Dim keywordColumn As ListColumn
    Dim keywordCell As Range
    Dim normalizedToken As String

    Set tblKeywords = FindListObject(ThisWorkbook, "Keywords")
    If tblKeywords Is Nothing Then Exit Function

    normalizedToken = NormalizeKeywordText(token)
    For Each keywordColumn In tblKeywords.ListColumns
        If Not keywordColumn.DataBodyRange Is Nothing Then
            For Each keywordCell In keywordColumn.DataBodyRange.Cells
                If Not IsError(keywordCell.Value) Then
                    If Trim$(CStr(keywordCell.Value)) <> "" Then
                        If InStr(1, NormalizeKeywordText(CStr(keywordCell.Value)), _
                                   normalizedToken, vbBinaryCompare) > 0 Then
                            KeywordTableContainsToken = True
                            Exit Function
                        End If
                    End If
                End If
            Next keywordCell
        End If
    Next keywordColumn
End Function

Sub ExportKeplerJSON()

    If Not EnsureRoutesReadyForExport(ThisWorkbook) Then Exit Sub

    '===============================
    ' STEP 1: SETUP
    '===============================
        Dim wsAirports  As Worksheet
        Dim wsRoutes    As Worksheet
        Dim tblAirports As ListObject
        Dim tblRoutes   As ListObject

        Set wsAirports = ThisWorkbook.Sheets("Airports")
        Set wsRoutes = ThisWorkbook.Sheets("Routes")
        Set tblAirports = wsAirports.ListObjects("Airports")
        Set tblRoutes = wsRoutes.ListObjects("Routes")

    '===============================
    ' STEP 2: BUILD ROUTES ALLDATA
    '===============================
        Dim depAirportCol   As Long
        Dim arrAirportCol   As Long
        Dim depLatCol       As Long
        Dim depLonCol       As Long
        Dim arrLatCol       As Long
        Dim arrLonCol       As Long

        depAirportCol = tblRoutes.ListColumns("DepAirport").Index
        arrAirportCol = tblRoutes.ListColumns("ArrAirport").Index
        depLatCol = tblRoutes.ListColumns("DepLat").Index
        depLonCol = tblRoutes.ListColumns("DepLon").Index
        arrLatCol = tblRoutes.ListColumns("ArrLat").Index
        arrLonCol = tblRoutes.ListColumns("ArrLon").Index

        Dim routesData As String
        routesData = ""
        If tblRoutes.DataBodyRange Is Nothing Then GoTo RoutesDataBuilt

        Dim r As Long
        For r = 1 To tblRoutes.DataBodyRange.Rows.Count
            Dim depAirport  As String
            Dim arrAirport  As String
            Dim depLat      As String
            Dim depLon      As String
            Dim arrLat      As String
            Dim arrLon      As String

            depAirport = tblRoutes.DataBodyRange.cells(r, depAirportCol).Value
            arrAirport = tblRoutes.DataBodyRange.cells(r, arrAirportCol).Value
            depLat = Format(tblRoutes.DataBodyRange.cells(r, depLatCol).Value, "0.######")
            depLon = Format(tblRoutes.DataBodyRange.cells(r, depLonCol).Value, "0.######")
            arrLat = Format(tblRoutes.DataBodyRange.cells(r, arrLatCol).Value, "0.######")
            arrLon = Format(tblRoutes.DataBodyRange.cells(r, arrLonCol).Value, "0.######")

            If depAirport = "" Or arrAirport = "" Then GoTo NextRoute
            If depLat = "0" Or arrLat = "0" Then GoTo NextRoute

            If routesData <> "" Then routesData = routesData & ","
            routesData = routesData & "[""" & depAirport & """,""" & arrAirport & """," & _
                         depLat & "," & depLon & "," & arrLat & "," & arrLon & "]"
NextRoute:
        Next r

RoutesDataBuilt:

    '===============================
    ' STEP 3: BUILD AIRPORTS ALLDATA
    '===============================
        Dim icaoCol     As Long
        Dim airportCol  As Long
        Dim latCol      As Long
        Dim lonCol      As Long
        Dim visitsCol   As Long

        icaoCol = tblAirports.ListColumns("ICAO").Index
        airportCol = tblAirports.ListColumns("Airport").Index
        latCol = tblAirports.ListColumns("Latitude").Index
        lonCol = tblAirports.ListColumns("Longitude").Index
        visitsCol = tblAirports.ListColumns("Visits").Index

        Dim airportsData As String
        airportsData = ""
        For r = 1 To tblAirports.DataBodyRange.Rows.Count
            Dim icao    As String
            Dim airport As String
            Dim lat     As String
            Dim lon     As String
            Dim visits  As String

            icao = tblAirports.DataBodyRange.cells(r, icaoCol).Value
            airport = tblAirports.DataBodyRange.cells(r, airportCol).Value
            lat = Format(tblAirports.DataBodyRange.cells(r, latCol).Value, "0.######")
            lon = Format(tblAirports.DataBodyRange.cells(r, lonCol).Value, "0.######")
            visits = tblAirports.DataBodyRange.cells(r, visitsCol).Value

            If icao = "" Or Not IsNumeric(visits) Then GoTo NextAirport
            If CDbl(visits) <= 0 Then GoTo NextAirport

            'Escape any quotes in airport name
            airport = Replace(airport, """", "\""")

            If airportsData <> "" Then airportsData = airportsData & ","
            airportsData = airportsData & "[""" & icao & """,""" & airport & """," & _
                           lat & "," & lon & "," & visits & "]"
NextAirport:
        Next r

    '===============================
    ' STEP 4: ASSEMBLE FULL JSON
    '===============================
        Dim json As String
        Dim cfg  As String

        '--- Routes dataset
        json = "{""datasets"":[{""version"":""v1"",""data"":{""id"":""-b3npja"","
        json = json & """label"":""timroutes.csv"",""color"":[143,47,191],""allData"":["
        json = json & routesData
        json = json & "],""fields"":["
        json = json & "{""name"":""DepAirport"",""type"":""string"",""format"":"""",""analyzerType"":""STRING""},"
        json = json & "{""name"":""ArrAirport"",""type"":""string"",""format"":"""",""analyzerType"":""STRING""},"
        json = json & "{""name"":""DepLat"",""type"":""real"",""format"":"""",""analyzerType"":""FLOAT""},"
        json = json & "{""name"":""DepLon"",""type"":""real"",""format"":"""",""analyzerType"":""FLOAT""},"
        json = json & "{""name"":""ArrLat"",""type"":""real"",""format"":"""",""analyzerType"":""FLOAT""},"
        json = json & "{""name"":""ArrLon"",""type"":""real"",""format"":"""",""analyzerType"":""FLOAT""}],"
        json = json & """type"":"""",""metadata"":{""id"":""-b3npja"",""format"":""row"","
        json = json & """label"":""timroutes.csv""},""disableDataOperation"":false}},"

        '--- Airports dataset
        json = json & "{""version"":""v1"",""data"":{""id"":""2apqg0"","
        json = json & """label"":""airports.csv"",""color"":[192,108,132],""allData"":["
        json = json & airportsData
        json = json & "],""fields"":["
        json = json & "{""name"":""ICAO"",""type"":""string"",""format"":"""",""analyzerType"":""STRING""},"
        json = json & "{""name"":""Airport"",""type"":""string"",""format"":"""",""analyzerType"":""STRING""},"
        json = json & "{""name"":""Latitude"",""type"":""real"",""format"":"""",""analyzerType"":""FLOAT""},"
        json = json & "{""name"":""Longitude"",""type"":""real"",""format"":"""",""analyzerType"":""FLOAT""},"
        json = json & "{""name"":""Visits"",""type"":""integer"",""format"":"""",""analyzerType"":""INT""}],"
        json = json & """type"":"""",""metadata"":{""id"":""2apqg0"",""format"":""row"","
        json = json & """label"":""airports.csv""},""disableDataOperation"":false}}],"

        '--- Config: opening
        cfg = """config"":{""version"":""v1"",""config"":{"
        cfg = cfg & """visState"":{""filters"":[],""layers"":["

        '--- Config: point layer (airports)
        cfg = cfg & "{""id"":""alkmueg"",""type"":""point"",""config"":{"
        cfg = cfg & """dataId"":""2apqg0"",""columnMode"":""points"","
        cfg = cfg & """label"":""airports"",""color"":[179,173,158],"
        cfg = cfg & """highlightColor"":[252,242,26,255],"
        cfg = cfg & """columns"":{""lat"":""Latitude"",""lng"":""Longitude""},"
        cfg = cfg & """isVisible"":true,""visConfig"":{"
        cfg = cfg & """radius"":29.5,""fixedRadius"":false,""opacity"":0.8,"
        cfg = cfg & """outline"":false,""thickness"":2,""strokeColor"":null,"
        cfg = cfg & """colorRange"":{""colors"":["
        cfg = cfg & """#4C0035"",""#880030"",""#B72F15"",""#D6610A"",""#EF9100"",""#FFC300""],"
        cfg = cfg & """name"":""Global Warming"",""type"":""sequential"",""category"":""Uber""},"
        cfg = cfg & """strokeColorRange"":{""name"":""Global Warming"","
        cfg = cfg & """type"":""sequential"",""category"":""Uber"","
        cfg = cfg & """colors"":[""#4C0035"",""#880030"",""#B72F15"","
        cfg = cfg & """#D6610A"",""#EF9100"",""#FFC300""]},"
        cfg = cfg & """radiusRange"":[0,50],""filled"":true,""billboard"":false,"
        cfg = cfg & """allowHover"":true,""showNeighborOnHover"":false,"
        cfg = cfg & """showHighlightColor"":true},"
        cfg = cfg & """hidden"":false,""textLabel"":["
        cfg = cfg & "{""field"":{""name"":""ICAO"",""type"":""string""},"
        cfg = cfg & """color"":[255,255,255],""size"":18,""offset"":[0,0],"
        cfg = cfg & """anchor"":""start"",""alignment"":""center"","
        cfg = cfg & """outlineWidth"":0,""outlineColor"":[255,0,0,255],"
        cfg = cfg & """background"":false,""backgroundColor"":[0,0,200,255]}]},"
        cfg = cfg & """visualChannels"":{"
        cfg = cfg & """colorField"":{""name"":""Visits"",""type"":""integer""},"
        cfg = cfg & """colorScale"":""quantile"","
        cfg = cfg & """strokeColorField"":null,""strokeColorScale"":""quantile"","
        cfg = cfg & """sizeField"":null,""sizeScale"":""linear""}},"

        '--- Config: arc layer (routes)
        cfg = cfg & "{""id"":""leut7db"",""type"":""arc"",""config"":{"
        cfg = cfg & """dataId"":""-b3npja"",""columnMode"":""points"","
        cfg = cfg & """label"":""Routes"",""color"":[137,218,193],"
        cfg = cfg & """highlightColor"":[252,242,26,255],"
        cfg = cfg & """columns"":{""lat0"":""DepLat"",""lng0"":""DepLon"","
        cfg = cfg & """lat1"":""ArrLat"",""lng1"":""ArrLon""},"
        cfg = cfg & """isVisible"":true,""visConfig"":{"
        cfg = cfg & """opacity"":0.8,""thickness"":3,"
        cfg = cfg & """colorRange"":{""name"":""Global Warming"","
        cfg = cfg & """type"":""sequential"",""category"":""Uber"","
        cfg = cfg & """colors"":[""#4C0035"",""#880030"",""#B72F15"","
        cfg = cfg & """#D6610A"",""#EF9100"",""#FFC300""]},"
        cfg = cfg & """sizeRange"":[0,10],""targetColor"":null},"
        cfg = cfg & """hidden"":false,""textLabel"":["
        cfg = cfg & "{""field"":null,""color"":[255,255,255],""size"":18,"
        cfg = cfg & """offset"":[0,0],""anchor"":""start"",""alignment"":""center"","
        cfg = cfg & """outlineWidth"":0,""outlineColor"":[255,0,0,255],"
        cfg = cfg & """background"":false,""backgroundColor"":[0,0,200,255]}]},"
        cfg = cfg & """visualChannels"":{"
        cfg = cfg & """colorField"":null,""colorScale"":""quantile"","
        cfg = cfg & """sizeField"":null,""sizeScale"":""linear""}}],"

        '--- Config: interaction
        cfg = cfg & """effects"":[],""interactionConfig"":{"
        cfg = cfg & """tooltip"":{""fieldsToShow"":{"
        cfg = cfg & """-b3npja"":[{""name"":""DepAirport"",""format"":null},"
        cfg = cfg & "{""name"":""ArrAirport"",""format"":null},"
        cfg = cfg & "{""name"":""DepLat"",""format"":null},"
        cfg = cfg & "{""name"":""DepLon"",""format"":null},"
        cfg = cfg & "{""name"":""ArrLat"",""format"":null}],"
        cfg = cfg & """2apqg0"":[{""name"":""ICAO"",""format"":null},"
        cfg = cfg & "{""name"":""Airport"",""format"":null},"
        cfg = cfg & "{""name"":""Latitude"",""format"":null},"
        cfg = cfg & "{""name"":""Longitude"",""format"":null},"
        cfg = cfg & "{""name"":""Visits"",""format"":null}]},"
        cfg = cfg & """compareMode"":false,""compareType"":""absolute"",""enabled"":true},"
        cfg = cfg & """brush"":{""size"":0.5,""enabled"":false},"
        cfg = cfg & """geocoder"":{""enabled"":false},"
        cfg = cfg & """coordinate"":{""enabled"":false}},"
        cfg = cfg & """layerBlending"":""subtractive"","
        cfg = cfg & """overlayBlending"":""screen"","
        cfg = cfg & """splitMaps"":[],""animationConfig"":{"
        cfg = cfg & """currentTime"":null,""speed"":1},"
        cfg = cfg & """editor"":{""features"":[],""visible"":true}},"

        '--- Config: map state
        cfg = cfg & """mapState"":{"
        cfg = cfg & """bearing"":0,""dragRotate"":false,"
        cfg = cfg & """latitude"":-14.306762666603749,"
        cfg = cfg & """longitude"":131.67865438430354,"
        cfg = cfg & """pitch"":0,""zoom"":6.647942855562414,"
        cfg = cfg & """isSplit"":false,""isViewportSynced"":true,"
        cfg = cfg & """isZoomLocked"":false,""splitMapViewports"":[]},"

        '--- Config: map style
        cfg = cfg & """mapStyle"":{""styleType"":""dark-matter"","
        cfg = cfg & """topLayerGroups"":{},""visibleLayerGroups"":{"
        cfg = cfg & """label"":true,""road"":true,""border"":true,"
        cfg = cfg & """building"":true,""water"":true,""land"":true,"
        cfg = cfg & """3d building"":false},"
        cfg = cfg & """threeDBuildingColor"":[15.035172933000911,"
        cfg = cfg & "15.035172933000911,15.035172933000911],"
        cfg = cfg & """backgroundColor"":[0,0,0],""mapStyles"":{}},"
        cfg = cfg & """uiState"":{""mapControls"":{""mapLegend"":{""active"":false}}}}},"

        '--- Info block
        cfg = cfg & """info"":{""app"":""kepler.gl"","
        cfg = cfg & """created_at"":""Sat Apr 04 2026 20:48:20 GMT+1100"","
        cfg = cfg & """title"":""keplergl_mkvi9sb"","
        cfg = cfg & """description"":"""",""source"":""kepler.gl""}}"

        json = json & cfg

    '===============================
    ' STEP 5: WRITE TO FILE
    '===============================
        Dim savePath    As String
        Dim filename    As String
        Dim basePath    As String

        filename = "logbook_kepler_routemap_" & Format(Date, "YYMMDD") & ".json"

        '--- Determine base path alongside the workbook, resolving OneDrive cloud URLs
        basePath = ResolveLocalPath(ThisWorkbook) & "\Route Map"

        '--- Create folder structure if it doesn't exist
        Dim parts()     As String
        Dim buildPath   As String
        Dim p           As Long
        parts = Split(basePath, Application.PathSeparator)
        buildPath = parts(0)
        For p = 1 To UBound(parts)
            buildPath = buildPath & Application.PathSeparator & parts(p)
            If Dir(buildPath, vbDirectory) = "" Then MkDir buildPath
        Next p

        savePath = basePath & Application.PathSeparator & filename

        '--- Write JSON to file
        Dim fileNum As Integer
        fileNum = FreeFile
        Open savePath For Output As #fileNum
            Print #fileNum, json
        Close #fileNum

    '===============================
    ' STEP 6: DONE
    '===============================
        MsgBox "Kepler.gl JSON exported successfully to:" & vbNewLine & savePath, vbInformation

End Sub

Sub SetLogbookFilterArrows()

    Application.ScreenUpdating = False

    Dim wsLog   As Worksheet
    Dim tbl     As ListObject
    Dim i       As Long

    Set wsLog = ThisWorkbook.Sheets("Logbook")
    Set tbl = wsLog.ListObjects("Logbook")

    '--- Define which column names should show filter arrows
    Dim visibleColumns As Variant
    visibleColumns = Array("Date", "Type", "Reg", "PIC", "Other Pilot or Crew", "Details")

    '--- Loop through all columns, show or hide arrow based on list above
    For i = 1 To tbl.ListColumns.Count
        Dim showArrow As Boolean
        showArrow = False

        Dim v As Variant
        For Each v In visibleColumns
            If tbl.ListColumns(i).Name = v Then
                showArrow = True
                Exit For
            End If
        Next v

        tbl.Range.AutoFilter Field:=i, VisibleDropDown:=showArrow
    Next i

    Application.ScreenUpdating = True

End Sub

Public Sub OpenHelp()
    Dim http      As Object
    Dim token     As String
    Dim url       As String
    Dim markdown  As String
    Dim html      As String
    Dim tempFile  As String
    Dim fileNum   As Integer

    ' Fetch README.md from GitHub
    token = Trim(CStr(ThisWorkbook.Names("GitHubToken").RefersToRange.Value))
    url = "https://raw.githubusercontent.com/alphadelta332/Electronic-Logbook/main/README.md"

    On Error GoTo Fail
    Set http = CreateObject("MSXML2.XMLHTTP")
    http.Open "GET", url, False
    http.setRequestHeader "Cache-Control", "no-cache"
    If token <> "" Then
        http.setRequestHeader "Authorization", "token " & token
    End If
    http.send
    If http.Status <> 200 And token <> "" Then
        ' The public README should still load if an old workbook contains
        ' a stale private-repo PAT.
        Set http = CreateObject("MSXML2.XMLHTTP")
        http.Open "GET", url, False
        http.setRequestHeader "Cache-Control", "no-cache"
        http.send
    End If

    If http.Status <> 200 Then GoTo Fail
    markdown = http.responseText

    ' Convert markdown to HTML
    html = MarkdownToHTML(markdown)

    ' Write to temp file and open in browser
    tempFile = Environ("TEMP") & "\LB_Help.html"
    fileNum = FreeFile
    Open tempFile For Output As #fileNum
        Print #fileNum, html
    Close #fileNum

    Shell "explorer.exe """ & tempFile & """", vbNormalFocus
    Exit Sub

Fail:
    MsgBox "Could not load help content. Please check your internet connection.", _
           vbExclamation, "Help Unavailable"
End Sub

Private Function MarkdownToHTML(md As String) As String
    Dim lines()  As String
    Dim out      As String
    Dim line     As String
    Dim i        As Long
    Dim inCode   As Boolean
    Dim inList   As Boolean

    lines = Split(md, vbLf)
    inCode = False
    inList = False

    out = "<!DOCTYPE html><html><head><meta charset=""UTF-8"">" & _
          "<title>Electronic Logbook - Help</title>" & _
          "<style>" & _
          "body{font-family:Segoe UI,Arial,sans-serif;max-width:860px;margin:40px auto;padding:0 20px;color:#24292e;line-height:1.6;}" & _
          "h1{font-size:2em;border-bottom:1px solid #eaecef;padding-bottom:0.3em;}" & _
          "h2{font-size:1.5em;border-bottom:1px solid #eaecef;padding-bottom:0.3em;margin-top:24px;}" & _
          "h3{font-size:1.2em;margin-top:20px;}" & _
          "code{background:#f6f8fa;padding:2px 5px;border-radius:3px;font-family:Consolas,monospace;font-size:0.9em;}" & _
          "pre{background:#f6f8fa;padding:16px;border-radius:6px;overflow-x:auto;}" & _
          "blockquote{border-left:4px solid #dfe2e5;padding:0 16px;color:#6a737d;margin:0;}" & _
          "table{border-collapse:collapse;width:100%;margin:16px 0;}" & _
          "th{background:#f6f8fa;font-weight:600;}" & _
          "th,td{border:1px solid #dfe2e5;padding:8px 12px;text-align:left;}" & _
          "tr:nth-child(even){background:#f6f8fa;}" & _
          "hr{border:0;border-top:1px solid #eaecef;margin:24px 0;}" & _
          "a{color:#0366d6;}" & _
          "ul,ol{padding-left:24px;}" & _
          "</style></head><body>"

    For i = 0 To UBound(lines)
        line = RTrim(lines(i))
        ' Strip carriage return if present
        If Right(line, 1) = Chr(13) Then line = Left(line, Len(line) - 1)

        ' Code fences
        If Left(line, 3) = "```" Then
            If inCode Then
                out = out & "</code></pre>"
                inCode = False
            Else
                out = out & "<pre><code>"
                inCode = True
            End If
            GoTo NextLine
        End If
        If inCode Then
            out = out & EscapeHTML(line) & vbLf
            GoTo NextLine
        End If

        ' Headings
        If Left(line, 4) = "### " Then
            out = out & "<h3>" & EscapeHTML(Mid(line, 5)) & "</h3>"
        ElseIf Left(line, 3) = "## " Then
            out = out & "<h2>" & EscapeHTML(Mid(line, 4)) & "</h2>"
        ElseIf Left(line, 2) = "# " Then
            out = out & "<h1>" & EscapeHTML(Mid(line, 3)) & "</h1>"

        ' Horizontal rule
        ElseIf line = "---" Then
            out = out & "<hr>"

        ' Blockquote
        ElseIf Left(line, 2) = "> " Then
            out = out & "<blockquote>" & InlineFormat(Mid(line, 3)) & "</blockquote>"

        ' Table row
        ElseIf Left(line, 1) = "|" And Right(line, 1) = "|" Then
            ' Skip separator rows like |---|---|
            If InStr(line, "---") = 0 Then
                Dim cells()  As String
                Dim cellOut  As String
                Dim isHeader As Boolean
                cells = Split(Mid(line, 2, Len(line) - 2), "|")
                ' Treat as header if previous non-empty line was also a | line
                isHeader = (i > 0 And Left(Trim(lines(i - 1)), 1) = "#") Or _
                           (i < UBound(lines) And InStr(lines(i + 1), "---") > 0)
                cellOut = "<tr>"
                Dim cc As Long
                For cc = 0 To UBound(cells)
                    If isHeader Then
                        cellOut = cellOut & "<th>" & InlineFormat(Trim(cells(cc))) & "</th>"
                    Else
                        cellOut = cellOut & "<td>" & InlineFormat(Trim(cells(cc))) & "</td>"
                    End If
                Next cc
                cellOut = cellOut & "</tr>"
                ' Wrap in table tags if first row
                If i = 0 Or Left(Trim(lines(i - 1)), 1) <> "|" Then
                    cellOut = "<table>" & cellOut
                End If
                Dim nextLine As String
                nextLine = IIf(i < UBound(lines), lines(i + 1), "")
                If i = UBound(lines) Or Left(Trim(nextLine), 1) <> "|" Or InStr(nextLine, "---") > 0 Then
                    If InStr(nextLine, "---") = 0 And (i = UBound(lines) Or Left(Trim(nextLine), 1) <> "|") Then
                        cellOut = cellOut & "</table>"
                    End If
                End If
                out = out & cellOut
            End If

        ' Bullet list
        ElseIf Left(line, 2) = "- " Then
            out = out & "<ul><li>" & InlineFormat(Mid(line, 3)) & "</li></ul>"

        ' Numbered list
        ElseIf Len(line) > 2 And IsNumeric(Left(line, 1)) And Mid(line, 2, 2) = ". " Then
            out = out & "<ol><li>" & InlineFormat(Mid(line, 4)) & "</li></ol>"

        ' Empty line = paragraph break
        ElseIf Trim(line) = "" Then
            out = out & "<p></p>"

        ' Regular paragraph
        Else
            out = out & "<p>" & InlineFormat(line) & "</p>"
        End If

NextLine:
    Next i

    out = out & "</body></html>"
    MarkdownToHTML = out
End Function

Private Function EscapeHTML(s As String) As String
    s = Replace(s, "&", "&amp;")
    s = Replace(s, "<", "&lt;")
    s = Replace(s, ">", "&gt;")
    EscapeHTML = s
End Function

Private Function InlineFormat(s As String) As String
    ' Escape HTML first
    s = EscapeHTML(s)

    ' Bold **text**
    Dim re As Object
    Set re = CreateObject("VBScript.RegExp")
    re.Global = True

    re.Pattern = "\*\*(.+?)\*\*"
    s = re.Replace(s, "<strong>$1</strong>")

    ' Italic *text*
    re.Pattern = "\*(.+?)\*"
    s = re.Replace(s, "<em>$1</em>")

    ' Inline code `text`
    re.Pattern = "`(.+?)`"
    s = re.Replace(s, "<code>$1</code>")

    ' Links [text](url)
    re.Pattern = "\[(.+?)\]\((.+?)\)"
    s = re.Replace(s, "<a href=""$2"">$1</a>")

    InlineFormat = s
End Function

Public Sub WriteCrumb(step As String)
    ' Writes the current execution step to a temp file.
    ' Unlike the debug log, this survives a hard Excel crash because it is
    ' written continuously during execution, not just in the error handler.
    ' After a crash, check %TEMP%\LB_Crumb.txt for the last step reached.
    On Error Resume Next
    Dim f As Integer
    f = FreeFile
    Open Environ("TEMP") & "\LB_Crumb.txt" For Output As #f
    Print #f, Format(Now, "yyyy-mm-dd hh:mm:ss") & " | " & step
    Close #f
    On Error GoTo 0
End Sub

Public Sub WriteDebugLog(source As String, errNum As Long, errDesc As String, Optional diagStep As String = "")
    On Error Resume Next

    Dim logDir    As String
    Dim logPath   As String
    Dim version   As String
    Dim fileNum   As Integer
    Dim crumbPath As String

    ' Prefer writing alongside the workbook so users can find the log easily.
    ' Falls back to Documents\Electronic Logbook if path resolution fails.
    logDir = ResolveLocalPath(ThisWorkbook)
    If logDir = "" Or Left(logDir, 4) = "http" Then
        logDir = Environ("USERPROFILE") & "\Documents\Electronic Logbook"
    End If
    If Dir(logDir, vbDirectory) = "" Then MkDir logDir

    ' Gather context -- all guarded by the top-level On Error Resume Next
    version = Trim(CStr(ThisWorkbook.Names("LogbookVersion").RefersToRange.Value))
    If version = "" Then version = "Unknown"

    Dim fDate    As String
    Dim fType    As String
    Dim fReg     As String
    Dim fPIC     As String
    Dim fDetails As String
    Dim fIpcDetected As String
    Dim fOpcDetected As String
    Dim fFlightReviewDetected As String
    Dim fRows    As String
    Dim fCrumb   As String
    fDate    = CStr(Range("neDate").Value)
    fType    = CStr(Range("neType").Value)
    fReg     = CStr(Range("neReg").Value)
    fPIC     = CStr(Range("nePIC").Value)
    fDetails = CStr(Range("neDetails").Value)
    If KeywordDetected(fDetails, "IPC") Then
        fIpcDetected = "Yes"
    Else
        fIpcDetected = "No"
    End If
    If KeywordDetected(fDetails, "OPC") Then
        fOpcDetected = "Yes"
    Else
        fOpcDetected = "No"
    End If
    If KeywordDetected(fDetails, "IPC") Or KeywordDetected(fDetails, "Flight Review") Then
        fFlightReviewDetected = "Yes"
    Else
        fFlightReviewDetected = "No"
    End If
    fRows    = CStr(ThisWorkbook.Sheets("Logbook").ListObjects("Logbook").DataBodyRange.Rows.Count)

    ' Read the last crash breadcrumb if one exists
    crumbPath = Environ("TEMP") & "\LB_Crumb.txt"
    If Dir(crumbPath) <> "" Then
        Dim cf As Integer
        cf = FreeFile
        Open crumbPath For Input As #cf
        Line Input #cf, fCrumb
        Close #cf
    End If

    logPath = logDir & "\debug_log.txt"
    fileNum = FreeFile
    Open logPath For Append As #fileNum
        Print #fileNum, String(50, "=")
        Print #fileNum, "Timestamp    : " & Format(Now, "yyyy-mm-dd hh:mm:ss")
        Print #fileNum, ""
        Print #fileNum, "-- ERROR ----------------------------------------"
        Print #fileNum, "Source       : " & source
        If diagStep <> "" Then Print #fileNum, "Step         : " & diagStep
        If fCrumb <> "" Then Print #fileNum, "Last crumb   : " & fCrumb
        Print #fileNum, "Error " & errNum & "      : " & errDesc
        Print #fileNum, ""
        Print #fileNum, "-- ENVIRONMENT ----------------------------------"
        Print #fileNum, "Excel        : " & Application.Version & " / " & Application.OperatingSystem
        Print #fileNum, "User         : " & Environ("USERNAME")
        Print #fileNum, "Workbook     : " & ThisWorkbook.Name
        Print #fileNum, "WB path      : " & ThisWorkbook.Path
        Print #fileNum, "Resolved path: " & ResolveLocalPath(ThisWorkbook)
        Print #fileNum, "Log saved to : " & logPath
        Print #fileNum, "AutoSave     : " & ThisWorkbook.AutoSaveOn
        Print #fileNum, "LB Version   : " & version
        Print #fileNum, ""
        Print #fileNum, "-- ENTRY STATE ----------------------------------"
        Print #fileNum, "Date         : " & fDate
        Print #fileNum, "Type         : " & fType
        Print #fileNum, "Reg          : " & fReg
        Print #fileNum, "PIC          : " & fPIC
        Print #fileNum, "Details      : " & fDetails
        Print #fileNum, "IPC detected : " & fIpcDetected
        Print #fileNum, "OPC detected : " & fOpcDetected
        Print #fileNum, "FR detected  : " & fFlightReviewDetected
        Print #fileNum, "Logbook rows : " & fRows
        Print #fileNum, ""
    Close #fileNum

    On Error GoTo 0
End Sub

Public Sub ToggleSuppressWarnings()
    Dim isActive As Boolean
    isActive = False

    If Range("suppressWarningsUntil").Value <> "" Then
        If IsDate(Range("suppressWarningsUntil").Value) Then
            If Now < CDate(Range("suppressWarningsUntil").Value) Then isActive = True
        End If
    End If

    If isActive Then
        Range("suppressWarningsUntil").ClearContents
        MsgBox "Warning suppression disabled. Warnings will appear normally.", vbInformation, "Warnings Enabled"
    Else
        Range("suppressWarningsUntil").Value = Now + 1
        MsgBox "All warnings suppressed for 24 hours.", vbInformation, "Warnings Suppressed"
    End If

    RefreshSuppressWarningsButton
End Sub

Public Sub RefreshSuppressWarningsButton()
    Dim isActive As Boolean
    isActive = False

    If Range("suppressWarningsUntil").Value <> "" Then
        If IsDate(Range("suppressWarningsUntil").Value) Then
            If Now < CDate(Range("suppressWarningsUntil").Value) Then isActive = True
        End If
    End If

    Dim ws      As Worksheet
    Dim shp     As Shape
    Dim btnShape As Shape
    Dim item    As Shape
    Set ws = ThisWorkbook.Sheets("New Entry")

    ' Button may be ungrouped or inside a group
    For Each shp In ws.Shapes
        If InStr(shp.OnAction, "ToggleSuppressWarnings") > 0 Then
            Set btnShape = shp
            Exit For
        End If
        If shp.Type = msoGroup Then
            For Each item In shp.GroupItems
                If InStr(item.OnAction, "ToggleSuppressWarnings") > 0 Then
                    Set btnShape = item
                    Exit For
                End If
            Next item
            If Not btnShape Is Nothing Then Exit For
        End If
    Next shp

    If Not btnShape Is Nothing Then
        btnShape.Fill.Solid
        If isActive Then
            btnShape.Fill.ForeColor.RGB = RGB(255, 140, 0)
        Else
            btnShape.Fill.ForeColor.RGB = RGB(189, 189, 189)
        End If
    End If

    Dim txtBox As Shape
    On Error Resume Next
    Set txtBox = ws.Shapes("SuppressWarningButtonText")
    On Error GoTo 0

    If Not txtBox Is Nothing Then
        If isActive Then
            txtBox.TextFrame.Characters.Text = "Warnings Suppressed"
        Else
            txtBox.TextFrame.Characters.Text = "Suppress Warnings for 24 Hours"
        End If
    End If
End Sub

' ==============================================================
' PATH UTILITIES
' ==============================================================
' ResolveLocalPath lives here (not in modUpdate) because modUpdate
' is dynamically downloaded and removed on every open by modBoot.
' Any module that needs path resolution must call this copy.

Public Function ResolveLocalPath(wb As Workbook) As String
    ' Returns the true local filesystem path for a workbook,
    ' mapping OneDrive cloud URLs to their local sync folder.
    ' Falls back to Documents if resolution fails.

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

    ' Strip protocol + host (everything up to the 4th slash)
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

Sub ShowDevSheets()
    Sheets("Admin").Visible = xlSheetVisible
    Sheets("Routes").Visible = xlSheetVisible
    Sheets("ChartData").Visible = xlSheetVisible
    Sheets("Airports").Visible = xlSheetVisible
End Sub

Sub HideDevSheets()
    Sheets("Admin").Visible = xlSheetVeryHidden
    Sheets("Routes").Visible = xlSheetVeryHidden
    Sheets("ChartData").Visible = xlSheetVeryHidden
    Sheets("Airports").Visible = xlSheetVeryHidden
End Sub
