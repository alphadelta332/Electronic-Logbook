param(
    [string]$DeviceLabel = "Pixel8",
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\mobile-real-device-acceptance-20260722')
)

$ErrorActionPreference = 'Stop'

$remoteDirectory = '/sdcard/Android/data/com.alphadelta.electroniclogbook/files/exports'
$remoteLines = @(& adb shell "ls -t $remoteDirectory/*.elogbook 2>/dev/null")
$remoteFile = $remoteLines |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -First 1

if ([string]::IsNullOrWhiteSpace($remoteFile)) {
    throw "No exported .elogbook file was found under $remoteDirectory. Export from the app first."
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$outputPath = Join-Path $OutputDirectory "$DeviceLabel-Mobile-Export.elogbook"
& adb pull $remoteFile $outputPath
if ($LASTEXITCODE -ne 0) {
    throw "adb pull failed for $remoteFile."
}

[pscustomobject]@{
    DeviceLabel = $DeviceLabel
    RemotePath = $remoteFile
    LocalPath = (Resolve-Path $outputPath).Path
} | ConvertTo-Json -Depth 3
