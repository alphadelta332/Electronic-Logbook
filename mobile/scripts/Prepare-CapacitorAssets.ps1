$ErrorActionPreference = "Stop"

$scriptRoot = $PSScriptRoot
$mobileRoot = Split-Path -Parent $scriptRoot
$sourceRoot = Join-Path $mobileRoot "src\ElectronicLogbook.Mobile\wwwroot"
$publishRoot = Join-Path $mobileRoot "artifacts\pages\wwwroot"
$assetRoot = Join-Path $mobileRoot "artifacts\capacitor"

if (-not (Test-Path -LiteralPath $sourceRoot)) {
    throw "Source web assets were not found at $sourceRoot"
}

if (-not (Test-Path -LiteralPath $publishRoot)) {
    throw "Published Blazor assets were not found at $publishRoot"
}

$expectedRoot = (Resolve-Path -LiteralPath $mobileRoot).Path
$resolvedAssetRoot = if (Test-Path -LiteralPath $assetRoot) {
    (Resolve-Path -LiteralPath $assetRoot).Path
}
else {
    $assetRoot
}

if (-not $resolvedAssetRoot.StartsWith($expectedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to prepare assets outside the mobile workspace: $resolvedAssetRoot"
}

if (Test-Path -LiteralPath $assetRoot) {
    Get-ChildItem -LiteralPath $assetRoot -Force | ForEach-Object {
        Remove-Item -LiteralPath $_.FullName -Recurse -Force
    }
}
else {
    New-Item -ItemType Directory -Force -Path $assetRoot | Out-Null
}

foreach ($item in Get-ChildItem -LiteralPath $sourceRoot -Force) {
    $destination = Join-Path $assetRoot $item.Name
    if ($item.PSIsContainer -and (Test-Path -LiteralPath $destination)) {
        Copy-Item -Path (Join-Path $item.FullName "*") -Destination $destination -Recurse -Force
    }
    else {
        Copy-Item -LiteralPath $item.FullName -Destination $destination -Recurse -Force
    }
}

foreach ($item in Get-ChildItem -LiteralPath $publishRoot -Force) {
    $destination = Join-Path $assetRoot $item.Name
    if ($item.PSIsContainer -and (Test-Path -LiteralPath $destination)) {
        Copy-Item -Path (Join-Path $item.FullName "*") -Destination $destination -Recurse -Force
    }
    else {
        Copy-Item -LiteralPath $item.FullName -Destination $destination -Recurse -Force
    }
}

Get-ChildItem -LiteralPath $assetRoot -Recurse -File |
    Where-Object { $_.Extension -eq ".gz" -or $_.Extension -eq ".br" } |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }

if (-not (Test-Path -LiteralPath (Join-Path $assetRoot "index.html"))) {
    throw "Prepared Android assets do not contain index.html"
}
