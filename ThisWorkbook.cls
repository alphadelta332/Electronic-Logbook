Option Explicit

Private mHandlingNewEntryLayoutChange As Boolean
Private mOpenStatusActive As Boolean
Private mOpenStatusStartedAt As Single
Private mOpenStatusLastStepStartedAt As Single
Private mOpenStatusCurrentStep As String
Private mOpenStatusPreviousDisplayStatusBar As Boolean
Private mOpenStatusPreviousStatusBar As Variant

Private Sub Workbook_Open()
    Dim openErrNum As Long
    Dim openErrDesc As String
    Dim openErrSource As String

    On Error GoTo Fail

    BeginWorkbookOpenStatus

    ' Restore any Application state left dirty by a previous crash.
    ' AddToLogbook disables these for performance; a hard crash bypasses Cleanup.
    SetWorkbookOpenStatus "Restoring Excel application state"
    Application.ScreenUpdating = True
    Application.EnableEvents = True
    Application.Calculation = xlCalculationAutomatic
    Application.CutCopyMode = False

    ' Re-apply protection each time the workbook opens.
    SetWorkbookOpenStatus "Applying workbook protection"
    EnsureWorkbookProtectionOnOpen

    ' Keep date-sensitive formulas current without a volatile TODAY() formula.
    SetWorkbookOpenStatus "Refreshing date-sensitive values"
    RefreshTodayValue
    SetWorkbookOpenStatus "Checking currency and recency formulas"
    EnsureCurrencyRecencyFormulaCompatibility ThisWorkbook

    ' Check for logbook updates
    SetWorkbookOpenStatus "Checking for workbook updates"
    modBoot.CheckForUpdate

    ' Refresh airport reference data before route-cache checks.
    SetWorkbookOpenStatus "Checking airport reference data"
    CheckAirportDatasetOnOpen ThisWorkbook

    ' Check Status of SuppressWarnings
    SetWorkbookOpenStatus "Refreshing warning controls"
    RefreshSuppressWarningsButton

    ' Apply the selected New Entry layout and keep layout radio controls configured.
    SetWorkbookOpenStatus "Preparing New Entry layout"
    InitialiseNewEntryLayoutUI
    SetWorkbookOpenStatus "Fitting Stats sheet columns"
    AutoFitStatsSheetColumns

    ' Reset split date entry fields to today on launch when configured.
    SetWorkbookOpenStatus "Resetting New Entry date"
    SetNewEntryDateToTodayOnOpen

    ' Prompt only when the routes cache is missing or was built with an old parser.
    SetWorkbookOpenStatus "Checking routes cache"
    CheckRoutesTableOnOpen ThisWorkbook

    ' Always start interactive sessions on the primary entry field.
    SetWorkbookOpenStatus "Opening New Entry screen"
    NavigateToDefaultOpenView

    FinishWorkbookOpenStatus "Ready"
    Exit Sub

Fail:
    openErrNum = Err.Number
    openErrDesc = Err.Description
    openErrSource = Err.Source
    On Error Resume Next
    SetWorkbookOpenStatus "Open failed during " & mOpenStatusCurrentStep
    FinishWorkbookOpenStatus "Open failed"
    On Error GoTo 0
    Err.Raise openErrNum, openErrSource, openErrDesc
End Sub

Private Sub BeginWorkbookOpenStatus()
    mOpenStatusPreviousDisplayStatusBar = Application.DisplayStatusBar
    mOpenStatusPreviousStatusBar = Application.StatusBar
    mOpenStatusStartedAt = Timer
    mOpenStatusLastStepStartedAt = mOpenStatusStartedAt
    mOpenStatusCurrentStep = ""
    mOpenStatusActive = True
    Application.DisplayStatusBar = True
    SetWorkbookOpenStatus "Starting"
End Sub

Private Sub SetWorkbookOpenStatus(ByVal stepText As String)
    Dim elapsedSeconds As Double

    If Not mOpenStatusActive Then Exit Sub

    If mOpenStatusCurrentStep <> "" Then
        Debug.Print Format$(Now, "yyyy-mm-dd hh:nn:ss") & _
                    " Electronic Logbook open step completed: " & _
                    mOpenStatusCurrentStep & " (" & _
                    Format$(ElapsedWorkbookOpenSeconds(mOpenStatusLastStepStartedAt), "0.0") & "s)"
    End If

    mOpenStatusCurrentStep = stepText
    mOpenStatusLastStepStartedAt = Timer
    elapsedSeconds = ElapsedWorkbookOpenSeconds(mOpenStatusStartedAt)
    Application.StatusBar = "Electronic Logbook: opening workbook - " & _
                            stepText & " (" & Format$(elapsedSeconds, "0.0") & "s elapsed)"
    DoEvents
End Sub

Private Sub FinishWorkbookOpenStatus(ByVal finalStepText As String)
    If Not mOpenStatusActive Then Exit Sub

    If finalStepText <> "" Then SetWorkbookOpenStatus finalStepText
    Debug.Print Format$(Now, "yyyy-mm-dd hh:nn:ss") & _
                " Electronic Logbook open completed in " & _
                Format$(ElapsedWorkbookOpenSeconds(mOpenStatusStartedAt), "0.0") & "s"

    If mOpenStatusPreviousDisplayStatusBar Then
        If VarType(mOpenStatusPreviousStatusBar) = vbString Then
            Application.StatusBar = CStr(mOpenStatusPreviousStatusBar)
        Else
            Application.StatusBar = False
        End If
    Else
        Application.StatusBar = False
    End If
    Application.DisplayStatusBar = mOpenStatusPreviousDisplayStatusBar

    mOpenStatusActive = False
    mOpenStatusCurrentStep = ""
End Sub

Private Function ElapsedWorkbookOpenSeconds(ByVal startedAt As Single) As Double
    Dim nowValue As Single

    nowValue = Timer
    If nowValue >= startedAt Then
        ElapsedWorkbookOpenSeconds = CDbl(nowValue - startedAt)
    Else
        ElapsedWorkbookOpenSeconds = CDbl((86400! - startedAt) + nowValue)
    End If
End Function

Private Sub Workbook_SheetActivate(ByVal Sh As Object)
    On Error GoTo CleanExit

    If Sh.Name = "Currency + Recency" Then
        EnsureCurrencyRecencyFormulaCompatibility ThisWorkbook
        If RefreshTodayValue() Then Sh.Calculate
    End If
    If Sh.Name = "Stats" Then AutoFitStatsSheetColumns

CleanExit:
End Sub

Private Sub Workbook_SheetCalculate(ByVal Sh As Object)
    On Error GoTo CleanExit
    If Sh.Name = "Stats" Then AutoFitStatsSheetColumns

CleanExit:
End Sub

Private Sub NavigateToDefaultOpenView()
    Dim targetCell As Range

    On Error GoTo CleanExit

    ThisWorkbook.Worksheets("New Entry").Activate
    Set targetCell = ThisWorkbook.Names("neDay").RefersToRange
    If targetCell.MergeCells Then Set targetCell = targetCell.MergeArea.Cells(1, 1)
    targetCell.Select
    ActiveWindow.ScrollRow = 1
    ActiveWindow.ScrollColumn = 1

CleanExit:
End Sub

Private Sub Workbook_SheetChange(ByVal Sh As Object, ByVal Target As Range)
    Dim layoutCell As Range
    Dim statsSheetChanged As Boolean

    If mHandlingNewEntryLayoutChange Then Exit Sub
    If Target Is Nothing Then Exit Sub

    On Error GoTo CleanExit
    statsSheetChanged = (Sh.Name = "Stats")
    If Sh.Name = "New Entry" Then ClearNewEntryValidationHighlightsForTarget Target

    Set layoutCell = ThisWorkbook.Names("NewEntryLayout").RefersToRange
    If layoutCell Is Nothing Then
        If statsSheetChanged Then AutoFitStatsSheetColumns
        GoTo CleanExit
    End If

    If Intersect(Target, layoutCell) Is Nothing Then
        If statsSheetChanged Then AutoFitStatsSheetColumns
        GoTo CleanExit
    End If

    mHandlingNewEntryLayoutChange = True
    ApplyConfiguredNewEntryLayout layoutCell.Value

CleanExit:
    mHandlingNewEntryLayoutChange = False
End Sub

Public Sub AutoFitStatsSheetColumns()
    Static isRunning As Boolean
    Dim ws As Worksheet
    Dim tblBase As ListObject

    If isRunning Then Exit Sub
    On Error GoTo CleanExit

    isRunning = True
    Set ws = ThisWorkbook.Worksheets("Stats")
    ws.UsedRange.Columns.AutoFit

    On Error Resume Next
    Set tblBase = ws.ListObjects("BaseAirportsTop10")
    If Not tblBase Is Nothing Then
        tblBase.ListColumns("ICAO").Range.EntireColumn.Hidden = True
    End If
    On Error GoTo CleanExit

CleanExit:
    isRunning = False
End Sub

Private Sub SetNewEntryDateToTodayOnOpen()
    On Error GoTo CleanExit

    If Val(CStr(Range("DateAfterExport").Value)) <> 3 Then Exit Sub

    Range("neYear").Value = Year(Date)
    Range("neMonth").Value = Format$(Date, "mmm")
    Range("neDay").Value = Day(Date)

    Range("neYear").NumberFormat = "0"
    Range("neMonth").NumberFormat = "@"
    Range("neDay").NumberFormat = "0"

CleanExit:
End Sub
