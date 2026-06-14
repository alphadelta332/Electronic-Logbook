Private Sub Workbook_Open()
    ' Restore any Application state left dirty by a previous crash.
    ' AddToLogbook disables these for performance; a hard crash bypasses Cleanup.
    Application.ScreenUpdating = True
    Application.EnableEvents = True
    Application.Calculation = xlCalculationAutomatic
    Application.CutCopyMode = False

    ' Keep date-sensitive formulas current without a volatile TODAY() formula.
    RefreshTodayValue

    ' Check for logbook updates
    modBoot.CheckForUpdate

    ' Check Status of SuppressWarnings
    RefreshSuppressWarningsButton

    ' Reset split date entry fields to today on launch when configured.
    SetNewEntryDateToTodayOnOpen

    ' Prompt only when the routes cache is missing or was built with an old parser.
    CheckRoutesTableOnOpen ThisWorkbook
End Sub

Private Sub Workbook_SheetActivate(ByVal Sh As Object)
    On Error GoTo CleanExit

    If Sh.Name = "Currency + Recency" Then
        If RefreshTodayValue() Then Sh.Calculate
    End If

CleanExit:
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
