param(
    [string]$DeviceLabel = "Pixel8",
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\mobile-real-device-acceptance-20260722'),
    [string]$PackageId
)

$ErrorActionPreference = 'Stop'

$knownPackageIds = @(
    'com.alphadelta.electroniclogbook.dev',
    'com.alphadelta.electroniclogbook'
)

if ([string]::IsNullOrWhiteSpace($PackageId)) {
    $PackageId = $knownPackageIds |
        Where-Object {
            @(& adb shell pm list packages --user 0 $_) -contains "package:$_"
        } |
        Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($PackageId)) {
        throw "Neither the development nor production Electronic Logbook Android app is installed. Install an app before receiving an export."
    }
}

$remoteDirectory = "/sdcard/Android/data/$PackageId/files/exports"
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
    PackageId = $PackageId
    RemotePath = $remoteFile
    LocalPath = (Resolve-Path $outputPath).Path
} | ConvertTo-Json -Depth 3
