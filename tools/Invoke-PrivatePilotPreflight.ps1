# Creates a redacted private-pilot preflight report.

[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$ConnectionString = $env:ELB_SUPABASE_PILOT_DB_URL,
    [string]$OutputPath,
    [switch]$RunRlsHarness
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path $RepoRoot).Path
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "artifacts\private-pilot-20260806\preflight.json"
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

$checks = New-Object System.Collections.Generic.List[object]

$requiredFiles = @(
    "docs\private-pilot-runbook.md",
    "docs\public-release-hardening-gate.md",
    "docs\hosted-pilot-supabase.md",
    "supabase\migrations\20260806000000_hosted_pilot_foundation.sql",
    "supabase\tests\hosted_pilot_rls.sql",
    "tools\Invoke-PrivatePilotHealthCheck.ps1",
    "artifacts\private-pilot-20260806\cohort.md",
    "artifacts\private-pilot-20260806\incident-log.md",
    "artifacts\private-pilot-20260806\weekly-checkins.md",
    "artifacts\private-pilot-20260806\exit-decision.md"
)

foreach ($relativePath in $requiredFiles) {
    $path = Join-Path $repoRoot $relativePath
    [void]$checks.Add((New-Check `
        -Name "required file: $relativePath" `
        -Passed (Test-Path -LiteralPath $path) `
        -Detail "presence only; no participant data read"))
}

$runbook = Get-Content -LiteralPath (Join-Path $repoRoot "docs\private-pilot-runbook.md") -Raw -Encoding UTF8
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
        -Name "hosted pilot health snapshot" `
        -Passed $false `
        -Detail "skipped; provide -ConnectionString or ELB_SUPABASE_PILOT_DB_URL"))
} else {
    $healthFileName = "{0}.health.json" -f [System.IO.Path]::GetFileNameWithoutExtension($OutputPath)
    $healthPath = Join-Path (Split-Path $OutputPath -Parent) $healthFileName
    & (Join-Path $repoRoot "tools\Invoke-PrivatePilotHealthCheck.ps1") `
        -ConnectionString $ConnectionString `
        -OutputPath $healthPath | Out-Host
    $healthSnapshot = Get-Content -LiteralPath $healthPath -Raw -Encoding UTF8 | ConvertFrom-Json
    [void]$checks.Add((New-Check `
        -Name "hosted pilot health snapshot" `
        -Passed ($healthSnapshot.databaseSizeStatus -eq "ok" -and @($healthSnapshot.localReviewFindings).Count -eq 0) `
        -Detail "redacted health snapshot captured"))
}

if ($RunRlsHarness) {
    if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
        [void]$checks.Add((New-Check `
            -Name "hosted RLS harness" `
            -Passed $false `
            -Detail "skipped; provide -ConnectionString or ELB_SUPABASE_PILOT_DB_URL"))
    } else {
        $psql = Get-Command psql -ErrorAction SilentlyContinue
        if ($null -eq $psql) {
            [void]$checks.Add((New-Check `
                -Name "hosted RLS harness" `
                -Passed $false `
                -Detail "psql was not found on PATH"))
        } else {
            & $psql.Source $ConnectionString -v ON_ERROR_STOP=1 -f (Join-Path $repoRoot "supabase\tests\hosted_pilot_rls.sql") | Out-Host
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
Write-Host "Private pilot preflight report written to $OutputPath." -ForegroundColor Green

if (-not $allPassed) {
    throw "Private pilot preflight is not ready. See $OutputPath for redacted details."
}
