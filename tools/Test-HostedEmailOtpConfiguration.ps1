# Verifies hosted email OTP settings without printing credentials or template contents.

[CmdletBinding()]
param(
    [ValidateSet("development", "privatePilot")]
    [string]$Environment = "development",
    [string]$LocalSupabaseRoot = (Join-Path $env:LOCALAPPDATA "ElectronicLogbook\Supabase")
)

$ErrorActionPreference = "Stop"

$metadataPath = Join-Path $LocalSupabaseRoot "hosted-pilot-projects.local.json"
$tokenPath = Join-Path $LocalSupabaseRoot "access-token.txt"
if (-not (Test-Path -LiteralPath $metadataPath -PathType Leaf)) {
    throw "Hosted project metadata is not configured."
}
if (-not (Test-Path -LiteralPath $tokenPath -PathType Leaf)) {
    throw "Supabase management access is not configured."
}

$metadata = Get-Content -LiteralPath $metadataPath -Raw -Encoding UTF8 | ConvertFrom-Json
$project = $metadata.$Environment
if ($null -eq $project -or [string]::IsNullOrWhiteSpace($project.project_ref)) {
    throw "The selected hosted project is not configured."
}

$managementToken = (Get-Content -LiteralPath $tokenPath -Raw -Encoding UTF8).Trim()
if ([string]::IsNullOrWhiteSpace($managementToken)) {
    throw "Supabase management access is not configured."
}

$authConfig = Invoke-RestMethod `
    -Method Get `
    -Uri ("https://api.supabase.com/v1/projects/{0}/config/auth" -f $project.project_ref) `
    -Headers @{ Authorization = "Bearer $managementToken" }

$expectedSenderEmail = if ($Environment -eq "development") {
    "signin@auth-dev.flightlogx.app"
} else {
    "signin@auth.flightlogx.app"
}
$template = [string]$authConfig.mailer_templates_magic_link_content
$checks = [ordered]@{
    "Resend SMTP host" = $authConfig.smtp_host -eq "smtp.resend.com"
    "Resend SMTP TLS port" = [int]$authConfig.smtp_port -eq 465
    "FlightLogX sender name" = $authConfig.smtp_sender_name -eq "FlightLogX"
    "environment-specific sender email" = $authConfig.smtp_admin_email -eq $expectedSenderEmail
    "OTP token template" = $template -match "\{\{\s*\.Token\s*\}\}"
    "no confirmation link in normal email" = $template -notmatch "ConfirmationURL|TokenHash"
    "six-digit OTP length" = [int]$authConfig.mailer_otp_length -eq 6
    "ten-minute OTP expiry" = [int]$authConfig.mailer_otp_exp -eq 600
    "30 email sends per hour" = [int]$authConfig.rate_limit_email_sent -eq 30
    "30 OTP requests per hour" = [int]$authConfig.rate_limit_otp -eq 30
}

foreach ($check in $checks.GetEnumerator()) {
    $result = if ($check.Value) { "PASS" } else { "FAIL" }
    Write-Host ("{0}: {1}" -f $result, $check.Key)
}

if ($checks.Values -contains $false) {
    throw "Hosted email OTP configuration does not match the FlightLogX requirements. No credentials or template contents were printed."
}

Write-Host ("Hosted email OTP configuration passed for {0}. No credentials or template contents were printed." -f $Environment)
