# Creates a redacted FlightLogX Preview preflight report.

[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$ConnectionString,
    [string]$AccessToken = $env:ELB_SUPABASE_PREVIEW_ACCESS_TOKEN,
    [string]$RefreshToken = $env:ELB_SUPABASE_PREVIEW_REFRESH_TOKEN,
    [string]$ServiceRoleKey = $env:ELB_SUPABASE_PREVIEW_SERVICE_ROLE_KEY,
    [string]$ExpectedDeviceId = $env:ELB_SUPABASE_PREVIEW_DEVICE_ID,
    [string]$OutputPath,
    [switch]$RunRlsHarness
)

$ErrorActionPreference = "Stop"

# Legacy environment aliases remain valid while existing owner machines migrate.
if ([string]::IsNullOrWhiteSpace($AccessToken)) { $AccessToken = $env:ELB_SUPABASE_PILOT_ACCESS_TOKEN }
if ([string]::IsNullOrWhiteSpace($RefreshToken)) { $RefreshToken = $env:ELB_SUPABASE_PILOT_REFRESH_TOKEN }
if ([string]::IsNullOrWhiteSpace($ServiceRoleKey)) { $ServiceRoleKey = $env:ELB_SUPABASE_PILOT_SERVICE_ROLE_KEY }
if ([string]::IsNullOrWhiteSpace($ExpectedDeviceId)) { $ExpectedDeviceId = $env:ELB_SUPABASE_PILOT_DEVICE_ID }

$repoRoot = (Resolve-Path $RepoRoot).Path
$localSupabaseRoot = Join-Path $env:LOCALAPPDATA "ElectronicLogbook\Supabase"
$canonicalSupabaseConfigPath = Join-Path $localSupabaseRoot "hosted-preview-projects.local.json"
$legacySupabaseConfigPath = Join-Path $localSupabaseRoot "hosted-pilot-projects.local.json"
$localSupabaseConfigPath = if (Test-Path -LiteralPath $canonicalSupabaseConfigPath -PathType Leaf) {
    $canonicalSupabaseConfigPath
} else {
    $legacySupabaseConfigPath
}
$localSupabaseAccessTokenPath = Join-Path $localSupabaseRoot "access-token.txt"
$localSupabaseConfig = $null
$supabaseManagementToken = $null
if (Test-Path -LiteralPath $localSupabaseConfigPath) {
    $localSupabaseConfig = Get-Content -LiteralPath $localSupabaseConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
}
$previewProject = if ($null -ne $localSupabaseConfig -and $null -ne $localSupabaseConfig.preview) {
    $localSupabaseConfig.preview
} elseif ($null -ne $localSupabaseConfig) {
    $localSupabaseConfig.privatePilot
} else {
    $null
}
if (Test-Path -LiteralPath $localSupabaseAccessTokenPath) {
    $supabaseManagementToken = (Get-Content -LiteralPath $localSupabaseAccessTokenPath -Raw -Encoding UTF8).Trim()
}
if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    $ConnectionString = $env:ELB_SUPABASE_PREVIEW_DB_URL
}
if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    $ConnectionString = [Environment]::GetEnvironmentVariable("ELB_SUPABASE_PREVIEW_DB_URL", "User")
}
if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    $ConnectionString = $env:ELB_SUPABASE_PILOT_DB_URL
}
if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    $ConnectionString = [Environment]::GetEnvironmentVariable("ELB_SUPABASE_PILOT_DB_URL", "User")
}
if ([string]::IsNullOrWhiteSpace($ConnectionString) -and $null -ne $localSupabaseConfig) {
    if ($null -ne $previewProject -and
        -not [string]::IsNullOrWhiteSpace($previewProject.project_ref) -and
        -not [string]::IsNullOrWhiteSpace($previewProject.region) -and
        -not [string]::IsNullOrWhiteSpace($previewProject.db_password)) {
        $encodedPassword = [Uri]::EscapeDataString($previewProject.db_password)
        $poolerHost = "aws-0-{0}.pooler.supabase.com" -f $previewProject.region
        $ConnectionString = "postgresql://postgres.{0}:{1}@{2}:5432/postgres" -f $previewProject.project_ref, $encodedPassword, $poolerHost
    }
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "artifacts\flightlogx-preview\preflight.json"
}
if (-not [System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath = Join-Path $repoRoot $OutputPath
}

function New-Check {
    param(
        [Parameter(Mandatory)]
        [string]$Name,
        [Parameter(Mandatory)]
        [bool]$Passed,
        [string]$Detail = ""
    )

    [pscustomobject][ordered]@{
        name = $Name
        passed = $Passed
        detail = $Detail
    }
}

function Read-JwtPayload {
    param([Parameter(Mandatory)][string]$Token)

    $parts = $Token.Split('.')
    if ($parts.Count -ne 3) {
        throw "Credential is not a JWT."
    }
    $payload = $parts[1].Replace('-', '+').Replace('_', '/')
    $payload = $payload.PadRight($payload.Length + ((4 - ($payload.Length % 4)) % 4), '=')
    [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload)) | ConvertFrom-Json
}

function Invoke-SecretSafeCheck {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Action
    )

    try {
        & $Action
        New-Check -Name $Name -Passed $true -Detail "validated without printing secrets"
    } catch {
        New-Check -Name $Name -Passed $false -Detail ("{0}: validation failed; secret-bearing exception text omitted" -f $_.Exception.GetType().Name)
    }
}

function Get-PreviewDbHost {
    param([string]$DatabaseConnectionString)

    if ([string]::IsNullOrWhiteSpace($DatabaseConnectionString)) {
        return $null
    }

    $hostMatch = [regex]::Match($DatabaseConnectionString, "@([^:/?\s]+)")
    if ($hostMatch.Success) {
        return $hostMatch.Groups[1].Value
    }

    return $null
}

$checks = New-Object System.Collections.Generic.List[object]

$runtimeConfigPath = Join-Path $repoRoot "mobile\src\ElectronicLogbook.Mobile\wwwroot\hosted-sync.local.json"
$runtimeConfig = $null
[void]$checks.Add((Invoke-SecretSafeCheck -Name "packaged runtime config copies match source" -Action {
    if (-not (Test-Path -LiteralPath $runtimeConfigPath)) {
        throw "source runtime config is missing"
    }
    $sourceHash = (Get-FileHash -LiteralPath $runtimeConfigPath -Algorithm SHA256).Hash
    $copyRoots = @(
        (Join-Path $repoRoot "mobile\src\ElectronicLogbook.Mobile\bin"),
        (Join-Path $repoRoot "mobile\android\app\src\main\assets\public")
    )
    $copies = foreach ($copyRoot in $copyRoots) {
        if (Test-Path -LiteralPath $copyRoot) {
            Get-ChildItem -LiteralPath $copyRoot -Recurse -File -Filter "hosted-sync.local.json"
        }
    }
    if (@($copies).Count -eq 0) {
        throw "no packaged runtime-config copy was found; build the isolated acceptance app first"
    }
    if ($copies | Where-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash -ne $sourceHash }) {
        throw "one or more packaged runtime-config copies differ from the source"
    }
    $script:runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
}))

[void]$checks.Add((Invoke-SecretSafeCheck -Name "project ref, anon-key role/ref/expiry, and Auth endpoint" -Action {
    if ($null -eq $runtimeConfig) {
        $script:runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    $uri = [Uri]$runtimeConfig.supabaseUrl
    if ($uri.Scheme -ne "https" -or $uri.AbsolutePath.Trim('/') -ne "") {
        throw "Supabase URL is not an HTTPS project root"
    }
    $projectRef = $uri.Host.Split('.')[0]
    $claims = Read-JwtPayload -Token $runtimeConfig.anonKey
    if ($claims.role -notin @("anon", "publishable")) { throw "anon-key role is invalid" }
    if ($claims.ref -and $claims.ref -ne $projectRef) { throw "anon-key project ref does not match" }
    if ($claims.exp -and [DateTimeOffset]::FromUnixTimeSeconds([long]$claims.exp) -le [DateTimeOffset]::UtcNow) { throw "anon key is expired" }
    $authUri = [Uri]::new($uri, "/auth/v1/settings")
    $response = Invoke-WebRequest -Uri $authUri -Headers @{ apikey = $runtimeConfig.anonKey } -Method Get -UseBasicParsing
    if ($response.StatusCode -lt 200 -or $response.StatusCode -ge 300) { throw "Auth endpoint rejected the packaged anon key" }
}))

[void]$checks.Add((Invoke-SecretSafeCheck -Name "Preview database region is ap-southeast-2" -Action {
    $databaseHost = Get-PreviewDbHost -DatabaseConnectionString $ConnectionString
    if ([string]::IsNullOrWhiteSpace($databaseHost)) {
        throw "Preview database host could not be read from the configured connection string"
    }
    if ($databaseHost -notmatch "(^|[.-])ap-southeast-2([.-]|$)") {
        throw "Preview database host does not identify ap-southeast-2"
    }
}))

[void]$checks.Add((Invoke-SecretSafeCheck -Name "Supabase management token sees active Preview project in ap-southeast-2" -Action {
    if ([string]::IsNullOrWhiteSpace($supabaseManagementToken)) {
        throw "Supabase management access token is not configured"
    }
    if ($null -eq $previewProject) { throw "local Preview project metadata is not configured" }
    $projectRef = $previewProject.project_ref
    if ([string]::IsNullOrWhiteSpace($projectRef)) {
        throw "local Preview project ref is not configured"
    }

    $previousToken = $env:SUPABASE_ACCESS_TOKEN
    $previousTelemetry = $env:SUPABASE_TELEMETRY_DISABLED
    try {
        $env:SUPABASE_ACCESS_TOKEN = $supabaseManagementToken
        $env:SUPABASE_TELEMETRY_DISABLED = "1"
        $projectsJson = & supabase projects list --output json 2>$null
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($projectsJson)) {
            throw "Supabase projects list failed"
        }
        $projects = $projectsJson | ConvertFrom-Json
        $preview = @($projects | Where-Object { $_.id -eq $projectRef -or $_.ref -eq $projectRef })[0]
        if ($null -eq $preview) {
            throw "Preview project was not returned by Supabase projects list"
        }
        if ($preview.region -ne "ap-southeast-2") {
            throw "Preview project is not in ap-southeast-2"
        }
        if ($preview.status -ne "ACTIVE_HEALTHY") {
            throw "Preview project is not active and healthy"
        }
    }
    finally {
        $env:SUPABASE_ACCESS_TOKEN = $previousToken
        $env:SUPABASE_TELEMETRY_DISABLED = $previousTelemetry
    }
}))

[void]$checks.Add((Invoke-SecretSafeCheck -Name "Google sign-in allows the Windows updater loopback callback" -Action {
    if ([string]::IsNullOrWhiteSpace($supabaseManagementToken)) {
        throw "Supabase management access token is not configured"
    }
    if ($null -eq $previewProject) { throw "local Preview project metadata is not configured" }
    $projectRef = $previewProject.project_ref
    if ([string]::IsNullOrWhiteSpace($projectRef)) {
        throw "local Preview project ref is not configured"
    }

    $authConfig = Invoke-RestMethod `
        -Uri "https://api.supabase.com/v1/projects/$projectRef/config/auth" `
        -Headers @{ Authorization = "Bearer $supabaseManagementToken" } `
        -Method Get
    if ($authConfig.external_google_enabled -ne $true) {
        throw "Google sign-in is not enabled"
    }
    $redirects = @($authConfig.uri_allow_list -split ',' | ForEach-Object { $_.Trim() })
    if ($redirects -notcontains "http://127.0.0.1:*/flightlogx-auth/**") {
        throw "Windows updater loopback callback is not allow-listed"
    }
}))

[void]$checks.Add((Invoke-SecretSafeCheck -Name "Auth signup disabled with invited-user email and Google only" -Action {
    if ($null -eq $runtimeConfig) {
        $script:runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    $settings = Invoke-RestMethod -Uri ([Uri]::new([Uri]$runtimeConfig.supabaseUrl, "/auth/v1/settings")) `
        -Headers @{ apikey = $runtimeConfig.anonKey } -Method Get
    if ($settings.disable_signup -ne $true) {
        throw "public Auth signup is not disabled"
    }
    if ($settings.external.email -ne $true) {
        throw "email sign-in is not enabled"
    }
    if ($settings.external.google -ne $true) {
        throw "Google returning-user recovery is not enabled"
    }
    $allowedExternalProviders = @("email", "google")
    $enabledExternalProviders = @(
        $settings.external.PSObject.Properties |
            Where-Object { $_.Name -notin $allowedExternalProviders -and $_.Value -eq $true } |
            Select-Object -ExpandProperty Name
    )
    if (@($enabledExternalProviders).Count -gt 0) {
        throw "one or more unapproved Auth providers are enabled"
    }
}))

[void]$checks.Add((Invoke-SecretSafeCheck -Name "desktop CLI management token, local Supabase, or service-role credential" -Action {
    if (-not [string]::IsNullOrWhiteSpace($supabaseManagementToken)) {
        return
    }
    if ([string]::IsNullOrWhiteSpace($ServiceRoleKey)) {
        $supabaseStatus = & supabase status --output json 2>$null
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($supabaseStatus)) {
            throw "no desktop management token, service-role credential, or readable local Supabase CLI status is configured"
        }
        return
    }
    $claims = Read-JwtPayload -Token $ServiceRoleKey
    if ($claims.role -ne "service_role") { throw "configured desktop key is not a service-role credential" }
    if ($null -ne $runtimeConfig) {
        $projectRef = ([Uri]$runtimeConfig.supabaseUrl).Host.Split('.')[0]
        if ($claims.ref -and $claims.ref -ne $projectRef) { throw "service-role project ref does not match" }
    }
}))

$authUserId = $null
[void]$checks.Add((Invoke-SecretSafeCheck -Name "retained access token through auth user endpoint" -Action {
    if ([string]::IsNullOrWhiteSpace($AccessToken)) { throw "retained access token is not configured on the desktop" }
    $accessClaims = Read-JwtPayload -Token $AccessToken
    if ($accessClaims.exp -and [DateTimeOffset]::FromUnixTimeSeconds([long]$accessClaims.exp) -le [DateTimeOffset]::UtcNow) {
        if ([string]::IsNullOrWhiteSpace($RefreshToken)) { throw "access token is expired and no retained refresh token is configured" }
        $refreshBody = @{ refresh_token = $RefreshToken } | ConvertTo-Json -Compress
        $refreshed = Invoke-RestMethod -Uri ([Uri]::new([Uri]$runtimeConfig.supabaseUrl, "/auth/v1/token?grant_type=refresh_token")) `
            -Headers @{ apikey = $runtimeConfig.anonKey } -Method Post -ContentType "application/json" -Body $refreshBody
        $script:AccessToken = $refreshed.access_token
    }
    $user = Invoke-RestMethod -Uri ([Uri]::new([Uri]$runtimeConfig.supabaseUrl, "/auth/v1/user")) `
        -Headers @{ apikey = $runtimeConfig.anonKey; Authorization = "Bearer $AccessToken" } -Method Get
    if ([string]::IsNullOrWhiteSpace($user.id)) { throw "Auth user response did not contain an account identifier" }
    $script:authUserId = $user.id
}))

$requiredFiles = @(
    "docs\flightlogx-preview-runbook.md",
    "docs\public-release-hardening-gate.md",
    "docs\hosted-preview-supabase.md",
    "supabase\migrations\20260806000000_hosted_pilot_foundation.sql",
    "supabase\tests\hosted_preview_rls.sql",
    "tools\Invoke-PreviewHealthCheck.ps1"
)

foreach ($relativePath in $requiredFiles) {
    $path = Join-Path $repoRoot $relativePath
    [void]$checks.Add((New-Check `
        -Name "required file: $relativePath" `
        -Passed (Test-Path -LiteralPath $path) `
        -Detail "presence only; no participant data read"))
}

$runbook = Get-Content -LiteralPath (Join-Path $repoRoot "docs\flightlogx-preview-runbook.md") -Raw -Encoding UTF8
[void]$checks.Add((New-Check `
    -Name "runbook defines invitation process" `
    -Passed ($runbook -match "## Invitation Process" -and $runbook -match "Public\s+self-registration must remain disabled") `
    -Detail "checks committed runbook text"))
[void]$checks.Add((New-Check `
    -Name "runbook defines incident severities" `
    -Passed ($runbook -match "S0 data loss" -and $runbook -match "S1 sync/security" -and $runbook -match "S2 blocked workflow") `
    -Detail "checks committed runbook text"))
[void]$checks.Add((New-Check `
    -Name "runbook defines exit decision criteria" `
    -Passed ($runbook -match "Pass requires:" -and $runbook -match "Fail if data recovery is uncertain") `
    -Detail "checks committed runbook text"))

$publicGate = Get-Content -LiteralPath (Join-Path $repoRoot "docs\public-release-hardening-gate.md") -Raw -Encoding UTF8
[void]$checks.Add((New-Check `
    -Name "public release hardening remains gated" `
    -Passed ($publicGate -match "Status: intentionally not started" -and $publicGate -match "project owner explicitly decides") `
    -Detail "checks committed gate text"))

$healthSnapshot = $null
if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    [void]$checks.Add((New-Check `
        -Name "hosted Preview health snapshot" `
        -Passed $false `
        -Detail "skipped; provide -ConnectionString or ELB_SUPABASE_PREVIEW_DB_URL"))
} else {
    $healthFileName = "{0}.health.json" -f [System.IO.Path]::GetFileNameWithoutExtension($OutputPath)
    $healthPath = Join-Path (Split-Path $OutputPath -Parent) $healthFileName
    & (Join-Path $repoRoot "tools\Invoke-PreviewHealthCheck.ps1") `
        -ConnectionString $ConnectionString `
        -OutputPath $healthPath | Out-Host
    $healthSnapshot = Get-Content -LiteralPath $healthPath -Raw -Encoding UTF8 | ConvertFrom-Json
    [void]$checks.Add((New-Check `
        -Name "hosted Preview health snapshot" `
        -Passed ($healthSnapshot.databaseSizeStatus -eq "ok" -and @($healthSnapshot.localReviewFindings).Count -eq 0) `
        -Detail "redacted health snapshot captured"))
}

[void]$checks.Add((Invoke-SecretSafeCheck -Name "active account and existing registered device" -Action {
    if ([string]::IsNullOrWhiteSpace($ConnectionString)) { throw "database connection is not configured" }
    if ([string]::IsNullOrWhiteSpace($authUserId)) { throw "retained Auth user was not validated" }
    $accountGuid = [Guid]::Parse($authUserId).ToString("D")
    $devicePredicate = ""
    if (-not [string]::IsNullOrWhiteSpace($ExpectedDeviceId)) {
        $deviceGuid = [Guid]::Parse($ExpectedDeviceId.Replace("dev_", "")).ToString("D")
        $devicePredicate = " and device_id = '$deviceGuid'::uuid"
    }
    $query = @"
select
  (select count(*) from public.accounts where account_id = '$accountGuid'::uuid and status = 'active'),
  (select count(*) from public.devices where account_id = '$accountGuid'::uuid and status = 'active'$devicePredicate),
  (select count(*) from public.logbooks where owner_account_id = '$accountGuid'::uuid);
"@
    $psql = Get-Command psql -ErrorAction Stop
    $row = & $psql.Source $ConnectionString -v ON_ERROR_STOP=1 -t -A -F '|' -c $query
    if ($LASTEXITCODE -ne 0) { throw "database account/device read failed" }
    $counts = ([string]$row).Trim().Split('|')
    if ($counts.Count -ne 3 -or $counts[0] -ne '1' -or $counts[1] -ne '1' -or $counts[2] -ne '1') {
        throw "expected one active account, one matching active device, and one hosted logbook"
    }
}))

if ($RunRlsHarness) {
    if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
        [void]$checks.Add((New-Check `
            -Name "hosted RLS harness" `
            -Passed $false `
            -Detail "skipped; provide -ConnectionString or ELB_SUPABASE_PREVIEW_DB_URL"))
    } else {
        $psql = Get-Command psql -ErrorAction SilentlyContinue
        if ($null -eq $psql) {
            [void]$checks.Add((New-Check `
                -Name "hosted RLS harness" `
                -Passed $false `
                -Detail "psql was not found on PATH"))
        } else {
            & $psql.Source $ConnectionString -v ON_ERROR_STOP=1 -f (Join-Path $repoRoot "supabase\tests\hosted_preview_rls.sql") | Out-Host
            [void]$checks.Add((New-Check `
                -Name "hosted RLS harness" `
                -Passed ($LASTEXITCODE -eq 0) `
                -Detail "adversarial RLS script completed"))
        }
    }
} else {
    [void]$checks.Add((New-Check `
        -Name "hosted RLS harness" `
        -Passed $false `
        -Detail "not run; pass -RunRlsHarness for pre-invite evidence"))
}

$allPassed = -not ($checks | Where-Object { -not $_.passed })
$status = if ($allPassed) { "ready-for-private-cohort-invite" } else { "not-ready" }
$checkResults = $checks.ToArray()
$report = [pscustomobject][ordered]@{
    capturedAt = (Get-Date).ToUniversalTime().ToString("o")
    status = $status
    secretHandling = "connection strings, tokens, participant names, emails, and workbook paths are not written"
    checks = $checkResults
    health = $healthSnapshot
}

$outputDirectory = Split-Path $OutputPath -Parent
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
Write-Host "FlightLogX Preview preflight report written to $OutputPath." -ForegroundColor Green

if (-not $allPassed) {
    throw "FlightLogX Preview preflight is not ready. See $OutputPath for redacted details."
}
