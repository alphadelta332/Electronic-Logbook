$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$mobileRoot = Split-Path -Parent $scriptRoot
$projectPath = Join-Path $mobileRoot "src\ElectronicLogbook.Mobile\ElectronicLogbook.Mobile.csproj"
$publishRoot = Join-Path $mobileRoot "artifacts\pages"
$expectedPublishRoot = [IO.Path]::GetFullPath($publishRoot).TrimEnd('\')
$expectedMobileRoot = [IO.Path]::GetFullPath($mobileRoot).TrimEnd('\') + '\'

if (-not $expectedPublishRoot.StartsWith($expectedMobileRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean a publish directory outside the mobile workspace: $expectedPublishRoot"
}

if (Test-Path -LiteralPath $publishRoot) {
    $resolvedPublishRoot = (Resolve-Path -LiteralPath $publishRoot).Path.TrimEnd('\')
    if (-not [string]::Equals(
            $resolvedPublishRoot,
            $expectedPublishRoot,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean an unexpected mobile publish directory: $resolvedPublishRoot"
    }

    Get-ChildItem -LiteralPath $resolvedPublishRoot -Force | ForEach-Object {
        Remove-Item -LiteralPath $_.FullName -Recurse -Force
    }
}
else {
    New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
}

& dotnet publish $projectPath -c Release -o $publishRoot
if ($LASTEXITCODE -ne 0) {
    throw "Mobile PWA publish failed."
}

$frameworkRoot = Join-Path $publishRoot "wwwroot\_framework"
if (-not (Test-Path -LiteralPath $frameworkRoot -PathType Container)) {
    throw "Mobile PWA publish did not produce Blazor framework assets."
}

$staleBootManifest = Join-Path $frameworkRoot "blazor.boot.json"
if (Test-Path -LiteralPath $staleBootManifest -PathType Leaf) {
    throw "Mobile PWA publish retained the obsolete Blazor boot manifest: $staleBootManifest"
}

$firstPartyAssemblies = @(Get-ChildItem -LiteralPath $frameworkRoot -File |
    Where-Object { $_.Name -match '^ElectronicLogbook\.(Mobile|Portable).*\.wasm$' })
$unexpectedAssemblies = @($firstPartyAssemblies | Where-Object {
    $_.Name -notmatch '^ElectronicLogbook\.(Mobile|Portable)\.[a-z0-9]{10}\.wasm$'
})
if ($firstPartyAssemblies.Count -ne 2 -or $unexpectedAssemblies.Count -gt 0) {
    $names = @($firstPartyAssemblies | Select-Object -ExpandProperty Name)
    throw "Mobile PWA publish contains mixed or stale first-party assemblies: $($names -join ', ')"
}

$serviceWorkerAssetsPath = Join-Path $publishRoot "wwwroot\service-worker-assets.js"
if (-not (Test-Path -LiteralPath $serviceWorkerAssetsPath -PathType Leaf)) {
    throw "Mobile PWA publish did not produce its service-worker asset manifest."
}
$serviceWorkerAssets = Get-Content -LiteralPath $serviceWorkerAssetsPath -Raw -Encoding UTF8
foreach ($assembly in $firstPartyAssemblies) {
    if ($serviceWorkerAssets.IndexOf(
            "_framework/$($assembly.Name)",
            [StringComparison]::Ordinal) -lt 0) {
        throw "The service-worker asset manifest does not reference $($assembly.Name)."
    }
}

Write-Host "Mobile PWA clean publish verified."
Write-Host "Output: $publishRoot"
