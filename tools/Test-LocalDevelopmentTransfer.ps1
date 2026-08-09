[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$entrypoint = Join-Path $PSScriptRoot 'Invoke-LocalDevelopmentTransfer.ps1'
$manifestPath = Join-Path $PSScriptRoot 'local-development-transfer.psd1'
$config = Import-PowerShellDataFile -LiteralPath $manifestPath
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("ElectronicLogbookTransferTests-" + [Guid]::NewGuid().ToString('N'))
$testPassword = 'Synthetic-Test-Password-Only-2026!'
$passed = 0

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "ASSERTION FAILED: $Message" }
    $script:passed++
}

function Resolve-TestSevenZip {
    $command = Get-Command '7z.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { return $command.Source }
    $candidate = Join-Path $env:ProgramFiles '7-Zip\7z.exe'
    if (Test-Path -LiteralPath $candidate) { return $candidate }
    throw '7-Zip is required for the transfer tests.'
}

function Get-TestHash {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Invoke-TransferProcess {
    param(
        [string[]]$Arguments,
        [string]$SyntheticPassword
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'powershell.exe'
    $quoted = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $entrypoint) + $Arguments
    $startInfo.Arguments = (($quoted | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }) -join ' ')
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    if (-not [string]::IsNullOrWhiteSpace($SyntheticPassword)) {
        $startInfo.EnvironmentVariables['ELB_TRANSFER_SYNTHETIC_PASSWORD'] = $SyntheticPassword
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    [void]$process.Start()
    $output = $process.StandardOutput.ReadToEnd()
    $errorOutput = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    $result = [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $output; Error = $errorOutput }
    $process.Dispose()
    return $result
}

function New-SyntheticBundle {
    param(
        [string]$SourceRoot,
        [string]$Archive,
        [switch]$TamperPayload,
        [switch]$AddUndeclaredFile
    )

    $payloadPath = Join-Path $SourceRoot 'payload\repo\AGENTS.md'
    New-Item -ItemType Directory -Path (Split-Path $payloadPath -Parent) -Force | Out-Null
    Set-Content -LiteralPath $payloadPath -Value 'synthetic restored context' -Encoding UTF8
    $hash = Get-TestHash $payloadPath
    if ($TamperPayload) {
        $bytes = [IO.File]::ReadAllBytes($payloadPath)
        $bytes[$bytes.Length - 1] = $bytes[$bytes.Length - 1] -bxor 1
        [IO.File]::WriteAllBytes($payloadPath, $bytes)
    }

    $metadataRoot = Join-Path $SourceRoot 'metadata'
    New-Item -ItemType Directory -Path $metadataRoot -Force | Out-Null
    [ordered]@{
        schemaVersion = 1
        bundleType = 'ElectronicLogbookOperationalDevelopmentState'
        createdAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        repository = @{ branch = 'dev'; commit = 'synthetic'; trackedWorktreeClean = $true }
        software = @()
        files = @(
            [ordered]@{
                bundlePath = 'payload/repo/AGENTS.md'
                targetRoot = 'Repo'
                relativePath = 'AGENTS.md'
                classification = 'private-context'
                length = (Get-Item $payloadPath).Length
                sha256 = $hash
            }
        )
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $metadataRoot 'bundle-manifest.json') -Encoding UTF8
    if ($AddUndeclaredFile) {
        Set-Content -LiteralPath (Join-Path $metadataRoot 'undeclared.txt') -Value 'must be rejected' -Encoding UTF8
    }

    Push-Location $SourceRoot
    try {
        & $script:sevenZip a -t7z $Archive '.\*' -mx=1 -mhe=on "-p$testPassword" -bb0 -bd | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not create the synthetic encrypted archive.' }
    }
    finally { Pop-Location }
}

try {
    New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null
    $script:sevenZip = Resolve-TestSevenZip

    Assert-True ($config.SchemaVersion -eq 1) 'manifest schema version must be 1'
    Assert-True (@($config.RepoAssets | Where-Object { $_.Path -eq 'AGENTS.md' -and $_.Required }).Count -eq 1) 'AGENTS.md must be required'
    Assert-True (@($config.RepoAssets | Where-Object { $_.Path -eq 'TODO.md' -and $_.Required }).Count -eq 1) 'TODO.md must be required'
    Assert-True (@($config.ForbiddenBundlePatterns | Where-Object { $_ -like '*auth.json*' }).Count -eq 1) 'Codex auth must be forbidden'
    Assert-True (@($config.ForbiddenBundlePatterns | Where-Object { $_ -like '*sessions*' }).Count -eq 1) 'Codex sessions must be forbidden'

    $entrypointText = Get-Content -LiteralPath $entrypoint -Raw -Encoding UTF8
    Assert-True ($entrypointText -match "@\('a'.*'-mhe=on'.*'-p'") 'real export must request encrypted headers and the native password prompt'
    Assert-True ($entrypointText -notmatch '(?m)^\s*\[string\]\$ArchivePassword') 'real workflow must not accept a command-line password parameter'

    $ignored = & git -C $repoRoot check-ignore 'LOCAL_DEVICE_SETUP_HANDOVER.md' 2>$null
    Assert-True ($LASTEXITCODE -ne 0) 'the sanitized handover must be trackable'
    $archiveIgnore = & git -C $repoRoot check-ignore 'ElectronicLogbook-LocalDevelopment-20990101-000000.7z' 2>$null
    Assert-True ($LASTEXITCODE -eq 0) 'generated archives must remain ignored'

    $syntheticRepo = Join-Path $temporaryRoot 'dry-run-repo'
    $syntheticLocal = Join-Path $temporaryRoot 'dry-run-localappdata'
    $syntheticCodex = Join-Path $temporaryRoot 'dry-run-codex'
    New-Item -ItemType Directory -Path $syntheticRepo, $syntheticLocal, $syntheticCodex -Force | Out-Null
    foreach ($asset in $config.RepoAssets | Where-Object Required) {
        $path = Join-Path $syntheticRepo $asset.Path
        New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
        Set-Content -LiteralPath $path -Value "synthetic $($asset.Path)" -Encoding UTF8
    }
    $localState = Join-Path $syntheticLocal 'ElectronicLogbook\synthetic.json'
    New-Item -ItemType Directory -Path (Split-Path $localState -Parent) -Force | Out-Null
    Set-Content -LiteralPath $localState -Value '{}' -Encoding UTF8
    $dryRun = Invoke-TransferProcess -Arguments @('-Action', 'Export', '-RepoRoot', $syntheticRepo, '-LocalAppDataRoot', $syntheticLocal, '-CodexHome', $syntheticCodex, '-WhatIf')
    Assert-True ($dryRun.ExitCode -eq 0) "synthetic export dry run failed: $($dryRun.Error)"
    Assert-True ($dryRun.Output -match 'Export dry run completed') 'dry run must confirm that no archive was created'

    $mainExportArchive = Join-Path $temporaryRoot 'main-export.7z'
    $mainExport = Invoke-TransferProcess -Arguments @(
        '-Action', 'Export', '-RepoRoot', $syntheticRepo, '-LocalAppDataRoot', $syntheticLocal,
        '-CodexHome', $syntheticCodex, '-OutputPath', $mainExportArchive, '-TestMode'
    ) -SyntheticPassword $testPassword
    Assert-True ($mainExport.ExitCode -eq 0) "main exporter failed against synthetic roots: $($mainExport.Error)"
    Assert-True (Test-Path -LiteralPath $mainExportArchive -PathType Leaf) 'main exporter must create the requested encrypted archive'
    & $script:sevenZip t $mainExportArchive "-p$testPassword" -bb0 -bd | Out-Null
    Assert-True ($LASTEXITCODE -eq 0) 'main exporter archive must pass 7-Zip integrity testing'

    $bundleSource = Join-Path $temporaryRoot 'valid-source'
    $validArchive = Join-Path $temporaryRoot 'valid.7z'
    New-SyntheticBundle -SourceRoot $bundleSource -Archive $validArchive
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & $script:sevenZip l $validArchive -bb0 -bd *> (Join-Path $temporaryRoot 'listing-without-password.txt')
    $listingExitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousPreference
    Assert-True ($listingExitCode -ne 0) 'encrypted headers must prevent listing without a password'
    $unauthorizedListing = Get-Content -LiteralPath (Join-Path $temporaryRoot 'listing-without-password.txt') -Raw
    Assert-True ($unauthorizedListing -notmatch 'AGENTS\.md') 'encrypted headers must hide payload filenames'
    & $script:sevenZip t $validArchive "-p$testPassword" -bb0 -bd | Out-Null
    Assert-True ($LASTEXITCODE -eq 0) 'synthetic encrypted archive must pass 7-Zip integrity testing'

    $previewRepo = Join-Path $temporaryRoot 'preview-repo'
    New-Item -ItemType Directory -Path $previewRepo -Force | Out-Null
    $previewImport = Invoke-TransferProcess -Arguments @(
        '-Action', 'Import', '-ArchivePath', $validArchive, '-RepoRoot', $previewRepo,
        '-LocalAppDataRoot', (Join-Path $temporaryRoot 'preview-local'),
        '-CodexHome', (Join-Path $temporaryRoot 'preview-codex'),
        '-SkipDependencyRestore', '-SkipPostImportVerify', '-TestMode', '-WhatIf'
    ) -SyntheticPassword $testPassword
    Assert-True ($previewImport.ExitCode -eq 0) "import WhatIf validation failed: $($previewImport.Error)"
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $previewRepo 'AGENTS.md'))) 'import WhatIf must not write destinations'

    $restoreRepo = Join-Path $temporaryRoot 'restore-repo'
    $restoreLocal = Join-Path $temporaryRoot 'restore-localappdata'
    $restoreCodex = Join-Path $temporaryRoot 'restore-codex'
    $restoreBackup = Join-Path $temporaryRoot 'restore-backup'
    New-Item -ItemType Directory -Path $restoreRepo, $restoreLocal, $restoreCodex -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $restoreRepo 'AGENTS.md') -Value 'existing context' -Encoding UTF8
    $import = Invoke-TransferProcess -Arguments @(
        '-Action', 'Import', '-ArchivePath', $validArchive, '-RepoRoot', $restoreRepo,
        '-LocalAppDataRoot', $restoreLocal, '-CodexHome', $restoreCodex,
        '-BackupRoot', $restoreBackup, '-SkipDependencyRestore', '-SkipPostImportVerify', '-TestMode'
    ) -SyntheticPassword $testPassword
    Assert-True ($import.ExitCode -eq 0) "synthetic import failed: $($import.Error)"
    Assert-True ((Get-Content -LiteralPath (Join-Path $restoreRepo 'AGENTS.md') -Raw) -match 'synthetic restored context') 'import must restore the declared file'
    Assert-True ((Get-Content -LiteralPath (Join-Path $restoreBackup 'Repo\AGENTS.md') -Raw) -match 'existing context') 'import must back up a conflicting file'

    $tamperedSource = Join-Path $temporaryRoot 'tampered-source'
    $tamperedArchive = Join-Path $temporaryRoot 'tampered.7z'
    New-SyntheticBundle -SourceRoot $tamperedSource -Archive $tamperedArchive -TamperPayload
    $tamperRepo = Join-Path $temporaryRoot 'tamper-repo'
    New-Item -ItemType Directory -Path $tamperRepo -Force | Out-Null
    $tamperedImport = Invoke-TransferProcess -Arguments @(
        '-Action', 'Import', '-ArchivePath', $tamperedArchive, '-RepoRoot', $tamperRepo,
        '-LocalAppDataRoot', (Join-Path $temporaryRoot 'tamper-local'),
        '-CodexHome', (Join-Path $temporaryRoot 'tamper-codex'),
        '-SkipDependencyRestore', '-SkipPostImportVerify', '-TestMode'
    ) -SyntheticPassword $testPassword
    Assert-True ($tamperedImport.ExitCode -ne 0) 'hash mismatch must reject the archive'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $tamperRepo 'AGENTS.md'))) 'hash rejection must happen before destination writes'

    $undeclaredSource = Join-Path $temporaryRoot 'undeclared-source'
    $undeclaredArchive = Join-Path $temporaryRoot 'undeclared.7z'
    New-SyntheticBundle -SourceRoot $undeclaredSource -Archive $undeclaredArchive -AddUndeclaredFile
    $undeclaredRepo = Join-Path $temporaryRoot 'undeclared-repo'
    New-Item -ItemType Directory -Path $undeclaredRepo -Force | Out-Null
    $undeclaredImport = Invoke-TransferProcess -Arguments @(
        '-Action', 'Import', '-ArchivePath', $undeclaredArchive, '-RepoRoot', $undeclaredRepo,
        '-LocalAppDataRoot', (Join-Path $temporaryRoot 'undeclared-local'),
        '-CodexHome', (Join-Path $temporaryRoot 'undeclared-codex'),
        '-SkipDependencyRestore', '-SkipPostImportVerify', '-TestMode'
    ) -SyntheticPassword $testPassword
    Assert-True ($undeclaredImport.ExitCode -ne 0) 'undeclared archive files must be rejected'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $undeclaredRepo 'AGENTS.md'))) 'undeclared-file rejection must happen before destination writes'

    $wrongPasswordRepo = Join-Path $temporaryRoot 'wrong-password-repo'
    New-Item -ItemType Directory -Path $wrongPasswordRepo -Force | Out-Null
    $wrongPassword = Invoke-TransferProcess -Arguments @(
        '-Action', 'Import', '-ArchivePath', $validArchive, '-RepoRoot', $wrongPasswordRepo,
        '-LocalAppDataRoot', (Join-Path $temporaryRoot 'wrong-local'),
        '-CodexHome', (Join-Path $temporaryRoot 'wrong-codex'),
        '-SkipDependencyRestore', '-SkipPostImportVerify', '-TestMode'
    ) -SyntheticPassword 'Wrong-Synthetic-Password!'
    Assert-True ($wrongPassword.ExitCode -ne 0) 'wrong password must fail import'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $wrongPasswordRepo 'AGENTS.md'))) 'wrong password must not change destinations'

    $corruptArchive = Join-Path $temporaryRoot 'corrupt.7z'
    [IO.File]::Copy($validArchive, $corruptArchive)
    $corruptBytes = [IO.File]::ReadAllBytes($corruptArchive)
    $corruptIndex = [math]::Floor($corruptBytes.Length * 0.6)
    $corruptBytes[$corruptIndex] = $corruptBytes[$corruptIndex] -bxor 255
    [IO.File]::WriteAllBytes($corruptArchive, $corruptBytes)
    $corruptRepo = Join-Path $temporaryRoot 'corrupt-repo'
    New-Item -ItemType Directory -Path $corruptRepo -Force | Out-Null
    $corruptImport = Invoke-TransferProcess -Arguments @(
        '-Action', 'Import', '-ArchivePath', $corruptArchive, '-RepoRoot', $corruptRepo,
        '-LocalAppDataRoot', (Join-Path $temporaryRoot 'corrupt-local'),
        '-CodexHome', (Join-Path $temporaryRoot 'corrupt-codex'),
        '-SkipDependencyRestore', '-SkipPostImportVerify', '-TestMode'
    ) -SyntheticPassword $testPassword
    Assert-True ($corruptImport.ExitCode -ne 0) 'corrupted archive must fail import'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $corruptRepo 'AGENTS.md'))) 'corrupted archive must not change destinations'

    Write-Host "Local development transfer tests passed: $passed assertions." -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
