# Contributing

Contributions are welcome, but this project has a cautious release process because the distributed asset is a macro-enabled Excel workbook.

## Development Setup

This is a Windows-focused project. The workbook and external updater rely on desktop
Excel COM automation, so the full validation workflow cannot run on macOS or Linux.

Install the following tools before working on the relevant area:

- Microsoft Excel for Windows (Microsoft 365 on Windows 11 is the primary supported
  environment; Excel 2021 and newer perpetual editions are supported).
- PowerShell 7 or Windows PowerShell for the repository scripts.
- .NET 10 SDK for the updater, portable package logic, tests, and Blazor mobile app.
  Keep the .NET 8 SDK installed for the recovery-envelope secret generator.
- Node.js with npm for the Capacitor Android shell. The committed
  `mobile/package-lock.json` pins its JavaScript dependencies.
- Android tooling only when building, installing, or USB-debugging the Android package
  on a Pixel or other device. Do not leave this until mobile data: the first setup is a
  few hundred megabytes even before Gradle and npm caches are populated.

### Mobile Android and Pixel USB Setup

The Blazor mobile PWA can be run in a desktop browser with only .NET and Node.js, but
normal Android behaviour over USB debugging requires all of the following on the laptop:

- Android Debug Bridge (`adb`). Install Android SDK Platform-Tools, for example:
  `winget install --id Google.PlatformTools --exact`.
- Java 21 JDK for the Android Gradle/Capacitor build. Temurin 21 is known to work:
  `winget install --id EclipseAdoptium.Temurin.21.JDK --exact`.
- Android SDK command-line tools or Android Studio with the SDK Manager.
- Android SDK Platform 36 and Android SDK Build-Tools 35.0.0, matching
  `mobile/android/variables.gradle`.
- Accepted Android SDK licenses. This is a one-time legal prompt; run it while online
  and accept only after reviewing the terms:
  `sdkmanager --licenses`.
- Pixel developer options enabled, USB debugging enabled, and the device authorization
  prompt accepted after connecting the USB cable.

If you use the command-line tools instead of Android Studio, a minimal setup is:

```powershell
$env:ANDROID_HOME = "$env:LOCALAPPDATA\Android\Sdk"
$env:JAVA_HOME = "C:\Program Files\Eclipse Adoptium\jdk-21"
$env:Path = "$env:JAVA_HOME\bin;$env:ANDROID_HOME\platform-tools;$env:ANDROID_HOME\cmdline-tools\latest\bin;$env:Path"

sdkmanager --sdk_root="$env:ANDROID_HOME" "platform-tools" "platforms;android-36" "build-tools;35.0.0"
sdkmanager --licenses
```

Approximate first-time download sizes, based on the current official Android package
metadata and tool installers:

| Tool or package | Approximate download |
| --- | ---: |
| Android SDK Command-line Tools for Windows | 156 MB |
| Android SDK Platform-Tools / `adb` | 18 MB installed; download is in the same small range |
| Android SDK Platform 36 | approximately 65-75 MB |
| Android SDK Build-Tools 35.0.0 for Windows | 60 MB |
| Temurin Java 21 JDK | roughly 170-220 MB |
| Gradle wrapper distribution on first Android build | roughly 150-170 MB |
| npm dependencies from `mobile/package-lock.json` | varies by cache, usually tens of MB |

Plan for roughly 600-750 MB on a clean laptop to reach `adb install` readiness without
Android Studio. Installing full Android Studio is larger and should be done on Wi-Fi.

For a plugged-in Pixel, verify the device before trying to install:

```powershell
adb devices
```

The device must show `device`, not `unauthorized`. If it is unauthorized, unlock the
Pixel and accept the USB debugging prompt.

Restore the dependencies from the repository root and mobile directory:

```powershell
dotnet restore ElectronicLogbook.Updater.sln
Set-Location mobile
npm ci
```

Build, sync, and install the Android debug app with the data-preserving installer:

```powershell
Set-Location mobile
npm run install:android:debug
```

Debug APKs install as `com.alphadelta.electroniclogbook.dev` while keeping the visible
app name `LogbookOne`. Release builds keep `com.alphadelta.electroniclogbook`. This
side-by-side debug application ID keeps development installs away from pilot or
release-test data without cluttering the launcher label.

The installer uses `adb install -r` and deliberately does not clear, uninstall, or reset
the package. Android only preserves installed app data across `adb install -r` when the
replacement APK has the same package name and signing key as the installed app. If
installation fails with `INSTALL_FAILED_UPDATE_INCOMPATIBLE`, the device already has
that package installed with a different signature. Export and verify any important local
logbook data before using a separate reset procedure. A normal uninstall clears the
app's private WebView and IndexedDB data.

For a quick browser-only Pixel check without building an APK, run the Blazor dev server
on the laptop, connect the authorized Pixel, and reverse the port:

```powershell
dotnet run --project mobile\src\ElectronicLogbook.Mobile\ElectronicLogbook.Mobile.csproj --urls http://localhost:5000
adb reverse tcp:5000 tcp:5000
```

Then open `http://127.0.0.1:5000/` in Chrome on the Pixel.

Return to the repository root before running the PowerShell validation scripts. No
Python environment or `requirements.txt` is needed: .NET dependencies are declared in
the project files and mobile dependencies are declared in `mobile/package.json`.

### Mobile Generated Assets

Keep `img/icon.png` as the tracked source icon. Do not commit
`mobile/src/ElectronicLogbook.Mobile/wwwroot/icon-192.png` or
`mobile/src/ElectronicLogbook.Mobile/wwwroot/icon-512.png`; they are generated by
`mobile/scripts/Generate-AppIcons.ps1` during the mobile build/sync workflow and should
remain local generated assets.

### Safe Local Checks

For ordinary source changes, start with the fast validation tier:

```powershell
.\tools\Invoke-Validation.ps1 -Tier Fast
```

Run focused .NET tests for updater or mobile changes:

```powershell
dotnet test updater\tests\ElectronicLogbook.Updater.Tests\ElectronicLogbook.Updater.Tests.csproj --no-restore
dotnet test mobile\tests\ElectronicLogbook.Mobile.Tests\ElectronicLogbook.Mobile.Tests.csproj
```

Use the Excel tier only on a disposable workbook copy when practical. It requires
desktop Excel, and the workbook under test must be closed. For a `dev`-channel workbook,
omit the release-only public-readiness check:

```powershell
.\tools\Invoke-Validation.ps1 -Tier Excel -SkipPublicReadinessCheck
```

## Before Opening a Pull Request

- Open an issue first for large changes or changes to the workbook update process.
- Keep every master-workbook VBA source change paired with the corresponding `Electronic_Logbook_Master.xlsm` change. After editing tracked VBA source, run `.\tools\ImportVbaIntoWorkbook.ps1 -WorkbookPath .\Electronic_Logbook_Master.xlsm`; string-only and other small VBA edits still require import. `tools\Test-VbaWorkbookPairing.ps1` enforces this before VBA source-quality and release metadata checks pass.
- Do not include personal logbook data, private tokens, or machine-specific paths.
- Run `tools/Invoke-Validation.ps1 -Tier Fast` before submitting.
- Do not smoke-test or modify `Electronic_Logbook_Master.xlsm` destructively. Use a
  disposable copy, and do not modify user-generated `*_Updated.xlsm` workbooks.
- If you changed workbook content, export the VBA source after testing so the text files match the workbook.

## Release Changes

Release PRs should use the pull request checklist. The master workbook must be checked locally before release because GitHub Actions cannot reliably inspect Excel named ranges without Excel installed.

At minimum, run:

```powershell
.\tools\Invoke-Validation.ps1 -Tier Excel
```

Then smoke test the prepared workbook manually in Excel.

## External Updater

Build and run the updater unit tests with:

```powershell
dotnet test ElectronicLogbook.Updater.sln --configuration Release
```

The updater is an experimental Windows-only prototype. It must never overwrite, rename, or
delete the source workbook. See `updater/README.md` for its supported migration contract.
