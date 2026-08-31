Attribute VB_Name = "modBoot"
' ==============================================================
' modBoot - Embedded updater wizard launcher
' ==============================================================
' New workbooks do not download or import executable VBA at runtime.
' This embedded bootstrap checks the public update channel and starts
' the external updater wizard. The legacy modUpdate.bas endpoint remains
' available only for older supported workbooks.
' ==============================================================

Option Explicit

Private Const GITHUB_USER  As String = "alphadelta332"
Private Const GITHUB_REPO  As String = "Electronic-Logbook"
Private Const MASTER_FILE  As String = "Electronic_Logbook_Master.xlsm"
Private Const WIZARD_EXE_NAME As String = "ElectronicLogbook.Updater.Wizard.exe"
Private Const WIZARD_ZIP_NAME As String = "ElectronicLogbook.Updater.Wizard.win-x64.zip"
Private Const RELEASE_MANIFEST_NAME As String = "release-manifest.json"
Private Const WIZARD_SIGNATURE_REPORT_NAME As String = "wizard-signature-report.json"
Private Const DEV_WIZARD_TAG_PREFIX As String = "dev-wizard-"
Private Const DEV_WIZARD_COMMIT_NAME As String = "dev-wizard-commit.txt"
Private Const PREVIEW_MIGRATION_VERSION As String = "3.0.0"
Private Const LEGACY_PREVIEW_GITHUB_BRANCH As String = "pilot"

Private mResolvedRef As String

Public Sub CheckForUpdate()
    On Error GoTo Fail
    Dim remoteVer As String
    Dim localVer  As String
    Dim msg       As String
    Dim title     As String

    remoteVer = FetchRemoteVersion()
    If remoteVer = "" Then Exit Sub

    localVer = GetLocalVersion()
    If IsNewerVersion(remoteVer, localVer) Then
        msg = BuildUpdateOfferMessage(localVer, remoteVer)
        title = UpdateOfferTitle(remoteVer)
        If MsgBox(msg, vbYesNo + vbInformation, title) = vbYes Then
            RunWizardUpdate remoteVer
        End If
    End If
    Exit Sub
Fail:
End Sub

Public Sub ConnectToElectronicLogbook()
    Dim sourceWorkbookPath As String
    Dim wizardReason As String
    Dim targetVersion As String
    Dim closeErr As Long
    Dim closeMsg As String

    On Error GoTo Fail

    If MsgBox("Connect this workbook to your invited Electronic Logbook account?" & vbCrLf & vbCrLf & _
              "The workbook will save and close, then the updater will ask you to sign in. A timestamped backup will be kept before account data is connected.", _
              vbYesNo + vbInformation, "Connect to Electronic Logbook") <> vbYes Then Exit Sub

    targetVersion = FetchRemoteVersion()
    If RequiresDevelopmentWizardWarning(GetGitHubBranch()) Then
        If Not ConfirmDevelopmentWizardLaunch(targetVersion) Then Exit Sub
    End If

    sourceWorkbookPath = ResolveLocalPath(ThisWorkbook) & "\" & ThisWorkbook.Name
    ThisWorkbook.Save

    If TryLaunchExternalUpdaterWizard( _
            sourceWorkbookPath, _
            GITHUB_USER & "/" & GITHUB_REPO, _
            wizardReason, _
            "", _
            targetVersion, _
            True) Then
        On Error Resume Next
        SuppressHostedWorkbookSyncOnClose
        Application.DisplayAlerts = False
        ThisWorkbook.Close SaveChanges:=False
        closeErr = Err.Number
        closeMsg = Err.Description
        Application.DisplayAlerts = True
        On Error GoTo 0

        If closeErr <> 0 Then
            MsgBox "The account connection window is running, but this workbook could not close automatically." & vbCrLf & vbCrLf & _
                   "Please close this workbook now so connection can continue." & vbCrLf & vbCrLf & _
                   "Close error: " & closeMsg, _
                   vbExclamation, "Manual Close Required"
        End If
        Exit Sub
    End If

    MsgBox "The Electronic Logbook updater was not available, so account connection could not start." & vbCrLf & vbCrLf & _
           "Reason: " & wizardReason & vbCrLf & vbCrLf & _
           "Your workbook has not been changed.", vbCritical, "Connection Failed"
    Exit Sub

Fail:
    MsgBox "Account connection could not start." & vbCrLf & vbCrLf & _
           Err.Description & vbCrLf & vbCrLf & _
           "Your workbook has not been changed.", vbCritical, "Connection Failed"
End Sub

Public Sub CheckForUpdateManual()
    Dim remoteVer As String
    Dim localVer  As String
    Dim msg       As String
    Dim title     As String

    remoteVer = FetchRemoteVersion()
    If remoteVer = "" Then
        MsgBox "Could not reach GitHub. Check your internet connection.", _
               vbExclamation, "No Connection"
        Exit Sub
    End If

    localVer = GetLocalVersion()
    If IsNewerVersion(remoteVer, localVer) Then
        msg = BuildUpdateOfferMessage(localVer, remoteVer)
        title = UpdateOfferTitle(remoteVer)
        If MsgBox(msg, vbYesNo + vbInformation, title) = vbYes Then
            RunWizardUpdate remoteVer
        End If
    Else
        MsgBox "You are up to date! (version " & localVer & ")", _
               vbInformation, "No Update Needed"
    End If
End Sub

Private Function BuildUpdateOfferMessage(ByVal localVer As String, _
                                         ByVal remoteVer As String) As String
    If IsPreviewMigrationOffer(remoteVer) Then
        BuildUpdateOfferMessage = _
            "Your logbook is ready to move to FlightLogX." & vbCrLf & vbCrLf & _
            "Version " & PREVIEW_MIGRATION_VERSION & " will guide you through moving the flights " & _
            "in this workbook to the FlightLogX app." & vbCrLf & vbCrLf & _
            "Nothing will change if you choose No." & vbCrLf & vbCrLf & _
            "Start the move now?"
    Else
        BuildUpdateOfferMessage = _
            "A new version of the Electronic Logbook is available!" & vbCrLf & vbCrLf & _
            "  Your version:  " & localVer & vbCrLf & _
            "  New version:   " & remoteVer & vbCrLf & vbCrLf & _
            "Update now? Your flight data will not be affected."
    End If
End Function

Private Function UpdateOfferTitle(ByVal remoteVer As String) As String
    If IsPreviewMigrationOffer(remoteVer) Then
        UpdateOfferTitle = "Move to FlightLogX"
    Else
        UpdateOfferTitle = "Logbook Update Available"
    End If
End Function

Private Function IsPreviewMigrationOffer(ByVal remoteVer As String) As Boolean
    IsPreviewMigrationOffer = IsPreviewUpdateBranch(GetGitHubBranch()) And _
                              NormalizeVersionText(remoteVer) = PREVIEW_MIGRATION_VERSION
End Function

Private Sub RunWizardUpdate(ByVal newVersion As String)
    Dim sourceWorkbookPath As String
    Dim wizardMasterPath As String
    Dim wizardReason As String
    Dim releaseChannel As Boolean

    newVersion = NormalizeVersionText(newVersion)
    releaseChannel = IsStableUpdateBranch(GetGitHubBranch())
    sourceWorkbookPath = ResolveLocalPath(ThisWorkbook) & "\" & ThisWorkbook.Name

    If Not releaseChannel Then
        wizardMasterPath = Environ("TEMP") & "\LB_Master_" & Format(Now, "yyyymmdd_hhmmss") & ".xlsm"
        If Not DownloadFile(RawURL(MASTER_FILE, mResolvedRef), wizardMasterPath) Then
            wizardReason = "Could not prepare the development master workbook for the updater wizard."
            wizardMasterPath = ""
        End If
    End If

    If wizardReason = "" Then
        If RequiresDevelopmentWizardWarning(GetGitHubBranch()) Then
            If Not ConfirmDevelopmentWizardLaunch(newVersion) Then
                MsgBox "Update cancelled. Your workbook has not been changed.", _
                       vbInformation, "Update Cancelled"
                Exit Sub
            End If
        End If

        On Error Resume Next
        ThisWorkbook.Save
        If Err.Number <> 0 Then
            wizardReason = "The source workbook could not be saved before the update: " & Err.Description
        End If
        Err.Clear
        On Error GoTo 0
    End If

    If wizardReason = "" Then
        If TryLaunchExternalUpdaterWizard(sourceWorkbookPath, GITHUB_USER & "/" & GITHUB_REPO, wizardReason, wizardMasterPath, newVersion) Then
            Dim closeErr As Long
            Dim closeMsg As String
            On Error Resume Next
            SuppressHostedWorkbookSyncOnClose
            Application.DisplayAlerts = False
            ThisWorkbook.Close SaveChanges:=False
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
    End If

    MsgBox "The external updater wizard was not available, so the update cannot continue safely." & vbCrLf & vbCrLf & _
           "Reason: " & wizardReason & vbCrLf & vbCrLf & _
           "Your workbook has not been changed.", vbCritical, "Update Failed"
End Sub

Private Function ConfirmDevelopmentWizardLaunch(ByVal targetVersion As String) As Boolean
    Dim message As String
    Dim branchName As String
    Dim channelName As String

    branchName = LCase$(Trim$(GetGitHubBranch()))
    If branchName = "hotfix" Then
        channelName = "hotfix"
    Else
        channelName = "development"
    End If

    message = "This workbook is configured for the " & channelName & " update channel." & vbCrLf & vbCrLf & _
              UCase$(Left$(channelName, 1)) & Mid$(channelName, 2) & " updater wizard builds may be unsigned and are intended only for testing. " & _
              "Continue only if you trust this workbook and repository checkout." & vbCrLf & vbCrLf & _
              "Target version: " & targetVersion & vbCrLf & vbCrLf & _
              "Continue with the " & channelName & " updater wizard?"

    ConfirmDevelopmentWizardLaunch = (MsgBox(message, vbYesNo + vbExclamation, _
                                             UCase$(Left$(channelName, 1)) & Mid$(channelName, 2) & " Updater Warning") = vbYes)
End Function

Private Function GetLocalVersion() As String
    GetLocalVersion = Trim$(ReadWorkbookNameValue(ThisWorkbook, "LogbookVersion"))
    If GetLocalVersion = "" Or GetLocalVersion = "0" Then GetLocalVersion = "0.0"
End Function

Private Function ReadWorkbookNameValue(ByVal wb As Workbook, ByVal nameText As String) As String
    On Error Resume Next
    ReadWorkbookNameValue = CStr(wb.Names(nameText).RefersToRange.Value)
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
    Set http = CreateDownloadHttpRequest()
    If http Is Nothing Then GoTo Fail
    http.Open "GET", url, False
    http.setRequestHeader "Cache-Control", "no-cache"
    http.setRequestHeader "Pragma", "no-cache"
    http.setRequestHeader "User-Agent", "Electronic-Logbook-Updater"
    http.send
    If http.Status = 200 Then
        FetchRemoteVersion = NormalizeVersionText(http.responseText)
    End If
    Exit Function
Fail:
    FetchRemoteVersion = ""
End Function

Private Function NormalizeVersionText(ByVal value As String) As String
    value = Replace(value, vbCr, "")
    value = Replace(value, vbLf, "")
    NormalizeVersionText = Trim$(value)
End Function

Private Function IsNewerVersion(remoteVer As String, localVer As String) As Boolean
    Dim rParts() As String
    Dim lParts() As String
    Dim i As Integer
    Dim maxLen As Integer
    Dim rNum As Long
    Dim lNum As Long

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

Private Function RawURL(filename As String, Optional gitRef As String = "") As String
    If gitRef = "" Then gitRef = ResolveGitHubRef()
    RawURL = "https://raw.githubusercontent.com/" & GITHUB_USER & "/" & _
             GITHUB_REPO & "/" & gitRef & "/" & filename & _
             "?_=" & Format(Now, "yyyymmddhhmmss")
End Function

Private Function ResolveGitHubRef() As String
    Dim branchName As String
    Dim sha As String

    branchName = GitHubSourceBranch(GetGitHubBranch())
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

Private Function IsStableUpdateBranch(ByVal branchName As String) As Boolean
    IsStableUpdateBranch = (LCase$(Trim$(branchName)) = "main")
End Function

Private Function IsPreviewUpdateBranch(ByVal branchName As String) As Boolean
    branchName = LCase$(Trim$(branchName))
    IsPreviewUpdateBranch = (branchName = "preview" Or branchName = LEGACY_PREVIEW_GITHUB_BRANCH)
End Function

Private Function GitHubSourceBranch(ByVal workbookChannel As String) As String
    workbookChannel = LCase$(Trim$(workbookChannel))
    If workbookChannel = "preview" Then
        GitHubSourceBranch = LEGACY_PREVIEW_GITHUB_BRANCH
    Else
        GitHubSourceBranch = workbookChannel
    End If
End Function

Private Function RequiresDevelopmentWizardWarning(ByVal branchName As String) As Boolean
    RequiresDevelopmentWizardWarning = Not IsStableUpdateBranch(branchName) And _
                                       Not IsPreviewUpdateBranch(branchName)
End Function

Private Function WorkbookUpdateChannelArgument() As String
    Dim branchName As String

    branchName = LCase$(Trim$(GetGitHubBranch()))
    If branchName = "hotfix" Then
        WorkbookUpdateChannelArgument = "hotfix"
    ElseIf IsPreviewUpdateBranch(branchName) Then
        WorkbookUpdateChannelArgument = "preview"
    ElseIf branchName = "main" Then
        WorkbookUpdateChannelArgument = "stable"
    Else
        WorkbookUpdateChannelArgument = "development"
    End If
End Function

Private Function GetBranchCommitSha(branchName As String) As String
    Dim http As Object
    Dim apiUrl As String
    Dim body As String

    On Error GoTo Fail
    apiUrl = "https://api.github.com/repos/" & GITHUB_USER & "/" & _
             GITHUB_REPO & "/commits/" & branchName & _
             "?_=" & Format(Now, "yyyymmddhhmmss")

    Set http = CreateDownloadHttpRequest()
    If http Is Nothing Then GoTo Fail
    http.Open "GET", apiUrl, False
    http.setRequestHeader "Accept", "application/vnd.github+json"
    http.setRequestHeader "Cache-Control", "no-cache"
    http.setRequestHeader "Pragma", "no-cache"
    http.setRequestHeader "User-Agent", "Electronic-Logbook-Updater"
    http.send
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
    Dim http As Object
    Dim stream As Object

    On Error GoTo Fail
    Set http = CreateDownloadHttpRequest()
    If http Is Nothing Then GoTo Fail
    http.Open "GET", url, False
    http.setRequestHeader "Cache-Control", "no-cache"
    http.setRequestHeader "Pragma", "no-cache"
    http.setRequestHeader "User-Agent", "Electronic-Logbook-Updater"
    http.send
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
                                                Optional ByVal targetVersion As String = "", _
                                                Optional ByVal connectHosted As Boolean = False) As Boolean
    Dim wizardPath As String
    Dim commandLine As String
    Dim shellObj As Object

    On Error GoTo Fail
    wizardPath = ResolveWizardExecutablePath(repository, targetVersion)
    If wizardPath = "" Then
        If reason = "" Then reason = "No wizard asset was found in release assets."
        Exit Function
    End If
    If Dir$(wizardPath) = "" Then
        reason = "Wizard executable path could not be resolved."
        Exit Function
    End If

    commandLine = """" & wizardPath & """ --source """ & sourceWorkbookPath & """ --repo """ & repository & """ --inplace"
    If masterWorkbookPath <> "" Then
        commandLine = commandLine & " --master """ & masterWorkbookPath & """"
        If Not IsStableUpdateBranch(GetGitHubBranch()) Then
            commandLine = commandLine & " --channel " & WorkbookUpdateChannelArgument()
        End If
    End If
    If connectHosted Then commandLine = commandLine & " --connect-hosted"

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

    targetVersion = NormalizeVersionText(targetVersion)

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

    If Not IsStableUpdateBranch(GetGitHubBranch()) Then
        If LCase$(Trim$(GetGitHubBranch())) = "hotfix" Then
            tempFolder = Environ("TEMP") & "\ElectronicLogbookUpdaterHotfix"
        ElseIf IsPreviewUpdateBranch(GetGitHubBranch()) Then
            tempFolder = Environ("TEMP") & "\ElectronicLogbookUpdaterPreview"
        Else
            tempFolder = Environ("TEMP") & "\ElectronicLogbookUpdaterDev"
        End If
        If mResolvedRef <> "" Then
            tempFolder = tempFolder & "_" & SafePathSegment(Left$(mResolvedRef, 12))
        ElseIf targetVersion <> "" Then
            tempFolder = tempFolder & "_" & SafePathSegment(targetVersion)
        End If
        EnsureFolderExists tempFolder

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
    If targetVersion <> "" Then tempFolder = tempFolder & "_" & SafePathSegment(targetVersion)
    EnsureFolderExists tempFolder

    candidate = tempFolder & "\" & WIZARD_EXE_NAME
    If Dir$(candidate) = "" Then
        If targetVersion <> "" Then
            If Not DownloadReleaseWizardPackage(repository, targetVersion, candidate, tempFolder) Then
                ResolveWizardExecutablePath = ""
                Exit Function
            End If
        ElseIf Not DownloadLatestWizardPackage(repository, candidate, tempFolder) Then
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

Private Function DownloadReleaseWizardPackage(ByVal repository As String, _
                                              ByVal version As String, _
                                              ByVal destinationExePath As String, _
                                              ByVal tempFolder As String) As Boolean
    Dim releaseBaseUrl As String
    Dim manifestPath As String
    Dim signatureReportPath As String
    Dim manifestJson As String
    Dim signatureReportJson As String

    version = NormalizeVersionText(version)
    If version = "" Then Exit Function

    releaseBaseUrl = "https://github.com/" & repository & "/releases/download/v" & version & "/"
    manifestPath = tempFolder & "\" & RELEASE_MANIFEST_NAME
    signatureReportPath = tempFolder & "\" & WIZARD_SIGNATURE_REPORT_NAME

    If Not DownloadFile(releaseBaseUrl & RELEASE_MANIFEST_NAME, manifestPath) Then Exit Function
    manifestJson = ReadAllTextFile(manifestPath)
    If manifestJson = "" Then Exit Function

    If DownloadFile(releaseBaseUrl & WIZARD_SIGNATURE_REPORT_NAME, signatureReportPath) Then
        signatureReportJson = ReadAllTextFile(signatureReportPath)
    End If

    If TryDownloadWizardFromUrl(releaseBaseUrl & WIZARD_EXE_NAME, destinationExePath, tempFolder, _
                                WIZARD_EXE_NAME, manifestJson, signatureReportJson) Then
        DownloadReleaseWizardPackage = True
        Exit Function
    End If

    DownloadReleaseWizardPackage = TryDownloadWizardFromUrl(releaseBaseUrl & WIZARD_ZIP_NAME, destinationExePath, tempFolder, _
                                                            WIZARD_ZIP_NAME, manifestJson, signatureReportJson)
End Function

Private Function TryDownloadWizardFromUrl(ByVal downloadUrl As String, _
                                          ByVal destinationExePath As String, _
                                          ByVal tempFolder As String, _
                                          Optional ByVal expectedAssetName As String = "", _
                                          Optional ByVal manifestJson As String = "", _
                                          Optional ByVal signatureReportJson As String = "") As Boolean
    Dim lowerUrl As String
    Dim zipPath As String
    Dim extractedExe As String

    lowerUrl = LCase$(downloadUrl)
    If Right$(lowerUrl, 4) = ".zip" Then
        zipPath = tempFolder & "\" & WIZARD_ZIP_NAME
        If Not DownloadFile(downloadUrl, zipPath) Then Exit Function
        If Not ValidateManifestAssetHash(zipPath, expectedAssetName, manifestJson) Then Exit Function
        If Not ExtractZipArchive(zipPath, tempFolder) Then Exit Function

        extractedExe = FindFileByNameRecursive(tempFolder, WIZARD_EXE_NAME)
        If extractedExe = "" Then Exit Function
        If signatureReportJson <> "" Then
            If Not ValidateSignatureReportHash(extractedExe, signatureReportJson) Then Exit Function
        End If

        On Error Resume Next
        If Dir$(destinationExePath) <> "" Then Kill destinationExePath
        Name extractedExe As destinationExePath
        If Err.Number <> 0 Then
            Err.Clear
            FileCopy extractedExe, destinationExePath
            If Err.Number <> 0 Then Exit Function
        End If
        On Error GoTo 0

        TryDownloadWizardFromUrl = (Dir$(destinationExePath) <> "")
        Exit Function
    End If

    If Not DownloadFile(downloadUrl, destinationExePath) Then Exit Function
    If Not ValidateManifestAssetHash(destinationExePath, expectedAssetName, manifestJson) Then Exit Function
    TryDownloadWizardFromUrl = True
End Function

Private Function ValidateManifestAssetHash(ByVal filePath As String, _
                                           ByVal assetName As String, _
                                           ByVal manifestJson As String) As Boolean
    Dim expectedSha As String

    If assetName = "" Or manifestJson = "" Then
        ValidateManifestAssetHash = True
        Exit Function
    End If

    expectedSha = ExtractManifestAssetSha256(manifestJson, assetName)
    If expectedSha = "" Then Exit Function

    ValidateManifestAssetHash = FileSha256Matches(filePath, expectedSha)
End Function

Private Function ValidateSignatureReportHash(ByVal filePath As String, _
                                             ByVal signatureReportJson As String) As Boolean
    Dim expectedSha As String

    expectedSha = ExtractJsonStringValue(signatureReportJson, "sha256")
    If expectedSha = "" Then Exit Function

    ValidateSignatureReportHash = FileSha256Matches(filePath, expectedSha)
End Function

Private Function FileSha256Matches(ByVal filePath As String, ByVal expectedSha As String) As Boolean
    Dim actualSha As String

    actualSha = ComputeSha256(filePath)
    If actualSha = "" Then Exit Function

    FileSha256Matches = (StrComp(LCase$(actualSha), LCase$(expectedSha), vbTextCompare) = 0)
End Function

Private Function ComputeSha256(ByVal filePath As String) As String
    Dim shellObj As Object
    Dim execObj As Object
    Dim command As String
    Dim escapedPath As String
    Dim outputText As String

    On Error GoTo Fail
    escapedPath = Replace(filePath, "'", "''")
    command = "powershell -NoProfile -ExecutionPolicy Bypass -Command ""(Get-FileHash -LiteralPath '" & _
              escapedPath & "' -Algorithm SHA256).Hash.ToLowerInvariant()"""

    Set shellObj = CreateObject("WScript.Shell")
    Set execObj = shellObj.Exec(command)
    Do While execObj.Status = 0
        DoEvents
    Loop

    If execObj.ExitCode <> 0 Then GoTo Fail
    outputText = Trim$(execObj.StdOut.ReadAll)
    If Len(outputText) = 64 Then ComputeSha256 = LCase$(outputText)
    Exit Function
Fail:
    ComputeSha256 = ""
End Function

Private Function ExtractManifestAssetSha256(ByVal jsonText As String, ByVal assetName As String) As String
    Dim re As Object
    Dim matches As Object

    On Error GoTo Fail
    Set re = CreateObject("VBScript.RegExp")
    re.Pattern = """name""\s*:\s*""" & EscapeRegexLiteral(assetName) & """[\s\S]*?""sha256""\s*:\s*""([0-9a-fA-F]{64})"""
    re.Global = False
    re.IgnoreCase = True

    If re.Test(jsonText) Then
        Set matches = re.Execute(jsonText)
        ExtractManifestAssetSha256 = CStr(matches(0).SubMatches(0))
    End If
    Exit Function
Fail:
    ExtractManifestAssetSha256 = ""
End Function

Private Function ExtractJsonStringValue(ByVal jsonText As String, ByVal propertyName As String) As String
    Dim re As Object
    Dim matches As Object

    On Error GoTo Fail
    Set re = CreateObject("VBScript.RegExp")
    re.Pattern = """" & EscapeRegexLiteral(propertyName) & """\s*:\s*""([^""]*)"""
    re.Global = False
    re.IgnoreCase = True

    If re.Test(jsonText) Then
        Set matches = re.Execute(jsonText)
        ExtractJsonStringValue = CStr(matches(0).SubMatches(0))
    End If
    Exit Function
Fail:
    ExtractJsonStringValue = ""
End Function

Private Function EscapeRegexLiteral(ByVal value As String) As String
    Dim specials As Variant
    Dim i As Long

    specials = Array("\", ".", "+", "*", "?", "^", "$", "(", ")", "[", "]", "{", "}", "|")
    EscapeRegexLiteral = value
    For i = LBound(specials) To UBound(specials)
        EscapeRegexLiteral = Replace(EscapeRegexLiteral, CStr(specials(i)), "\" & CStr(specials(i)))
    Next i
End Function

Private Function FetchLatestWizardDownloadUrl(ByVal repository As String) As String
    Dim http As Object
    Dim body As String
    Dim apiUrl As String

    On Error GoTo Fail
    apiUrl = "https://api.github.com/repos/" & repository & "/releases/latest"

    Set http = CreateDownloadHttpRequest()
    If http Is Nothing Then GoTo Fail
    http.Open "GET", apiUrl, False
    http.setRequestHeader "Accept", "application/vnd.github+json"
    http.setRequestHeader "Cache-Control", "no-cache"
    http.setRequestHeader "Pragma", "no-cache"
    http.setRequestHeader "User-Agent", "Electronic-Logbook-Updater"
    http.send

    If http.Status <> 200 Then GoTo Fail
    body = http.responseText
    FetchLatestWizardDownloadUrl = ExtractWizardDownloadUrl(body)
    Exit Function
Fail:
    FetchLatestWizardDownloadUrl = ""
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
    Dim folder As Object

    On Error GoTo Fail
    Set fso = CreateObject("Scripting.FileSystemObject")
    Set folder = fso.GetFolder(rootFolder)
    FindFileByNameRecursive = FindFileInFolder(folder, fileName)
    Exit Function
Fail:
    FindFileByNameRecursive = ""
End Function

Private Function FindFileInFolder(ByVal folder As Object, ByVal fileName As String) As String
    Dim file As Object
    Dim subFolder As Object
    Dim found As String

    For Each file In folder.Files
        If StrComp(file.Name, fileName, vbTextCompare) = 0 Then
            FindFileInFolder = file.Path
            Exit Function
        End If
    Next file

    For Each subFolder In folder.SubFolders
        found = FindFileInFolder(subFolder, fileName)
        If found <> "" Then
            FindFileInFolder = found
            Exit Function
        End If
    Next subFolder
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

Private Function ReadAllTextFile(ByVal filePath As String) As String
    Dim stream As Object

    On Error GoTo Fail
    Set stream = CreateObject("ADODB.Stream")
    stream.Type = 2
    stream.Charset = "utf-8"
    stream.Open
    stream.LoadFromFile filePath
    ReadAllTextFile = stream.ReadText
    stream.Close
    Exit Function
Fail:
    On Error Resume Next
    If Not stream Is Nothing Then stream.Close
    On Error GoTo 0
    ReadAllTextFile = ""
End Function

Private Function ResolveLocalPath(ByVal wb As Workbook) As String
    If LCase$(Left$(wb.Path, 4)) <> "http" Then
        ResolveLocalPath = wb.Path
        Exit Function
    End If

    ResolveLocalPath = Environ("USERPROFILE") & "\Documents"
End Function

Private Sub EnsureFolderExists(ByVal folderPath As String)
    Dim fso As Object
    If folderPath = "" Then Exit Sub
    Set fso = CreateObject("Scripting.FileSystemObject")
    If Not fso.FolderExists(folderPath) Then fso.CreateFolder folderPath
End Sub

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
