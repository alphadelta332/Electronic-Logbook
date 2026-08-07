# Builds and stores the private-pilot Supabase Session Pooler URL from the DB password.
# This avoids direct-connection IPv6 issues and URL-encodes special password characters.

[CmdletBinding()]
param(
    [string]$ProjectRef = "iyjkhayrymyxmzwgpsty",

    [string]$PoolerHost = "aws-0-ap-southeast-2.pooler.supabase.com",

    [int]$Port = 5432,

    [string]$DatabasePassword
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($DatabasePassword)) {
    $securePassword = Read-Host -Prompt "Supabase database password" -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
    try {
        $DatabasePassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        if ($bstr -ne [IntPtr]::Zero) {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
        }
    }
}

if ([string]::IsNullOrWhiteSpace($DatabasePassword)) {
    throw "Database password is required."
}

if ($DatabasePassword -match "YOUR-PASSWORD|YOUR_REAL_DATABASE_PASSWORD|<YOUR_DATABASE_PASSWORD>|PASTE_") {
    throw "Database password still appears to be a placeholder."
}

if ($DatabasePassword.Length -lt 8) {
    throw "Database password looks too short. Use the Supabase project database password, not a masked value or placeholder."
}

$encodedPassword = [Uri]::EscapeDataString($DatabasePassword)
$connectionString = "postgresql://postgres.${ProjectRef}:$encodedPassword@${PoolerHost}:$Port/postgres"

[Environment]::SetEnvironmentVariable(
    "ELB_SUPABASE_PILOT_DB_URL",
    $connectionString,
    [EnvironmentVariableTarget]::User)

Write-Host "ELB_SUPABASE_PILOT_DB_URL saved using the Supabase Session Pooler." -ForegroundColor Green
Write-Host "The password and connection string were not printed." -ForegroundColor Yellow
