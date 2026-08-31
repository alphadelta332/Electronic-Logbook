# Captures a redacted FlightLogX Preview health snapshot.

[CmdletBinding()]
param(
    [string]$ConnectionString,
    [string]$OutputPath,
    [int]$AccountReviewThreshold = 25,
    [int64]$DatabaseBytesReviewThreshold = 250MB,
    [int64]$OperationReviewThreshold = 50000,
    [switch]$PassThru
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    $ConnectionString = $env:ELB_SUPABASE_PREVIEW_DB_URL
}

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    $ConnectionString = [Environment]::GetEnvironmentVariable("ELB_SUPABASE_PREVIEW_DB_URL", "User")
}

# Legacy alias for existing development machines. Remove only after local state has migrated.
if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    $ConnectionString = $env:ELB_SUPABASE_PILOT_DB_URL
}

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    $ConnectionString = [Environment]::GetEnvironmentVariable("ELB_SUPABASE_PILOT_DB_URL", "User")
}

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw "Provide -ConnectionString or set ELB_SUPABASE_PREVIEW_DB_URL. The legacy ELB_SUPABASE_PILOT_DB_URL alias is also accepted. The value is never printed."
}

$psql = Get-Command psql -ErrorAction SilentlyContinue
if ($null -eq $psql) {
    throw "psql was not found on PATH."
}

$query = @"
select row_to_json(health)
from public.get_hosted_pilot_health() as health;
"@

$raw = & $psql.Source $ConnectionString -v ON_ERROR_STOP=1 -t -A -c $query
if ($LASTEXITCODE -ne 0) {
    throw "Hosted Preview health query failed."
}

$jsonText = ($raw | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($jsonText)) {
    throw "Hosted Preview health query returned no rows."
}

$health = $jsonText | ConvertFrom-Json
$triggers = @()
if ($null -ne $health.paid_plan_upgrade_triggers) {
    $triggers = @($health.paid_plan_upgrade_triggers)
}

$reviewFindings = New-Object System.Collections.Generic.List[string]
if ([int64]$health.active_account_count -ge $AccountReviewThreshold) {
    [void]$reviewFindings.Add("active Preview accounts reached the local review threshold")
}
if ([int64]$health.estimated_database_bytes -ge $DatabaseBytesReviewThreshold) {
    [void]$reviewFindings.Add("estimated database size reached the local review threshold")
}
if ([int64]$health.stored_operation_count -ge $OperationReviewThreshold) {
    [void]$reviewFindings.Add("stored operation count reached the local review threshold")
}
foreach ($trigger in $triggers) {
    if (-not [string]::IsNullOrWhiteSpace([string]$trigger)) {
        [void]$reviewFindings.Add([string]$trigger)
    }
}

$snapshot = [ordered]@{
    capturedAt = (Get-Date).ToUniversalTime().ToString("o")
    source = "public.get_hosted_pilot_health"
    activeAccountCount = [int64]$health.active_account_count
    activeDeviceCount = [int64]$health.active_device_count
    storedOperationCount = [int64]$health.stored_operation_count
    estimatedDatabaseBytes = [int64]$health.estimated_database_bytes
    databaseSizeStatus = [string]$health.database_size_status
    paidPlanUpgradeTriggers = @($triggers)
    localReviewFindings = @($reviewFindings)
}

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $resolvedOutputPath = $OutputPath
    if (-not [System.IO.Path]::IsPathRooted($resolvedOutputPath)) {
        $resolvedOutputPath = Join-Path (Get-Location).Path $resolvedOutputPath
    }

    $outputDirectory = Split-Path $resolvedOutputPath -Parent
    if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
        New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
    }

    $snapshot | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $resolvedOutputPath -Encoding UTF8
    Write-Host "Hosted Preview health snapshot written to $resolvedOutputPath." -ForegroundColor Green
} else {
    $snapshot | ConvertTo-Json -Depth 6
}

if ($reviewFindings.Count -gt 0) {
    Write-Warning ("Preview review required: " + ($reviewFindings -join "; "))
} else {
    Write-Host "Hosted Preview health is within local review thresholds." -ForegroundColor Green
}

if ($PassThru) {
    [pscustomobject]$snapshot
}
