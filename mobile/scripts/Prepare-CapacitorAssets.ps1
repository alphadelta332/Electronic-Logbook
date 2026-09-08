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

$publishedFrameworkRoot = Join-Path $publishRoot "_framework"
if (Test-Path -LiteralPath (Join-Path $publishedFrameworkRoot "blazor.boot.json") -PathType Leaf) {
    throw "Published Blazor assets contain an obsolete boot manifest. Run npm.cmd run publish:pwa to create a clean publish."
}
$publishedFirstPartyAssemblies = @(Get-ChildItem -LiteralPath $publishedFrameworkRoot -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^ElectronicLogbook\.(Mobile|Portable).*\.wasm$' })
$unexpectedPublishedAssemblies = @($publishedFirstPartyAssemblies | Where-Object {
    $_.Name -notmatch '^ElectronicLogbook\.(Mobile|Portable)\.[a-z0-9]{10}\.wasm$'
})
if ($publishedFirstPartyAssemblies.Count -ne 2 -or $unexpectedPublishedAssemblies.Count -gt 0) {
    $names = @($publishedFirstPartyAssemblies | Select-Object -ExpandProperty Name)
    throw "Published Blazor assets contain mixed or stale first-party assemblies: $($names -join ', ')"
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
