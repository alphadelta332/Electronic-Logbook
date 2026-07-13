function Add-ExportLogbookForm {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        $Workbook
    )

    $components = $Workbook.VBProject.VBComponents
    $form = $components.Add(3)
    $form.Name = "frmExportLogbook"
    $form.Properties.Item("Caption").Value = "Export Logbook"
    $form.Properties.Item("Width").Value = 570
    $form.Properties.Item("Height").Value = 420
    $form.Properties.Item("BackColor").Value = 0x00F5F5F5
    $form.Properties.Item("StartUpPosition").Value = 1

    $designer = $form.Designer

    function Add-Control {
        param(
            [Parameter(Mandatory)] $Container,
            [Parameter(Mandatory)] [string] $Type,
            [Parameter(Mandatory)] [string] $Name,
            [Parameter(Mandatory)] [hashtable] $Properties
        )

        $control = $Container.Controls.Add($Type, $Name, $true)
        foreach ($propertyName in $Properties.Keys) {
            $control.$propertyName = $Properties[$propertyName]
        }
        return $control
    }

    function Set-ControlFont {
        param(
            [Parameter(Mandatory)] $Control,
            [double] $Size = 9,
            [bool] $Bold = $false
        )

        if ($Control.PSObject.Properties.Name -contains "FontName") {
            $Control.FontName = "Segoe UI"
            $Control.FontSize = $Size
            $Control.FontBold = $Bold
        } else {
            $font = $Control.Font
            $font.Name = "Segoe UI"
            $font.Size = $Size
            $font.Bold = $Bold
        }
    }

    $header = Add-Control $designer "Forms.Label.1" "lblHeader" @{
        Left = 0; Top = 0; Width = 565; Height = 64
        BackStyle = 1; BackColor = 0x00663322; Caption = ""
    }
    $title = Add-Control $designer "Forms.Label.1" "lblTitle" @{
        Left = 20; Top = 13; Width = 300; Height = 24
        BackStyle = 0; ForeColor = 0x00FFFFFF; Caption = "Export Logbook"
    }
    Set-ControlFont $title 16 $true
    $subtitle = Add-Control $designer "Forms.Label.1" "lblSubtitle" @{
        Left = 21; Top = 39; Width = 500; Height = 15
        BackStyle = 0; ForeColor = 0x00E6E6E6
        Caption = "Choose what to export and where to save it."
    }
    Set-ControlFont $subtitle 9 $false

    $formatFrame = Add-Control $designer "Forms.Frame.1" "fraFormat" @{
        Left = 18; Top = 78; Width = 255; Height = 125
        Caption = " File format "; BackColor = 0x00FFFFFF
        ForeColor = 0x00333333; SpecialEffect = 0
    }
    Set-ControlFont $formatFrame 9 $true

    $xlsx = Add-Control $formatFrame "Forms.OptionButton.1" "optXlsx" @{
        Left = 15; Top = 22; Width = 220; Height = 18
        Caption = "Excel workbook (.xlsx)"; BackColor = 0x00FFFFFF
    }
    $csv = Add-Control $formatFrame "Forms.OptionButton.1" "optCsv" @{
        Left = 15; Top = 52; Width = 220; Height = 18
        Caption = "CSV data file (.csv)"; BackColor = 0x00FFFFFF
    }
    $pdf = Add-Control $formatFrame "Forms.OptionButton.1" "optPdf" @{
        Left = 15; Top = 82; Width = 220; Height = 18
        Caption = "Printable document (.pdf)"; BackColor = 0x00FFFFFF
    }
    Set-ControlFont $xlsx
    Set-ControlFont $csv
    Set-ControlFont $pdf

    $detailsFrame = Add-Control $designer "Forms.Frame.1" "fraDetails" @{
        Left = 290; Top = 78; Width = 255; Height = 125
        Caption = " Details layout "; BackColor = 0x00FFFFFF
        ForeColor = 0x00333333; SpecialEffect = 0
    }
    Set-ControlFont $detailsFrame 9 $true

    $combined = Add-Control $detailsFrame "Forms.OptionButton.1" "optCombined" @{
        Left = 15; Top = 22; Width = 220; Height = 18
        Caption = "Combined details field"; BackColor = 0x00FFFFFF
    }
    $combinedInfo = Add-Control $detailsFrame "Forms.Label.1" "lblCombinedInfo" @{
        Left = 34; Top = 42; Width = 205; Height = 25
        BackStyle = 0; ForeColor = 0x00707070
        Caption = "Route, remarks and checks in one readable field."
    }
    $separate = Add-Control $detailsFrame "Forms.OptionButton.1" "optSeparate" @{
        Left = 15; Top = 78; Width = 220; Height = 18
        Caption = "Keep fields separate"; BackColor = 0x00FFFFFF
    }
    Set-ControlFont $combined
    Set-ControlFont $combinedInfo 8 $false
    Set-ControlFont $separate

    $dateFrame = Add-Control $designer "Forms.Frame.1" "fraDates" @{
        Left = 18; Top = 218; Width = 527; Height = 100
        Caption = " Date range "; BackColor = 0x00FFFFFF
        ForeColor = 0x00333333; SpecialEffect = 0
    }
    Set-ControlFont $dateFrame 9 $true

    $allDates = Add-Control $dateFrame "Forms.CheckBox.1" "chkAllDates" @{
        Left = 15; Top = 20; Width = 130; Height = 18
        Caption = "Export all entries"; BackColor = 0x00FFFFFF
    }
    $startLabel = Add-Control $dateFrame "Forms.Label.1" "lblStartDate" @{
        Left = 15; Top = 57; Width = 45; Height = 18
        BackStyle = 0; Caption = "Start"
    }
    $startYear = Add-Control $dateFrame "Forms.ComboBox.1" "txtStartYear" @{
        Left = 62; Top = 53; Width = 50; Height = 23
        SpecialEffect = 2
        Style = 2
    }
    $startMonth = Add-Control $dateFrame "Forms.ComboBox.1" "cboStartMonth" @{
        Left = 118; Top = 53; Width = 62; Height = 23
        SpecialEffect = 2
        Style = 2
    }
    $startDay = Add-Control $dateFrame "Forms.ComboBox.1" "txtStartDay" @{
        Left = 186; Top = 53; Width = 36; Height = 23
        SpecialEffect = 2
        Style = 2
    }
    $endLabel = Add-Control $dateFrame "Forms.Label.1" "lblEndDate" @{
        Left = 270; Top = 57; Width = 35; Height = 18
        BackStyle = 0; Caption = "End"
    }
    $endYear = Add-Control $dateFrame "Forms.ComboBox.1" "txtEndYear" @{
        Left = 312; Top = 53; Width = 50; Height = 23
        SpecialEffect = 2
        Style = 2
    }
    $endMonth = Add-Control $dateFrame "Forms.ComboBox.1" "cboEndMonth" @{
        Left = 368; Top = 53; Width = 62; Height = 23
        SpecialEffect = 2
        Style = 2
    }
    $endDay = Add-Control $dateFrame "Forms.ComboBox.1" "txtEndDay" @{
        Left = 436; Top = 53; Width = 36; Height = 23
        SpecialEffect = 2
        Style = 2
    }
    $dateHint = Add-Control $dateFrame "Forms.Label.1" "lblDateHint" @{
        Left = 15; Top = 78; Width = 500; Height = 14
        BackStyle = 0; ForeColor = 0x00707070
        Caption = "Use Year / Month / Day. Leave a whole start or end date blank for an open-ended range."
    }
    Set-ControlFont $allDates
    Set-ControlFont $startLabel
    Set-ControlFont $startYear
    Set-ControlFont $startMonth
    Set-ControlFont $startDay
    Set-ControlFont $endLabel
    Set-ControlFont $endYear
    Set-ControlFont $endMonth
    Set-ControlFont $endDay
    Set-ControlFont $dateHint 8 $false

    $status = Add-Control $designer "Forms.Label.1" "lblStatus" @{
        Left = 20; Top = 337; Width = 335; Height = 30
        BackStyle = 0; ForeColor = 0x00666666
        Caption = "Ready to export."
    }
    Set-ControlFont $status 8 $false

    $cancel = Add-Control $designer "Forms.CommandButton.1" "cmdCancel" @{
        Left = 374; Top = 347; Width = 80; Height = 30
        Caption = "Cancel"; BackColor = 0x00E6E6E6
        ForeColor = 0x00333333; Cancel = $true
    }
    $export = Add-Control $designer "Forms.CommandButton.1" "cmdExport" @{
        Left = 465; Top = 347; Width = 80; Height = 30
        Caption = "Export"; BackColor = 0x00CC6600
        ForeColor = 0x00FFFFFF; Default = $true
    }
    Set-ControlFont $cancel 9 $true
    Set-ControlFont $export 9 $true

    $code = @'
Option Explicit

Private Sub UserForm_Initialize()
    optXlsx.Value = True
    optCombined.Value = True
    PopulateDateLists
    chkAllDates.Value = True
    ToggleDateFields
    UpdateSummary
End Sub

Private Sub optXlsx_Click()
    UpdateSummary
End Sub

Private Sub optCsv_Click()
    UpdateSummary
End Sub

Private Sub optPdf_Click()
    UpdateSummary
End Sub

Private Sub optCombined_Click()
    UpdateSummary
End Sub

Private Sub optSeparate_Click()
    UpdateSummary
End Sub

Private Sub chkAllDates_Click()
    ToggleDateFields
    UpdateSummary
End Sub

Private Sub txtStartYear_Change()
    UpdateSummary
End Sub

Private Sub cboStartMonth_Change()
    UpdateSummary
End Sub

Private Sub txtStartDay_Change()
    UpdateSummary
End Sub

Private Sub txtEndYear_Change()
    UpdateSummary
End Sub

Private Sub cboEndMonth_Change()
    UpdateSummary
End Sub

Private Sub txtEndDay_Change()
    UpdateSummary
End Sub

Private Sub cmdExport_Click()
    Dim startDate As Variant
    Dim endDate As Variant
    Dim outputPath As String
    Dim errorText As String
    Dim focusControlName As String

    ClearError

    If Not chkAllDates.Value Then
        If Not TryReadOptionalDate("start date", txtStartYear.Value, _
                                   cboStartMonth.Value, txtStartDay.Value, _
                                   startDate, focusControlName) Then
            ShowError "Enter a complete, valid start date."
            Me.Controls(focusControlName).SetFocus
            Exit Sub
        End If

        If Not TryReadOptionalDate("end date", txtEndYear.Value, _
                                   cboEndMonth.Value, txtEndDay.Value, _
                                   endDate, focusControlName) Then
            ShowError "Enter a complete, valid end date."
            Me.Controls(focusControlName).SetFocus
            Exit Sub
        End If

        If Not IsEmpty(startDate) And Not IsEmpty(endDate) Then
            If CDate(startDate) > CDate(endDate) Then
                ShowError "The start date cannot be later than the end date."
                Exit Sub
            End If
        End If
    End If

    outputPath = ChooseLogbookExportPath(SelectedFormat())
    If Len(outputPath) = 0 Then Exit Sub

    If Len(Dir$(outputPath)) > 0 Then
        If MsgBox("The selected file already exists. Replace it?", _
                  vbYesNo + vbExclamation, "Export Logbook") <> vbYes Then Exit Sub
    End If

    SetBusy True
    If ExportLogbookToFile(outputPath, SelectedFormat(), _
                           optCombined.Value, startDate, endDate, False) Then
        SetBusy False
        MsgBox "Logbook exported successfully to:" & vbCrLf & outputPath, _
               vbInformation, "Export Logbook"
        Unload Me
    Else
        errorText = LastLogbookExportError()
        If Len(errorText) = 0 Then errorText = "The export could not be completed."
        SetBusy False
        ShowError errorText
    End If
End Sub

Private Sub cmdCancel_Click()
    Unload Me
End Sub

Private Function SelectedFormat() As String
    If optCsv.Value Then
        SelectedFormat = "csv"
    ElseIf optPdf.Value Then
        SelectedFormat = "pdf"
    Else
        SelectedFormat = "xlsx"
    End If
End Function

Private Sub ToggleDateFields()
    Dim datesEnabled As Boolean
    Dim fieldBackColor As Long

    datesEnabled = Not chkAllDates.Value
    If datesEnabled Then
        fieldBackColor = vbWhite
    Else
        fieldBackColor = RGB(242, 242, 242)
    End If

    txtStartYear.Enabled = datesEnabled
    cboStartMonth.Enabled = datesEnabled
    txtStartDay.Enabled = datesEnabled
    txtEndYear.Enabled = datesEnabled
    cboEndMonth.Enabled = datesEnabled
    txtEndDay.Enabled = datesEnabled
    txtStartYear.BackColor = fieldBackColor
    cboStartMonth.BackColor = fieldBackColor
    txtStartDay.BackColor = fieldBackColor
    txtEndYear.BackColor = fieldBackColor
    cboEndMonth.BackColor = fieldBackColor
    txtEndDay.BackColor = fieldBackColor
    lblStartDate.Enabled = datesEnabled
    lblEndDate.Enabled = datesEnabled
    lblDateHint.Enabled = datesEnabled
    If chkAllDates.Value Then
        txtStartYear.ListIndex = -1
        cboStartMonth.ListIndex = -1
        txtStartDay.ListIndex = -1
        txtEndYear.ListIndex = -1
        cboEndMonth.ListIndex = -1
        txtEndDay.ListIndex = -1
    End If
End Sub

Private Sub PopulateDateLists()
    Dim yearNumber As Long
    Dim monthName As Variant
    Dim dayNumber As Long
    Dim minYear As Long
    Dim maxYear As Long

    GetLogbookExportYearBounds minYear, maxYear
    For yearNumber = minYear To maxYear
        txtStartYear.AddItem CStr(yearNumber)
        txtEndYear.AddItem CStr(yearNumber)
    Next yearNumber

    For Each monthName In Array("Jan", "Feb", "Mar", "Apr", "May", "Jun", _
                                "Jul", "Aug", "Sep", "Oct", "Nov", "Dec")
        cboStartMonth.AddItem CStr(monthName)
        cboEndMonth.AddItem CStr(monthName)
    Next monthName

    For dayNumber = 1 To 31
        txtStartDay.AddItem CStr(dayNumber)
        txtEndDay.AddItem CStr(dayNumber)
    Next dayNumber
End Sub

Private Sub GetLogbookExportYearBounds(ByRef minYear As Long, ByRef maxYear As Long)
    Dim tbl As ListObject
    Dim yearCell As Range
    Dim yearNumber As Long
    Dim currentYear As Long

    currentYear = Year(Date)
    minYear = currentYear
    maxYear = currentYear

    On Error Resume Next
    Set tbl = ThisWorkbook.Worksheets("Logbook").ListObjects("Logbook")
    On Error GoTo 0
    If tbl Is Nothing Then Exit Sub
    If tbl.DataBodyRange Is Nothing Then Exit Sub

    For Each yearCell In tbl.ListColumns("Year").DataBodyRange.Cells
        If Not IsError(yearCell.Value) And IsNumeric(yearCell.Value) Then
            yearNumber = CLng(yearCell.Value)
            If yearNumber >= 1900 And yearNumber <= 9999 Then
                If yearNumber < minYear Then minYear = yearNumber
                If yearNumber > maxYear Then maxYear = yearNumber
            End If
        End If
    Next yearCell
End Sub

Private Function TryReadOptionalDate(ByVal dateLabel As String, _
                                     ByVal yearText As String, _
                                     ByVal monthText As String, _
                                     ByVal dayText As String, _
                                     ByRef outputDate As Variant, _
                                     ByRef focusControlName As String) As Boolean
    Dim cleanYear As String
    Dim cleanMonth As String
    Dim cleanDay As String
    Dim yearNumber As Long
    Dim monthNumber As Long
    Dim dayNumber As Long
    Dim candidateDate As Date

    cleanYear = Trim$(yearText)
    cleanMonth = Trim$(monthText)
    cleanDay = Trim$(dayText)
    outputDate = Empty

    If Len(cleanYear) = 0 And Len(cleanMonth) = 0 And Len(cleanDay) = 0 Then
        TryReadOptionalDate = True
        Exit Function
    End If

    focusControlName = DatePartFocusName(dateLabel, cleanYear, cleanMonth, cleanDay)
    If Len(cleanYear) = 0 Or Len(cleanMonth) = 0 Or Len(cleanDay) = 0 Then
        Exit Function
    End If
    If Not IsNumeric(cleanYear) Then
        focusControlName = IIf(dateLabel = "start date", "txtStartYear", "txtEndYear")
        Exit Function
    End If
    If Not IsNumeric(cleanDay) Then
        focusControlName = IIf(dateLabel = "start date", "txtStartDay", "txtEndDay")
        Exit Function
    End If

    yearNumber = CLng(cleanYear)
    dayNumber = CLng(cleanDay)
    monthNumber = MonthNumberFromName(cleanMonth)
    If yearNumber < 1900 Or yearNumber > 9999 Then
        focusControlName = IIf(dateLabel = "start date", "txtStartYear", "txtEndYear")
        Exit Function
    End If
    If monthNumber = 0 Then
        focusControlName = IIf(dateLabel = "start date", "cboStartMonth", "cboEndMonth")
        Exit Function
    End If
    If dayNumber < 1 Or dayNumber > 31 Then
        focusControlName = IIf(dateLabel = "start date", "txtStartDay", "txtEndDay")
        Exit Function
    End If

    On Error GoTo InvalidDate
    candidateDate = DateSerial(yearNumber, monthNumber, dayNumber)
    If Year(candidateDate) <> yearNumber Or _
       Month(candidateDate) <> monthNumber Or _
       Day(candidateDate) <> dayNumber Then GoTo InvalidDate

    outputDate = candidateDate
    TryReadOptionalDate = True
    Exit Function

InvalidDate:
    focusControlName = IIf(dateLabel = "start date", "txtStartDay", "txtEndDay")
End Function

Private Function DatePartFocusName(ByVal dateLabel As String, _
                                   ByVal yearText As String, _
                                   ByVal monthText As String, _
                                   ByVal dayText As String) As String
    If Len(yearText) = 0 Then
        DatePartFocusName = IIf(dateLabel = "start date", "txtStartYear", "txtEndYear")
    ElseIf Len(monthText) = 0 Then
        DatePartFocusName = IIf(dateLabel = "start date", "cboStartMonth", "cboEndMonth")
    ElseIf Len(dayText) = 0 Then
        DatePartFocusName = IIf(dateLabel = "start date", "txtStartDay", "txtEndDay")
    Else
        DatePartFocusName = IIf(dateLabel = "start date", "txtStartYear", "txtEndYear")
    End If
End Function

Private Function MonthNumberFromName(ByVal monthName As String) As Long
    Select Case LCase$(Trim$(monthName))
        Case "jan": MonthNumberFromName = 1
        Case "feb": MonthNumberFromName = 2
        Case "mar": MonthNumberFromName = 3
        Case "apr": MonthNumberFromName = 4
        Case "may": MonthNumberFromName = 5
        Case "jun": MonthNumberFromName = 6
        Case "jul": MonthNumberFromName = 7
        Case "aug": MonthNumberFromName = 8
        Case "sep": MonthNumberFromName = 9
        Case "oct": MonthNumberFromName = 10
        Case "nov": MonthNumberFromName = 11
        Case "dec": MonthNumberFromName = 12
    End Select
End Function

Private Sub UpdateSummary()
    Dim formatLabel As String
    Dim detailsLabel As String
    Dim datesLabel As String

    Select Case SelectedFormat()
        Case "csv": formatLabel = "CSV"
        Case "pdf": formatLabel = "PDF"
        Case Else: formatLabel = "Excel workbook"
    End Select
    If optCombined.Value Then
        detailsLabel = "Combined details"
    Else
        detailsLabel = "Separate details"
    End If
    If chkAllDates.Value Then
        datesLabel = "All dates"
    Else
        datesLabel = "Selected date range"
    End If

    lblStatus.ForeColor = RGB(102, 102, 102)
    lblStatus.Caption = formatLabel & "  |  " & detailsLabel & "  |  " & datesLabel
End Sub

Private Sub ClearError()
    UpdateSummary
End Sub

Private Sub ShowError(ByVal messageText As String)
    lblStatus.ForeColor = RGB(192, 0, 0)
    lblStatus.Caption = messageText
End Sub

Private Sub SetBusy(ByVal busy As Boolean)
    cmdExport.Enabled = Not busy
    cmdCancel.Enabled = Not busy
    If busy Then
        lblStatus.ForeColor = RGB(51, 102, 153)
        lblStatus.Caption = "Preparing your export..."
        Me.MousePointer = 11
    Else
        Me.MousePointer = 0
    End If
    Me.Repaint
    DoEvents
End Sub
'@

    $form.CodeModule.AddFromString($code)
    Write-Host "  Created frmExportLogbook"
}
