Set-StrictMode -Version Latest

function Invoke-NativeCapture {
    param(
        [Parameter(Mandatory = $true)] [string] $FilePath,
        [Parameter(Mandatory = $true)] [string[]] $Arguments
    )

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $output = & $FilePath @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = @($output)
    }
}

function Get-Sha256Hex {
    param([Parameter(Mandatory = $true)] [string] $Path)

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        try {
            $hash = [System.BitConverter]::ToString($sha256.ComputeHash($stream))
            return $hash.Replace("-", "").ToLowerInvariant()
        }
        finally {
            $sha256.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Invoke-AdbChecked {
    param(
        [Parameter(Mandatory = $true)] [string] $Adb,
        [Parameter(Mandatory = $true)] [string] $DeviceSerial,
        [Parameter(Mandatory = $true)] [string[]] $Arguments
    )

    $adbArguments = @("-s", $DeviceSerial) + $Arguments
    $result = Invoke-NativeCapture -FilePath $Adb -Arguments $adbArguments
    if ($result.ExitCode -ne 0) {
        throw "adb $($Arguments -join ' ') failed: $($result.Output -join [Environment]::NewLine)"
    }

    return $result.Output
}

function Find-ApkSigner {
    param([Parameter(Mandatory = $true)] [string] $SdkRoot)

    $buildToolsRoot = Join-Path $SdkRoot "build-tools"
    $candidate = Get-ChildItem -LiteralPath $buildToolsRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName "apksigner.bat" } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
    if ($null -eq $candidate) {
        throw "Android apksigner was not found under $buildToolsRoot."
    }

    return $candidate
}

function Get-ApkCertificateSha256 {
    param(
        [Parameter(Mandatory = $true)] [string] $ApkSigner,
        [Parameter(Mandatory = $true)] [string] $ApkPath
    )

    $result = Invoke-NativeCapture -FilePath $ApkSigner -Arguments @("verify", "--print-certs", $ApkPath)
    if ($result.ExitCode -ne 0) {
        throw "Could not verify APK signing certificate for $ApkPath."
    }

    $digestLine = $result.Output | Where-Object {
        $_ -match "certificate SHA-256 digest:\s*([0-9a-f]+)"
    } | Select-Object -First 1
    if ($null -eq $digestLine -or $digestLine -notmatch "certificate SHA-256 digest:\s*([0-9a-f]+)") {
        throw "Could not read the APK signing certificate SHA-256 digest for $ApkPath."
    }

    return $Matches[1].ToLowerInvariant()
}

function Get-InstalledPackageCertificateSha256 {
    param(
        [Parameter(Mandatory = $true)] [string] $Adb,
        [Parameter(Mandatory = $true)] [string] $DeviceSerial,
        [Parameter(Mandatory = $true)] [string] $PackageName,
        [Parameter(Mandatory = $true)] [string] $ApkSigner
    )

    $listResult = Invoke-NativeCapture -FilePath $Adb -Arguments @(
        "-s", $DeviceSerial, "shell", "pm", "list", "packages", "--user", "0", $PackageName)
    if ($listResult.ExitCode -ne 0) {
        throw "Could not query installed packages for $PackageName."
    }

    $installedPackageLine = $listResult.Output |
        Where-Object { ([string] $_).Trim() -eq "package:$PackageName" } |
        Select-Object -First 1
    if ($null -eq $installedPackageLine) {
        return $null
    }

    $pathResult = Invoke-NativeCapture -FilePath $Adb -Arguments @(
        "-s", $DeviceSerial, "shell", "pm", "path", $PackageName)
    if ($pathResult.ExitCode -ne 0) {
        throw "Could not query installed package $PackageName."
    }

    $baseApkLine = $pathResult.Output | Where-Object { $_ -match "^package:(.+/base\.apk)$" } | Select-Object -First 1
    if ($null -eq $baseApkLine) {
        return $null
    }

    $remoteApkPath = $baseApkLine.Substring("package:".Length).Trim()
    $temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("electronic-logbook-apk-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
    $localApkPath = Join-Path $temporaryRoot "installed-base.apk"

    try {
        $pullResult = Invoke-NativeCapture -FilePath $Adb -Arguments @(
            "-s", $DeviceSerial, "pull", $remoteApkPath, $localApkPath)
        if ($pullResult.ExitCode -ne 0) {
            throw "Could not copy the installed APK for certificate verification: $($pullResult.Output -join ' ')"
        }

        return Get-ApkCertificateSha256 -ApkSigner $ApkSigner -ApkPath $localApkPath
    }
    finally {
        if (Test-Path -LiteralPath $temporaryRoot) {
            Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
        }
    }
}

function Copy-DeviceFileFromAppSandbox {
    param(
        [Parameter(Mandatory = $true)] [string] $Adb,
        [Parameter(Mandatory = $true)] [string] $DeviceSerial,
        [Parameter(Mandatory = $true)] [string] $PackageName,
        [Parameter(Mandatory = $true)] [string] $RemotePath,
        [Parameter(Mandatory = $true)] [string] $LocalPath
    )

    $processInfo = [Diagnostics.ProcessStartInfo]::new()
    $processInfo.FileName = $Adb
    $processInfo.Arguments = "-s $DeviceSerial exec-out run-as $PackageName cat $RemotePath"
    $processInfo.UseShellExecute = $false
    $processInfo.RedirectStandardOutput = $true
    $processInfo.RedirectStandardError = $true
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $processInfo

    if (-not $process.Start()) {
        throw "Could not start the adb backup stream."
    }

    $outputStream = [IO.File]::Create($LocalPath)
    try {
        $process.StandardOutput.BaseStream.CopyTo($outputStream)
    }
    finally {
        $outputStream.Dispose()
    }

    $errorText = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
        throw "Could not copy $RemotePath from the app sandbox: $errorText"
    }
}

function Copy-LocalFileToAppSandbox {
    param(
        [Parameter(Mandatory = $true)] [string] $Adb,
        [Parameter(Mandatory = $true)] [string] $DeviceSerial,
        [Parameter(Mandatory = $true)] [string] $PackageName,
        [Parameter(Mandatory = $true)] [string] $LocalPath,
        [Parameter(Mandatory = $true)] [string] $RemotePath
    )

    $remoteBase64Path = "$RemotePath.b64"
    Invoke-AdbChecked -Adb $Adb -DeviceSerial $DeviceSerial -Arguments @(
        "shell", "run-as", $PackageName, "mkdir", "-p", "cache") | Out-Null
    Invoke-AdbChecked -Adb $Adb -DeviceSerial $DeviceSerial -Arguments @(
        "shell", "run-as", $PackageName, "sh", "-c", "': > $remoteBase64Path'") | Out-Null

    $encoded = [Convert]::ToBase64String([IO.File]::ReadAllBytes($LocalPath))
    $chunkSize = 24000
    for ($offset = 0; $offset -lt $encoded.Length; $offset += $chunkSize) {
        $length = [Math]::Min($chunkSize, $encoded.Length - $offset)
        $chunk = $encoded.Substring($offset, $length)
        Invoke-AdbChecked -Adb $Adb -DeviceSerial $DeviceSerial -Arguments @(
            "shell", "run-as", $PackageName, "sh", "-c",
            "'printf %s $chunk >> $remoteBase64Path'") | Out-Null
    }

    Invoke-AdbChecked -Adb $Adb -DeviceSerial $DeviceSerial -Arguments @(
        "shell", "run-as", $PackageName, "sh", "-c",
        "'base64 -d $remoteBase64Path > $RemotePath'") | Out-Null
    Invoke-AdbChecked -Adb $Adb -DeviceSerial $DeviceSerial -Arguments @(
        "shell", "run-as", $PackageName, "rm", $remoteBase64Path) | Out-Null
}

function Get-IndexedDbArchiveEvidence {
    param(
        [Parameter(Mandatory = $true)] [string] $ArchivePath,
        [Parameter(Mandatory = $true)] [string] $EvidenceRoot
    )

    New-Item -ItemType Directory -Path $EvidenceRoot -Force | Out-Null
    & tar "-xzf" $ArchivePath "-C" $EvidenceRoot
    if ($LASTEXITCODE -ne 0) {
        throw "The device backup archive could not be extracted for verification."
    }

    $indexedDbRoot = Join-Path $EvidenceRoot "app_webview\Default\IndexedDB"
    $levelDbRoot = Get-ChildItem -LiteralPath $indexedDbRoot -Directory -Filter "*.indexeddb.leveldb" -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -eq $levelDbRoot) {
        throw "The device backup does not contain the Electronic Logbook IndexedDB database."
    }

    $currentPath = Join-Path $levelDbRoot.FullName "CURRENT"
    if (-not (Test-Path -LiteralPath $currentPath -PathType Leaf)) {
        throw "The IndexedDB backup is incomplete: CURRENT is missing."
    }

    $manifestName = (Get-Content -LiteralPath $currentPath -Raw).Trim()
    if ([string]::IsNullOrWhiteSpace($manifestName) -or
        -not (Test-Path -LiteralPath (Join-Path $levelDbRoot.FullName $manifestName) -PathType Leaf)) {
        throw "The IndexedDB backup is incomplete: its active manifest is missing."
    }

    $dataFiles = Get-ChildItem -LiteralPath $levelDbRoot.FullName -File | Where-Object {
        ($_.Extension -eq ".log" -or $_.Extension -eq ".ldb") -and $_.Length -gt 0
    }
    if (@($dataFiles).Count -eq 0) {
        throw "The IndexedDB backup contains no non-empty LevelDB data files."
    }

    $files = Get-ChildItem -LiteralPath $indexedDbRoot -Recurse -File | Sort-Object FullName | ForEach-Object {
        [pscustomobject]@{
            path = $_.FullName.Substring($indexedDbRoot.Length + 1).Replace("\", "/")
            length = $_.Length
            sha256 = Get-Sha256Hex -Path $_.FullName
        }
    }

    return [pscustomobject]@{
        FileCount = @($files).Count
        Files = @($files)
    }
}

function New-VerifiedIndexedDbSnapshot {
    param(
        [Parameter(Mandatory = $true)] [string] $Adb,
        [Parameter(Mandatory = $true)] [string] $DeviceSerial,
        [Parameter(Mandatory = $true)] [string] $PackageName,
        [Parameter(Mandatory = $true)] [string] $DestinationRoot,
        [string] $Name = "source"
    )

    New-Item -ItemType Directory -Path $DestinationRoot -Force | Out-Null
    $archivePath = Join-Path $DestinationRoot "$Name-indexeddb.tgz"
    $evidenceRoot = Join-Path $DestinationRoot "$Name-verified"
    $remoteArchivePath = "cache/electronic-logbook-device-bridge.tgz"

    Invoke-AdbChecked -Adb $Adb -DeviceSerial $DeviceSerial -Arguments @(
        "shell", "am", "force-stop", $PackageName) | Out-Null
    Invoke-AdbChecked -Adb $Adb -DeviceSerial $DeviceSerial -Arguments @(
        "shell", "run-as", $PackageName, "tar", "-czf", $remoteArchivePath,
        "app_webview/Default/IndexedDB") | Out-Null
    try {
        Copy-DeviceFileFromAppSandbox -Adb $Adb -DeviceSerial $DeviceSerial `
            -PackageName $PackageName -RemotePath $remoteArchivePath -LocalPath $archivePath
    }
    finally {
        Invoke-AdbChecked -Adb $Adb -DeviceSerial $DeviceSerial -Arguments @(
            "shell", "run-as", $PackageName, "rm", "-f", $remoteArchivePath) | Out-Null
    }

    if ((Get-Item -LiteralPath $archivePath).Length -eq 0) {
        throw "The device backup archive is empty."
    }

    $evidence = Get-IndexedDbArchiveEvidence -ArchivePath $archivePath -EvidenceRoot $evidenceRoot
    return [pscustomobject]@{
        ArchivePath = $archivePath
        ArchiveSha256 = Get-Sha256Hex -Path $archivePath
        FileCount = $evidence.FileCount
        Files = $evidence.Files
    }
}

function Assert-EquivalentIndexedDbSnapshots {
    param(
        [Parameter(Mandatory = $true)] $Expected,
        [Parameter(Mandatory = $true)] $Actual
    )

    $expectedLines = $Expected.Files | ForEach-Object { "$($_.path)|$($_.length)|$($_.sha256)" }
    $actualLines = $Actual.Files | ForEach-Object { "$($_.path)|$($_.length)|$($_.sha256)" }
    $differences = Compare-Object -ReferenceObject @($expectedLines) -DifferenceObject @($actualLines)
    if ($null -ne $differences) {
        throw "The restored IndexedDB files do not exactly match the verified source backup."
    }
}

function Get-PendingSigningMigration {
    param(
        [Parameter(Mandatory = $true)] [string] $DeviceSerial,
        [Parameter(Mandatory = $true)] [string] $PackageName
    )

    $safeSerial = $DeviceSerial -replace "[^A-Za-z0-9._-]", "_"
    $deviceRoot = Join-Path $env:LOCALAPPDATA "ElectronicLogbook\AndroidDeviceBridge\$safeSerial"
    if (-not (Test-Path -LiteralPath $deviceRoot -PathType Container)) {
        return $null
    }

    foreach ($directory in Get-ChildItem -LiteralPath $deviceRoot -Directory | Sort-Object Name -Descending) {
        $manifestPath = Join-Path $directory.FullName "migration.json"
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
            continue
        }

        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ($manifest.packageName -eq $PackageName -and $manifest.status -ne "complete") {
            return [pscustomobject]@{
                BackupRoot = $directory.FullName
                ManifestPath = $manifestPath
                Manifest = $manifest
            }
        }
    }

    return $null
}

function Complete-IndexedDbRestore {
    param(
        [Parameter(Mandatory = $true)] [string] $Adb,
        [Parameter(Mandatory = $true)] [string] $DeviceSerial,
        [Parameter(Mandatory = $true)] [string] $PackageName,
        [Parameter(Mandatory = $true)] [string] $BackupRoot,
        [Parameter(Mandatory = $true)] [string] $ManifestPath,
        [Parameter(Mandatory = $true)] $Manifest
    )

    $sourceArchivePath = [string] $Manifest.sourceArchive
    if (-not (Test-Path -LiteralPath $sourceArchivePath -PathType Leaf)) {
        throw "Pending device migration backup is missing: $sourceArchivePath"
    }

    $sourceEvidence = Get-IndexedDbArchiveEvidence -ArchivePath $sourceArchivePath `
        -EvidenceRoot (Join-Path $BackupRoot "source-restore-verified")
    $source = [pscustomobject]@{
        Files = $sourceEvidence.Files
    }

    Invoke-AdbChecked -Adb $Adb -DeviceSerial $DeviceSerial -Arguments @(
        "shell", "am", "force-stop", $PackageName) | Out-Null
    Copy-LocalFileToAppSandbox -Adb $Adb -DeviceSerial $DeviceSerial `
        -PackageName $PackageName -LocalPath $sourceArchivePath `
        -RemotePath "cache/electronic-logbook-restore.tgz"
    Invoke-AdbChecked -Adb $Adb -DeviceSerial $DeviceSerial -Arguments @(
        "shell", "run-as", $PackageName, "tar", "-tzf",
        "cache/electronic-logbook-restore.tgz") | Out-Null
    Invoke-AdbChecked -Adb $Adb -DeviceSerial $DeviceSerial -Arguments @(
        "shell", "run-as", $PackageName, "rm", "-rf",
        "app_webview/Default/IndexedDB") | Out-Null
    Invoke-AdbChecked -Adb $Adb -DeviceSerial $DeviceSerial -Arguments @(
        "shell", "run-as", $PackageName, "tar", "-xzf",
        "cache/electronic-logbook-restore.tgz") | Out-Null
    Invoke-AdbChecked -Adb $Adb -DeviceSerial $DeviceSerial -Arguments @(
        "shell", "run-as", $PackageName, "rm", "-f",
        "cache/electronic-logbook-restore.tgz") | Out-Null

    $restored = New-VerifiedIndexedDbSnapshot -Adb $Adb -DeviceSerial $DeviceSerial `
        -PackageName $PackageName -DestinationRoot $BackupRoot -Name "restored"
    Assert-EquivalentIndexedDbSnapshots -Expected $source -Actual $restored

    $Manifest.status = "complete"
    $Manifest | Add-Member -NotePropertyName completedAtUtc `
        -NotePropertyValue ([DateTimeOffset]::UtcNow.ToString("O")) -Force
    $Manifest | Add-Member -NotePropertyName restoredArchiveSha256 `
        -NotePropertyValue $restored.ArchiveSha256 -Force
    $Manifest | Add-Member -NotePropertyName restoredFileCount `
        -NotePropertyValue $restored.FileCount -Force
    $Manifest | ConvertTo-Json | Set-Content -LiteralPath $ManifestPath -Encoding UTF8

    Invoke-AdbChecked -Adb $Adb -DeviceSerial $DeviceSerial -Arguments @(
        "shell", "monkey", "-p", $PackageName, "-c",
        "android.intent.category.LAUNCHER", "1") | Out-Null
}

function Invoke-DataPreservingDebugInstall {
    param(
        [Parameter(Mandatory = $true)] [string] $Adb,
        [Parameter(Mandatory = $true)] [string] $SdkRoot,
        [Parameter(Mandatory = $true)] [string] $DeviceSerial,
        [Parameter(Mandatory = $true)] [string] $PackageName,
        [Parameter(Mandatory = $true)] [string] $ApkPath,
        [switch] $SkipLaunch
    )

    Invoke-AdbChecked -Adb $Adb -DeviceSerial $DeviceSerial -Arguments @("get-state") | Out-Null
    $apkSigner = Find-ApkSigner -SdkRoot $SdkRoot
    $targetCertificate = Get-ApkCertificateSha256 -ApkSigner $apkSigner -ApkPath $ApkPath
    $installedCertificate = Get-InstalledPackageCertificateSha256 -Adb $Adb `
        -DeviceSerial $DeviceSerial -PackageName $PackageName -ApkSigner $apkSigner

    $pendingMigration = Get-PendingSigningMigration -DeviceSerial $DeviceSerial -PackageName $PackageName
    if ($null -ne $pendingMigration) {
        $pendingTargetCertificate = [string] $pendingMigration.Manifest.targetCertificateSha256
        if ($pendingTargetCertificate -ne $targetCertificate) {
            throw "A pending migration targets a different APK certificate: $($pendingMigration.BackupRoot)"
        }

        if ($null -eq $installedCertificate) {
            Invoke-AdbChecked -Adb $Adb -DeviceSerial $DeviceSerial -Arguments @(
                "install", $ApkPath) | Out-Null
            $installedCertificate = $targetCertificate
            $pendingMigration.Manifest.status = "target-installed-restore-pending"
            $pendingMigration.Manifest | ConvertTo-Json | Set-Content `
                -LiteralPath $pendingMigration.ManifestPath -Encoding UTF8
        }
        elseif ($installedCertificate -ne $targetCertificate) {
            if ($pendingMigration.Manifest.status -ne "source-backup-verified" -or
                $installedCertificate -ne [string] $pendingMigration.Manifest.sourceCertificateSha256) {
                throw "The installed certificate does not match either side of the pending migration."
            }

            Invoke-AdbChecked -Adb $Adb -DeviceSerial $DeviceSerial -Arguments @(
                "uninstall", $PackageName) | Out-Null
            Invoke-AdbChecked -Adb $Adb -DeviceSerial $DeviceSerial -Arguments @(
                "install", $ApkPath) | Out-Null
            $installedCertificate = $targetCertificate
            $pendingMigration.Manifest.status = "target-installed-restore-pending"
            $pendingMigration.Manifest | ConvertTo-Json | Set-Content `
                -LiteralPath $pendingMigration.ManifestPath -Encoding UTF8
        }

        Complete-IndexedDbRestore -Adb $Adb -DeviceSerial $DeviceSerial `
            -PackageName $PackageName -BackupRoot $pendingMigration.BackupRoot `
            -ManifestPath $pendingMigration.ManifestPath -Manifest $pendingMigration.Manifest
        return [pscustomobject]@{
            Mode = "resumed-signing-migration"
            BackupPath = $pendingMigration.BackupRoot
        }
    }

    if ($null -eq $installedCertificate) {
        Invoke-AdbChecked -Adb $Adb -DeviceSerial $DeviceSerial -Arguments @(
            "install", $ApkPath) | Out-Null
        if (-not $SkipLaunch) {
            Invoke-AdbChecked -Adb $Adb -DeviceSerial $DeviceSerial -Arguments @(
                "shell", "monkey", "-p", $PackageName, "-c",
                "android.intent.category.LAUNCHER", "1") | Out-Null
        }
        return [pscustomobject]@{ Mode = "new-install"; BackupPath = $null }
    }

    if ($installedCertificate -eq $targetCertificate) {
        Invoke-AdbChecked -Adb $Adb -DeviceSerial $DeviceSerial -Arguments @(
            "install", "-r", $ApkPath) | Out-Null
        if (-not $SkipLaunch) {
            Invoke-AdbChecked -Adb $Adb -DeviceSerial $DeviceSerial -Arguments @(
                "shell", "monkey", "-p", $PackageName, "-c",
                "android.intent.category.LAUNCHER", "1") | Out-Null
        }
        return [pscustomobject]@{ Mode = "in-place-update"; BackupPath = $null }
    }

    $runAsResult = Invoke-NativeCapture -FilePath $Adb -Arguments @(
        "-s", $DeviceSerial, "shell", "run-as", $PackageName, "id")
    if ($runAsResult.ExitCode -ne 0) {
        throw @"
The installed package uses a different signing certificate and does not permit a verified
app-sandbox bridge. An in-place Android update is impossible. Production builds must keep
the same protected release signing identity; this script will not uninstall an inaccessible
production data store.
"@
    }

    $safeSerial = $DeviceSerial -replace "[^A-Za-z0-9._-]", "_"
    $migrationId = [DateTimeOffset]::UtcNow.ToString("yyyyMMdd-HHmmss")
    $backupRoot = Join-Path $env:LOCALAPPDATA "ElectronicLogbook\AndroidDeviceBridge\$safeSerial\$migrationId"
    $source = New-VerifiedIndexedDbSnapshot -Adb $Adb -DeviceSerial $DeviceSerial `
        -PackageName $PackageName -DestinationRoot $backupRoot -Name "source"
    $manifestPath = Join-Path $backupRoot "migration.json"
    $manifest = [ordered]@{
        schemaVersion = 1
        status = "source-backup-verified"
        createdAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        deviceSerial = $DeviceSerial
        packageName = $PackageName
        sourceCertificateSha256 = $installedCertificate
        targetCertificateSha256 = $targetCertificate
        sourceArchive = $source.ArchivePath
        sourceArchiveSha256 = $source.ArchiveSha256
        sourceFileCount = $source.FileCount
    }
    $manifest | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding UTF8

    try {
        Invoke-AdbChecked -Adb $Adb -DeviceSerial $DeviceSerial -Arguments @(
            "uninstall", $PackageName) | Out-Null
        $manifest.status = "source-removed-target-install-pending"
        $manifest | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding UTF8

        Invoke-AdbChecked -Adb $Adb -DeviceSerial $DeviceSerial -Arguments @(
            "install", $ApkPath) | Out-Null
        $manifest.status = "target-installed-restore-pending"
        $manifest | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding UTF8

        Complete-IndexedDbRestore -Adb $Adb -DeviceSerial $DeviceSerial `
            -PackageName $PackageName -BackupRoot $backupRoot `
            -ManifestPath $manifestPath -Manifest ([pscustomobject] $manifest)
        return [pscustomobject]@{ Mode = "verified-signing-migration"; BackupPath = $backupRoot }
    }
    catch {
        throw "Android signing migration stopped safely. Verified backup: $backupRoot. $($_.Exception.Message)"
    }
}
