[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Export', 'Import', 'Install', 'Verify')]
    [string]$Action,

    [string]$ArchivePath,

    [string]$OutputPath,

    [string]$RepoRoot,

    [string]$LocalAppDataRoot = $env:LOCALAPPDATA,

    [string]$CodexHome = (Join-Path $env:USERPROFILE '.codex'),

    [string]$BackupRoot,

    [switch]$SkipDependencyRestore,

    [switch]$SkipPostImportVerify,

    [switch]$TestMode
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptPath = $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path (Split-Path $scriptPath -Parent) -Parent
}
$script:RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
$script:ManifestPath = Join-Path $PSScriptRoot 'local-development-transfer.psd1'
$script:TransferConfig = Import-PowerShellDataFile -LiteralPath $script:ManifestPath

function Write-Step {
    param([string]$Message)
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Resolve-SevenZip {
    $candidates = @(
        (Get-Command '7z.exe' -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source),
        (Join-Path $env:ProgramFiles '7-Zip\7z.exe'),
        $(if (${env:ProgramFiles(x86)}) { Join-Path ${env:ProgramFiles(x86)} '7-Zip\7z.exe' })
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw '7-Zip was not found. Run this script with -Action Install or install 7zip.7zip with winget.'
}

function Invoke-SevenZip {
    param(
        [string]$SevenZip,
        [string[]]$Arguments
    )

    if ($TestMode) {
        $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        foreach ($root in @($script:RepoRoot, $LocalAppDataRoot, $CodexHome)) {
            if (-not [IO.Path]::GetFullPath($root).StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) {
                throw 'TestMode is allowed only when every destination root is under the Windows temporary directory.'
            }
        }
    }

    if ($TestMode) {
        & $SevenZip @Arguments | Out-Null
        $exitCode = $LASTEXITCODE
    }
    else {
        & $SevenZip @Arguments
        $exitCode = $LASTEXITCODE
    }
    if ($exitCode -ne 0) {
        throw "7-Zip exited with code $exitCode. The archive may be damaged."
    }
}

function ConvertTo-PortablePath {
    param([string]$Path)
    return $Path.Replace('\', '/').TrimStart('/')
}

function Get-Sha256Hex {
    param([string]$Path)

    $stream = [IO.File]::OpenRead($Path)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $algorithm.Dispose()
        $stream.Dispose()
    }
}

function Test-TransferPathExcluded {
    param([string]$PortablePath)

    $candidate = '/' + (ConvertTo-PortablePath $PortablePath) + '/'
    foreach ($pattern in $script:TransferConfig.ExcludedPathPatterns) {
        $normalizedPattern = '/' + (ConvertTo-PortablePath $pattern).Trim('/')
        if ($candidate -like "$normalizedPattern*" -or (ConvertTo-PortablePath $PortablePath) -like (ConvertTo-PortablePath $pattern)) {
            return $true
        }
    }
    return $false
}

function Test-ForbiddenBundlePath {
    param([string]$PortablePath)

    $candidate = '/' + (ConvertTo-PortablePath $PortablePath)
    foreach ($pattern in $script:TransferConfig.ForbiddenBundlePatterns) {
        if ($candidate -like ('/' + (ConvertTo-PortablePath $pattern).TrimStart('/'))) {
            return $true
        }
    }
    return $false
}

function Get-SanitizedCodexPreferences {
    param([string]$ConfigPath)

    $result = [ordered]@{
        note = 'Reference-only sanitized snapshot. Authentication, MCP environment values, paths, sessions, and databases are deliberately excluded.'
        topLevel = [ordered]@{}
        windows = [ordered]@{}
        features = [ordered]@{}
        plugins = [ordered]@{}
    }
    if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) {
        return $result
    }

    $section = ''
    foreach ($line in Get-Content -LiteralPath $ConfigPath -Encoding UTF8) {
        $trimmed = $line.Trim()
        if ($trimmed -match '^\[(.+)\]$') {
            $section = $Matches[1]
            continue
        }
        if ($trimmed -notmatch '^([A-Za-z0-9_.-]+)\s*=\s*(.+)$') { continue }

        $key = $Matches[1]
        $value = $Matches[2]
        if ($section -eq '' -and $key -in @('model', 'model_reasoning_effort', 'service_tier')) {
            $result.topLevel[$key] = $value
        }
        elseif ($section -eq 'windows' -and $key -eq 'sandbox') {
            $result.windows[$key] = $value
        }
        elseif ($section -eq 'features') {
            $result.features[$key] = $value
        }
        elseif ($section -like 'plugins.*' -and $key -eq 'enabled') {
            $result.plugins[$section.Substring(8)] = $value
        }
    }
    return $result
}

function Get-CommandInventory {
    $definitions = @(
        @{ Name = 'git'; Args = @('--version') }
        @{ Name = 'dotnet'; Args = @('--version') }
        @{ Name = 'node'; Args = @('--version') }
        @{ Name = 'npm.cmd'; Args = @('--version') }
        @{ Name = 'java'; Args = @('-version') }
        @{ Name = 'adb'; Args = @('version') }
        @{ Name = 'docker'; Args = @('--version') }
        @{ Name = 'supabase'; Args = @('--version') }
        @{ Name = 'psql'; Args = @('--version') }
        @{ Name = 'code'; Args = @('--version') }
        @{ Name = 'codex'; Args = @('--version') }
        @{ Name = 'graphify'; Args = @('--version') }
    )

    $inventory = @()
    foreach ($definition in $definitions) {
        $command = Get-Command $definition.Name -ErrorAction SilentlyContinue | Select-Object -First 1
        $version = $null
        if ($null -ne $command) {
            try {
                $version = ((& $command.Source @($definition.Args) 2>&1 | Out-String).Trim() -split "`r?`n")[0]
            }
            catch {
                $version = 'present; version unavailable'
            }
        }
        $inventory += [ordered]@{ command = $definition.Name; present = ($null -ne $command); version = $version }
    }
    return $inventory
}

function Get-NativeCommandOutput {
    param([string]$Name, [string[]]$Arguments = @())

    $command = Get-Command $Name -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $command) { return '' }
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        return ((& $command.Source @Arguments 2>&1 | Out-String).Trim())
    }
    catch { return '' }
    finally { $ErrorActionPreference = $previousPreference }
}

function Get-AssetMatches {
    param(
        [string]$Root,
        [hashtable]$Asset
    )

    $nativeRelative = $Asset.Path.Replace('/', [IO.Path]::DirectorySeparatorChar)
    $candidate = Join-Path $Root $nativeRelative
    $hasWildcard = $Asset.Path.IndexOfAny([char[]]'*?[') -ge 0
    if ($hasWildcard) {
        return @(Get-ChildItem -Path $candidate -Force -ErrorAction SilentlyContinue)
    }
    if (Test-Path -LiteralPath $candidate) {
        return @((Get-Item -LiteralPath $candidate -Force))
    }
    return @()
}

function Add-AssetToStage {
    param(
        [string]$SourceRoot,
        [string]$TargetRootName,
        [hashtable]$Asset,
        [string]$StageRoot,
        [Collections.Generic.List[object]]$Records,
        [switch]$InventoryOnly
    )

    $matches = @(Get-AssetMatches -Root $SourceRoot -Asset $Asset)
    if ($matches.Count -eq 0) {
        return [pscustomobject]@{ Missing = $true; Required = [bool]$Asset.Required; Path = $Asset.Path }
    }

    foreach ($match in $matches) {
        $files = if ($match.PSIsContainer) {
            @(Get-ChildItem -LiteralPath $match.FullName -Recurse -Force -File -ErrorAction Stop)
        }
        else { @($match) }

        foreach ($file in $files) {
            $relative = $file.FullName.Substring($SourceRoot.TrimEnd('\').Length).TrimStart('\')
            $bundlePath = ConvertTo-PortablePath (Join-Path "payload\$($TargetRootName.ToLowerInvariant())" $relative)
            if (Test-TransferPathExcluded $bundlePath) { continue }
            if (Test-ForbiddenBundlePath $bundlePath) {
                throw "A forbidden path was selected for transfer: $bundlePath"
            }

            $hash = Get-Sha256Hex -Path $file.FullName
            $Records.Add([ordered]@{
                bundlePath = $bundlePath
                targetRoot = $TargetRootName
                relativePath = ConvertTo-PortablePath $relative
                classification = $Asset.Classification
                length = [long]$file.Length
                sha256 = $hash
            })

            if (-not $InventoryOnly) {
                $destination = Join-Path $StageRoot $bundlePath.Replace('/', '\')
                New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
                Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
            }
        }
    }

    return [pscustomobject]@{ Missing = $false; Required = [bool]$Asset.Required; Path = $Asset.Path }
}

function Invoke-ExportAction {
    $records = [Collections.Generic.List[object]]::new()
    $missing = @()
    $inventoryOnly = [bool]$WhatIfPreference
    $stageRoot = Join-Path ([IO.Path]::GetTempPath()) ("ElectronicLogbookTransfer-" + [Guid]::NewGuid().ToString('N'))

    try {
        Write-Step 'Classifying operational files'
        if (-not $inventoryOnly) { New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null }

        foreach ($asset in $script:TransferConfig.RepoAssets) {
            $result = Add-AssetToStage -SourceRoot $script:RepoRoot -TargetRootName 'Repo' -Asset $asset -StageRoot $stageRoot -Records $records -InventoryOnly:$inventoryOnly
            if ($result.Missing) { $missing += $result }
        }
        foreach ($asset in $script:TransferConfig.LocalAppDataAssets) {
            $result = Add-AssetToStage -SourceRoot $LocalAppDataRoot -TargetRootName 'LocalAppData' -Asset $asset -StageRoot $stageRoot -Records $records -InventoryOnly:$inventoryOnly
            if ($result.Missing) { $missing += $result }
        }
        foreach ($asset in $script:TransferConfig.CodexAssets) {
            $result = Add-AssetToStage -SourceRoot $CodexHome -TargetRootName 'Codex' -Asset $asset -StageRoot $stageRoot -Records $records -InventoryOnly:$inventoryOnly
            if ($result.Missing) { $missing += $result }
        }

        foreach ($item in $missing) {
            $label = if ($item.Required) { 'REQUIRED' } else { 'optional' }
            Write-Host "[$label missing] $($item.Path)"
        }
        $requiredMissing = @($missing | Where-Object Required)
        if ($requiredMissing.Count -gt 0) {
            throw "$($requiredMissing.Count) required transfer asset(s) are missing."
        }
        if ($records.Count -eq 0) { throw 'The transfer inventory is empty.' }

        $totalBytes = ($records | ForEach-Object { [long]$_['length'] } | Measure-Object -Sum).Sum
        Write-Host "Inventory: $($records.Count) files, $([math]::Round(($totalBytes / 1MB), 2)) MiB"
        if ($inventoryOnly) {
            Write-Host 'Export dry run completed. No staging directory or archive was created.' -ForegroundColor Green
            return
        }

        $metadataRoot = Join-Path $stageRoot 'metadata'
        New-Item -ItemType Directory -Path $metadataRoot -Force | Out-Null
        $preferences = Get-SanitizedCodexPreferences -ConfigPath (Join-Path $CodexHome 'config.toml')
        $preferences | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $metadataRoot 'codex-preferences.json') -Encoding UTF8

        $gitBranch = $null
        $gitCommit = $null
        $gitStatusClean = $false
        if (Test-Path -LiteralPath (Join-Path $script:RepoRoot '.git')) {
            $gitBranch = Get-NativeCommandOutput -Name 'git' -Arguments @('-C', $script:RepoRoot, 'branch', '--show-current')
            $gitCommit = Get-NativeCommandOutput -Name 'git' -Arguments @('-C', $script:RepoRoot, 'rev-parse', 'HEAD')
            $gitStatus = Get-NativeCommandOutput -Name 'git' -Arguments @('-C', $script:RepoRoot, 'status', '--porcelain', '--untracked-files=normal')
            $gitStatusClean = [string]::IsNullOrWhiteSpace($gitStatus)
        }
        $bundleManifest = [ordered]@{
            schemaVersion = [int]$script:TransferConfig.SchemaVersion
            bundleType = 'ElectronicLogbookOperationalDevelopmentState'
            createdAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
            repository = [ordered]@{ branch = $gitBranch; commit = $gitCommit; trackedWorktreeClean = $gitStatusClean }
            software = @(Get-CommandInventory)
            files = @($records)
        }
        $bundleManifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $metadataRoot 'bundle-manifest.json') -Encoding UTF8

        $archive = if (-not [string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath } elseif (-not [string]::IsNullOrWhiteSpace($ArchivePath)) { $ArchivePath } else {
            Join-Path ([Environment]::GetFolderPath('UserProfile')) ("Downloads\ElectronicLogbook-LocalDevelopment-{0}.7z" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
        }
        if ([IO.Path]::GetExtension($archive) -ne '.7z') { $archive += '.7z' }
        $archive = [IO.Path]::GetFullPath($archive)
        New-Item -ItemType Directory -Path (Split-Path $archive -Parent) -Force | Out-Null
        if (Test-Path -LiteralPath $archive) { throw "Archive already exists: $archive" }

        $sevenZip = Resolve-SevenZip
        Write-Step 'Creating unencrypted 7-Zip archive'
        Write-Warning 'This archive contains private credentials and signing material. Keep it on trusted storage and delete transfer copies when finished.'
        Push-Location $stageRoot
        try {
            Invoke-SevenZip -SevenZip $sevenZip -Arguments @('a', '-t7z', $archive, '.\*', '-mx=9', '-bb0', '-bd')
        }
        finally { Pop-Location }

        Write-Step 'Testing archive integrity'
        Invoke-SevenZip -SevenZip $sevenZip -Arguments @('t', $archive, '-bb0', '-bd')
        Write-Host "Local development handover created: $archive" -ForegroundColor Green
    }
    finally {
        if (Test-Path -LiteralPath $stageRoot) { Remove-Item -LiteralPath $stageRoot -Recurse -Force }
    }
}

function Get-SafeStagedPath {
    param([string]$StageRoot, [string]$BundlePath)

    $portable = ConvertTo-PortablePath $BundlePath
    if ($portable -match '(^|/)\.\.(/|$)' -or [IO.Path]::IsPathRooted($portable)) {
        throw "Unsafe bundle path: $BundlePath"
    }
    $root = [IO.Path]::GetFullPath($StageRoot).TrimEnd('\') + '\'
    $resolved = [IO.Path]::GetFullPath((Join-Path $StageRoot $portable.Replace('/', '\')))
    if (-not $resolved.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Bundle path escapes the staging directory: $BundlePath"
    }
    return $resolved
}

function Set-AndroidLocalProperties {
    param([string]$RepositoryRoot, [string]$AndroidSdkRoot)

    if (-not (Test-Path -LiteralPath $AndroidSdkRoot -PathType Container)) { return }
    $path = Join-Path $RepositoryRoot 'mobile\android\local.properties'
    $value = 'sdk.dir=' + ($AndroidSdkRoot.Replace('\', '/').Replace(':', '\:'))
    if ($PSCmdlet.ShouldProcess($path, 'Regenerate Android SDK path')) {
        Set-Content -LiteralPath $path -Value $value -Encoding ASCII
    }
}

function Update-AndroidSigningMetadata {
    param([string]$LocalRoot)

    $signingRoot = Join-Path $LocalRoot 'ElectronicLogbook\AndroidSigning'
    $identities = @(
        @{
            Name = 'development'
            Metadata = 'electronic-logbook-development.json'
            Keystore = 'electronic-logbook-development.keystore'
            Credentials = $null
        },
        @{
            Name = 'permanent Preview'
            Metadata = $script:TransferConfig.Expected.PreviewSigningMetadataFile
            Keystore = $script:TransferConfig.Expected.PreviewSigningKeystoreFile
            Credentials = $script:TransferConfig.Expected.PreviewSigningCredentialsFile
        }
    )

    foreach ($identity in $identities) {
        $metadataPath = Join-Path $signingRoot $identity.Metadata
        $keystorePath = Join-Path $signingRoot $identity.Keystore
        $requiredPaths = @($metadataPath, $keystorePath)
        if ($identity.Credentials) {
            $requiredPaths += Join-Path $signingRoot $identity.Credentials
        }

        $existingCount = @($requiredPaths | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }).Count
        if ($existingCount -eq 0) { continue }
        if ($existingCount -ne $requiredPaths.Count) {
            throw "The $($identity.Name) Android signing identity was restored incompletely."
        }

        $metadata = Get-Content -LiteralPath $metadataPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $metadata.keystorePath = $keystorePath
        $metadata.updatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        if ($PSCmdlet.ShouldProcess($metadataPath, 'Rewrite restored keystore path')) {
            $metadata | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $metadataPath -Encoding UTF8
        }
    }
}

function Invoke-DependencyRestore {
    if ($SkipDependencyRestore) { return }
    Write-Step 'Restoring repository dependencies'
    & dotnet restore (Join-Path $script:RepoRoot 'ElectronicLogbook.Updater.sln')
    if ($LASTEXITCODE -ne 0) { throw 'Updater solution restore failed.' }
    & dotnet restore (Join-Path $script:RepoRoot 'mobile\src\ElectronicLogbook.Mobile\ElectronicLogbook.Mobile.csproj')
    if ($LASTEXITCODE -ne 0) { throw 'Mobile project restore failed.' }
    Push-Location (Join-Path $script:RepoRoot 'mobile')
    try {
        & npm.cmd ci
        if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }
    }
    finally { Pop-Location }
}

function Invoke-ImportAction {
    if ([string]::IsNullOrWhiteSpace($ArchivePath)) { throw '-ArchivePath is required for Import.' }
    $archive = (Resolve-Path -LiteralPath $ArchivePath).Path
    $sevenZip = Resolve-SevenZip
    $stageRoot = Join-Path ([IO.Path]::GetTempPath()) ("ElectronicLogbookTransferImport-" + [Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($stageRoot)

    try {
        Write-Step 'Extracting archive into temporary staging'
        Invoke-SevenZip -SevenZip $sevenZip -Arguments @('x', $archive, "-o$stageRoot", '-y', '-bb0', '-bd')

        $manifestPath = Join-Path $stageRoot 'metadata\bundle-manifest.json'
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'Archive does not contain bundle-manifest.json.' }
        $bundle = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($bundle.schemaVersion -ne $script:TransferConfig.SchemaVersion -or $bundle.bundleType -ne 'ElectronicLogbookOperationalDevelopmentState') {
            throw 'The archive schema or bundle type is not supported.'
        }

        Write-Step 'Validating paths and SHA-256 hashes'
        $declared = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($record in $bundle.files) {
            if ($record.targetRoot -notin @('Repo', 'LocalAppData', 'Codex')) { throw "Unsupported target root: $($record.targetRoot)" }
            if (Test-ForbiddenBundlePath $record.bundlePath) { throw "Archive contains forbidden content: $($record.bundlePath)" }
            $expectedBundlePath = "payload/$($record.targetRoot.ToLowerInvariant())/$(ConvertTo-PortablePath $record.relativePath)"
            if ((ConvertTo-PortablePath $record.bundlePath) -cne $expectedBundlePath) {
                throw "Bundle path and destination declaration do not agree: $($record.bundlePath)"
            }
            $source = Get-SafeStagedPath -StageRoot $stageRoot -BundlePath $record.bundlePath
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Archive file is missing: $($record.bundlePath)" }
            if (-not $declared.Add((ConvertTo-PortablePath $record.bundlePath))) { throw "Archive has a duplicate path: $($record.bundlePath)" }
            $sourceItem = Get-Item -LiteralPath $source -Force
            if (($sourceItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Archive contains a link or reparse point: $($record.bundlePath)" }
            if ([long]$sourceItem.Length -ne [long]$record.length) { throw "Length validation failed for $($record.bundlePath)" }
            $actualHash = Get-Sha256Hex -Path $source
            if ($actualHash -ne $record.sha256) { throw "Hash validation failed for $($record.bundlePath)" }
        }

        $allStagedFiles = @(Get-ChildItem -LiteralPath $stageRoot -Recurse -Force -File | ForEach-Object {
            ConvertTo-PortablePath $_.FullName.Substring($stageRoot.Length).TrimStart('\')
        })
        $allowedMetadata = @('metadata/bundle-manifest.json', 'metadata/codex-preferences.json')
        foreach ($stagedFile in $allStagedFiles) {
            if (-not $declared.Contains($stagedFile) -and $stagedFile -notin $allowedMetadata) {
                throw "Archive contains an undeclared file: $stagedFile"
            }
        }

        $rootMap = @{
            Repo = $script:RepoRoot
            LocalAppData = $LocalAppDataRoot
            Codex = $CodexHome
        }
        if ([string]::IsNullOrWhiteSpace($BackupRoot)) {
            $BackupRoot = Join-Path (Split-Path $LocalAppDataRoot -Parent) ("ElectronicLogbook-DeviceTransfer\RestoreBackups\" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
        }

        Write-Host "Validated $($bundle.files.Count) payload files. Existing destinations will be backed up before replacement."
        foreach ($record in $bundle.files) {
            $source = Get-SafeStagedPath -StageRoot $stageRoot -BundlePath $record.bundlePath
            $targetRoot = $rootMap[$record.targetRoot]
            $destination = Get-SafeStagedPath -StageRoot $targetRoot -BundlePath $record.relativePath
            if (Test-Path -LiteralPath $destination -PathType Leaf) {
                $backup = Join-Path $BackupRoot (Join-Path $record.targetRoot $record.relativePath.Replace('/', '\'))
                if ($PSCmdlet.ShouldProcess($backup, "Back up existing $($record.targetRoot) file")) {
                    New-Item -ItemType Directory -Path (Split-Path $backup -Parent) -Force | Out-Null
                    Copy-Item -LiteralPath $destination -Destination $backup -Force
                }
            }
            if ($PSCmdlet.ShouldProcess($destination, "Restore $($record.classification)")) {
                New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
                Copy-Item -LiteralPath $source -Destination $destination -Force
            }
        }

        Update-AndroidSigningMetadata -LocalRoot $LocalAppDataRoot
        $sdkRoot = Join-Path $LocalAppDataRoot $script:TransferConfig.Environment.AndroidSdkRelativeToLocalAppData
        Set-AndroidLocalProperties -RepositoryRoot $script:RepoRoot -AndroidSdkRoot $sdkRoot

        if (-not $WhatIfPreference) {
            Invoke-DependencyRestore
            if (-not $SkipPostImportVerify) { Invoke-VerifyAction }
            Write-Host "Restore completed. Conflict backups: $BackupRoot" -ForegroundColor Green
            Write-Host 'GitHub and Codex must be signed in again on this device.' -ForegroundColor Yellow
        }
    }
    finally {
        if ([IO.Directory]::Exists($stageRoot)) { [IO.Directory]::Delete($stageRoot, $true) }
    }
}

function Add-UserPathEntry {
    param([string]$Entry)

    $expanded = [Environment]::ExpandEnvironmentVariables($Entry)
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $parts = @($userPath -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($parts -notcontains $expanded) {
        [Environment]::SetEnvironmentVariable('Path', (($parts + $expanded) -join ';'), 'User')
    }
    if (($env:Path -split ';') -notcontains $expanded) { $env:Path += ";$expanded" }
}

function Find-AndroidSdkManager {
    $sdkRoot = Join-Path $LocalAppDataRoot $script:TransferConfig.Environment.AndroidSdkRelativeToLocalAppData
    $candidate = Join-Path $sdkRoot 'cmdline-tools\latest\bin\sdkmanager.bat'
    if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    $command = Get-Command 'sdkmanager.bat' -ErrorAction SilentlyContinue | Select-Object -First 1
    return $(if ($command) { $command.Source } else { $null })
}

function Invoke-InstallAction {
    if ($env:OS -ne 'Windows_NT') { throw 'Install is supported only on Windows.' }
    if (-not (Get-Command winget.exe -ErrorAction SilentlyContinue)) { throw 'winget is required. Install or update Windows App Installer first.' }

    Write-Step 'Installing Windows packages'
    foreach ($package in $script:TransferConfig.WingetPackages) {
        if ($PSCmdlet.ShouldProcess($package.Id, "Install $($package.Name) with winget")) {
            $listOutput = & winget.exe list --id $package.Id --exact --accept-source-agreements 2>&1 | Out-String
            $installed = $LASTEXITCODE -eq 0 -and $listOutput -match [regex]::Escape($package.Id)
            $wingetAction = if ($installed) { 'upgrade' } else { 'install' }
            & winget.exe $wingetAction --id $package.Id --exact --accept-package-agreements --accept-source-agreements --silent
            if ($LASTEXITCODE -ne 0) {
                $verifyOutput = & winget.exe list --id $package.Id --exact --accept-source-agreements 2>&1 | Out-String
                $stillInstalled = $LASTEXITCODE -eq 0 -and $verifyOutput -match [regex]::Escape($package.Id)
                if ($installed -and $stillInstalled) {
                    Write-Host "$($package.Name) is already installed; no applicable Winget upgrade was found."
                }
                else {
                    $message = "winget could not install $($package.Name) ($($package.Id))."
                    if ($package.Required) { throw $message } else { Write-Warning $message }
                }
            }
        }
    }

    if (-not $WhatIfPreference) {
        $machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')
        $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
        $env:Path = @($machinePath, $userPath) -join ';'
    }

    $javaRoot = Get-ChildItem -LiteralPath $script:TransferConfig.Environment.JavaInstallRoot -Directory -Filter $script:TransferConfig.Environment.JavaDirectoryPattern -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | Select-Object -First 1 -ExpandProperty FullName
    $androidRoot = Join-Path $LocalAppDataRoot $script:TransferConfig.Environment.AndroidSdkRelativeToLocalAppData
    if ($javaRoot) {
        if ($PSCmdlet.ShouldProcess('JAVA_HOME', "Set to $javaRoot")) {
            [Environment]::SetEnvironmentVariable('JAVA_HOME', $javaRoot, 'User')
            $env:JAVA_HOME = $javaRoot
        }
    }
    if ($PSCmdlet.ShouldProcess('ANDROID_HOME and ANDROID_SDK_ROOT', "Set to $androidRoot")) {
        [Environment]::SetEnvironmentVariable('ANDROID_HOME', $androidRoot, 'User')
        [Environment]::SetEnvironmentVariable('ANDROID_SDK_ROOT', $androidRoot, 'User')
        $env:ANDROID_HOME = $androidRoot
        $env:ANDROID_SDK_ROOT = $androidRoot
    }
    foreach ($entry in $script:TransferConfig.Environment.UserPathEntries) {
        if ($PSCmdlet.ShouldProcess('User PATH', "Add $entry")) { Add-UserPathEntry $entry }
    }

    $npm = Get-Command 'npm.cmd' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($npm) {
        foreach ($package in $script:TransferConfig.NpmGlobalPackages) {
            if ($PSCmdlet.ShouldProcess($package.Package, 'Install global npm package')) {
                & $npm.Source install --global $package.Package
                if ($LASTEXITCODE -ne 0 -and $package.Required) { throw "npm could not install $($package.Package)." }
            }
        }
    }
    elseif (-not $WhatIfPreference -and @($script:TransferConfig.NpmGlobalPackages | Where-Object Required).Count -gt 0) {
        throw 'npm was installed but is not available in the refreshed PATH. Open a new terminal and rerun Install.'
    }

    $uv = Get-Command 'uv.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($uv) {
        foreach ($tool in $script:TransferConfig.UvTools) {
            if ($PSCmdlet.ShouldProcess($tool.Package, 'Install or upgrade uv tool')) {
                & $uv.Source tool install --upgrade $tool.Package
                if ($LASTEXITCODE -ne 0 -and $tool.Required) { throw "uv could not install $($tool.Package)." }
            }
        }
    }

    $code = Get-Command 'code.cmd' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($code) {
        foreach ($extension in $script:TransferConfig.VsCodeExtensions) {
            if ($PSCmdlet.ShouldProcess($extension.Id, 'Install VS Code extension')) {
                & $code.Source --install-extension $extension.Id --force
                if ($LASTEXITCODE -ne 0 -and $extension.Required) { throw "VS Code extension install failed: $($extension.Id)" }
            }
        }
    }
    elseif (-not $WhatIfPreference -and @($script:TransferConfig.VsCodeExtensions | Where-Object Required).Count -gt 0) {
        throw 'VS Code was installed but code.cmd is not available in the refreshed PATH. Open a new terminal and rerun Install.'
    }

    $sdkManager = Find-AndroidSdkManager
    if ($sdkManager) {
        if ($PSCmdlet.ShouldProcess('Android SDK', 'Install platform 36, build-tools 35.0.0, and platform-tools')) {
            & $sdkManager --sdk_root=$androidRoot 'platform-tools' "platforms;$($script:TransferConfig.Expected.AndroidPlatform)" "build-tools;$($script:TransferConfig.Expected.AndroidBuildTools)"
            if ($LASTEXITCODE -ne 0) { Write-Warning 'Android SDK packages were not fully installed. Review licenses and use Android Studio SDK Manager.' }
        }
    }
    else {
        Write-Warning 'sdkmanager is not available yet. Open Android Studio, install its command-line tools, then rerun Install.'
    }

    Write-Step 'Manual checkpoints'
    $script:TransferConfig.ManualCheckpoints | ForEach-Object { Write-Host "- $_" }
    if (-not $WhatIfPreference) {
        Write-Host 'Installation pass completed. Open a new terminal, complete the checkpoints, then run -Action Verify.' -ForegroundColor Green
    }
}

function Add-CheckResult {
    param(
        [Collections.Generic.List[object]]$Results,
        [string]$Name,
        [bool]$Passed,
        [bool]$Required,
        [string]$Detail
    )
    $Results.Add([pscustomobject]@{ Check = $Name; Status = if ($Passed) { 'PASS' } elseif ($Required) { 'FAIL' } else { 'WARN' }; Detail = $Detail })
}

function Test-RecoveryEnvelopeSecretFile {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return [pscustomobject]@{ Passed = $false; Detail = 'missing private recovery configuration' }
    }

    try {
        $values = @{}
        foreach ($line in Get-Content -LiteralPath $Path -Encoding UTF8) {
            if ($line -match '^([^#=]+)=(.*)$') { $values[$Matches[1].Trim()] = $Matches[2].Trim() }
        }
        $required = @(
            'RECOVERY_INGRESS_PUBLIC_KEY_SPKI_BASE64',
            'RECOVERY_INGRESS_PRIVATE_KEY_PKCS8_BASE64',
            'RECOVERY_KEK_BASE64',
            'RECOVERY_KEY_VERSION_ID'
        )
        if (@($required | Where-Object { [string]::IsNullOrWhiteSpace([string]$values[$_]) }).Count -gt 0) {
            return [pscustomobject]@{ Passed = $false; Detail = 'required recovery variable missing' }
        }

        $publicBytes = [Convert]::FromBase64String($values.RECOVERY_INGRESS_PUBLIC_KEY_SPKI_BASE64)
        $privateBytes = [Convert]::FromBase64String($values.RECOVERY_INGRESS_PRIVATE_KEY_PKCS8_BASE64)
        $kek = [Convert]::FromBase64String($values.RECOVERY_KEK_BASE64)
        if ($kek.Length -ne 32) {
            return [pscustomobject]@{ Passed = $false; Detail = 'recovery KEK is not 32 bytes' }
        }

        $publicRsa = [Security.Cryptography.RSA]::Create()
        $privateRsa = [Security.Cryptography.RSA]::Create()
        $probe = [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
        $unwrapped = $null
        $bytesRead = 0
        try {
            $publicRsa.ImportSubjectPublicKeyInfo($publicBytes, [ref]$bytesRead)
            $bytesRead = 0
            $privateRsa.ImportPkcs8PrivateKey($privateBytes, [ref]$bytesRead)
            $wrapped = $publicRsa.Encrypt($probe, [Security.Cryptography.RSAEncryptionPadding]::OaepSHA256)
            $unwrapped = $privateRsa.Decrypt($wrapped, [Security.Cryptography.RSAEncryptionPadding]::OaepSHA256)
            if (-not [Security.Cryptography.CryptographicOperations]::FixedTimeEquals($probe, $unwrapped)) {
                return [pscustomobject]@{ Passed = $false; Detail = 'recovery ingress public/private keys do not match' }
            }
        }
        finally {
            if ($probe) { [Security.Cryptography.CryptographicOperations]::ZeroMemory($probe) }
            if ($null -ne $unwrapped) { [Security.Cryptography.CryptographicOperations]::ZeroMemory($unwrapped) }
            $publicRsa.Dispose()
            $privateRsa.Dispose()
            [Security.Cryptography.CryptographicOperations]::ZeroMemory($privateBytes)
            [Security.Cryptography.CryptographicOperations]::ZeroMemory($kek)
        }
        return [pscustomobject]@{ Passed = $true; Detail = 'required values present; RSA pair and 32-byte KEK verified' }
    }
    catch {
        return [pscustomobject]@{ Passed = $false; Detail = 'private recovery configuration is invalid' }
    }
}

function Invoke-VerifyAction {
    Write-Step 'Verifying development environment'
    $results = [Collections.Generic.List[object]]::new()

    foreach ($name in @('git', 'dotnet', 'node', 'npm.cmd', 'java', 'adb', 'supabase', 'firebase', 'psql', 'code')) {
        $command = Get-Command $name -ErrorAction SilentlyContinue | Select-Object -First 1
        Add-CheckResult $results $name ($null -ne $command) $true $(if ($command) { $command.Source } else { 'not found on PATH' })
    }
    foreach ($name in @('gh', 'pwsh', 'docker', 'codex', 'graphify')) {
        $command = Get-Command $name -ErrorAction SilentlyContinue | Select-Object -First 1
        Add-CheckResult $results $name ($null -ne $command) $false $(if ($command) { $command.Source } else { 'not found on PATH; install if that workflow is needed' })
    }

    try { $sevenZip = Resolve-SevenZip; Add-CheckResult $results '7-Zip' $true $true $sevenZip }
    catch { Add-CheckResult $results '7-Zip' $false $true $_.Exception.Message }

    $sdks = @((Get-NativeCommandOutput -Name 'dotnet' -Arguments @('--list-sdks')) -split "`r?`n" | Where-Object { $_ })
    foreach ($major in $script:TransferConfig.Expected.DotNetSdkMajors) {
        Add-CheckResult $results ".NET SDK $major" (@($sdks | Where-Object { $_ -match "^$major\." }).Count -gt 0) $true (($sdks -join ', '))
    }

    $nodeVersion = Get-NativeCommandOutput -Name 'node' -Arguments @('--version')
    Add-CheckResult $results 'Node major' ($nodeVersion -match "^v$($script:TransferConfig.Expected.NodeMajor)\.") $true $nodeVersion
    $javaVersion = Get-NativeCommandOutput -Name 'java' -Arguments @('-version')
    Add-CheckResult $results 'Java 21' ($javaVersion -match 'version "21\.') $true (($javaVersion -split "`r?`n")[0])

    $androidRoot = Join-Path $LocalAppDataRoot $script:TransferConfig.Environment.AndroidSdkRelativeToLocalAppData
    Add-CheckResult $results 'ANDROID_HOME' ($env:ANDROID_HOME -eq $androidRoot) $true "expected $androidRoot"
    Add-CheckResult $results 'ANDROID_SDK_ROOT' ($env:ANDROID_SDK_ROOT -eq $androidRoot) $true "expected $androidRoot"
    Add-CheckResult $results 'Android SDK 36' (Test-Path -LiteralPath (Join-Path $androidRoot "platforms\$($script:TransferConfig.Expected.AndroidPlatform)")) $true $androidRoot
    Add-CheckResult $results 'Android build-tools' (Test-Path -LiteralPath (Join-Path $androidRoot "build-tools\$($script:TransferConfig.Expected.AndroidBuildTools)")) $true $script:TransferConfig.Expected.AndroidBuildTools

    $supabaseVersion = Get-NativeCommandOutput -Name 'supabase' -Arguments @('--version')
    Add-CheckResult $results 'Supabase CLI version' ($supabaseVersion -eq $script:TransferConfig.Expected.SupabaseVersion) $true $supabaseVersion
    $firebaseVersionOutput = Get-NativeCommandOutput -Name 'firebase' -Arguments @('--version')
    $firebaseVersion = @($firebaseVersionOutput -split "`r?`n" | Where-Object { $_ -match '^\d+\.\d+\.\d+$' }) | Select-Object -Last 1
    Add-CheckResult $results 'Firebase CLI version' ($firebaseVersion -eq $script:TransferConfig.Expected.FirebaseCliVersion) $true $firebaseVersion
    $psqlVersion = Get-NativeCommandOutput -Name 'psql' -Arguments @('--version')
    Add-CheckResult $results 'PostgreSQL client 17' ($psqlVersion -match ' 17\.') $true $(if ($psqlVersion) { $psqlVersion } else { 'not found' })

    $dockerInfo = Get-NativeCommandOutput -Name 'docker' -Arguments @('info', '--format', '{{.ServerVersion}}')
    Add-CheckResult $results 'Docker Desktop engine' ($dockerInfo -match '^\d+\.\d+') $false $(if ($dockerInfo -match '^\d+\.\d+') { $dockerInfo } else { 'not running; launch Docker Desktop when hosted tests are needed' })
    $wslStatus = (Get-NativeCommandOutput -Name 'wsl.exe' -Arguments @('--status')) -replace ([regex]::Escape([string][char]0)), ''
    Add-CheckResult $results 'WSL2' ($wslStatus -match 'Default Version:\s*2') $false $(if ($wslStatus) { ($wslStatus -replace "`r?`n", ' ').Trim() } else { 'WSL status unavailable' })

    $codexLogin = Get-NativeCommandOutput -Name 'codex' -Arguments @('login', 'status')
    Add-CheckResult $results 'Codex authentication' ($codexLogin -match 'Logged in') $false 'sign in through Codex on this device'

    $excelPassed = $false
    $excelDetail = 'Excel COM activation failed'
    try {
        $excel = New-Object -ComObject Excel.Application
        $excelPassed = $true
        $excelDetail = "Excel $($excel.Version) COM automation available"
        $excel.Quit()
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($excel)
    }
    catch { $excelDetail = $_.Exception.Message }
    Add-CheckResult $results 'Excel COM' $excelPassed $true $excelDetail

    foreach ($path in @('AGENTS.md', 'TODO.md', 'LOCAL_DEVICE_SETUP_HANDOVER.md', $script:TransferConfig.Expected.OwnerEnrollmentScript, 'docs/flightlogx-preview-android-install.md')) {
        Add-CheckResult $results $path (Test-Path -LiteralPath (Join-Path $script:RepoRoot $path) -PathType Leaf) $true 'required repository context'
    }
    $hostedConfig = Join-Path $script:RepoRoot 'mobile\src\ElectronicLogbook.Mobile\wwwroot\hosted-sync.local.json'
    Add-CheckResult $results 'Hosted sync local config' (Test-Path -LiteralPath $hostedConfig -PathType Leaf) $false 'required only for hosted Preview work'
    $electronicLogbookLocalRoot = Join-Path $LocalAppDataRoot 'ElectronicLogbook'
    Add-CheckResult $results 'Supabase management token' (Test-Path -LiteralPath (Join-Path $electronicLogbookLocalRoot 'Supabase\access-token.txt') -PathType Leaf) $true 'private transfer asset; value is never printed'
    $hostedMetadataRoot = Join-Path $electronicLogbookLocalRoot 'Supabase'
    $canonicalHostedMetadata = Join-Path $hostedMetadataRoot $script:TransferConfig.Expected.HostedProjectMetadataFile
    $legacyHostedMetadata = Join-Path $hostedMetadataRoot $script:TransferConfig.Expected.LegacyHostedProjectMetadataFile
    $hostedMetadataFound = (Test-Path -LiteralPath $canonicalHostedMetadata -PathType Leaf) -or
        (Test-Path -LiteralPath $legacyHostedMetadata -PathType Leaf)
    Add-CheckResult $results 'Hosted project metadata' $hostedMetadataFound $true 'canonical Preview metadata or its narrow legacy filename alias; values are never printed'
    $firebaseConfigPath = Join-Path $script:RepoRoot 'mobile\android\app\google-services.json'
    $firebaseConfigPassed = $false
    if (Test-Path -LiteralPath $firebaseConfigPath -PathType Leaf) {
        try {
            $firebaseConfig = Get-Content -LiteralPath $firebaseConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
            $firebaseClient = @($firebaseConfig.client | Where-Object {
                $_.client_info.android_client_info.package_name -eq $script:TransferConfig.Expected.FirebaseAndroidPackageName
            }) | Select-Object -First 1
            $firebaseConfigPassed = $firebaseConfig.project_info.project_id -eq $script:TransferConfig.Expected.FirebaseProjectId `
                -and $null -ne $firebaseClient `
                -and -not [string]::IsNullOrWhiteSpace($firebaseClient.client_info.mobilesdk_app_id) `
                -and @($firebaseClient.api_key).Count -gt 0
        }
        catch {
            $firebaseConfigPassed = $false
        }
    }
    Add-CheckResult $results 'Firebase Android Preview config' $firebaseConfigPassed $true 'private transfer asset; project, package, app ID, and key presence checked without printing values'
    $resendRoot = Join-Path $electronicLogbookLocalRoot 'Resend'
    foreach ($fileName in $script:TransferConfig.Expected.ResendApiKeyFiles) {
        $keyPath = Join-Path $resendRoot $fileName
        $keyConfigured = (Test-Path -LiteralPath $keyPath -PathType Leaf) `
            -and -not [string]::IsNullOrWhiteSpace((Get-Content -LiteralPath $keyPath -Raw -Encoding UTF8).Trim())
        Add-CheckResult $results "Resend sending key: $fileName" $keyConfigured $true 'private transfer asset; value is never printed'
    }
    $recoveryRoot = Join-Path $electronicLogbookLocalRoot 'Supabase\recovery-envelope'
    foreach ($fileName in $script:TransferConfig.Expected.RecoveryEnvelopeSecretFiles) {
        $secretCheck = Test-RecoveryEnvelopeSecretFile -Path (Join-Path $recoveryRoot $fileName)
        Add-CheckResult $results "Recovery envelope secrets: $fileName" $secretCheck.Passed $true $secretCheck.Detail
    }
    Add-CheckResult $results 'Google Auth local state' (Test-Path -LiteralPath (Join-Path $electronicLogbookLocalRoot 'Google Auth\webclientid.txt') -PathType Leaf) $false 'required for Android hosted Google sign-in work'

    $signingRoot = Join-Path $LocalAppDataRoot 'ElectronicLogbook\AndroidSigning'
    $signingMetadata = Join-Path $signingRoot 'electronic-logbook-development.json'
    $keystore = Join-Path $signingRoot 'electronic-logbook-development.keystore'
    $signingPassed = (Test-Path -LiteralPath $signingMetadata -PathType Leaf) -and (Test-Path -LiteralPath $keystore -PathType Leaf)
    Add-CheckResult $results 'Durable Android signing identity' $signingPassed $true $signingRoot
    if ($signingPassed) {
        $metadata = Get-Content -LiteralPath $signingMetadata -Raw -Encoding UTF8 | ConvertFrom-Json
        Add-CheckResult $results 'Signing metadata path' ($metadata.keystorePath -eq $keystore) $true $metadata.keystorePath
        $keytool = Get-Command 'keytool.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($keytool) {
            $output = & $keytool.Source -list -v -keystore $keystore -storepass android -alias androiddebugkey 2>&1 | Out-String
            $fingerprint = if ($output -match 'SHA256:\s*([0-9A-F:]+)') { $Matches[1].Replace(':', '').ToLowerInvariant() } else { '' }
            Add-CheckResult $results 'Signing certificate fingerprint' ($fingerprint -eq $metadata.certificateSha256) $true $fingerprint
        }
    }

    $previewKeystore = Join-Path $signingRoot $script:TransferConfig.Expected.PreviewSigningKeystoreFile
    $previewCredentialsPath = Join-Path $signingRoot $script:TransferConfig.Expected.PreviewSigningCredentialsFile
    $previewMetadataPath = Join-Path $signingRoot $script:TransferConfig.Expected.PreviewSigningMetadataFile
    $previewSigningPaths = @($previewKeystore, $previewCredentialsPath, $previewMetadataPath)
    $previewSigningPassed = @($previewSigningPaths | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }).Count -eq $previewSigningPaths.Count
    Add-CheckResult $results 'Permanent Preview Android signing identity' $previewSigningPassed $true 'protected private transfer assets; credential values are never printed'
    if ($previewSigningPassed) {
        try {
            $previewCredentials = Get-Content -LiteralPath $previewCredentialsPath -Raw -Encoding UTF8 | ConvertFrom-Json
            $previewMetadata = Get-Content -LiteralPath $previewMetadataPath -Raw -Encoding UTF8 | ConvertFrom-Json
            $previewStructurePassed = $previewCredentials.schemaVersion -eq 1 `
                -and -not [string]::IsNullOrWhiteSpace($previewCredentials.storePassword) `
                -and -not [string]::IsNullOrWhiteSpace($previewCredentials.keyPassword) `
                -and -not [string]::IsNullOrWhiteSpace($previewCredentials.keyAlias) `
                -and $previewMetadata.packageName -eq $script:TransferConfig.Expected.FirebaseAndroidPackageName `
                -and $previewMetadata.keystorePath -eq $previewKeystore `
                -and $previewMetadata.keyAlias -eq $previewCredentials.keyAlias `
                -and -not [string]::IsNullOrWhiteSpace($previewMetadata.certificateSha256)
            Add-CheckResult $results 'Permanent Preview signing metadata' $previewStructurePassed $true 'package, alias, path, and secret presence checked without printing credential values'

            $keytool = Get-Command 'keytool.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($keytool -and $previewStructurePassed) {
                $previousStorePassword = $env:ELECTRONIC_LOGBOOK_PREVIEW_VERIFY_STORE_PASSWORD
                try {
                    $env:ELECTRONIC_LOGBOOK_PREVIEW_VERIFY_STORE_PASSWORD = $previewCredentials.storePassword
                    $output = & $keytool.Source -list -v -keystore $previewKeystore -storetype PKCS12 `
                        '-storepass:env' ELECTRONIC_LOGBOOK_PREVIEW_VERIFY_STORE_PASSWORD `
                        -alias $previewCredentials.keyAlias 2>&1 | Out-String
                    $fingerprint = if ($LASTEXITCODE -eq 0 -and $output -match 'SHA256:\s*([0-9A-F:]+)') {
                        $Matches[1].Replace(':', '').ToLowerInvariant()
                    }
                    else { '' }
                    Add-CheckResult $results 'Permanent Preview signing certificate fingerprint' ($fingerprint -eq $previewMetadata.certificateSha256) $true $(if ($fingerprint) { $fingerprint } else { 'keystore validation failed' })
                }
                finally {
                    $env:ELECTRONIC_LOGBOOK_PREVIEW_VERIFY_STORE_PASSWORD = $previousStorePassword
                }
            }
        }
        catch {
            Add-CheckResult $results 'Permanent Preview signing metadata' $false $true 'protected signing files could not be parsed or validated'
        }
    }

    $extensions = @((Get-NativeCommandOutput -Name 'code' -Arguments @('--list-extensions')) -split "`r?`n" | Where-Object { $_ })
    foreach ($extension in $script:TransferConfig.VsCodeExtensions) {
        Add-CheckResult $results "VS Code: $($extension.Id)" ($extensions -contains $extension.Id) ([bool]$extension.Required) 'extension installation'
    }

    $ghAuthenticated = $false
    if (Get-Command gh -ErrorAction SilentlyContinue) {
        $ghStatus = Get-NativeCommandOutput -Name 'gh' -Arguments @('auth', 'status')
        $ghAuthenticated = $ghStatus -match 'Logged in to'
    }
    Add-CheckResult $results 'GitHub authentication' $ghAuthenticated $false 'run gh auth login on this device'

    $results | Format-Table -AutoSize -Wrap
    $failures = @($results | Where-Object Status -eq 'FAIL')
    if ($failures.Count -gt 0) { throw "$($failures.Count) required environment check(s) failed." }
    Write-Host 'Required environment checks passed.' -ForegroundColor Green
}

switch ($Action) {
    'Export' { Invoke-ExportAction }
    'Import' { Invoke-ImportAction }
    'Install' { Invoke-InstallAction }
    'Verify' { Invoke-VerifyAction }
}
