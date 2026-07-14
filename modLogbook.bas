Attribute VB_Name = "modLogbook"
Option Explicit

Public Const ROUTE_DEFINITION_VERSION As Long = 3
Private mProtectionDisabledForSession As Boolean
Private Const NEW_ENTRY_ACTIVE_SHEET As String = "New Entry"
Private Const NEW_ENTRY_UNUSED_SHEET As String = "New Entry Unused Layout"
Private Const NEW_ENTRY_SWAP_TEMP_SHEET As String = "New Entry Swap Temp"
Private Const NEW_ENTRY_LAYOUT_MARKER_NAME As String = "NewEntryLayoutKind"
Private Const NEW_ENTRY_LAYOUT_COMPACT As String = "Compact"
Private Const NEW_ENTRY_LAYOUT_GROUPED As String = "Grouped"
Private Const AIRCRAFT_TYPES_SHEET As String = "AircraftTypes"
Private Const AIRCRAFT_TYPES_TABLE As String = "AircraftTypes"
Private Const LOGTEN_REPORT_SHEET As String = "LogTen Import Report"
Private Const AIRPORT_ICAO_VALIDATION_NAME As String = "AirportIcaoValidationList"
Private Const REMOTE_AIRPORT_WARNING_THRESHOLD_NM As Double = 3000
Private Const HIGH_SPEED_ROUTE_WARNING_THRESHOLD_KT As Double = 700
Private Const ADD_LOGBOOK_LAYOUT_DIAG_SHEET As String = "_AddToLogbookLayoutDiagnostics"
Private Const ADD_LOGBOOK_LAYOUT_DIAG_FLAG As String = "AddToLogbookLayoutDiagnostics"
Private Const LOGBOOK_ACTION_BUTTON_WIDTH As Double = 121.2
Private Const LOGBOOK_ACTION_BUTTON_HEIGHT As Double = 45
Private Const LOGBOOK_ACTION_BUTTON_POSITION_TOLERANCE As Double = 1
Private mApplyingNewEntryLayout As Boolean
Private mLastLogbookExportError As String

Sub AddToLogbook(Optional ByVal showSuccessMessage As Boolean = True)

    Dim previousDisplayStatusBar As Boolean
    Dim previousStatusBar As Variant

    On Error GoTo Cleanup

    ' Ensure unqualified Range() calls resolve against this workbook/session.
    On Error Resume Next
    ThisWorkbook.Activate
    ThisWorkbook.Sheets(NEW_ENTRY_ACTIVE_SHEET).Activate
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
        previousDisplayStatusBar = Application.DisplayStatusBar
        previousStatusBar = Application.StatusBar
        SetAddToLogbookStatus "Starting checks"

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
    Dim ipcSelected As Boolean
    Dim opcSelected As Boolean
    Dim flightReviewSelected As Boolean
    Dim totalsWereOn As Boolean
    Dim totalsStateCaptured As Boolean
    Dim tableStyleName As String
    Dim logbookWasProtected As Boolean
    Dim latestLogbookDate As Date
    Dim shouldSortLogbook As Boolean
    Dim entryWasWritten As Boolean
    Dim initialLogbookRows As Long

        Set wsEntry = ThisWorkbook.Sheets("New Entry")
        Set wsLog = ThisWorkbook.Sheets("Logbook")
        Set tbl = wsLog.ListObjects("Logbook")
        initialLogbookRows = tbl.ListRows.Count
        logbookWasProtected = wsLog.ProtectContents
        TraceAddToLogbookLayout "Initial table state", tbl
        If logbookWasProtected Then
            On Error Resume Next
            wsLog.Unprotect Password:=ProtectionPassword()
            On Error GoTo Cleanup
            TraceAddToLogbookLayout "After unprotect", tbl
        End If
        If Not ListColumnExists(tbl, "Flight ID") Or _
           Not ListColumnExists(tbl, "FR") Or _
           Not ListColumnExists(tbl, "IPC") Or _
           Not ListColumnExists(tbl, "OPC") Or _
           Not ListColumnExists(tbl, "Remarks") Then
            MsgBox "ERROR: The Logbook table is missing one or more New Entry columns. Please update the workbook structure before adding entries.", vbCritical
            GoTo Cleanup
        End If
        RefreshTodayValue
        SetAddToLogbookStatus "Validating entry"
        todayDate = CDate(GetWorkbookNameValue(ThisWorkbook, "today", Date))
        ipcSelected = NewEntryBooleanValue("neIPC")
        opcSelected = NewEntryBooleanValue("neOPC")
        flightReviewSelected = NewEntryBooleanValue("neFR")
        RefreshDateCalculationFormulas tbl
        ClearNewEntryValidationHighlights

    '===============================
    ' STEP 3: HARD VALIDATION (STOP ERRORS)
    '===============================
        diagStep = "Step 3: Hard Validation"

    '--- 3a. Date Validity Check
        'Check 1: Ensure the date field doesn't contain a formula error (e.g. invalid month string)
        If IsError(NewEntryValue("neDate")) Then
            MarkNewEntryProblemFields NewEntryDateInputFieldNames()
            MsgBox "ERROR: Formatting error in the Date field. Ensure the month is entered in the correct 3 letter format, the date is a valid one, and the date is not in the future.", vbCritical
            GoTo Cleanup
        End If

        'Check 2: Ensure the resolved value is actually a recognisable date
        If Not IsDate(NewEntryValue("neDate")) Then
            MarkNewEntryProblemFields NewEntryDateInputFieldNames()
            MsgBox "ERROR: Formatting error in the Date field. Ensure the month is entered in the correct 3 letter format, the date is a valid one, and the date is not in the future.", vbCritical
            GoTo Cleanup
        End If

        'Check 3: Ensure the date is not in the future
        If CDate(NewEntryValue("neDate")) > todayDate Then
            MarkNewEntryProblemFields NewEntryDateInputFieldNames()
            MsgBox "ERROR: Formatting error in the Date field. Ensure the month is entered in the correct 3 letter format, the date is a valid one, and the date is not in the future.", vbCritical
            GoTo Cleanup
        End If

        entryDate = CDate(NewEntryValue("neDate"))
        latestLogbookDate = GetLatestLogbookEntryDate(tbl)
        shouldSortLogbook = (latestLogbookDate <> 0 And entryDate < latestLogbookDate)

    '--- 3b. Registration Check (skipped for sim entries)
        If NewEntryNumericValue("neIfrSim") = 0 Then

            'Check 1: Aircraft registration must not be blank
            If CStr(NewEntryValue("neReg")) = "" Then
                MarkNewEntryProblemFields Array("neReg")
                MsgBox "ERROR: Registration cannot be blank.", vbCritical
                GoTo Cleanup
            End If

        End If

    '--- 3c. Zero Hours Check
        If CountPositiveNewEntryFields(NewEntryFlightHourFieldNames()) = 0 Then
            MarkNewEntryProblemFields NewEntryFlightHourFieldNames()
            MsgBox "ERROR: Total hours cannot be zero.", vbCritical
            GoTo Cleanup
        End If

    '--- 3d. IFR Hours vs Total Hours Check
        If NewEntryNumericValue("neIfrIf") > SumNewEntryFields(NewEntryFlightTimeFieldNames()) Then
            MarkNewEntryProblemFields CombineNewEntryFieldNames(Array("neIfrIf"), NewEntryFlightTimeFieldNames())
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
            If NewEntryValue(CStr(requiredFields(i))) = "" Then
                MarkNewEntryProblemFields Array(CStr(requiredFields(i)))
                MsgBox "ERROR: " & fieldNames(i) & " cannot be blank.", vbCritical
                GoTo Cleanup
            End If
        Next i

        If NewEntryNumericValue("neIfrSim") = 0 Then
            If NewEntryValue("neFrom") = "" Then
                MarkNewEntryProblemFields Array("neFrom")
                MsgBox "ERROR: Departure Airport cannot be blank.", vbCritical
                GoTo Cleanup
            End If

            If NewEntryValue("neTo") = "" Then
                MarkNewEntryProblemFields Array("neTo")
                MsgBox "ERROR: Destination Airport cannot be blank.", vbCritical
                GoTo Cleanup
            End If
        End If

    '--- 3f. Non-Numeric Value Check (Hours, Landings, Approaches)
        Dim checkField As Variant

        For Each checkField In NewEntryNumericFieldNames()
            If NewEntryValue(CStr(checkField)) <> "" Then
                If Not IsNumeric(NewEntryValue(CStr(checkField))) Then
                    MarkNewEntryProblemFields Array(CStr(checkField))
                    MsgBox "ERROR: Non-numerical value found in Hours, Landings, or Approaches.", vbCritical
                    GoTo Cleanup
                End If
            End If
        Next checkField

    '===============================
    ' STEP 4: WARNING CHECKS (CONTINUE OR CANCEL)
    '===============================
        diagStep = "Step 4: Warning Checks"
        SetAddToLogbookStatus "Running warnings"
    ' All warnings use vbOKCancel: OK = proceed, Cancel = go back.
    ' Warnings can be suppressed for 24 hours via the dedicated sheet button (ToggleSuppressWarnings).

    '--- 4a. Initialise Warning Suppression State
        Dim suppressWarnings As Boolean
        suppressWarnings = False

        Dim suppressUntilValue As Variant
        suppressUntilValue = GetWorkbookNameValue(ThisWorkbook, "suppressWarningsUntil", vbNullString)
        If CStr(suppressUntilValue) <> "" Then
            If IsDate(suppressUntilValue) Then
                If Now < CDate(suppressUntilValue) Then suppressWarnings = True
            End If
        End If

    '--- 4b. IPC / OPC Consistency Checks
        If Not suppressWarnings Then
            If NewEntryBooleanValue("neOPC") And _
               (NewEntryNumericValue("neIfrIf") > 0 Or NewEntryNumericValue("neIfrSim") > 0) And _
               Not NewEntryBooleanValue("neIPC") Then
                response = MsgBox("Warning: OPC is ticked and instrument hours are logged, but IPC is not ticked. Continue?", _
                                  vbOKCancel + vbExclamation, _
                                  "OPC Without IPC")
                If response = vbCancel Then
                    MarkNewEntryProblemFields Array("neOPC", "neIPC", "neIfrIf", "neIfrSim")
                    GoTo Cleanup
                End If
            End If

            If NewEntryBooleanValue("neIPC") And Not NewEntryBooleanValue("neFR") Then
                response = MsgBox("Warning: IPC is ticked, but Flight Review is not ticked. Continue?", _
                                  vbOKCancel + vbExclamation, _
                                  "IPC Without Flight Review")
                If response = vbCancel Then
                    MarkNewEntryProblemFields Array("neIPC", "neFR")
                    GoTo Cleanup
                End If
            End If

            If NewEntryBooleanValue("neIPC") And NewEntryNumericValue("neCircling") = 0 Then
                response = MsgBox("No Circling Approach was recorded on this IPC. This means you will not be recent for circling approaches until your next IPC. Continue?", _
                                  vbOKCancel + vbExclamation, _
                                  "IPC Without Circling Approach")
                If response = vbCancel Then
                    MarkNewEntryProblemFields Array("neIPC", "neCircling")
                    GoTo Cleanup
                End If
            End If
        End If

    '--- 4c. Unrecognised Route Airports
        If Not suppressWarnings Then
            Dim unrecognisedAirportFields As Variant
            unrecognisedAirportFields = UnrecognisedNewEntryAirportFieldNames()
            If VariantArrayHasItems(unrecognisedAirportFields) Then
                response = MsgBox(UnrecognisedNewEntryAirportWarningMessage(unrecognisedAirportFields), _
                                  vbOKCancel + vbExclamation, _
                                  "Unrecognised Airport")
                If response = vbCancel Then
                    MarkNewEntryProblemFields unrecognisedAirportFields
                    GoTo Cleanup
                End If
            End If
        End If

    '--- 4c-2. Distant Route Airports
        If Not suppressWarnings Then
            Dim distantAirportFields As Variant
            Dim distantAirportMessage As String

            distantAirportFields = DistantNewEntryAirportFieldNames(distantAirportMessage)
            If VariantArrayHasItems(distantAirportFields) Then
                response = MsgBox(distantAirportMessage, _
                                  vbOKCancel + vbExclamation, _
                                  "Distant Airport")
                If response = vbCancel Then
                    MarkNewEntryProblemFields distantAirportFields
                    GoTo Cleanup
                End If
            End If
        End If

    '--- 4c-3. Implied Route Speed
        If Not suppressWarnings Then
            Dim highSpeedRouteFields As Variant
            Dim highSpeedRouteMessage As String

            highSpeedRouteFields = HighSpeedNewEntryRouteFieldNames(highSpeedRouteMessage)
            If VariantArrayHasItems(highSpeedRouteFields) Then
                response = MsgBox(highSpeedRouteMessage, _
                                  vbOKCancel + vbExclamation, _
                                  "High Route Speed")
                If response = vbCancel Then
                    MarkNewEntryProblemFields highSpeedRouteFields
                    GoTo Cleanup
                End If
            End If
        End If

    '--- 4d. No Landings Recorded (Non-Sim, Non-Copilot Entries Only)
        Dim copilotHoursRecorded As Boolean
        copilotHoursRecorded = NewEntryHasCopilotFlightTime()

        If Not suppressWarnings Then
            If NewEntryNumericValue("neIfrSim") = 0 Then
                If Not copilotHoursRecorded And _
                   NewEntryNumericValue("neLandingsDay") = 0 And _
                   NewEntryNumericValue("neLandingsNight") = 0 Then
                    response = MsgBox("Warning: No Landings Recorded. Proceed?", vbOKCancel + vbExclamation, "No Landings")
                    If response = vbCancel Then
                        MarkNewEntryProblemFields Array("neLandingsDay", "neLandingsNight")
                        GoTo Cleanup
                    End If
                End If
            End If
        End If

    '--- 4e. Earlier Than Latest Existing Logbook Entry Check
        If Not suppressWarnings Then
            If latestLogbookDate <> 0 Then
                If CDate(NewEntryValue("neDate")) < latestLogbookDate Then
                    response = MsgBox("Warning: This entry is dated before the latest existing Logbook entry (" & _
                                      Format(latestLogbookDate, "dd mmm yyyy") & "). Continue?", _
                                      vbOKCancel + vbExclamation, "Earlier Than Latest Logbook Entry")
                    If response = vbCancel Then
                        MarkNewEntryProblemFields NewEntryDateInputFieldNames()
                        GoTo Cleanup
                    End If
                End If
            End If
        End If

    '--- 4f. Day Hours vs Day Landings Cross-Check
        Dim dayHours As Double
        dayHours = SumNewEntryFields(NewEntryDayFlightTimeFieldNames())

        If Not suppressWarnings Then
            'Check 1: Day hours recorded but no day landings
            If dayHours > 0 And Not copilotHoursRecorded Then
                If NewEntryNumericValue("neLandingsDay") = 0 Then
                    response = MsgBox("Warning: Day hours recorded, but no Day Landings recorded. Continue?", vbOKCancel + vbExclamation, "Day Hours Warning")
                    If response = vbCancel Then
                        MarkNewEntryProblemFields CombineNewEntryFieldNames(NewEntryDayFlightTimeFieldNames(), Array("neLandingsDay"))
                        GoTo Cleanup
                    End If
                End If
            End If

            'Check 2: Day landings recorded but no day hours
            If NewEntryNumericValue("neLandingsDay") > 0 Then
                If dayHours = 0 Then
                    response = MsgBox("Warning: Day Landings recorded, but no Day hours recorded. Continue?", vbOKCancel + vbExclamation, "Day Landings Warning")
                    If response = vbCancel Then
                        MarkNewEntryProblemFields CombineNewEntryFieldNames(Array("neLandingsDay"), NewEntryDayFlightTimeFieldNames())
                        GoTo Cleanup
                    End If
                End If
            End If
        End If

    '--- 4g. Night Hours vs Night Landings Cross-Check
        Dim nightHours As Double
        nightHours = SumNewEntryFields(NewEntryNightFlightTimeFieldNames())

        If Not suppressWarnings Then
            'Check 1: Night hours recorded but no night landings
            If nightHours > 0 And Not copilotHoursRecorded Then
                If NewEntryNumericValue("neLandingsNight") = 0 Then
                    response = MsgBox("Warning: Night hours recorded, but no Night Landings recorded. Continue?", vbOKCancel + vbExclamation, "Night Hours Warning")
                    If response = vbCancel Then
                        MarkNewEntryProblemFields CombineNewEntryFieldNames(NewEntryNightFlightTimeFieldNames(), Array("neLandingsNight"))
                        GoTo Cleanup
                    End If
                End If
            End If

            'Check 2: Night landings recorded but no night hours
            If NewEntryNumericValue("neLandingsNight") > 0 Then
                If nightHours = 0 Then
                    response = MsgBox("Warning: Night Landings recorded, but no Night hours recorded. Continue?", vbOKCancel + vbExclamation, "Night Landings Warning")
                    If response = vbCancel Then
                        MarkNewEntryProblemFields CombineNewEntryFieldNames(Array("neLandingsNight"), NewEntryNightFlightTimeFieldNames())
                        GoTo Cleanup
                    End If
                End If
            End If
        End If

    '--- 4h. OPC Without Instrument Hours / Approaches Check
        If Not suppressWarnings Then
            If opcSelected Then
                      If NewEntryNumericValue("neIfrIf") = 0 And _
                          NewEntryNumericValue("neIfrSim") = 0 And _
                    CountPositiveNewEntryFields(NewEntryApproachFieldNames()) = 0 Then
                    response = MsgBox("OPC is ticked but no instrument hours/approaches recorded. Continue?", vbOKCancel + vbExclamation, "OPC Validation")
                    If response = vbCancel Then
                        MarkNewEntryProblemFields CombineNewEntryFieldNames(NewEntryCurrencyFieldNames(), Array("neIfrIf", "neIfrSim"), NewEntryApproachFieldNames())
                        GoTo Cleanup
                    End If
                End If
            End If
        End If

    '--- 4i. Approaches Without Instrument Hours Check
        If Not suppressWarnings Then
            If CountPositiveNewEntryFields(NewEntryApproachFieldNames()) > 0 Then
                     If NewEntryNumericValue("neIfrIf") = 0 And _
                         NewEntryNumericValue("neIfrSim") = 0 Then
                    response = MsgBox("Warning: Approaches recorded with no Instrument hours. Continue?", vbOKCancel + vbExclamation, "Approaches Warning")
                    If response = vbCancel Then
                        MarkNewEntryProblemFields CombineNewEntryFieldNames(NewEntryApproachFieldNames(), Array("neIfrIf", "neIfrSim"))
                        GoTo Cleanup
                    End If
                End If
            End If
        End If

    '--- 4j. High Landings vs Hours Check
        'Total landings (day + night) should not exceed 6x total flight hours
        If Not suppressWarnings Then
            If (NewEntryNumericValue("neLandingsDay") + NewEntryNumericValue("neLandingsNight")) > _
                6 * SumNewEntryFields(NewEntryFlightTimeFieldNames()) Then
                response = MsgBox("Warning: Number of Landings seems high compared to number of hours. Continue?", vbOKCancel + vbExclamation, "High Landings Warning")
                If response = vbCancel Then
                    MarkNewEntryProblemFields CombineNewEntryFieldNames(Array("neLandingsDay", "neLandingsNight"), NewEntryFlightTimeFieldNames())
                    GoTo Cleanup
                End If
            End If
        End If

    '--- 4k. High Approaches vs Hours Check
        'Total approaches should not exceed 3x total flight hours
        If Not suppressWarnings Then
            If SumNewEntryFields(NewEntryApproachFieldNames()) > _
                3 * SumNewEntryFields(NewEntryFlightTimeFieldNames()) Then
                response = MsgBox("Warning: Number of Approaches seems high compared to number of hours. Continue?", vbOKCancel + vbExclamation, "High Approaches Warning")
                If response = vbCancel Then
                    MarkNewEntryProblemFields CombineNewEntryFieldNames(NewEntryApproachFieldNames(), NewEntryFlightTimeFieldNames())
                    GoTo Cleanup
                End If
            End If
        End If

    '--- 4l. Dual / ICUS / Copilot Without Other Crew Check
        If Not suppressWarnings Then
            If NewEntryValue("neOtherCrew") = "" Then
                If SumNewEntryFields(Array( _
                    "neSeIcusDay", "neSeIcusNight", _
                    "neSeDualDay", "neSeDualNight", _
                    "neMeIcusDay", "neMeIcusNight", _
                    "neMeDualDay", "neMeDualNight", _
                    "neCopilotDay", "neCopilotNight")) > 0 Then
                    response = MsgBox("Warning: Dual, ICUS, or Copilot hours recorded, but no Other Pilot or Crew recorded. Continue?", vbOKCancel + vbExclamation, "Other Crew Warning")
                    If response = vbCancel Then
                        MarkNewEntryProblemFields NewEntryOtherCrewWarningFieldNames()
                        GoTo Cleanup
                    End If
                End If
            End If
        End If

    '--- 4m. Single and Multi Engine Hours in Same Entry Check
        If Not suppressWarnings Then
            Dim currentSingleEngineHours As Double
            Dim currentMultiEngineHours As Double

            currentSingleEngineHours = SumNewEntryFields(NewEntrySingleEngineFieldNames())
            currentMultiEngineHours = SumNewEntryFields(NewEntryMultiEngineFieldNames())

            If currentSingleEngineHours > 0 And currentMultiEngineHours > 0 Then
                response = MsgBox("Warning: This entry records both Single Engine and Multi Engine hours. Continue?", _
                                  vbOKCancel + vbExclamation, _
                                  "Mixed Engine Class Hours")
                If response = vbCancel Then
                    MarkNewEntryProblemFields CombineNewEntryFieldNames(NewEntrySingleEngineFieldNames(), NewEntryMultiEngineFieldNames())
                    GoTo Cleanup
                End If
            End If
        End If

    '--- 4n. Aircraft Type Engine Class History Check
        If Not suppressWarnings Then
            Dim hasSingleEngineHistory As Boolean
            Dim hasMultiEngineHistory As Boolean

            currentSingleEngineHours = SumNewEntryFields(NewEntrySingleEngineFieldNames())
            currentMultiEngineHours = SumNewEntryFields(NewEntryMultiEngineFieldNames())

            If currentSingleEngineHours > 0 Or currentMultiEngineHours > 0 Then
                hasSingleEngineHistory = AircraftTypeHasEngineClassHours(tbl, _
                                                                         CStr(NewEntryValue("neType")), _
                                                                         NewEntrySingleEngineColumnNames())
                hasMultiEngineHistory = AircraftTypeHasEngineClassHours(tbl, _
                                                                        CStr(NewEntryValue("neType")), _
                                                                        NewEntryMultiEngineColumnNames())

                If currentSingleEngineHours > 0 And currentMultiEngineHours = 0 And hasMultiEngineHistory Then
                    response = MsgBox("Warning: This type has previously been logged with Multi Engine hours, but this entry records Single Engine hours. Continue?", _
                                      vbOKCancel + vbExclamation, _
                                      "Aircraft Type Engine Class")
                    If response = vbCancel Then
                        MarkNewEntryProblemFields CombineNewEntryFieldNames(Array("neType"), NewEntrySingleEngineFieldNames())
                        GoTo Cleanup
                    End If
                ElseIf currentMultiEngineHours > 0 And currentSingleEngineHours = 0 And hasSingleEngineHistory Then
                    response = MsgBox("Warning: This type has previously been logged with Single Engine hours, but this entry records Multi Engine hours. Continue?", _
                                      vbOKCancel + vbExclamation, _
                                      "Aircraft Type Engine Class")
                    If response = vbCancel Then
                        MarkNewEntryProblemFields CombineNewEntryFieldNames(Array("neType"), NewEntryMultiEngineFieldNames())
                        GoTo Cleanup
                    End If
                End If
            End If
        End If

    '--- Shared indexes for Type/Registration history and duplicate checks
        Dim dateCol     As Long
        Dim detailsCol  As Long
        Dim typeCol     As Long
        Dim regCol      As Long
        Dim dupFound    As Boolean
        Dim rr          As Long

        dateCol = tbl.ListColumns("Date").Index
        detailsCol = tbl.ListColumns("Remarks").Index
        typeCol = tbl.ListColumns("Type").Index
        regCol = tbl.ListColumns("Reg").Index

    '--- 4o. Aircraft Type and Registration Mismatch History Check
        If Not suppressWarnings Then
            Dim currentType As String
            Dim currentReg As String
            Dim hasRegWithDifferentType As Boolean

            currentType = LCase$(Trim$(CStr(NewEntryValue("neType"))))
            currentReg = LCase$(Trim$(CStr(NewEntryValue("neReg"))))

            If currentType <> "" And currentReg <> "" Then
                hasRegWithDifferentType = False

                For rr = 1 To tbl.DataBodyRange.Rows.Count
                    If LCase$(Trim$(CStr(tbl.DataBodyRange.Cells(rr, regCol).Value))) = currentReg Then
                        If LCase$(Trim$(CStr(tbl.DataBodyRange.Cells(rr, typeCol).Value))) <> "" And _
                           LCase$(Trim$(CStr(tbl.DataBodyRange.Cells(rr, typeCol).Value))) <> currentType Then
                            hasRegWithDifferentType = True
                        End If
                    End If

                    If hasRegWithDifferentType Then Exit For
                Next rr

                If hasRegWithDifferentType Then
                    response = MsgBox("Warning: This Registration has previously been logged with a different aircraft Type. Continue?", _
                                      vbOKCancel + vbExclamation, _
                                      "Type/Registration Mismatch")
                    If response = vbCancel Then
                        MarkNewEntryProblemFields Array("neType", "neReg")
                        GoTo Cleanup
                    End If
                End If
            End If
        End If

    '--- 4p. Duplicate Entry Check
        'Warn if an entry with the same Date, Type, Reg and Remarks already exists in the logbook
        dupFound = False

        For rr = 1 To tbl.DataBodyRange.Rows.Count
                If tbl.DataBodyRange.cells(rr, dateCol).Value = NewEntryValue("neDate") And _
                    LCase(Trim(tbl.DataBodyRange.cells(rr, typeCol).Value)) = LCase(Trim(CStr(NewEntryValue("neType")))) And _
                    LCase(Trim(tbl.DataBodyRange.cells(rr, regCol).Value)) = LCase(Trim(CStr(NewEntryValue("neReg")))) And _
               LCase(Trim(tbl.DataBodyRange.cells(rr, detailsCol).Value)) = LCase(Trim(NewEntryValue("neRemarks"))) Then
                dupFound = True
                Exit For
            End If
        Next rr

        If Not suppressWarnings Then
            If dupFound Then
                response = MsgBox("Warning: An entry with the same Date, Type, Registration and Remarks already exists in the Logbook. This may be a duplicate. Continue?", vbOKCancel + vbExclamation, "Duplicate Entry")
                If response = vbCancel Then
                    MarkNewEntryProblemFields CombineNewEntryFieldNames(NewEntryDateInputFieldNames(), Array("neType", "neReg", "neRemarks"))
                    GoTo Cleanup
                End If
            End If
        End If

    '===============================
    ' STEP 5: ADD NEW ROW TO TABLE
    '===============================
        diagStep = "Step 5a: Add Row"
        WriteCrumb diagStep
        SetAddToLogbookStatus "Writing entry"

    '--- 5a. Add a clean table row without inheriting direct visual formats
        Dim fmtCol As Long
        Dim templateRow As Range

        totalsWereOn = tbl.ShowTotals
        totalsStateCaptured = True
        tableStyleName = tbl.TableStyle.Name
        Set templateRow = tbl.DataBodyRange.Rows(1)

        If totalsWereOn Then tbl.ShowTotals = False

        Set newRow = tbl.ListRows.Add(AlwaysInsert:=True)
        entryWasWritten = True
        TraceAddToLogbookLayout "After ListRows.Add", tbl

    '--- Copy Year, Month, Day
        WriteValueToLogbookColumn newRow.Range, tbl, "Year", NewEntryValue("neYear")
        WriteValueToLogbookColumn newRow.Range, tbl, "Month", NewEntryValue("neMonth")
        WriteValueToLogbookColumn newRow.Range, tbl, "Day", NewEntryValue("neDay")

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
        RefreshLogbookCalculatedFormulas tbl

    '--- 5c. Copy Remaining Data
        diagStep = "Step 5c: Copy Data"
        WriteCrumb diagStep
        CopyNewEntryFieldsToLogbookRow newRow.Range, tbl, _
                                       flightReviewSelected, _
                                       ipcSelected, _
                                       opcSelected

    '--- 5d. Fix Month Formatting (always Proper Case e.g. Mar)
        If VarType(newRow.Range.cells(1, 3).Value) = vbString Then
            newRow.Range.cells(1, 3).Value = StrConv(newRow.Range.cells(1, 3).Value, vbProperCase)
        End If

    '--- 5e. Remove formats inherited by row insertion and formula FillDown
        diagStep = "Step 5f: Format Row"
        WriteCrumb diagStep
        newRow.Range.ClearFormats

        For fmtCol = 1 To tbl.ListColumns.Count
            ApplyLogbookCellDataFormatting newRow.Range.Cells(1, fmtCol), _
                                           templateRow.Cells(1, fmtCol)
        Next fmtCol
        If tbl.ListRows.Count > 1 Then
            newRow.Range.Cells(1, 2).NumberFormat = _
                tbl.DataBodyRange.Cells(tbl.ListRows.Count - 1, 2).NumberFormat
        End If

        tbl.TableStyle = tableStyleName
        tbl.ShowTableStyleRowStripes = True
        tbl.ShowTableStyleColumnStripes = False
        tbl.ShowTotals = totalsWereOn
        totalsStateCaptured = False
        NormaliseLogbookFormatting tbl
        TraceAddToLogbookLayout "After totals restore and formatting", tbl

    '--- 5g. Sort Logbook by Date
        diagStep = "Step 5g: Sort Logbook"
        WriteCrumb diagStep
        If shouldSortLogbook Then
            SetAddToLogbookStatus "Sorting logbook"
        Else
            SetAddToLogbookStatus "finalising logbook"
        End If
        Application.Calculate
        If shouldSortLogbook Then SortLogbookByDate tbl
        TraceAddToLogbookLayout "After sort/calculation", tbl

    '--- Persist the entry now so it survives a crash in the chart/pivot steps below
        diagStep = "Step 5: Save Entry"
        WriteCrumb diagStep
        On Error Resume Next
        ThisWorkbook.Save
        On Error GoTo Cleanup
        TraceAddToLogbookLayout "After Step 5 save", tbl
        If logbookWasProtected Then
            diagStep = "Step 5: Re-protect after saved entry"
            WriteCrumb diagStep
            ProtectLogbookSheetForRuntime wsLog
        End If

    '===============================
    ' STEP 6: UPDATE CHART DATA
    '===============================
        diagStep = "Step 6b: Update Chart"
        WriteCrumb diagStep
        SetAddToLogbookStatus "Updating chart"
        On Error Resume Next
        UpdateHoursOverTimeChart ThisWorkbook
        If Err.Number <> 0 Then
            Dim chartErrNum As Long
            Dim chartErrDesc As String
            chartErrNum = Err.Number
            chartErrDesc = Err.Description
            Err.Clear
            WriteDebugLog "UpdateHoursOverTimeChart", chartErrNum, chartErrDesc, diagStep
        End If
        On Error GoTo Cleanup

    '===============================
    ' STEP 7: RESET INPUT FORM
    '===============================
    ' Entry is already saved - errors here must not surface as failures to the user.
        diagStep = "Step 7: Reset Form"
        WriteCrumb diagStep
        SetAddToLogbookStatus "Resetting form"
        On Error Resume Next

    '--- 7a. Reset Date Fields
        Select Case Val(CStr(GetWorkbookNameValue(ThisWorkbook, "DateAfterExport", 1)))
            Case 1
                ResetNewEntryDateFields DateAdd("d", 1, entryDate)
            Case 3
                ResetNewEntryDateFields todayDate
        End Select

    '--- 7b. Reset PIC to Default
        SetNewEntryValue "nePIC", "Self"

    '--- 7c. Clear Remaining Input Fields
        ClearNewEntryFields NewEntryClearFieldNames()
        ResetNewEntryRouteFieldsAfterAdd

        On Error GoTo Cleanup

    '===============================
    ' STEP 8: REFRESH DATA
    '===============================
        diagStep = "Step 8a: Add Routes"
        WriteCrumb diagStep
        SetAddToLogbookStatus "Updating routes"
        On Error Resume Next
        Call AddNewRoutes
        On Error GoTo Cleanup

        diagStep = "Step 8a.1: Refresh Airport Stats"
        WriteCrumb diagStep
        SetAddToLogbookStatus "Refreshing airport stats"
        RefreshAirportVisitStatsWithWorkbookProtection ThisWorkbook, False

        '--- Refresh all pivot tables in the workbook
        diagStep = "Step 8b: Refresh Pivots"
        WriteCrumb diagStep
        SetAddToLogbookStatus "Refreshing summaries"
        DoEvents
        On Error Resume Next
        RefreshWorkbookPivotSummariesWithWorkbookProtection ThisWorkbook
        On Error GoTo Cleanup

    '===============================
    ' STEP 9: SUCCESS MESSAGE
    '===============================
        diagStep = "Step 9a: Finalise Layout"
        WriteCrumb diagStep
        SetAddToLogbookStatus "Finalising layout"
        On Error Resume Next
        If logbookWasProtected Then
            WriteCrumb "Step 9a.0: Unprotect for final layout"
            wsLog.Unprotect Password:=ProtectionPassword()
        End If
        WriteCrumb "Step 9a.1: Update hidden rows"
        UpdateHiddenRows ThisWorkbook
        RestoreNewEntryView
        TraceAddToLogbookLayout "After final UpdateHiddenRows", tbl
        If entryWasWritten Then
            WriteCrumb "Step 9a.2: Save final layout"
            ThisWorkbook.Save
            TraceAddToLogbookLayout "After final layout save", tbl
        End If
        If logbookWasProtected Then
            WriteCrumb "Step 9a.3: Re-protect after final layout"
            ProtectLogbookSheetForRuntime wsLog
            TraceAddToLogbookLayout "After final layout protect", tbl
        End If
        WriteCrumb "Step 9a.4: Restore New Entry view"
        RestoreNewEntryView
        On Error GoTo Cleanup

        diagStep = "Step 9: Success"
        SetAddToLogbookStatus "Done"

        If showSuccessMessage Then MsgBox "Entry successfully added to Logbook!", vbInformation

    '===============================
    ' STEP 10: CLEANUP & RESTORE SETTINGS
    '===============================

Cleanup:
        '--- Capture error details before restoring settings (restoring clears Err object)
        Dim errNum      As Long
        Dim errDesc     As String
        errNum = Err.Number
        errDesc = Err.Description

        If totalsStateCaptured Then
            On Error Resume Next
            tbl.ShowTotals = totalsWereOn
            On Error GoTo 0
            TraceAddToLogbookLayout "Cleanup restored totals", tbl
        End If

        RestoreNewEntryView
        TraceAddToLogbookLayout "Cleanup before app state restore", tbl
        Application.ScreenUpdating = True
        Application.EnableEvents = True
        Application.Calculation = xlCalculationAutomatic
        Application.CutCopyMode = False
        If previousDisplayStatusBar Then
            If VarType(previousStatusBar) = vbString Then
                Application.StatusBar = CStr(previousStatusBar)
            Else
                Application.StatusBar = False
            End If
        Else
            Application.StatusBar = False
        End If
        Application.DisplayStatusBar = previousDisplayStatusBar
        TraceAddToLogbookLayout "Cleanup after app state restore", tbl

        If logbookWasProtected Then
            On Error Resume Next
            TraceAddToLogbookLayout "Cleanup before protect", tbl
            ProtectLogbookSheetForRuntime wsLog
            On Error GoTo 0
            TraceAddToLogbookLayout "Cleanup after protect", tbl
        End If

        '--- Report any unexpected error (errNum 0 means clean exit via GoTo Cleanup on cancel)
        If errNum <> 0 Then
            On Error Resume Next
            WriteDebugLog "AddToLogbook", errNum, errDesc, diagStep
            On Error GoTo 0
            MsgBox BuildUserFacingErrorMessage( _
                   "The entry could not be added cleanly.", _
                   "Check the Logbook table before adding another entry. If the entry is missing, try adding it again. If this keeps happening, use the Report a Bug button and include the debug log.", _
                   errNum, "AddToLogbook", errDesc, diagStep), _
                   vbCritical, "Add Entry Failed"
        End If

        Exit Sub

End Sub

Private Sub SetAddToLogbookStatus(ByVal stepText As String)
    Application.DisplayStatusBar = True
    Application.StatusBar = "Electronic Logbook: " & stepText
    DoEvents
End Sub

Private Sub ProtectLogbookSheetForRuntime(ws As Worksheet)
    UnlockLogbookRowsForDeletion ws
    LockLogbookDateColumn ws
    ws.Protect Password:=ProtectionPassword(), DrawingObjects:=False, Contents:=True, Scenarios:=True, _
               UserInterfaceOnly:=True, AllowFiltering:=True, AllowSorting:=True, _
               AllowFormattingCells:=True, AllowFormattingColumns:=True, AllowFormattingRows:=True, _
               AllowUsingPivotTables:=True, _
               AllowInsertingRows:=True, AllowDeletingRows:=True
End Sub

Public Function RefreshTodayValue() As Boolean
    On Error GoTo CleanExit

    Dim todayCell As Range
    Set todayCell = ThisWorkbook.Names("today").RefersToRange

    If todayCell.HasFormula Or _
       Not IsDate(todayCell.Value) Or _
       CLng(CDate(todayCell.Value)) <> CLng(Date) Then
        todayCell.Value = Date
        RefreshTodayValue = True
    End If

CleanExit:
End Function

Private Sub ApplyLogbookCellDataFormatting(ByVal targetCell As Range, _
                                           ByVal templateCell As Range)
    With targetCell
        .NumberFormat = templateCell.NumberFormat
        .HorizontalAlignment = templateCell.HorizontalAlignment
        .VerticalAlignment = templateCell.VerticalAlignment
        .WrapText = templateCell.WrapText
        .Orientation = templateCell.Orientation
        .IndentLevel = templateCell.IndentLevel
        .ShrinkToFit = templateCell.ShrinkToFit
        .ReadingOrder = templateCell.ReadingOrder
        .Font.Name = templateCell.Font.Name
        .Font.Size = templateCell.Font.Size
        .Font.Bold = templateCell.Font.Bold
        .Font.Italic = templateCell.Font.Italic
        .Font.Underline = templateCell.Font.Underline
    End With
End Sub

Public Sub NormaliseLogbookTableFormatting(Optional ByVal showConfirmation As Boolean = True)
    Dim tbl As ListObject

    On Error GoTo Fail

    Application.ScreenUpdating = False
    Set tbl = ThisWorkbook.Sheets("Logbook").ListObjects("Logbook")
    NormaliseLogbookFormatting tbl
    Application.ScreenUpdating = True

    If showConfirmation Then
        MsgBox "Logbook table formatting has been reset to the selected table style.", _
               vbInformation, "Logbook Formatting Reset"
    End If
    Exit Sub
Fail:
    Application.ScreenUpdating = True
    MsgBox BuildUserFacingErrorMessage( _
           "The Logbook formatting could not be reset.", _
           "Your logbook data was not intentionally changed. Try closing and reopening the workbook, then run the formatting reset again.", _
           Err.Number, Err.Source, Err.Description, "Resetting Logbook table formatting"), _
           vbCritical, "Formatting Reset Failed"
End Sub

Public Sub NormaliseLogbookFormatting(ByVal tbl As ListObject, _
                                      Optional ByVal targetWorkbook As Workbook = Nothing)
    If targetWorkbook Is Nothing Then Set targetWorkbook = tbl.Parent.Parent

    NormaliseLogbookDataFormatting tbl
    NormaliseLogbookDataBorders tbl
    NormaliseLogbookTotalsFormatting tbl
    UpdateLogbookTotalsNamedRanges tbl, targetWorkbook
    UpdateLogbookFilterHeadersNamedRange tbl, targetWorkbook
    ApplyLogbookPalette tbl, targetWorkbook
    ApplyLogbookTotalsRowBorders tbl
    ApplyLogbookTotalsFormatting tbl, targetWorkbook
    ApplyVisibleLogbookOutsideBorder tbl
    ApplyNativeCheckboxesIfAvailable tbl
End Sub

Private Sub NormaliseLogbookDataFormatting(ByVal tbl As ListObject)
    Dim templateRow As Range
    Dim dataColumn As Range
    Dim colIndex As Long

    If tbl.DataBodyRange Is Nothing Then Exit Sub

    Set templateRow = tbl.DataBodyRange.Rows(1)
    tbl.DataBodyRange.Font.Name = templateRow.Cells(1, 1).Font.Name
    tbl.DataBodyRange.Font.Size = templateRow.Cells(1, 1).Font.Size

    For colIndex = 1 To tbl.ListColumns.Count
        Set dataColumn = tbl.DataBodyRange.Columns(colIndex)
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

Private Sub NormaliseLogbookDataBorders(ByVal tbl As ListObject)
    Dim templateRow As Range
    Dim dataColumn As Range
    Dim colIndex As Long
    Dim leftLineStyle() As Variant
    Dim leftWeight() As Variant
    Dim leftColor() As Variant
    Dim rightLineStyle() As Variant
    Dim rightWeight() As Variant
    Dim rightColor() As Variant

    If tbl.DataBodyRange Is Nothing Then Exit Sub

    Set templateRow = tbl.DataBodyRange.Rows(1)
    ReDim leftLineStyle(1 To tbl.ListColumns.Count)
    ReDim leftWeight(1 To tbl.ListColumns.Count)
    ReDim leftColor(1 To tbl.ListColumns.Count)
    ReDim rightLineStyle(1 To tbl.ListColumns.Count)
    ReDim rightWeight(1 To tbl.ListColumns.Count)
    ReDim rightColor(1 To tbl.ListColumns.Count)

    For colIndex = 1 To tbl.ListColumns.Count
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

    tbl.DataBodyRange.Borders.LineStyle = xlNone

    For colIndex = 1 To tbl.ListColumns.Count
        Set dataColumn = tbl.DataBodyRange.Columns(colIndex)
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

Private Sub UpdateLogbookTotalsNamedRanges(ByVal tbl As ListObject, _
                                           ByVal targetWorkbook As Workbook)
    Dim ws As Worksheet
    Dim totalsBlock As Range
    Dim sumTotalsRange As Range
    Dim totalsFormula As String
    Dim sumTotalsFormula As String

    If Not tbl.ShowTotals Then Exit Sub

    Set ws = tbl.Parent
    Set totalsBlock = ws.Range(ws.Cells(tbl.TotalsRowRange.Row, tbl.ListColumns("Flight ID").Range.Column), _
                               ws.Cells(tbl.TotalsRowRange.Row + 1, tbl.ListColumns("Other Pilot or Crew").Range.Column))
    Set sumTotalsRange = ws.Range(ws.Cells(tbl.TotalsRowRange.Row, tbl.ListColumns(LogbookCustomStartColumn(tbl)).Range.Column), _
                                  ws.Cells(tbl.TotalsRowRange.Row, tbl.ListColumns("TotalApps").Range.Column))

    totalsFormula = "='" & Replace(ws.Name, "'", "''") & "'!" & totalsBlock.Address
    sumTotalsFormula = "='" & Replace(ws.Name, "'", "''") & "'!" & sumTotalsRange.Address

    On Error Resume Next
    targetWorkbook.Names("LogbookTotals").RefersTo = totalsFormula
    If Err.Number <> 0 Then
        Err.Clear
        targetWorkbook.Names.Add Name:="LogbookTotals", RefersTo:=totalsFormula
    End If
    Err.Clear
    targetWorkbook.Names("LogbookSumTotals").RefersTo = sumTotalsFormula
    If Err.Number <> 0 Then
        Err.Clear
        targetWorkbook.Names.Add Name:="LogbookSumTotals", RefersTo:=sumTotalsFormula
    End If
    On Error GoTo 0
End Sub

Private Sub UpdateLogbookFilterHeadersNamedRange(ByVal tbl As ListObject, _
                                                 ByVal targetWorkbook As Workbook)
    Dim ws As Worksheet
    Dim dateHeader As Range
    Dim entryHeaders As Range
    Dim filterFormula As String

    Set ws = tbl.Parent
    Set dateHeader = tbl.HeaderRowRange.Cells(1, tbl.ListColumns("Date").Index)
    Set entryHeaders = ws.Range(ws.Cells(tbl.HeaderRowRange.Row, tbl.ListColumns("Type").Range.Column), _
                                ws.Cells(tbl.HeaderRowRange.Row, tbl.ListColumns("Circling").Range.Column))

    filterFormula = "='" & Replace(ws.Name, "'", "''") & "'!" & dateHeader.Address & _
                    ",'" & Replace(ws.Name, "'", "''") & "'!" & entryHeaders.Address

    On Error Resume Next
    targetWorkbook.Names("LogbookFilterHeaders").RefersTo = filterFormula
    If Err.Number <> 0 Then
        Err.Clear
        targetWorkbook.Names.Add Name:="LogbookFilterHeaders", RefersTo:=filterFormula
    End If
    On Error GoTo 0
End Sub

Private Sub ApplyLogbookPalette(ByVal tbl As ListObject, _
                                ByVal targetWorkbook As Workbook)
    Const SUM_TOTALS_LIGHTNESS As Double = 0.2
    Dim headerRange As Range
    Dim sumTotalsRange As Range
    Dim secondaryColor As Long

    If tbl.DataBodyRange Is Nothing Then Exit Sub

    secondaryColor = tbl.DataBodyRange.Rows(1).Cells(1, 1).DisplayFormat.Interior.Color

    On Error Resume Next
    Set headerRange = targetWorkbook.Names("LogbookHeaders").RefersToRange
    Set sumTotalsRange = targetWorkbook.Names("LogbookSumTotals").RefersToRange
    On Error GoTo 0

    If Not headerRange Is Nothing Then
        headerRange.Interior.Pattern = xlSolid
        headerRange.Interior.Color = secondaryColor
        headerRange.Font.Color = ContrastingTextColor(secondaryColor)
    End If

    If Not tbl.ShowTotals Then Exit Sub

    tbl.TotalsRowRange.Interior.Pattern = xlSolid
    tbl.TotalsRowRange.Interior.Color = vbBlack
    tbl.TotalsRowRange.Font.Color = vbWhite

    If Not sumTotalsRange Is Nothing Then
        sumTotalsRange.Interior.Pattern = xlSolid
        sumTotalsRange.Interior.Color = ColorWithLightness(secondaryColor, SUM_TOTALS_LIGHTNESS)
        sumTotalsRange.Font.Color = vbWhite
        sumTotalsRange.Cells(1, 1).Offset(0, -1).HorizontalAlignment = xlRight
        sumTotalsRange.Cells(1, 1).Offset(0, -1).WrapText = False
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

Private Sub ApplyLogbookTotalsRowBorders(ByVal tbl As ListObject)
    Dim totalsRange As Range

    If Not tbl.ShowTotals Then Exit Sub

    Set totalsRange = tbl.TotalsRowRange
    totalsRange.Borders.LineStyle = xlNone
    SetBorderFormat totalsRange.Borders(xlEdgeTop), xlDouble, xlMedium, vbBlack
    SetBorderFormat totalsRange.Borders(xlEdgeLeft), xlContinuous, xlThin, vbBlack
    SetBorderFormat totalsRange.Borders(xlEdgeRight), xlContinuous, xlThin, vbBlack
    SetBorderFormat totalsRange.Borders(xlEdgeBottom), xlContinuous, xlThin, vbBlack
    SetBorderFormat totalsRange.Borders(xlInsideVertical), xlContinuous, xlThin, vbBlack
End Sub

Private Sub NormaliseLogbookTotalsFormatting(ByVal tbl As ListObject)
    Dim totalsRange As Range
    Dim tableStyleName As String
    Dim tableFontName As String
    Dim tableFontSize As Double
    Dim columnCount As Long
    Dim colIndex As Long
    Dim numberFormats() As Variant
    Dim horizontalAlignments() As Variant
    Dim verticalAlignments() As Variant
    Dim wrapTextValues() As Variant

    If Not tbl.ShowTotals Then Exit Sub

    Set totalsRange = tbl.TotalsRowRange
    tableStyleName = tbl.TableStyle.Name
    tableFontName = tbl.DataBodyRange.Cells(1, 1).Font.Name
    tableFontSize = tbl.DataBodyRange.Cells(1, 1).Font.Size
    columnCount = tbl.ListColumns.Count

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

    tbl.TableStyle = tableStyleName
    totalsRange.Font.Name = tableFontName
    totalsRange.Font.Size = tableFontSize
End Sub

Private Sub ApplyLogbookTotalsFormatting(ByVal tbl As ListObject, _
                                         ByVal targetWorkbook As Workbook)
    Dim ws As Worksheet
    Dim totalsBlock As Range
    Dim topRow As Range
    Dim bottomRow As Range
    Dim firstColumnCells As Range
    Dim labelCells As Range
    Dim hoursCells As Range
    Dim totalsCellLeftOfBlock As Range
    Dim experienceCellLeftOfBlock As Range
    Dim nameFormula As String
    Dim tableFontName As String
    Dim tableFontSize As Double
    Dim secondaryColor As Long

    If Not tbl.ShowTotals Then Exit Sub

    Set ws = tbl.Parent
    Set totalsBlock = ws.Range(ws.Cells(tbl.TotalsRowRange.Row, tbl.ListColumns("Flight ID").Range.Column), _
                               ws.Cells(tbl.TotalsRowRange.Row + 1, tbl.ListColumns("Other Pilot or Crew").Range.Column))
    Set topRow = totalsBlock.Rows(1)
    Set bottomRow = totalsBlock.Rows(2)
    Set firstColumnCells = Union(topRow.Cells(1, 1), bottomRow.Cells(1, 1))
    Set labelCells = Union(topRow.Cells(1, 2), bottomRow.Cells(1, 2))
    Set hoursCells = Union(topRow.Cells(1, 3), bottomRow.Cells(1, 3))
    Set totalsCellLeftOfBlock = topRow.Cells(1, 1).Offset(0, -1)
    Set experienceCellLeftOfBlock = bottomRow.Cells(1, 1).Offset(0, -1)
    tableFontName = tbl.DataBodyRange.Cells(1, 1).Font.Name
    tableFontSize = tbl.DataBodyRange.Cells(1, 1).Font.Size
    secondaryColor = LogbookSecondaryFillColor(tbl)

    nameFormula = "='" & Replace(ws.Name, "'", "''") & "'!" & totalsBlock.Address
    On Error Resume Next
    targetWorkbook.Names("LogbookTotals").RefersTo = nameFormula
    If Err.Number <> 0 Then
        Err.Clear
        targetWorkbook.Names.Add Name:="LogbookTotals", RefersTo:=nameFormula
    End If
    On Error GoTo 0

    topRow.Interior.Pattern = xlNone
    topRow.Font.Color = vbBlack
    topRow.Font.Bold = False
    topRow.Cells(1, 3).Font.Bold = True

    bottomRow.Interior.Pattern = xlSolid
    bottomRow.Interior.Color = secondaryColor
    bottomRow.Font.Color = ContrastingTextColor(secondaryColor)
    bottomRow.Font.Bold = True
    totalsBlock.Font.Name = tableFontName
    totalsBlock.Font.Size = tableFontSize

    firstColumnCells.HorizontalAlignment = xlRight
    firstColumnCells.WrapText = False
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
    totalsCellLeftOfBlock.Interior.Pattern = xlSolid
    totalsCellLeftOfBlock.Interior.Color = vbBlack
    totalsCellLeftOfBlock.Font.Color = vbWhite
    totalsCellLeftOfBlock.Font.Bold = False
    totalsCellLeftOfBlock.HorizontalAlignment = xlRight
    totalsCellLeftOfBlock.WrapText = False
    totalsCellLeftOfBlock.Borders.LineStyle = xlNone
    SetBorderFormat totalsCellLeftOfBlock.Borders(xlEdgeTop), xlDouble, xlMedium, vbBlack
    experienceCellLeftOfBlock.Interior.Pattern = experienceCellLeftOfBlock.Offset(0, -1).Interior.Pattern
    experienceCellLeftOfBlock.Interior.Color = experienceCellLeftOfBlock.Offset(0, -1).Interior.Color
    experienceCellLeftOfBlock.Font.Color = experienceCellLeftOfBlock.Offset(0, -1).Font.Color
    experienceCellLeftOfBlock.Font.Bold = experienceCellLeftOfBlock.Offset(0, -1).Font.Bold
    experienceCellLeftOfBlock.HorizontalAlignment = xlRight
    experienceCellLeftOfBlock.WrapText = False
    experienceCellLeftOfBlock.Borders.LineStyle = xlNone
End Sub

Public Function LogbookCustomStartColumn(ByVal tbl As ListObject) As Long
    Dim firstHoursColumn As Long

    firstHoursColumn = tbl.ListColumns("SeIcusDay").Index
    If ListColumnExists(tbl, "OPC") And tbl.ListColumns("OPC").Index < firstHoursColumn Then
        LogbookCustomStartColumn = tbl.ListColumns("OPC").Index + 1
    ElseIf ListColumnExists(tbl, "Details") Then
        LogbookCustomStartColumn = tbl.ListColumns("Details").Index + 1
    ElseIf ListColumnExists(tbl, "Remarks") Then
        LogbookCustomStartColumn = tbl.ListColumns("Remarks").Index + 1
    End If
End Function

Public Sub ApplyNativeCheckboxesIfAvailable(ByVal tbl As ListObject)
    Dim columnName As Variant

    If tbl.DataBodyRange Is Nothing Then Exit Sub

    For Each columnName In Array("FR", "IPC", "OPC")
        If ListColumnExists(tbl, CStr(columnName)) Then
            On Error Resume Next
            tbl.ListColumns(CStr(columnName)).DataBodyRange.CellControl.SetCheckbox
            Err.Clear
            On Error GoTo 0
        End If
    Next columnName
End Sub

Private Function LogbookSecondaryFillColor(ByVal tbl As ListObject) As Long
    LogbookSecondaryFillColor = tbl.DataBodyRange.Rows(1).Cells(1, 1).DisplayFormat.Interior.Color
End Function

Private Sub ApplyVisibleLogbookOutsideBorder(ByVal tbl As ListObject)
    Dim visibleRange As Range
    Dim ws As Worksheet

    If Not tbl.ShowTotals Then Exit Sub

    Set ws = tbl.Parent
    Set visibleRange = ws.Range(ws.Cells(2, tbl.ListColumns("Date").Range.Column), _
                                ws.Cells(tbl.TotalsRowRange.Row, tbl.ListColumns("Circling").Range.Column))

    SetBorderFormat visibleRange.Borders(xlEdgeTop), xlContinuous, xlThin, vbBlack
    SetBorderFormat visibleRange.Borders(xlEdgeLeft), xlContinuous, xlThin, vbBlack
    SetBorderFormat visibleRange.Borders(xlEdgeRight), xlContinuous, xlThin, vbBlack
    SetBorderFormat visibleRange.Borders(xlEdgeBottom), xlContinuous, xlThin, vbBlack
End Sub

Public Function ListColumnExists(ByVal tbl As ListObject, ByVal columnName As String) As Boolean
    On Error Resume Next
    ListColumnExists = Not tbl.ListColumns(columnName) Is Nothing
    On Error GoTo 0
End Function

Private Function NormaliseKeywordText(ByVal value As String, _
                                      Optional ByVal removeSeparator As Boolean = False) As String
    If removeSeparator Then value = Replace(value, "|", "")
    value = LCase$(value)
    value = Replace(value, "(", "|")
    value = Replace(value, ")", "|")
    value = Replace(value, "-", "|")
    value = Replace(value, ",", "|")
    value = Replace(value, " ", "|")
    value = Replace(value, "&", "|")
    NormaliseKeywordText = "|" & value & "|"
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
    Dim dateRange As Range

    Range("neDate").Formula = NewEntryDateFormula()

    If Not tbl.DataBodyRange Is Nothing Then
        Set dateRange = tbl.ListColumns("Date").DataBodyRange

        ' Keep this as a repair step only; avoid rewriting the whole column every entry.
        If dateRange.Rows.Count = 1 Then
            If Not dateRange.Cells(1, 1).HasFormula Then
                dateRange.Formula = LogbookDateFormula()
            End If
        ElseIf Not dateRange.Cells(dateRange.Rows.Count, 1).HasFormula Then
            dateRange.Formula = LogbookDateFormula()
        End If
    End If
End Sub

Public Sub RefreshLogbookCalculatedFormulas(ByVal tbl As ListObject)
    If tbl Is Nothing Then Exit Sub
    If tbl.DataBodyRange Is Nothing Then Exit Sub

    SetLogbookColumnFormula tbl, "TotalHours", _
        "=SUM(Logbook[[#This Row],[SeIcusDay]:[CopilotNight]])"
    SetLogbookColumnFormula tbl, "TotalApps", _
        "=SUM(Logbook[[#This Row],[ILS]:[DGA (Azi)]])"

    SetLogbookRunningTotalFormula tbl, "CumLandingsDay", "LandingsDay", _
        "Logbook[[#This Row],[LandingsDay]]"
    SetLogbookRunningTotalFormula tbl, "CumLandingsNight", "LandingsNight", _
        "Logbook[[#This Row],[LandingsNight]]"
    SetLogbookRunningTotalFormula tbl, "CumILS", "ILS", _
        "Logbook[[#This Row],[ILS]]"
    SetLogbookRunningTotalFormula tbl, "CumVOR", "VOR", _
        "Logbook[[#This Row],[VOR]]"
    SetLogbookRunningTotalFormula tbl, "CumRNP", "RNP", _
        "Logbook[[#This Row],[RNP]]"
    SetLogbookRunningTotalFormula tbl, "CumNDB", "NDB", _
        "Logbook[[#This Row],[NDB]]"
    SetLogbookRunningTotalFormula tbl, "CumDgaCdi", "DGA (CDI)", _
        "Logbook[[#This Row],[DGA (CDI)]]"
    SetLogbookRunningTotalFormula tbl, "CumDgaAzi", "DGA (Azi)", _
        "Logbook[[#This Row],[DGA (Azi)]]"
    SetLogbookRunningTotalFormula tbl, "CumCirc", "Circling", _
        "Logbook[[#This Row],[Circling]]"
    SetLogbookRunningTotalFormula tbl, "CumTotalApps", "TotalApps", _
        "Logbook[[#This Row],[TotalApps]]"
    SetLogbookColumnFormula tbl, "CumTotalHours", _
        "=SUM(INDEX(Logbook[TotalHours],1):Logbook[[#This Row],[TotalHours]])"
    SetLogbookRunningTotalFormula tbl, "Cum2D", "VOR", _
        "SUM(Logbook[[#This Row],[VOR]:[DGA (Azi)]])"
    SetLogbookRunningTotalFormula tbl, "Cum3D", "ILS", _
        "Logbook[[#This Row],[ILS]]"
    SetLogbookRunningTotalFormula tbl, "CumCDI", "ILS", _
        "SUM(Logbook[[#This Row],[ILS]:[RNP]])+Logbook[[#This Row],[DGA (CDI)]]"
    SetLogbookRunningTotalFormula tbl, "CumAzi", "NDB", _
        "Logbook[[#This Row],[NDB]]+Logbook[[#This Row],[DGA (Azi)]]"
End Sub

Private Sub SetLogbookColumnFormula(ByVal tbl As ListObject, _
                                    ByVal columnName As String, _
                                    ByVal formulaText As String)
    If Not ListColumnExists(tbl, columnName) Then Exit Sub
    If tbl.ListColumns(columnName).DataBodyRange Is Nothing Then Exit Sub

    tbl.ListColumns(columnName).DataBodyRange.Formula = formulaText
End Sub

Private Sub SetLogbookRunningTotalFormula(ByVal tbl As ListObject, _
                                          ByVal columnName As String, _
                                          ByVal sourceColumnName As String, _
                                          ByVal currentRowExpression As String)
    If Not ListColumnExists(tbl, columnName) Then Exit Sub
    If Not ListColumnExists(tbl, sourceColumnName) Then Exit Sub
    If tbl.ListColumns(columnName).DataBodyRange Is Nothing Then Exit Sub

    tbl.ListColumns(columnName).DataBodyRange.FormulaR1C1 = _
        "=IF(ROW()-ROW(Logbook[#Headers])=ROWS(Logbook[" & columnName & "])," & _
        currentRowExpression & "," & currentRowExpression & _
        "+INDEX(Logbook[" & columnName & "],ROW()-ROW(Logbook[#Headers])+1))"
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

Private Sub RefreshWorkbookPivotCaches(ByVal wb As Workbook)
    Dim ws As Worksheet
    Dim pt As PivotTable
    Dim refreshedCaches As Object
    Dim cacheKey As String

    Set refreshedCaches = CreateObject("Scripting.Dictionary")

    For Each ws In wb.Worksheets
        For Each pt In ws.PivotTables
            cacheKey = CStr(pt.PivotCache.Index)
            If Not refreshedCaches.Exists(cacheKey) Then
                pt.PivotCache.Refresh
                refreshedCaches.Add cacheKey, True
            End If
        Next pt
    Next ws
End Sub

Private Sub DisableWorkbookPivotRefreshOnOpen(ByVal wb As Workbook)
    Dim ws As Worksheet
    Dim pt As PivotTable
    Dim cacheKey As String
    Dim updatedCaches As Object

    Set updatedCaches = CreateObject("Scripting.Dictionary")

    On Error Resume Next
    For Each ws In wb.Worksheets
        For Each pt In ws.PivotTables
            cacheKey = CStr(pt.PivotCache.Index)
            If Not updatedCaches.Exists(cacheKey) Then
                pt.PivotCache.RefreshOnFileOpen = False
                updatedCaches.Add cacheKey, True
            End If
        Next pt
    Next ws
    On Error GoTo 0
End Sub

Private Sub RefreshWorkbookPivotSummariesWithWorkbookProtection(ByVal wb As Workbook)
    Dim protectionWasActive As Boolean
    Dim errNum As Long
    Dim errDesc As String
    Dim errSource As String

    On Error GoTo Fail
    protectionWasActive = WorkbookProtectionIsActive(wb)
    If protectionWasActive Then UnprotectWorkbookForEditing wb

    RefreshWorkbookPivotCaches wb
    FixHoursByYearPivotLayout wb
    DisableWorkbookPivotRefreshOnOpen wb

CleanExit:
    If protectionWasActive And Not mProtectionDisabledForSession Then ApplyWorkbookProtection False, wb
    Exit Sub

Fail:
    errNum = Err.Number
    errDesc = Err.Description
    errSource = Err.Source
    On Error Resume Next
    If protectionWasActive And Not mProtectionDisabledForSession Then ApplyWorkbookProtection False, wb
    On Error GoTo 0
    Err.Raise errNum, errSource, errDesc
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

Private Function NewEntryCell(ByVal fieldName As String) As Range
    Dim inputCell As Range
    Dim nm As Name

    Set nm = FindNameByBase(fieldName)
    If nm Is Nothing Then
        Err.Raise 1004, "NewEntryCell", "Named range not found: " & fieldName
    End If

    Set inputCell = nm.RefersToRange

    If inputCell.MergeCells Then
        Set NewEntryCell = inputCell.MergeArea.Cells(1, 1)
    Else
        Set NewEntryCell = inputCell
    End If
End Function

Private Function NewEntryValue(ByVal fieldName As String) As Variant
    NewEntryValue = NewEntryCell(fieldName).Value
End Function

Private Function NewEntryNumericValue(ByVal fieldName As String) As Double
    If NewEntryValue(fieldName) = "" Then
        NewEntryNumericValue = 0
    ElseIf Not IsNumeric(NewEntryValue(fieldName)) Then
        NewEntryNumericValue = 0
    Else
        NewEntryNumericValue = CDbl(NewEntryValue(fieldName))
    End If
End Function

Private Function NewEntryBooleanValue(ByVal fieldName As String) As Boolean
    Dim value As Variant

    value = NewEntryValue(fieldName)
    If VarType(value) = vbBoolean Then
        NewEntryBooleanValue = CBool(value)
    ElseIf IsNumeric(value) Then
        NewEntryBooleanValue = (CDbl(value) <> 0)
    Else
        Select Case LCase$(Trim$(CStr(value)))
            Case "true", "yes", "y", "1", "x"
                NewEntryBooleanValue = True
        End Select
    End If
End Function

Private Sub SetNewEntryValue(ByVal fieldName As String, ByVal value As Variant)
    NewEntryCell(fieldName).Value = value
End Sub

Private Sub ClearNewEntryFields(ByVal fieldNames As Variant)
    Dim fieldName As Variant
    Dim clearedAreas As Object
    Dim clearArea As Range
    Dim areaKey As String

    Set clearedAreas = CreateObject("Scripting.Dictionary")
    For Each fieldName In fieldNames
        Set clearArea = NewEntryCell(CStr(fieldName))
        If clearArea.MergeCells Then Set clearArea = clearArea.MergeArea
        areaKey = clearArea.Worksheet.Name & "!" & clearArea.Address(External:=False)
        If Not clearedAreas.Exists(areaKey) Then
            clearArea.ClearContents
            clearedAreas.Add areaKey, True
        End If
    Next fieldName
End Sub

Public Sub ClearNewEntryValidationHighlightsForTarget(ByVal targetRange As Range)
    Dim fieldName As Variant
    Dim inputArea As Range

    If targetRange Is Nothing Then Exit Sub

    On Error Resume Next
    For Each fieldName In NewEntryLayoutFieldNames()
        Set inputArea = NewEntryCell(CStr(fieldName))
        If inputArea.MergeCells Then Set inputArea = inputArea.MergeArea
        If inputArea.Worksheet Is targetRange.Worksheet Then
            If Not Intersect(targetRange, inputArea) Is Nothing Then
                ClearNewEntryValidationHighlight inputArea
            End If
        End If
        Set inputArea = Nothing
    Next fieldName
    On Error GoTo 0
End Sub

Private Sub ClearNewEntryValidationHighlights()
    Dim fieldName As Variant
    Dim inputArea As Range

    On Error Resume Next
    For Each fieldName In NewEntryLayoutFieldNames()
        Set inputArea = NewEntryCell(CStr(fieldName))
        If inputArea.MergeCells Then Set inputArea = inputArea.MergeArea
        ClearNewEntryValidationHighlight inputArea
        Set inputArea = Nothing
    Next fieldName
    On Error GoTo 0
End Sub

Private Sub MarkNewEntryProblemFields(ByVal fieldNames As Variant)
    Dim fieldName As Variant
    Dim inputArea As Range

    On Error Resume Next
    For Each fieldName In fieldNames
        Set inputArea = NewEntryCell(CStr(fieldName))
        If inputArea.MergeCells Then Set inputArea = inputArea.MergeArea
        With inputArea.Interior
            .Pattern = xlSolid
            .Color = NewEntryValidationHighlightColor()
        End With
        Set inputArea = Nothing
    Next fieldName
    On Error GoTo 0
End Sub

Private Sub ClearNewEntryValidationHighlight(ByVal inputArea As Range)
    If inputArea Is Nothing Then Exit Sub

    If inputArea.Interior.Pattern = xlSolid And _
       inputArea.Interior.Color = NewEntryValidationHighlightColor() Then
        inputArea.Interior.Pattern = xlNone
    End If
End Sub

Private Function NewEntryValidationHighlightColor() As Long
    NewEntryValidationHighlightColor = RGB(255, 199, 206)
End Function

Private Sub EnsureAirportIcaoValidationName()
    Dim nm As Name
    Dim refersToText As String

    refersToText = "=Airports[ICAO]"

    On Error Resume Next
    Set nm = ThisWorkbook.Names(AIRPORT_ICAO_VALIDATION_NAME)
    On Error GoTo 0

    If nm Is Nothing Then
        ThisWorkbook.Names.Add Name:=AIRPORT_ICAO_VALIDATION_NAME, RefersTo:=refersToText
    Else
        nm.RefersTo = refersToText
    End If
End Sub

Private Sub DeleteLegacyNewEntryAirportHintShape()
    Dim sheetName As Variant
    Dim ws As Worksheet

    For Each sheetName In Array(NEW_ENTRY_ACTIVE_SHEET, NEW_ENTRY_UNUSED_SHEET)
        On Error Resume Next
        Set ws = ThisWorkbook.Worksheets(CStr(sheetName))
        If Not ws Is Nothing Then ws.Shapes("AirportNameHint").Delete
        Set ws = Nothing
        On Error GoTo 0
    Next sheetName
End Sub

Private Function CombineNewEntryFieldNames(ParamArray fieldGroups() As Variant) As Variant
    Dim combined As Collection
    Dim result() As String
    Dim group As Variant
    Dim fieldName As Variant
    Dim i As Long

    Set combined = New Collection

    For Each group In fieldGroups
        If IsArray(group) Then
            For Each fieldName In group
                combined.Add CStr(fieldName)
            Next fieldName
        Else
            combined.Add CStr(group)
        End If
    Next group

    If combined.Count = 0 Then
        CombineNewEntryFieldNames = Array()
        Exit Function
    End If

    ReDim result(0 To combined.Count - 1)
    For i = 1 To combined.Count
        result(i - 1) = CStr(combined(i))
    Next i

    CombineNewEntryFieldNames = result
End Function

Private Function VariantArrayHasItems(ByVal values As Variant) As Boolean
    On Error GoTo CleanExit
    If IsArray(values) Then VariantArrayHasItems = (UBound(values) >= LBound(values))
CleanExit:
End Function

Private Function UnrecognisedNewEntryAirportFieldNames() As Variant
    Dim problemFields As Collection
    Dim result() As String
    Dim i As Long

    Set problemFields = New Collection

    If Trim$(CStr(NewEntryValue("neFrom"))) <> "" Then
        If Not NewEntryAirportIsRecognised(CStr(NewEntryValue("neFrom"))) Then problemFields.Add "neFrom"
    End If
    If Trim$(CStr(NewEntryValue("neTo"))) <> "" Then
        If Not NewEntryAirportIsRecognised(CStr(NewEntryValue("neTo"))) Then problemFields.Add "neTo"
    End If

    If problemFields.Count = 0 Then
        UnrecognisedNewEntryAirportFieldNames = Array()
        Exit Function
    End If

    ReDim result(0 To problemFields.Count - 1)
    For i = 1 To problemFields.Count
        result(i - 1) = CStr(problemFields(i))
    Next i

    UnrecognisedNewEntryAirportFieldNames = result
End Function

Private Function UnrecognisedNewEntryAirportWarningMessage(ByVal fieldNames As Variant) As String
    Dim hasFrom As Boolean
    Dim hasTo As Boolean
    Dim fieldName As Variant
    Dim airportText As String

    For Each fieldName In fieldNames
        Select Case LCase$(CStr(fieldName))
            Case "nefrom"
                hasFrom = True
            Case "neto"
                hasTo = True
        End Select
    Next fieldName

    If hasFrom And hasTo Then
        airportText = "The Departure and Destination airport codes are not recognised."
    ElseIf hasFrom Then
        airportText = "The Departure airport code is not recognised."
    ElseIf hasTo Then
        airportText = "The Destination airport code is not recognised."
    Else
        airportText = "An airport code is not recognised."
    End If

    UnrecognisedNewEntryAirportWarningMessage = _
        "Warning: " & airportText & " Continue?"
End Function

Private Function NewEntryAirportIsRecognised(ByVal airportCode As String) As Boolean
    Dim tblAirports As ListObject
    Dim rowIndex As Long
    Dim candidate As String
    Dim icaoCol As Long
    Dim twoCol As Long
    Dim threeCol As Long

    candidate = UCase$(Trim$(airportCode))
    If candidate = "" Then Exit Function

    On Error GoTo CleanExit
    Set tblAirports = ThisWorkbook.Worksheets("Airports").ListObjects("Airports")
    If tblAirports.DataBodyRange Is Nothing Then Exit Function

    icaoCol = tblAirports.ListColumns("ICAO").Index
    twoCol = tblAirports.ListColumns("Two").Index
    threeCol = tblAirports.ListColumns("Three").Index

    For rowIndex = 1 To tblAirports.DataBodyRange.Rows.Count
        If candidate = UCase$(Trim$(CStr(tblAirports.DataBodyRange.Cells(rowIndex, icaoCol).Value))) Or _
           candidate = UCase$(Trim$(CStr(tblAirports.DataBodyRange.Cells(rowIndex, twoCol).Value))) Or _
           candidate = UCase$(Trim$(CStr(tblAirports.DataBodyRange.Cells(rowIndex, threeCol).Value))) Then
            NewEntryAirportIsRecognised = True
            Exit Function
        End If
    Next rowIndex

CleanExit:
End Function

Private Sub ResetNewEntryRouteFieldsAfterAdd()
    Dim fromText As String
    Dim toText As String
    Dim fromIsBase As Boolean
    Dim toIsBase As Boolean

    fromText = Trim$(CStr(NewEntryValue("neFrom")))
    toText = Trim$(CStr(NewEntryValue("neTo")))

    If fromText = "" And toText = "" Then Exit Sub
    If toText = "" Then Exit Sub
    If StrComp(fromText, toText, vbTextCompare) = 0 Then Exit Sub

    fromIsBase = NewEntryAirportIsBase(fromText)
    toIsBase = NewEntryAirportIsBase(toText)

    If fromIsBase Then
        SetNewEntryValue "neFrom", toText
        SetNewEntryValue "neTo", fromText
    ElseIf Not toIsBase Then
        SetNewEntryValue "neFrom", toText
        SetNewEntryValue "neTo", vbNullString
    End If
End Sub

Private Function NewEntryAirportIsBase(ByVal airportCode As String) As Boolean
    Dim tblBase As ListObject
    Dim tblAirports As ListObject
    Dim rowIndex As Long
    Dim airportIcao As String
    Dim baseIcao As String
    Dim baseAirportName As String

    airportIcao = NewEntryAirportIcao(airportCode)
    If airportIcao = "" Then airportIcao = UCase$(Trim$(airportCode))
    If airportIcao = "" Then Exit Function

    On Error Resume Next
    Set tblBase = FindListObject(ThisWorkbook, "BaseAirportsTop10")
    On Error GoTo 0

    If Not tblBase Is Nothing Then
        If ListColumnExists(tblBase, "Base") And Not tblBase.DataBodyRange Is Nothing Then
            For rowIndex = 1 To tblBase.DataBodyRange.Rows.Count
                If NewEntryRouteBooleanValue(tblBase.DataBodyRange.Cells(rowIndex, tblBase.ListColumns("Base").Index).Value) Then
                    baseIcao = vbNullString
                    If ListColumnExists(tblBase, "ICAO") Then
                        baseIcao = UCase$(Trim$(CStr(tblBase.DataBodyRange.Cells(rowIndex, tblBase.ListColumns("ICAO").Index).Value)))
                    End If
                    If baseIcao = "" And ListColumnExists(tblBase, "Airport") Then
                        baseAirportName = Trim$(CStr(tblBase.DataBodyRange.Cells(rowIndex, tblBase.ListColumns("Airport").Index).Value))
                        baseIcao = NewEntryAirportIcao(baseAirportName)
                    End If

                    If baseIcao <> "" And baseIcao = airportIcao Then
                        NewEntryAirportIsBase = True
                        Exit Function
                    End If
                End If
            Next rowIndex
        End If
    End If

    On Error Resume Next
    Set tblAirports = ThisWorkbook.Worksheets("Airports").ListObjects("Airports")
    On Error GoTo 0

    If tblAirports Is Nothing Then Exit Function
    If Not ListColumnExists(tblAirports, "Base") Then Exit Function
    If Not ListColumnExists(tblAirports, "ICAO") Then Exit Function
    If tblAirports.DataBodyRange Is Nothing Then Exit Function

    For rowIndex = 1 To tblAirports.DataBodyRange.Rows.Count
        If NewEntryRouteBooleanValue(tblAirports.DataBodyRange.Cells(rowIndex, tblAirports.ListColumns("Base").Index).Value) Then
            baseIcao = UCase$(Trim$(CStr(tblAirports.DataBodyRange.Cells(rowIndex, tblAirports.ListColumns("ICAO").Index).Value)))
            If baseIcao <> "" And baseIcao = airportIcao Then
                NewEntryAirportIsBase = True
                Exit Function
            End If
        End If
    Next rowIndex
End Function

Private Function NewEntryAirportIcao(ByVal airportText As String) As String
    Dim tblAirports As ListObject
    Dim rowIndex As Long
    Dim candidate As String
    Dim icaoCol As Long
    Dim twoCol As Long
    Dim threeCol As Long
    Dim airportCol As Long
    Dim rowIcao As String
    Dim rowTwo As String
    Dim rowThree As String
    Dim rowAirport As String

    candidate = UCase$(Trim$(airportText))
    If candidate = "" Then Exit Function

    On Error GoTo CleanExit
    Set tblAirports = ThisWorkbook.Worksheets("Airports").ListObjects("Airports")
    If tblAirports.DataBodyRange Is Nothing Then Exit Function
    If Not ListColumnExists(tblAirports, "ICAO") Then Exit Function

    icaoCol = tblAirports.ListColumns("ICAO").Index
    If ListColumnExists(tblAirports, "Two") Then twoCol = tblAirports.ListColumns("Two").Index
    If ListColumnExists(tblAirports, "Three") Then threeCol = tblAirports.ListColumns("Three").Index
    If ListColumnExists(tblAirports, "Airport") Then airportCol = tblAirports.ListColumns("Airport").Index

    For rowIndex = 1 To tblAirports.DataBodyRange.Rows.Count
        rowIcao = UCase$(Trim$(CStr(tblAirports.DataBodyRange.Cells(rowIndex, icaoCol).Value)))
        rowTwo = vbNullString
        rowThree = vbNullString
        rowAirport = vbNullString
        If twoCol > 0 Then rowTwo = UCase$(Trim$(CStr(tblAirports.DataBodyRange.Cells(rowIndex, twoCol).Value)))
        If threeCol > 0 Then rowThree = UCase$(Trim$(CStr(tblAirports.DataBodyRange.Cells(rowIndex, threeCol).Value)))
        If airportCol > 0 Then rowAirport = Trim$(CStr(tblAirports.DataBodyRange.Cells(rowIndex, airportCol).Value))

        If candidate = rowIcao Or _
           (rowTwo <> "" And candidate = rowTwo) Or _
           (rowThree <> "" And candidate = rowThree) Or _
           (rowAirport <> "" And StrComp(Trim$(airportText), rowAirport, vbTextCompare) = 0) Then
            NewEntryAirportIcao = rowIcao
            Exit Function
        End If
    Next rowIndex

CleanExit:
End Function

Private Function DistantNewEntryAirportFieldNames(ByRef warningMessage As String) As Variant
    Dim problemFields As Collection
    Dim warningLines As Collection
    Dim result() As String
    Dim i As Long

    Set problemFields = New Collection
    Set warningLines = New Collection

    AddDistantNewEntryAirportWarning "neFrom", "Departure", problemFields, warningLines
    AddDistantNewEntryAirportWarning "neTo", "Destination", problemFields, warningLines
    AddDistantNewEntryRouteLegWarning problemFields, warningLines

    If problemFields.Count = 0 Then
        DistantNewEntryAirportFieldNames = Array()
        Exit Function
    End If

    warningMessage = "Warning: One or more route airport distance checks look unusual." & _
                     vbCrLf & vbCrLf
    For i = 1 To warningLines.Count
        warningMessage = warningMessage & CStr(warningLines(i)) & vbCrLf
    Next i
    warningMessage = warningMessage & vbCrLf & "Continue?"

    ReDim result(0 To problemFields.Count - 1)
    For i = 1 To problemFields.Count
        result(i - 1) = CStr(problemFields(i))
    Next i

    DistantNewEntryAirportFieldNames = result
End Function

Private Sub AddDistantNewEntryAirportWarning(ByVal fieldName As String, _
                                             ByVal fieldLabel As String, _
                                             ByVal problemFields As Collection, _
                                             ByVal warningLines As Collection)
    Dim airportText As String
    Dim airportIcao As String
    Dim airportName As String
    Dim nearestVisitedIcao As String
    Dim nearestVisitedName As String
    Dim nearestDistanceNm As Double

    airportText = Trim$(CStr(NewEntryValue(fieldName)))
    If airportText = "" Then Exit Sub

    airportIcao = NewEntryAirportIcao(airportText)
    If airportIcao = "" Then Exit Sub

    If Not NearestVisitedAirportDistanceNm(airportIcao, airportName, nearestVisitedIcao, _
                                           nearestVisitedName, nearestDistanceNm) Then Exit Sub
    If nearestDistanceNm < REMOTE_AIRPORT_WARNING_THRESHOLD_NM Then Exit Sub

    problemFields.Add fieldName
    warningLines.Add fieldLabel & " " & airportIcao & AirportNameSuffix(airportName) & _
                     " is about " & Format$(nearestDistanceNm, "#,##0") & _
                     " NM from the nearest previously visited airport, " & _
                     nearestVisitedIcao & AirportNameSuffix(nearestVisitedName) & "."
End Sub

Private Sub AddDistantNewEntryRouteLegWarning(ByVal problemFields As Collection, _
                                              ByVal warningLines As Collection)
    Dim fromText As String
    Dim toText As String
    Dim fromIcao As String
    Dim toIcao As String
    Dim fromName As String
    Dim toName As String
    Dim fromLat As Double
    Dim fromLon As Double
    Dim toLat As Double
    Dim toLon As Double
    Dim routeDistanceNm As Double

    fromText = Trim$(CStr(NewEntryValue("neFrom")))
    toText = Trim$(CStr(NewEntryValue("neTo")))
    If fromText = "" Or toText = "" Then Exit Sub

    fromIcao = NewEntryAirportIcao(fromText)
    toIcao = NewEntryAirportIcao(toText)
    If fromIcao = "" Or toIcao = "" Then Exit Sub
    If fromIcao = toIcao Then Exit Sub

    If Not AirportLocationByIcao(fromIcao, fromName, fromLat, fromLon) Then Exit Sub
    If Not AirportLocationByIcao(toIcao, toName, toLat, toLon) Then Exit Sub

    routeDistanceNm = GreatCircleDistanceNm(fromLat, fromLon, toLat, toLon)
    If routeDistanceNm < REMOTE_AIRPORT_WARNING_THRESHOLD_NM Then Exit Sub

    problemFields.Add "neFrom"
    problemFields.Add "neTo"
    warningLines.Add "The route from " & fromIcao & AirportNameSuffix(fromName) & _
                     " to " & toIcao & AirportNameSuffix(toName) & _
                     " is about " & Format$(routeDistanceNm, "#,##0") & " NM."
End Sub

Private Function HighSpeedNewEntryRouteFieldNames(ByRef warningMessage As String) As Variant
    Dim fromText As String
    Dim toText As String
    Dim fromIcao As String
    Dim toIcao As String
    Dim fromName As String
    Dim toName As String
    Dim fromLat As Double
    Dim fromLon As Double
    Dim toLat As Double
    Dim toLon As Double
    Dim routeDistanceNm As Double
    Dim totalFlightHours As Double
    Dim impliedSpeedKt As Double

    totalFlightHours = SumNewEntryFields(NewEntryFlightTimeFieldNames())
    If totalFlightHours <= 0 Then
        HighSpeedNewEntryRouteFieldNames = Array()
        Exit Function
    End If

    fromText = Trim$(CStr(NewEntryValue("neFrom")))
    toText = Trim$(CStr(NewEntryValue("neTo")))
    If fromText = "" Or toText = "" Then
        HighSpeedNewEntryRouteFieldNames = Array()
        Exit Function
    End If

    fromIcao = NewEntryAirportIcao(fromText)
    toIcao = NewEntryAirportIcao(toText)
    If fromIcao = "" Or toIcao = "" Or fromIcao = toIcao Then
        HighSpeedNewEntryRouteFieldNames = Array()
        Exit Function
    End If

    If Not AirportLocationByIcao(fromIcao, fromName, fromLat, fromLon) Then
        HighSpeedNewEntryRouteFieldNames = Array()
        Exit Function
    End If
    If Not AirportLocationByIcao(toIcao, toName, toLat, toLon) Then
        HighSpeedNewEntryRouteFieldNames = Array()
        Exit Function
    End If

    routeDistanceNm = GreatCircleDistanceNm(fromLat, fromLon, toLat, toLon)
    impliedSpeedKt = routeDistanceNm / totalFlightHours
    If impliedSpeedKt <= HIGH_SPEED_ROUTE_WARNING_THRESHOLD_KT Then
        HighSpeedNewEntryRouteFieldNames = Array()
        Exit Function
    End If

    warningMessage = "Warning: The route from " & fromIcao & AirportNameSuffix(fromName) & _
                     " to " & toIcao & AirportNameSuffix(toName) & " is about " & _
                     Format$(routeDistanceNm, "#,##0") & " NM. With " & _
                     Format$(totalFlightHours, "0.0#") & " flight hours recorded, the implied " & _
                     "average speed is " & Format$(impliedSpeedKt, "#,##0") & _
                     " knots. Continue?"
    HighSpeedNewEntryRouteFieldNames = CombineNewEntryFieldNames( _
        Array("neFrom", "neTo"), NewEntryFlightTimeFieldNames())
End Function

Private Function NearestVisitedAirportDistanceNm(ByVal airportIcao As String, _
                                                 ByRef airportName As String, _
                                                 ByRef nearestVisitedIcao As String, _
                                                 ByRef nearestVisitedName As String, _
                                                 ByRef nearestDistanceNm As Double) As Boolean
    Dim tblAirports As ListObject
    Dim targetRow As Long
    Dim rowIndex As Long
    Dim icaoCol As Long
    Dim airportCol As Long
    Dim latCol As Long
    Dim lonCol As Long
    Dim visitsCol As Long
    Dim targetLat As Double
    Dim targetLon As Double
    Dim visitedLat As Double
    Dim visitedLon As Double
    Dim distanceNm As Double
    Dim rowIcao As String
    Dim visits As Variant

    On Error GoTo CleanExit
    Set tblAirports = ThisWorkbook.Worksheets("Airports").ListObjects("Airports")
    If tblAirports.DataBodyRange Is Nothing Then Exit Function
    If Not ListColumnExists(tblAirports, "ICAO") Then Exit Function
    If Not ListColumnExists(tblAirports, "Airport") Then Exit Function
    If Not ListColumnExists(tblAirports, "Latitude") Then Exit Function
    If Not ListColumnExists(tblAirports, "Longitude") Then Exit Function
    If Not ListColumnExists(tblAirports, "Visits") Then Exit Function

    icaoCol = tblAirports.ListColumns("ICAO").Index
    airportCol = tblAirports.ListColumns("Airport").Index
    latCol = tblAirports.ListColumns("Latitude").Index
    lonCol = tblAirports.ListColumns("Longitude").Index
    visitsCol = tblAirports.ListColumns("Visits").Index

    airportIcao = UCase$(Trim$(airportIcao))
    For rowIndex = 1 To tblAirports.DataBodyRange.Rows.Count
        rowIcao = UCase$(Trim$(CStr(tblAirports.DataBodyRange.Cells(rowIndex, icaoCol).Value)))
        If rowIcao = airportIcao Then
            targetRow = rowIndex
            Exit For
        End If
    Next rowIndex
    If targetRow = 0 Then Exit Function
    If Not IsNumeric(tblAirports.DataBodyRange.Cells(targetRow, latCol).Value) Then Exit Function
    If Not IsNumeric(tblAirports.DataBodyRange.Cells(targetRow, lonCol).Value) Then Exit Function

    airportName = Trim$(CStr(tblAirports.DataBodyRange.Cells(targetRow, airportCol).Value))
    targetLat = CDbl(tblAirports.DataBodyRange.Cells(targetRow, latCol).Value)
    targetLon = CDbl(tblAirports.DataBodyRange.Cells(targetRow, lonCol).Value)
    nearestDistanceNm = 0

    For rowIndex = 1 To tblAirports.DataBodyRange.Rows.Count
        visits = tblAirports.DataBodyRange.Cells(rowIndex, visitsCol).Value
        If Not IsNumeric(visits) Then GoTo NextRow
        If CDbl(visits) <= 0 Then GoTo NextRow
        If Not IsNumeric(tblAirports.DataBodyRange.Cells(rowIndex, latCol).Value) Then GoTo NextRow
        If Not IsNumeric(tblAirports.DataBodyRange.Cells(rowIndex, lonCol).Value) Then GoTo NextRow

        visitedLat = CDbl(tblAirports.DataBodyRange.Cells(rowIndex, latCol).Value)
        visitedLon = CDbl(tblAirports.DataBodyRange.Cells(rowIndex, lonCol).Value)
        distanceNm = GreatCircleDistanceNm(targetLat, targetLon, visitedLat, visitedLon)

        If Not NearestVisitedAirportDistanceNm Or distanceNm < nearestDistanceNm Then
            nearestDistanceNm = distanceNm
            nearestVisitedIcao = UCase$(Trim$(CStr(tblAirports.DataBodyRange.Cells(rowIndex, icaoCol).Value)))
            nearestVisitedName = Trim$(CStr(tblAirports.DataBodyRange.Cells(rowIndex, airportCol).Value))
            NearestVisitedAirportDistanceNm = True
        End If

NextRow:
    Next rowIndex

CleanExit:
End Function

Private Function AirportLocationByIcao(ByVal airportIcao As String, _
                                       ByRef airportName As String, _
                                       ByRef latitude As Double, _
                                       ByRef longitude As Double) As Boolean
    Dim tblAirports As ListObject
    Dim rowIndex As Long
    Dim icaoCol As Long
    Dim airportCol As Long
    Dim latCol As Long
    Dim lonCol As Long
    Dim rowIcao As String

    On Error GoTo CleanExit
    Set tblAirports = ThisWorkbook.Worksheets("Airports").ListObjects("Airports")
    If tblAirports.DataBodyRange Is Nothing Then Exit Function
    If Not ListColumnExists(tblAirports, "ICAO") Then Exit Function
    If Not ListColumnExists(tblAirports, "Airport") Then Exit Function
    If Not ListColumnExists(tblAirports, "Latitude") Then Exit Function
    If Not ListColumnExists(tblAirports, "Longitude") Then Exit Function

    icaoCol = tblAirports.ListColumns("ICAO").Index
    airportCol = tblAirports.ListColumns("Airport").Index
    latCol = tblAirports.ListColumns("Latitude").Index
    lonCol = tblAirports.ListColumns("Longitude").Index
    airportIcao = UCase$(Trim$(airportIcao))

    For rowIndex = 1 To tblAirports.DataBodyRange.Rows.Count
        rowIcao = UCase$(Trim$(CStr(tblAirports.DataBodyRange.Cells(rowIndex, icaoCol).Value)))
        If rowIcao = airportIcao Then
            If Not IsNumeric(tblAirports.DataBodyRange.Cells(rowIndex, latCol).Value) Then Exit Function
            If Not IsNumeric(tblAirports.DataBodyRange.Cells(rowIndex, lonCol).Value) Then Exit Function

            airportName = Trim$(CStr(tblAirports.DataBodyRange.Cells(rowIndex, airportCol).Value))
            latitude = CDbl(tblAirports.DataBodyRange.Cells(rowIndex, latCol).Value)
            longitude = CDbl(tblAirports.DataBodyRange.Cells(rowIndex, lonCol).Value)
            AirportLocationByIcao = True
            Exit Function
        End If
    Next rowIndex

CleanExit:
End Function

Private Function GreatCircleDistanceNm(ByVal lat1Deg As Double, _
                                       ByVal lon1Deg As Double, _
                                       ByVal lat2Deg As Double, _
                                       ByVal lon2Deg As Double) As Double
    Const EARTH_RADIUS_NM As Double = 3440.065

    Dim lat1 As Double
    Dim lat2 As Double
    Dim dLat As Double
    Dim dLon As Double
    Dim a As Double
    Dim c As Double

    lat1 = DegreesToRadians(lat1Deg)
    lat2 = DegreesToRadians(lat2Deg)
    dLat = DegreesToRadians(lat2Deg - lat1Deg)
    dLon = DegreesToRadians(lon2Deg - lon1Deg)

    a = Sin(dLat / 2) ^ 2 + Cos(lat1) * Cos(lat2) * Sin(dLon / 2) ^ 2
    If a <= 0 Then
        c = 0
    ElseIf a >= 1 Then
        c = 4 * Atn(1)
    Else
        c = 2 * Atn(Sqr(a) / Sqr(1 - a))
    End If

    GreatCircleDistanceNm = EARTH_RADIUS_NM * c
End Function

Private Function DegreesToRadians(ByVal degrees As Double) As Double
    DegreesToRadians = degrees * (Atn(1) / 45)
End Function

Private Function AirportNameSuffix(ByVal airportName As String) As String
    airportName = Trim$(airportName)
    If airportName <> "" Then AirportNameSuffix = " (" & airportName & ")"
End Function

Private Function NewEntryRouteBooleanValue(ByVal value As Variant) As Boolean
    If VarType(value) = vbBoolean Then
        NewEntryRouteBooleanValue = CBool(value)
    ElseIf IsNumeric(value) Then
        NewEntryRouteBooleanValue = (CDbl(value) <> 0)
    Else
        Select Case LCase$(Trim$(CStr(value)))
            Case "true", "yes", "y", "1", "x"
                NewEntryRouteBooleanValue = True
        End Select
    End If
End Function

Private Function SumNewEntryFields(ByVal fieldNames As Variant) As Double
    Dim fieldName As Variant

    For Each fieldName In fieldNames
        SumNewEntryFields = SumNewEntryFields + NewEntryNumericValue(CStr(fieldName))
    Next fieldName
End Function

Private Function NewEntryHasCopilotFlightTime() As Boolean
    NewEntryHasCopilotFlightTime = (NewEntryNumericValue("neCopilotDay") > 0 Or _
                                    NewEntryNumericValue("neCopilotNight") > 0)
End Function

Private Function CountPositiveNewEntryFields(ByVal fieldNames As Variant) As Long
    Dim fieldName As Variant

    For Each fieldName In fieldNames
        If NewEntryNumericValue(CStr(fieldName)) > 0 Then
            CountPositiveNewEntryFields = CountPositiveNewEntryFields + 1
        End If
    Next fieldName
End Function

Private Sub CopyNewEntryFieldsToLogbookRow(ByVal rowRange As Range, _
                                           ByVal tbl As ListObject, _
                                           ByVal flightReviewValue As Boolean, _
                                           ByVal ipcValue As Boolean, _
                                           ByVal opcValue As Boolean)
    Dim i As Long
    Dim customStartColumn As Long
    Dim customFieldNames As Variant
    Dim fieldNames As Variant
    Dim columnNames As Variant

    fieldNames = NewEntryLogbookFieldNames()
    columnNames = NewEntryLogbookColumnNames()
    For i = LBound(fieldNames) To UBound(fieldNames)
        WriteValueToLogbookColumn rowRange, tbl, CStr(columnNames(i)), NewEntryValue(CStr(fieldNames(i)))
    Next i

    customStartColumn = LogbookCustomStartColumn(tbl)
    customFieldNames = NewEntryLogbookCustomFieldNames()
    For i = LBound(customFieldNames) To UBound(customFieldNames)
        WriteValueToLogbookColumnIndex rowRange, customStartColumn + i, NewEntryValue(CStr(customFieldNames(i)))
    Next i

    WriteValueToLogbookColumn rowRange, tbl, "FR", flightReviewValue
    WriteValueToLogbookColumn rowRange, tbl, "IPC", ipcValue
    WriteValueToLogbookColumn rowRange, tbl, "OPC", opcValue
End Sub

Private Function NewEntryLogbookFieldNames() As Variant
    NewEntryLogbookFieldNames = Array( _
        "neType", "neReg", "neFlightID", "nePIC", "neOtherCrew", "neFrom", "neTo", "neVia", "neRemarks", _
        "neSeIcusDay", "neSeIcusNight", "neSeDualDay", "neSeDualNight", _
        "neSeCommandDay", "neSeCommandNight", _
        "neMeIcusDay", "neMeIcusNight", "neMeDualDay", "neMeDualNight", _
        "neMeCommandDay", "neMeCommandNight", _
        "neCopilotDay", "neCopilotNight", "neIfrIf", "neIfrSim", _
        "neLandingsDay", "neLandingsNight", _
        "neILS", "neVOR", "neRNP", "neNDB", "neDgaCdi", "neDgaAzi", "neCircling")
End Function

Private Function NewEntryLogbookColumnNames() As Variant
    NewEntryLogbookColumnNames = Array( _
        "Type", "Reg", "Flight ID", "PIC", "Other Pilot or Crew", "From", "To", "Via", "Remarks", _
        "SeIcusDay", "SeIcusNight", "SeDualDay", "SeDualNight", _
        "SeCommandDay", "SeCommandNight", _
        "MeIcusDay", "MeIcusNight", "MeDualDay", "MeDualNight", _
        "MeCommandDay", "MeCommandNight", _
        "CopilotDay", "CopilotNight", "IfrIf", "IfrSim", _
        "LandingsDay", "LandingsNight", _
        "ILS", "VOR", "RNP", "NDB", "DGA (CDI)", "DGA (Azi)", "Circling")
End Function

Private Function NewEntryLogbookCustomFieldNames() As Variant
    NewEntryLogbookCustomFieldNames = Array("neSI1", "neSI2", "neSI3", "neSI4")
End Function

Private Sub WriteValueToLogbookColumn(ByVal rowRange As Range, _
                                      ByVal tbl As ListObject, _
                                      ByVal columnName As String, _
                                      ByVal value As Variant)
    rowRange.Cells(1, tbl.ListColumns(columnName).Index).Value = value
End Sub

Private Sub WriteValueToLogbookColumnIndex(ByVal rowRange As Range, _
                                           ByVal columnIndex As Long, _
                                           ByVal value As Variant)
    rowRange.Cells(1, columnIndex).Value = value
End Sub

Private Function NewEntryNumericFieldNames() As Variant
    NewEntryNumericFieldNames = Array( _
        "neSI1", "neSI2", "neSI3", "neSI4", _
        "neSeIcusDay", "neSeIcusNight", "neSeDualDay", "neSeDualNight", _
        "neSeCommandDay", "neSeCommandNight", _
        "neMeIcusDay", "neMeIcusNight", "neMeDualDay", "neMeDualNight", _
        "neMeCommandDay", "neMeCommandNight", _
        "neCopilotDay", "neCopilotNight", "neIfrIf", "neIfrSim", _
        "neLandingsDay", "neLandingsNight", _
        "neILS", "neVOR", "neRNP", "neNDB", "neDgaCdi", "neDgaAzi", "neCircling")
End Function

Private Function NewEntryFlightTimeFieldNames() As Variant
    NewEntryFlightTimeFieldNames = Array( _
        "neSeIcusDay", "neSeIcusNight", "neSeDualDay", "neSeDualNight", _
        "neSeCommandDay", "neSeCommandNight", _
        "neMeIcusDay", "neMeIcusNight", "neMeDualDay", "neMeDualNight", _
        "neMeCommandDay", "neMeCommandNight", _
        "neCopilotDay", "neCopilotNight")
End Function

Private Function NewEntryDayFlightTimeFieldNames() As Variant
    NewEntryDayFlightTimeFieldNames = Array( _
        "neSeIcusDay", "neSeDualDay", "neSeCommandDay", _
        "neMeIcusDay", "neMeDualDay", "neMeCommandDay", _
        "neCopilotDay")
End Function

Private Function NewEntryNightFlightTimeFieldNames() As Variant
    NewEntryNightFlightTimeFieldNames = Array( _
        "neSeIcusNight", "neSeDualNight", "neSeCommandNight", _
        "neMeIcusNight", "neMeDualNight", "neMeCommandNight", _
        "neCopilotNight")
End Function

Private Function NewEntrySingleEngineFieldNames() As Variant
    NewEntrySingleEngineFieldNames = Array( _
        "neSeIcusDay", "neSeIcusNight", "neSeDualDay", "neSeDualNight", _
        "neSeCommandDay", "neSeCommandNight")
End Function

Private Function NewEntryMultiEngineFieldNames() As Variant
    NewEntryMultiEngineFieldNames = Array( _
        "neMeIcusDay", "neMeIcusNight", "neMeDualDay", "neMeDualNight", _
        "neMeCommandDay", "neMeCommandNight")
End Function

Private Function NewEntryFlightHourFieldNames() As Variant
    NewEntryFlightHourFieldNames = Array( _
        "neSeIcusDay", "neSeIcusNight", "neSeDualDay", "neSeDualNight", _
        "neSeCommandDay", "neSeCommandNight", _
        "neMeIcusDay", "neMeIcusNight", "neMeDualDay", "neMeDualNight", _
        "neMeCommandDay", "neMeCommandNight", _
        "neCopilotDay", "neCopilotNight", "neIfrSim")
End Function

Private Function NewEntryApproachFieldNames() As Variant
    NewEntryApproachFieldNames = Array( _
        "neILS", "neVOR", "neRNP", "neNDB", "neDgaCdi", "neDgaAzi", "neCircling")
End Function

Private Function NewEntryDateInputFieldNames() As Variant
    NewEntryDateInputFieldNames = Array("neYear", "neMonth", "neDay")
End Function

Private Function NewEntryCurrencyFieldNames() As Variant
    NewEntryCurrencyFieldNames = Array("neFR", "neIPC", "neOPC")
End Function

Private Function NewEntryOtherCrewWarningFieldNames() As Variant
    NewEntryOtherCrewWarningFieldNames = Array( _
        "neOtherCrew", _
        "neSeIcusDay", "neSeIcusNight", _
        "neSeDualDay", "neSeDualNight", _
        "neMeIcusDay", "neMeIcusNight", _
        "neMeDualDay", "neMeDualNight", _
        "neCopilotDay", "neCopilotNight")
End Function

Private Function NewEntrySingleEngineColumnNames() As Variant
    NewEntrySingleEngineColumnNames = Array( _
        "SeIcusDay", "SeIcusNight", "SeDualDay", "SeDualNight", _
        "SeCommandDay", "SeCommandNight")
End Function

Private Function NewEntryMultiEngineColumnNames() As Variant
    NewEntryMultiEngineColumnNames = Array( _
        "MeIcusDay", "MeIcusNight", "MeDualDay", "MeDualNight", _
        "MeCommandDay", "MeCommandNight")
End Function

Private Function NewEntryClearFieldNames() As Variant
    NewEntryClearFieldNames = Array( _
        "neType", "neReg", "neFlightID", "neOtherCrew", "neVia", "neRemarks", _
        "neFR", "neIPC", "neOPC", _
        "neSI1", "neSI2", "neSI3", "neSI4", _
        "neSeIcusDay", "neSeIcusNight", "neSeDualDay", "neSeDualNight", _
        "neSeCommandDay", "neSeCommandNight", _
        "neMeIcusDay", "neMeIcusNight", "neMeDualDay", "neMeDualNight", _
        "neMeCommandDay", "neMeCommandNight", _
        "neCopilotDay", "neCopilotNight", "neIfrIf", "neIfrSim", _
        "neLandingsDay", "neLandingsNight", _
        "neILS", "neVOR", "neRNP", "neNDB", "neDgaCdi", "neDgaAzi", "neCircling")
End Function

' ==============================================================
' LOGTEN IMPORT
' ==============================================================

Public Sub ImportFromLogTen()
    Dim filePath As Variant
    Dim importResult As Object

    filePath = Application.GetOpenFilename( _
        "LogTen exports (*.txt;*.tsv;*.csv),*.txt;*.tsv;*.csv,All files (*.*),*.*", _
        , "Select LogTen Export")
    If VarType(filePath) = vbBoolean Then Exit Sub

    Set importResult = ImportFromLogTenFile(CStr(filePath))
    If CBool(importResult("Completed")) Then
        MsgBox CStr(importResult("Message")), vbInformation, "LogTen Import Complete"
    Else
        MsgBox CStr(importResult("Message")), vbExclamation, "LogTen Import Stopped"
    End If
End Sub

Public Function ImportFromLogTenFile(ByVal filePath As String) As Object
    Dim previousScreenUpdating As Boolean
    Dim previousEnableEvents As Boolean
    Dim previousCalculation As XlCalculation
    Dim previousDisplayStatusBar As Boolean
    Dim previousStatusBar As Variant
    Dim result As Object
    Dim records As Collection
    Dim headers As Object
    Dim mappedRows As Collection
    Dim aircraftTypes As Object
    Dim unknownTypes As Object
    Dim ignoredApproaches As Object
    Dim errors As Collection
    Dim blanks As Long
    Dim oldFormatDetected As Boolean
    Dim wsLog As Worksheet
    Dim tbl As ListObject
    Dim tableStyleName As String
    Dim totalsWereOn As Boolean
    Dim totalsStateCaptured As Boolean
    Dim logbookWasProtected As Boolean
    Dim rowItem As Object
    Dim imported As Long
    Dim duplicates As Long
    Dim simRows As Long
    Dim rowIndex As Long
    Dim diagStep As String
    Dim existingKeys As Object
    Dim rowsToImport As Collection

    Set result = CreateObject("Scripting.Dictionary")
    result.Add "Completed", False
    result.Add "Message", ""

    On Error GoTo Fail

    diagStep = "reading LogTen export"
    Set records = ReadLogTenRecords(filePath, headers, blanks, oldFormatDetected)
    If oldFormatDetected Then
        result("Message") = "This looks like LogTen's default full export. Use the dynamic export format instead."
        Set ImportFromLogTenFile = result
        Exit Function
    End If

    If records.Count = 0 Then
        result("Message") = "No importable LogTen rows were found."
        Set ImportFromLogTenFile = result
        Exit Function
    End If

    diagStep = "loading aircraft type table"
    Set aircraftTypes = LoadAircraftTypeClasses()
    Set unknownTypes = CreateObject("Scripting.Dictionary")
    Set ignoredApproaches = CreateObject("Scripting.Dictionary")
    Set errors = New Collection
    Set mappedRows = New Collection

    For rowIndex = 1 To records.Count
        diagStep = "mapping row " & CStr(rowIndex + 1)
        Set rowItem = MapLogTenRecord(records(rowIndex), rowIndex + 1, aircraftTypes, unknownTypes, ignoredApproaches, errors)
        If Not rowItem Is Nothing Then mappedRows.Add rowItem
    Next rowIndex

    If unknownTypes.Count > 0 Or errors.Count > 0 Then
        WriteLogTenImportReport mappedRows, errors, unknownTypes, ignoredApproaches, 0, 0, blanks, True
        result("Message") = "The import was not written because validation found issues." & vbCrLf & vbCrLf & _
                            "Unknown aircraft types: " & JoinDictionaryKeys(unknownTypes, ", ") & vbCrLf & _
                            "Review the '" & LOGTEN_REPORT_SHEET & "' sheet for details."
        Set ImportFromLogTenFile = result
        Exit Function
    End If

    previousScreenUpdating = Application.ScreenUpdating
    previousEnableEvents = Application.EnableEvents
    previousCalculation = Application.Calculation
    previousDisplayStatusBar = Application.DisplayStatusBar
    previousStatusBar = Application.StatusBar

    Application.ScreenUpdating = False
    Application.EnableEvents = False
    Application.Calculation = xlCalculationManual
    Application.DisplayStatusBar = True
    Application.StatusBar = "Electronic Logbook: importing LogTen export"

    diagStep = "opening Logbook table"
    Set wsLog = ThisWorkbook.Sheets("Logbook")
    Set tbl = wsLog.ListObjects("Logbook")
    logbookWasProtected = wsLog.ProtectContents
    If logbookWasProtected Then wsLog.Unprotect Password:=ProtectionPassword()

    tableStyleName = tbl.TableStyle.Name
    totalsWereOn = tbl.ShowTotals
    totalsStateCaptured = True
    If totalsWereOn Then tbl.ShowTotals = False

    diagStep = "checking duplicates"
    Set existingKeys = BuildExistingLogTenDuplicateKeys(tbl)
    Set rowsToImport = New Collection
    For Each rowItem In mappedRows
        If existingKeys.Exists(CStr(rowItem("DuplicateKey"))) Then
            duplicates = duplicates + 1
            rowItem("Status") = "Duplicate"
        Else
            existingKeys.Add CStr(rowItem("DuplicateKey")), True
            rowsToImport.Add rowItem
            If CDbl(rowItem("IfrSim")) > 0 And CStr(rowItem("Type")) = "SIM" Then simRows = simRows + 1
            rowItem("Status") = "Imported"
        End If
    Next rowItem

    diagStep = "writing imported rows"
    imported = rowsToImport.Count
    If imported > 0 Then AppendMappedLogTenRows tbl, rowsToImport

    tbl.TableStyle = tableStyleName
    tbl.ShowTableStyleRowStripes = True
    tbl.ShowTableStyleColumnStripes = False
    tbl.ShowTotals = totalsWereOn
    totalsStateCaptured = False

    If imported > 0 Then
        diagStep = "normalising Logbook formatting"
        NormaliseLogbookFormatting tbl
        RefreshDateCalculationFormulas tbl
        tbl.ListColumns("Date").DataBodyRange.Calculate
        SortLogbookByDate tbl
        UpdateHiddenRows ThisWorkbook
        MarkRoutesDirty ThisWorkbook
        RefreshAirportVisitStatsWithWorkbookProtection ThisWorkbook, False
        ThisWorkbook.Save
    End If

    diagStep = "writing import report"
    WriteLogTenImportReport mappedRows, errors, unknownTypes, ignoredApproaches, imported, duplicates, blanks, False

    If logbookWasProtected Then ProtectLogbookSheetForRuntime wsLog
    RestoreImportApplicationState previousScreenUpdating, previousEnableEvents, previousCalculation, _
                                  previousDisplayStatusBar, previousStatusBar

    result("Completed") = True
    result("Message") = "Imported " & imported & " row(s)." & vbCrLf & _
                        "Skipped duplicates: " & duplicates & vbCrLf & _
                        "Blank rows ignored: " & blanks & vbCrLf & _
                        "Simulator rows imported: " & simRows & vbCrLf & _
                        "Ignored approach labels: " & JoinDictionaryKeys(ignoredApproaches, ", ")
    Set ImportFromLogTenFile = result
    Exit Function

Fail:
    Dim errNum As Long
    Dim errDesc As String
    errNum = Err.Number
    errDesc = Err.Description
    On Error Resume Next
    If totalsStateCaptured Then tbl.ShowTotals = totalsWereOn
    If logbookWasProtected Then ProtectLogbookSheetForRuntime wsLog
    RestoreImportApplicationState previousScreenUpdating, previousEnableEvents, previousCalculation, _
                                  previousDisplayStatusBar, previousStatusBar
    result("Message") = BuildUserFacingErrorMessage( _
                        "The LogTen import could not be completed.", _
                        "No completed import was applied. Check the selected export file and try again. If this keeps happening, use the Report a Bug button and include the debug log.", _
                        errNum, "ImportFromLogTenFile", errDesc, diagStep)
    Set ImportFromLogTenFile = result
End Function

Public Sub ImportAircraftTypesFromCsv()
    Dim filePath As Variant
    Dim records As Collection
    Dim headers As Object
    Dim blanks As Long
    Dim oldFormatDetected As Boolean
    Dim tbl As ListObject
    Dim rowRecord As Object
    Dim designator As String
    Dim descriptionCode As String
    Dim imported As Long

    filePath = Application.GetOpenFilename( _
        "Aircraft type CSV (*.csv;*.txt;*.tsv),*.csv;*.txt;*.tsv,All files (*.*),*.*", _
        , "Select Aircraft Type Designator CSV")
    If VarType(filePath) = vbBoolean Then Exit Sub

    On Error GoTo Fail
    Set records = ReadDelimitedRecords(CStr(filePath), headers, blanks, oldFormatDetected)
    Set tbl = EnsureAircraftTypesTable()

    If Not tbl.DataBodyRange Is Nothing Then tbl.DataBodyRange.Delete

    For Each rowRecord In records
        designator = FirstPresentField(rowRecord, Array("Designator", "Type Designator", "Aircraft Type", "AircraftType", "TYPE DESIGNATOR"))
        descriptionCode = FirstPresentField(rowRecord, Array("DescriptionCode", "Description Code", "Description", "Aircraft Description", "DESCRIPTION"))
        designator = UCase$(Trim$(designator))
        descriptionCode = UCase$(Trim$(descriptionCode))

        If designator <> "" And Len(descriptionCode) >= 2 Then
            AddAircraftTypeRow tbl, designator, descriptionCode, "Imported"
            imported = imported + 1
        End If
    Next rowRecord

    If imported = 0 Then SeedAircraftTypes tbl
    MsgBox "Imported " & imported & " aircraft type row(s).", vbInformation, "Aircraft Types Imported"
    Exit Sub

Fail:
    MsgBox BuildUserFacingErrorMessage( _
           "Aircraft type import could not be completed.", _
           "Check that the selected CSV has aircraft type/designator columns, then try again.", _
           Err.Number, Err.Source, Err.Description, "Importing aircraft types from CSV"), _
           vbExclamation, "Aircraft Types Import"
End Sub

Private Sub RestoreImportApplicationState(ByVal screenUpdating As Boolean, _
                                          ByVal enableEvents As Boolean, _
                                          ByVal calculationMode As XlCalculation, _
                                          ByVal displayStatusBar As Boolean, _
                                          ByVal statusBarValue As Variant)
    Application.ScreenUpdating = screenUpdating
    Application.EnableEvents = enableEvents
    Application.Calculation = calculationMode
    Application.DisplayStatusBar = displayStatusBar
    If displayStatusBar Then
        If VarType(statusBarValue) = vbString Then
            Application.StatusBar = CStr(statusBarValue)
        Else
            Application.StatusBar = False
        End If
    Else
        Application.StatusBar = False
    End If
    Application.CutCopyMode = False
End Sub

Private Function ReadLogTenRecords(ByVal filePath As String, _
                                   ByRef headers As Object, _
                                   ByRef blankRows As Long, _
                                   ByRef oldFormatDetected As Boolean) As Collection
    Dim records As Collection
    Set records = ReadDelimitedRecords(filePath, headers, blankRows, oldFormatDetected)

    If headers.Exists("flight_flightdate") Then oldFormatDetected = True
    If Not oldFormatDetected Then ValidateLogTenHeaders headers
    Set ReadLogTenRecords = records
End Function

Private Function ReadDelimitedRecords(ByVal filePath As String, _
                                      ByRef headers As Object, _
                                      ByRef blankRows As Long, _
                                      ByRef oldFormatDetected As Boolean) As Collection
    Dim fso As Object
    Dim stream As Object
    Dim content As String
    Dim lines As Variant
    Dim headerFields As Variant
    Dim fields As Variant
    Dim delimiter As String
    Dim lineIndex As Long
    Dim colIndex As Long
    Dim rowRecord As Object
    Dim headerName As String
    Dim records As Collection

    Set records = New Collection
    Set headers = CreateObject("Scripting.Dictionary")
    headers.CompareMode = vbTextCompare

    Set fso = CreateObject("Scripting.FileSystemObject")
    If Not fso.FileExists(filePath) Then Err.Raise 53, "ReadDelimitedRecords", "File not found: " & filePath

    Set stream = CreateObject("ADODB.Stream")
    stream.Type = 2
    stream.Charset = "utf-8"
    stream.Open
    stream.LoadFromFile filePath
    content = stream.ReadText
    stream.Close
    content = Replace(content, ChrW$(&HFEFF), "")

    content = Replace(content, vbCrLf, vbLf)
    content = Replace(content, vbCr, vbLf)
    lines = Split(content, vbLf)
    If UBound(lines) < 0 Then
        Set ReadDelimitedRecords = records
        Exit Function
    End If

    delimiter = DetectDelimiter(CStr(lines(0)))
    headerFields = ParseDelimitedLine(CStr(lines(0)), delimiter)
    For colIndex = LBound(headerFields) To UBound(headerFields)
        headerName = Trim$(CStr(headerFields(colIndex)))
        If headerName <> "" Then headers(LCase$(headerName)) = colIndex
    Next colIndex

    If UBound(headerFields) > 100 Then oldFormatDetected = True

    For lineIndex = 1 To UBound(lines)
        If Trim$(CStr(lines(lineIndex))) = "" Then
            blankRows = blankRows + 1
        Else
            fields = ParseDelimitedLine(CStr(lines(lineIndex)), delimiter)
            Set rowRecord = CreateObject("Scripting.Dictionary")
            rowRecord.CompareMode = vbTextCompare
            rowRecord.Add "__RowNumber", lineIndex + 1
            For colIndex = LBound(headerFields) To UBound(headerFields)
                headerName = Trim$(CStr(headerFields(colIndex)))
                If headerName <> "" Then
                    If colIndex <= UBound(fields) Then
                        rowRecord(headerName) = Trim$(CStr(fields(colIndex)))
                    Else
                        rowRecord(headerName) = ""
                    End If
                End If
            Next colIndex
            If LogTenRecordHasAnyValue(rowRecord) Then
                records.Add rowRecord
            Else
                blankRows = blankRows + 1
            End If
        End If
    Next lineIndex

    Set ReadDelimitedRecords = records
End Function

Private Function DetectDelimiter(ByVal headerLine As String) As String
    If CountOccurrences(headerLine, vbTab) >= CountOccurrences(headerLine, ",") Then
        DetectDelimiter = vbTab
    Else
        DetectDelimiter = ","
    End If
End Function

Private Function CountOccurrences(ByVal value As String, ByVal token As String) As Long
    CountOccurrences = (Len(value) - Len(Replace(value, token, ""))) / Len(token)
End Function

Private Function ParseDelimitedLine(ByVal lineText As String, ByVal delimiter As String) As Variant
    Dim values As Collection
    Dim valueText As String
    Dim i As Long
    Dim ch As String
    Dim inQuotes As Boolean
    Dim arr() As String

    Set values = New Collection
    For i = 1 To Len(lineText)
        ch = Mid$(lineText, i, 1)
        If ch = """" Then
            If inQuotes And i < Len(lineText) And Mid$(lineText, i + 1, 1) = """" Then
                valueText = valueText & """"
                i = i + 1
            Else
                inQuotes = Not inQuotes
            End If
        ElseIf ch = delimiter And Not inQuotes Then
            values.Add valueText
            valueText = ""
        Else
            valueText = valueText & ch
        End If
    Next i
    values.Add valueText

    ReDim arr(0 To values.Count - 1)
    For i = 1 To values.Count
        arr(i - 1) = CStr(values(i))
    Next i
    ParseDelimitedLine = arr
End Function

Private Sub ValidateLogTenHeaders(ByVal headers As Object)
    Dim requiredHeaders As Variant
    Dim headerName As Variant
    Dim missing As String

    requiredHeaders = Array("Date", "Aircraft ID", "Aircraft Type", "From", "To", _
                            "Total Time", "Simulator", "PIC/P1 Crew", "Day Ldg", _
                            "Night Ldg", "Approach 1", "Approach 2")
    For Each headerName In requiredHeaders
        If Not headers.Exists(LCase$(CStr(headerName))) Then
            missing = AppendListItem(missing, CStr(headerName), ", ")
        End If
    Next headerName

    If missing <> "" Then Err.Raise 5, "ValidateLogTenHeaders", "Missing required LogTen headers: " & missing
End Sub

Private Function LogTenRecordHasAnyValue(ByVal rowRecord As Object) As Boolean
    Dim key As Variant
    For Each key In rowRecord.Keys
        If Left$(CStr(key), 2) <> "__" Then
            If Trim$(CStr(rowRecord(key))) <> "" Then
                LogTenRecordHasAnyValue = True
                Exit Function
            End If
        End If
    Next key
End Function

Private Function MapLogTenRecord(ByVal sourceRow As Object, _
                                 ByVal rowNumber As Long, _
                                 ByVal aircraftTypes As Object, _
                                 ByVal unknownTypes As Object, _
                                 ByVal ignoredApproaches As Object, _
                                 ByVal errors As Collection) As Object
    Dim mapped As Object
    Dim aircraftType As String
    Dim aircraftClass As String
    Dim isSimulatorOnly As Boolean
    Dim entryDate As Date
    Dim totalHours As Double
    Dim nightHours As Double
    Dim picHours As Double
    Dim p1usHours As Double
    Dim sicHours As Double
    Dim simulatorHours As Double
    Dim accountedHours As Double
    Dim remainderHours As Double
    Dim commandHours As Double
    Dim icusHours As Double
    Dim copilotHours As Double
    Dim dualHours As Double
    Dim rowText As String

    Set mapped = CreateObject("Scripting.Dictionary")
    mapped.CompareMode = vbTextCompare

    If Not IsDate(FieldValue(sourceRow, "Date")) Then
        errors.Add "Row " & rowNumber & ": invalid or missing Date."
        Exit Function
    End If

    entryDate = CDate(FieldValue(sourceRow, "Date"))
    aircraftType = UCase$(Trim$(FieldValue(sourceRow, "Aircraft Type")))
    totalHours = ParseLogTenHours(FieldValue(sourceRow, "Total Time"))
    nightHours = ParseLogTenHours(FieldValue(sourceRow, "Night"))
    picHours = ParseLogTenHours(FieldValue(sourceRow, "PIC"))
    p1usHours = ParseLogTenHours(FieldValue(sourceRow, "P1u/s"))
    sicHours = ParseLogTenHours(FieldValue(sourceRow, "SIC"))
    simulatorHours = ParseLogTenHours(FieldValue(sourceRow, "Simulator"))
    isSimulatorOnly = (simulatorHours > 0 And aircraftType = "" And totalHours = 0)

    If totalHours = 0 And simulatorHours = 0 Then Exit Function

    If isSimulatorOnly Then
        aircraftType = "SIM"
        aircraftClass = "SIM"
    Else
        If Not aircraftTypes.Exists(aircraftType) Then
            unknownTypes(aircraftType) = True
            Exit Function
        End If
        aircraftClass = CStr(aircraftTypes(aircraftType))
    End If

    accountedHours = picHours + p1usHours + sicHours
    If totalHours > 0 And accountedHours > totalHours + 0.0001 Then
        errors.Add "Row " & rowNumber & ": PIC/P1u/s/SIC hours exceed Total Time."
        Exit Function
    End If

    commandHours = picHours
    icusHours = p1usHours
    copilotHours = sicHours
    remainderHours = totalHours - accountedHours
    If remainderHours < 0 Then remainderHours = 0

    rowText = JoinLogTenRowValues(sourceRow)
    If remainderHours > 0 Then
        If InStr(1, rowText, "ICUS", vbTextCompare) > 0 Then
            icusHours = icusHours + remainderHours
        Else
            dualHours = dualHours + remainderHours
        End If
    End If

    AddMappedBaseFields mapped, sourceRow, entryDate, aircraftType, rowNumber
    AllocateMappedHours mapped, aircraftClass, commandHours, icusHours, copilotHours, dualHours, nightHours

    mapped("IfrIf") = ParseLogTenHours(FieldValue(sourceRow, "Actual Inst"))
    mapped("IfrSim") = simulatorHours
    mapped("LandingsDay") = ParseLogTenNumber(FieldValue(sourceRow, "Day Ldg"))
    mapped("LandingsNight") = ParseLogTenNumber(FieldValue(sourceRow, "Night Ldg"))
    ApplyLogTenApproaches mapped, FieldValue(sourceRow, "Approach 1"), ignoredApproaches
    ApplyLogTenApproaches mapped, FieldValue(sourceRow, "Approach 2"), ignoredApproaches
    mapped("DuplicateKey") = BuildLogTenDuplicateKey(mapped)
    Set MapLogTenRecord = mapped
End Function

Private Sub AddMappedBaseFields(ByVal mapped As Object, _
                                ByVal sourceRow As Object, _
                                ByVal entryDate As Date, _
                                ByVal aircraftType As String, _
                                ByVal rowNumber As Long)
    mapped("SourceRow") = rowNumber
    mapped("Date") = entryDate
    mapped("Year") = Year(entryDate)
    mapped("Month") = Format$(entryDate, "mmm")
    mapped("Day") = Day(entryDate)
    mapped("Type") = aircraftType
    mapped("Reg") = UCase$(Trim$(FieldValue(sourceRow, "Aircraft ID")))
    mapped("Flight ID") = Trim$(FieldValue(sourceRow, "Flight #"))
    mapped("PIC") = FirstPresentField(sourceRow, Array("PIC/P1 Crew", "PIC"))
    mapped("Other Pilot or Crew") = JoinNonBlank(Array(FieldValue(sourceRow, "SIC/P2 Crew"), FieldValue(sourceRow, "Observer")), ", ")
    mapped("From") = UCase$(Trim$(FieldValue(sourceRow, "From")))
    mapped("To") = UCase$(Trim$(FieldValue(sourceRow, "To")))
    mapped("Via") = Trim$(FieldValue(sourceRow, "Route"))
    mapped("Remarks") = BuildLogTenRemarks(sourceRow)
    mapped("Details") = BuildLogTenDetails(sourceRow)

    InitialiseMappedNumericFields mapped
End Sub

Private Sub InitialiseMappedNumericFields(ByVal mapped As Object)
    Dim columnName As Variant

    For Each columnName In Array("SeIcusDay", "SeIcusNight", "SeDualDay", "SeDualNight", _
                                 "SeCommandDay", "SeCommandNight", "MeIcusDay", "MeIcusNight", _
                                 "MeDualDay", "MeDualNight", "MeCommandDay", "MeCommandNight", _
                                 "CopilotDay", "CopilotNight", "IfrIf", "IfrSim", _
                                 "LandingsDay", "LandingsNight", "ILS", "VOR", "RNP", _
                                 "NDB", "DGA (CDI)", "DGA (Azi)", "Circling")
        mapped(CStr(columnName)) = 0#
    Next columnName
End Sub

Private Sub AllocateMappedHours(ByVal mapped As Object, _
                                ByVal aircraftClass As String, _
                                ByVal commandHours As Double, _
                                ByVal icusHours As Double, _
                                ByVal copilotHours As Double, _
                                ByVal dualHours As Double, _
                                ByVal nightHours As Double)
    Dim commandNight As Double
    Dim icusNight As Double
    Dim copilotNight As Double
    Dim dualNight As Double
    Dim remainingNight As Double
    Dim prefix As String

    remainingNight = nightHours
    commandNight = AllocateNightHours(commandHours, remainingNight)
    icusNight = AllocateNightHours(icusHours, remainingNight)
    copilotNight = AllocateNightHours(copilotHours, remainingNight)
    dualNight = AllocateNightHours(dualHours, remainingNight)

    If aircraftClass = "SIM" Then Exit Sub
    If aircraftClass = "ME" Then
        prefix = "Me"
    Else
        prefix = "Se"
    End If

    mapped(prefix & "CommandDay") = RoundLogTenHours(commandHours - commandNight)
    mapped(prefix & "CommandNight") = RoundLogTenHours(commandNight)
    mapped(prefix & "IcusDay") = RoundLogTenHours(icusHours - icusNight)
    mapped(prefix & "IcusNight") = RoundLogTenHours(icusNight)
    mapped(prefix & "DualDay") = RoundLogTenHours(dualHours - dualNight)
    mapped(prefix & "DualNight") = RoundLogTenHours(dualNight)
    mapped("CopilotDay") = RoundLogTenHours(copilotHours - copilotNight)
    mapped("CopilotNight") = RoundLogTenHours(copilotNight)
End Sub

Private Function AllocateNightHours(ByVal bucketHours As Double, ByRef remainingNight As Double) As Double
    If remainingNight <= 0 Or bucketHours <= 0 Then Exit Function

    If bucketHours <= remainingNight Then
        AllocateNightHours = bucketHours
        remainingNight = remainingNight - bucketHours
    Else
        AllocateNightHours = remainingNight
        remainingNight = 0
    End If
End Function

Private Function BuildLogTenDetails(ByVal sourceRow As Object) As String
    Dim routeText As String
    Dim details As String
    Dim FlightID As String

    FlightID = Trim$(FieldValue(sourceRow, "Flight #"))
    routeText = BuildLogTenRouteText(sourceRow)

    If FlightID <> "" Then details = FlightID
    If routeText <> "" Then details = AppendListItem(details, routeText, " ")
    details = AppendListItem(details, FieldValue(sourceRow, "Remarks"), " | ")
    details = AppendListItem(details, FieldValue(sourceRow, "IPC/ICC"), " | ")
    details = AppendListItem(details, FieldValue(sourceRow, "Flight Review"), " | ")

    If details = "" Then details = "LogTen import"
    BuildLogTenDetails = details
End Function

Private Function BuildLogTenRemarks(ByVal sourceRow As Object) As String
    Dim remarks As String

    remarks = AppendListItem(remarks, FieldValue(sourceRow, "Remarks"), " | ")
    remarks = AppendListItem(remarks, FieldValue(sourceRow, "IPC/ICC"), " | ")
    remarks = AppendListItem(remarks, FieldValue(sourceRow, "Flight Review"), " | ")

    If remarks = "" Then remarks = "LogTen import"
    BuildLogTenRemarks = remarks
End Function

Private Function BuildLogTenRouteText(ByVal sourceRow As Object) As String
    Dim dep As String
    Dim arr As String
    Dim route As String

    dep = UCase$(Trim$(FieldValue(sourceRow, "From")))
    arr = UCase$(Trim$(FieldValue(sourceRow, "To")))
    route = Trim$(FieldValue(sourceRow, "Route"))

    If dep = "" And arr = "" Then Exit Function
    If route <> "" Then
        BuildLogTenRouteText = dep & "-" & route & "-" & arr
    Else
        BuildLogTenRouteText = dep & "-" & arr
    End If
End Function

Private Sub ApplyLogTenApproaches(ByVal mapped As Object, _
                                  ByVal approachText As String, _
                                  ByVal ignoredApproaches As Object)
    Dim parts As Variant
    Dim approachCount As Double
    Dim approachType As String

    approachText = Trim$(approachText)
    If approachText = "" Then Exit Sub

    parts = Split(approachText, ";")
    If UBound(parts) < 1 Then Exit Sub

    approachCount = ParseLogTenNumber(CStr(parts(0)))
    If approachCount = 0 Then approachCount = 1
    approachType = UCase$(Trim$(CStr(parts(1))))

    Select Case approachType
        Case "ILS"
            mapped("ILS") = CDbl(mapped("ILS")) + approachCount
        Case "RNP", "LNAV/VNAV"
            mapped("RNP") = CDbl(mapped("RNP")) + approachCount
        Case "GLS"
            mapped("ILS") = CDbl(mapped("ILS")) + approachCount
        Case "VOR"
            mapped("VOR") = CDbl(mapped("VOR")) + approachCount
        Case "RNP"
            mapped("RNP") = CDbl(mapped("RNP")) + approachCount
        Case "NDB"
            mapped("NDB") = CDbl(mapped("NDB")) + approachCount
        Case "VISUAL"
            ignoredApproaches("Visual") = True
        Case Else
            If approachType <> "" Then ignoredApproaches(approachType) = True
    End Select
End Sub

Private Sub AppendMappedLogTenRows(ByVal tbl As ListObject, ByVal rowsToImport As Collection)
    Dim originalRowCount As Long
    Dim targetRange As Range
    Dim importIndex As Long
    Dim targetRow As Range
    Dim mapped As Object
    Dim formulaCol As Long
    Dim formulaSource As Range
    Dim formulaTarget As Range
    Dim insertAtRow As Long

    If rowsToImport.Count = 0 Then Exit Sub
    If tbl.DataBodyRange Is Nothing Then Err.Raise 5, "AppendMappedLogTenRows", "Logbook table has no template row."

    originalRowCount = tbl.ListRows.Count
    insertAtRow = tbl.DataBodyRange.Row + originalRowCount
    tbl.Parent.Rows(insertAtRow & ":" & insertAtRow + rowsToImport.Count - 1).Insert Shift:=xlDown
    Set targetRange = tbl.Range.Resize(tbl.Range.Rows.Count + rowsToImport.Count, tbl.Range.Columns.Count)
    tbl.Resize targetRange

    For formulaCol = 1 To tbl.ListColumns.Count
        If tbl.DataBodyRange.Cells(originalRowCount, formulaCol).HasFormula Then
            Set formulaSource = tbl.DataBodyRange.Cells(originalRowCount, formulaCol)
            Set formulaTarget = tbl.DataBodyRange.Cells(originalRowCount, formulaCol).Resize(rowsToImport.Count + 1, 1)
            formulaSource.AutoFill Destination:=formulaTarget
        End If
    Next formulaCol
    RefreshLogbookCalculatedFormulas tbl

    For importIndex = 1 To rowsToImport.Count
        Set mapped = rowsToImport(importIndex)
        Set targetRow = tbl.DataBodyRange.Rows(originalRowCount + importIndex)
        WriteMappedLogTenRow targetRow, tbl, mapped
    Next importIndex
End Sub

Private Sub WriteMappedLogTenRow(ByVal targetRow As Range, ByVal tbl As ListObject, ByVal mapped As Object)
    Dim columnName As Variant
    Dim remarksColumn As String

    WriteMappedValueToColumn targetRow, tbl, "Year", mapped("Year")
    WriteMappedValueToColumn targetRow, tbl, "Month", mapped("Month")
    WriteMappedValueToColumn targetRow, tbl, "Day", mapped("Day")
    WriteMappedValueToColumn targetRow, tbl, "Type", mapped("Type")
    WriteMappedValueToColumn targetRow, tbl, "Reg", mapped("Reg")
    WriteMappedValueToColumnIfPresent targetRow, tbl, "Flight ID", mapped("Flight ID")
    WriteMappedValueToColumn targetRow, tbl, "PIC", mapped("PIC")
    WriteMappedValueToColumn targetRow, tbl, "Other Pilot or Crew", mapped("Other Pilot or Crew")
    WriteMappedValueToColumnIfPresent targetRow, tbl, "From", mapped("From")
    WriteMappedValueToColumnIfPresent targetRow, tbl, "To", mapped("To")
    WriteMappedValueToColumnIfPresent targetRow, tbl, "Via", mapped("Via")
    remarksColumn = LogbookRemarksColumnName(tbl)
    If remarksColumn = "Remarks" Then
        WriteMappedValueToColumn targetRow, tbl, remarksColumn, mapped("Remarks")
    ElseIf remarksColumn = "Details" Then
        WriteMappedValueToColumn targetRow, tbl, remarksColumn, mapped("Details")
    End If

    For Each columnName In Array("SeIcusDay", "SeIcusNight", "SeDualDay", "SeDualNight", _
                                 "SeCommandDay", "SeCommandNight", "MeIcusDay", "MeIcusNight", _
                                 "MeDualDay", "MeDualNight", "MeCommandDay", "MeCommandNight", _
                                 "CopilotDay", "CopilotNight", "IfrIf", "IfrSim", "LandingsDay", _
                                 "LandingsNight", "ILS", "VOR", "RNP", "NDB", "DGA (CDI)", _
                                 "DGA (Azi)", "Circling")
        If CDbl(mapped(CStr(columnName))) <> 0 Then
            WriteMappedValueToColumn targetRow, tbl, CStr(columnName), CDbl(mapped(CStr(columnName)))
        Else
            WriteMappedValueToColumn targetRow, tbl, CStr(columnName), vbNullString
        End If
    Next columnName
End Sub

Private Sub AppendMappedLogTenRow(ByVal tbl As ListObject, ByVal mapped As Object)
    Dim newRow As ListRow
    Dim templateRow As Range
    Dim iPrevRow As Long
    Dim iCol As Long
    Dim columnName As Variant
    Dim fmtCol As Long
    Dim remarksColumn As String

    Set templateRow = tbl.DataBodyRange.Rows(1)
    Set newRow = tbl.ListRows.Add(AlwaysInsert:=True)

    If tbl.ListRows.Count > 1 Then
        iPrevRow = tbl.ListRows.Count - 1
        For iCol = 1 To tbl.ListColumns.Count
            If tbl.DataBodyRange.Cells(iPrevRow, iCol).HasFormula Then
                tbl.DataBodyRange.Cells(iPrevRow, iCol).Resize(2, 1).FillDown
            End If
        Next iCol
    End If
    RefreshLogbookCalculatedFormulas tbl

    WriteMappedValueToColumn newRow.Range, tbl, "Year", mapped("Year")
    WriteMappedValueToColumn newRow.Range, tbl, "Month", mapped("Month")
    WriteMappedValueToColumn newRow.Range, tbl, "Day", mapped("Day")
    WriteMappedValueToColumn newRow.Range, tbl, "Type", mapped("Type")
    WriteMappedValueToColumn newRow.Range, tbl, "Reg", mapped("Reg")
    WriteMappedValueToColumnIfPresent newRow.Range, tbl, "Flight ID", mapped("Flight ID")
    WriteMappedValueToColumn newRow.Range, tbl, "PIC", mapped("PIC")
    WriteMappedValueToColumn newRow.Range, tbl, "Other Pilot or Crew", mapped("Other Pilot or Crew")
    WriteMappedValueToColumnIfPresent newRow.Range, tbl, "From", mapped("From")
    WriteMappedValueToColumnIfPresent newRow.Range, tbl, "To", mapped("To")
    WriteMappedValueToColumnIfPresent newRow.Range, tbl, "Via", mapped("Via")
    remarksColumn = LogbookRemarksColumnName(tbl)
    If remarksColumn = "Remarks" Then
        WriteMappedValueToColumn newRow.Range, tbl, remarksColumn, mapped("Remarks")
    ElseIf remarksColumn = "Details" Then
        WriteMappedValueToColumn newRow.Range, tbl, remarksColumn, mapped("Details")
    End If

    For Each columnName In Array("SeIcusDay", "SeIcusNight", "SeDualDay", "SeDualNight", _
                                 "SeCommandDay", "SeCommandNight", "MeIcusDay", "MeIcusNight", _
                                 "MeDualDay", "MeDualNight", "MeCommandDay", "MeCommandNight", _
                                 "CopilotDay", "CopilotNight", "IfrIf", "IfrSim", "LandingsDay", _
                                 "LandingsNight", "ILS", "VOR", "RNP", "NDB", "DGA (CDI)", _
                                 "DGA (Azi)", "Circling")
        If CDbl(mapped(CStr(columnName))) <> 0 Then
            WriteMappedValueToColumn newRow.Range, tbl, CStr(columnName), CDbl(mapped(CStr(columnName)))
        Else
            WriteMappedValueToColumn newRow.Range, tbl, CStr(columnName), vbNullString
        End If
    Next columnName

    newRow.Range.ClearFormats
    For fmtCol = 1 To tbl.ListColumns.Count
        ApplyLogbookCellDataFormatting newRow.Range.Cells(1, fmtCol), templateRow.Cells(1, fmtCol)
    Next fmtCol
End Sub

Private Sub WriteMappedValueToColumn(ByVal rowRange As Range, _
                                     ByVal tbl As ListObject, _
                                     ByVal columnName As String, _
                                     ByVal value As Variant)
    rowRange.Cells(1, tbl.ListColumns(columnName).Index).Value = value
End Sub

Private Sub WriteMappedValueToColumnIfPresent(ByVal rowRange As Range, _
                                             ByVal tbl As ListObject, _
                                             ByVal columnName As String, _
                                             ByVal value As Variant)
    If ListColumnExists(tbl, columnName) Then
        WriteMappedValueToColumn rowRange, tbl, columnName, value
    End If
End Sub

Private Function BuildExistingLogTenDuplicateKeys(ByVal tbl As ListObject) As Object
    Dim result As Object
    Dim rowIndex As Long
    Dim key As String

    Set result = CreateObject("Scripting.Dictionary")
    result.CompareMode = vbTextCompare

    If Not tbl.DataBodyRange Is Nothing Then
        For rowIndex = 1 To tbl.DataBodyRange.Rows.Count
            key = BuildExistingLogTenDuplicateKey(tbl, rowIndex)
            If Not result.Exists(key) Then result.Add key, True
        Next rowIndex
    End If

    Set BuildExistingLogTenDuplicateKeys = result
End Function

Private Function LogTenMappedRowIsDuplicate(ByVal tbl As ListObject, ByVal mapped As Object) As Boolean
    Dim rowIndex As Long

    If tbl.DataBodyRange Is Nothing Then Exit Function

    For rowIndex = 1 To tbl.DataBodyRange.Rows.Count
        If BuildExistingLogTenDuplicateKey(tbl, rowIndex) = CStr(mapped("DuplicateKey")) Then
            LogTenMappedRowIsDuplicate = True
            Exit Function
        End If
    Next rowIndex
End Function

Private Function BuildExistingLogTenDuplicateKey(ByVal tbl As ListObject, ByVal rowIndex As Long) As String
    Dim parts As Collection
    Dim columnName As Variant
    Dim remarksColumn As String
    Dim value As Variant

    Set parts = New Collection
    value = tbl.ListColumns("Date").DataBodyRange.Cells(rowIndex, 1).Value
    If IsDate(value) Then
        parts.Add Format$(CDate(value), "yyyy-mm-dd")
    Else
        parts.Add ""
    End If
    parts.Add NormaliseDuplicateText(CStr(tbl.ListColumns("Type").DataBodyRange.Cells(rowIndex, 1).Value))
    parts.Add NormaliseDuplicateText(CStr(tbl.ListColumns("Reg").DataBodyRange.Cells(rowIndex, 1).Value))
    parts.Add NormaliseDuplicateText(CStr(LogbookColumnValue(tbl, rowIndex, "Flight ID")))
    parts.Add NormaliseDuplicateText(CStr(LogbookColumnValue(tbl, rowIndex, "From")))
    parts.Add NormaliseDuplicateText(CStr(LogbookColumnValue(tbl, rowIndex, "Via")))
    parts.Add NormaliseDuplicateText(CStr(LogbookColumnValue(tbl, rowIndex, "To")))
    remarksColumn = LogbookRemarksColumnName(tbl)
    parts.Add NormaliseDuplicateText(CStr(LogbookColumnValue(tbl, rowIndex, remarksColumn)))

    For Each columnName In LogTenDuplicateHourColumns()
        parts.Add FormatDuplicateNumber(tbl.ListColumns(CStr(columnName)).DataBodyRange.Cells(rowIndex, 1).Value)
    Next columnName

    BuildExistingLogTenDuplicateKey = JoinCollection(parts, "|")
End Function

Private Function BuildLogTenDuplicateKey(ByVal mapped As Object) As String
    Dim parts As Collection
    Dim columnName As Variant

    Set parts = New Collection
    parts.Add Format$(CDate(mapped("Date")), "yyyy-mm-dd")
    parts.Add NormaliseDuplicateText(CStr(mapped("Type")))
    parts.Add NormaliseDuplicateText(CStr(mapped("Reg")))
    parts.Add NormaliseDuplicateText(CStr(mapped("Flight ID")))
    parts.Add NormaliseDuplicateText(CStr(mapped("From")))
    parts.Add NormaliseDuplicateText(CStr(mapped("Via")))
    parts.Add NormaliseDuplicateText(CStr(mapped("To")))
    parts.Add NormaliseDuplicateText(CStr(mapped("Remarks")))

    For Each columnName In LogTenDuplicateHourColumns()
        parts.Add FormatDuplicateNumber(mapped(CStr(columnName)))
    Next columnName

    BuildLogTenDuplicateKey = JoinCollection(parts, "|")
End Function

Private Function LogTenDuplicateHourColumns() As Variant
    LogTenDuplicateHourColumns = Array("SeIcusDay", "SeIcusNight", "SeDualDay", "SeDualNight", _
                                       "SeCommandDay", "SeCommandNight", "MeIcusDay", "MeIcusNight", _
                                       "MeDualDay", "MeDualNight", "MeCommandDay", "MeCommandNight", _
                                       "CopilotDay", "CopilotNight", "IfrIf", "IfrSim")
End Function

Private Function FormatDuplicateNumber(ByVal value As Variant) As String
    If IsNumeric(value) Then
        FormatDuplicateNumber = Format$(RoundLogTenHours(CDbl(value)), "0.000000")
    Else
        FormatDuplicateNumber = "0.000000"
    End If
End Function

Private Function NormaliseDuplicateText(ByVal value As String) As String
    NormaliseDuplicateText = UCase$(Trim$(value))
End Function

Private Function ParseLogTenHours(ByVal value As String) As Double
    value = Trim$(value)
    If value = "" Then Exit Function

    If InStr(value, ":") > 0 Then
        Dim parts As Variant
        parts = Split(value, ":")
        If UBound(parts) = 1 Then
            If IsNumeric(parts(0)) And IsNumeric(parts(1)) Then
                ParseLogTenHours = RoundLogTenHours((CDbl(parts(0)) * 60 + CDbl(parts(1))) / 60)
            End If
        End If
    ElseIf IsNumeric(value) Then
        ParseLogTenHours = RoundLogTenHours(CDbl(value))
    End If
End Function

Private Function ParseLogTenNumber(ByVal value As String) As Double
    value = Trim$(value)
    If value = "" Then Exit Function
    If IsNumeric(value) Then ParseLogTenNumber = CDbl(value)
End Function

Private Function RoundLogTenHours(ByVal value As Double) As Double
    RoundLogTenHours = Round(value, 6)
End Function

Private Function FieldValue(ByVal sourceRow As Object, ByVal fieldName As String) As String
    If sourceRow.Exists(fieldName) Then FieldValue = CStr(sourceRow(fieldName))
End Function

Private Function FirstPresentField(ByVal sourceRow As Object, ByVal fieldNames As Variant) As String
    Dim fieldName As Variant
    For Each fieldName In fieldNames
        If Trim$(FieldValue(sourceRow, CStr(fieldName))) <> "" Then
            FirstPresentField = Trim$(FieldValue(sourceRow, CStr(fieldName)))
            Exit Function
        End If
    Next fieldName
End Function

Private Function JoinLogTenRowValues(ByVal sourceRow As Object) As String
    Dim key As Variant
    For Each key In sourceRow.Keys
        If Left$(CStr(key), 2) <> "__" Then
            JoinLogTenRowValues = JoinLogTenRowValues & " " & CStr(sourceRow(key))
        End If
    Next key
End Function

Private Function JoinNonBlank(ByVal values As Variant, ByVal delimiter As String) As String
    Dim item As Variant
    For Each item In values
        JoinNonBlank = AppendListItem(JoinNonBlank, CStr(item), delimiter)
    Next item
End Function

Private Function AppendListItem(ByVal listText As String, ByVal itemText As String, ByVal delimiter As String) As String
    itemText = Trim$(itemText)
    If itemText = "" Then
        AppendListItem = listText
    ElseIf listText = "" Then
        AppendListItem = itemText
    Else
        AppendListItem = listText & delimiter & itemText
    End If
End Function

Private Function JoinCollection(ByVal values As Collection, ByVal delimiter As String) As String
    Dim i As Long
    For i = 1 To values.Count
        If i > 1 Then JoinCollection = JoinCollection & delimiter
        JoinCollection = JoinCollection & CStr(values(i))
    Next i
End Function

Private Function JoinDictionaryKeys(ByVal dict As Object, ByVal delimiter As String) As String
    Dim key As Variant
    If dict Is Nothing Then Exit Function
    For Each key In dict.Keys
        JoinDictionaryKeys = AppendListItem(JoinDictionaryKeys, CStr(key), delimiter)
    Next key
    If JoinDictionaryKeys = "" Then JoinDictionaryKeys = "none"
End Function

Private Function LoadAircraftTypeClasses() As Object
    Dim tbl As ListObject
    Dim result As Object
    Dim rowIndex As Long
    Dim designator As String
    Dim engineClass As String

    Set result = CreateObject("Scripting.Dictionary")
    result.CompareMode = vbTextCompare
    Set tbl = EnsureAircraftTypesTable()

    If Not tbl.DataBodyRange Is Nothing Then
        For rowIndex = 1 To tbl.DataBodyRange.Rows.Count
            designator = UCase$(Trim$(CStr(tbl.ListColumns("Designator").DataBodyRange.Cells(rowIndex, 1).Value)))
            engineClass = UCase$(Trim$(CStr(tbl.ListColumns("EngineClass").DataBodyRange.Cells(rowIndex, 1).Value)))
            If designator <> "" And engineClass <> "" Then result(designator) = engineClass
        Next rowIndex
    End If

    Set LoadAircraftTypeClasses = result
End Function

Private Function EnsureAircraftTypesTable() As ListObject
    Dim ws As Worksheet
    Dim tbl As ListObject
    Dim headerRange As Range
    Dim workbookWasProtected As Boolean

    workbookWasProtected = ThisWorkbook.ProtectStructure
    If workbookWasProtected Then ThisWorkbook.Unprotect Password:=ProtectionPassword()

    On Error Resume Next
    Set ws = ThisWorkbook.Worksheets(AIRCRAFT_TYPES_SHEET)
    On Error GoTo 0

    If ws Is Nothing Then
        Set ws = ThisWorkbook.Worksheets.Add(After:=ThisWorkbook.Worksheets(ThisWorkbook.Worksheets.Count))
        ws.Name = AIRCRAFT_TYPES_SHEET
    End If
    ws.Unprotect Password:=ProtectionPassword()

    On Error Resume Next
    Set tbl = ws.ListObjects(AIRCRAFT_TYPES_TABLE)
    On Error GoTo 0

    If tbl Is Nothing Then
        ws.Cells.Clear
        ws.Range("A1:F1").Value = Array("Designator", "DescriptionCode", "EngineCount", "EngineClass", "Source", "LastUpdated")
        Set headerRange = ws.Range("A1:F2")
        Set tbl = ws.ListObjects.Add(xlSrcRange, headerRange, , xlYes)
        tbl.Name = AIRCRAFT_TYPES_TABLE
        If Not tbl.DataBodyRange Is Nothing Then tbl.DataBodyRange.Delete
        SeedAircraftTypes tbl
    ElseIf tbl.DataBodyRange Is Nothing Then
        SeedAircraftTypes tbl
    End If

    ws.Visible = xlSheetVeryHidden
    If workbookWasProtected Then ThisWorkbook.Protect Password:=ProtectionPassword(), Structure:=True, Windows:=False
    Set EnsureAircraftTypesTable = tbl
End Function

Private Sub SeedAircraftTypes(ByVal tbl As ListObject)
    AddAircraftTypeRow tbl, "A320", "L2J", "Seed"
    AddAircraftTypeRow tbl, "A321", "L2J", "Seed"
    AddAircraftTypeRow tbl, "C172", "L1P", "Seed"
    AddAircraftTypeRow tbl, "PA44", "L2P", "Seed"
End Sub

Private Sub AddAircraftTypeRow(ByVal tbl As ListObject, _
                               ByVal designator As String, _
                               ByVal descriptionCode As String, _
                               ByVal sourceText As String)
    Dim row As ListRow
    Dim engineCount As String

    engineCount = AircraftDescriptionEngineCount(descriptionCode)
    Set row = tbl.ListRows.Add
    row.Range.Cells(1, tbl.ListColumns("Designator").Index).Value = UCase$(designator)
    row.Range.Cells(1, tbl.ListColumns("DescriptionCode").Index).Value = UCase$(descriptionCode)
    row.Range.Cells(1, tbl.ListColumns("EngineCount").Index).Value = engineCount
    row.Range.Cells(1, tbl.ListColumns("EngineClass").Index).Value = AircraftEngineClass(engineCount)
    row.Range.Cells(1, tbl.ListColumns("Source").Index).Value = sourceText
    row.Range.Cells(1, tbl.ListColumns("LastUpdated").Index).Value = Date
End Sub

Private Function AircraftDescriptionEngineCount(ByVal descriptionCode As String) As String
    descriptionCode = UCase$(Trim$(descriptionCode))
    If Len(descriptionCode) >= 2 Then AircraftDescriptionEngineCount = Mid$(descriptionCode, 2, 1)
End Function

Private Function AircraftEngineClass(ByVal engineCount As String) As String
    Select Case UCase$(Trim$(engineCount))
        Case "1"
            AircraftEngineClass = "SE"
        Case "2", "3", "4", "6", "8", "C"
            AircraftEngineClass = "ME"
        Case Else
            AircraftEngineClass = ""
    End Select
End Function

Private Sub WriteLogTenImportReport(ByVal mappedRows As Collection, _
                                    ByVal errors As Collection, _
                                    ByVal unknownTypes As Object, _
                                    ByVal ignoredApproaches As Object, _
                                    ByVal imported As Long, _
                                    ByVal duplicates As Long, _
                                    ByVal blankRows As Long, _
                                    ByVal validationOnly As Boolean)
    Dim ws As Worksheet
    Dim rowIndex As Long
    Dim item As Variant
    Dim mapped As Object
    Dim key As Variant

    Set ws = EnsureLogTenImportReportSheet()
    ws.Cells.Clear
    ws.Range("A1").Value = "LogTen Import Report"
    ws.Range("A3").Value = "Imported"
    ws.Range("B3").Value = imported
    ws.Range("A4").Value = "Duplicates"
    ws.Range("B4").Value = duplicates
    ws.Range("A5").Value = "Blank rows ignored"
    ws.Range("B5").Value = blankRows
    ws.Range("A6").Value = "Validation only"
    ws.Range("B6").Value = validationOnly

    rowIndex = 8
    ws.Cells(rowIndex, 1).Value = "Issues"
    rowIndex = rowIndex + 1
    For Each item In errors
        ws.Cells(rowIndex, 1).Value = CStr(item)
        rowIndex = rowIndex + 1
    Next item
    For Each key In unknownTypes.Keys
        ws.Cells(rowIndex, 1).Value = "Unknown aircraft type: " & CStr(key)
        rowIndex = rowIndex + 1
    Next key
    For Each key In ignoredApproaches.Keys
        ws.Cells(rowIndex, 1).Value = "Ignored approach label: " & CStr(key)
        rowIndex = rowIndex + 1
    Next key

    rowIndex = rowIndex + 2
    ws.Cells(rowIndex, 1).Resize(1, 7).Value = Array("Status", "Source Row", "Date", "Type", "Reg", "Details", "Duplicate Key")
    rowIndex = rowIndex + 1
    For Each mapped In mappedRows
        ws.Cells(rowIndex, 1).Value = FieldValue(mapped, "Status")
        ws.Cells(rowIndex, 2).Value = mapped("SourceRow")
        ws.Cells(rowIndex, 3).Value = mapped("Date")
        ws.Cells(rowIndex, 4).Value = mapped("Type")
        ws.Cells(rowIndex, 5).Value = mapped("Reg")
        ws.Cells(rowIndex, 6).Value = mapped("Details")
        ws.Cells(rowIndex, 7).Value = mapped("DuplicateKey")
        rowIndex = rowIndex + 1
    Next mapped

    ws.Columns.AutoFit
End Sub

Private Function EnsureLogTenImportReportSheet() As Worksheet
    Dim workbookWasProtected As Boolean

    workbookWasProtected = ThisWorkbook.ProtectStructure
    If workbookWasProtected Then ThisWorkbook.Unprotect Password:=ProtectionPassword()

    On Error Resume Next
    Set EnsureLogTenImportReportSheet = ThisWorkbook.Worksheets(LOGTEN_REPORT_SHEET)
    On Error GoTo 0
    If EnsureLogTenImportReportSheet Is Nothing Then
        Set EnsureLogTenImportReportSheet = ThisWorkbook.Worksheets.Add(After:=ThisWorkbook.Worksheets(ThisWorkbook.Worksheets.Count))
        EnsureLogTenImportReportSheet.Name = LOGTEN_REPORT_SHEET
    End If
    EnsureLogTenImportReportSheet.Unprotect Password:=ProtectionPassword()
    EnsureLogTenImportReportSheet.Visible = xlSheetVisible
    If workbookWasProtected Then ThisWorkbook.Protect Password:=ProtectionPassword(), Structure:=True, Windows:=False
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

Private Function AircraftTypeHasEngineClassHours(ByVal tbl As ListObject, _
                                                 ByVal aircraftType As String, _
                                                 ByVal hourColumnNames As Variant) As Boolean
    Dim typeCol As Long
    Dim rowIndex As Long
    Dim NormalisedType As String

    If tbl.DataBodyRange Is Nothing Then Exit Function

    NormalisedType = LCase(Trim(aircraftType))
    If NormalisedType = "" Then Exit Function

    typeCol = tbl.ListColumns("Type").Index

    For rowIndex = 1 To tbl.DataBodyRange.Rows.Count
        If LCase(Trim(CStr(tbl.DataBodyRange.Cells(rowIndex, typeCol).Value))) = NormalisedType Then
            If SumLogbookRowColumns(tbl, rowIndex, hourColumnNames) > 0 Then
                AircraftTypeHasEngineClassHours = True
                Exit Function
            End If
        End If
    Next rowIndex
End Function

Private Function SumLogbookRowColumns(ByVal tbl As ListObject, _
                                      ByVal rowIndex As Long, _
                                      ByVal columnNames As Variant) As Double
    Dim columnName As Variant
    Dim cellValue As Variant

    For Each columnName In columnNames
        cellValue = tbl.DataBodyRange.Cells(rowIndex, tbl.ListColumns(CStr(columnName)).Index).Value
        If IsNumeric(cellValue) Then SumLogbookRowColumns = SumLogbookRowColumns + CDbl(cellValue)
    Next columnName
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

    HideRowsBelowLogbookData tbl
    RepairLogbookActionButtons tbl
End Sub

Private Sub RestoreNewEntryView()
    On Error Resume Next
    ThisWorkbook.Worksheets(NEW_ENTRY_ACTIVE_SHEET).Activate
    ActiveWindow.ScrollRow = 1
    ActiveWindow.ScrollColumn = 1
    On Error GoTo 0
End Sub

Public Sub HideRowsBelowLogbookData(ByVal tbl As ListObject, Optional ByVal bufferRows As Long = 7)
    Dim ws As Worksheet
    Dim lastDataRow As Long

    If tbl Is Nothing Then Exit Sub

    Set ws = tbl.Parent
    ws.Rows.Hidden = False
    If tbl.DataBodyRange Is Nothing Then Exit Sub

    lastDataRow = tbl.DataBodyRange.Row + tbl.DataBodyRange.Rows.Count - 1
    If lastDataRow + bufferRows <= ws.Rows.Count Then
        ws.Rows(lastDataRow + bufferRows & ":" & ws.Rows.Count).Hidden = True
    End If
End Sub

Private Sub RepairLogbookActionButtons(ByVal tbl As ListObject)
    RepairLogbookActionButton tbl, _
                           "DeleteSelectedLogbookRowsButton", _
                           "DeleteSelectedLogbookRows", _
                           "Year", _
                           False, _
                           False
    RepairLogbookActionButton tbl, _
                           "ExportLogbookButton", _
                           "ExportLogbook", _
                           "To", _
                           True, _
                           True
End Sub

Private Sub RepairLogbookActionButton(ByVal tbl As ListObject, _
                                      ByVal buttonName As String, _
                                      ByVal actionName As String, _
                                      ByVal alignColumnName As String, _
                                      ByVal createMissing As Boolean, _
                                      ByVal rebuildIfStillAway As Boolean)
    Dim ws        As Worksheet
    Dim btn       As Shape
    Dim topRow    As Long
    Dim leftCol   As Long
    Dim targetLeft As Double
    Dim targetTop  As Double

    On Error GoTo CleanFail
    Set ws = tbl.Parent

    topRow = tbl.TotalsRowRange.Row + 2
    leftCol = tbl.ListColumns(alignColumnName).Range.Column
    If topRow + 3 > ws.Rows.Count Then Exit Sub

    targetLeft = ws.Cells(topRow, leftCol).Left
    targetTop = ws.Cells(topRow, leftCol).Top

    On Error Resume Next
    Set btn = ws.Shapes(buttonName)
    On Error GoTo CleanFail

    If btn Is Nothing Then
        If createMissing Then
            CreateLogbookActionButtonShape ws, buttonName, actionName, targetLeft, targetTop
        End If
        Exit Sub
    End If

    ConfigureShapeAction btn, actionName

    On Error Resume Next
    MoveLogbookActionButton btn, targetLeft, targetTop
    On Error GoTo CleanFail

    If LogbookActionButtonIsAwayFromTarget(btn, targetLeft, targetTop) Then
        BringExportLogbookButtonTargetIntoView ws, topRow, leftCol
        MoveLogbookActionButton btn, targetLeft, targetTop
    End If

    If rebuildIfStillAway Then
        If LogbookActionButtonIsAwayFromTarget(btn, targetLeft, targetTop) Then
            RebuildLogbookActionButtonGroup ws, btn, buttonName, actionName, targetLeft, targetTop
        End If
    End If
CleanFail:
End Sub

Private Sub ConfigureShapeAction(ByVal shp As Shape, ByVal actionName As String)
    Dim item As Shape

    On Error Resume Next
    shp.OnAction = actionName
    If shp.Type = msoGroup Then
        For Each item In shp.GroupItems
            item.OnAction = actionName
        Next item
    End If
    On Error GoTo 0
End Sub

Private Sub BringExportLogbookButtonTargetIntoView(ByVal ws As Worksheet, _
                                                   ByVal topRow As Long, _
                                                   ByVal leftCol As Long)
    Dim restoreRow As Long
    Dim previousScreenUpdating As Boolean

    On Error Resume Next
    previousScreenUpdating = Application.ScreenUpdating
    Application.ScreenUpdating = False
    ws.Parent.Activate
    ws.Activate
    Application.Goto ws.Cells(topRow, leftCol), True
    restoreRow = topRow - 30
    If restoreRow < 1 Then restoreRow = 1
    ActiveWindow.ScrollColumn = 1
    ActiveWindow.ScrollRow = restoreRow
    Application.ScreenUpdating = previousScreenUpdating
    If previousScreenUpdating Then DoEvents
    On Error GoTo 0
End Sub

Private Function CreateLogbookActionButtonShape(ByVal ws As Worksheet, _
                                                ByVal buttonName As String, _
                                                ByVal actionName As String, _
                                                ByVal targetLeft As Double, _
                                                ByVal targetTop As Double) As Shape
    Dim btn As Shape

    On Error GoTo CleanExit
    Set btn = ws.Shapes.AddShape(msoShapeRoundedRectangle, targetLeft, targetTop, _
                                 LOGBOOK_ACTION_BUTTON_WIDTH, LOGBOOK_ACTION_BUTTON_HEIGHT)
    btn.Name = buttonName
    btn.TextFrame.Characters.Text = LogbookActionButtonFallbackText(buttonName)
    ConfigureShapeAction btn, actionName
    MoveLogbookActionButton btn, targetLeft, targetTop
    Set CreateLogbookActionButtonShape = btn

CleanExit:
End Function

Private Sub MoveLogbookActionButton(ByVal btn As Shape, _
                                    ByVal targetLeft As Double, _
                                    ByVal targetTop As Double)
    btn.Placement = xlFreeFloating
    btn.Visible = msoTrue
    btn.Left = targetLeft
    btn.Top = targetTop
    btn.Width = LOGBOOK_ACTION_BUTTON_WIDTH
    btn.Height = LOGBOOK_ACTION_BUTTON_HEIGHT
    btn.ZOrder msoBringToFront
End Sub

Private Function LogbookActionButtonIsAwayFromTarget(ByVal btn As Shape, _
                                                     ByVal targetLeft As Double, _
                                                     ByVal targetTop As Double) As Boolean
    LogbookActionButtonIsAwayFromTarget = _
        Abs(btn.Left - targetLeft) > LOGBOOK_ACTION_BUTTON_POSITION_TOLERANCE Or _
        Abs(btn.Top - targetTop) > LOGBOOK_ACTION_BUTTON_POSITION_TOLERANCE
End Function

Private Function LogbookActionButtonFallbackText(ByVal buttonName As String) As String
    Select Case buttonName
        Case "DeleteSelectedLogbookRowsButton"
            LogbookActionButtonFallbackText = "Delete Selected"
        Case Else
            LogbookActionButtonFallbackText = "Export Logbook"
    End Select
End Function

Private Sub RebuildLogbookActionButtonGroup(ByVal ws As Worksheet, _
                                            ByVal btn As Shape, _
                                            ByVal buttonName As String, _
                                            ByVal actionName As String, _
                                            ByVal targetLeft As Double, _
                                            ByVal targetTop As Double)
    Dim oldLeft      As Double
    Dim oldTop       As Double
    Dim itemCount    As Long
    Dim itemNames()  As String
    Dim itemLefts()  As Double
    Dim itemTops()   As Double
    Dim i            As Long
    Dim sr           As ShapeRange
    Dim rebuilt      As Shape

    On Error GoTo Fallback

    If btn.Type <> msoGroup Then GoTo Fallback

    oldLeft = btn.Left
    oldTop = btn.Top
    itemCount = btn.GroupItems.Count
    ReDim itemNames(1 To itemCount)
    ReDim itemLefts(1 To itemCount)
    ReDim itemTops(1 To itemCount)

    For i = 1 To itemCount
        itemNames(i) = btn.GroupItems.Item(i).Name
        itemLefts(i) = btn.GroupItems.Item(i).Left
        itemTops(i) = btn.GroupItems.Item(i).Top
    Next i

    Set sr = btn.Ungroup
    For i = 1 To itemCount
        ws.Shapes(itemNames(i)).Left = targetLeft + (itemLefts(i) - oldLeft)
        ws.Shapes(itemNames(i)).Top = targetTop + (itemTops(i) - oldTop)
        ws.Shapes(itemNames(i)).OnAction = actionName
    Next i

    Set rebuilt = ws.Shapes.Range(itemNames).Group
    rebuilt.Name = buttonName
    MoveLogbookActionButton rebuilt, targetLeft, targetTop
    Exit Sub

Fallback:
    On Error Resume Next
    btn.Delete
    Set rebuilt = CreateLogbookActionButtonShape(ws, buttonName, actionName, targetLeft, targetTop)
    On Error GoTo 0
End Sub

Public Sub UpdateHoursOverTimeChart(wb As Workbook)
    Dim wsCharts As Worksheet
    Dim wsData   As Worksheet
    Dim rnhRange As Range
    Dim rng      As Range
    Dim lastRow  As Long
    Dim chartObj As ChartObject
    Dim chartSer As Series

    Set wsCharts = wb.Sheets("Charts")
    Set wsData = wb.Sheets("ChartData")
    Set rnhRange = wb.Names("RunningTotalHours").RefersToRange
    Set chartObj = wsCharts.ChartObjects("HoursOverTime")

    lastRow = wsData.cells(wsData.Rows.Count, rnhRange.Columns(1).Column).End(xlUp).row
    If lastRow < 2 Then Exit Sub
    Set rng = wsData.Range( _
        wsData.cells(2, rnhRange.Columns(1).Column), _
        wsData.cells(lastRow, rnhRange.Columns(2).Column))

    On Error Resume Next

    If chartObj.Chart.SeriesCollection.Count = 0 Then
        chartObj.Chart.SeriesCollection.NewSeries
    End If

    Set chartSer = chartObj.Chart.SeriesCollection(1)
    chartSer.XValues = rng.Columns(1)
    chartSer.Values = rng.Columns(2)

    ' Fallback for chart states where direct XValues/Values assignment fails.
    If Err.Number <> 0 Then
        Err.Clear
        chartObj.Chart.SetSourceData Source:=rng
        If chartObj.Chart.SeriesCollection.Count > 0 Then
            Set chartSer = chartObj.Chart.SeriesCollection(1)
            chartSer.XValues = rng.Columns(1)
            chartSer.Values = rng.Columns(2)
        End If
    End If

    On Error GoTo 0
End Sub

Public Sub MarkRoutesDirty(Optional wb As Workbook = Nothing)
    If wb Is Nothing Then Set wb = ThisWorkbook
    SetWorkbookNameValue wb, "RoutesDirty", True
End Sub

Public Sub MarkRoutesDirtyForChangedRange(ByVal changedSheet As Object, ByVal changedRange As Range)
    On Error GoTo CleanExit

    If changedRange Is Nothing Then Exit Sub
    If TypeName(changedSheet) <> "Worksheet" Then Exit Sub

    If LogbookRouteSourceChanged(changedSheet, changedRange) Or _
       AirportRouteLookupChanged(changedSheet, changedRange) Or _
       KeywordRouteIgnoreListChanged(changedSheet, changedRange) Then
        MarkRoutesDirty changedSheet.Parent
    End If

CleanExit:
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

Public Sub CheckAirportDatasetOnOpen(Optional wb As Workbook = Nothing)
    Dim targetWorkbook As Workbook
    Dim protectionWasActive As Boolean
    Dim updateAvailable As Boolean
    Dim response As VbMsgBoxResult
    Dim errNum As Long
    Dim errDesc As String
    Dim errSource As String

    If ShouldSuppressOpenPrompts() Then Exit Sub

    On Error GoTo Fail
    If wb Is Nothing Then
        Set targetWorkbook = ThisWorkbook
    Else
        Set targetWorkbook = wb
    End If

    protectionWasActive = WorkbookProtectionIsActive(targetWorkbook)
    If protectionWasActive Then UnprotectWorkbookForEditing targetWorkbook
    updateAvailable = modAirports.AirportDatasetUpdateAvailable(targetWorkbook, False)
    If protectionWasActive And Not mProtectionDisabledForSession Then ApplyWorkbookProtection False, targetWorkbook

    If updateAvailable Then
        response = MsgBox("A newer airport dataset is available." & vbCrLf & vbCrLf & _
                          "Updating the Airports table may take a short time while the latest airport list and visit statistics are refreshed." & vbCrLf & vbCrLf & _
                          "Update now?", _
                          vbYesNo + vbInformation, "Airport Dataset Update Available")
        If response = vbYes Then RefreshAirportDatasetWithWorkbookProtection targetWorkbook, True, True
    End If
    Exit Sub

Fail:
    errNum = Err.Number
    errDesc = Err.Description
    errSource = Err.Source
    On Error Resume Next
    If protectionWasActive And Not mProtectionDisabledForSession Then ApplyWorkbookProtection False, targetWorkbook
    On Error GoTo 0
    WriteDebugLog "CheckAirportDatasetOnOpen", errNum, errDesc, "Checking whether a newer airport dataset is available"
    MsgBox BuildUserFacingErrorMessage( _
           "The airport dataset update check could not be completed.", _
           "Your logbook was not changed. Check your internet connection and try again later. If this keeps happening, use the Report a Bug button and include the debug log.", _
           errNum, errSource, errDesc, "Checking airport dataset version"), _
           vbExclamation, "Airport Dataset Check Failed"
End Sub

Public Function RefreshAirportDatasetWithWorkbookProtection(Optional wb As Workbook = Nothing, _
                                                           Optional forceCheck As Boolean = False, _
                                                           Optional showCompletionMessage As Boolean = False) As Boolean
    Dim targetWorkbook As Workbook
    Dim protectionWasActive As Boolean
    Dim oldScreenUpdating As Boolean
    Dim oldEnableEvents As Boolean
    Dim oldDisplayStatusBar As Boolean
    Dim oldStatusBar As Variant
    Dim oldCalculation As XlCalculation
    Dim errNum As Long
    Dim errDesc As String
    Dim errSource As String
    Dim diagStep As String

    On Error GoTo Fail
    diagStep = "Preparing workbook for airport dataset update"
    If wb Is Nothing Then
        Set targetWorkbook = ThisWorkbook
    Else
        Set targetWorkbook = wb
    End If

    protectionWasActive = WorkbookProtectionIsActive(targetWorkbook)
    If protectionWasActive Then UnprotectWorkbookForEditing targetWorkbook

    diagStep = "Saving Excel application state"
    oldScreenUpdating = Application.ScreenUpdating
    oldEnableEvents = Application.EnableEvents
    oldDisplayStatusBar = Application.DisplayStatusBar
    oldStatusBar = Application.StatusBar
    oldCalculation = Application.Calculation
    Application.ScreenUpdating = False
    Application.EnableEvents = False
    Application.DisplayStatusBar = True
    Application.StatusBar = "Updating airport dataset..."
    Application.Calculation = xlCalculationManual

    diagStep = "Downloading and importing airport dataset"
    RefreshAirportDatasetWithWorkbookProtection = modAirports.RefreshAirportDataset(targetWorkbook, forceCheck)
    diagStep = "Marking route cache after airport dataset update"
    If RefreshAirportDatasetWithWorkbookProtection Or modAirports.AirportDatasetRoutesStateNeedsRefresh(targetWorkbook) Then
        MarkRoutesDirty targetWorkbook
        modAirports.MarkAirportDatasetRoutesStateCurrent targetWorkbook
    End If

CleanExit:
    diagStep = "Restoring Excel application state"
    Application.StatusBar = oldStatusBar
    Application.DisplayStatusBar = oldDisplayStatusBar
    Application.Calculation = oldCalculation
    Application.EnableEvents = oldEnableEvents
    Application.ScreenUpdating = oldScreenUpdating
    If protectionWasActive And Not mProtectionDisabledForSession Then ApplyWorkbookProtection False, targetWorkbook
    If showCompletionMessage Then
        If RefreshAirportDatasetWithWorkbookProtection Then
            MsgBox "Airport dataset updated successfully." & vbCrLf & vbCrLf & _
                   "The Airports table and airport visit statistics have been refreshed.", _
                   vbInformation, "Airport Dataset Updated"
        Else
            MsgBox "Airport dataset check completed." & vbCrLf & vbCrLf & _
                   "Your Airports table is already up to date.", _
                   vbInformation, "Airport Dataset"
        End If
    End If
    Exit Function

Fail:
    errNum = Err.Number
    errDesc = Err.Description
    errSource = Err.Source
    On Error Resume Next
    Application.StatusBar = oldStatusBar
    Application.DisplayStatusBar = oldDisplayStatusBar
    Application.Calculation = oldCalculation
    Application.EnableEvents = oldEnableEvents
    Application.ScreenUpdating = oldScreenUpdating
    If protectionWasActive And Not mProtectionDisabledForSession Then ApplyWorkbookProtection False, targetWorkbook
    On Error GoTo 0
    WriteDebugLog "RefreshAirportDatasetWithWorkbookProtection", errNum, errDesc, diagStep
    MsgBox BuildUserFacingErrorMessage( _
           "The airport dataset could not be updated.", _
           "No logbook entries were changed. Check your internet connection and try the airport update again. If it keeps failing, use the Report a Bug button and include the debug log.", _
           errNum, errSource, errDesc, diagStep), _
           vbExclamation, "Airport Dataset Update Failed"
End Function

Public Sub RefreshAirportVisitStatsWithWorkbookProtection(Optional wb As Workbook = Nothing, _
                                                         Optional showCompletionMessage As Boolean = True)
    Dim targetWorkbook As Workbook
    Dim protectionWasActive As Boolean
    Dim oldStatusBar As Variant
    Dim oldDisplayStatusBar As Boolean
    Dim errNum As Long
    Dim errDesc As String
    Dim errSource As String

    On Error GoTo Fail
    If wb Is Nothing Then
        Set targetWorkbook = ThisWorkbook
    Else
        Set targetWorkbook = wb
    End If

    protectionWasActive = WorkbookProtectionIsActive(targetWorkbook)
    If protectionWasActive Then UnprotectWorkbookForEditing targetWorkbook

    oldDisplayStatusBar = Application.DisplayStatusBar
    oldStatusBar = Application.StatusBar
    Application.DisplayStatusBar = True
    Application.StatusBar = "Refreshing airport visit stats..."

    modAirports.RefreshAirportVisitStats targetWorkbook
    If targetWorkbook Is ThisWorkbook Then ThisWorkbook.AutoFitStatsSheetColumns

CleanExit:
    Application.StatusBar = oldStatusBar
    Application.DisplayStatusBar = oldDisplayStatusBar
    If protectionWasActive And Not mProtectionDisabledForSession Then ApplyWorkbookProtection False, targetWorkbook
    If showCompletionMessage Then
        MsgBox "Airport visit statistics refreshed successfully.", _
               vbInformation, "Airport Stats Refreshed"
    End If
    Exit Sub

Fail:
    errNum = Err.Number
    errDesc = Err.Description
    errSource = Err.Source
    On Error Resume Next
    Application.StatusBar = oldStatusBar
    Application.DisplayStatusBar = oldDisplayStatusBar
    If protectionWasActive And Not mProtectionDisabledForSession Then ApplyWorkbookProtection False, targetWorkbook
    On Error GoTo 0
    Err.Raise errNum, errSource, errDesc
End Sub

Public Sub BackupCurrentWorkbook()
    Dim localPath As String
    Dim canonicalName As String
    Dim backupPath As String

    On Error GoTo Fail
    localPath = ResolveLocalPath(ThisWorkbook)
    canonicalName = RecoveryCanonicalWorkbookName(ThisWorkbook.Name)
    backupPath = BuildRecoveryPath(localPath, canonicalName, "Backup")

    ThisWorkbook.SaveCopyAs backupPath
    MsgBox "Backup created successfully:" & vbCrLf & vbCrLf & backupPath, _
           vbInformation, "Backup Complete"
    Exit Sub

Fail:
    MsgBox BuildUserFacingErrorMessage( _
           "Could not create a backup copy.", _
           "Check that the workbook folder is writable and that the file is not blocked by OneDrive, SharePoint, or another sync tool. Then try again.", _
           Err.Number, Err.Source, Err.Description, "Creating backup workbook copy"), _
           vbCritical, "Backup Failed"
End Sub

Public Sub RestorePreviousVersion()
    Dim localPath As String
    Dim canonicalName As String
    Dim latestOldPath As String
    Dim restoredPath As String
    Dim oldPattern As String

    On Error GoTo Fail
    localPath = ResolveLocalPath(ThisWorkbook)
    canonicalName = RecoveryCanonicalWorkbookName(ThisWorkbook.Name)
    latestOldPath = FindLatestOldBackupPath(localPath, canonicalName)

    If latestOldPath = "" Then
         oldPattern = BuildOldPattern(canonicalName)
        MsgBox "No previous-version backup was found in this folder." & vbCrLf & vbCrLf & _
             "Expected pattern: " & oldPattern, _
               vbExclamation, "Restore Previous Version"
        Exit Sub
    End If

    restoredPath = BuildRecoveryPath(localPath, canonicalName, "Restored")
    FileCopy latestOldPath, restoredPath
    Workbooks.Open restoredPath, ReadOnly:=False

    MsgBox "A restored workbook copy has been created and opened:" & vbCrLf & vbCrLf & _
           restoredPath & vbCrLf & vbCrLf & _
           "Review it, then keep/rename it as needed.", _
           vbInformation, "Restore Copy Ready"
    Exit Sub

Fail:
    MsgBox BuildUserFacingErrorMessage( _
           "Could not prepare the restored workbook copy.", _
           "Check that the workbook folder is writable and that the previous-version backup still exists, then try again.", _
           Err.Number, Err.Source, Err.Description, "Preparing restored workbook copy"), _
           vbCritical, "Restore Failed"
End Sub

Private Function RecoveryCanonicalWorkbookName(ByVal workbookName As String) As String
    Dim dotPos As Long
    Dim baseName As String
    Dim extension As String
    Dim markerPos As Long
    Dim suffix As String

    dotPos = InStrRev(workbookName, ".")
    If dotPos > 0 Then
        baseName = Left$(workbookName, dotPos - 1)
        extension = Mid$(workbookName, dotPos)
    Else
        baseName = workbookName
        extension = ""
    End If

    markerPos = InStrRev(baseName, "_Old")
    If markerPos > 0 Then
        suffix = Mid$(baseName, markerPos + 4)
        If suffix = "" Or Left$(suffix, 1) = "_" Then
            baseName = Left$(baseName, markerPos - 1)
        End If
    End If

    RecoveryCanonicalWorkbookName = baseName & extension
End Function

Private Function BuildOldPattern(ByVal canonicalName As String) As String
    Dim dotPos As Long
    Dim baseName As String
    Dim extension As String

    dotPos = InStrRev(canonicalName, ".")
    If dotPos > 0 Then
        baseName = Left$(canonicalName, dotPos - 1)
        extension = Mid$(canonicalName, dotPos)
    Else
        baseName = canonicalName
        extension = ".xlsm"
    End If

    BuildOldPattern = baseName & "_Old_*" & extension
End Function

Private Function BuildRecoveryPath(ByVal localPath As String, ByVal canonicalName As String, ByVal marker As String) As String
    Dim dotPos As Long
    Dim baseName As String
    Dim extension As String
    Dim timestamp As String
    Dim candidate As String
    Dim suffix As Long

    dotPos = InStrRev(canonicalName, ".")
    If dotPos > 0 Then
        baseName = Left$(canonicalName, dotPos - 1)
        extension = Mid$(canonicalName, dotPos)
    Else
        baseName = canonicalName
        extension = ".xlsm"
    End If

    timestamp = Format(Now, "yyyymmdd-hhmmss")
    candidate = localPath & "\" & baseName & "_" & marker & "_" & timestamp & extension
    suffix = 1
    Do While Dir$(candidate) <> ""
        candidate = localPath & "\" & baseName & "_" & marker & "_" & timestamp & "_" & suffix & extension
        suffix = suffix + 1
    Loop

    BuildRecoveryPath = candidate
End Function

Private Function FindLatestOldBackupPath(ByVal localPath As String, ByVal canonicalName As String) As String
    Dim dotPos As Long
    Dim baseName As String
    Dim extension As String
    Dim pattern As String
    Dim candidate As String
    Dim latestPath As String
    Dim latestStamp As Date
    Dim currentStamp As Date

    dotPos = InStrRev(canonicalName, ".")
    If dotPos > 0 Then
        baseName = Left$(canonicalName, dotPos - 1)
        extension = Mid$(canonicalName, dotPos)
    Else
        baseName = canonicalName
        extension = ".xlsm"
    End If

    pattern = baseName & "_Old_*" & extension
    candidate = Dir$(localPath & "\" & pattern)
    Do While candidate <> ""
        currentStamp = FileDateTime(localPath & "\" & candidate)
        If latestPath = "" Or currentStamp > latestStamp Then
            latestStamp = currentStamp
            latestPath = localPath & "\" & candidate
        End If
        candidate = Dir$
    Loop

    FindLatestOldBackupPath = latestPath
End Function

Public Sub CheckRoutesTableOnOpen(Optional wb As Workbook = Nothing)
    If wb Is Nothing Then Set wb = ThisWorkbook

    If ShouldSkipRoutesPromptOnOpen(wb) Then Exit Sub

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
    ElseIf RoutesDirtyState(wb) Then
        If MsgBox("Your Routes table may be out of date because existing logbook entries or airport codes were changed." & vbCrLf & vbCrLf & _
                  "Rebuild the Routes table now?", _
                  vbYesNo + vbExclamation, "Routes May Be Out Of Date") = vbYes Then
            BuildRoutesTable wb
            MsgBox "Routes table rebuilt successfully.", vbInformation
        End If
    End If
End Sub

Private Function ShouldSkipRoutesPromptOnOpen(wb As Workbook) As Boolean
    Dim branchValue As String
    Dim fileName As String

    If ShouldSuppressOpenPrompts() Then
        ShouldSkipRoutesPromptOnOpen = True
        Exit Function
    End If

    fileName = LCase$(wb.Name)
    If InStr(fileName, "_old_") > 0 Or _
       InStr(fileName, "_backup_") > 0 Or _
       InStr(fileName, "_restored_") > 0 Then
        ShouldSkipRoutesPromptOnOpen = True
        Exit Function
    End If

    branchValue = LCase$(Trim$(CStr(GetWorkbookNameValue(wb, "GitHubBranch", ""))))
    If branchValue <> "" And branchValue <> "main" Then
        ShouldSkipRoutesPromptOnOpen = True
        Exit Function
    End If

    ShouldSkipRoutesPromptOnOpen = False
End Function

Public Sub DeleteSelectedLogbookRows()
    Dim ws As Worksheet
    Dim tbl As ListObject
    Dim selectedRows As Range
    Dim area As Range
    Dim rowRange As Range
    Dim rowIndexes As Object
    Dim key As Variant
    Dim rowIndex As Long
    Dim bottomDeletedRowIndex As Long
    Dim postDeleteRowIndex As Long
    Dim dateColumnIndex As Long
    Dim logbookWasProtected As Boolean
    Dim previousScreenUpdating As Boolean
    Dim previousEnableEvents As Boolean
    Dim previousDisplayAlerts As Boolean
    Dim previousCalculation As XlCalculation
    Dim previousDisplayStatusBar As Boolean
    Dim previousStatusBar As Variant
    Dim appStateCaptured As Boolean
    Dim previousAutoSaveOn As Boolean
    Dim autoSaveStateCaptured As Boolean

    On Error GoTo Fail

    Set ws = ThisWorkbook.Sheets("Logbook")
    Set tbl = ws.ListObjects("Logbook")
    If tbl.DataBodyRange Is Nothing Then Exit Sub

    Set selectedRows = Intersect(Selection.EntireRow, tbl.DataBodyRange)
    If selectedRows Is Nothing Then
        MsgBox "Select one or more Logbook table rows to delete.", vbInformation, "Delete Logbook Rows"
        Exit Sub
    End If

    Set rowIndexes = CreateObject("Scripting.Dictionary")
    For Each area In selectedRows.Areas
        For Each rowRange In area.Rows
            rowIndex = rowRange.Row - tbl.DataBodyRange.Row + 1
            If rowIndex >= 1 And rowIndex <= tbl.ListRows.Count Then
                rowIndexes(CStr(rowIndex)) = rowIndex
                If rowIndex > bottomDeletedRowIndex Then bottomDeletedRowIndex = rowIndex
            End If
        Next rowRange
    Next area

    If rowIndexes.Count = 0 Then Exit Sub
    If tbl.ListRows.Count - rowIndexes.Count < 1 Then
        MsgBox "At least one Logbook row must remain as the table template.", vbExclamation, "Delete Logbook Rows"
        Exit Sub
    End If

    If MsgBox("Delete " & rowIndexes.Count & " selected Logbook row(s)?", _
              vbOKCancel + vbExclamation, "Delete Logbook Rows") = vbCancel Then Exit Sub

    dateColumnIndex = tbl.ListColumns("Date").Index

    previousScreenUpdating = Application.ScreenUpdating
    previousEnableEvents = Application.EnableEvents
    previousDisplayAlerts = Application.DisplayAlerts
    previousCalculation = Application.Calculation
    previousDisplayStatusBar = Application.DisplayStatusBar
    previousStatusBar = Application.StatusBar
    appStateCaptured = True

    autoSaveStateCaptured = TryPauseWorkbookAutoSave(ThisWorkbook, previousAutoSaveOn)

    Application.ScreenUpdating = False
    Application.EnableEvents = False
    Application.DisplayAlerts = False
    Application.Calculation = xlCalculationManual
    Application.DisplayStatusBar = True
    Application.StatusBar = "Electronic Logbook: deleting selected rows..."
    Application.CutCopyMode = False

    logbookWasProtected = ws.ProtectContents
    If logbookWasProtected Then ws.Unprotect Password:=ProtectionPassword()

    For Each key In SortedDictionaryKeysDescending(rowIndexes)
        tbl.ListRows(CLng(rowIndexes(key))).Delete
    Next key

    Application.StatusBar = "Electronic Logbook: refreshing logbook summaries..."
    RefreshLogbookCalculatedFormulas tbl
    NormaliseLogbookFormatting tbl
    UpdateHiddenRows ThisWorkbook
    MarkRoutesDirty ThisWorkbook
    RefreshAirportVisitStatsWithWorkbookProtection ThisWorkbook, False
    RefreshWorkbookPivotSummariesWithWorkbookProtection ThisWorkbook
    postDeleteRowIndex = bottomDeletedRowIndex
    If postDeleteRowIndex > tbl.ListRows.Count Then postDeleteRowIndex = tbl.ListRows.Count
    RestoreLogbookSelectionAfterDelete ws, tbl, dateColumnIndex, postDeleteRowIndex

CleanExit:
    If logbookWasProtected Then ProtectLogbookSheetForRuntime ws
    RestoreWorkbookAutoSave ThisWorkbook, autoSaveStateCaptured, previousAutoSaveOn
    If appStateCaptured Then
        Application.Calculation = previousCalculation
        Application.DisplayAlerts = previousDisplayAlerts
        Application.EnableEvents = previousEnableEvents
        Application.ScreenUpdating = previousScreenUpdating
        If previousDisplayStatusBar Then
            Application.StatusBar = previousStatusBar
        Else
            Application.StatusBar = False
        End If
        Application.DisplayStatusBar = previousDisplayStatusBar
    End If
    Exit Sub

Fail:
    Dim errNum As Long
    Dim errDesc As String
    errNum = Err.Number
    errDesc = Err.Description
    On Error Resume Next
    If logbookWasProtected Then ProtectLogbookSheetForRuntime ws
    RestoreWorkbookAutoSave ThisWorkbook, autoSaveStateCaptured, previousAutoSaveOn
    If appStateCaptured Then
        Application.Calculation = previousCalculation
        Application.DisplayAlerts = previousDisplayAlerts
        Application.EnableEvents = previousEnableEvents
        Application.ScreenUpdating = previousScreenUpdating
        If previousDisplayStatusBar Then
            Application.StatusBar = previousStatusBar
        Else
            Application.StatusBar = False
        End If
        Application.DisplayStatusBar = previousDisplayStatusBar
    Else
        Application.ScreenUpdating = True
        Application.EnableEvents = True
        Application.DisplayAlerts = True
    End If
    On Error GoTo 0
    MsgBox BuildUserFacingErrorMessage( _
           "The selected Logbook rows could not be deleted.", _
           "No further rows were intentionally deleted. Check the Logbook table and try again after closing any dialogs or filters.", _
           errNum, "DeleteSelectedLogbookRows", errDesc, "Deleting selected Logbook rows"), _
           vbCritical, "Delete Logbook Rows"
End Sub

Private Sub RestoreLogbookSelectionAfterDelete(ByVal ws As Worksheet, _
                                               ByVal tbl As ListObject, _
                                               ByVal dateColumnIndex As Long, _
                                               ByVal targetRowIndex As Long)
    On Error Resume Next
    If ws Is Nothing Then Exit Sub
    If tbl Is Nothing Then Exit Sub
    If tbl.DataBodyRange Is Nothing Then Exit Sub
    If targetRowIndex < 1 Then targetRowIndex = 1
    If targetRowIndex > tbl.ListRows.Count Then targetRowIndex = tbl.ListRows.Count

    ThisWorkbook.Activate
    ws.Activate
    tbl.DataBodyRange.Cells(targetRowIndex, dateColumnIndex).Select
    On Error GoTo 0
End Sub

Private Function TryPauseWorkbookAutoSave(ByVal wb As Workbook, ByRef previousAutoSaveOn As Boolean) As Boolean
    On Error GoTo CleanExit

    previousAutoSaveOn = wb.AutoSaveOn
    If previousAutoSaveOn Then wb.AutoSaveOn = False
    TryPauseWorkbookAutoSave = True

CleanExit:
End Function

Private Sub RestoreWorkbookAutoSave(ByVal wb As Workbook, _
                                    ByVal autoSaveStateCaptured As Boolean, _
                                    ByVal previousAutoSaveOn As Boolean)
    On Error Resume Next
    If autoSaveStateCaptured And previousAutoSaveOn Then wb.AutoSaveOn = True
    On Error GoTo 0
End Sub

Private Function SortedDictionaryKeysDescending(ByVal dict As Object) As Variant
    Dim keys As Variant
    Dim i As Long
    Dim j As Long
    Dim temp As Variant

    keys = dict.Keys
    For i = LBound(keys) To UBound(keys) - 1
        For j = i + 1 To UBound(keys)
            If CLng(dict(keys(i))) < CLng(dict(keys(j))) Then
                temp = keys(i)
                keys(i) = keys(j)
                keys(j) = temp
            End If
        Next j
    Next i

    SortedDictionaryKeysDescending = keys
End Function

Private Function ShouldSuppressOpenPrompts() As Boolean
    Dim flagValue As String

    On Error Resume Next

    ' Automation sessions (for example, COM-driven tooling) should not block on MsgBox prompts.
    If Application.Visible = False Then
        ShouldSuppressOpenPrompts = True
        Exit Function
    End If

    If Application.UserControl = False Then
        ShouldSuppressOpenPrompts = True
        Exit Function
    End If

    flagValue = LCase$(Trim$(Environ$("ELB_SUPPRESS_OPEN_PROMPTS")))
    If flagValue = "1" Or flagValue = "true" Or flagValue = "yes" Then
        ShouldSuppressOpenPrompts = True
        Exit Function
    End If

    On Error GoTo 0
    ShouldSuppressOpenPrompts = False
End Function

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
        Dim protectionWasActive As Boolean
        Dim errNum As Long
        Dim errDesc As String
        Dim errSource As String

        On Error GoTo Fail
        If wb Is Nothing Then Set wb = ThisWorkbook
        protectionWasActive = WorkbookProtectionIsActive(wb)
        If protectionWasActive Then UnprotectWorkbookForEditing wb

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
            details = LogbookRouteSourceText(tblLog, row)
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
        If protectionWasActive And Not mProtectionDisabledForSession Then ApplyWorkbookProtection False, wb
        Exit Sub

Fail:
        errNum = Err.Number
        errDesc = Err.Description
        errSource = Err.Source
        If protectionWasActive And Not mProtectionDisabledForSession Then
            On Error Resume Next
            ApplyWorkbookProtection False, wb
            On Error GoTo 0
        End If
        Err.Raise errNum, errSource, errDesc
End Sub

Private Function WorkbookProtectionIsActive(wb As Workbook) As Boolean
    Dim ws As Worksheet

    If wb.ProtectStructure Or wb.ProtectWindows Then
        WorkbookProtectionIsActive = True
        Exit Function
    End If

    For Each ws In wb.Worksheets
        If ws.ProtectContents Then
            WorkbookProtectionIsActive = True
            Exit Function
        End If
    Next ws
End Function

Sub AddNewRoutes()

    '===============================
    ' STEP 1: SETUP
    '===============================
        Dim wsLog       As Worksheet
        Dim wsRoutes    As Worksheet
        Dim tblLog      As ListObject
        Dim tblAirports As ListObject
        Dim tblRoutes   As ListObject
        Dim routesWasProtected As Boolean
        Dim errNum As Long
        Dim errDesc As String
        Dim errSource As String

        On Error GoTo Fail

        Set wsLog = ThisWorkbook.Sheets("Logbook")
        Set wsRoutes = ThisWorkbook.Sheets("Routes")
        routesWasProtected = wsRoutes.ProtectContents
        If routesWasProtected Then wsRoutes.Unprotect Password:=ProtectionPassword()

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
        If IsLogbookRowSimOnly(tblLog, lastLogRow) Then GoTo CleanExit

        Dim details As String
        details = LogbookRouteSourceText(tblLog, lastLogRow)
        If details = "" Then GoTo CleanExit

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

CleanExit:
        If routesWasProtected And Not mProtectionDisabledForSession Then ProtectStandardWorksheetForRuntime wsRoutes
        Exit Sub

Fail:
        errNum = Err.Number
        errDesc = Err.Description
        errSource = Err.Source
        On Error Resume Next
        If routesWasProtected And Not mProtectionDisabledForSession Then ProtectStandardWorksheetForRuntime wsRoutes
        On Error GoTo 0
        Err.Raise errNum, errSource, errDesc
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
    Dim NormalisedToken As String

    Set tblKeywords = FindListObject(ThisWorkbook, "Keywords")
    If tblKeywords Is Nothing Then Exit Function

    NormalisedToken = NormaliseKeywordText(token)
    For Each keywordColumn In tblKeywords.ListColumns
        If Not keywordColumn.DataBodyRange Is Nothing Then
            For Each keywordCell In keywordColumn.DataBodyRange.Cells
                If Not IsError(keywordCell.Value) Then
                    If Trim$(CStr(keywordCell.Value)) <> "" Then
                        If InStr(1, NormaliseKeywordText(CStr(keywordCell.Value)), _
                                   NormalisedToken, vbBinaryCompare) > 0 Then
                            KeywordTableContainsToken = True
                            Exit Function
                        End If
                    End If
                End If
            Next keywordCell
        End If
    Next keywordColumn
End Function

Private Function LogbookRouteSourceText(ByVal tbl As ListObject, ByVal rowIndex As Long) As String
    Dim routeText As String
    Dim remarksColumn As String

    If ListColumnExists(tbl, "From") Or ListColumnExists(tbl, "To") Or ListColumnExists(tbl, "Via") Then
        routeText = AppendListItem(routeText, LogbookColumnValue(tbl, rowIndex, "From"), " ")
        routeText = AppendListItem(routeText, LogbookColumnValue(tbl, rowIndex, "Via"), " ")
        routeText = AppendListItem(routeText, LogbookColumnValue(tbl, rowIndex, "To"), " ")
    End If

    If Len(Trim$(routeText)) = 0 Then
        remarksColumn = LogbookRemarksColumnName(tbl)
        If Len(remarksColumn) > 0 Then
            routeText = CStr(LogbookColumnValue(tbl, rowIndex, remarksColumn))
        End If
    End If

    LogbookRouteSourceText = Trim$(routeText)
End Function

Private Function LogbookColumnValue(ByVal tbl As ListObject, _
                                    ByVal rowIndex As Long, _
                                    ByVal columnName As String) As Variant
    If Not ListColumnExists(tbl, columnName) Then Exit Function
    LogbookColumnValue = tbl.ListColumns(columnName).DataBodyRange.Cells(rowIndex, 1).Value
End Function

Private Function LogbookRemarksColumnName(ByVal tbl As ListObject) As String
    If ListColumnExists(tbl, "Remarks") Then
        LogbookRemarksColumnName = "Remarks"
    ElseIf ListColumnExists(tbl, "Details") Then
        LogbookRemarksColumnName = "Details"
    End If
End Function

' ==============================================================
' LOGBOOK EXPORT
' ==============================================================

Public Sub ExportLogbook()
    frmExportLogbook.Show
End Sub

Public Function ChooseLogbookExportPath(ByVal exportFormat As String) As String
    Dim selectedPath As Variant
    Dim fileFilter As String
    Dim defaultPath As String

    Select Case LCase$(Trim$(exportFormat))
        Case "xlsx": fileFilter = "Excel Workbook (*.xlsx),*.xlsx"
        Case "csv": fileFilter = "CSV UTF-8 (*.csv),*.csv"
        Case "pdf": fileFilter = "PDF (*.pdf),*.pdf"
        Case Else: Exit Function
    End Select

    defaultPath = ResolveLocalPath(ThisWorkbook) & Application.PathSeparator & _
                  "Logbook Export " & Format$(Date, "yyyy-mm-dd") & _
                  "." & LCase$(Trim$(exportFormat))
    selectedPath = Application.GetSaveAsFilename( _
        InitialFileName:=defaultPath, _
        FileFilter:=fileFilter, Title:="Choose Export Location")
    If VarType(selectedPath) = vbBoolean Then Exit Function
    ChooseLogbookExportPath = EnsureLogbookExportExtension( _
        CStr(selectedPath), exportFormat)
End Function

Public Function LastLogbookExportError() As String
    LastLogbookExportError = mLastLogbookExportError
End Function

Public Function ExportLogbookToFile(ByVal outputPath As String, _
                                    ByVal exportFormat As String, _
                                    ByVal combineDetails As Boolean, _
                                    Optional ByVal startDate As Variant, _
                                    Optional ByVal endDate As Variant, _
                                    Optional ByVal showErrors As Boolean = False) As Boolean
    Dim sourceSheet As Worksheet
    Dim sourceTable As ListObject
    Dim selectedRows As Collection
    Dim outputHeaders As Collection
    Dim sourceIndexes As Collection
    Dim outputValues As Variant
    Dim exportBook As Workbook
    Dim exportSheet As Worksheet
    Dim previousScreenUpdating As Boolean
    Dim previousDisplayAlerts As Boolean
    Dim previousStatusBar As Variant
    Dim previousDisplayStatusBar As Boolean
    Dim errorMessage As String

    On Error GoTo Fail
    mLastLogbookExportError = vbNullString

    exportFormat = LCase$(Trim$(exportFormat))
    If exportFormat <> "xlsx" And exportFormat <> "csv" And exportFormat <> "pdf" Then
        Err.Raise vbObjectError + 2300, "ExportLogbookToFile", _
                  "The export format must be xlsx, csv, or pdf."
    End If
    If Len(Trim$(outputPath)) = 0 Then
        Err.Raise vbObjectError + 2301, "ExportLogbookToFile", _
                  "An output path is required."
    End If
    If Not IsMissing(startDate) And Not IsEmpty(startDate) And Not IsDate(startDate) Then
        Err.Raise vbObjectError + 2302, "ExportLogbookToFile", _
                  "The start date is not valid."
    End If
    If Not IsMissing(endDate) And Not IsEmpty(endDate) And Not IsDate(endDate) Then
        Err.Raise vbObjectError + 2303, "ExportLogbookToFile", _
                  "The end date is not valid."
    End If
    If Not IsMissing(startDate) And Not IsMissing(endDate) Then
        If Not IsEmpty(startDate) And Not IsEmpty(endDate) Then
            If CDate(startDate) > CDate(endDate) Then
                Err.Raise vbObjectError + 2304, "ExportLogbookToFile", _
                          "The start date cannot be later than the end date."
            End If
        End If
    End If

    previousScreenUpdating = Application.ScreenUpdating
    previousDisplayAlerts = Application.DisplayAlerts
    previousDisplayStatusBar = Application.DisplayStatusBar
    previousStatusBar = Application.StatusBar
    Application.ScreenUpdating = False
    Application.DisplayAlerts = False
    Application.DisplayStatusBar = True
    Application.StatusBar = "Electronic Logbook: preparing export"

    Set sourceSheet = ThisWorkbook.Worksheets("Logbook")
    Set sourceTable = sourceSheet.ListObjects("Logbook")
    ValidateLogbookExportColumns sourceTable

    Set selectedRows = SelectLogbookExportRows(sourceTable, startDate, endDate)
    If selectedRows.Count = 0 Then
        Err.Raise vbObjectError + 2305, "ExportLogbookToFile", _
                  "No logbook entries match the selected date range."
    End If

    Set outputHeaders = New Collection
    Set sourceIndexes = New Collection
    BuildLogbookExportColumns sourceTable, selectedRows, combineDetails, _
                              outputHeaders, sourceIndexes
    outputValues = BuildLogbookExportValues(sourceTable, selectedRows, _
                                            outputHeaders, sourceIndexes, _
                                            combineDetails)

    outputPath = EnsureLogbookExportExtension(outputPath, exportFormat)
    PrepareLogbookExportOutputFile outputPath
    If exportFormat = "csv" Then
        Application.StatusBar = "Electronic Logbook: writing CSV"
        WriteLogbookCsv outputPath, outputValues
    Else
        Application.StatusBar = "Electronic Logbook: building formatted export"
        Set exportBook = CreateFormattedLogbookExport( _
            sourceSheet, sourceTable, outputValues, sourceIndexes, _
            combineDetails, exportFormat = "xlsx", exportSheet)

        If exportFormat = "xlsx" Then
            Application.StatusBar = "Electronic Logbook: saving XLSX"
            exportBook.SaveAs Filename:=outputPath, FileFormat:=xlOpenXMLWorkbook, _
                              CreateBackup:=False, Local:=True
            exportBook.Close SaveChanges:=False
            Set exportBook = Application.Workbooks.Open(outputPath)
            Set exportSheet = exportBook.Worksheets(1)
            Application.ScreenUpdating = True
            ConfigureCopiedLogbookView exportBook, exportSheet, _
                                       exportSheet.ListObjects(1)
            Application.ScreenUpdating = False
            exportBook.Save
        Else
            Application.StatusBar = "Electronic Logbook: creating PDF"
            ConfigureLogbookPdf exportSheet
            exportSheet.ExportAsFixedFormat Type:=xlTypePDF, Filename:=outputPath, _
                                            Quality:=xlQualityStandard, _
                                            IncludeDocProperties:=True, _
                                            IgnorePrintAreas:=False, _
                                            OpenAfterPublish:=False
        End If
    End If

    ExportLogbookToFile = True

Cleanup:
    On Error Resume Next
    If Not exportBook Is Nothing Then exportBook.Close SaveChanges:=False
    Application.CutCopyMode = False
    Application.StatusBar = previousStatusBar
    Application.DisplayStatusBar = previousDisplayStatusBar
    Application.DisplayAlerts = previousDisplayAlerts
    Application.ScreenUpdating = previousScreenUpdating
    On Error GoTo 0
    Exit Function

Fail:
    errorMessage = Err.Description
    mLastLogbookExportError = errorMessage
    ExportLogbookToFile = False
    If showErrors Then
        MsgBox "The logbook could not be exported." & vbCrLf & vbCrLf & errorMessage, _
               vbExclamation, "Export Logbook"
    End If
    Resume Cleanup
End Function

Private Sub PrepareLogbookExportOutputFile(ByVal outputPath As String)
    If Len(Dir$(outputPath)) = 0 Then Exit Sub

    On Error GoTo DeleteFailed
    SetAttr outputPath, vbNormal
    Kill outputPath
    On Error GoTo 0
    Exit Sub

DeleteFailed:
    Err.Raise vbObjectError + 2306, "PrepareLogbookExportOutputFile", _
              "The selected export file could not be replaced. Close the existing file and try again:" & _
              vbCrLf & outputPath
End Sub

Private Sub ValidateLogbookExportColumns(ByVal sourceTable As ListObject)
    Dim columnName As Variant

    For Each columnName In Array( _
        "Date", "Year", "Month", "Day", "Type", "Reg", "Flight ID", "PIC", _
        "Other Pilot or Crew", "From", "To", "Via", "Remarks", "FR", "IPC", "OPC", _
        "SeIcusDay", "Circling", "TotalHours", "TotalApps")
        If Not ListColumnExists(sourceTable, CStr(columnName)) Then
            Err.Raise vbObjectError + 2310, "ValidateLogbookExportColumns", _
                      "The Logbook table is missing the required '" & CStr(columnName) & "' column."
        End If
    Next columnName
End Sub

Private Function SelectLogbookExportRows(ByVal sourceTable As ListObject, _
                                         ByVal startDate As Variant, _
                                         ByVal endDate As Variant) As Collection
    Dim rows As New Collection
    Dim rowIndex As Long
    Dim entryDate As Date
    Dim hasStart As Boolean
    Dim hasEnd As Boolean

    hasStart = Not IsMissing(startDate) And Not IsEmpty(startDate)
    hasEnd = Not IsMissing(endDate) And Not IsEmpty(endDate)

    If sourceTable.DataBodyRange Is Nothing Then
        Set SelectLogbookExportRows = rows
        Exit Function
    End If

    For rowIndex = 1 To sourceTable.ListRows.Count
        If LogbookRowHasEntryData(sourceTable, rowIndex) Then
            If Not TryGetLogbookExportDate(sourceTable, rowIndex, entryDate) Then
                Err.Raise vbObjectError + 2311, "SelectLogbookExportRows", _
                          "Logbook row " & CStr(sourceTable.DataBodyRange.Row + rowIndex - 1) & _
                          " contains data but does not have a valid date."
            End If

            If (Not hasStart Or entryDate >= DateValue(CDate(startDate))) And _
               (Not hasEnd Or entryDate <= DateValue(CDate(endDate))) Then
                rows.Add rowIndex
            End If
        End If
    Next rowIndex

    Set SelectLogbookExportRows = rows
End Function

Private Function LogbookRowHasEntryData(ByVal sourceTable As ListObject, _
                                        ByVal rowIndex As Long) As Boolean
    Dim firstIndex As Long
    Dim lastIndex As Long
    Dim columnIndex As Long
    Dim value As Variant

    firstIndex = sourceTable.ListColumns("Year").Index
    lastIndex = sourceTable.ListColumns("Circling").Index

    For columnIndex = firstIndex To lastIndex
        value = sourceTable.DataBodyRange.Cells(rowIndex, columnIndex).Value2
        If IsError(value) Then
            LogbookRowHasEntryData = True
            Exit Function
        ElseIf VarType(value) = vbBoolean Then
            If CBool(value) Then
                LogbookRowHasEntryData = True
                Exit Function
            End If
        ElseIf IsNumeric(value) Then
            If CDbl(value) <> 0 Then
                LogbookRowHasEntryData = True
                Exit Function
            End If
        ElseIf Len(Trim$(CStr(value))) > 0 Then
            LogbookRowHasEntryData = True
            Exit Function
        End If
    Next columnIndex
End Function

Private Function TryGetLogbookExportDate(ByVal sourceTable As ListObject, _
                                         ByVal rowIndex As Long, _
                                         ByRef entryDate As Date) As Boolean
    Dim value As Variant
    Dim dateText As String

    On Error GoTo InvalidDate

    value = sourceTable.DataBodyRange.Cells( _
        rowIndex, sourceTable.ListColumns("Date").Index).Value
    If Not IsError(value) And IsDate(value) Then
        entryDate = DateValue(CDate(value))
        TryGetLogbookExportDate = True
        Exit Function
    End If

    dateText = CStr(sourceTable.DataBodyRange.Cells( _
        rowIndex, sourceTable.ListColumns("Day").Index).Value) & " " & _
        CStr(sourceTable.DataBodyRange.Cells( _
        rowIndex, sourceTable.ListColumns("Month").Index).Value) & " " & _
        CStr(sourceTable.DataBodyRange.Cells( _
        rowIndex, sourceTable.ListColumns("Year").Index).Value)
    If IsDate(dateText) Then
        entryDate = DateValue(CDate(dateText))
        TryGetLogbookExportDate = True
    End If
    Exit Function

InvalidDate:
    TryGetLogbookExportDate = False
End Function

Private Sub BuildLogbookExportColumns(ByVal sourceTable As ListObject, _
                                      ByVal selectedRows As Collection, _
                                      ByVal combineDetails As Boolean, _
                                      ByVal outputHeaders As Collection, _
                                      ByVal sourceIndexes As Collection)
    Dim columnName As Variant
    Dim columnIndex As Long
    Dim customStart As Long
    Dim customEnd As Long

    For Each columnName In Array( _
        "Year", "Month", "Day", "Type", "Reg", "Flight ID", "PIC", _
        "Other Pilot or Crew")
        AddLogbookExportColumn sourceTable, CStr(columnName), outputHeaders, sourceIndexes
    Next columnName

    If combineDetails Then
        outputHeaders.Add "Details"
        sourceIndexes.Add 0
    Else
        For Each columnName In Array("From", "To", "Via", "Remarks", "FR", "IPC", "OPC")
            AddLogbookExportColumn sourceTable, CStr(columnName), outputHeaders, sourceIndexes
        Next columnName
    End If

    customStart = sourceTable.ListColumns("OPC").Index + 1
    customEnd = sourceTable.ListColumns("SeIcusDay").Index - 1
    For columnIndex = customStart To customEnd
        If LogbookExportColumnHasData(sourceTable, columnIndex, selectedRows) Then
            outputHeaders.Add sourceTable.ListColumns(columnIndex).Name
            sourceIndexes.Add columnIndex
        End If
    Next columnIndex

    For columnIndex = sourceTable.ListColumns("SeIcusDay").Index To _
                      sourceTable.ListColumns("Circling").Index
        outputHeaders.Add sourceTable.ListColumns(columnIndex).Name
        sourceIndexes.Add columnIndex
    Next columnIndex

    AddLogbookExportColumn sourceTable, "TotalHours", outputHeaders, sourceIndexes
    AddLogbookExportColumn sourceTable, "TotalApps", outputHeaders, sourceIndexes
End Sub

Private Sub AddLogbookExportColumn(ByVal sourceTable As ListObject, _
                                   ByVal columnName As String, _
                                   ByVal outputHeaders As Collection, _
                                   ByVal sourceIndexes As Collection)
    outputHeaders.Add sourceTable.ListColumns(columnName).Name
    sourceIndexes.Add sourceTable.ListColumns(columnName).Index
End Sub

Private Function LogbookExportColumnHasData(ByVal sourceTable As ListObject, _
                                            ByVal columnIndex As Long, _
                                            ByVal selectedRows As Collection) As Boolean
    Dim rowItem As Variant
    Dim value As Variant

    For Each rowItem In selectedRows
        value = sourceTable.DataBodyRange.Cells(CLng(rowItem), columnIndex).Value2
        If IsError(value) Then
            LogbookExportColumnHasData = True
            Exit Function
        ElseIf VarType(value) = vbBoolean Then
            If CBool(value) Then
                LogbookExportColumnHasData = True
                Exit Function
            End If
        ElseIf IsNumeric(value) Then
            If CDbl(value) <> 0 Then
                LogbookExportColumnHasData = True
                Exit Function
            End If
        ElseIf Len(Trim$(CStr(value))) > 0 Then
            LogbookExportColumnHasData = True
            Exit Function
        End If
    Next rowItem
End Function

Private Function BuildLogbookExportValues(ByVal sourceTable As ListObject, _
                                          ByVal selectedRows As Collection, _
                                          ByVal outputHeaders As Collection, _
                                          ByVal sourceIndexes As Collection, _
                                          ByVal combineDetails As Boolean) As Variant
    Dim values As Variant
    Dim outputRow As Long
    Dim outputColumn As Long
    Dim sourceRow As Long
    Dim sourceIndex As Long

    ReDim values(1 To selectedRows.Count + 1, 1 To outputHeaders.Count)

    For outputColumn = 1 To outputHeaders.Count
        values(1, outputColumn) = CStr(outputHeaders(outputColumn))
    Next outputColumn

    For outputRow = 1 To selectedRows.Count
        sourceRow = CLng(selectedRows(outputRow))
        For outputColumn = 1 To outputHeaders.Count
            sourceIndex = CLng(sourceIndexes(outputColumn))
            If combineDetails And sourceIndex = 0 Then
                values(outputRow + 1, outputColumn) = _
                    CombinedLogbookDetails(sourceTable, sourceRow)
            Else
                values(outputRow + 1, outputColumn) = _
                    sourceTable.DataBodyRange.Cells(sourceRow, sourceIndex).Value2
            End If
        Next outputColumn
    Next outputRow

    BuildLogbookExportValues = values
End Function

Private Function CombinedLogbookDetails(ByVal sourceTable As ListObject, _
                                        ByVal rowIndex As Long) As String
    Dim routeText As String
    Dim remarksText As String
    Dim flagsText As String

    AppendLogbookRoutePart routeText, LogbookExportCellText(sourceTable, rowIndex, "From")
    AppendLogbookRoutePart routeText, LogbookExportCellText(sourceTable, rowIndex, "Via")
    AppendLogbookRoutePart routeText, LogbookExportCellText(sourceTable, rowIndex, "To")

    remarksText = LogbookExportCellText(sourceTable, rowIndex, "Remarks")
    If Len(remarksText) > 0 Then AppendLogbookDetailPart routeText, "(" & remarksText & ")"

    If LogbookExportFlagValue(sourceTable, rowIndex, "FR") Then
        AppendLogbookSlashPart flagsText, "Flight Review"
    End If
    If LogbookExportFlagValue(sourceTable, rowIndex, "IPC") Then
        AppendLogbookSlashPart flagsText, "IPC"
    End If
    If LogbookExportFlagValue(sourceTable, rowIndex, "OPC") Then
        AppendLogbookSlashPart flagsText, "OPC"
    End If
    If Len(flagsText) > 0 Then AppendLogbookDetailPart routeText, "(" & flagsText & ")"

    CombinedLogbookDetails = routeText
End Function

Private Sub AppendLogbookRoutePart(ByRef routeText As String, ByVal partText As String)
    If Len(partText) = 0 Then Exit Sub
    If Len(routeText) > 0 Then routeText = routeText & "-"
    routeText = routeText & partText
End Sub

Private Sub AppendLogbookDetailPart(ByRef detailsText As String, ByVal partText As String)
    If Len(partText) = 0 Then Exit Sub
    If Len(detailsText) > 0 Then detailsText = detailsText & " "
    detailsText = detailsText & partText
End Sub

Private Sub AppendLogbookSlashPart(ByRef flagsText As String, ByVal partText As String)
    If Len(flagsText) > 0 Then flagsText = flagsText & "/"
    flagsText = flagsText & partText
End Sub

Private Function LogbookExportCellText(ByVal sourceTable As ListObject, _
                                       ByVal rowIndex As Long, _
                                       ByVal columnName As String) As String
    Dim value As Variant

    value = sourceTable.DataBodyRange.Cells( _
        rowIndex, sourceTable.ListColumns(columnName).Index).Value2
    If Not IsError(value) Then LogbookExportCellText = Trim$(CStr(value))
End Function

Private Function LogbookExportFlagValue(ByVal sourceTable As ListObject, _
                                        ByVal rowIndex As Long, _
                                        ByVal columnName As String) As Boolean
    Dim value As Variant

    value = sourceTable.DataBodyRange.Cells( _
        rowIndex, sourceTable.ListColumns(columnName).Index).Value2
    If IsError(value) Or IsEmpty(value) Then Exit Function
    If VarType(value) = vbBoolean Then
        LogbookExportFlagValue = CBool(value)
    ElseIf IsNumeric(value) Then
        LogbookExportFlagValue = (CDbl(value) <> 0)
    Else
        LogbookExportFlagValue = (LCase$(Trim$(CStr(value))) = "true" Or _
                                  LCase$(Trim$(CStr(value))) = "yes")
    End If
End Function

Private Function CreateFormattedLogbookExport( _
    ByVal sourceSheet As Worksheet, _
    ByVal sourceTable As ListObject, _
    ByVal outputValues As Variant, _
    ByVal sourceIndexes As Collection, _
    ByVal combineDetails As Boolean, _
    ByVal preserveCalculatedTotals As Boolean, _
    ByRef exportSheet As Worksheet) As Workbook

    Dim exportBook As Workbook
    Dim placeholderSheet As Worksheet
    Dim exportTable As ListObject
    Dim sourceColumnIndex As Long
    Dim absoluteColumn As Long
    Dim targetRows As Long
    Dim keepColumn As Boolean
    Dim outputIndex As Variant
    Dim targetRange As Range

    Set exportBook = Application.Workbooks.Add(xlWBATWorksheet)
    Set placeholderSheet = exportBook.Worksheets(1)
    sourceSheet.Copy Before:=placeholderSheet
    Set exportSheet = exportBook.Worksheets(1)
    placeholderSheet.Delete

    exportSheet.Name = "Logbook Export"
    exportSheet.Unprotect Password:=ProtectionPassword()
    Set exportTable = exportSheet.ListObjects(1)

    ' Delete unwanted worksheet columns from right to left. Working at sheet
    ' level keeps the multi-row LogbookHeaders block aligned with the table.
    For sourceColumnIndex = sourceTable.ListColumns.Count To 1 Step -1
        keepColumn = False
        For Each outputIndex In sourceIndexes
            If CLng(outputIndex) = sourceColumnIndex Then
                keepColumn = True
                Exit For
            End If
        Next outputIndex
        If combineDetails And _
           sourceColumnIndex = sourceTable.ListColumns("From").Index Then
            keepColumn = True
        End If

        If Not keepColumn Then
            absoluteColumn = sourceTable.Range.Column + sourceColumnIndex - 1
            exportSheet.Columns(absoluteColumn).Delete
        End If
    Next sourceColumnIndex

    Set exportTable = exportSheet.ListObjects(1)
    targetRows = UBound(outputValues, 1) - 1

    Do While exportTable.ListRows.Count > targetRows
        exportTable.ListRows(exportTable.ListRows.Count).Delete
    Loop
    Do While exportTable.ListRows.Count < targetRows
        exportTable.ListRows.Add AlwaysInsert:=True
    Loop

    Set targetRange = exportSheet.Range( _
        exportTable.HeaderRowRange.Cells(1, 1), _
        exportTable.DataBodyRange.Cells(targetRows, exportTable.ListColumns.Count))
    targetRange.Value2 = outputValues

    If combineDetails Then
        exportTable.ListColumns("Details").Range.ColumnWidth = 60
        exportTable.ListColumns("Details").DataBodyRange.WrapText = True
    End If

    exportTable.ShowTotals = True
    On Error Resume Next
    If exportSheet.FilterMode Then exportSheet.ShowAllData
    exportTable.AutoFilter.ShowAllData
    On Error GoTo 0
    exportSheet.Rows("1:" & CStr(exportTable.TotalsRowRange.Row + 2)).Hidden = False
    exportSheet.Calculate
    ConfigureCopiedLogbookDateHeader exportBook, exportTable
    ApplyCopiedLogbookLeftBorder exportBook, exportTable
    CleanCopiedLogbookWorkbook exportBook, exportSheet
    Set exportTable = exportSheet.ListObjects(1)
    RecalculateCopiedLogbookTotals exportBook, exportTable, preserveCalculatedTotals
    exportSheet.Calculate

    Set CreateFormattedLogbookExport = exportBook
End Function

Private Sub RecalculateCopiedLogbookTotals(ByVal exportBook As Workbook, _
                                           ByVal exportTable As ListObject, _
                                           ByVal preserveFormulas As Boolean)
    Dim sumTotalsRange As Range
    Dim totalsBlock As Range
    Dim totalCell As Range
    Dim tableColumnIndex As Long
    Dim grandTotalHours As Double
    Dim simulatorHours As Double
    Dim tableName As String
    Dim columnName As String
    Dim sumFormula As String

    Set sumTotalsRange = exportBook.Names("LogbookSumTotals").RefersToRange
    tableName = exportTable.Name
    For Each totalCell In sumTotalsRange.Cells
        tableColumnIndex = totalCell.Column - exportTable.Range.Column + 1
        If tableColumnIndex >= 1 And tableColumnIndex <= exportTable.ListColumns.Count Then
            If preserveFormulas Then
                columnName = Replace( _
                    exportTable.ListColumns(tableColumnIndex).Name, "]", "]]")
                sumFormula = "=SUBTOTAL(109," & tableName & "[" & columnName & "])"
                totalCell.Formula = sumFormula
            Else
                totalCell.Value2 = SumNumericLogbookExportRange( _
                    exportTable.ListColumns(tableColumnIndex).DataBodyRange)
            End If
        End If
    Next totalCell

    grandTotalHours = SumNumericLogbookExportRange( _
        exportTable.ListColumns("TotalHours").DataBodyRange)
    simulatorHours = SumNumericLogbookExportRange( _
        exportTable.ListColumns("IfrSim").DataBodyRange)

    Set totalsBlock = exportBook.Names("LogbookTotals").RefersToRange
    If preserveFormulas Then
        totalsBlock.Cells(1, totalsBlock.Columns.Count).Formula = _
            "=SUBTOTAL(109," & tableName & "[TotalHours])"
        totalsBlock.Cells(2, totalsBlock.Columns.Count).Formula = _
            "=SUBTOTAL(109," & tableName & "[TotalHours])+" & _
            "SUBTOTAL(109," & tableName & "[IfrSim])"
    Else
        totalsBlock.Cells(1, totalsBlock.Columns.Count).Value2 = grandTotalHours
        totalsBlock.Cells(2, totalsBlock.Columns.Count).Value2 = _
            grandTotalHours + simulatorHours
    End If

    With sumTotalsRange.Cells(1, 1).Offset(0, -1)
        .Value2 = "TOTALS:"
        .HorizontalAlignment = xlRight
    End With
End Sub

Private Function SumNumericLogbookExportRange(ByVal valuesRange As Range) As Double
    Dim cell As Range
    Dim value As Variant

    If valuesRange Is Nothing Then Exit Function
    For Each cell In valuesRange.Cells
        value = cell.Value2
        If Not IsError(value) And IsNumeric(value) Then
            SumNumericLogbookExportRange = _
                SumNumericLogbookExportRange + CDbl(value)
        End If
    Next cell
End Function

Private Sub ConfigureCopiedLogbookDateHeader(ByVal exportBook As Workbook, _
                                             ByVal exportTable As ListObject)
    Dim headersRange As Range
    Dim dateHeaderRange As Range
    Dim firstDatePartColumn As Long
    Dim lastDatePartColumn As Long
    Dim firstHeaderRow As Long
    Dim lastHeaderRow As Long

    Set headersRange = exportBook.Names("LogbookHeaders").RefersToRange
    firstDatePartColumn = exportTable.ListColumns("Year").Range.Column
    lastDatePartColumn = exportTable.ListColumns("Day").Range.Column
    firstHeaderRow = headersRange.Row
    lastHeaderRow = headersRange.Row + headersRange.Rows.Count - 1

    Set dateHeaderRange = exportTable.Parent.Range( _
        exportTable.Parent.Cells(firstHeaderRow, firstDatePartColumn), _
        exportTable.Parent.Cells(lastHeaderRow, lastDatePartColumn))
    With dateHeaderRange
        If .MergeCells Then .UnMerge
        .ClearContents
        .Merge
        .Value2 = "DATE"
        .HorizontalAlignment = xlCenter
        .VerticalAlignment = xlCenter
        .Font.Bold = True
    End With
End Sub

Private Sub ApplyCopiedLogbookLeftBorder(ByVal exportBook As Workbook, _
                                         ByVal exportTable As ListObject)
    Dim headersRange As Range
    Dim borderRange As Range
    Dim firstColumn As Long
    Dim firstRow As Long
    Dim lastRow As Long

    Set headersRange = exportBook.Names("LogbookHeaders").RefersToRange
    firstColumn = exportTable.Range.Column
    firstRow = headersRange.Row
    lastRow = exportTable.TotalsRowRange.Row
    Set borderRange = exportTable.Parent.Range( _
        exportTable.Parent.Cells(firstRow, firstColumn), _
        exportTable.Parent.Cells(lastRow, firstColumn))

    With borderRange.Borders(xlEdgeLeft)
        .LineStyle = xlContinuous
        .Color = vbBlack
        .Weight = xlThin
    End With

    With exportTable.Parent.Range( _
        exportTable.Parent.Cells(lastRow + 1, firstColumn), _
        exportTable.Parent.Cells(lastRow + 2, firstColumn)).Borders(xlEdgeLeft)
        .LineStyle = xlNone
    End With
End Sub

Private Sub CleanCopiedLogbookWorkbook(ByVal exportBook As Workbook, _
                                       ByVal exportSheet As Worksheet)
    Dim nameIndex As Long
    Dim nameText As String
    Dim separatorPosition As Long
    Dim cleanupStep As String
    Dim formulaCells As Range
    Dim formulaArea As Range

    On Error GoTo CleanupFailed

    ' The export is a snapshot. Remove formula dependencies and input
    ' validation before discarding unrelated source-workbook names.
    cleanupStep = "freezing exported values"
    On Error Resume Next
    Set formulaCells = exportSheet.UsedRange.SpecialCells(xlCellTypeFormulas)
    On Error GoTo CleanupFailed
    If Not formulaCells Is Nothing Then
        For Each formulaArea In formulaCells.Areas
            formulaArea.Value2 = formulaArea.Value2
        Next formulaArea
    End If
    On Error Resume Next
    exportSheet.Cells.Validation.Delete
    On Error GoTo 0

    cleanupStep = "removing unrelated workbook names"
    For nameIndex = exportBook.Names.Count To 1 Step -1
        nameText = exportBook.Names(nameIndex).Name
        separatorPosition = InStrRev(nameText, "!")
        If separatorPosition > 0 Then nameText = Mid$(nameText, separatorPosition + 1)
        nameText = Replace(nameText, "'", vbNullString)

        If LCase$(nameText) <> "logbookheaders" And _
           LCase$(nameText) <> "logbooktotals" And _
           LCase$(nameText) <> "logbooksumtotals" Then
            On Error Resume Next
            exportBook.Names(nameIndex).Delete
            On Error GoTo 0
        End If
    Next nameIndex
    Exit Sub

CleanupFailed:
    Err.Raise Err.Number, "CleanCopiedLogbookWorkbook", _
              cleanupStep & ": " & Err.Description
End Sub

Private Sub ConfigureCopiedLogbookView(ByVal exportBook As Workbook, _
                                       ByVal exportSheet As Worksheet, _
                                       ByVal exportTable As ListObject)
    exportBook.Activate
    exportSheet.Activate
    exportBook.Windows(1).DisplayGridlines = False
    exportBook.Windows(1).Zoom = ThisWorkbook.Windows(1).Zoom
    exportBook.Windows(1).Activate
    exportSheet.Activate
    With exportBook.Windows(1)
        .FreezePanes = False
        .Split = False
    End With
    exportSheet.Cells(exportTable.HeaderRowRange.Row + 1, 1).Select
    exportBook.Windows(1).FreezePanes = True
End Sub

Private Sub ConfigureLogbookPdf(ByVal exportSheet As Worksheet)
    Dim exportTable As ListObject
    Dim lastPrintRow As Long
    Dim lastPrintColumn As Long

    Set exportTable = exportSheet.ListObjects(1)
    HideLogbookPdfDrawingObjects exportSheet
    lastPrintRow = exportTable.TotalsRowRange.Row + 2
    lastPrintColumn = exportTable.Range.Column + exportTable.ListColumns.Count

    With exportSheet.PageSetup
        .PrintArea = exportSheet.Range( _
            exportSheet.Cells(1, 1), _
            exportSheet.Cells(lastPrintRow, lastPrintColumn)).Address
        .PrintTitleRows = "$2:$5"
        .Orientation = xlLandscape
        If Not TrySetLogbookPdfPaperSize(exportSheet.PageSetup, xlPaperA3) Then
            TrySetLogbookPdfPaperSize exportSheet.PageSetup, xlPaperA4
        End If
        .Zoom = False
        .FitToPagesWide = 1
        .FitToPagesTall = False
        .CenterHorizontally = True
        .LeftMargin = Application.CentimetersToPoints(0.5)
        .RightMargin = Application.CentimetersToPoints(0.5)
        .TopMargin = Application.CentimetersToPoints(0.8)
        .BottomMargin = Application.CentimetersToPoints(0.8)
        .FooterMargin = Application.CentimetersToPoints(0.3)
        .CenterFooter = "Page &P of &N"
    End With
End Sub

Private Sub HideLogbookPdfDrawingObjects(ByVal exportSheet As Worksheet)
    Dim shp As Shape
    Dim oleObject As OLEObject

    On Error Resume Next
    For Each shp In exportSheet.Shapes
        shp.PrintObject = False
        shp.Visible = msoFalse
    Next shp

    For Each oleObject In exportSheet.OLEObjects
        oleObject.PrintObject = False
        oleObject.Visible = False
    Next oleObject
    On Error GoTo 0
End Sub

Private Function TrySetLogbookPdfPaperSize(ByVal pageSetup As PageSetup, _
                                           ByVal paperSize As XlPaperSize) As Boolean
    On Error Resume Next
    pageSetup.PaperSize = paperSize
    TrySetLogbookPdfPaperSize = (Err.Number = 0)
    Err.Clear
    On Error GoTo 0
End Function

Private Sub WriteLogbookCsv(ByVal outputPath As String, ByVal outputValues As Variant)
    Const adTypeBinary As Long = 1
    Const adTypeText As Long = 2
    Const adSaveCreateOverWrite As Long = 2
    Dim textStream As Object
    Dim binaryStream As Object
    Dim rowIndex As Long
    Dim columnIndex As Long
    Dim lineText As String

    Set textStream = CreateObject("ADODB.Stream")
    textStream.Type = adTypeText
    textStream.Charset = "utf-8"
    textStream.Open

    For rowIndex = 1 To UBound(outputValues, 1)
        lineText = vbNullString
        For columnIndex = 1 To UBound(outputValues, 2)
            If columnIndex > 1 Then lineText = lineText & ","
            lineText = lineText & CsvLogbookValue(outputValues(rowIndex, columnIndex))
        Next columnIndex
        textStream.WriteText lineText & vbCrLf
    Next rowIndex

    textStream.Position = 0
    textStream.Type = adTypeBinary
    textStream.Position = 3

    Set binaryStream = CreateObject("ADODB.Stream")
    binaryStream.Type = adTypeBinary
    binaryStream.Open
    textStream.CopyTo binaryStream
    binaryStream.SaveToFile outputPath, adSaveCreateOverWrite

    binaryStream.Close
    textStream.Close
End Sub

Private Function CsvLogbookValue(ByVal value As Variant) As String
    Dim textValue As String

    If IsError(value) Then
        textValue = "#ERROR"
    ElseIf IsEmpty(value) Then
        textValue = vbNullString
    ElseIf VarType(value) = vbBoolean Then
        If CBool(value) Then
            textValue = "TRUE"
        Else
            textValue = "FALSE"
        End If
    Else
        textValue = CStr(value)
    End If

    If InStr(textValue, """") > 0 Then textValue = Replace(textValue, """", """""")
    If InStr(textValue, ",") > 0 Or InStr(textValue, """") > 0 Or _
       InStr(textValue, vbCr) > 0 Or InStr(textValue, vbLf) > 0 Then
        textValue = """" & textValue & """"
    End If
    CsvLogbookValue = textValue
End Function

Public Function EnsureLogbookExportExtension(ByVal outputPath As String, _
                                             ByVal exportFormat As String) As String
    Dim expectedExtension As String

    expectedExtension = "." & LCase$(Trim$(exportFormat))
    If LCase$(Right$(outputPath, Len(expectedExtension))) <> expectedExtension Then
        outputPath = outputPath & expectedExtension
    End If
    EnsureLogbookExportExtension = outputPath
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
    Dim typeIndex As Long
    Dim circlingIndex As Long

    Set wsLog = ThisWorkbook.Sheets("Logbook")
    Set tbl = wsLog.ListObjects("Logbook")

    typeIndex = tbl.ListColumns("Type").Index
    circlingIndex = tbl.ListColumns("Circling").Index

    UpdateLogbookFilterHeadersNamedRange tbl, ThisWorkbook

    '--- Show arrows on Date and on every logbook-entry column from Type through Circling.
    For i = 1 To tbl.ListColumns.Count
        Dim showArrow As Boolean
        showArrow = (tbl.ListColumns(i).Name = "Date") Or _
                    (i >= typeIndex And i <= circlingIndex)

        tbl.Range.AutoFilter Field:=i, VisibleDropDown:=showArrow
    Next i

    Application.ScreenUpdating = True

End Sub

Public Sub ReportBug()
    Const BUG_REPORT_FORM_URL As String = _
        "https://docs.google.com/forms/d/e/1FAIpQLScCSzixoAFcyIBE6FI-wl1xMofomKPTePtUcwrUK7II7z_V9w/viewform"
    Dim diagnosticsPath As String
    Dim diagnosticsCreated As Boolean

    On Error GoTo Fail
    If InStr(1, BUG_REPORT_FORM_URL, "REPLACE_WITH_FORM_ID", vbTextCompare) > 0 Then
        MsgBox "The bug report form has not been configured yet.", _
               vbInformation, "Bug Report Form Unavailable"
        Exit Sub
    End If

    diagnosticsCreated = ExportDiagnosticsInternal(False, diagnosticsPath)

    ThisWorkbook.FollowHyperlink Address:=BUG_REPORT_FORM_URL, NewWindow:=True

    If diagnosticsCreated Then
        MsgBox "The bug form has been opened." & vbCrLf & vbCrLf & _
               "A diagnostics snapshot was also generated at:" & vbCrLf & diagnosticsPath & vbCrLf & vbCrLf & _
               "Attach it only if requested.", vbInformation, "Bug Report Started"
    Else
        MsgBox "The bug form has been opened, but diagnostics export failed." & vbCrLf & vbCrLf & _
               "You can still submit your report manually.", vbExclamation, "Bug Report Started"
    End If
    Exit Sub

Fail:
    MsgBox "Could not open the bug report form. Please visit:" & vbCrLf & vbCrLf & _
           BUG_REPORT_FORM_URL, vbExclamation, "Bug Report Form Unavailable"
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

Public Sub EnableAddToLogbookLayoutDiagnostics()
    Dim ws As Worksheet

    Set ws = EnsureAddToLogbookLayoutDiagnosticSheet(True)
    ws.Range("AA1").Value = True
    SetAddToLogbookLayoutDiagnosticsName ws
    ws.Visible = xlSheetVisible
    ws.Activate
End Sub

Public Sub DisableAddToLogbookLayoutDiagnostics()
    On Error Resume Next
    ThisWorkbook.Names(ADD_LOGBOOK_LAYOUT_DIAG_FLAG).RefersToRange.Value = False
    On Error GoTo 0
End Sub

Public Sub ClearAddToLogbookLayoutDiagnostics()
    If Not AddToLogbookLayoutDiagnosticsEnabled() Then Exit Sub
    EnsureAddToLogbookLayoutDiagnosticSheet True
End Sub

Private Function AddToLogbookLayoutDiagnosticsEnabled() As Boolean
    On Error GoTo Fail
    AddToLogbookLayoutDiagnosticsEnabled = CBool( _
        GetWorkbookNameValue(ThisWorkbook, ADD_LOGBOOK_LAYOUT_DIAG_FLAG, False))
    Exit Function
Fail:
    AddToLogbookLayoutDiagnosticsEnabled = False
End Function

Private Function EnsureAddToLogbookLayoutDiagnosticSheet(Optional ByVal clearExisting As Boolean = False) As Worksheet
    Dim ws As Worksheet
    Dim workbookWasProtected As Boolean

    On Error Resume Next
    Set ws = ThisWorkbook.Worksheets(ADD_LOGBOOK_LAYOUT_DIAG_SHEET)
    On Error GoTo 0

    If ws Is Nothing Then
        workbookWasProtected = ThisWorkbook.ProtectStructure
        If workbookWasProtected Then ThisWorkbook.Unprotect Password:=ProtectionPassword()
        Set ws = ThisWorkbook.Worksheets.Add(After:=ThisWorkbook.Worksheets(ThisWorkbook.Worksheets.Count))
        ws.Name = ADD_LOGBOOK_LAYOUT_DIAG_SHEET
        If workbookWasProtected Then ThisWorkbook.Protect Password:=ProtectionPassword(), Structure:=True, Windows:=False
    End If

    If clearExisting Or Len(Trim$(CStr(ws.Cells(1, 1).Value))) = 0 Then
        ws.Cells.Clear
        ws.Range("A1:Z1").Value = Array( _
            "Timestamp", "Stage", "SheetProtected", "ScreenUpdating", "EnableEvents", _
            "Calculation", "TableRows", "DataBodyLastRow", "TotalsRow", "TargetRow", _
            "TargetCell", "TargetLeft", "TargetTop", "HiddenFromRow", "ButtonExists", _
            "ButtonVisible", "ButtonPlacement", "ButtonLeft", "ButtonTop", "ButtonWidth", _
            "ButtonHeight", "ButtonTopLeftCell", "ButtonBottomRightCell", "ButtonOnAction", _
            "ButtonName", "Notes")
        ws.Rows(1).Font.Bold = True
        ws.Columns("A:Z").EntireColumn.AutoFit
    End If

    Set EnsureAddToLogbookLayoutDiagnosticSheet = ws
End Function

Private Sub SetAddToLogbookLayoutDiagnosticsName(ByVal ws As Worksheet)
    On Error Resume Next
    ThisWorkbook.Names(ADD_LOGBOOK_LAYOUT_DIAG_FLAG).Delete
    On Error GoTo 0
    ThisWorkbook.Names.Add Name:=ADD_LOGBOOK_LAYOUT_DIAG_FLAG, _
        RefersTo:="='" & ws.Name & "'!$AA$1"
End Sub

Private Sub TraceAddToLogbookLayout(ByVal stage As String, Optional ByVal tbl As ListObject = Nothing)
    On Error GoTo CleanExit

    Dim wsDiag          As Worksheet
    Dim wsLog           As Worksheet
    Dim btn             As Shape
    Dim targetCell      As Range
    Dim nextRow         As Long
    Dim tableRows       As Variant
    Dim dataBodyLastRow As Variant
    Dim totalsRow       As Variant
    Dim targetRow       As Variant
    Dim hiddenFromRow   As Variant
    Dim targetLeft      As Variant
    Dim targetTop       As Variant
    Dim buttonExists    As Boolean
    Dim buttonVisible   As Variant
    Dim buttonPlacement As Variant
    Dim buttonLeft      As Variant
    Dim buttonTop       As Variant
    Dim buttonWidth     As Variant
    Dim buttonHeight    As Variant
    Dim buttonTopLeft   As String
    Dim buttonBottomRight As String
    Dim buttonOnAction  As String
    Dim buttonName      As String
    Dim sheetProtected  As Variant
    Dim targetAddress   As String
    Dim notes           As String

    If Not AddToLogbookLayoutDiagnosticsEnabled() Then Exit Sub

    If tbl Is Nothing Then
        On Error Resume Next
        Set tbl = ThisWorkbook.Worksheets("Logbook").ListObjects("Logbook")
        On Error GoTo CleanExit
    End If

    If Not tbl Is Nothing Then
        Set wsLog = tbl.Parent
        sheetProtected = wsLog.ProtectContents
        tableRows = tbl.ListRows.Count
        If Not tbl.DataBodyRange Is Nothing Then
            dataBodyLastRow = tbl.DataBodyRange.Row + tbl.DataBodyRange.Rows.Count - 1
            hiddenFromRow = CLng(dataBodyLastRow) + 7
        End If
        If tbl.ShowTotals Then
            totalsRow = tbl.TotalsRowRange.Row
            targetRow = CLng(totalsRow) + 2
            Set targetCell = wsLog.Cells(CLng(targetRow), tbl.ListColumns("Year").Range.Column)
            targetAddress = targetCell.Address(False, False)
            targetLeft = targetCell.Left
            targetTop = targetCell.Top
        Else
            notes = "Totals row is currently hidden."
        End If

        On Error Resume Next
        Set btn = wsLog.Shapes("ExportLogbookButton")
        buttonExists = Not btn Is Nothing
        If buttonExists Then
            buttonVisible = btn.Visible
            buttonPlacement = btn.Placement
            buttonLeft = btn.Left
            buttonTop = btn.Top
            buttonWidth = btn.Width
            buttonHeight = btn.Height
            buttonTopLeft = btn.TopLeftCell.Address(False, False)
            buttonBottomRight = btn.BottomRightCell.Address(False, False)
            buttonOnAction = btn.OnAction
            buttonName = btn.Name
        End If
        If Err.Number <> 0 Then
            If Len(notes) > 0 Then notes = notes & " "
            notes = notes & "Button read error " & CStr(Err.Number) & ": " & Err.Description
            Err.Clear
        End If
        On Error GoTo CleanExit
    Else
        notes = "Logbook table was not available."
    End If

    Set wsDiag = EnsureAddToLogbookLayoutDiagnosticSheet(False)
    nextRow = wsDiag.Cells(wsDiag.Rows.Count, 1).End(xlUp).Row + 1
    If nextRow < 2 Then nextRow = 2

    wsDiag.Cells(nextRow, 1).Resize(1, 26).Value = Array( _
        Now, stage, sheetProtected, _
        Application.ScreenUpdating, Application.EnableEvents, Application.Calculation, _
        tableRows, dataBodyLastRow, totalsRow, targetRow, _
        targetAddress, _
        targetLeft, targetTop, hiddenFromRow, buttonExists, _
        buttonVisible, buttonPlacement, buttonLeft, buttonTop, buttonWidth, _
        buttonHeight, buttonTopLeft, buttonBottomRight, buttonOnAction, _
        buttonName, notes)

CleanExit:
End Sub

Public Function BuildUserFacingErrorMessage(ByVal userMessage As String, _
                                            ByVal recoveryMessage As String, _
                                            ByVal errNum As Long, _
                                            ByVal errSource As String, _
                                            ByVal errDesc As String, _
                                            Optional ByVal diagStep As String = "", _
                                            Optional ByVal diagnosticsPath As String = "") As String
    Dim details As String

    details = "Technical details for support:" & vbCrLf & _
              "Error " & CStr(errNum)
    If Trim$(errSource) <> "" Then details = details & " in " & errSource
    If Trim$(errDesc) <> "" Then details = details & ": " & errDesc
    If Trim$(diagStep) <> "" Then details = details & vbCrLf & "Step: " & diagStep
    If Trim$(diagnosticsPath) <> "" Then details = details & vbCrLf & "Diagnostics: " & diagnosticsPath

    BuildUserFacingErrorMessage = userMessage & vbCrLf & vbCrLf & _
                                  recoveryMessage & vbCrLf & vbCrLf & _
                                  details
End Function

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
    Dim fIpcSelected As String
    Dim fOpcSelected As String
    Dim fFlightReviewSelected As String
    Dim fRows    As String
    Dim fCrumb   As String
    fDate    = CStr(Range("neDate").Value)
    fType    = CStr(Range("neType").Value)
    fIpcSelected          = IIf(NewEntryBooleanValue("neIPC"), "Yes", "No")
    fOpcSelected          = IIf(NewEntryBooleanValue("neOPC"), "Yes", "No")
    fFlightReviewSelected = IIf(NewEntryBooleanValue("neFR"), "Yes", "No")
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
        Print #fileNum, "Workbook     : " & ThisWorkbook.Name
        Dim pathType As String
        If InStr(1, ThisWorkbook.Path, "OneDrive", vbTextCompare) > 0 Or _
           InStr(1, ThisWorkbook.Path, "sharepoint", vbTextCompare) > 0 Then
            pathType = "OneDrive/SharePoint"
        ElseIf Left(ThisWorkbook.Path, 2) = "\\" Then
            pathType = "Network"
        ElseIf Len(ThisWorkbook.Path) > 0 Then
            pathType = "Local"
        Else
            pathType = "Unknown"
        End If
        Print #fileNum, "WB location  : " & pathType
        Print #fileNum, "Log saved to : " & logPath
        Print #fileNum, "AutoSave     : " & ThisWorkbook.AutoSaveOn
        Print #fileNum, "LB Version   : " & version
        Print #fileNum, ""
        Print #fileNum, "-- ENTRY STATE ----------------------------------"
        Print #fileNum, "Date         : " & fDate
        Print #fileNum, "Type         : " & fType
        Print #fileNum, "IPC selected : " & fIpcSelected
        Print #fileNum, "OPC selected : " & fOpcSelected
        Print #fileNum, "FR selected  : " & fFlightReviewSelected
        Print #fileNum, "Logbook rows : " & fRows
        Print #fileNum, ""
    Close #fileNum

    On Error GoTo 0
End Sub

' ==============================================================
' EXPORT DIAGNOSTICS
' ==============================================================
' Generates a redacted diagnostics snapshot for support purposes.
' Contains structural/version information only -- no personal data,
' flight records, names, registrations, or file paths.

Public Sub ExportDiagnostics()
    Dim outPath As String
    If ExportDiagnosticsInternal(True, outPath) Then Exit Sub
End Sub

Private Function ExportDiagnosticsInternal(Optional showConfirmation As Boolean = True, _
                                           Optional ByRef exportedPath As String = "") As Boolean
    On Error Resume Next

    Dim logDir   As String
    Dim outPath  As String
    Dim fileNum  As Integer
    Dim version  As String
    Dim wb       As Workbook
    Set wb = ThisWorkbook

    ' Write alongside the workbook; fall back to Documents if path unavailable.
    logDir = ResolveLocalPath(wb)
    If logDir = "" Or Left(logDir, 4) = "http" Then
        logDir = Environ("USERPROFILE") & "\Documents\Electronic Logbook"
    End If
    If Dir(logDir, vbDirectory) = "" Then MkDir logDir
    outPath = logDir & "\diagnostics_" & Format(Now, "yyyymmdd_hhmmss") & ".txt"
    exportedPath = outPath

    version = Trim(CStr(wb.Names("LogbookVersion").RefersToRange.Value))
    If version = "" Then version = "Unknown"

    Dim pathType As String
    If InStr(1, wb.Path, "OneDrive", vbTextCompare) > 0 Or _
       InStr(1, wb.Path, "sharepoint", vbTextCompare) > 0 Then
        pathType = "OneDrive/SharePoint"
    ElseIf Left(wb.Path, 2) = "\\" Then
        pathType = "Network"
    ElseIf Len(wb.Path) > 0 Then
        pathType = "Local"
    Else
        pathType = "Unknown"
    End If

    Dim tbl       As ListObject
    Dim rowCount  As String
    Dim colNames  As String
    Dim c         As ListColumn
    Set tbl = wb.Sheets("Logbook").ListObjects("Logbook")
    If Not tbl Is Nothing Then
        rowCount = CStr(tbl.DataBodyRange.Rows.Count)
        For Each c In tbl.ListColumns
            colNames = colNames & c.Name & ", "
        Next c
        If Len(colNames) > 2 Then colNames = Left(colNames, Len(colNames) - 2)
    End If

    ' Named range inventory (non-sensitive values only).
    Dim gitBranch  As String
    Dim dateReset  As String
    Dim routesVer  As String
    Dim routesBlt  As String
    Dim routesDrty As String
    gitBranch  = Trim(CStr(wb.Names("GitHubBranch").RefersToRange.Value))
    dateReset  = Trim(CStr(wb.Names("DateAfterExport").RefersToRange.Value))
    routesVer  = Trim(CStr(wb.Names("RoutesDefinitionVersion").RefersToRange.Value))
    routesBlt  = Trim(CStr(wb.Names("RoutesBuilt").RefersToRange.Value))
    routesDrty = Trim(CStr(wb.Names("RoutesDirty").RefersToRange.Value))

    ' Keywords table row count.
    Dim kwCount As String
    Dim kwTbl   As ListObject
    Set kwTbl = wb.Sheets("Settings").ListObjects("Keywords")
    If Not kwTbl Is Nothing Then
        If Not kwTbl.DataBodyRange Is Nothing Then
            kwCount = CStr(kwTbl.DataBodyRange.Rows.Count)
        Else
            kwCount = "0"
        End If
    End If

    ' Warning suppression state (active/inactive, not the timestamp).
    Dim suppressState As String
    suppressState = "Inactive"
    Dim suppressVal As Variant
    suppressVal = wb.Names("suppressWarningsUntil").RefersToRange.Value
    If suppressVal <> "" Then
        If IsDate(suppressVal) Then
            If Now < CDate(suppressVal) Then suppressState = "Active"
        End If
    End If

    fileNum = FreeFile
    Open outPath For Output As #fileNum
        Print #fileNum, "Electronic Logbook - Diagnostics Snapshot"
        Print #fileNum, "Generated    : " & Format(Now, "yyyy-mm-dd hh:mm:ss")
        Print #fileNum, String(50, "-")
        Print #fileNum, ""
        Print #fileNum, "-- WORKBOOK --------------------------------------"
        Print #fileNum, "LB Version   : " & version
        Print #fileNum, "GitHub Branch: " & gitBranch
        Print #fileNum, "WB location  : " & pathType
        Print #fileNum, "AutoSave     : " & wb.AutoSaveOn
        Print #fileNum, ""
        Print #fileNum, "-- ENVIRONMENT -----------------------------------"
        Print #fileNum, "Excel        : " & Application.Version & " / " & Application.OperatingSystem
        Print #fileNum, ""
        Print #fileNum, "-- LOGBOOK TABLE ---------------------------------"
        Print #fileNum, "Row count    : " & rowCount
        Print #fileNum, "Columns      : " & colNames
        Print #fileNum, ""
        Print #fileNum, "-- NAMED PREFERENCES -----------------------------"
        Print #fileNum, "Date reset   : " & dateReset
        Print #fileNum, "Warn suppress: " & suppressState
        Print #fileNum, "Routes built : " & routesBlt
        Print #fileNum, "Routes dirty : " & routesDrty
        Print #fileNum, "Routes ver   : " & routesVer
        Print #fileNum, "Keywords     : " & kwCount & " row(s)"
        Print #fileNum, ""
        Print #fileNum, "-- PROTECTION / PIVOTS ---------------------------"
        PrintPivotProtectionDiagnostics fileNum, wb
        Print #fileNum, ""
        Print #fileNum, "-- NOTE ------------------------------------------"
        Print #fileNum, "This file contains no personal data, flight records,"
        Print #fileNum, "names, registrations, or file paths."
    Close #fileNum
    On Error GoTo 0

    If Dir(outPath) <> "" Then
        If showConfirmation Then
            MsgBox "Diagnostics saved to:" & vbCrLf & vbCrLf & outPath & vbCrLf & vbCrLf & _
                   "This file contains no personal data and is safe to share.", _
                   vbInformation, "Diagnostics Exported"
        End If
        ExportDiagnosticsInternal = True
    Else
        If showConfirmation Then
            MsgBox "Could not write the diagnostics file. Check folder permissions.", _
                   vbExclamation, "Export Failed"
        End If
        ExportDiagnosticsInternal = False
    End If
End Function

Private Sub PrintPivotProtectionDiagnostics(ByVal fileNum As Integer, ByVal wb As Workbook)
    Dim ws As Worksheet
    Dim pt As PivotTable
    Dim pivotCount As Long

    On Error Resume Next
    Print #fileNum, "Workbook protected: structure=" & CStr(wb.ProtectStructure) & _
                    ", windows=" & CStr(wb.ProtectWindows)
    For Each ws In wb.Worksheets
        pivotCount = 0
        pivotCount = ws.PivotTables.Count
        If ws.ProtectContents Or pivotCount > 0 Then
            Print #fileNum, "Sheet: " & ws.Name & _
                            " | protected=" & CStr(ws.ProtectContents) & _
                            " | pivots=" & CStr(pivotCount)
        End If
        If pivotCount > 0 Then
            For Each pt In ws.PivotTables
                Print #fileNum, "  Pivot: " & pt.Name & _
                                " | cache=" & CStr(pt.PivotCache.Index) & _
                                " | refreshOnOpen=" & CStr(pt.PivotCache.RefreshOnFileOpen)
            Next pt
        End If
    Next ws
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

Public Sub InitialiseNewEntryLayoutUI()
    Dim desiredLayout As Long

    desiredLayout = ResolveNewEntryLayoutId(GetWorkbookNameValue(ThisWorkbook, "NewEntryLayout", 1))
    ConfigureNewEntryLayoutControls
    ApplyConfiguredNewEntryLayout desiredLayout
End Sub

Public Sub SetNewEntryLayoutFromButtons()
    ApplyConfiguredNewEntryLayout ResolveNewEntryLayoutId( _
        GetWorkbookNameValue(ThisWorkbook, "NewEntryLayout", 1))
End Sub

Public Sub SetNewEntryLayout1()
    SetCompactView
End Sub

Public Sub SetNewEntryLayout2()
    SetGroupedView
End Sub

Public Sub SetGroupedView()
    ApplyConfiguredNewEntryLayout 2
End Sub

Public Sub SetCompactView()
    ApplyConfiguredNewEntryLayout 1
End Sub

Public Sub SetNewEntryLayoutCompactButton()
    ApplyConfiguredNewEntryLayout 1
End Sub

Public Sub SetNewEntryLayoutGroupedButton()
    ApplyConfiguredNewEntryLayout 2
End Sub

Public Sub ApplyConfiguredNewEntryLayout(Optional ByVal requestedLayout As Variant)
    Dim layoutId As Long
    Dim desiredCompact As Boolean
    Dim currentCompact As Boolean
    Dim layoutChanged As Boolean
    Dim fieldNames As Variant
    Dim fieldValues() As Variant
    Dim i As Long
    Dim nameText As String
    Dim dateAfterExportId As Long
    Dim previousScreenUpdating As Boolean
    Dim previousEnableEvents As Boolean
    Dim previousCalculation As XlCalculation

    If mApplyingNewEntryLayout Then Exit Sub
    mApplyingNewEntryLayout = True

    previousScreenUpdating = Application.ScreenUpdating
    previousEnableEvents = Application.EnableEvents
    previousCalculation = Application.Calculation

    On Error GoTo CleanFail
    Application.ScreenUpdating = False
    Application.EnableEvents = False
    Application.Calculation = xlCalculationManual

    layoutId = ResolveNewEntryLayoutId(requestedLayout)
    dateAfterExportId = ResolveDateAfterExportId( _
        GetWorkbookNameValue(ThisWorkbook, "DateAfterExport", 1))
    fieldNames = NewEntryLayoutFieldNames()
    RepairNewEntryLayoutNames fieldNames

    desiredCompact = (layoutId = 1)
    currentCompact = IsCurrentNewEntryLayoutCompact()
    layoutChanged = (desiredCompact <> currentCompact) Or _
                    (CurrentConfiguredNewEntryLayoutId() <> layoutId)

    ReDim fieldValues(LBound(fieldNames) To UBound(fieldNames))

    For i = LBound(fieldNames) To UBound(fieldNames)
        nameText = CStr(fieldNames(i))
        fieldValues(i) = GetWorkbookNameValue(ThisWorkbook, nameText, vbNullString)
    Next i

    If layoutChanged Then
        ApplyNewEntryLayoutBindingTargets layoutId, fieldNames
    Else
        RepairNewEntryLayoutNames fieldNames
    End If

    For i = LBound(fieldNames) To UBound(fieldNames)
        SetWorkbookNameValue ThisWorkbook, CStr(fieldNames(i)), fieldValues(i)
    Next i

    If CurrentConfiguredNewEntryLayoutId() <> layoutId Then
        SetWorkbookNameValue ThisWorkbook, "NewEntryLayout", layoutId
    End If

    SyncNewEntryLayoutButtons layoutId

    SetWorkbookNameValue ThisWorkbook, "DateAfterExport", dateAfterExportId
    ConfigureNewEntryLayoutControls
    EnforceNewEntrySheetRoles
    SetWorkbookNameValue ThisWorkbook, "DateAfterExport", dateAfterExportId
    If layoutChanged Then ActivateNewEntrySheet

CleanExit:
    Application.Calculation = previousCalculation
    Application.EnableEvents = previousEnableEvents
    Application.ScreenUpdating = previousScreenUpdating
    mApplyingNewEntryLayout = False
    Exit Sub

CleanFail:
    Resume CleanExit
End Sub

Public Sub ConfigureNewEntryLayoutControls()
    Dim ws As Worksheet
    Dim shp As Shape
    Dim compactBtn As Shape
    Dim groupedBtn As Shape
    Dim sheetName As Variant

    For Each sheetName In Array(NEW_ENTRY_ACTIVE_SHEET, NEW_ENTRY_UNUSED_SHEET)
        On Error Resume Next
        Set ws = ThisWorkbook.Sheets(CStr(sheetName))
        On Error GoTo 0
        If ws Is Nothing Then GoTo NextSheet

        For Each shp In ws.Shapes
            If shp.Type = msoFormControl Then
                Select Case shp.FormControlType
                    Case xlGroupBox
                        On Error Resume Next
                        shp.Line.Visible = msoFalse
                        shp.Fill.Visible = msoFalse
                        shp.OnAction = vbNullString
                        On Error GoTo 0
                    Case xlOptionButton
                        On Error Resume Next
                        shp.Line.Visible = msoFalse
                        On Error GoTo 0
                End Select
            End If
        Next shp

        FindNewEntryLayoutButtons ws, compactBtn, groupedBtn
        If Not compactBtn Is Nothing And Not groupedBtn Is Nothing Then
            On Error Resume Next
            compactBtn.ControlFormat.LinkedCell = vbNullString
            groupedBtn.ControlFormat.LinkedCell = vbNullString
            compactBtn.Name = "NewEntryLayoutCompactOption"
            groupedBtn.Name = "NewEntryLayoutGroupedOption"
            compactBtn.OnAction = "SetNewEntryLayoutCompactButton"
            groupedBtn.OnAction = "SetNewEntryLayoutGroupedButton"
            On Error GoTo 0
        End If

        ConfigureNewEntryDateResetControls ws
        ConfigureNewEntryCommandButtons ws

        RemoveRadioGroupOutlines ws

NextSheet:
        Set compactBtn = Nothing
        Set groupedBtn = Nothing
        Set ws = Nothing
    Next sheetName
End Sub

Private Sub ConfigureNewEntryDateResetControls(ByVal ws As Worksheet)
    Dim buttons As Collection
    Dim button As Shape
    Dim dateAfterExportId As Long

    Set buttons = FindDateAfterExportButtons(ws)
    If buttons.Count = 0 Then Exit Sub

    dateAfterExportId = ResolveDateAfterExportId( _
        GetWorkbookNameValue(ThisWorkbook, "DateAfterExport", 1))

    For Each button In buttons
        On Error Resume Next
        button.ControlFormat.LinkedCell = "DateAfterExport"
        On Error GoTo 0
    Next button

    SetWorkbookNameValue ThisWorkbook, "DateAfterExport", dateAfterExportId
End Sub

Private Function FindDateAfterExportButtons(ByVal ws As Worksheet) As Collection
    Dim buttons As Collection
    Dim shp As Shape

    Set buttons = New Collection

    For Each shp In ws.Shapes
        If IsDateAfterExportOptionButton(shp) Then buttons.Add shp
    Next shp

    Set FindDateAfterExportButtons = buttons
End Function

Private Function IsDateAfterExportOptionButton(ByVal shp As Shape) As Boolean
    Dim buttonId As String

    On Error GoTo Fail
    If shp.Type <> msoFormControl Then Exit Function
    If shp.FormControlType <> xlOptionButton Then Exit Function

    buttonId = LCase$(Trim$(shp.Name & " " & shp.OnAction & " " & _
                             ShapeText(shp) & " " & shp.ControlFormat.LinkedCell))
    IsDateAfterExportOptionButton = (InStr(buttonId, "dateafterexport") > 0 Or _
                                     InStr(buttonId, "resetdateto") > 0 Or _
                                     InStr(buttonId, "leavedateasis") > 0)
    Exit Function

Fail:
    IsDateAfterExportOptionButton = False
End Function

Private Function ResolveDateAfterExportId(ByVal candidate As Variant) As Long
    If IsNumeric(candidate) Then
        If CLng(candidate) >= 1 And CLng(candidate) <= 3 Then
            ResolveDateAfterExportId = CLng(candidate)
            Exit Function
        End If
    End If

    ResolveDateAfterExportId = 1
End Function

Private Sub ConfigureNewEntryCommandButtons(ByVal ws As Worksheet)
    Dim shp As Shape
    Dim item As Shape
    Dim actionName As String
    Dim nameText As String
    Dim labelText As String

    For Each shp In ws.Shapes
        actionName = vbNullString
        nameText = LCase$(Trim$(shp.Name))
        labelText = LCase$(Trim$(ShapeText(shp)))

        If InStr(nameText, "addtologbook") > 0 Or (InStr(labelText, "add to") > 0 And InStr(labelText, "logbook") > 0) Then
            actionName = "AddToLogbook"
        ElseIf InStr(nameText, "reportabug") > 0 Or InStr(labelText, "report a bug") > 0 Then
            actionName = "ReportBug"
        ElseIf InStr(nameText, "suppresswarnings") > 0 Or InStr(labelText, "suppress warnings") > 0 Then
            actionName = "ToggleSuppressWarnings"
        End If

        If Len(actionName) > 0 Then
            On Error Resume Next
            shp.OnAction = actionName
            If shp.Type = msoGroup Then
                For Each item In shp.GroupItems
                    item.OnAction = actionName
                Next item
            End If
            On Error GoTo 0
        End If
    Next shp
End Sub

Private Function ShapeText(ByVal shp As Shape) As String
    Dim item As Shape

    On Error Resume Next
    ShapeText = CStr(shp.TextFrame.Characters.Text)

    If Len(Trim$(ShapeText)) = 0 And shp.Type = msoGroup Then
        For Each item In shp.GroupItems
            ShapeText = CStr(item.TextFrame.Characters.Text)
            If Len(Trim$(ShapeText)) > 0 Then Exit For
        Next item
    End If

    On Error GoTo 0
End Function

Private Sub RemoveRadioGroupOutlines(ByVal ws As Worksheet)
    ' Attempt to suppress GroupBox borders. The etched/3D border is painted by
    ' Windows at the OS level, so VBA Line properties may not fully hide it.
    ' GroupBoxes must NOT be deleted - they provide independent grouping for the
    ' two separate sets of option buttons on this sheet.
    Dim shp As Shape
    Dim bgColor As Long

    On Error Resume Next
    bgColor = ws.Range("A1").Interior.Color
    If bgColor = 0 Then bgColor = RGB(255, 255, 255)
    On Error GoTo 0

    For Each shp In ws.Shapes
        If shp.Type = msoFormControl Then
            If shp.FormControlType = xlGroupBox Then
                On Error Resume Next
                shp.TextFrame.Characters.Text = ""
                shp.Line.Visible = msoFalse
                shp.Fill.Visible = msoFalse
                shp.Line.ForeColor.RGB = bgColor
                shp.Line.Transparency = 1
                On Error GoTo 0
            End If
        End If
    Next shp
End Sub

Private Sub SyncNewEntryLayoutButtons(ByVal layoutId As Long)
    Dim ws As Worksheet
    Dim compactBtn As Shape
    Dim groupedBtn As Shape
    Dim sheetName As Variant
    Dim compactAction As String
    Dim groupedAction As String

    For Each sheetName In Array(NEW_ENTRY_ACTIVE_SHEET, NEW_ENTRY_UNUSED_SHEET)
        On Error Resume Next
        Set ws = ThisWorkbook.Sheets(CStr(sheetName))
        On Error GoTo 0
        If ws Is Nothing Then GoTo NextSheet

        FindNewEntryLayoutButtons ws, compactBtn, groupedBtn
        If Not compactBtn Is Nothing And Not groupedBtn Is Nothing Then
            On Error Resume Next
            compactAction = compactBtn.OnAction
            groupedAction = groupedBtn.OnAction
            compactBtn.OnAction = vbNullString
            groupedBtn.OnAction = vbNullString

            compactBtn.ControlFormat.Value = IIf(layoutId = 1, xlOn, xlOff)
            groupedBtn.ControlFormat.Value = IIf(layoutId = 2, xlOn, xlOff)

            compactBtn.OnAction = compactAction
            groupedBtn.OnAction = groupedAction
            On Error GoTo 0
        End If

NextSheet:
        Set compactBtn = Nothing
        Set groupedBtn = Nothing
        Set ws = Nothing
    Next sheetName
End Sub

Private Sub FindNewEntryLayoutButtons(ByVal ws As Worksheet, ByRef compactBtn As Shape, ByRef groupedBtn As Shape)
    Dim shp As Shape
    Dim linkedLayoutButtons As Collection
    Dim remainingButtons As Collection
    Dim button As Shape

    Set linkedLayoutButtons = New Collection
    Set remainingButtons = New Collection

    For Each shp In ws.Shapes
        If IsNewEntryLayoutOptionButton(shp) Then
            If IsCompactLayoutButton(shp) Then
                Set compactBtn = shp
            ElseIf IsGroupedLayoutButton(shp) Then
                Set groupedBtn = shp
            ElseIf IsLinkedNewEntryLayoutButton(shp) Then
                linkedLayoutButtons.Add shp
            End If
        End If
    Next shp

    If compactBtn Is Nothing Or groupedBtn Is Nothing Then
        For Each button In linkedLayoutButtons
            If (compactBtn Is Nothing Or Not (button Is compactBtn)) And _
               (groupedBtn Is Nothing Or Not (button Is groupedBtn)) Then
                remainingButtons.Add button
            End If
        Next button

        If remainingButtons.Count >= 2 Then
            AssignLayoutButtonsByPosition remainingButtons.Item(1), remainingButtons.Item(2), compactBtn, groupedBtn
        End If
    End If
End Sub

Private Function IsNewEntryLayoutOptionButton(ByVal shp As Shape) As Boolean
    On Error GoTo Fail
    If shp.Type <> msoFormControl Then Exit Function
    If shp.FormControlType <> xlOptionButton Then Exit Function

    IsNewEntryLayoutOptionButton = IsCompactLayoutButton(shp) Or _
                                   IsGroupedLayoutButton(shp) Or _
                                   IsLinkedNewEntryLayoutButton(shp)
    Exit Function
Fail:
    IsNewEntryLayoutOptionButton = False
End Function

Private Function IsLinkedNewEntryLayoutButton(ByVal shp As Shape) As Boolean
    On Error Resume Next
    IsLinkedNewEntryLayoutButton = (LCase$(Trim$(shp.ControlFormat.LinkedCell)) = "newentrylayout")
    On Error GoTo 0
End Function

Private Function IsCompactLayoutButton(ByVal shp As Shape) As Boolean
    Dim buttonId As String

    buttonId = LCase$(Trim$(shp.Name & " " & shp.OnAction & " " & ShapeText(shp)))
    IsCompactLayoutButton = (InStr(buttonId, "compact") > 0 Or _
                             InStr(buttonId, "setcompactview") > 0)
End Function

Private Function IsGroupedLayoutButton(ByVal shp As Shape) As Boolean
    Dim buttonId As String

    buttonId = LCase$(Trim$(shp.Name & " " & shp.OnAction & " " & ShapeText(shp)))
    IsGroupedLayoutButton = (InStr(buttonId, "grouped") > 0 Or _
                             InStr(buttonId, "setgroupedview") > 0)
End Function

Private Sub AssignLayoutButtonsByPosition(ByVal buttonA As Shape, ByVal buttonB As Shape, _
                                          ByRef compactBtn As Shape, ByRef groupedBtn As Shape)
    If buttonA.Left <= buttonB.Left Then
        If compactBtn Is Nothing Then Set compactBtn = buttonA
        If groupedBtn Is Nothing Then Set groupedBtn = buttonB
    Else
        If compactBtn Is Nothing Then Set compactBtn = buttonB
        If groupedBtn Is Nothing Then Set groupedBtn = buttonA
    End If
End Sub

Private Function CurrentConfiguredNewEntryLayoutId() As Long
    CurrentConfiguredNewEntryLayoutId = ResolveNewEntryLayoutId( _
        GetWorkbookNameValue(ThisWorkbook, "NewEntryLayout", 1))
End Function

Private Function IsCurrentNewEntryLayoutCompact() As Boolean
    Dim ws As Worksheet
    Dim inputCell As Range

    On Error Resume Next
    Set ws = ThisWorkbook.Sheets(NEW_ENTRY_ACTIVE_SHEET)
    Set inputCell = FindNameByBase("neSeIcusDay").RefersToRange
    On Error GoTo 0

    If Not inputCell Is Nothing Then
        If inputCell.Worksheet.Name = NEW_ENTRY_ACTIVE_SHEET Then
            If inputCell.Row <= 18 And inputCell.Column >= 15 Then
                IsCurrentNewEntryLayoutCompact = True
                Exit Function
            End If

            If inputCell.Row >= 20 And inputCell.Column <= 6 Then
                IsCurrentNewEntryLayoutCompact = False
                Exit Function
            End If
        End If
    End If

    If Not ws Is Nothing Then IsCurrentNewEntryLayoutCompact = IsNewEntrySheetCompact(ws)
End Function

Private Function IsNewEntrySheetCompact(ByVal ws As Worksheet) As Boolean
    Dim layoutKind As String

    layoutKind = NewEntrySheetLayoutKind(ws)
    If StrComp(layoutKind, NEW_ENTRY_LAYOUT_COMPACT, vbTextCompare) = 0 Then
        IsNewEntrySheetCompact = True
        Exit Function
    End If

    If StrComp(layoutKind, NEW_ENTRY_LAYOUT_GROUPED, vbTextCompare) = 0 Then
        IsNewEntrySheetCompact = False
        Exit Function
    End If

    BootstrapNewEntryLayoutMarkers
    layoutKind = NewEntrySheetLayoutKind(ws)
    IsNewEntrySheetCompact = (StrComp(layoutKind, NEW_ENTRY_LAYOUT_COMPACT, vbTextCompare) = 0)
End Function

Private Sub ApplyNewEntryLayoutBindingTargets(ByVal layoutId As Long, ByVal fieldNames As Variant)
    Dim compactSheet As Worksheet
    Dim groupedSheet As Worksheet
    Dim primarySheet As Worksheet
    Dim secondarySheet As Worksheet

    ResolveNewEntryLayoutSheets compactSheet, groupedSheet
    If compactSheet Is Nothing Or groupedSheet Is Nothing Then Exit Sub

    If ResolveNewEntryLayoutId(layoutId) = 1 Then
        Set primarySheet = compactSheet
        Set secondarySheet = groupedSheet
    Else
        Set primarySheet = groupedSheet
        Set secondarySheet = compactSheet
    End If

    SetNewEntryLayoutNameTargets fieldNames, primarySheet, secondarySheet
End Sub

Private Sub RepairNewEntryLayoutNames(ByVal fieldNames As Variant)
    Dim activeSheet As Worksheet
    Dim inactiveSheet As Worksheet

    On Error Resume Next
    Set activeSheet = ThisWorkbook.Sheets(NEW_ENTRY_ACTIVE_SHEET)
    Set inactiveSheet = ThisWorkbook.Sheets(NEW_ENTRY_UNUSED_SHEET)
    On Error GoTo 0

    If activeSheet Is Nothing Or inactiveSheet Is Nothing Then Exit Sub

    BootstrapNewEntryLayoutMarkers
    SetNewEntryLayoutNameTargets fieldNames, activeSheet, inactiveSheet
End Sub

Private Sub ResolveNewEntryLayoutSheets(ByRef compactSheet As Worksheet, ByRef groupedSheet As Worksheet)
    Dim activeSheet As Worksheet
    Dim inactiveSheet As Worksheet

    On Error Resume Next
    Set activeSheet = ThisWorkbook.Sheets(NEW_ENTRY_ACTIVE_SHEET)
    Set inactiveSheet = ThisWorkbook.Sheets(NEW_ENTRY_UNUSED_SHEET)
    On Error GoTo 0

    If activeSheet Is Nothing Or inactiveSheet Is Nothing Then Exit Sub

    BootstrapNewEntryLayoutMarkers

    If IsNewEntrySheetCompact(activeSheet) Then
        Set compactSheet = activeSheet
    Else
        Set groupedSheet = activeSheet
    End If

    If IsNewEntrySheetCompact(inactiveSheet) Then
        Set compactSheet = inactiveSheet
    Else
        Set groupedSheet = inactiveSheet
    End If
End Sub

Private Sub SetNewEntryLayoutNameTargets(ByVal fieldNames As Variant, ByVal primarySheet As Worksheet, ByVal secondarySheet As Worksheet)
    Dim i As Long
    Dim nameText As String
    Dim primaryTarget As Range
    Dim secondaryTarget As Range
    Dim activeNameTargets As Object
    Dim secondaryNameTargets As Object

    Set activeNameTargets = CaptureNewEntryNameTargets(fieldNames, False)
    Set secondaryNameTargets = CaptureNewEntryNameTargets(fieldNames, True)

    For i = LBound(fieldNames) To UBound(fieldNames)
        nameText = CStr(fieldNames(i))
        Set primaryTarget = NewEntryLayoutTargetForSheet(nameText, primarySheet, activeNameTargets, secondaryNameTargets)
        Set secondaryTarget = NewEntryLayoutTargetForSheet(nameText, secondarySheet, activeNameTargets, secondaryNameTargets)

        If Not primaryTarget Is Nothing Then EnsureWorkbookNameRefersTo nameText, primaryTarget

        If Not secondaryTarget Is Nothing Then EnsureSecondaryNameRefersTo nameText & "2", secondaryTarget
    Next i
End Sub

Private Sub EnsureWorkbookNameRefersTo(ByVal nameText As String, ByVal targetRange As Range)
    Dim nm As Name
    Dim existingName As Name

    On Error Resume Next
    Set nm = ThisWorkbook.Names(nameText)
    On Error GoTo 0

    If nm Is Nothing Then
        ThisWorkbook.Names.Add Name:=nameText, RefersTo:=NameRefersToRange(targetRange)
    Else
        nm.RefersTo = NameRefersToRange(targetRange)
    End If

    For Each existingName In ThisWorkbook.Names
        If LCase$(NameBaseText(existingName.Name)) = LCase$(nameText) _
            And LCase$(existingName.Name) <> LCase$(nameText) Then
            existingName.RefersTo = NameRefersToRange(targetRange)
        End If
    Next existingName
End Sub

Private Sub EnsureSecondaryNameRefersTo(ByVal nameText As String, ByVal targetRange As Range)
    Dim nm As Name

    Set nm = FindNameByBase(nameText)
    If nm Is Nothing Then
        ThisWorkbook.Names.Add Name:=nameText, RefersTo:=NameRefersToRange(targetRange)
    Else
        nm.RefersTo = NameRefersToRange(targetRange)
    End If
End Sub

Private Function NameRefersToRange(ByVal targetRange As Range) As String
    NameRefersToRange = "='" & targetRange.Worksheet.Name & "'!" & targetRange.Address(True, True)
End Function

Private Sub BootstrapNewEntryLayoutMarkers()
    Dim activeSheet As Worksheet
    Dim inactiveSheet As Worksheet
    Dim activeKind As String
    Dim inactiveKind As String
    Dim configuredLayout As Long

    On Error Resume Next
    Set activeSheet = ThisWorkbook.Sheets(NEW_ENTRY_ACTIVE_SHEET)
    Set inactiveSheet = ThisWorkbook.Sheets(NEW_ENTRY_UNUSED_SHEET)
    On Error GoTo 0
    If activeSheet Is Nothing Or inactiveSheet Is Nothing Then Exit Sub

    activeKind = NewEntrySheetLayoutKind(activeSheet)
    inactiveKind = NewEntrySheetLayoutKind(inactiveSheet)

    If NewEntryLayoutKindIsValid(activeKind) And NewEntryLayoutKindIsValid(inactiveKind) Then Exit Sub

    If NewEntryLayoutKindIsValid(activeKind) Then
        SetNewEntrySheetLayoutKind inactiveSheet, OppositeNewEntryLayoutKind(activeKind)
        Exit Sub
    End If

    If NewEntryLayoutKindIsValid(inactiveKind) Then
        SetNewEntrySheetLayoutKind activeSheet, OppositeNewEntryLayoutKind(inactiveKind)
        Exit Sub
    End If

    configuredLayout = CurrentConfiguredNewEntryLayoutId()
    If configuredLayout = 1 Then
        SetNewEntrySheetLayoutKind activeSheet, NEW_ENTRY_LAYOUT_COMPACT
        SetNewEntrySheetLayoutKind inactiveSheet, NEW_ENTRY_LAYOUT_GROUPED
    Else
        SetNewEntrySheetLayoutKind activeSheet, NEW_ENTRY_LAYOUT_GROUPED
        SetNewEntrySheetLayoutKind inactiveSheet, NEW_ENTRY_LAYOUT_COMPACT
    End If
End Sub

Private Function NewEntrySheetLayoutKind(ByVal ws As Worksheet) As String
    Dim nm As Name
    Dim refersToText As String

    On Error Resume Next
    Set nm = ws.Names(NEW_ENTRY_LAYOUT_MARKER_NAME)
    On Error GoTo 0
    If nm Is Nothing Then Exit Function

    refersToText = Trim$(nm.RefersTo)
    If Left$(refersToText, 2) = "=""" And Right$(refersToText, 1) = """" Then
        NewEntrySheetLayoutKind = Mid$(refersToText, 3, Len(refersToText) - 3)
    End If
End Function

Private Sub SetNewEntrySheetLayoutKind(ByVal ws As Worksheet, ByVal layoutKind As String)
    Dim nm As Name
    Dim refersToText As String

    If Not NewEntryLayoutKindIsValid(layoutKind) Then Exit Sub
    refersToText = "=""" & layoutKind & """"

    On Error Resume Next
    Set nm = ws.Names(NEW_ENTRY_LAYOUT_MARKER_NAME)
    If nm Is Nothing Then
        ws.Names.Add Name:=NEW_ENTRY_LAYOUT_MARKER_NAME, RefersTo:=refersToText
    Else
        nm.RefersTo = refersToText
    End If
    On Error GoTo 0
End Sub

Private Function NewEntryLayoutKindIsValid(ByVal layoutKind As String) As Boolean
    NewEntryLayoutKindIsValid = (StrComp(layoutKind, NEW_ENTRY_LAYOUT_COMPACT, vbTextCompare) = 0 Or _
                                 StrComp(layoutKind, NEW_ENTRY_LAYOUT_GROUPED, vbTextCompare) = 0)
End Function

Private Function OppositeNewEntryLayoutKind(ByVal layoutKind As String) As String
    If StrComp(layoutKind, NEW_ENTRY_LAYOUT_COMPACT, vbTextCompare) = 0 Then
        OppositeNewEntryLayoutKind = NEW_ENTRY_LAYOUT_GROUPED
    Else
        OppositeNewEntryLayoutKind = NEW_ENTRY_LAYOUT_COMPACT
    End If
End Function

Private Function CaptureNewEntryNameTargets(ByVal fieldNames As Variant, ByVal secondaryNames As Boolean) As Object
    Dim targets As Object
    Dim i As Long
    Dim nameText As String
    Dim nm As Name
    Dim targetRange As Range

    Set targets = CreateObject("Scripting.Dictionary")
    targets.CompareMode = 1

    For i = LBound(fieldNames) To UBound(fieldNames)
        nameText = CStr(fieldNames(i))
        If secondaryNames Then nameText = nameText & "2"

        Set nm = FindNameByBase(nameText)
        Set targetRange = Nothing
        If Not nm Is Nothing Then
            On Error Resume Next
            Set targetRange = nm.RefersToRange
            On Error GoTo 0
            If Not targetRange Is Nothing Then Set targets(CStr(fieldNames(i))) = targetRange
        End If
    Next i

    Set CaptureNewEntryNameTargets = targets
End Function

Private Function NewEntryLayoutTargetForSheet(ByVal fieldName As String, _
                                              ByVal targetSheet As Worksheet, _
                                              ByVal activeNameTargets As Object, _
                                              ByVal secondaryNameTargets As Object) As Range
    Dim targetRange As Range

    If activeNameTargets.Exists(fieldName) Then
        Set targetRange = activeNameTargets(fieldName)
        If targetRange.Worksheet Is targetSheet Then
            Set NewEntryLayoutTargetForSheet = targetRange
            Exit Function
        End If
    End If

    If secondaryNameTargets.Exists(fieldName) Then
        Set targetRange = secondaryNameTargets(fieldName)
        If targetRange.Worksheet Is targetSheet Then
            Set NewEntryLayoutTargetForSheet = targetRange
            Exit Function
        End If
    End If
End Function

Private Sub SwapNewEntryLayoutBindings(ByVal fieldNames As Variant)
    ApplyNewEntryLayoutBindingTargets IIf(IsCurrentNewEntryLayoutCompact(), 2, 1), fieldNames
End Sub

Private Function FindNameByBase(ByVal baseName As String) As Name
    Dim nm As Name
    Dim probe As String
    Dim firstMatch As Name

    probe = LCase$(baseName)

    For Each nm In ThisWorkbook.Names
        If LCase$(nm.Name) = probe Then
            Set FindNameByBase = nm
            Exit Function
        End If

        If LCase$(NameBaseText(nm.Name)) = probe Then
            If firstMatch Is Nothing Then Set firstMatch = nm
        End If
    Next nm

    Set FindNameByBase = firstMatch
End Function

Private Function NameBaseText(ByVal fullName As String) As String
    If InStrRev(fullName, "!") > 0 Then
        NameBaseText = Mid$(fullName, InStrRev(fullName, "!") + 1)
    Else
        NameBaseText = fullName
    End If
End Function

Private Sub EnforceNewEntrySheetRoles()
    Dim activeSheet As Worksheet
    Dim inactiveSheet As Worksheet
    Dim wbWasProtected As Boolean

    Set activeSheet = ResolveSheetFromWorkbookName("neDate")
    Set inactiveSheet = ResolveSheetFromNameBase("neDate2")
    If activeSheet Is Nothing Or inactiveSheet Is Nothing Then Exit Sub

    wbWasProtected = ThisWorkbook.ProtectStructure
    On Error Resume Next
    If wbWasProtected Then ThisWorkbook.Unprotect Password:=ProtectionPassword()

    If activeSheet.Name <> NEW_ENTRY_ACTIVE_SHEET Or inactiveSheet.Name <> NEW_ENTRY_UNUSED_SHEET Then
        activeSheet.Name = NEW_ENTRY_SWAP_TEMP_SHEET
        inactiveSheet.Name = NEW_ENTRY_UNUSED_SHEET
        activeSheet.Name = NEW_ENTRY_ACTIVE_SHEET
    End If

    ThisWorkbook.Sheets(NEW_ENTRY_ACTIVE_SHEET).Visible = xlSheetVisible
    ThisWorkbook.Sheets(NEW_ENTRY_UNUSED_SHEET).Visible = xlSheetVeryHidden

    If wbWasProtected Then
        ThisWorkbook.Protect Password:=ProtectionPassword(), Structure:=True, Windows:=False
    End If
    On Error GoTo 0
End Sub

Private Function ResolveSheetFromWorkbookName(ByVal nameText As String) As Worksheet
    Dim nm As Name
    On Error Resume Next
    Set nm = ThisWorkbook.Names(nameText)
    On Error GoTo 0
    If nm Is Nothing Then Exit Function

    Set ResolveSheetFromWorkbookName = ResolveSheetFromRefersTo(nm.RefersTo)
End Function

Private Function ResolveSheetFromNameBase(ByVal baseName As String) As Worksheet
    Dim nm As Name
    Set nm = FindNameByBase(baseName)
    If nm Is Nothing Then Exit Function

    Set ResolveSheetFromNameBase = ResolveSheetFromRefersTo(nm.RefersTo)
End Function

Private Function ResolveSheetFromRefersTo(ByVal refersToText As String) As Worksheet
    Dim bangPos As Long
    Dim sheetToken As String

    bangPos = InStr(refersToText, "!")
    If bangPos <= 0 Then Exit Function

    sheetToken = Left$(refersToText, bangPos - 1)
    sheetToken = Replace(sheetToken, "=", "")
    sheetToken = Replace(sheetToken, "'", "")

    On Error Resume Next
    Set ResolveSheetFromRefersTo = ThisWorkbook.Sheets(sheetToken)
    On Error GoTo 0
End Function

Private Sub ActivateNewEntrySheet()
    On Error Resume Next
    ThisWorkbook.Sheets(NEW_ENTRY_ACTIVE_SHEET).Activate
    On Error GoTo 0
End Sub

Private Function ResolveNewEntryLayoutId(ByVal requestedLayout As Variant) As Long
    Dim candidate As Variant

    candidate = requestedLayout
    If IsEmpty(candidate) Then
        candidate = GetWorkbookNameValue(ThisWorkbook, "NewEntryLayout", 2)
    End If

    If Not IsNumeric(candidate) Then
        ResolveNewEntryLayoutId = 1
        Exit Function
    End If

    If CLng(candidate) = 1 Then
        ResolveNewEntryLayoutId = 1
    Else
        ResolveNewEntryLayoutId = 2
    End If
End Function

Private Function NewEntryLayoutFieldNames() As Variant
    NewEntryLayoutFieldNames = Array( _
        "neYear", "neMonth", "neDay", "neDate", "neReg", "neType", "neFlightID", "nePIC", "neOtherCrew", _
        "neFR", "neIPC", "neOPC", "neFrom", "neTo", "neVia", "neDetails", "neRemarks", _
        "neSeIcusDay", "neSeIcusNight", "neSeDualDay", "neSeDualNight", "neSeCommandDay", "neSeCommandNight", _
        "neMeIcusDay", "neMeIcusNight", "neMeDualDay", "neMeDualNight", "neMeCommandDay", "neMeCommandNight", _
        "neCopilotDay", "neCopilotNight", "neIfrIf", "neIfrSim", "neLandingsDay", "neLandingsNight", _
        "neILS", "neRNP", "neRNAV", "neNDB", "neVOR", "neDgaCdi", "neDgaAzi", "neCircling", _
        "neSI1", "neSI2", "neSI3", "neSI4")
End Function

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

' ==============================================================
' WORKBOOK PROTECTION
' ==============================================================

Public Sub EnsureWorkbookProtectionOnOpen()
    If WorkbookProtectionDisabledByBranch(ThisWorkbook) Then
        UnprotectWorkbookForEditing
        mProtectionDisabledForSession = True
        Exit Sub
    End If

    mProtectionDisabledForSession = False
    ApplyWorkbookProtection False
End Sub

Public Sub EnableProtectionForRelease()
    SetWorkbookNameValue ThisWorkbook, "GitHubBranch", "main"
    mProtectionDisabledForSession = False
    ApplyWorkbookProtection True
End Sub

Public Sub DisableProtectionForDevelopment()
    SetWorkbookNameValue ThisWorkbook, "GitHubBranch", "dev"
    UnprotectWorkbookForEditing
    mProtectionDisabledForSession = True
    MsgBox "Workbook protection has been disabled while GitHubBranch is set to 'dev'." & vbCrLf & vbCrLf & _
           "Run EnableProtectionForRelease to set GitHubBranch back to 'main' and re-enable protection.", _
           vbInformation, "Development Mode Enabled"
End Sub

Private Function WorkbookProtectionDisabledByBranch(wb As Workbook) As Boolean
    Dim branchValue As String

    branchValue = LCase$(Trim$(CStr(GetWorkbookNameValue(wb, "GitHubBranch", ""))))
    WorkbookProtectionDisabledByBranch = (branchValue = "dev")
End Function

Private Sub ApplyWorkbookProtection(Optional showConfirmation As Boolean = False, Optional targetWorkbook As Workbook = Nothing)
    Dim wb As Workbook
    Dim ws As Worksheet
    Dim wsLog As Worksheet
    Dim wsCharts As Worksheet
    Dim tbl As ListObject

    If targetWorkbook Is Nothing Then
        Set wb = ThisWorkbook
    Else
        Set wb = targetWorkbook
    End If

    UnprotectWorkbookForEditing wb
    EnsurePrimarySheetOrder wb
    DisableWorkbookPivotRefreshOnOpen wb

    For Each ws In wb.Worksheets
        On Error Resume Next
        ws.Cells.Locked = True
        On Error GoTo 0
    Next ws

    UnlockNamedRangesByPrefix wb, "ne"
    UnlockNamedRangeIfPresent wb, "DateAfterExport"
    UnlockNamedRangeIfPresent wb, "NewEntryLayout"
    UnlockNamedRangeIfPresent wb, "FROverride"
    UnlockNamedRangeIfPresent wb, "IPCOverride"
    UnlockNamedRangeIfPresent wb, "OPCOverride"
    UnlockListColumnDataIfPresent wb, "BaseAirportsTop10", "Base"

    On Error Resume Next
    Set wsLog = wb.Sheets("Logbook")
    On Error GoTo 0
    If Not wsLog Is Nothing Then
        On Error Resume Next
        Set tbl = wsLog.ListObjects("Logbook")
        On Error GoTo 0

        If Not tbl Is Nothing Then
            On Error Resume Next
            If Not tbl.DataBodyRange Is Nothing Then tbl.DataBodyRange.Locked = False
            UnlockLogbookRowsForDeletion wsLog
            If Not tbl.HeaderRowRange Is Nothing Then tbl.HeaderRowRange.Locked = False
            On Error GoTo 0
        End If
    End If

    For Each ws In wb.Worksheets
        On Error Resume Next
        If LCase$(ws.Name) = "logbook" Then
            ProtectLogbookSheetForRuntime ws
        ElseIf LCase$(ws.Name) = "stats" Then
            ws.Protect Password:=ProtectionPassword(), DrawingObjects:=False, Contents:=True, Scenarios:=True, _
                       UserInterfaceOnly:=True, AllowUsingPivotTables:=True, _
                       AllowFormattingColumns:=True, AllowFormattingRows:=True
        Else
            ProtectStandardWorksheetForRuntime ws
        End If
        On Error GoTo 0
    Next ws

    On Error Resume Next
    wb.Protect Password:=ProtectionPassword(), Structure:=True, Windows:=False
    On Error GoTo 0

    If showConfirmation Then
        MsgBox "Workbook protection has been enabled for release mode.", vbInformation, "Protection Enabled"
    End If

    Set tbl = Nothing
    Set wsLog = Nothing
    Set wsCharts = Nothing
    Set wb = Nothing
End Sub

Private Sub ProtectStandardWorksheetForRuntime(ws As Worksheet)
    ws.Protect Password:=ProtectionPassword(), DrawingObjects:=False, Contents:=True, Scenarios:=True, _
               UserInterfaceOnly:=True, AllowUsingPivotTables:=True
End Sub

Private Sub UnlockListColumnDataIfPresent(ByVal wb As Workbook, ByVal tableName As String, ByVal columnName As String)
    Dim ws As Worksheet
    Dim tbl As ListObject

    On Error Resume Next
    For Each ws In wb.Worksheets
        Set tbl = Nothing
        Set tbl = ws.ListObjects(tableName)
        If Not tbl Is Nothing Then
            If Not tbl.DataBodyRange Is Nothing Then
                tbl.ListColumns(columnName).DataBodyRange.Locked = False
            End If
            Exit For
        End If
    Next ws
    On Error GoTo 0
End Sub

Private Function LogbookRouteSourceChanged(ByVal ws As Worksheet, ByVal changedRange As Range) As Boolean
    Dim tbl As ListObject
    Dim columnName As Variant
    Dim routeColumns As Variant

    On Error Resume Next
    Set tbl = ws.ListObjects("Logbook")
    On Error GoTo 0
    If tbl Is Nothing Then Exit Function
    If tbl.DataBodyRange Is Nothing Then Exit Function

    routeColumns = Array("From", "Via", "To", "Remarks", "Details", _
                         "SeIcusDay", "SeIcusNight", "SeDualDay", "SeDualNight", _
                         "MeIcusDay", "MeIcusNight", "MeDualDay", "MeDualNight", _
                         "PIC", "CopilotDay", "CopilotNight", "IfrIf", "IfrSim")

    For Each columnName In routeColumns
        If TableColumnDataIntersects(tbl, CStr(columnName), changedRange) Then
            LogbookRouteSourceChanged = True
            Exit Function
        End If
    Next columnName
End Function

Private Function AirportRouteLookupChanged(ByVal ws As Worksheet, ByVal changedRange As Range) As Boolean
    Dim tbl As ListObject
    Dim columnName As Variant

    On Error Resume Next
    Set tbl = ws.ListObjects("Airports")
    On Error GoTo 0
    If tbl Is Nothing Then Exit Function
    If tbl.DataBodyRange Is Nothing Then Exit Function

    For Each columnName In Array("ICAO", "Two", "Three")
        If TableColumnDataIntersects(tbl, CStr(columnName), changedRange) Then
            AirportRouteLookupChanged = True
            Exit Function
        End If
    Next columnName
End Function

Private Function KeywordRouteIgnoreListChanged(ByVal ws As Worksheet, ByVal changedRange As Range) As Boolean
    Dim tbl As ListObject

    On Error Resume Next
    Set tbl = ws.ListObjects("Keywords")
    On Error GoTo 0
    If tbl Is Nothing Then Exit Function
    If tbl.DataBodyRange Is Nothing Then Exit Function

    KeywordRouteIgnoreListChanged = Not Intersect(changedRange, tbl.DataBodyRange) Is Nothing
End Function

Private Function TableColumnDataIntersects(ByVal tbl As ListObject, _
                                           ByVal columnName As String, _
                                           ByVal targetRange As Range) As Boolean
    If Not ListColumnExists(tbl, columnName) Then Exit Function
    If tbl.ListColumns(columnName).DataBodyRange Is Nothing Then Exit Function

    TableColumnDataIntersects = Not Intersect(targetRange, tbl.ListColumns(columnName).DataBodyRange) Is Nothing
End Function

Private Sub LockLogbookDateColumn(ByVal ws As Worksheet)
    Dim tbl As ListObject

    If LCase$(ws.Name) <> "logbook" Then Exit Sub

    On Error Resume Next
    Set tbl = ws.ListObjects("Logbook")
    If Not tbl Is Nothing Then
        If Not tbl.DataBodyRange Is Nothing Then
            tbl.ListColumns("Date").DataBodyRange.Locked = True
        End If
    End If
    On Error GoTo 0
End Sub

Private Sub UnlockLogbookRowsForDeletion(ByVal ws As Worksheet)
    Dim tbl As ListObject

    If LCase$(ws.Name) <> "logbook" Then Exit Sub

    On Error Resume Next
    Set tbl = ws.ListObjects("Logbook")
    If Not tbl Is Nothing Then
        If Not tbl.DataBodyRange Is Nothing Then tbl.DataBodyRange.EntireRow.Locked = False
    End If
    On Error GoTo 0
End Sub

Private Sub EnsurePrimarySheetOrder(wb As Workbook)
    Dim wsHelp As Worksheet
    Dim wsLast As Worksheet
    Dim wsActive As Worksheet
    Dim activeAddress As String

    On Error Resume Next
    If TypeName(ActiveSheet) = "Worksheet" Then Set wsActive = ActiveSheet
    If Not wsActive Is Nothing Then
        If wsActive.Parent Is wb Then
            activeAddress = ActiveCell.Address(False, False)
        Else
            Set wsActive = Nothing
        End If
    End If
    On Error GoTo 0

    On Error Resume Next
    Set wsHelp = wb.Worksheets("Help")
    On Error GoTo 0

    If wsHelp Is Nothing Then Exit Sub

    Set wsLast = wb.Worksheets(wb.Worksheets.Count)

    On Error Resume Next
    If wsHelp.Index <> wsLast.Index Then
        wsHelp.Move After:=wsLast
    End If
    If Not wsActive Is Nothing Then
        wsActive.Activate
        If activeAddress <> "" Then wsActive.Range(activeAddress).Select
    End If
    On Error GoTo 0

    Set wsHelp = Nothing
    Set wsLast = Nothing
    Set wsActive = Nothing
End Sub

Private Sub UnprotectWorkbookForEditing(Optional targetWorkbook As Workbook = Nothing)
    Dim wb As Workbook
    Dim ws As Worksheet

    If targetWorkbook Is Nothing Then
        Set wb = ThisWorkbook
    Else
        Set wb = targetWorkbook
    End If

    On Error Resume Next
    wb.Unprotect Password:=ProtectionPassword()
    For Each ws In wb.Worksheets
        ws.Unprotect Password:=ProtectionPassword()
    Next ws
    On Error GoTo 0

    Set wb = Nothing
End Sub

Private Sub UnlockNamedRangesByPrefix(wb As Workbook, prefix As String)
    Dim nm As Name
    Dim nmText As String
    Dim baseName As String
    Dim targetRange As Range

    For Each nm In wb.Names
        nmText = LCase$(nm.Name)
        baseName = nmText

        If InStrRev(baseName, "!") > 0 Then
            baseName = Mid$(baseName, InStrRev(baseName, "!") + 1)
        End If

        If Left$(baseName, Len(prefix)) = LCase$(prefix) Then
            On Error Resume Next
            Set targetRange = nm.RefersToRange
            If Not targetRange Is Nothing Then
                targetRange.Locked = False
                If targetRange.MergeCells Then
                    targetRange.MergeArea.Locked = False
                End If
            End If
            On Error GoTo 0
            Set targetRange = Nothing
        End If
    Next nm
End Sub

Private Sub UnlockNamedRangeIfPresent(wb As Workbook, nameText As String)
    Dim targetRange As Range
    On Error Resume Next
    Set targetRange = wb.Names(nameText).RefersToRange
    If Not targetRange Is Nothing Then
        targetRange.Locked = False
        If targetRange.MergeCells Then
            targetRange.MergeArea.Locked = False
        End If
    End If
    On Error GoTo 0
    Set targetRange = Nothing
End Sub

Private Function ProtectionPassword() As String
    ' Keep blank by default to avoid password prompts during development.
    ' Set a value here if you want password-protected unprotect operations.
    ProtectionPassword = ""
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
