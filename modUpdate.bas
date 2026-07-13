Attribute VB_Name = "modUpdate"
' ==============================================================
' modUpdate - Auto-update system for Electronic Logbook
' ==============================================================

Option Explicit

Private mResolvedRef As String
Private mLastUpdateFailureReason As String
Private Const ROUTE_CACHE_DEFINITION_VERSION As Long = 1
Private Const LOGBOOK_ACTION_BUTTON_WIDTH As Double = 121.2
Private Const LOGBOOK_ACTION_BUTTON_HEIGHT As Double = 45
Private Const LOGBOOK_ACTION_BUTTON_POSITION_TOLERANCE As Double = 1

' -- GITHUB CONFIG --------------------------------------------
Private Const GITHUB_USER  As String = "alphadelta332"
Private Const GITHUB_REPO  As String = "Electronic-Logbook"
Private Const MASTER_FILE  As String = "Electronic_Logbook_Master.xlsm"
Private Const WIZARD_EXE_NAME As String = "ElectronicLogbook.Updater.Wizard.exe"
Private Const WIZARD_ZIP_NAME As String = "ElectronicLogbook.Updater.Wizard.win-x64.zip"
Private Const DEV_WIZARD_TAG_PREFIX As String = "dev-wizard-"
Private Const DEV_WIZARD_COMMIT_NAME As String = "dev-wizard-commit.txt"
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
'   Legacy Logbook[Details] split into From, To, Via, and Remarks
'   Legacy currency detection helper columns converted to FR, IPC, and OPC
'   BaseAirportsTop10                         (matched by ICAO)
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
    Dim canonicalName As String
    Dim oldPath       As String
    Dim updatedPath   As String
    Dim masterWb      As Workbook
    Dim errMsg        As String
    Dim errNum        As Long
    Dim diagStep      As String
    Dim finalHandoffStarted As Boolean
    Dim finalReady As Boolean
    Dim readinessNote As String
    Dim sessionId     As String
    Dim expectedRows  As Long
    Dim expectedTotalHours As Double
    Dim expectedTotalKnown As Boolean
    Dim diagnosticsPath As String
    Dim wizardReportPath As String
    Dim sourceWorkbookPath As String
    Dim wizardReason As String
    Dim wizardMasterPath As String
    Dim releaseChannel As Boolean

    ' Unique per-run filenames prevent stale leftovers from a prior failed update
    ' from being silently used as the staging input.
    sessionId = Format(Now, "yyyymmdd_hhmmss")
    mLastUpdateFailureReason = ""
    On Error Resume Next
    expectedRows = ThisWorkbook.Sheets("Logbook").ListObjects("Logbook").DataBodyRange.Rows.Count
    expectedTotalHours = GetLogbookTotalHours(ThisWorkbook)
    expectedTotalKnown = (Err.Number = 0)
    Err.Clear
    On Error GoTo 0

    tempPath = Environ("TEMP") & "\LB_Master_" & sessionId & ".xlsm"
    ' Resolve to the local folder the logbook is already in.
    ' ResolveLocalPath handles OneDrive cloud URLs by mapping them to
    ' the local sync folder, so FileCopy always targets a real FS path.
    localPath = ResolveLocalPath(ThisWorkbook)
    originalName = ThisWorkbook.Name
    canonicalName = CanonicalWorkbookName(originalName)
    savePath = localPath & "\" & canonicalName
    updatedPath = savePath
    oldPath = BuildOldWorkbookPath(localPath, canonicalName)
    releaseChannel = (LCase$(Trim$(GetGitHubBranch())) = "main")
    If Not releaseChannel Then
        diagnosticsPath = BuildUpdateDiagnosticsPath(localPath, canonicalName)
    End If
    wizardReportPath = BuildWizardReportPath(localPath, canonicalName)
    sourceWorkbookPath = localPath & "\" & originalName
    WriteUpdateDiagnostic diagnosticsPath, "Update started. Source=" & sourceWorkbookPath & _
        "; targetVersion=" & newVersion & "; branch=" & GetGitHubBranch()

    ' Prefer the external wizard flow. Development builds may fall back to the
    ' classic updater for diagnostics; release builds abort if the wizard is unavailable.
    If Not releaseChannel Then
        wizardMasterPath = tempPath
        If Not DownloadFile(RawURL(MASTER_FILE, mResolvedRef), wizardMasterPath) Then
            wizardReason = "Could not prepare the development master workbook for the updater wizard."
            wizardMasterPath = ""
        End If
    ElseIf Not LatestReleaseMatchesVersion(GITHUB_USER & "/" & GITHUB_REPO, newVersion) Then
        wizardReason = "Release wizard assets for version " & newVersion & " are not published yet."
    End If

    If wizardReason = "" And TryLaunchExternalUpdaterWizard(sourceWorkbookPath, GITHUB_USER & "/" & GITHUB_REPO, wizardReason, wizardMasterPath, newVersion) Then
        WriteUpdateDiagnostic diagnosticsPath, "External updater wizard launched."
        WriteUpdateDiagnostic diagnosticsPath, "Launcher diagnostics stop here because Excel handed off to the external wizard."
        WriteUpdateDiagnostic diagnosticsPath, "Wizard diagnostic report path if enabled: " & wizardReportPath
        UpdateStatus ""

        Dim closeErr As Long
        Dim closeMsg As String
        Dim shouldQuitExcel As Boolean

        shouldQuitExcel = (Application.Workbooks.Count <= 1)
        On Error Resume Next
        Application.DisplayAlerts = False
        ThisWorkbook.Save
        If shouldQuitExcel Then
            Application.Quit
        Else
            ThisWorkbook.Close SaveChanges:=False
        End If
        closeErr = Err.Number
        closeMsg = Err.Description
        Application.DisplayAlerts = True
        On Error GoTo 0

        If closeErr <> 0 Then
            MsgBox "The updater wizard is running, but this workbook could not close automatically." & vbCrLf & vbCrLf & _
                "Please close this workbook now so the wizard can continue." & vbCrLf & vbCrLf & _
                "Close error: " & closeMsg, _
                vbExclamation, "Manual Close Required"
        End If

        Exit Sub
    End If

    If wizardReason <> "" And releaseChannel Then
        WriteUpdateDiagnostic diagnosticsPath, "External updater wizard unavailable on release channel. Reason=" & wizardReason
        MsgBox "The external updater wizard was not available, so the update cannot continue safely." & vbCrLf & vbCrLf & _
               "Reason: " & wizardReason & vbCrLf & vbCrLf & _
               "Your workbook has not been changed.", vbCritical, "Update Failed"
        UpdateStatus ""
        Exit Sub
    End If

    If wizardReason <> "" Then
        WriteUpdateDiagnostic diagnosticsPath, "External updater wizard unavailable. Reason=" & wizardReason
        MsgBox "The external updater wizard was not available, so the classic updater will be used for this run." & vbCrLf & vbCrLf & _
               "Reason: " & wizardReason, vbInformation, "Using Classic Updater"
    End If

    diagStep = "Downloading master workbook"
    WriteUpdateDiagnostic diagnosticsPath, diagStep
    UpdateStatus "Downloading update (version " & newVersion & ")..."
    If Not DownloadFile(RawURL(MASTER_FILE, mResolvedRef), tempPath) Then
        WriteUpdateDiagnostic diagnosticsPath, "Failed: could not download master workbook."
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
    WriteUpdateDiagnostic diagnosticsPath, diagStep
    Set masterWb = Workbooks.Open(tempPath, ReadOnly:=False, UpdateLinks:=False)

    diagStep = "Unprotecting master workbook"
    WriteUpdateDiagnostic diagnosticsPath, diagStep
    PrepareMasterWorkbookForMigration masterWb

    diagStep = "Copying Logbook data into master"
    WriteUpdateDiagnostic diagnosticsPath, diagStep
    UpdateStatus "Copying flight data..."
    InjectLogbookData masterWb

    diagStep = "Copying Keywords data into master"
    CopyKeywordsData masterWb

    diagStep = "Copying base airport selections into master"
    CopyBaseAirportSelections masterWb

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
    ApplyMasterLogbookFormatting masterWb
    ApplyBaseAirportCheckboxesIfAvailable FindListObject(masterWb, "BaseAirportsTop10")

    diagStep = "Updating hidden rows"
    Dim wsLog     As Worksheet
    Dim tblLog    As ListObject
    Set wsLog  = masterWb.Sheets("Logbook")
    Set tblLog = wsLog.ListObjects("Logbook")
    HideRowsBelowLogbookData tblLog
    RepairLogbookActionButtons tblLog
    Set wsLog  = Nothing
    Set tblLog = Nothing

    diagStep = "Refreshing airport visit stats"
    WriteUpdateDiagnostic diagnosticsPath, diagStep
    UpdateStatus "Refreshing airport visit stats..."
    Application.Run "'" & masterWb.Name & "'!RefreshAirportVisitStats", masterWb

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
        On Error Resume Next
        Dim hotChartObj As ChartObject
        Dim hotSeries As Series
        Set hotChartObj = wsCharts.ChartObjects("HoursOverTime")
        If hotChartObj.Chart.SeriesCollection.Count = 0 Then
            hotChartObj.Chart.SeriesCollection.NewSeries
        End If
        Set hotSeries = hotChartObj.Chart.SeriesCollection(1)
        hotSeries.XValues = chartRng.Columns(1)
        hotSeries.Values = chartRng.Columns(2)
        If Err.Number <> 0 Then
            Err.Clear
            hotChartObj.Chart.SetSourceData Source:=chartRng
            If hotChartObj.Chart.SeriesCollection.Count > 0 Then
                Set hotSeries = hotChartObj.Chart.SeriesCollection(1)
                hotSeries.XValues = chartRng.Columns(1)
                hotSeries.Values = chartRng.Columns(2)
            End If
        End If
        On Error GoTo UpdateFailed
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
    ActivatePrimarySheetForSave masterWb

    ' Save to a local temp path first, then move to destination.
    ' Direct SaveAs to OneDrive paths is unreliable depending on sync state.
    localSavePath = Environ("TEMP") & "\LB_Updated_" & sessionId & ".xlsm"
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

    ' Validate the staged workbook before any destructive operations.
    ' This is the last safe abort point: the original file has not been touched.
    On Error GoTo 0
    diagStep = "Validating staged update"
    WriteUpdateDiagnostic diagnosticsPath, diagStep
    UpdateStatus "Validating update..."
    If Not ValidateStagedUpdate(localSavePath, newVersion, expectedRows, expectedTotalHours, expectedTotalKnown) Then
        WriteUpdateDiagnostic diagnosticsPath, "Staged update validation failed. Reason=" & mLastUpdateFailureReason
        On Error Resume Next
        Kill localSavePath
        On Error GoTo 0
        Application.Calculation = xlCalculationAutomatic
        Application.ScreenUpdating = True
        Application.EnableEvents = True
        UpdateStatus ""
        MsgBox "The staged update failed validation. Your logbook has not been changed." & vbCrLf & vbCrLf & _
               "Reason: " & mLastUpdateFailureReason & vbCrLf & vbCrLf & _
               "Diagnostics were written to:" & vbCrLf & diagnosticsPath & vbCrLf & vbCrLf & _
               "Please try updating again. If the problem persists, use the Report a Bug button.", _
               vbCritical, "Update Validation Failed"
        Exit Sub
    End If
    On Error GoTo UpdateFailed

    diagStep = "Saving backup copy"
    WriteUpdateDiagnostic diagnosticsPath, diagStep
    UpdateStatus "Saving backup copy..."
    finalHandoffStarted = True
    Application.DisplayAlerts = False
    On Error Resume Next
    ThisWorkbook.SaveCopyAs Filename:=oldPath
    If Err.Number <> 0 Then
        Dim backupErr As String
        backupErr = Err.Description
        Err.Clear
        Application.DisplayAlerts = True
        On Error GoTo UpdateFailed
        Err.Raise vbObjectError + 931, "modUpdate.RunUpdate", _
                  "Could not create backup old-copy file. " & backupErr
    End If
    On Error GoTo UpdateFailed
    Application.DisplayAlerts = True

    diagStep = "Validating backup file"
    WriteUpdateDiagnostic diagnosticsPath, diagStep
    UpdateStatus "Validating backup..."
    If Not ValidateBackupWorkbook(oldPath, ThisWorkbook) Then
        WriteUpdateDiagnostic diagnosticsPath, "Backup validation failed. Reason=" & mLastUpdateFailureReason
        Application.Calculation = xlCalculationAutomatic
        Application.ScreenUpdating = True
        Application.EnableEvents = True
        UpdateStatus ""
        MsgBox "The previous-version backup could not be validated after save." & vbCrLf & vbCrLf & _
               "Reason: " & mLastUpdateFailureReason & vbCrLf & vbCrLf & _
               "Your workbook has been kept as:" & vbCrLf & oldPath & vbCrLf & vbCrLf & _
               "Diagnostics were written to:" & vbCrLf & diagnosticsPath & vbCrLf & vbCrLf & _
               "The update was stopped before replacing the original filename.", _
               vbCritical, "Backup Validation Failed"
        Exit Sub
    End If

    diagStep = "Moving updated file to updated copy"
    WriteUpdateDiagnostic diagnosticsPath, diagStep
    UpdateStatus "Saving updated logbook..."
    updatedPath = BuildUpdatedWorkbookPath(localPath, canonicalName)
    ReplaceFileWithRetry localSavePath, updatedPath
    On Error Resume Next
    Kill localSavePath
    On Error GoTo 0

    diagStep = "Waiting for final workbook readiness"
    WriteUpdateDiagnostic diagnosticsPath, diagStep
    UpdateStatus "finalising updated file..."
    finalReady = WaitForUpdatedWorkbookReady(updatedPath, newVersion, 90)

    Application.Calculation = xlCalculationAutomatic
    Application.ScreenUpdating = True
    Application.EnableEvents = True
    UpdateStatus ""

    If finalReady Then
        WriteUpdateDiagnostic diagnosticsPath, "Final workbook readiness verified."
        readinessNote = vbCrLf & vbCrLf & _
                        "Ready to open: the updated workbook was verified after handoff."
    Else
        WriteUpdateDiagnostic diagnosticsPath, "Final workbook readiness timed out."
        readinessNote = vbCrLf & vbCrLf & _
                        "OneDrive is still finalising this file. Do not open it yet." & vbCrLf & _
                        "Wait for sync to finish (pending icon clears), then open from Explorer."
    End If

    MsgBox "Update complete! Your updated logbook has been saved as:" & vbCrLf & vbCrLf & _
        updatedPath & vbCrLf & vbCrLf & _
        "Backup copy saved as:" & vbCrLf & vbCrLf & _
        oldPath & vbCrLf & vbCrLf & _
        "Please close this old file and open the updated file above." & vbCrLf & vbCrLf & _
        "Please verify that your total hours, Charts page, and Currency + Recency page match what you had before." & readinessNote, _
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
    WriteUpdateDiagnostic diagnosticsPath, "Update failed at step=" & diagStep & _
        "; error=" & CStr(errNum) & "; description=" & errMsg
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

    MsgBox BuildUpdateUserFacingErrorMessage( _
           "The workbook update could not be completed.", _
           failureNote & vbCrLf & vbCrLf & _
           "Please try updating again. If the problem persists, use the Report a Bug button and include the update diagnostics file.", _
           errNum, "modUpdate.RunUpdate", errMsg, diagStep, diagnosticsPath), _
           vbCritical, "Update Failed"
End Sub

Private Function CanonicalWorkbookName(ByVal workbookName As String) As String
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
        ' Treat names ending in _Old, _Old_<timestamp>, or _Old_<timestamp>_<n>
        If suffix = "" Or Left$(suffix, 1) = "_" Then
            baseName = Left$(baseName, markerPos - 1)
        End If
    End If

    CanonicalWorkbookName = baseName & extension
End Function

Private Sub ReplaceFileWithRetry(ByVal sourcePath As String, ByVal targetPath As String)
    Dim attempt As Long

    For attempt = 1 To 5
        On Error Resume Next
        If Dir$(targetPath) <> "" Then Kill targetPath
        Err.Clear

        FileCopy sourcePath, targetPath
        If Err.Number = 0 Then
            If Dir$(targetPath) <> "" Then
                If FileLen(targetPath) > 0 Then
                    On Error GoTo 0
                    Exit Sub
                End If
            End If
        End If

        Err.Clear
        On Error GoTo 0
        DoEvents
    Next attempt

    Err.Raise vbObjectError + 930, "modUpdate.RunUpdate", _
              "Could not write updated workbook to original filename."
End Sub

Private Function WaitForUpdatedWorkbookReady(ByVal workbookPath As String, ByVal expectedVersion As String, ByVal timeoutSeconds As Long) As Boolean
    Dim startedAt As Date
    Dim priorSize As Long
    Dim priorStamp As Date
    Dim stablePasses As Long
    Dim currentSize As Long
    Dim currentStamp As Date

    startedAt = Now
    priorSize = -1
    priorStamp = 0
    stablePasses = 0

    Do
        If Dir$(workbookPath) <> "" Then
            On Error Resume Next
            currentSize = FileLen(workbookPath)
            currentStamp = FileDateTime(workbookPath)
            If Err.Number <> 0 Then
                Err.Clear
                currentSize = -1
                currentStamp = 0
            End If
            On Error GoTo 0

            If currentSize > 0 Then
                If currentSize = priorSize And currentStamp = priorStamp Then
                    stablePasses = stablePasses + 1
                Else
                    stablePasses = 0
                End If

                priorSize = currentSize
                priorStamp = currentStamp

                If stablePasses >= 1 Then
                    If CanOpenWorkbookVersion(workbookPath, expectedVersion) Then
                        WaitForUpdatedWorkbookReady = True
                        Exit Function
                    End If
                End If
            End If
        End If

        If DateDiff("s", startedAt, Now) >= timeoutSeconds Then Exit Do
        UpdateStatus "finalising updated file... (" & CStr(DateDiff("s", startedAt, Now)) & "s)"
        WaitOneSecond
    Loop

    WaitForUpdatedWorkbookReady = False
End Function

Private Function CanOpenWorkbookVersion(ByVal workbookPath As String, ByVal expectedVersion As String) As Boolean
    Dim openedWb As Workbook
    Dim openedVersion As String

    On Error GoTo Fail
    Set openedWb = Workbooks.Open(Filename:=workbookPath, ReadOnly:=True, UpdateLinks:=False, AddToMru:=False)
    openedVersion = Trim$(CStr(openedWb.Names("LogbookVersion").RefersToRange.Value))
    openedWb.Close SaveChanges:=False
    Set openedWb = Nothing

    CanOpenWorkbookVersion = (StrComp(openedVersion, expectedVersion, vbTextCompare) = 0)
    Exit Function

Fail:
    On Error Resume Next
    If Not openedWb Is Nothing Then openedWb.Close SaveChanges:=False
    Set openedWb = Nothing
    On Error GoTo 0
    CanOpenWorkbookVersion = False
End Function

Private Sub WaitOneSecond()
    Dim target As Date
    target = DateAdd("s", 1, Now)
    Do While Now < target
        DoEvents
    Loop
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

Private Function BuildUpdatedWorkbookPath(folderPath As String, workbookName As String) As String
    Dim dotPos As Long
    Dim baseName As String
    Dim extension As String
    Dim candidate As String
    Dim counter As Long

    dotPos = InStrRev(workbookName, ".")
    If dotPos > 0 Then
        baseName = Left$(workbookName, dotPos - 1)
        extension = Mid$(workbookName, dotPos)
    Else
        baseName = workbookName
        extension = ""
    End If

    candidate = folderPath & "\" & baseName & "_Updated" & extension
    If Dir$(candidate) = "" Then
        BuildUpdatedWorkbookPath = candidate
        Exit Function
    End If

    counter = 1
    Do
        candidate = folderPath & "\" & baseName & "_Updated_" & counter & extension
        counter = counter + 1
    Loop While Dir$(candidate) <> ""

    BuildUpdatedWorkbookPath = candidate
End Function

Private Function BuildUpdateDiagnosticsPath(ByVal folderPath As String, ByVal workbookName As String) As String
    Dim dotPos As Long
    Dim baseName As String

    dotPos = InStrRev(workbookName, ".")
    If dotPos > 0 Then
        baseName = Left$(workbookName, dotPos - 1)
    Else
        baseName = workbookName
    End If

    BuildUpdateDiagnosticsPath = folderPath & "\" & baseName & "_UpdateDiagnostics.txt"
End Function

Private Function BuildUpdateUserFacingErrorMessage(ByVal userMessage As String, _
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

    BuildUpdateUserFacingErrorMessage = userMessage & vbCrLf & vbCrLf & _
                                        recoveryMessage & vbCrLf & vbCrLf & _
                                        details
End Function

Private Function BuildWizardReportPath(ByVal folderPath As String, ByVal workbookName As String) As String
    Dim dotPos As Long
    Dim baseName As String

    dotPos = InStrRev(workbookName, ".")
    If dotPos > 0 Then
        baseName = Left$(workbookName, dotPos - 1)
    Else
        baseName = workbookName
    End If

    BuildWizardReportPath = folderPath & "\" & baseName & ".update-report.json"
End Function

Private Sub WriteUpdateDiagnostic(ByVal diagnosticsPath As String, ByVal message As String)
    Dim fileNumber As Integer

    If diagnosticsPath = "" Then Exit Sub

    On Error Resume Next
    fileNumber = FreeFile
    Open diagnosticsPath For Append As #fileNumber
    Print #fileNumber, Format$(Now, "yyyy-mm-dd hh:nn:ss") & " | " & message
    Close #fileNumber
    On Error GoTo 0
End Sub

Private Sub PrepareMasterWorkbookForMigration(masterWb As Workbook)
    Dim ws As Worksheet

    ' The release workbook can now be saved in a protected state.
    ' Update migration needs table resize/copy operations, so we force
    ' an unprotected editing state for the temporary downloaded master.
    On Error Resume Next
    masterWb.Unprotect Password:=""
    For Each ws In masterWb.Worksheets
        ws.Unprotect Password:=""
    Next ws
    On Error GoTo 0
End Sub

Private Sub ActivatePrimarySheetForSave(masterWb As Workbook)
    On Error Resume Next
    masterWb.Activate
    masterWb.Worksheets("New Entry").Activate
    If Err.Number <> 0 Then
        Err.Clear
        masterWb.Worksheets(1).Activate
    End If
    On Error GoTo 0
End Sub

Private Function ValidateBackupWorkbook(backupPath As String, Optional backupWb As Workbook = Nothing) As Boolean
    If backupWb Is Nothing Then Set backupWb = ThisWorkbook

    ' Validate the workbook that is already open after SaveAs.
    ' Re-opening the same path here can block on file locks/OneDrive sync.
    On Error GoTo Fail

    Dim t As ListObject
    Set t = backupWb.Sheets("Logbook").ListObjects("Logbook")
    If t Is Nothing Then GoTo Fail

    ' Confirm the backup file exists on disk and is non-empty.
    If Dir$(backupPath) = "" Then
        Err.Raise vbObjectError + 921, , "Backup file not found after SaveAs."
    End If

    Dim backupSize As Long
    backupSize = FileLen(backupPath)
    If backupSize <= 0 Then
        Err.Raise vbObjectError + 922, , "Backup file is empty after SaveAs."
    End If

    ValidateBackupWorkbook = True
    Exit Function

Fail:
    On Error Resume Next
    mLastUpdateFailureReason = Err.Description
    If mLastUpdateFailureReason = "" Then mLastUpdateFailureReason = "Backup validation failed."
    Application.Run "WriteDebugLog", "modUpdate.ValidateBackupWorkbook", Err.Number, Err.Description, "Validating backup file"
    On Error GoTo 0
    ValidateBackupWorkbook = False
End Function

' ==============================================================
' DATA INJECTION - LOGBOOK
' ==============================================================

Private Sub InjectLogbookData(masterWb As Workbook)
    Dim loSrc        As ListObject
    Dim loDst        As ListObject
    Dim dataColStart As Long
    Dim dataColEnd   As Long
    Dim userRows     As Long
    Dim masterRows   As Long
    Dim hasTotals    As Boolean
    Dim airportLookup As Object

    Set loSrc = ThisWorkbook.Sheets("Logbook").ListObjects("Logbook")
    Set loDst = masterWb.Sheets("Logbook").ListObjects("Logbook")

    If loSrc.DataBodyRange Is Nothing Then Exit Sub
    If loDst.DataBodyRange Is Nothing Then Exit Sub

    dataColStart = loSrc.ListColumns("Year").Index
    dataColEnd   = loSrc.ListColumns("Circling").Index
    userRows     = loSrc.DataBodyRange.Rows.Count
    masterRows   = loDst.DataBodyRange.Rows.Count
    hasTotals    = loDst.ShowTotals
    Set airportLookup = BuildAirportAliasLookup(masterWb)

    If userRows > masterRows Then
        loDst.ShowTotals = False
        loDst.Resize loDst.Range.Resize(userRows + 1, loDst.ListColumns.Count)
        FillLogbookFormulas loDst, masterRows, userRows
        RefreshLogbookCalculatedFormulas loDst
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

    customSrcStart = LogbookCustomStartColumn(loSrc)
    customSrcEnd   = loSrc.ListColumns("SeIcusDay").Index - 1
    customDstStart = LogbookCustomStartColumn(loDst)
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
    Dim dstColName As String
    Dim dstColIdx As Long
    For Each srcCol In loSrc.ListColumns
        If (srcCol.Index >= dataColStart And srcCol.Index <= dataColEnd) Or _
           srcCol.Name = "CurrencyExclusions" Then
            dstColName = DestinationLogbookColumnName(loDst, srcCol.Name)
            If Len(dstColName) = 0 Then GoTo NextSourceColumn

            On Error Resume Next
            dstColIdx = loDst.ListColumns(dstColName).Index
            If Err.Number = 0 Then
                loDst.DataBodyRange.Columns(dstColIdx).Resize(userRows, 1).Value = _
                    srcCol.DataBodyRange.Resize(userRows, 1).Value
            End If
            Err.Clear
            On Error GoTo 0
        End If
NextSourceColumn:
    Next srcCol

    SplitLegacyDetailsIntoNewEntryColumns loSrc, loDst, airportLookup, userRows
    ConvertLegacyCurrencyColumns loSrc, loDst, userRows

    ' Clear explicit cell fills and bold accumulated from prior AddToLogbook
    ' PasteSpecial calls. Lets the table's built-in stripe style show cleanly.
    loDst.DataBodyRange.Interior.Pattern = xlNone
    loDst.DataBodyRange.Font.Bold = False
End Sub

Private Function DestinationLogbookColumnName(ByVal loDst As ListObject, ByVal sourceName As String) As String
    If ListColumnExists(loDst, sourceName) Then
        DestinationLogbookColumnName = sourceName
    ElseIf LCase$(sourceName) = "rnav" And ListColumnExists(loDst, "RNP") Then
        DestinationLogbookColumnName = "RNP"
    ElseIf LCase$(sourceName) = "cumrnav" And ListColumnExists(loDst, "CumRNP") Then
        DestinationLogbookColumnName = "CumRNP"
    End If
End Function

Private Sub SplitLegacyDetailsIntoNewEntryColumns(ByVal loSrc As ListObject, _
                                                  ByVal loDst As ListObject, _
                                                  ByVal airportLookup As Object, _
                                                  ByVal rowCount As Long)
    Dim sourceDetailsColumn As String
    Dim rowIndex As Long
    Dim splitValues As Variant

    If rowCount <= 0 Then Exit Sub
    If Not ListColumnExists(loDst, "From") Then Exit Sub
    If Not ListColumnExists(loDst, "To") Then Exit Sub
    If Not ListColumnExists(loDst, "Via") Then Exit Sub
    If Not ListColumnExists(loDst, "Remarks") Then Exit Sub

    If ListColumnExists(loSrc, "Remarks") Then
        CopyColumnIfPresent loSrc, loDst, "From", rowCount
        CopyColumnIfPresent loSrc, loDst, "To", rowCount
        CopyColumnIfPresent loSrc, loDst, "Via", rowCount
        CopyColumnIfPresent loSrc, loDst, "Remarks", rowCount
        Exit Sub
    End If

    If Not ListColumnExists(loSrc, "Details") Then Exit Sub
    sourceDetailsColumn = "Details"

    For rowIndex = 1 To rowCount
        splitValues = SplitLegacyDetailsText( _
            CStr(loSrc.ListColumns(sourceDetailsColumn).DataBodyRange.Cells(rowIndex, 1).Value), _
            airportLookup)

        loDst.ListColumns("From").DataBodyRange.Cells(rowIndex, 1).Value = splitValues(0)
        loDst.ListColumns("To").DataBodyRange.Cells(rowIndex, 1).Value = splitValues(1)
        loDst.ListColumns("Via").DataBodyRange.Cells(rowIndex, 1).Value = splitValues(2)
        loDst.ListColumns("Remarks").DataBodyRange.Cells(rowIndex, 1).Value = splitValues(3)
    Next rowIndex
End Sub

Private Sub ConvertLegacyCurrencyColumns(ByVal loSrc As ListObject, _
                                         ByVal loDst As ListObject, _
                                         ByVal rowCount As Long)
    Dim rowIndex As Long
    Dim excluded As Boolean

    If rowCount <= 0 Then Exit Sub
    If Not ListColumnExists(loDst, "FR") Then Exit Sub
    If Not ListColumnExists(loDst, "IPC") Then Exit Sub
    If Not ListColumnExists(loDst, "OPC") Then Exit Sub

    For rowIndex = 1 To rowCount
        excluded = False
        If ListColumnExists(loSrc, "CurrencyExclusions") Then
            excluded = LogbookBooleanValue(loSrc, "CurrencyExclusions", rowIndex)
        End If

        If excluded Then
            loDst.ListColumns("FR").DataBodyRange.Cells(rowIndex, 1).Value = False
            loDst.ListColumns("IPC").DataBodyRange.Cells(rowIndex, 1).Value = False
            loDst.ListColumns("OPC").DataBodyRange.Cells(rowIndex, 1).Value = False
        Else
            loDst.ListColumns("FR").DataBodyRange.Cells(rowIndex, 1).Value = _
                LogbookCurrencyCheckValue(loSrc, "FR", rowIndex) Or _
                LogbookCurrencyCheckValue(loSrc, "FlightReview", rowIndex) Or _
                LogbookCurrencyCheckValue(loSrc, "IPC", rowIndex)
            loDst.ListColumns("IPC").DataBodyRange.Cells(rowIndex, 1).Value = _
                LogbookCurrencyCheckValue(loSrc, "IPC", rowIndex)
            loDst.ListColumns("OPC").DataBodyRange.Cells(rowIndex, 1).Value = _
                LogbookCurrencyCheckValue(loSrc, "OPC", rowIndex)
        End If
    Next rowIndex
End Sub

Private Function LogbookCurrencyCheckValue(ByVal lo As ListObject, _
                                           ByVal columnName As String, _
                                           ByVal rowIndex As Long) As Boolean
    Dim value As Variant
    Dim textValue As String

    If Not ListColumnExists(lo, columnName) Then Exit Function
    value = lo.ListColumns(columnName).DataBodyRange.Cells(rowIndex, 1).Value
    If IsError(value) Then Exit Function

    If VarType(value) = vbBoolean Then
        LogbookCurrencyCheckValue = CBool(value)
    ElseIf IsDate(value) Then
        LogbookCurrencyCheckValue = True
    Else
        textValue = LCase$(Trim$(CStr(value)))
        Select Case textValue
            Case "true", "yes", "y", "1", "x"
                LogbookCurrencyCheckValue = True
        End Select
    End If
End Function

Private Function LogbookBooleanValue(ByVal lo As ListObject, _
                                     ByVal columnName As String, _
                                     ByVal rowIndex As Long) As Boolean
    Dim value As Variant

    If Not ListColumnExists(lo, columnName) Then Exit Function
    value = lo.ListColumns(columnName).DataBodyRange.Cells(rowIndex, 1).Value
    If VarType(value) = vbBoolean Then
        LogbookBooleanValue = CBool(value)
    ElseIf IsNumeric(value) Then
        LogbookBooleanValue = (CDbl(value) <> 0)
    Else
        Select Case LCase$(Trim$(CStr(value)))
            Case "true", "yes", "y", "1", "x"
                LogbookBooleanValue = True
        End Select
    End If
End Function

Private Sub CopyColumnIfPresent(ByVal loSrc As ListObject, _
                                ByVal loDst As ListObject, _
                                ByVal columnName As String, _
                                ByVal rowCount As Long)
    If Not ListColumnExists(loSrc, columnName) Then Exit Sub
    If Not ListColumnExists(loDst, columnName) Then Exit Sub

    loDst.ListColumns(columnName).DataBodyRange.Resize(rowCount, 1).Value = _
        loSrc.ListColumns(columnName).DataBodyRange.Resize(rowCount, 1).Value
End Sub

Private Function BuildAirportAliasLookup(ByVal wb As Workbook) As Object
    Dim lookup As Object
    Dim loAirports As ListObject
    Dim rowIndex As Long
    Dim icao As String

    Set lookup = CreateObject("Scripting.Dictionary")
    lookup.CompareMode = 1

    On Error Resume Next
    Set loAirports = wb.Sheets("Airports").ListObjects("Airports")
    On Error GoTo 0
    If loAirports Is Nothing Then
        Set BuildAirportAliasLookup = lookup
        Exit Function
    End If
    If loAirports.DataBodyRange Is Nothing Then
        Set BuildAirportAliasLookup = lookup
        Exit Function
    End If

    For rowIndex = 1 To loAirports.DataBodyRange.Rows.Count
        icao = UCase$(Trim$(CStr(loAirports.ListColumns("ICAO").DataBodyRange.Cells(rowIndex, 1).Value)))
        If Len(icao) > 0 Then
            AddAirportAlias lookup, icao, icao
            AddAirportAlias lookup, CStr(loAirports.ListColumns("Two").DataBodyRange.Cells(rowIndex, 1).Value), icao
            AddAirportAlias lookup, CStr(loAirports.ListColumns("Three").DataBodyRange.Cells(rowIndex, 1).Value), icao
        End If
    Next rowIndex

    Set BuildAirportAliasLookup = lookup
End Function

Private Sub AddAirportAlias(ByVal lookup As Object, ByVal aliasText As String, ByVal icao As String)
    aliasText = UCase$(Trim$(aliasText))
    If Len(aliasText) = 0 Then Exit Sub
    If Not lookup.Exists(aliasText) Then lookup.Add aliasText, icao
End Sub

Private Function SplitLegacyDetailsText(ByVal detailsText As String, ByVal airportLookup As Object) As Variant
    Dim normalised As String
    Dim delimiters As Variant
    Dim delimiter As Variant
    Dim tokens As Variant
    Dim token As Variant
    Dim tokenText As String
    Dim matchedIcaos As Collection
    Dim remarkTokens As Collection
    Dim result(0 To 3) As String
    Dim i As Long
    Dim routeText As String
    Dim remarksText As String

    Set matchedIcaos = New Collection
    Set remarkTokens = New Collection

    normalised = Replace(detailsText, "|", "")
    delimiters = Array("-", " ", ",", "(", ")")
    For Each delimiter In delimiters
        normalised = Replace(normalised, CStr(delimiter), "|")
    Next delimiter

    tokens = Split(normalised, "|")
    For Each token In tokens
        tokenText = Trim$(CStr(token))
        If Len(tokenText) > 0 Then
            If airportLookup.Exists(UCase$(tokenText)) Then
                matchedIcaos.Add CStr(airportLookup(UCase$(tokenText)))
            Else
                remarkTokens.Add tokenText
            End If
        End If
    Next token

    If matchedIcaos.Count >= 1 Then result(0) = CStr(matchedIcaos(1))
    If matchedIcaos.Count >= 2 Then result(1) = CStr(matchedIcaos(matchedIcaos.Count))
    If matchedIcaos.Count > 2 Then
        For i = 2 To matchedIcaos.Count - 1
            If Len(routeText) > 0 Then routeText = routeText & "-"
            routeText = routeText & CStr(matchedIcaos(i))
        Next i
        result(2) = routeText
    End If

    For i = 1 To remarkTokens.Count
        If Len(remarksText) > 0 Then remarksText = remarksText & " "
        remarksText = remarksText & CStr(remarkTokens(i))
    Next i
    result(3) = remarksText

    SplitLegacyDetailsText = result
End Function

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

Private Sub RefreshLogbookCalculatedFormulas(ByVal tbl As ListObject)
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

Private Function LogbookCustomStartColumn(ByVal tbl As ListObject) As Long
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

Private Function ListColumnExists(ByVal tbl As ListObject, ByVal columnName As String) As Boolean
    On Error Resume Next
    ListColumnExists = Not tbl.ListColumns(columnName) Is Nothing
    On Error GoTo 0
End Function

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

Private Sub CopyBaseAirportSelections(masterWb As Workbook)
    Dim loSrc As ListObject
    Dim loDst As ListObject

    On Error GoTo Fail

    Set loSrc = FindListObject(ThisWorkbook, "BaseAirportsTop10")
    Set loDst = FindListObject(masterWb, "BaseAirportsTop10")

    If Not loSrc Is Nothing And Not loDst Is Nothing Then
        CopyTableDataByMatchingColumns loSrc, loDst
        ApplyBaseAirportCheckboxesIfAvailable loDst
        Exit Sub
    End If

    If loDst Is Nothing Then Exit Sub
    MigrateLegacyAirportBaseFlags loDst
    ApplyBaseAirportCheckboxesIfAvailable loDst
    Exit Sub
Fail:
    Err.Clear
End Sub

Private Sub CopyTableDataByMatchingColumns(ByVal loSrc As ListObject, ByVal loDst As ListObject)
    Dim srcCol As ListColumn
    Dim dstColIdx As Long
    Dim sourceRows As Long
    Dim destRows As Long

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
        On Error GoTo 0
        If dstColIdx > 0 Then
            loDst.DataBodyRange.Columns(dstColIdx).Resize(sourceRows, 1).Value = _
                srcCol.DataBodyRange.Resize(sourceRows, 1).Value
        End If
    Next srcCol
End Sub

Private Sub MigrateLegacyAirportBaseFlags(ByVal loDst As ListObject)
    Dim loAirports As ListObject
    Dim rowIndex As Long
    Dim outputRow As Long
    Dim icao As String
    Dim airportName As String

    Set loAirports = FindListObject(ThisWorkbook, "Airports")
    If loAirports Is Nothing Then Exit Sub
    If loAirports.DataBodyRange Is Nothing Then Exit Sub
    If Not ListColumnExists(loAirports, "Base") Then Exit Sub

    If loDst.DataBodyRange Is Nothing Then loDst.ListRows.Add
    loDst.DataBodyRange.ClearContents
    outputRow = 1

    For rowIndex = 1 To loAirports.DataBodyRange.Rows.Count
        If LegacyBaseValueIsChecked(loAirports.DataBodyRange.Cells(rowIndex, loAirports.ListColumns("Base").Index).Value) Then
            If outputRow > loDst.DataBodyRange.Rows.Count Then loDst.ListRows.Add
            icao = UCase$(Trim$(CStr(loAirports.DataBodyRange.Cells(rowIndex, loAirports.ListColumns("ICAO").Index).Value)))
            airportName = Trim$(CStr(loAirports.DataBodyRange.Cells(rowIndex, loAirports.ListColumns("Airport").Index).Value))
            loDst.DataBodyRange.Cells(outputRow, loDst.ListColumns("ICAO").Index).Value = icao
            loDst.DataBodyRange.Cells(outputRow, loDst.ListColumns("Airport").Index).Value = airportName
            loDst.DataBodyRange.Cells(outputRow, loDst.ListColumns("Base").Index).Value = True
            outputRow = outputRow + 1
        End If
    Next rowIndex
End Sub

Private Sub ApplyBaseAirportCheckboxesIfAvailable(ByVal lo As ListObject)
    If lo Is Nothing Then Exit Sub
    If lo.DataBodyRange Is Nothing Then Exit Sub
    If Not ListColumnExists(lo, "Base") Then Exit Sub

    On Error Resume Next
    lo.ListColumns("Base").DataBodyRange.CellControl.SetCheckbox
    Err.Clear
    On Error GoTo 0
End Sub

Private Sub ApplyMasterLogbookFormatting(ByVal masterWb As Workbook)
    Dim lo As ListObject

    Set lo = masterWb.Sheets("Logbook").ListObjects("Logbook")

    Application.Run "'" & masterWb.Name & "'!modLogbook.NormaliseLogbookFormatting", lo, masterWb
    ApplyLogbookNativeCheckboxesIfAvailable lo
End Sub

Private Sub ApplyLogbookNativeCheckboxesIfAvailable(ByVal lo As ListObject)
    Dim columnName As Variant

    If lo Is Nothing Then Exit Sub
    If lo.DataBodyRange Is Nothing Then Exit Sub

    For Each columnName In Array("FR", "IPC", "OPC")
        If ListColumnExists(lo, CStr(columnName)) Then
            On Error Resume Next
            lo.ListColumns(CStr(columnName)).DataBodyRange.CellControl.SetCheckbox
            Err.Clear
            On Error GoTo 0
        End If
    Next columnName
End Sub

Private Function LegacyBaseValueIsChecked(ByVal value As Variant) As Boolean
    If VarType(value) = vbBoolean Then
        LegacyBaseValueIsChecked = CBool(value)
    ElseIf IsNumeric(value) Then
        LegacyBaseValueIsChecked = (CDbl(value) <> 0)
    Else
        Select Case LCase$(Trim$(CStr(value)))
            Case "true", "yes", "y", "1", "x"
                LegacyBaseValueIsChecked = True
        End Select
    End If
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
    Dim masterRoutesDirty As Variant
    Dim routeVersion As Variant

    routesBuilt = GetWorkbookNameValue(ThisWorkbook, "RoutesBuilt", "")
    routesDirty = GetWorkbookNameValue(ThisWorkbook, "RoutesDirty", False)
    masterRoutesDirty = GetWorkbookNameValue(masterWb, "RoutesDirty", False)
    routeVersion = GetWorkbookNameValue(ThisWorkbook, "RoutesDefinitionVersion", 0)

    If Trim(CStr(routesBuilt)) <> "" Then
        If Not IsNumeric(routeVersion) Then
            routeVersion = ROUTE_CACHE_DEFINITION_VERSION
        ElseIf CLng(routeVersion) = 0 Then
            routeVersion = ROUTE_CACHE_DEFINITION_VERSION
        End If
    End If

    SetWorkbookNameValue masterWb, "RoutesBuilt", routesBuilt
    SetWorkbookNameValue masterWb, "RoutesDirty", WorkbookBooleanValue(routesDirty) Or WorkbookBooleanValue(masterRoutesDirty)
    SetWorkbookNameValue masterWb, "RoutesDefinitionVersion", routeVersion
End Sub

Private Function WorkbookBooleanValue(ByVal value As Variant) As Boolean
    If IsError(value) Then Exit Function

    If VarType(value) = vbBoolean Then
        WorkbookBooleanValue = CBool(value)
    ElseIf IsNumeric(value) Then
        WorkbookBooleanValue = (CDbl(value) <> 0)
    Else
        Select Case LCase$(Trim$(CStr(value)))
            Case "true", "yes", "y", "1", "x"
                WorkbookBooleanValue = True
        End Select
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
    Dim sourceFormatName  As String
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
		
    ' Copy the source workbook's table style and stripe settings. The migration
    ' changes the schema, but the user's Logbook palette belongs to their file.
    On Error Resume Next
    dstLo.TableStyle = srcLo.TableStyle
    dstLo.ShowTableStyleRowStripes = srcLo.ShowTableStyleRowStripes
    dstLo.ShowTableStyleColumnStripes = srcLo.ShowTableStyleColumnStripes
    dstLo.ShowTableStyleFirstColumn = srcLo.ShowTableStyleFirstColumn
    dstLo.ShowTableStyleLastColumn = srcLo.ShowTableStyleLastColumn
    On Error GoTo Fail

    ' Copy full table formats by destination column name, using source-template
    ' columns for 2.0.0 columns that did not exist in older workbooks.
    For Each dstCol In dstLo.ListColumns
        If dstCol.Index > dstLo.ListColumns("CumAzi").Index Then Exit For
        sourceFormatName = LogbookSourceFormatColumnName(srcLo, dstCol.Name)
        If sourceFormatName = "" Then GoTo NextDestinationColumn

        Set srcCol = Nothing
        On Error Resume Next
        Set srcCol = srcLo.ListColumns(sourceFormatName)
        On Error GoTo Fail

        If Not srcCol Is Nothing Then
            Set srcRng = srcLo.Range.Columns(srcCol.Index)
            Set dstRng = dstLo.Range.Columns(dstCol.Index)

            srcRng.Copy
            dstRng.PasteSpecial xlPasteFormats
            Application.CutCopyMode = False
        End If
NextDestinationColumn:
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

Private Function LogbookSourceFormatColumnName(ByVal srcLo As ListObject, _
                                               ByVal columnName As String) As String
    Select Case LCase$(columnName)
        Case "details", "flightreview", "currencyexclusions", "rnav", "cumrnav"
            LogbookSourceFormatColumnName = ""
        Case "flight id"
            If ListColumnExists(srcLo, "Flight ID") Then
                LogbookSourceFormatColumnName = "Flight ID"
            Else
                LogbookSourceFormatColumnName = "Reg"
            End If
        Case "from", "to", "via", "remarks"
            If ListColumnExists(srcLo, "Details") Then
                LogbookSourceFormatColumnName = "Details"
            Else
                LogbookSourceFormatColumnName = columnName
            End If
        Case "fr", "ipc", "opc"
            If ListColumnExists(srcLo, "Custom 1") Then
                LogbookSourceFormatColumnName = "Custom 1"
            Else
                LogbookSourceFormatColumnName = "Details"
            End If
        Case "rnp"
            If ListColumnExists(srcLo, "RNP") Then
                LogbookSourceFormatColumnName = "RNP"
            ElseIf ListColumnExists(srcLo, "RNAV") Then
                LogbookSourceFormatColumnName = "RNAV"
            End If
        Case "cumrnp"
            If ListColumnExists(srcLo, "CumRNP") Then
                LogbookSourceFormatColumnName = "CumRNP"
            ElseIf ListColumnExists(srcLo, "CumRNAV") Then
                LogbookSourceFormatColumnName = "CumRNAV"
            End If
        Case Else
            If ListColumnExists(srcLo, columnName) Then
                LogbookSourceFormatColumnName = columnName
            End If
    End Select
End Function

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
    Dim srcFirstCol As Long
    Dim srcOtherCol As Long
    Dim dstFirstCol As Long
    Dim dstOtherCol As Long
    Dim srcRange As Range
    Dim dstRange As Range
    Dim srcSumRange As Range
    Dim dstSumRange As Range
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
    If ListColumnExists(srcLo, "Flight ID") Then
        srcFirstCol = srcLo.ListColumns("Flight ID").Range.Column
    Else
        srcFirstCol = srcLo.ListColumns("Reg").Range.Column
    End If
    srcOtherCol = srcLo.ListColumns("Other Pilot or Crew").Range.Column
    Set srcRange = srcWs.Range(srcWs.Cells(srcRow, srcFirstCol), _
                               srcWs.Cells(srcRow + 1, srcOtherCol))

    ' Destination: master's totals row + 1 row below (same columns)
    dstRow = dstLo.TotalsRowRange.Row
    If ListColumnExists(dstLo, "Flight ID") Then
        dstFirstCol = dstLo.ListColumns("Flight ID").Range.Column
    Else
        dstFirstCol = dstLo.ListColumns("Reg").Range.Column
    End If
    dstOtherCol = dstLo.ListColumns("Other Pilot or Crew").Range.Column
    Set dstRange = dstWs.Range(dstWs.Cells(dstRow, dstFirstCol), _
                               dstWs.Cells(dstRow + 1, dstOtherCol))
    If Not dstLo.DataBodyRange Is Nothing Then
        masterFont = dstLo.DataBodyRange.Cells(1, 1).Font.Name
    End If

    srcRange.Copy
    dstRange.PasteSpecial xlPasteFormats
    Application.CutCopyMode = False

    Set srcSumRange = srcWs.Range(srcWs.Cells(srcRow, srcLo.ListColumns(LogbookCustomStartColumn(srcLo)).Range.Column), _
                                  srcWs.Cells(srcRow, srcLo.ListColumns("TotalApps").Range.Column))
    Set dstSumRange = dstWs.Range(dstWs.Cells(dstRow, dstLo.ListColumns(LogbookCustomStartColumn(dstLo)).Range.Column), _
                                  dstWs.Cells(dstRow, dstLo.ListColumns("TotalApps").Range.Column))
    srcSumRange.Copy
    dstSumRange.PasteSpecial xlPasteFormats
    Application.CutCopyMode = False
    dstSumRange.WrapText = False

    If masterFont <> "" Then dstRange.Font.Name = masterFont
    Exit Sub
Fail:
    Application.CutCopyMode = False
    Err.Clear
End Sub

Private Sub HideRowsBelowLogbookData(ByVal lo As ListObject, Optional ByVal bufferRows As Long = 7)
    Dim ws As Worksheet
    Dim lastDataRow As Long

    If lo Is Nothing Then Exit Sub

    Set ws = lo.Parent
    ws.Rows.Hidden = False
    If lo.DataBodyRange Is Nothing Then Exit Sub

    lastDataRow = lo.DataBodyRange.Row + lo.DataBodyRange.Rows.Count - 1
    If lastDataRow + bufferRows <= ws.Rows.Count Then
        ws.Rows(lastDataRow + bufferRows & ":" & ws.Rows.Count).Hidden = True
    End If
End Sub

Private Sub RepairLogbookActionButtons(lo As ListObject)
    RepairLogbookActionButton lo, _
                           "DeleteSelectedLogbookRowsButton", _
                           "DeleteSelectedLogbookRows", _
                           "Year", _
                           False
    RepairLogbookActionButton lo, _
                           "ExportLogbookButton", _
                           "ExportLogbook", _
                           "To", _
                           True
End Sub

Private Sub RepairLogbookActionButton(lo As ListObject, _
                                      ByVal buttonName As String, _
                                      ByVal actionName As String, _
                                      ByVal alignColumnName As String, _
                                      ByVal rebuildIfStillAway As Boolean)
    Dim ws      As Worksheet
    Dim btn     As Shape
    Dim topRow  As Long
    Dim leftCol As Long
    Dim targetLeft As Double
    Dim targetTop  As Double

    On Error GoTo CleanFail
    Set ws = lo.Parent
    Set btn = ws.Shapes(buttonName)

    topRow = lo.TotalsRowRange.Row + 2
    leftCol = lo.ListColumns(alignColumnName).Range.Column
    If topRow + 3 > ws.Rows.Count Then Exit Sub

    targetLeft = ws.Cells(topRow, leftCol).Left
    targetTop = ws.Cells(topRow, leftCol).Top
    ConfigureShapeAction btn, actionName

    MoveLogbookActionButton btn, targetLeft, targetTop

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
    Set rebuilt = ws.Shapes.AddShape(msoShapeRoundedRectangle, targetLeft, targetTop, _
                                     LOGBOOK_ACTION_BUTTON_WIDTH, LOGBOOK_ACTION_BUTTON_HEIGHT)
    rebuilt.Name = buttonName
    rebuilt.TextFrame.Characters.Text = LogbookActionButtonFallbackText(buttonName)
    ConfigureShapeAction rebuilt, actionName
    MoveLogbookActionButton rebuilt, targetLeft, targetTop
    On Error GoTo 0
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
    ' Older workbooks can have slightly different grouped-field layouts,
    ' so we use tolerant field operations and a yearly-layout fallback.
    On Error GoTo GroupFail
    Set pt = masterWb.Sheets("ChartData").PivotTables("HoursByYear")
    On Error GoTo 0

    ' Remove Date and refresh so cache rebuilds cleanly with valid dates.
    ' This can fail on some grouped layouts, so continue with fallback logic.
    TrySetPivotFieldOrientation pt, "Date", xlHidden
    DoEvents
    Application.Calculation = xlCalculationAutomatic
    pt.RefreshTable
    Application.Calculation = xlCalculationManual
    DoEvents

    ' Re-add Date before grouping when available.
    If TrySetPivotFieldOrientation(pt, "Date", xlRowField) Then
        On Error Resume Next
        pt.PivotFields("Date").Position = 1
        On Error GoTo 0
        DoEvents

        If Not TryGroupDateByMonthAndYear(pt) Then
            ApplyHoursByYearPivotFallbackLayout pt
            Exit Sub
        End If
    End If

    ' Keep yearly layout stable for the chart defaults.
    ApplyHoursByYearPivotFallbackLayout pt
    Exit Sub

GroupFail:
    MsgBox BuildUpdateUserFacingErrorMessage( _
           "HoursByYear date grouping could not be automatically restored.", _
           "The update can still be used, but the Hours by Year chart may need a manual refresh." & vbCrLf & vbCrLf & _
           "To fix it in the updated file:" & vbCrLf & _
           "  1. Open the HoursByYear pivot table" & vbCrLf & _
           "  2. Remove 'Date' from the Rows field" & vbCrLf & _
           "  3. Press Ctrl+Alt+F5 to refresh all" & vbCrLf & _
           "  4. Re-add 'Date' to the Rows field", _
           Err.Number, Err.Source, Err.Description, "Restoring HoursByYear pivot date grouping"), _
           vbExclamation, "Pivot Grouping Warning"
    Err.Clear
End Sub

Private Sub ApplyHoursByYearPivotFallbackLayout(ByVal pt As PivotTable)
    If pt Is Nothing Then Exit Sub

    If PivotFieldExists(pt, "Years (Date)") Then
        TrySetPivotFieldOrientation pt, "Years (Date)", xlRowField

        On Error Resume Next
        pt.PivotFields("Years (Date)").Position = 1
        pt.PivotFields("Years (Date)").ShowDetail = False
        On Error GoTo 0

        TrySetPivotFieldOrientation pt, "Date", xlHidden
        TrySetPivotFieldOrientation pt, "Months (Date)", xlHidden
        TrySetPivotFieldOrientation pt, "Days (Date)", xlHidden
        TrySetPivotFieldOrientation pt, "Quarters (Date)", xlHidden
    Else
        ' If grouped fields do not exist, leave Date as the active row field.
        TrySetPivotFieldOrientation pt, "Date", xlRowField
    End If
End Sub

Private Function TryGroupDateByMonthAndYear(ByVal pt As PivotTable) As Boolean
    On Error GoTo GroupFailed

    pt.PivotFields("Date").LabelRange.Cells(2).Group _
        Start:=True, End:=True, _
        Periods:=Array(False, False, False, False, True, False, True)

    TryGroupDateByMonthAndYear = True
    Exit Function

GroupFailed:
    TryGroupDateByMonthAndYear = False
    Err.Clear
End Function

Private Function TrySetPivotFieldOrientation(ByVal pt As PivotTable, ByVal fieldName As String, ByVal orientation As XlPivotFieldOrientation) As Boolean
    On Error GoTo SetFailed

    pt.PivotFields(fieldName).Orientation = orientation
    TrySetPivotFieldOrientation = True
    Exit Function

SetFailed:
    TrySetPivotFieldOrientation = False
    Err.Clear
End Function

Private Function PivotFieldExists(ByVal pt As PivotTable, ByVal fieldName As String) As Boolean
    On Error Resume Next
    PivotFieldExists = Not pt.PivotFields(fieldName) Is Nothing
    On Error GoTo 0
End Function

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
    Set http = CreateDownloadHttpRequest()
    If http Is Nothing Then GoTo Fail
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
        Set http = CreateDownloadHttpRequest()
        If http Is Nothing Then GoTo Fail
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

Private Function CreateDownloadHttpRequest() As Object
    On Error Resume Next
    Set CreateDownloadHttpRequest = CreateObject("MSXML2.ServerXMLHTTP.6.0")
    If CreateDownloadHttpRequest Is Nothing Then
        Set CreateDownloadHttpRequest = CreateObject("MSXML2.XMLHTTP")
    End If
    On Error GoTo 0
End Function

Private Function TryLaunchExternalUpdaterWizard(ByVal sourceWorkbookPath As String, _
                                                ByVal repository As String, _
                                                Optional ByRef reason As String = "", _
                                                Optional ByVal masterWorkbookPath As String = "", _
                                                Optional ByVal targetVersion As String = "") As Boolean
    Dim wizardPath As String
    Dim commandLine As String
    Dim quotedExe As String
    Dim shellObj As Object

    On Error GoTo Fail

    wizardPath = ResolveWizardExecutablePath(repository, targetVersion)
    If wizardPath = "" Then
        If reason = "" Then
            If LCase$(Trim$(GetGitHubBranch())) <> "main" Then
                reason = "Development updater wizard could not be found or downloaded."
            Else
                reason = "No wizard asset was found in release assets."
            End If
        End If
        Exit Function
    End If
    If Dir$(wizardPath) = "" Then
        reason = "Wizard executable path could not be resolved."
        Exit Function
    End If

    quotedExe = """" & wizardPath & """"
    commandLine = quotedExe & " --source """ & sourceWorkbookPath & """ --repo """ & repository & """ --inplace"
    If masterWorkbookPath <> "" Then
        commandLine = commandLine & " --master """ & masterWorkbookPath & """"
        If LCase$(Trim$(GetGitHubBranch())) <> "main" Then
            commandLine = commandLine & " --channel development"
        End If
    End If

    Set shellObj = CreateObject("WScript.Shell")
    shellObj.Run commandLine, 1, False
    TryLaunchExternalUpdaterWizard = True
    Exit Function

Fail:
    reason = Err.Description
    TryLaunchExternalUpdaterWizard = False
End Function

Private Function ResolveWizardExecutablePath(ByVal repository As String, _
                                             Optional ByVal targetVersion As String = "") As String
    Dim namedPath As String
    Dim folderPath As String
    Dim candidate As String
    Dim tempFolder As String

    On Error Resume Next
    namedPath = Trim(CStr(ThisWorkbook.Names("UpdaterWizardPath").RefersToRange.Value))
    On Error GoTo 0
    If namedPath <> "" Then
        If Dir$(namedPath) <> "" Then
            ResolveWizardExecutablePath = namedPath
            Exit Function
        End If
    End If

    folderPath = ResolveLocalPath(ThisWorkbook)
    candidate = folderPath & "\" & WIZARD_EXE_NAME
    If Dir$(candidate) <> "" Then
        ResolveWizardExecutablePath = candidate
        Exit Function
    End If

    candidate = folderPath & "\updater\dist\" & WIZARD_EXE_NAME
    If Dir$(candidate) <> "" Then
        ResolveWizardExecutablePath = candidate
        Exit Function
    End If

    If LCase$(Trim$(GetGitHubBranch())) <> "main" Then
        tempFolder = Environ("TEMP") & "\ElectronicLogbookUpdaterDev"
        If mResolvedRef <> "" Then
            tempFolder = tempFolder & "_" & SafePathSegment(Left$(mResolvedRef, 12))
        ElseIf targetVersion <> "" Then
            tempFolder = tempFolder & "_" & SafePathSegment(targetVersion)
        End If
        If Dir$(tempFolder, vbDirectory) = "" Then MkDir tempFolder

        candidate = tempFolder & "\" & WIZARD_EXE_NAME
        If Dir$(candidate) = "" Then
            If Not DownloadDevelopmentWizardPackage(repository, candidate, tempFolder) Then
                ResolveWizardExecutablePath = ""
                Exit Function
            End If
        End If

        ResolveWizardExecutablePath = candidate
        Exit Function
    End If

    tempFolder = Environ("TEMP") & "\ElectronicLogbookUpdater"
    If targetVersion <> "" Then
        tempFolder = tempFolder & "_" & SafePathSegment(targetVersion)
    End If
    If Dir$(tempFolder, vbDirectory) = "" Then MkDir tempFolder

    candidate = tempFolder & "\" & WIZARD_EXE_NAME
    If Dir$(candidate) = "" Then
        If Not DownloadLatestWizardPackage(repository, candidate, tempFolder) Then
            ResolveWizardExecutablePath = ""
            Exit Function
        End If
    End If

    ResolveWizardExecutablePath = candidate
End Function

Private Function DownloadDevelopmentWizardPackage(ByVal repository As String, _
                                                  ByVal destinationExePath As String, _
                                                  ByVal tempFolder As String) As Boolean
    Dim downloadUrl As String
    Dim commitPath As String
    Dim publishedCommit As String
    Dim devWizardTag As String

    If mResolvedRef = "" Then Exit Function
    devWizardTag = DEV_WIZARD_TAG_PREFIX & SafePathSegment(Left$(mResolvedRef, 12))

    commitPath = tempFolder & "\" & DEV_WIZARD_COMMIT_NAME
    downloadUrl = "https://github.com/" & repository & "/releases/download/" & devWizardTag & "/" & DEV_WIZARD_COMMIT_NAME
    If Not DownloadFile(downloadUrl, commitPath) Then Exit Function

    publishedCommit = Trim$(ReadFirstTextLine(commitPath))
    If StrComp(publishedCommit, mResolvedRef, vbTextCompare) <> 0 Then Exit Function

    downloadUrl = "https://github.com/" & repository & "/releases/download/" & devWizardTag & "/" & WIZARD_EXE_NAME
    If TryDownloadWizardFromUrl(downloadUrl, destinationExePath, tempFolder) Then
        DownloadDevelopmentWizardPackage = True
        Exit Function
    End If

    downloadUrl = "https://github.com/" & repository & "/releases/download/" & devWizardTag & "/" & WIZARD_ZIP_NAME
    DownloadDevelopmentWizardPackage = TryDownloadWizardFromUrl(downloadUrl, destinationExePath, tempFolder)
End Function

Private Function SafePathSegment(ByVal value As String) As String
    Dim result As String
    Dim i As Long
    Dim ch As String

    result = ""
    For i = 1 To Len(value)
        ch = Mid$(value, i, 1)
        If (ch >= "0" And ch <= "9") Or _
           (ch >= "A" And ch <= "Z") Or _
           (ch >= "a" And ch <= "z") Or _
           ch = "." Or ch = "-" Or ch = "_" Then
            result = result & ch
        Else
            result = result & "_"
        End If
    Next i

    If result = "" Then result = "current"
    SafePathSegment = result
End Function

Private Function ReadFirstTextLine(ByVal filePath As String) As String
    Dim fileNumber As Integer
    Dim lineText As String

    On Error GoTo Fail
    fileNumber = FreeFile
    Open filePath For Input As #fileNumber
    Line Input #fileNumber, lineText
    Close #fileNumber
    ReadFirstTextLine = lineText
    Exit Function

Fail:
    On Error Resume Next
    If fileNumber <> 0 Then Close #fileNumber
    On Error GoTo 0
    ReadFirstTextLine = ""
End Function

Private Function DownloadLatestWizardPackage(ByVal repository As String, _
                                             ByVal destinationExePath As String, _
                                             ByVal tempFolder As String) As Boolean
    Dim downloadUrl As String

    downloadUrl = FetchLatestWizardDownloadUrl(repository)
    If downloadUrl <> "" Then
        If TryDownloadWizardFromUrl(downloadUrl, destinationExePath, tempFolder) Then
            DownloadLatestWizardPackage = True
            Exit Function
        End If
    End If

    downloadUrl = "https://github.com/" & repository & "/releases/latest/download/" & WIZARD_EXE_NAME
    If TryDownloadWizardFromUrl(downloadUrl, destinationExePath, tempFolder) Then
        DownloadLatestWizardPackage = True
        Exit Function
    End If

    downloadUrl = "https://github.com/" & repository & "/releases/latest/download/" & WIZARD_ZIP_NAME
    DownloadLatestWizardPackage = TryDownloadWizardFromUrl(downloadUrl, destinationExePath, tempFolder)
End Function

Private Function TryDownloadWizardFromUrl(ByVal downloadUrl As String, _
                                          ByVal destinationExePath As String, _
                                          ByVal tempFolder As String) As Boolean
    Dim lowerUrl As String
    Dim zipPath As String

    lowerUrl = LCase$(downloadUrl)
    If Right$(lowerUrl, 4) = ".zip" Then
        zipPath = tempFolder & "\" & WIZARD_ZIP_NAME
        If Not DownloadFile(downloadUrl, zipPath) Then
            TryDownloadWizardFromUrl = False
            Exit Function
        End If
        If Not ExtractZipArchive(zipPath, tempFolder) Then
            TryDownloadWizardFromUrl = False
            Exit Function
        End If

        Dim extractedExe As String
        extractedExe = FindFileByNameRecursive(tempFolder, WIZARD_EXE_NAME)
        If extractedExe = "" Then
            TryDownloadWizardFromUrl = False
            Exit Function
        End If

        On Error Resume Next
        If Dir$(destinationExePath) <> "" Then Kill destinationExePath
        Name extractedExe As destinationExePath
        If Err.Number <> 0 Then
            Err.Clear
            FileCopy extractedExe, destinationExePath
            If Err.Number <> 0 Then
                TryDownloadWizardFromUrl = False
                Exit Function
            End If
        End If
        On Error GoTo 0

        TryDownloadWizardFromUrl = (Dir$(destinationExePath) <> "")
        Exit Function
    End If

    TryDownloadWizardFromUrl = DownloadFile(downloadUrl, destinationExePath)
End Function

Private Function FetchLatestWizardDownloadUrl(ByVal repository As String) As String
    Dim http As Object
    Dim token As String
    Dim body As String
    Dim apiUrl As String

    On Error GoTo Fail
    apiUrl = "https://api.github.com/repos/" & repository & "/releases/latest"

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
        ' public release asset discovery.
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
    FetchLatestWizardDownloadUrl = ExtractWizardDownloadUrl(body)
    Exit Function
Fail:
    FetchLatestWizardDownloadUrl = ""
End Function

Private Function LatestReleaseMatchesVersion(ByVal repository As String, ByVal version As String) As Boolean
    Dim tag As String

    tag = FetchLatestReleaseTag(repository)
    If tag = "" Then
        LatestReleaseMatchesVersion = False
    Else
        LatestReleaseMatchesVersion = (LCase$(tag) = LCase$("v" & version))
    End If
End Function

Private Function FetchLatestReleaseTag(ByVal repository As String) As String
    Dim http As Object
    Dim token As String
    Dim body As String
    Dim apiUrl As String

    On Error GoTo Fail
    apiUrl = "https://api.github.com/repos/" & repository & "/releases/latest"

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
    FetchLatestReleaseTag = ExtractJsonStringValue(body, "tag_name")
    Exit Function
Fail:
    FetchLatestReleaseTag = ""
End Function

Private Function ExtractWizardDownloadUrl(ByVal jsonText As String) As String
    Dim re As Object
    Dim matches As Object
    Dim value As String

    On Error GoTo Fail
    Set re = CreateObject("VBScript.RegExp")
    re.Pattern = """browser_download_url""\s*:\s*""([^""]*ElectronicLogbook\.Updater\.Wizard[^""]*\.(exe|zip))"""
    re.Global = False
    re.IgnoreCase = True

    If re.Test(jsonText) Then
        Set matches = re.Execute(jsonText)
        value = CStr(matches(0).SubMatches(0))
        value = Replace(value, "\u0026", "&")
        value = Replace(value, "\/", "/")
        ExtractWizardDownloadUrl = value
    End If
    Exit Function
Fail:
    ExtractWizardDownloadUrl = ""
End Function

Private Function ExtractJsonStringValue(ByVal jsonText As String, ByVal propertyName As String) As String
    Dim re As Object
    Dim matches As Object
    Dim patternName As String

    On Error GoTo Fail
    patternName = propertyName

    Set re = CreateObject("VBScript.RegExp")
    re.Pattern = """" & patternName & """\s*:\s*""([^""]*)"""
    re.Global = False
    re.IgnoreCase = True

    If re.Test(jsonText) Then
        Set matches = re.Execute(jsonText)
        ExtractJsonStringValue = CStr(matches(0).SubMatches(0))
        ExtractJsonStringValue = Replace(ExtractJsonStringValue, "\u0026", "&")
        ExtractJsonStringValue = Replace(ExtractJsonStringValue, "\/", "/")
    End If
    Exit Function
Fail:
    ExtractJsonStringValue = ""
End Function

Private Function ExtractZipArchive(ByVal zipPath As String, ByVal destinationFolder As String) As Boolean
    Dim shellObj As Object
    Dim command As String
    Dim escapedZip As String
    Dim escapedDest As String
    Dim exitCode As Long

    On Error GoTo Fail
    escapedZip = Replace(zipPath, "'", "''")
    escapedDest = Replace(destinationFolder, "'", "''")

    command = "powershell -NoProfile -ExecutionPolicy Bypass -Command ""Expand-Archive -LiteralPath '" & _
              escapedZip & "' -DestinationPath '" & escapedDest & "' -Force"""

    Set shellObj = CreateObject("WScript.Shell")
    exitCode = shellObj.Run(command, 0, True)
    ExtractZipArchive = (exitCode = 0)
    Exit Function
Fail:
    ExtractZipArchive = False
End Function

Private Function FindFileByNameRecursive(ByVal rootFolder As String, ByVal fileName As String) As String
    Dim fso As Object
    Set fso = CreateObject("Scripting.FileSystemObject")
    If Not fso.FolderExists(rootFolder) Then Exit Function

    FindFileByNameRecursive = FindFileByNameRecursiveInner(fso.GetFolder(rootFolder), fileName)
End Function

Private Function FindFileByNameRecursiveInner(ByVal folderObj As Object, ByVal fileName As String) As String
    Dim fileObj As Object
    Dim subFolder As Object
    Dim candidate As String

    For Each fileObj In folderObj.Files
        If StrComp(fileObj.Name, fileName, vbTextCompare) = 0 Then
            FindFileByNameRecursiveInner = CStr(fileObj.Path)
            Exit Function
        End If
    Next fileObj

    For Each subFolder In folderObj.SubFolders
        candidate = FindFileByNameRecursiveInner(subFolder, fileName)
        If candidate <> "" Then
            FindFileByNameRecursiveInner = candidate
            Exit Function
        End If
    Next subFolder
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

' ==============================================================
' STAGED UPDATE VALIDATION
' ==============================================================
' Opens the staged workbook read-only and confirms it is safe to
' use as a replacement before any destructive operations begin.
' Returns True only when all checks pass; writes a debug entry
' and returns False on any failure so the caller can abort cleanly.

Private Function ValidateStagedUpdate(stagedPath As String, _
                                      expectedVersion As String, _
                                      expectedRows As Long, _
                                      expectedTotalHours As Double, _
                                      expectedTotalKnown As Boolean) As Boolean
    Dim stagedWb   As Workbook
    Dim tbl        As ListObject
    Dim actualRows As Long
    Dim actualVer  As String
    Dim actualTotalHours As Double
    Dim reqName    As Variant
    Dim failReason As String
    Dim prevSec    As MsoAutomationSecurity

    ' Prevent macros in the staged file from running during validation.
    prevSec = Application.AutomationSecurity
    Application.AutomationSecurity = msoAutomationSecurityForceDisable

    On Error GoTo ValidationFailed

    Set stagedWb = Workbooks.Open(stagedPath, ReadOnly:=True, UpdateLinks:=False)

    ' 1. Logbook table must exist.
    On Error Resume Next
    Set tbl = stagedWb.Sheets("Logbook").ListObjects("Logbook")
    On Error GoTo ValidationFailed
    If tbl Is Nothing Then
        failReason = "Logbook table missing from staged file."
        GoTo ValidationFailed
    End If

    ' 2. Row count must match the source workbook.
    On Error Resume Next
    actualRows = tbl.DataBodyRange.Rows.Count
    On Error GoTo ValidationFailed
    If expectedRows > 0 And actualRows <> expectedRows Then
        failReason = "Row count mismatch: expected " & expectedRows & ", found " & actualRows & "."
        GoTo ValidationFailed
    End If

    ' 3. Version stamp must match the target version.
    On Error Resume Next
    actualVer = Trim(CStr(stagedWb.Names("LogbookVersion").RefersToRange.Value))
    On Error GoTo ValidationFailed
    If actualVer <> expectedVersion Then
        failReason = "Version mismatch: expected " & expectedVersion & ", found """ & actualVer & """."
        GoTo ValidationFailed
    End If

    ' 4. Required names must exist in the staged workbook.
    For Each reqName In Array("LogbookVersion", "GitHubBranch", "DateAfterExport", _
                              "RoutesBuilt", "RoutesDirty", "RoutesDefinitionVersion", _
                              "suppressWarningsUntil")
        On Error Resume Next
        Dim nm As Name
        Set nm = stagedWb.Names(CStr(reqName))
        On Error GoTo ValidationFailed
        If nm Is Nothing Then
            failReason = "Required name missing: " & CStr(reqName)
            GoTo ValidationFailed
        End If
        Set nm = Nothing
    Next reqName

    ' 5. Keywords table must be present.
    ' The table can live on different sheets across versions,
    ' so find it by name instead of hard-coding a sheet.
    Dim kw As ListObject
    Set kw = FindListObject(stagedWb, "Keywords")
    If kw Is Nothing Then
        failReason = "Keywords table missing from staged file."
        GoTo ValidationFailed
    End If

    ' 6. Total-hours sanity check when the source total is available.
    If expectedTotalKnown Then
        actualTotalHours = GetLogbookTotalHours(stagedWb)
        If Abs(actualTotalHours - expectedTotalHours) > 0.01 Then
            failReason = "Total-hours mismatch: expected " & expectedTotalHours & ", found " & actualTotalHours & "."
            GoTo ValidationFailed
        End If
    End If

    ' All checks passed.
    stagedWb.Close SaveChanges:=False
    Set stagedWb = Nothing
    Application.AutomationSecurity = prevSec
    ValidateStagedUpdate = True
    Exit Function

ValidationFailed:
    Dim errN As Long
    Dim errD As String
    errN = Err.Number
    errD = Err.Description
    If failReason = "" Then failReason = errD
    If failReason = "" Then failReason = "Staged update validation failed."
    mLastUpdateFailureReason = failReason
    On Error Resume Next
    If Not stagedWb Is Nothing Then stagedWb.Close SaveChanges:=False
    Application.AutomationSecurity = prevSec
    Application.Run "WriteDebugLog", "modUpdate.ValidateStagedUpdate", errN, failReason, "Validating staged update"
    On Error GoTo 0
    ValidateStagedUpdate = False
End Function

Private Function GetLogbookTotalHours(wb As Workbook) As Double
    Dim tbl As ListObject
    Set tbl = wb.Sheets("Logbook").ListObjects("Logbook")

    If tbl Is Nothing Then Err.Raise vbObjectError + 900, , "Logbook table missing"
    If tbl.DataBodyRange Is Nothing Then
        GetLogbookTotalHours = 0
        Exit Function
    End If

    Dim totalCol As Long
    totalCol = tbl.ListColumns("Total").Index
    GetLogbookTotalHours = Application.WorksheetFunction.Sum(tbl.ListColumns(totalCol).DataBodyRange)
End Function
