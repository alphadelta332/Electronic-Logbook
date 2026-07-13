Attribute VB_Name = "modBoot"
' ==============================================================
' modBoot - Bootstrap loader for the Electronic Logbook
' ==============================================================
' This module downloads the latest modUpdate.bas from GitHub
' and imports it before running the update check, ensuring the
' most current update logic is always used.
'
' modBoot itself is NOT updated in the original file during an
' update run. However, the _Updated.xlsm file IS built from the
' master, so it will contain the latest version of modBoot.
' In practice, modBoot should rarely need to change.
'
' Workbook_Open should call modBoot.CheckForUpdate instead of
' modUpdate.CheckForUpdate directly.
'
' REQUIREMENT: "Trust access to the VBA project object model"
' must be enabled in Trust Center > Macro Settings.
' Without it, the bootstrap warns the user and the update check cannot run.
' ==============================================================

Option Explicit

' These must match the constants in modUpdate.
' modBoot is the only place these need to be kept in sync manually.
Private Const GITHUB_USER  As String = "alphadelta332"
Private Const GITHUB_REPO  As String = "Electronic-Logbook"
Private Const MODULE_FILE  As String = "modUpdate.bas"
Private Const MODULE_MANIFEST_FILE As String = "modUpdate-manifest.json"
Private Const RELEASE_SIGNER_THUMBPRINT As String = "D1C34BACCECE7A31E0ACBF88C570F8A952349B23"
Private Const TRUST_WARNING_NAME As String = "UpdateTrustAccessWarningShown"

Public Sub CheckForUpdate()
    RefreshModUpdate
    On Error Resume Next
    Application.Run "modUpdate.CheckForUpdate"
    If Err.Number <> 0 Then WarnUpdateCheckUnavailable False
    On Error GoTo 0
    CleanupModUpdate
End Sub

Public Sub CheckForUpdateManual()
    RefreshModUpdate
    On Error Resume Next
    Application.Run "modUpdate.CheckForUpdateManual"
    If Err.Number <> 0 Then WarnUpdateCheckUnavailable True
    On Error GoTo 0
    CleanupModUpdate
End Sub

Private Sub CleanupModUpdate()
    ' Remove modUpdate from the workbook after it has run.
    ' It is downloaded fresh on every open so there is no value
    ' in keeping it baked into the workbook between sessions.
    On Error Resume Next
    Dim vbp  As Object
    Dim comp As Object
    Set vbp  = ThisWorkbook.VBProject
    Set comp = vbp.VBComponents("modUpdate")
    If Not comp Is Nothing Then
        vbp.VBComponents.Remove comp
    End If
    On Error GoTo 0
End Sub

Private Sub RefreshModUpdate()
    Dim tempFile As String
    Dim manifestFile As String
    Dim signatureFile As String
    Dim gitRef As String
    Dim vbp      As Object
    Dim oldComp  As Object

    ' Verify VBA project access is available before importing fresh code.
    On Error Resume Next
    Set vbp = ThisWorkbook.VBProject
    If Err.Number <> 0 Then
        On Error GoTo 0
        WarnTrustAccessRequired
        Exit Sub
    End If
    On Error GoTo 0

    ' Download latest modUpdate.bas to a temp file
    tempFile = Environ("TEMP") & "\modUpdate_Latest.bas"
    gitRef = ResolveGitHubRef()
    If Not DownloadFile(RawURL(MODULE_FILE, gitRef), tempFile) Then Exit Sub
    If LCase$(GetGitHubBranch()) = "main" Then
        manifestFile = Environ("TEMP") & "\modUpdate-manifest.json"
        signatureFile = manifestFile & ".p7s"
        If Not DownloadFile(ReleaseAssetURL(MODULE_MANIFEST_FILE), manifestFile) Or _
           Not DownloadFile(ReleaseAssetURL(MODULE_MANIFEST_FILE & ".p7s"), signatureFile) Or _
           Not VerifySignedModuleManifest(tempFile, manifestFile, signatureFile, gitRef) Then
            On Error Resume Next
            Kill tempFile
            On Error GoTo 0
            WarnUpdateCheckUnavailable False
            Exit Sub
        End If
    End If

    ' Remove existing modUpdate and import the fresh one
    On Error Resume Next
    Set oldComp = vbp.VBComponents("modUpdate")
    If Not oldComp Is Nothing Then
        vbp.VBComponents.Remove oldComp
    End If
    vbp.VBComponents.Import tempFile
    Kill tempFile
    On Error GoTo 0
End Sub

Private Sub WarnTrustAccessRequired()
    If HasShownTrustWarning() Then Exit Sub

    MsgBox "The Electronic Logbook could not refresh its update code because Excel is blocking access to the VBA project." & vbCrLf & vbCrLf & _
           "To enable automatic updates:" & vbCrLf & _
           "1. Open File > Options > Trust Center > Trust Center Settings." & vbCrLf & _
           "2. Open Macro Settings." & vbCrLf & _
           "3. Tick ""Trust access to the VBA project object model""." & vbCrLf & _
           "4. Close and reopen this workbook." & vbCrLf & vbCrLf & _
           "The update check cannot run until this setting is enabled.", _
           vbExclamation, "Update Setup Needed"

    MarkTrustWarningShown
End Sub

Private Sub WarnUpdateCheckUnavailable(isManual As Boolean)
    If Not isManual And HasShownTrustWarning() Then Exit Sub

    MsgBox "The Electronic Logbook could not run its update check." & vbCrLf & vbCrLf & _
           "This can happen when there is no internet connection, GitHub is temporarily unavailable, or Excel is blocking the logbook from refreshing its updater code." & vbCrLf & vbCrLf & _
           "Check your internet connection and try again. If this keeps happening, enable File > Options > Trust Center > Trust Center Settings > Macro Settings > ""Trust access to the VBA project object model"", then close and reopen this workbook.", _
           vbExclamation, "Update Check Unavailable"
End Sub

Private Function HasShownTrustWarning() As Boolean
    Dim nm       As Name
    Dim flagText As String

    On Error Resume Next
    Set nm = ThisWorkbook.Names(TRUST_WARNING_NAME)
    On Error GoTo 0

    If nm Is Nothing Then Exit Function

    flagText = UCase$(Replace(nm.RefersTo, "=", ""))
    flagText = Replace(flagText, """", "")
    HasShownTrustWarning = (flagText = "TRUE")
End Function

Private Sub MarkTrustWarningShown()
    Dim nm As Name

    On Error Resume Next
    Set nm = ThisWorkbook.Names(TRUST_WARNING_NAME)
    On Error GoTo 0

    If Not nm Is Nothing Then
        nm.RefersTo = "=TRUE"
        Exit Sub
    End If

    On Error Resume Next
    Set nm = ThisWorkbook.Names.Add(Name:=TRUST_WARNING_NAME, RefersTo:="=TRUE")
    nm.Visible = False
    On Error GoTo 0
End Sub

Private Function RawURL(ByVal filename As String, ByVal gitRef As String) As String
    RawURL = "https://raw.githubusercontent.com/" & GITHUB_USER & "/" & _
                GITHUB_REPO & "/" & gitRef & "/" & filename & _
                "?_=" & Format(Now, "yyyymmddhhmmss")
End Function

Private Function ReleaseAssetURL(ByVal filename As String) As String
    ReleaseAssetURL = "https://github.com/" & GITHUB_USER & "/" & GITHUB_REPO & _
                      "/releases/latest/download/" & filename
End Function

Private Function GetGitHubBranch() As String
    On Error Resume Next
    GetGitHubBranch = Trim(CStr(ThisWorkbook.Names("GitHubBranch").RefersToRange.Value))
    If GetGitHubBranch = "" Then GetGitHubBranch = "main"
    On Error GoTo 0
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

Private Function GetBranchCommitSha(branchName As String) As String
    Dim http     As Object
    Dim apiUrl   As String
    Dim body     As String

    On Error GoTo Fail

    apiUrl = "https://api.github.com/repos/" & GITHUB_USER & "/" & _
             GITHUB_REPO & "/commits/" & branchName & _
             "?_=" & Format(Now, "yyyymmddhhmmss")

    Set http = CreateHttpGetRequest(apiUrl, True)
    If http Is Nothing Then GoTo Fail
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

Private Function DownloadFile(url As String, destPath As String) As Boolean
    Dim http   As Object
    Dim stream As Object

    On Error GoTo Fail
    Set http = CreateHttpGetRequest(url, False)
    If http Is Nothing Then GoTo Fail
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

' All synchronous update requests use ServerXMLHTTP with finite timeouts so a
' DNS, proxy, or network failure cannot leave Excel blocked indefinitely.
Private Function CreateHttpGetRequest(ByVal url As String, ByVal acceptGitHubJson As Boolean) As Object
    Dim http As Object

    On Error GoTo Fail
    Set http = CreateObject("MSXML2.ServerXMLHTTP.6.0")
    http.setTimeouts 5000, 5000, 15000, 30000
    http.Open "GET", url, False
    If acceptGitHubJson Then http.setRequestHeader "Accept", "application/vnd.github+json"
    http.setRequestHeader "Cache-Control", "no-cache"
    http.setRequestHeader "Pragma", "no-cache"
    http.setRequestHeader "User-Agent", "Electronic-Logbook-Updater"
    http.send
    Set CreateHttpGetRequest = http
    Exit Function
Fail:
    Set CreateHttpGetRequest = Nothing
End Function

' Stable releases fail closed: the signed manifest must be cryptographically
' valid, pin the release certificate, bind to the resolved commit, and contain
' the exact SHA-256 of the downloaded VBA module. Development branches remain
' deliberately unsigned for local iteration only.
Private Function VerifySignedModuleManifest(ByVal modulePath As String, _
                                            ByVal manifestPath As String, _
                                            ByVal signaturePath As String, _
                                            ByVal expectedRef As String) As Boolean
    Dim shellObj As Object
    Dim command As String

    On Error GoTo Fail
    command = "powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command """ & _
              "$ErrorActionPreference='Stop';Add-Type -AssemblyName System.Security;" & _
              "$m=[IO.File]::ReadAllBytes('" & EscapePowerShellLiteral(manifestPath) & "');" & _
              "$ci=[System.Security.Cryptography.Pkcs.ContentInfo]::new($m);$c=[System.Security.Cryptography.Pkcs.SignedCms]::new($ci,$true);" & _
              "$c.Decode([IO.File]::ReadAllBytes('" & EscapePowerShellLiteral(signaturePath) & "'));$c.CheckSignature($true);" & _
              "if($c.SignerInfos.Count -ne 1 -or $c.SignerInfos[0].Certificate.Thumbprint -ne '" & RELEASE_SIGNER_THUMBPRINT & "'){exit 1};" & _
              "$j=Get-Content -Raw -LiteralPath '" & EscapePowerShellLiteral(manifestPath) & "'|ConvertFrom-Json;" & _
              "$a=@($j.assets|Where-Object {$_.name -eq '" & MODULE_FILE & "'})[0];" & _
              "if($null -eq $a -or $j.ref -ne '" & EscapePowerShellLiteral(expectedRef) & "' -or " & _
              "(Get-FileHash -LiteralPath '" & EscapePowerShellLiteral(modulePath) & "' -Algorithm SHA256).Hash.ToLower() -ne $a.sha256.ToLower()){exit 1}"""
    Set shellObj = CreateObject("WScript.Shell")
    VerifySignedModuleManifest = (shellObj.Run(command, 0, True) = 0)
    Exit Function
Fail:
    VerifySignedModuleManifest = False
End Function

Private Function EscapePowerShellLiteral(ByVal value As String) As String
    EscapePowerShellLiteral = Replace(value, "'", "''")
End Function
