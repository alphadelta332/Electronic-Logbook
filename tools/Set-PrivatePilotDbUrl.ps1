# Stores the private-pilot database URL in the Windows user environment.
# The value is intentionally not printed.

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ConnectionString
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw "ConnectionString is required."
}

if ($ConnectionString -match "YOUR-PASSWORD|YOUR_REAL_DATABASE_PASSWORD|PASTE_|xxxxx") {
    throw "ConnectionString still appears to contain a placeholder. Replace it with the real database password/string first."
}

if ($ConnectionString -notmatch "^postgres(ql)?://") {
    throw "ConnectionString should start with postgresql:// or postgres://."
}

[Environment]::SetEnvironmentVariable(
    "ELB_SUPABASE_PILOT_DB_URL",
    $ConnectionString,
    [EnvironmentVariableTarget]::User)

$saved = [Environment]::GetEnvironmentVariable(
    "ELB_SUPABASE_PILOT_DB_URL",
    [EnvironmentVariableTarget]::User)

if ([string]::IsNullOrWhiteSpace($saved)) {
    throw "ELB_SUPABASE_PILOT_DB_URL was not saved."
}

Write-Host "ELB_SUPABASE_PILOT_DB_URL saved to the Windows user environment." -ForegroundColor Green
Write-Host "The connection string was not printed." -ForegroundColor Yellow
