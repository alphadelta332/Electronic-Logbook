# Runs one disposable end-to-end recovery rehearsal against the hosted development project.

[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$EvidenceDirectory,
    [switch]$WorkbookClientInvestigation,
    [switch]$WorkbookMigrationJourney
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
$localRoot = Join-Path $env:LOCALAPPDATA 'ElectronicLogbook\Supabase'
$canonicalMetadataPath = Join-Path $localRoot 'hosted-preview-projects.local.json'
$legacyMetadataPath = Join-Path $localRoot 'hosted-pilot-projects.local.json'
$metadataPath = if (Test-Path -LiteralPath $canonicalMetadataPath -PathType Leaf) {
    $canonicalMetadataPath
} else {
    $legacyMetadataPath
}
$tokenPath = Join-Path $localRoot 'access-token.txt'
$projectPath = Join-Path $repoRoot 'supabase\tests\HostedRecoveryRehearsal\HostedRecoveryRehearsal.csproj'

foreach ($path in @($metadataPath, $tokenPath, $projectPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required hosted-rehearsal input is missing: $path"
    }
}

$supabase = Get-Command 'supabase' -ErrorAction Stop | Select-Object -First 1
$psql = Get-Command 'psql' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $psql) {
    $psqlPath = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
    if (-not (Test-Path -LiteralPath $psqlPath -PathType Leaf)) {
        throw 'PostgreSQL 17 psql is required for trigger-safe disposable ledger cleanup.'
    }
    $psql = Get-Item -LiteralPath $psqlPath
}
$psqlExecutablePath = if (-not [string]::IsNullOrWhiteSpace([string]$psql.Source)) {
    [string]$psql.Source
} else {
    [string]$psql.FullName
}
if ([string]::IsNullOrWhiteSpace($psqlExecutablePath) -or
    -not (Test-Path -LiteralPath $psqlExecutablePath -PathType Leaf)) {
    throw 'PostgreSQL 17 psql executable path could not be resolved.'
}
$metadata = Get-Content -LiteralPath $metadataPath -Raw -Encoding UTF8 | ConvertFrom-Json
$projectRef = [string]$metadata.development.project_ref
if ([string]::IsNullOrWhiteSpace($projectRef)) {
    throw 'Development project metadata does not contain a project ref.'
}

$managementToken = (Get-Content -LiteralPath $tokenPath -Raw -Encoding UTF8).Trim()
$managementHeaders = @{ Authorization = "Bearer $managementToken" }
$projects = Invoke-RestMethod -Uri 'https://api.supabase.com/v1/projects' -Headers $managementHeaders -Method Get
$developmentProjects = @($projects | Where-Object { $_.id -eq $projectRef -or $_.ref -eq $projectRef })
if ($developmentProjects.Count -ne 1) {
    throw 'Supabase management returned an unexpected development-project result.'
}
$developmentProject = $developmentProjects[0]
if ($developmentProject.name -ne 'Electronic Logbook Development') {
    throw 'Configured development project does not match the expected project name.'
}
if ($developmentProject.region -ne 'ap-southeast-2') {
    throw 'Hosted recovery rehearsal requires the Sydney development project.'
}
if ($developmentProject.status -ne 'ACTIVE_HEALTHY') {
    throw 'Hosted recovery rehearsal requires the development project to be active and healthy.'
}

$authConfig = Invoke-RestMethod `
    -Uri "https://api.supabase.com/v1/projects/$projectRef/config/auth" `
    -Headers $managementHeaders `
    -Method Get
if ($authConfig.disable_signup -ne $true) {
    throw 'Hosted recovery rehearsal requires public Auth signup to be disabled.'
}
if ($authConfig.external_email_enabled -ne $true) {
    throw 'Hosted recovery rehearsal requires invited-user email sign-in.'
}
$enabledExternalProviders = @(
    $authConfig.PSObject.Properties |
        Where-Object { $_.Name -match '^external_(.+)_enabled$' -and $_.Value -eq $true } |
        ForEach-Object { [regex]::Match($_.Name, '^external_(.+)_enabled$').Groups[1].Value }
)
if (@($enabledExternalProviders).Count -ne 1 -or $enabledExternalProviders[0] -ne 'email') {
    throw 'Hosted recovery rehearsal requires email to be the only enabled external Auth provider.'
}

if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    $EvidenceDirectory = Join-Path $repoRoot ('artifacts\flightlogx-preview-hosted-recovery-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$EvidenceDirectory = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
$evidencePath = Join-Path $EvidenceDirectory 'verification.json'

$previousSupabaseAccessToken = $env:SUPABASE_ACCESS_TOKEN
$env:SUPABASE_ACCESS_TOKEN = $managementToken
$keys = @(& $supabase.Source projects api-keys --project-ref $projectRef --output json | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0) {
    throw 'Could not read the development project API keys.'
}
$anonKey = [string](($keys | Where-Object name -eq 'anon' | Select-Object -First 1).api_key)
$serviceRoleKey = [string](($keys | Where-Object name -eq 'service_role' | Select-Object -First 1).api_key)
if ([string]::IsNullOrWhiteSpace($anonKey) -or [string]::IsNullOrWhiteSpace($serviceRoleKey)) {
    throw 'The development project did not expose legacy anon and service_role keys required by the mobile Preview client.'
}

try {
    $env:ELB_REHEARSAL_SUPABASE_URL = "https://$projectRef.supabase.co"
    $env:ELB_REHEARSAL_ANON_KEY = $anonKey
    $env:ELB_REHEARSAL_SERVICE_ROLE_KEY = $serviceRoleKey
    $env:ELB_REHEARSAL_EVIDENCE_PATH = $evidencePath
    $env:ELB_REHEARSAL_PSQL_PATH = $psqlExecutablePath
    $env:ELB_REHEARSAL_DB_HOST = 'aws-0-ap-southeast-2.pooler.supabase.com'
    $env:ELB_REHEARSAL_DB_USER = "postgres.$projectRef"
    $env:ELB_REHEARSAL_DB_PASSWORD = [string]$metadata.development.db_password
    $env:ELB_REHEARSAL_WORKBOOK_CLIENT = if ($WorkbookClientInvestigation) { '1' } else { '0' }
    $env:ELB_REHEARSAL_WORKBOOK_MIGRATION = if ($WorkbookMigrationJourney) { '1' } else { '0' }

    Write-Host $(if ($WorkbookMigrationJourney) {
        'Running the disposable workbook-migration-to-clean-Android journey.'
    } elseif ($WorkbookClientInvestigation) {
        'Running a disposable workbook-connection-client investigation.'
    } else {
        'Running a disposable hosted recovery rehearsal.'
    }) -ForegroundColor Cyan
    Write-Host 'Secrets and disposable identifiers are held only in process memory; evidence is redacted.' -ForegroundColor Yellow
    & dotnet run --project $projectPath --no-launch-profile
    if ($LASTEXITCODE -ne 0) {
        throw 'Hosted recovery rehearsal failed. Review the redacted evidence file.'
    }
}
finally {
    Remove-Item Env:ELB_REHEARSAL_SUPABASE_URL -ErrorAction SilentlyContinue
    Remove-Item Env:ELB_REHEARSAL_ANON_KEY -ErrorAction SilentlyContinue
    Remove-Item Env:ELB_REHEARSAL_SERVICE_ROLE_KEY -ErrorAction SilentlyContinue
    Remove-Item Env:ELB_REHEARSAL_EVIDENCE_PATH -ErrorAction SilentlyContinue
    Remove-Item Env:ELB_REHEARSAL_PSQL_PATH -ErrorAction SilentlyContinue
    Remove-Item Env:ELB_REHEARSAL_DB_HOST -ErrorAction SilentlyContinue
    Remove-Item Env:ELB_REHEARSAL_DB_USER -ErrorAction SilentlyContinue
    Remove-Item Env:ELB_REHEARSAL_DB_PASSWORD -ErrorAction SilentlyContinue
    Remove-Item Env:ELB_REHEARSAL_WORKBOOK_CLIENT -ErrorAction SilentlyContinue
    Remove-Item Env:ELB_REHEARSAL_WORKBOOK_MIGRATION -ErrorAction SilentlyContinue
    if ($null -eq $previousSupabaseAccessToken) {
        Remove-Item Env:SUPABASE_ACCESS_TOKEN -ErrorAction SilentlyContinue
    } else {
        $env:SUPABASE_ACCESS_TOKEN = $previousSupabaseAccessToken
    }
}

Write-Host "Redacted evidence: $evidencePath" -ForegroundColor Green
