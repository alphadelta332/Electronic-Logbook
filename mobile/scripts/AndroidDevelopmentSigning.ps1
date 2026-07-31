Set-StrictMode -Version Latest

function Find-JavaKeyTool {
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

    throw "Java keytool was not found. Set JAVA_HOME to a JDK before building Android packages."
}

function Initialize-AndroidDevelopmentSigning {
    if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        throw "LOCALAPPDATA is required to store the durable development signing identity."
    }

    $signingRoot = Join-Path $env:LOCALAPPDATA "ElectronicLogbook\AndroidSigning"
    $keystorePath = Join-Path $signingRoot "electronic-logbook-development.keystore"
    $metadataPath = Join-Path $signingRoot "electronic-logbook-development.json"
    $keyAlias = "androiddebugkey"
    $password = "android"
    $keyTool = Find-JavaKeyTool

    if (-not (Test-Path -LiteralPath $keystorePath -PathType Leaf)) {
        New-Item -ItemType Directory -Path $signingRoot -Force | Out-Null
        $standardDebugKeystore = Join-Path $env:USERPROFILE ".android\debug.keystore"

        if (Test-Path -LiteralPath $standardDebugKeystore -PathType Leaf) {
            Copy-Item -LiteralPath $standardDebugKeystore -Destination $keystorePath
            $origin = "copied-from-existing-android-debug-keystore"
        }
        else {
            & $keyTool @(
                "-genkeypair",
                "-keystore", $keystorePath,
                "-storepass", $password,
                "-alias", $keyAlias,
                "-keypass", $password,
                "-dname", "CN=Electronic Logbook Development,O=AlphaDelta,C=AU",
                "-keyalg", "RSA",
                "-keysize", "2048",
                "-validity", "36500")
            if ($LASTEXITCODE -ne 0) {
                throw "Could not create the durable Android development signing identity."
            }

            $origin = "generated"
        }
    }
    else {
        $origin = "existing"
    }

    $keyToolOutput = & $keyTool @(
        "-list",
        "-v",
        "-keystore", $keystorePath,
        "-storepass", $password,
        "-alias", $keyAlias) 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "The durable Android development keystore could not be opened: $keystorePath"
    }

    $fingerprintLine = $keyToolOutput | Where-Object { $_ -match "SHA256:\s*([0-9A-F:]+)" } | Select-Object -First 1
    if ($null -eq $fingerprintLine -or $fingerprintLine -notmatch "SHA256:\s*([0-9A-F:]+)") {
        throw "The SHA-256 certificate fingerprint could not be read from the development keystore."
    }

    $fingerprint = $Matches[1].Replace(":", "").ToLowerInvariant()
    $env:ELECTRONIC_LOGBOOK_DEV_KEYSTORE = $keystorePath
    $env:ELECTRONIC_LOGBOOK_DEV_STORE_PASSWORD = $password
    $env:ELECTRONIC_LOGBOOK_DEV_KEY_ALIAS = $keyAlias
    $env:ELECTRONIC_LOGBOOK_DEV_KEY_PASSWORD = $password

    [ordered]@{
        schemaVersion = 1
        purpose = "Electronic Logbook development and acceptance builds only"
        keystorePath = $keystorePath
        certificateSha256 = $fingerprint
        origin = $origin
        updatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    } | ConvertTo-Json | Set-Content -LiteralPath $metadataPath -Encoding UTF8

    return [pscustomobject]@{
        KeystorePath = $keystorePath
        CertificateSha256 = $fingerprint
        MetadataPath = $metadataPath
    }
}
