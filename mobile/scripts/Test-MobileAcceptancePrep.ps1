param(
    [string] $EvidenceOutputPath
)

$ErrorActionPreference = "Stop"

$scriptRoot = $PSScriptRoot
$mobileRoot = Split-Path -Parent $scriptRoot
$repoRoot = Split-Path -Parent $mobileRoot
$capacitorConfigPath = Join-Path $mobileRoot "capacitor.config.json"
$preparedAssetRoot = Join-Path $mobileRoot "artifacts\capacitor"
$androidAssetRoot = Join-Path $mobileRoot "android\app\src\main\assets\public"
$apkRoot = Join-Path $mobileRoot "android\app\build\outputs\apk\debug"
$apkPath = Join-Path $apkRoot "app-debug.apk"
$metadataPath = Join-Path $apkRoot "output-metadata.json"

if ([string]::IsNullOrWhiteSpace($EvidenceOutputPath)) {
    $EvidenceOutputPath = Join-Path $repoRoot "artifacts\mobile-real-device-acceptance-20260722\acceptance-prep-result.json"
}

function Assert-FileExists {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,
        [Parameter(Mandatory = $true)]
        [string] $Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description was not found at $Path"
    }
}

function Assert-DirectoryExists {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,
        [Parameter(Mandatory = $true)]
        [string] $Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "$Description was not found at $Path"
    }
}

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Actual,
        [Parameter(Mandatory = $true)]
        [object] $Expected,
        [Parameter(Mandatory = $true)]
        [string] $Description
    )

    if ($Actual -ne $Expected) {
        throw "$Description was '$Actual'; expected '$Expected'"
    }
}

function Get-Sha256Hash {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        try {
            $hash = $sha256.ComputeHash($stream)
            return -join ($hash | ForEach-Object { $_.ToString("x2", [System.Globalization.CultureInfo]::InvariantCulture) })
        }
        finally {
            $sha256.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

Assert-FileExists -Path $capacitorConfigPath -Description "Capacitor configuration"
Assert-DirectoryExists -Path $preparedAssetRoot -Description "Prepared Capacitor web assets"
Assert-DirectoryExists -Path $androidAssetRoot -Description "Android embedded web assets"
Assert-FileExists -Path $apkPath -Description "Android debug APK"
Assert-FileExists -Path $metadataPath -Description "Android APK metadata"

$config = Get-Content -LiteralPath $capacitorConfigPath -Raw | ConvertFrom-Json
Assert-Equal -Actual $config.appId -Expected "com.alphadelta.electroniclogbook" -Description "Capacitor appId"
Assert-Equal -Actual $config.appName -Expected "Electronic Logbook" -Description "Capacitor appName"
Assert-Equal -Actual $config.webDir -Expected "artifacts/capacitor" -Description "Capacitor webDir"
Assert-Equal -Actual $config.server.androidScheme -Expected "https" -Description "Capacitor Android scheme"

$metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
Assert-Equal -Actual $metadata.artifactType.type -Expected "APK" -Description "Android artifact type"
Assert-Equal -Actual $metadata.applicationId -Expected "com.alphadelta.electroniclogbook" -Description "Android applicationId"
Assert-Equal -Actual $metadata.variantName -Expected "debug" -Description "Android build variant"
Assert-Equal -Actual $metadata.elements[0].outputFile -Expected "app-debug.apk" -Description "Android APK output file"

$apkLength = (Get-Item -LiteralPath $apkPath).Length
if ($apkLength -lt 1MB) {
    throw "Android debug APK is unexpectedly small: $apkLength bytes"
}

$requiredAssets = @(
    "index.html",
    "manifest.webmanifest",
    "service-worker.js",
    "service-worker.published.js",
    "service-worker-assets.js",
    "js\logbookStore.js",
    "css\app.css",
    "icon-192.png",
    "icon-512.png"
)

$verifiedAssets = @()
foreach ($relativePath in $requiredAssets) {
    $prepared = Join-Path $preparedAssetRoot $relativePath
    $embedded = Join-Path $androidAssetRoot $relativePath
    Assert-FileExists -Path $prepared -Description "Prepared asset $relativePath"
    Assert-FileExists -Path $embedded -Description "Android embedded asset $relativePath"

    $preparedHash = Get-Sha256Hash -Path $prepared
    $embeddedHash = Get-Sha256Hash -Path $embedded
    Assert-Equal -Actual $embeddedHash -Expected $preparedHash -Description "Android embedded asset hash for $relativePath"

    $verifiedAssets += [PSCustomObject]@{
        relativePath = $relativePath
        sha256 = $embeddedHash
        bytes = (Get-Item -LiteralPath $embedded).Length
    }
}

Assert-DirectoryExists -Path (Join-Path $androidAssetRoot "_framework") -Description "Android embedded Blazor framework assets"

$compressedAssets = Get-ChildItem -LiteralPath $androidAssetRoot -Recurse -File |
    Where-Object { $_.Extension -eq ".gz" -or $_.Extension -eq ".br" }
if ($compressedAssets.Count -gt 0) {
    $paths = $compressedAssets | Select-Object -ExpandProperty FullName
    throw "Android embedded assets include compressed files that Capacitor should omit: $($paths -join ', ')"
}

$manifestPath = Join-Path $androidAssetRoot "manifest.webmanifest"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
Assert-Equal -Actual $manifest.name -Expected "Electronic Logbook" -Description "Web manifest name"
Assert-Equal -Actual $manifest.display -Expected "standalone" -Description "Web manifest display mode"
Assert-Equal -Actual $manifest.prefer_related_applications -Expected $false -Description "Web manifest related-app preference"
Assert-Equal -Actual $manifest.start_url -Expected "./" -Description "Web manifest start URL"
Assert-Equal -Actual $manifest.scope -Expected "./" -Description "Web manifest scope"

$iconSizes = @($manifest.icons | ForEach-Object { $_.sizes })
if ($iconSizes -notcontains "192x192" -or $iconSizes -notcontains "512x512") {
    throw "Web manifest does not contain both 192x192 and 512x512 icons."
}

$evidenceDirectory = Split-Path -Parent $EvidenceOutputPath
if (-not [string]::IsNullOrWhiteSpace($evidenceDirectory) -and -not (Test-Path -LiteralPath $evidenceDirectory)) {
    New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
}

$evidence = [PSCustomObject]@{
    verifiedAt = [DateTimeOffset]::UtcNow.ToString("O", [System.Globalization.CultureInfo]::InvariantCulture)
    result = "passed"
    app = [PSCustomObject]@{
        appId = $config.appId
        appName = $config.appName
        webDir = $config.webDir
        androidScheme = $config.server.androidScheme
    }
    apk = [PSCustomObject]@{
        path = $apkPath
        sha256 = Get-Sha256Hash -Path $apkPath
        bytes = $apkLength
        artifactType = $metadata.artifactType.type
        applicationId = $metadata.applicationId
        variantName = $metadata.variantName
        outputFile = $metadata.elements[0].outputFile
        minSdkVersionForDexing = $metadata.minSdkVersionForDexing
    }
    webManifest = [PSCustomObject]@{
        name = $manifest.name
        display = $manifest.display
        startUrl = $manifest.start_url
        scope = $manifest.scope
        preferRelatedApplications = $manifest.prefer_related_applications
        iconSizes = $iconSizes
    }
    embeddedAssetRoot = $androidAssetRoot
    verifiedAssets = $verifiedAssets
}

$evidenceJson = $evidence | ConvertTo-Json -Depth 8
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($EvidenceOutputPath, $evidenceJson, $utf8WithoutBom)

Write-Host "Mobile acceptance prep verified."
Write-Host "APK: $apkPath"
Write-Host "Embedded assets: $androidAssetRoot"
Write-Host "Evidence: $EvidenceOutputPath"
