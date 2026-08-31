Set-StrictMode -Version Latest

function Find-PreviewJavaKeyTool {
    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($env:JAVA_HOME)) {
        $candidates += Join-Path $env:JAVA_HOME "bin\keytool.exe"
    }

    $command = Get-Command "keytool.exe" -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        $candidates += $command.Source
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    throw "Java keytool was not found. Set JAVA_HOME to a JDK before building the Preview APK."
}

function New-PreviewSigningSecret {
    [byte[]] $bytes = New-Object byte[] 32
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($bytes)
    }
    finally {
        $generator.Dispose()
    }

    return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Get-PreviewSigningFingerprint {
    param(
        [Parameter(Mandatory = $true)] [string] $KeyTool,
        [Parameter(Mandatory = $true)] [string] $KeystorePath,
        [Parameter(Mandatory = $true)] [string] $KeyAlias
    )

    $output = & $KeyTool @(
        "-list",
        "-v",
        "-keystore", $KeystorePath,
        "-storetype", "PKCS12",
        "-storepass:env", "ELECTRONIC_LOGBOOK_PREVIEW_STORE_PASSWORD",
        "-alias", $KeyAlias) 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "The permanent FlightLogX Preview keystore could not be opened."
    }

    $fingerprintLine = $output | Where-Object { $_ -match "SHA256:\s*([0-9A-F:]+)" } | Select-Object -First 1
    if ($null -eq $fingerprintLine -or $fingerprintLine -notmatch "SHA256:\s*([0-9A-F:]+)") {
        throw "The SHA-256 certificate fingerprint could not be read from the Preview keystore."
    }

    return $Matches[1].Replace(":", "").ToLowerInvariant()
}

function Initialize-AndroidPreviewSigning {
    if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        throw "LOCALAPPDATA is required to store the permanent FlightLogX Preview signing identity."
    }

    $signingRoot = Join-Path $env:LOCALAPPDATA "ElectronicLogbook\AndroidSigning"
    # These protected filenames and the key alias are permanent legacy identifiers. Renaming
    # or regenerating them would risk breaking updates to already-installed production-ID apps.
    $keystorePath = Join-Path $signingRoot "flightlogx-pilot.keystore"
    $credentialsPath = Join-Path $signingRoot "flightlogx-pilot-credentials.json"
    $metadataPath = Join-Path $signingRoot "flightlogx-pilot-signing.json"
    $keyAlias = "flightlogxpilot"
    $statePaths = @($keystorePath, $credentialsPath, $metadataPath)
    $existingStateCount = @($statePaths | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }).Count
    $keyTool = Find-PreviewJavaKeyTool

    if ($existingStateCount -ne 0 -and $existingStateCount -ne $statePaths.Count) {
        throw "The permanent Preview signing identity is incomplete. Do not regenerate it; restore the missing AndroidSigning file from the trusted transfer archive."
    }

    if ($existingStateCount -eq 0) {
        New-Item -ItemType Directory -Path $signingRoot -Force | Out-Null
        $password = New-PreviewSigningSecret
        $env:ELECTRONIC_LOGBOOK_PREVIEW_STORE_PASSWORD = $password
        $env:ELECTRONIC_LOGBOOK_PREVIEW_KEY_PASSWORD = $password

        $temporaryKeystore = Join-Path $signingRoot ("flightlogx-preview-{0}.tmp" -f [Guid]::NewGuid().ToString("N"))
        try {
            & $keyTool @(
                "-genkeypair",
                "-keystore", $temporaryKeystore,
                "-storetype", "PKCS12",
                "-storepass:env", "ELECTRONIC_LOGBOOK_PREVIEW_STORE_PASSWORD",
                "-alias", $keyAlias,
                "-keypass:env", "ELECTRONIC_LOGBOOK_PREVIEW_KEY_PASSWORD",
                "-dname", "CN=FlightLogX Preview,O=Alpha Delta,C=AU",
                "-keyalg", "RSA",
                "-keysize", "4096",
                "-validity", "36500")
            if ($LASTEXITCODE -ne 0) {
                throw "Could not create the permanent FlightLogX Preview signing identity."
            }

            Move-Item -LiteralPath $temporaryKeystore -Destination $keystorePath
        }
        finally {
            if (Test-Path -LiteralPath $temporaryKeystore -PathType Leaf) {
                Remove-Item -LiteralPath $temporaryKeystore -Force
            }
        }

        [ordered]@{
            schemaVersion = 1
            keyAlias = $keyAlias
            storePassword = $password
            keyPassword = $password
        } | ConvertTo-Json | Set-Content -LiteralPath $credentialsPath -Encoding UTF8

        $fingerprint = Get-PreviewSigningFingerprint -KeyTool $keyTool -KeystorePath $keystorePath -KeyAlias $keyAlias
        [ordered]@{
            schemaVersion = 1
            purpose = "Permanent FlightLogX Preview Android app signing identity"
            packageName = "com.alphadelta.electroniclogbook"
            keystorePath = $keystorePath
            keyAlias = $keyAlias
            certificateSha256 = $fingerprint
            keyAlgorithm = "RSA-4096"
            validityDays = 36500
            createdAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
            updatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        } | ConvertTo-Json | Set-Content -LiteralPath $metadataPath -Encoding UTF8
    }

    try {
        $credentials = Get-Content -LiteralPath $credentialsPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $metadata = Get-Content -LiteralPath $metadataPath -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        throw "The permanent Preview signing credentials or metadata file is invalid. Restore it instead of regenerating the key."
    }

    if ($credentials.schemaVersion -ne 1 `
        -or [string]::IsNullOrWhiteSpace($credentials.storePassword) `
        -or [string]::IsNullOrWhiteSpace($credentials.keyPassword) `
        -or $credentials.keyAlias -ne $keyAlias) {
        throw "The permanent Preview signing credentials are incomplete or unsupported."
    }

    if ($metadata.schemaVersion -ne 1 `
        -or $metadata.packageName -ne "com.alphadelta.electroniclogbook" `
        -or $metadata.keyAlias -ne $keyAlias `
        -or [string]::IsNullOrWhiteSpace($metadata.certificateSha256)) {
        throw "The permanent Preview signing metadata does not match the FlightLogX production package."
    }

    $env:ELECTRONIC_LOGBOOK_PREVIEW_KEYSTORE = $keystorePath
    $env:ELECTRONIC_LOGBOOK_PREVIEW_STORE_PASSWORD = $credentials.storePassword
    $env:ELECTRONIC_LOGBOOK_PREVIEW_KEY_ALIAS = $credentials.keyAlias
    $env:ELECTRONIC_LOGBOOK_PREVIEW_KEY_PASSWORD = $credentials.keyPassword

    $fingerprint = Get-PreviewSigningFingerprint -KeyTool $keyTool -KeystorePath $keystorePath -KeyAlias $keyAlias
    if ($fingerprint -ne $metadata.certificateSha256) {
        throw "The permanent Preview signing certificate does not match its protected metadata. Do not build or distribute this APK."
    }

    return [pscustomobject]@{
        KeystorePath = $keystorePath
        CertificateSha256 = $fingerprint
        CredentialsPath = $credentialsPath
        MetadataPath = $metadataPath
        KeyAlias = $keyAlias
    }
}
