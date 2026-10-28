[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$entrypoint = Join-Path $PSScriptRoot 'Invoke-LocalDevelopmentTransfer.ps1'
$manifestPath = Join-Path $PSScriptRoot 'local-development-transfer.psd1'
$config = Import-PowerShellDataFile -LiteralPath $manifestPath
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("ElectronicLogbookTransferTests-" + [Guid]::NewGuid().ToString('N'))
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
    param([string[]]$Arguments)

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = (Get-Process -Id $PID).Path
    $quoted = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $entrypoint) + $Arguments
    $startInfo.Arguments = (($quoted | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }) -join ' ')
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
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
        & $script:sevenZip a -t7z $Archive '.\*' -mx=1 -bb0 -bd | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not create the synthetic archive.' }
    }
    finally { Pop-Location }
}

try {
    New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null
    $script:sevenZip = Resolve-TestSevenZip

    Assert-True ($config.SchemaVersion -eq 1) 'manifest schema version must be 1'
    Assert-True ($config.Environment.AndroidJavaSocketTempRelativeToWindows -eq 'Temp') 'debug Gradle socket directory must resolve under Windows, not the profile temp directory'
    $debugInstallerSource = Get-Content (Join-Path $repoRoot 'mobile/scripts/Install-AndroidDebugBuild.ps1') -Raw
    Assert-True ($debugInstallerSource.Contains('$transferConfig.Environment.AndroidJavaSocketTempRelativeToWindows')) 'debug installer must use the transfer manifest socket setting'
    Assert-True ($debugInstallerSource.Contains('$env:JAVA_TOOL_OPTIONS = $previousJavaToolOptions')) 'debug installer must restore the calling process Java options'
    Assert-True (@($config.RepoAssets | Where-Object { $_.Path -eq 'AGENTS.md' -and $_.Required }).Count -eq 1) 'AGENTS.md must be required'
    Assert-True (@($config.RepoAssets | Where-Object { $_.Path -eq 'TODO.md' -and $_.Required }).Count -eq 1) 'TODO.md must be required'
    Assert-True (@($config.RepoAssets | Where-Object { $_.Path -eq 'docs/local/*.md' -and -not $_.Required -and $_.Classification -eq 'private-reference' }).Count -eq 1) 'local Markdown research must be optional private transfer material'
    Assert-True (@($config.ForbiddenBundlePatterns | Where-Object { $_ -like '*auth.json*' }).Count -eq 1) 'Codex auth must be forbidden'
    Assert-True (@($config.ForbiddenBundlePatterns | Where-Object { $_ -like '*sessions*' }).Count -eq 1) 'Codex sessions must be forbidden'
    $transferAst = [Management.Automation.Language.Parser]::ParseFile($entrypoint, [ref]$null, [ref]$null)
    Assert-True (($config.NpmProjects -join ',') -eq 'mobile,supabase/functions/aircraft-catalog') 'restore must include the mobile and aircraft lockfile projects'
    foreach ($project in $config.NpmProjects) {
        Assert-True (Test-Path -LiteralPath (Join-Path $repoRoot "$project/package-lock.json")) "$project must have a reproducible npm lockfile"
    }
    $dependencyRestore = $transferAst.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-DependencyRestore' }, $true)
    $restoreFixture = Join-Path $temporaryRoot 'dependency-restore'
    foreach ($project in $config.NpmProjects) { New-Item -ItemType Directory -Force -Path (Join-Path $restoreFixture $project) | Out-Null }
    $restoreResult = & (New-Module -ArgumentList $config, $restoreFixture, $dependencyRestore.Extent.Text -ScriptBlock {
        param($Config, $Root, $Source)
        $script:TransferConfig = $Config; $script:RepoRoot = $Root; $SkipDependencyRestore = $false
        $script:calls = [Collections.Generic.List[string]]::new()
        function Write-Step { param($Message) }
        function dotnet { $global:LASTEXITCODE = 0 }
        function npm.cmd { $script:calls.Add((Get-Location).Path); $global:LASTEXITCODE = 0 }
        Invoke-Expression $Source
    }) {
        Invoke-DependencyRestore
        $count = $script:calls.Count
        $SkipDependencyRestore = $true
        Invoke-DependencyRestore
        [pscustomobject]@{ Calls = @($script:calls); CountBeforeSkip = $count }
    }
    Assert-True ($restoreResult.Calls.Count -eq 2 -and $restoreResult.CountBeforeSkip -eq 2) 'dependency restore must run both npm projects once and honor skip'
    Assert-True ($restoreResult.Calls[1] -eq (Join-Path $restoreFixture 'supabase/functions/aircraft-catalog')) 'aircraft dependency restore must execute inside its own project'
    $pathPolicyFunctions = $transferAst.FindAll({
        param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -in @('ConvertTo-PortablePath', 'Test-TransferPathExcluded', 'Test-ForbiddenBundlePath')
    }, $false)
    & {
        $script:TransferConfig = $config
        foreach ($policyFunction in $pathPolicyFunctions) { Invoke-Expression $policyFunction.Extent.Text }
        foreach ($directory in @('.codex-attachments', '.codex-remote-attachments')) {
            $attachmentPath = "payload/repo/$directory/thread/message/probe.png"
            Assert-True (Test-TransferPathExcluded $attachmentPath) "$directory must be omitted from export"
            Assert-True (Test-ForbiddenBundlePath $attachmentPath) "$directory must be rejected on import"
            git -C $repoRoot check-ignore -q -- "$directory/thread/message/probe.png"
            Assert-True ($LASTEXITCODE -eq 0) "$directory must be ignored by Git"
        }
    }
    Assert-True (@($config.WingetPackages | Where-Object { $_.Id -eq 'PostgreSQL.PostgreSQL.17' -and $_.Required }).Count -eq 1) 'PostgreSQL 17 must be required for hosted cleanup and SQL evidence'
    Assert-True (@($config.NpmGlobalPackages | Where-Object { $_.Package -eq 'firebase-tools@15.28.2' -and $_.Command -eq 'firebase' -and $_.Required }).Count -eq 1) 'Firebase CLI must be pinned and required for Preview distribution'
    Assert-True ($config.Expected.FirebaseCliVersion -eq '15.28.2') 'Firebase CLI verifier version must match the pinned package'
    Assert-True (@($config.RepoAssets | Where-Object { $_.Path -eq 'mobile/android/app/google-services.json' -and $_.Required -and $_.Classification -eq 'private-config' }).Count -eq 1) 'private Firebase Android config must be transferred for Preview development'
    Assert-True ($config.Expected.FirebaseProjectId -eq 'flightlogx-private-pilot') 'Firebase project verifier must target the approved Preview project with its retained legacy external ID'
    Assert-True ($config.Expected.FirebaseAndroidPackageName -eq 'com.alphadelta.electroniclogbook') 'Firebase package verifier must target the permanent Android ID'
    Assert-True ($config.Expected.FirebaseAndroidBuildVariant -eq 'preview') 'Firebase Google Services configuration must be restricted to the permanent Preview build variant'
    Assert-True ($config.Expected.HostedProjectMetadataFile -eq 'hosted-preview-projects.local.json') 'canonical hosted project metadata filename must use Preview wording'
    Assert-True ($config.Expected.WranglerVersion -eq '4.147.0') 'Wrangler scheduler maintenance version must be pinned'
    Assert-True (@($config.NpmGlobalPackages | Where-Object { $_.Package -eq ('wrangler@' + $config.Expected.WranglerVersion) -and $_.Command -eq 'wrangler' -and -not $_.Required }).Count -eq 1) 'Wrangler must be an optional pinned CLI'
    Assert-True (@($config.ExternalLocalStateExclusions | Where-Object { $_.Path -eq '%APPDATA%/xdg.config/.wrangler' -and $_.Lifecycle -eq 'fresh-authentication' }).Count -eq 1) 'Cloudflare OAuth state must be freshly authenticated and excluded from transfer'
    Assert-True ($config.Expected.LegacyHostedProjectMetadataFile -eq 'hosted-pilot-projects.local.json') 'legacy hosted project metadata filename must remain a narrow compatibility alias'
    Assert-True ($config.Expected.PreviewSigningKeystoreFile -eq 'flightlogx-pilot.keystore') 'permanent Preview keystore must retain its legacy transfer filename'
    Assert-True ($config.Expected.PreviewSigningCredentialsFile -eq 'flightlogx-pilot-credentials.json') 'permanent Preview credentials must retain their legacy transfer filename'
    Assert-True ($config.Expected.PreviewSigningMetadataFile -eq 'flightlogx-pilot-signing.json') 'permanent Preview signing metadata must retain its legacy transfer filename'
    Assert-True ($config.Expected.OwnerEnrollmentScript -eq 'tools/Add-FlightLogXParticipant.ps1') 'owner enrolment command must have a stable tracked entrypoint'
    Assert-True ($config.Expected.AirportCatalogPublisherScript -eq 'tools/Publish-AirportCatalog.ps1') 'airport catalogue publisher must have a tracked deployment entrypoint'
    Assert-True ($config.Expected.AirportCatalogPublisherSecretName -eq 'AIRPORT_CATALOG_PUBLISH_TOKEN') 'airport publication uses a remote publisher-only token'
    Assert-True (Test-Path (Join-Path $repoRoot $config.Expected.AirportCatalogSchedulerSource)) 'shared Cloudflare scheduler source must exist after checkout'
    Assert-True ($config.Expected.AndroidRecoverySystemImage -eq 'system-images;android-35;google_apis_playstore;x86_64') 'recovery AVD must use the Google Play API 35 system image'
    Assert-True ($config.Expected.AndroidRecoveryAvdName -eq 'ElectronicLogbook_Pixel_Play_API35') 'recovery AVD must have a stable guarded name'
    Assert-True ($config.Expected.AndroidRecoverySnapshotName -eq 'flightlogx_google_authenticated_clean_v1') 'authenticated clean snapshot must have a stable guarded name'
    Assert-True ($config.Expected.AndroidRecoveryResetScript -eq 'tools/Reset-FlightLogXAndroidRecoveryEmulator.ps1') 'recovery reset command must have a stable tracked entrypoint'
    Assert-True ($config.Expected.ParticipantHandoffDirectory -eq 'ElectronicLogbook\ParticipantHandoffs') 'private participant handoffs must stay under transferred local state'
    Assert-True (@($config.Expected.ResendApiKeyFiles).Count -eq 2) 'separate development and Preview Resend sending keys must be verified'
    Assert-True (@($config.Expected.RecoveryEnvelopeSecretFiles).Count -eq 2) 'development and Preview recovery secret files must be verified'
    Assert-True (@($config.LocalAppDataAssets | Where-Object { $_.Path -eq 'ElectronicLogbook' }).Count -eq 0) 'the whole ElectronicLogbook tree must never be a transfer asset'
    Assert-True (@($config.LocalAppDataAssets | Where-Object { $_.Lifecycle -ne 'local-transfer' }).Count -eq 0) 'every transferred local asset must use the local-transfer lifecycle'
    Assert-True (@($config.LocalAppDataAssets | Where-Object { $_.Path -eq 'ElectronicLogbook/Google Auth/webclientid.txt' -and -not $_.Required }).Count -eq 1) 'the public Google Web client ID must be transferred when present'
    Assert-True (@($config.LocalAppDataAssets | Where-Object { $_.Path -eq 'ElectronicLogbook/Google Auth/androidclientid.txt' -and -not $_.Required }).Count -eq 1) 'the public Google Android client ID must be transferred when present'
    Assert-True (@($config.LocalAppDataAssets | Where-Object { $_.Path -eq 'ElectronicLogbook/Google Auth/previewandroidclientid.txt' -and -not $_.Required }).Count -eq 1) 'the permanent Preview Google Android client ID must be transferred when present'
    Assert-True (@($config.LocalAppDataAssets | Where-Object { $_.Path -like 'ElectronicLogbook/Google Auth/*' }).Count -eq 3) 'only the public Web, development Android, and Preview Android OAuth identifiers may enter the transfer allowlist from Google Auth'
    Assert-True (@($config.LocalAppDataAssets | Where-Object { $_.Classification -eq 'secret-signing-identity' }).Count -eq 2) 'development and permanent Preview keystores must be explicitly allowlisted'
    Assert-True (@($config.LocalAppDataAssets | Where-Object { $_.Classification -eq 'secret-api-credential' }).Count -eq 3) 'Supabase and both Resend API credentials must be explicitly allowlisted'
    Assert-True (@($config.LocalAppDataAssets | Where-Object { $_.ContainsKey('RequirementGroup') -and $_['RequirementGroup'] -eq 'hosted-project-metadata' }).Count -eq 2) 'canonical and legacy hosted metadata must form one required alternative group'
    Assert-True (@($config.LocalAppDataExclusions | Where-Object { $_.Path -eq 'ElectronicLogbook/AndroidDeviceBridge' -and $_.Lifecycle -eq 'deliberate-exclusion' }).Count -eq 1) 'device bridge backups must be deliberately excluded'
    Assert-True (@($config.ManualCheckpoints | Where-Object { $_ -match 'Wireless debugging' -and $_ -match 'USB only when explicitly chosen' -and $_ -match 'Verify the hardware serial' }).Count -eq 1) 'Android setup must default to verified wireless debugging with USB as an explicit choice'
    Assert-True (@($config.LocalAppDataExclusions | Where-Object { $_.Path -eq 'ElectronicLogbook/AnalysisTools' -and $_.Lifecycle -eq 'regenerated-dependency' }).Count -eq 1) 'analysis tools must be classified as regenerated dependencies'
    Assert-True (@($config.LocalAppDataExclusions | Where-Object { $_.Path -eq 'ElectronicLogbook/Temp' -and $_.Lifecycle -eq 'regenerated-output' }).Count -eq 1) 'temporary diagnostic outputs must be classified as regenerated output'
    Assert-True (@($config.LocalAppDataExclusions | Where-Object { $_.Path -eq 'ElectronicLogbook/Recovery Codes' -and $_.Lifecycle -eq 'deliberate-exclusion' }).Count -eq 1) 'user recovery artifacts must be deliberately excluded'
    Assert-True (@($config.LocalAppDataExclusions | Where-Object { $_.Path -eq 'ElectronicLogbook/Google Auth/client_secret_*.json' -and $_.Lifecycle -eq 'deliberate-exclusion' }).Count -eq 1) 'unused Google client-secret downloads must be deliberately excluded'
    Assert-True (@($config.ExternalLocalStateExclusions | Where-Object { $_.Path -eq '%USERPROFILE%/.android/avd' -and $_.Lifecycle -eq 'deliberate-exclusion' }).Count -eq 1) 'authenticated Android virtual devices must be deliberately excluded from transfer'

    $recoveryScriptPath = Join-Path $PSScriptRoot 'Reset-FlightLogXAndroidRecoveryEmulator.ps1'
    Assert-True (Test-Path -LiteralPath $recoveryScriptPath -PathType Leaf) 'tracked recovery reset command must exist'
    $recoveryScriptText = Get-Content -LiteralPath $recoveryScriptPath -Raw -Encoding UTF8
    Assert-True ($recoveryScriptText -match "ValidateSet\('CaptureBaseline', 'ResetAppAndInstall', 'RestoreSnapshotAndInstall', 'Verify'\)") 'recovery reset command must expose only the guarded actions'
    Assert-True ($recoveryScriptText -match '\$DeviceSerial -notmatch.*emulator-') 'recovery reset command must reject physical-device serials'
    Assert-True ($recoveryScriptText -match 'ro\.kernel\.qemu') 'recovery reset command must verify Android reports an emulator'
    Assert-True ($recoveryScriptText -match '\$actualName -ne \$ExpectedAvdName') 'recovery reset command must guard the exact AVD name'
    Assert-True ($recoveryScriptText -match '-not \$ApproveSnapshotRestore') 'recovery reset command must require explicit approval before snapshot rollback'
    Assert-True ($recoveryScriptText -match '-not \$ApproveFlightLogXRemoval') 'routine reset must require explicit approval before removing FlightLogX local data'
    Assert-True ($recoveryScriptText -match 'No account identifier or authentication token was printed') 'recovery verification must not disclose Google account or token data'

    $entrypointText = Get-Content -LiteralPath $entrypoint -Raw -Encoding UTF8
    Assert-True ($entrypointText -match 'Expected\.AirportCatalogSchedulerSource') 'transfer verifier must check the shared Cloudflare scheduler source'
    Assert-True ($entrypointText -match "@\('a'.*'-t7z'.*'-mx=9'.*'-bb0'") 'real export must create a 7-Zip archive'
    Assert-True ($entrypointText -notmatch "'-mhe=on'|Read-ArchivePassword|Archive password") 'workflow must not encrypt or request an archive password'
    Assert-True ($entrypointText -match 'Test-RecoveryEnvelopeSecretFile') 'verifier must validate recovery secret structure and key pairing'
    Assert-True ($entrypointText -match "Cloudflare Wrangler version.*Expected\.WranglerVersion") 'verifier must check the optional pinned Wrangler version'
    Assert-True ($entrypointText -match 'Resend sending key') 'verifier must require the separate Resend sending keys without printing them'
    Assert-True ($entrypointText.Contains("Add-CheckResult `$results 'PostgreSQL client 17' (`$psqlVersion -match ' 17\.') `$true")) 'PostgreSQL 17 version must be a required verification check'
    Assert-True (($config.Expected.PostgreSqlBackupTools -join ',') -eq 'pg_dump,pg_dumpall,pg_restore,initdb,pg_ctl') 'backup clients and isolated lifecycle fixture tools must share the PostgreSQL installation'
    Assert-True ($config.Expected.HostedBackupScript -eq 'tools/hosted-backup/cli.mjs' -and (Test-Path (Join-Path $repoRoot $config.Expected.HostedBackupScript))) 'backup entrypoint must resolve after checkout'
    Assert-True ($entrypointText -match 'Expected\.PostgreSqlBackupTools' -and $entrypointText -match 'PostgreSQL backup tool') 'transfer verification must check all backup client versions'
    Assert-True ($config.Expected.HostedBackupReplayProject -eq 'tools/hosted-backup/ReplayCheck/ReplayCheck.csproj' -and (Test-Path (Join-Path $repoRoot $config.Expected.HostedBackupReplayProject))) 'application replay project must resolve after checkout'
    Assert-True ($entrypointText -match 'HostedBackupReplayProject') 'transfer verification must check the application replay project'
    foreach ($setting in @('HostedBackupCompatibilityContract','HostedBackupCompatibilityTestScript')) {
        Assert-True ((Test-Path (Join-Path $repoRoot $config.Expected[$setting])) -and $entrypointText.Contains('Expected.' + $setting)) 'backup compatibility sources must resolve after checkout'
    }
    foreach ($setting in @('HostedBackupLogicalExportScript','HostedBackupLogicalClientScript','HostedBackupLogicalRestoreScript')) {
        Assert-True ((Test-Path (Join-Path $repoRoot $config.Expected[$setting])) -and $entrypointText.Contains('Expected.' + $setting)) 'scoped logical export/client/restorer must resolve after checkout'
    }
    foreach ($setting in @('HostedBackupDailyScript','HostedBackupDailyTestScript','HostedBackupDailyWorkflow')) {
        Assert-True ((Test-Path (Join-Path $repoRoot $config.Expected[$setting])) -and $entrypointText.Contains('Expected.' + $setting)) 'daily backup runner, regression and disabled workflow must resolve after checkout'
    }
    Assert-True (@($config.ExternalLocalStateExclusions | Where-Object { $_.Path -eq 'GitHub/alphadelta332/flightlogx-preview-backups/ActionsSecrets' -and $_.Lifecycle -eq 'fresh-authentication' }).Count -eq 1) 'scoped private job secrets must be reprovisioned, never transferred'
    Assert-True ($config.Expected.HostedBackupDailyPreparation.Count -eq 6 -and $entrypointText.Contains('Expected.HostedBackupDailyPreparation')) 'private source/job preparation, primary composition and isolated regressions must resolve after checkout'
    foreach ($preparationFile in $config.Expected.HostedBackupDailyPreparation) {
        Assert-True (Test-Path -LiteralPath (Join-Path $repoRoot $preparationFile) -PathType Leaf) 'private source preparation source must exist'
    }
    Assert-True (@($config.ExternalLocalStateExclusions | Where-Object { $_.Path -eq '%TEMP%/elb-daily-repository-*' -and $_.Lifecycle -eq 'regenerated-output' }).Count -eq 1) 'isolated private source stages must be regenerated, never transferred as configuration'
    $compatibility = Get-Content (Join-Path $repoRoot $config.Expected.HostedBackupCompatibilityContract) -Raw | ConvertFrom-Json
    Assert-True (@($compatibility.targetImages.PSObject.Properties).Count -eq 2 -and @($compatibility.targetImages.PSObject.Properties.Value | Where-Object { $_ -notmatch '^public\.ecr\.aws/supabase/(gotrue|storage-api):v[0-9.]+@sha256:[a-f0-9]{64}$' }).Count -eq 0) 'disposable service image dependencies must pin reviewed immutable digests'
    Assert-True ($entrypointText -match 'compatibility.targetImages' -and $entrypointText -match 'Optional backup compatibility image') 'verification must report optional regenerated service images'
    Assert-True (@($config.LocalAppDataAssets | Where-Object { $_.Path -like 'ElectronicLogbook/HostedBackup/*' }).Count -eq 7) 'HostedBackup transfer must be limited to recipient, receipt, storage/source credentials, source CA, expiring owner review and private alert recipient'
    Assert-True (@($config.LocalAppDataAssets | Where-Object { $_.Path -eq 'ElectronicLogbook/HostedBackup/daily-review.local.json' -and $_.Lifecycle -eq 'local-transfer' -and -not $_.Required }).Count -eq 1) 'owner review transfers without silently renewing its expiry'
    Assert-True (@($config.LocalAppDataAssets | Where-Object { $_.Path -eq 'ElectronicLogbook/HostedBackup/daily-review.local.json' -and $_.Classification -eq 'private-backup-owner-review-and-source-fingerprint' }).Count -eq 1 -and $entrypointText.Contains('sourceDefinitionSha256')) 'source definition fingerprint and actual expiry must retain their declared custody and guarded lifecycle'
    Assert-True ($config.Expected.HostedBackupDailyControls.Count -eq 8 -and $entrypointText.Contains('Expected.HostedBackupDailyControls')) 'cost policy, review, durable reservations and request/cache controls must be verified after checkout'
    Assert-True ($config.Expected.HostedBackupDailyControls -contains 'tools/hosted-backup/daily-request-controls.mjs') 'durable component ceilings and cached provider evidence must survive checkout verification'
    Assert-True ($config.Expected.HostedBackupDailyPreparation -contains 'tools/hosted-backup/daily-primary.mjs' -and $config.Expected.HostedBackupDailyPreparation -contains 'tools/hosted-backup/daily-primary.test.mjs') 'disabled primary timer composition and regression must survive checkout verification'
    Assert-True ($config.Expected.HostedBackupDailyPreparation -contains 'tools/hosted-backup/prepare-daily-job.mjs' -and $config.Expected.HostedBackupDailyPreparation -contains 'tools/hosted-backup/prepare-daily-job.test.mjs') 'owner-side encrypted deployment/job preparation and regression must survive checkout verification'
    $primaryControlsConfig = Get-Content (Join-Path $repoRoot 'tools/hosted-backup/daily-controls.wrangler.json') -Raw | ConvertFrom-Json
    Assert-True ($primaryControlsConfig.vars.BACKUP_ENABLED -eq 'false' -and $primaryControlsConfig.vars.MONITOR_ENABLED -eq 'false' -and $entrypointText.Contains('Cached backup permit activation')) 'public cache-only permit and internal monitoring must transfer disabled with separate activation checks'
    Assert-True ($config.Expected.HostedBackupDailyControls -contains 'tools/hosted-backup/daily-cost-policy.mjs' -and $config.Expected.HostedBackupDailyControls -contains 'tools/hosted-backup/daily-cost-policy.test.mjs') 'shared reviewed-baseline policy and its boundary checks must survive checkout verification'
    Assert-True ($config.Expected.HostedBackupMonitor.Count -eq 12 -and $entrypointText.Contains('Expected.HostedBackupMonitor')) 'monitor policy, adapters and separate disabled observer sources must resolve after checkout'
    Assert-True ($config.Expected.HostedBackupMonitor -contains 'tools/hosted-backup/daily-alert-delivery.mjs' -and $config.Expected.HostedBackupMonitor -contains 'tools/hosted-backup/daily-alert-delivery.test.mjs') 'restricted Resend delivery adapter and regression must resolve after checkout'
    Assert-True (@($config.LocalAppDataAssets | Where-Object { $_.Path -eq 'ElectronicLogbook/HostedBackup/alert-delivery.local.json' -and $_.Lifecycle -eq 'local-transfer' -and -not $_.Required -and $_.Classification -eq 'private-backup-alert-recipient' }).Count -eq 1) 'optional private alert recipient must transfer only as classified private state'
    foreach ($monitorFile in $config.Expected.HostedBackupMonitor) {
        Assert-True (Test-Path -LiteralPath (Join-Path $repoRoot $monitorFile) -PathType Leaf) "monitor source must exist: $monitorFile"
    }
    foreach($controlFile in $config.Expected.HostedBackupDailyControls) {
        Assert-True (Test-Path (Join-Path $repoRoot $controlFile)) 'durable reservation source/configuration/tests must exist'
    }
    Assert-True (@($config.ExternalLocalStateExclusions | Where-Object { $_.Path -eq 'Cloudflare/flightlogx-backup-controls' -and $_.Lifecycle -eq 'fresh-authentication' }).Count -eq 1) 'remote reservation namespace must survive transfer and secrets must be freshly provisioned'
    Assert-True (@($config.ExternalLocalStateExclusions | Where-Object { $_.Path -eq '%TEMP%/elb-daily-controls-*' -and $_.Lifecycle -eq 'regenerated-output' }).Count -eq 1) 'synthetic local SQLite evidence must be excluded from operational transfer'
    Assert-True (@($config.ExternalLocalStateExclusions | Where-Object { $_.Path -eq 'Cloudflare/flightlogx-backup-watchdog' -and $_.Lifecycle -eq 'fresh-authentication' }).Count -eq 1) 'separate watchdog secrets must be recreated while its remote namespace and heartbeat/alert counters survive transfer'
    Assert-True (@($config.ExternalLocalStateExclusions | Where-Object { $_.Path -eq '%TEMP%/elb-daily-watchdog-*' -and $_.Lifecycle -eq 'regenerated-output' }).Count -eq 1) 'isolated observer SQLite must remain regenerated evidence outside transfer'
    Assert-True ($entrypointText.Contains('MONITOR_HEARTBEAT') -and $entrypointText.Contains('WATCHDOG_ENABLED=false')) 'verification must report private observer binding and disabled activation gate'
    Assert-True ($config.Expected.HostedBackupProviderCollector.Count -eq 4 -and $config.Expected.HostedBackupProviderCollector -contains 'tools/hosted-backup/daily-request-controls.mjs' -and $config.Expected.HostedBackupProviderCollector -contains 'docs/hosted-backup-provider-collector.md' -and $entrypointText.Contains('Expected.HostedBackupProviderCollector')) 'collector implementation, expiring review cache, tests and reviewed-input contract must resolve after checkout'
    Assert-True ($entrypointText.Contains('Exact retired storage review') -and @($config.ExternalLocalStateExclusions | Where-Object { $_.Path -eq 'Cloudflare/flightlogx-backup-controls' -and $_.Reason.Contains('zeroStorageAt') }).Count -eq 1) 'verification and remote-state classification must preserve exact zero-sample review custody and expiry'
    foreach ($collectorFile in $config.Expected.HostedBackupProviderCollector) {
        Assert-True (Test-Path (Join-Path $repoRoot $collectorFile)) 'collector source must exist'
    }
    Assert-True (@($config.LocalAppDataExclusions | Where-Object { $_.Path -eq 'ElectronicLogbook/HostedBackup/github-plan-read-token.txt' -and $_.Lifecycle -eq 'fresh-authentication' }).Count -eq 1) 'dedicated monitoring authentication must not enter the transfer bundle'
    Assert-True (@($config.LocalAppDataExclusions | Where-Object { $_.Path -eq 'ElectronicLogbook/HostedBackup/github-actions-read-token.txt' -and $_.Lifecycle -eq 'fresh-authentication' }).Count -eq 1) 'repository-only Actions monitoring authentication must not enter the transfer bundle'
    Assert-True ($config.Expected.HostedBackupMonitor -contains 'tools/hosted-backup/daily-monitor-observation.mjs' -and $config.Expected.HostedBackupMonitor -contains 'tools/hosted-backup/daily-monitor-observation.test.mjs') 'authenticated history/object adapters and regression must be required checkout sources'
    Assert-True (@($config.LocalAppDataExclusions | Where-Object { $_.Path -eq 'ElectronicLogbook/HostedBackup/supabase-metadata-read-token.txt' -and $_.Lifecycle -eq 'fresh-authentication' }).Count -eq 1) 'Supabase metadata monitoring authentication must not enter the transfer bundle'
    Assert-True (@($config.LocalAppDataExclusions | Where-Object { $_.Path -eq 'ElectronicLogbook/HostedBackup/supabase-organization-read-token.txt' -and $_.Lifecycle -eq 'fresh-authentication' }).Count -eq 1) 'separate organization-only monitoring authentication must not enter the transfer bundle'
    Assert-True (@($config.LocalAppDataExclusions | Where-Object { $_.Path -eq 'ElectronicLogbook/HostedBackup/cloudflare-monitoring-read-token.txt' -and $_.Lifecycle -eq 'fresh-authentication' }).Count -eq 1) 'read-only Cloudflare monitoring authentication must not enter the transfer bundle'
    Assert-True (@($config.LocalAppDataExclusions | Where-Object { $_.Path -eq 'ElectronicLogbook/HostedBackup/cloudflare-monitoring-credentials.local.json' -and $_.Lifecycle -eq 'fresh-authentication' }).Count -eq 1) 'owner-retained Cloudflare API/S3 details must not enter the transfer bundle'
    Assert-True (@($config.LocalAppDataAssets | Where-Object { $_.Path -eq 'ElectronicLogbook/HostedBackup/source-connection.local.json' -and $_.Classification -eq 'secret-backup-source-credentials' -and -not $_.Required }).Count -eq 1) 'restricted source credentials must be explicitly classified for trusted transfer'
    Assert-True (@($config.LocalAppDataAssets | Where-Object { $_.Path -eq 'ElectronicLogbook/HostedBackup/source-root.crt' -and $_.Classification -eq 'public-backup-source-ca' }).Count -eq 1) 'source TLS CA must accompany the restricted connection'
    Assert-True ((Test-Path (Join-Path $repoRoot $config.Expected.HostedBackupSourceSetupScript)) -and $entrypointText -match 'Expected\.HostedBackupSourceSetupScript') 'source setup must resolve and be verified after checkout'
    foreach ($setting in @('HostedBackupSupervisedExportScript','HostedBackupSupervisedVerifyScript')) {
        Assert-True ((Test-Path (Join-Path $repoRoot $config.Expected[$setting])) -and $entrypointText.Contains('Expected.' + $setting)) 'supervised backup entrypoints must resolve and be verified after checkout'
    }
    Assert-True (@($config.LocalAppDataAssets | Where-Object { $_.Path -eq 'ElectronicLogbook/HostedBackup/r2-credentials.local.json' -and $_.Classification -eq 'secret-backup-storage-credentials' -and -not $_.Required }).Count -eq 1) 'R2 storage credentials must be explicitly classified for trusted local transfer'
    Assert-True ((Test-Path (Join-Path $repoRoot $config.Expected.HostedBackupR2AccessScript)) -and (Test-Path (Join-Path $repoRoot $config.Expected.HostedBackupR2Policy))) 'R2 setup and policy must resolve after checkout'
    Assert-True ($entrypointText -match 'Expected\.HostedBackupR2AccessScript' -and $entrypointText -match 'Expected\.HostedBackupR2Policy') 'transfer verification must check R2 setup files'
    Assert-True ((Test-Path (Join-Path $repoRoot $config.Expected.HostedBackupR2CheckScript)) -and $config.Expected.HostedBackupCurlCommand -eq 'curl.exe') 'owner-run R2 check must declare its script and Windows curl prerequisite'
    Assert-True ($entrypointText -match 'Expected\.HostedBackupR2CheckScript' -and $entrypointText -match 'Expected\.HostedBackupCurlCommand' -and $entrypointText -match "backupCurlHelp -match '--aws-sigv4'") 'transfer verification must check the R2 permission script and curl signing support'
    Assert-True ('*/backup-private.encrypted.pem' -in $config.ExcludedPathPatterns -and '*/recovery.elbbackup' -in $config.ExcludedPathPatterns) 'private recovery identity and capsule must never enter the transfer archive'
    Assert-True ($config.Expected.HostedBackupRecoveryPowerShellMinVersion -eq '7.2' -and (Test-Path (Join-Path $repoRoot $config.Expected.HostedBackupRecoveryScript))) 'protected recovery setup prerequisite and script must be documented in the manifest'
    Assert-True ($config.Expected.HostedBackupMonitorPowerShellHost -eq 'System32/WindowsPowerShell/v1.0/powershell.exe' -and $entrypointText.Contains('Expected.HostedBackupMonitorPowerShellHost')) 'optional hidden monitor tasks must verify the existing Windows PowerShell host from the manifest'
    Assert-True ($entrypointText -match "Firebase CLI version.*Expected\.FirebaseCliVersion") 'verifier must enforce the pinned Firebase CLI version'
    Assert-True ($entrypointText -match 'sdkManager.*Expected\.AndroidRecoverySystemImage') 'installer must install the recovery emulator Google Play system image'
    Assert-True ($entrypointText -match 'Android recovery Google Play image') 'verifier must report whether the recovery emulator image is installed'
    Assert-True ($entrypointText -match "googleAndroidClientPath = Join-Path.*'previewandroidclientid\.txt'" -and $entrypointText -match 'webMatch\.Groups\[1\].*androidMatch\.Groups\[1\]') 'verifier must require Web and permanent Preview Android OAuth client IDs from the same Google project without printing them'
    Assert-True ($entrypointText -match 'Firebase Android Preview config') 'verifier must validate the private Firebase Android config without printing it'
    Assert-True ($entrypointText -match 'HostedProjectMetadataFile' -and $entrypointText -match 'LegacyHostedProjectMetadataFile') 'verifier must accept canonical Preview metadata and its narrow legacy filename alias'
    Assert-True ($entrypointText -match 'Permanent Preview Android signing identity') 'verifier must require the permanent Preview keystore and protected credentials'
    Assert-True ($entrypointText -match "'-storepass:env'.*ELECTRONIC_LOGBOOK_PREVIEW_VERIFY_STORE_PASSWORD") 'verifier must keep the Preview keystore password out of the keytool command line'
    Assert-True ($entrypointText -match 'Android signing identity was restored incompletely') 'import must reject incomplete permanent Preview signing state'
    Assert-True ($entrypointText -match 'Assert-TransferConfiguration') 'every transfer action must validate the local allowlist and exclusion policy'
    Assert-True ($entrypointText -match 'Inventory by classification') 'export must summarize classifications without printing secret values'
    Assert-True ($entrypointText -match 'Assert-LocalAppDataInventoryClassified') 'export must reject newly discovered unclassified local files'
    Assert-True ($entrypointText -match 'winget\.exe list' -and $entrypointText -match "wingetAction = if.*'upgrade'") 'installer must distinguish installed Winget packages from missing packages'
    Assert-True ($entrypointText -match 'stillInstalled') 'installer must re-verify a package after a nonzero Winget upgrade result'

    $handoverText = Get-Content -LiteralPath (Join-Path $repoRoot 'LOCAL_DEVICE_SETUP_HANDOVER.md') -Raw -Encoding UTF8
    Assert-True ($handoverText -match 'Gradle applies this descriptor only to the permanent-package `preview` variant') 'handover must explain that the Firebase descriptor is restricted to Preview builds'
    Assert-True ($handoverText -match 'Debug\s+and acceptance builds deliberately skip it') 'handover must explain why suffixed development variants skip the Firebase descriptor'

    $ignored = & git -C $repoRoot check-ignore 'LOCAL_DEVICE_SETUP_HANDOVER.md' 2>$null
    Assert-True ($LASTEXITCODE -ne 0) 'the sanitized handover must be trackable'
    $archiveIgnore = & git -C $repoRoot check-ignore 'ElectronicLogbook-LocalDevelopment-20990101-000000.7z' 2>$null
    Assert-True ($LASTEXITCODE -eq 0) 'generated archives must remain ignored'
    & git -C $repoRoot check-ignore 'docs/local/research.md' | Out-Null
    Assert-True ($LASTEXITCODE -eq 0) 'local research must remain ignored'

    $syntheticRepo = Join-Path $temporaryRoot 'dry-run-repo'
    $syntheticLocal = Join-Path $temporaryRoot 'dry-run-localappdata'
    $syntheticCodex = Join-Path $temporaryRoot 'dry-run-codex'
    New-Item -ItemType Directory -Path $syntheticRepo, $syntheticLocal, $syntheticCodex -Force | Out-Null
    foreach ($asset in $config.RepoAssets | Where-Object Required) {
        $path = Join-Path $syntheticRepo $asset.Path
        New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
        Set-Content -LiteralPath $path -Value "synthetic $($asset.Path)" -Encoding UTF8
    }
    foreach ($asset in $config.LocalAppDataAssets | Where-Object Required) {
        $path = Join-Path $syntheticLocal $asset.Path
        New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
        Set-Content -LiteralPath $path -Value "synthetic $($asset.Classification)" -Encoding UTF8
    }
    foreach ($group in @($config.LocalAppDataAssets | Where-Object { $_.ContainsKey('RequirementGroup') } | Group-Object { $_['RequirementGroup'] })) {
        $asset = $group.Group | Select-Object -First 1
        $path = Join-Path $syntheticLocal $asset.Path
        New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
        Set-Content -LiteralPath $path -Value "synthetic $($asset.Classification)" -Encoding UTF8
    }
    $excludedLocalState = Join-Path $syntheticLocal 'ElectronicLogbook\Gate1RetainedState\must-not-transfer.json'
    $researchPath = Join-Path $syntheticRepo 'docs\local\research.md'
    New-Item -ItemType Directory -Path (Split-Path $researchPath -Parent) -Force | Out-Null
    Set-Content -LiteralPath $researchPath -Value 'synthetic private research' -Encoding UTF8
    Set-Content -LiteralPath (Join-Path (Split-Path $researchPath -Parent) 'not-allowlisted.json') -Value '{}' -Encoding UTF8
    New-Item -ItemType Directory -Path (Split-Path $excludedLocalState -Parent) -Force | Out-Null
    Set-Content -LiteralPath $excludedLocalState -Value '{"generated":true}' -Encoding UTF8
    $excludedTempState = Join-Path $syntheticLocal 'ElectronicLogbook\Temp\receipt-verify-test\source.zip'
    New-Item -ItemType Directory -Path (Split-Path $excludedTempState -Parent) -Force | Out-Null
    Set-Content -LiteralPath $excludedTempState -Value 'temporary diagnostic source archive' -Encoding UTF8
    $unclassifiedLocalState = Join-Path $syntheticLocal 'ElectronicLogbook\unclassified.tmp'
    Set-Content -LiteralPath $unclassifiedLocalState -Value 'must be classified' -Encoding UTF8
    $unclassifiedRun = Invoke-TransferProcess -Arguments @('-Action', 'Export', '-RepoRoot', $syntheticRepo, '-LocalAppDataRoot', $syntheticLocal, '-CodexHome', $syntheticCodex, '-WhatIf')
    Assert-True ($unclassifiedRun.ExitCode -ne 0) 'export must reject an unclassified local file'
    Assert-True (($unclassifiedRun.Error + $unclassifiedRun.Output) -match 'local ElectronicLogbook file\(s\) are unclassified') 'unclassified-file rejection must explain how to update the manifest'
    Remove-Item -LiteralPath $unclassifiedLocalState -Force
    $hostedMetadataPath = Join-Path $syntheticLocal 'ElectronicLogbook\Supabase\hosted-preview-projects.local.json'
    Remove-Item -LiteralPath $hostedMetadataPath -Force
    $missingGroupRun = Invoke-TransferProcess -Arguments @('-Action', 'Export', '-RepoRoot', $syntheticRepo, '-LocalAppDataRoot', $syntheticLocal, '-CodexHome', $syntheticCodex, '-WhatIf')
    Assert-True ($missingGroupRun.ExitCode -ne 0) 'export must require canonical or compatibility hosted-project metadata'
    Assert-True (($missingGroupRun.Error + $missingGroupRun.Output) -match 'Required local transfer asset group is missing: hosted-project-metadata') 'missing hosted metadata must identify its requirement group'
    Set-Content -LiteralPath $hostedMetadataPath -Value 'synthetic private-project-metadata' -Encoding UTF8
    $dryRun = Invoke-TransferProcess -Arguments @('-Action', 'Export', '-RepoRoot', $syntheticRepo, '-LocalAppDataRoot', $syntheticLocal, '-CodexHome', $syntheticCodex, '-WhatIf')
    Assert-True ($dryRun.ExitCode -eq 0) "synthetic export dry run failed: $($dryRun.Error)"
    Assert-True ($dryRun.Output -match 'Export dry run completed') 'dry run must confirm that no archive was created'
    Assert-True ($dryRun.Output -match 'Inventory by classification:.*secret-api-credential=3') 'dry run must expose named classification counts without secret values'

    $mainExportArchive = Join-Path $temporaryRoot 'main-export.7z'
    $mainExport = Invoke-TransferProcess -Arguments @(
        '-Action', 'Export', '-RepoRoot', $syntheticRepo, '-LocalAppDataRoot', $syntheticLocal,
        '-CodexHome', $syntheticCodex, '-OutputPath', $mainExportArchive, '-TestMode'
    )
    Assert-True ($mainExport.ExitCode -eq 0) "main exporter failed against synthetic roots: $($mainExport.Error)"
    Assert-True (Test-Path -LiteralPath $mainExportArchive -PathType Leaf) 'main exporter must create the requested archive'
    & $script:sevenZip t $mainExportArchive -bb0 -bd | Out-Null
    Assert-True ($LASTEXITCODE -eq 0) 'main exporter archive must pass 7-Zip integrity testing'
    $mainListing = (& $script:sevenZip l $mainExportArchive -bb0 -bd | Out-String)
    Assert-True ($mainListing -notmatch 'must-not-transfer\.json') 'unlisted retained-device state must stay outside the transfer archive'
    Assert-True ($mainListing -notmatch 'source\.zip') 'temporary diagnostic files must stay outside the transfer archive'
    Assert-True ($mainListing -match 'electronic-logbook-development\.keystore') 'explicitly allowlisted signing identity must be present in the transfer archive'
    Assert-True ($mainListing -match 'docs[/\\]local[/\\]research\.md') 'local research must survive operational export'
    Assert-True ($mainListing -notmatch 'not-allowlisted\.json') 'local research transfer must select Markdown only'

    $bundleSource = Join-Path $temporaryRoot 'valid-source'
    $validArchive = Join-Path $temporaryRoot 'valid.7z'
    New-SyntheticBundle -SourceRoot $bundleSource -Archive $validArchive
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & $script:sevenZip l $validArchive -bb0 -bd *> (Join-Path $temporaryRoot 'archive-listing.txt')
    $listingExitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousPreference
    Assert-True ($listingExitCode -eq 0) 'unencrypted archive must be readable without a password'
    $publicListing = Get-Content -LiteralPath (Join-Path $temporaryRoot 'archive-listing.txt') -Raw
    Assert-True ($publicListing -match 'AGENTS\.md') 'unencrypted archive must expose payload filenames'
    & $script:sevenZip t $validArchive -bb0 -bd | Out-Null
    Assert-True ($LASTEXITCODE -eq 0) 'synthetic archive must pass 7-Zip integrity testing'

    $previewRepo = Join-Path $temporaryRoot 'preview-repo'
    New-Item -ItemType Directory -Path $previewRepo -Force | Out-Null
    $previewImport = Invoke-TransferProcess -Arguments @(
        '-Action', 'Import', '-ArchivePath', $validArchive, '-RepoRoot', $previewRepo,
        '-LocalAppDataRoot', (Join-Path $temporaryRoot 'preview-local'),
        '-CodexHome', (Join-Path $temporaryRoot 'preview-codex'),
        '-SkipDependencyRestore', '-SkipPostImportVerify', '-TestMode', '-WhatIf'
    )
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
    )
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
    )
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
    )
    Assert-True ($undeclaredImport.ExitCode -ne 0) 'undeclared archive files must be rejected'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $undeclaredRepo 'AGENTS.md'))) 'undeclared-file rejection must happen before destination writes'

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
    )
    Assert-True ($corruptImport.ExitCode -ne 0) 'corrupted archive must fail import'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $corruptRepo 'AGENTS.md'))) 'corrupted archive must not change destinations'

    Write-Host "Local development transfer tests passed: $passed assertions." -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
