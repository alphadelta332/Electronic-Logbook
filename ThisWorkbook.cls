Option Explicit

Private mHandlingNewEntryLayoutChange As Boolean

Private Sub Workbook_Open()
    ' Restore any Application state left dirty by a previous crash.
    ' AddToLogbook disables these for performance; a hard crash bypasses Cleanup.
    Application.ScreenUpdating = True
    Application.EnableEvents = True
    Application.Calculation = xlCalculationAutomatic
    Application.CutCopyMode = False

    ' Re-apply protection each time the workbook opens.
    EnsureWorkbookProtectionOnOpen

    ' Keep date-sensitive formulas current without a volatile TODAY() formula.
    RefreshTodayValue

    ' Check for logbook updates
    modBoot.CheckForUpdate

    ' Check Status of SuppressWarnings
    RefreshSuppressWarningsButton

    ' Apply the selected New Entry layout and keep layout radio controls configured.
    InitialiseNewEntryLayoutUI

    ' Reset split date entry fields to today on launch when configured.
    SetNewEntryDateToTodayOnOpen

    ' Prompt only when the routes cache is missing or was built with an old parser.
    CheckRoutesTableOnOpen ThisWorkbook

    ' Always start interactive sessions on the primary entry field.
    NavigateToDefaultOpenView
End Sub

Private Sub Workbook_SheetActivate(ByVal Sh As Object)
    On Error GoTo CleanExit

    If Sh.Name = "Currency + Recency" Then
        If RefreshTodayValue() Then Sh.Calculate
    End If

CleanExit:
End Sub

Private Sub NavigateToDefaultOpenView()
    Dim targetCell As Range

    On Error GoTo CleanExit

    ThisWorkbook.Worksheets("New Entry").Activate
    Set targetCell = ThisWorkbook.Names("neDay").RefersToRange
    If targetCell.MergeCells Then Set targetCell = targetCell.MergeArea.Cells(1, 1)
    Application.Goto targetCell, True

CleanExit:
End Sub

Private Sub Workbook_SheetChange(ByVal Sh As Object, ByVal Target As Range)
    Dim layoutCell As Range

    If mHandlingNewEntryLayoutChange Then Exit Sub
    If Target Is Nothing Then Exit Sub

    On Error GoTo CleanExit
    Set layoutCell = ThisWorkbook.Names("NewEntryLayout").RefersToRange
    If layoutCell Is Nothing Then GoTo CleanExit

    If Intersect(Target, layoutCell) Is Nothing Then GoTo CleanExit

    mHandlingNewEntryLayoutChange = True
    ApplyConfiguredNewEntryLayout layoutCell.Value

CleanExit:
    mHandlingNewEntryLayoutChange = False
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
