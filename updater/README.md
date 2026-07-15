# External Updater

This is the Windows external updater used by the Electronic Logbook wizard flow.

It creates a staged updated workbook from a clean master and preserved user data, then can
finalise an in-place handoff that keeps the original filename and writes a timestamped
`*_Old_yyyyMMdd-HHmmss.xlsm` backup.

## Preserved Data

- Logbook raw entry columns from `Year` through `Circling`
- Custom Logbook column headings
- Currency detection `Keywords`
- Base airport selections from `BaseAirportsTop10`
- Routes table and route-cache state
- Date reset and warning-suppression preferences
- Logbook table style, custom column formatting, totals-area formatting, and palette

The updater also rebuilds the live Logbook totals ranges, repairs expanded-row visibility,
refreshes pivot tables, and updates the Hours Over Time chart range.

Everything else comes from the clean master workbook.

## CLI Usage

Use a local master while developing:

```powershell
dotnet run --project updater/src/ElectronicLogbook.Updater -- `
  --source "C:\Path\My Logbook.xlsm" `
  --master "C:\Path\Electronic_Logbook_Master.xlsm" `
  --output "C:\Path\My Logbook Updated.xlsm"
```

When a published release contains `release-manifest.json`, omit `--master` to download
and verify the latest release workbook:

```powershell
dotnet run --project updater/src/ElectronicLogbook.Updater -- `
  --source "C:\Path\My Logbook.xlsm" `
  --output "C:\Path\My Logbook Updated.xlsm"

# Optional: finalise by replacing the source filename and creating *_Old backup
dotnet run --project updater/src/ElectronicLogbook.Updater -- `
  --source "C:\Path\My Logbook.xlsm" `
  --output "C:\Path\My Logbook Updated.xlsm" `
  --inplace
```

The updater writes a JSON validation report beside the final updated workbook.

Run the disposable Excel migration test locally with:

```powershell
.\updater\Test-ExternalUpdater.ps1
```

## Backward Compatibility Policy

The supported automatic-update floor is defined in `updater/compatibility-policy.json`.
The updater must migrate every tagged release at or newer than `minimumSupportedVersion`
directly to the current master workbook.
At runtime, the external updater refuses source workbooks older than that floor before
copying user data into a staged output workbook.

Run the full Excel compatibility matrix before releases:

```powershell
.\updater\Test-CompatibilityMatrix.ps1
```

The matrix extracts each supported `Electronic_Logbook_Master.xlsm` from git tags, seeds
known data into a temporary source copy, runs the current external updater, and validates
that the updated workbook opens with preserved data. Change `minimumSupportedVersion`
in the policy file if the supported floor intentionally changes.

GitHub-hosted runners are used for static and unit checks only. The Excel compatibility
matrix requires desktop Microsoft Excel, so it should be run locally or through the
manual `Compatibility matrix` workflow on a self-hosted Windows runner labelled
`self-hosted`, `windows`, and `excel`.

## Current Limitations

- Requires Microsoft Excel for Windows.
- Uses Excel COM automation and must run while the source workbook is closed.
- Does not yet provide a full visual-diff test or Normalise every possible user-customized format.
- The executable is not currently code-signed or distributed as a release asset.

## Release Asset Packaging

Build release-ready wizard assets with:

```powershell
.\updater\Publish-WizardAsset.ps1
```

To Authenticode-sign the wizard executable during packaging, install the code-signing
certificate in the current user or local machine certificate store and run:

```powershell
.\updater\Publish-WizardAsset.ps1 `
  -Sign `
  -CertificateThumbprint "THUMBPRINT"
```

This script outputs:

- `updater/dist/ElectronicLogbook.Updater.Wizard.exe`
- `updater/dist/ElectronicLogbook.Updater.Wizard.win-x64.zip`

Upload at least one of these assets to the GitHub release. The in-workbook launcher will use the `.exe` directly and can fall back to the `.zip` asset.

After the release tag exists, upload the wizard assets with:

```powershell
.\updater\Upload-WizardAsset.ps1 -Tag vX.Y.Z
```

Add `-Clobber` if replacing an existing draft-release asset.
Add `-Sign -CertificateThumbprint "THUMBPRINT"` to sign during the publish step before upload.

## Product Direction (Implemented)

Confirmed product direction:

- Wizard flow UI (not a single blank progress bar)
- Locked update experience while migration is running
- Auto-check for updates at startup
- User-triggered update start (no automatic install)
- Explicit recovery/rollback actions in the completion screen

Wizard pages:

1. Welcome and current version
2. Update available and release notes summary
3. Preflight checks (Excel lock, write access, space, network)
4. Ready to update confirmation
5. Updating (live phase/status log)
6. Complete (open updated copy/report) or failure recovery

## Progress Event Contract (UI Integration)

The migration engine now supports a progress sink via `IUpdaterProgressSink` and emits structured events:

- `phase-started`
- `phase-failed`
- `update-completed`

Stable phase IDs for UI mapping are defined in `UpdaterPhaseIds`.

Current CLI wiring uses `ConsoleUpdaterProgressSink`; the wizard subscribes to the same progress events for determinate update progress.

## Wizard MVP (Now Available)

A first-pass Windows wizard app now exists at:

- `updater/src/ElectronicLogbook.Updater.Wizard`

Run it with:

```powershell
dotnet run --project updater/src/ElectronicLogbook.Updater.Wizard
```

Current MVP behavior:

1. 6-step wizard shell (Welcome, Available, Preflight, Ready, Updating, Complete)
2. Auto-check for latest GitHub release metadata on startup
3. User-triggered update start from the Ready page
4. Locked update navigation while migration runs
5. Live phase/status log wired from engine progress events
6. Local-master mode and release-download mode support
7. End-user fields are read-only; source/output/channel are resolved automatically
8. In-place swap is enabled by default (`*_Old` backup + original filename preserved)
9. Finish screen can open the updated workbook via a checkbox
10. Update cancellation is honoured at migration phase checkpoints

Optional launch arguments are supported for integration/testing:

```powershell
dotnet run --project updater/src/ElectronicLogbook.Updater.Wizard -- `
  --source "C:\Path\My Logbook.xlsm" `
  --output "C:\Path\My Logbook_Updated.xlsm" `
  --master "C:\Path\Electronic_Logbook_Master.xlsm"
```

If `--master` is omitted, release mode is used and `--repo owner/name` can be supplied.
Use `--no-inplace` if you want to keep output as a separate file during testing.

## Recommended Dev Testing Setup

Use one wizard app for both end-user and dev testing. Do not create a separate dev wizard UI.

- End-user flow: launch in release mode.
- Dev flow: launch with `--master` to force a local master workbook.

Helper scripts:

- `updater/Run-Wizard-Dev.ps1`
  - default mode uses local `Electronic_Logbook_Master.xlsm`
  - optional `-UseReleaseChannel` switch tests release mode
- `updater/Run-Wizard-Release.ps1`
  - explicit release-mode launcher

Example local-master dev run:

```powershell
.\updater\Run-Wizard-Dev.ps1 `
  -SourcePath "D:\Alex PC\OneDrive\WorkUni\Logbook\Electronic Logbook - Working Copy.xlsm"
```

Example release-channel run:

```powershell
.\updater\Run-Wizard-Release.ps1 `
  -SourcePath "D:\Alex PC\OneDrive\WorkUni\Logbook\Electronic Logbook - Working Copy.xlsm"
```

This is an MVP implementation and intentionally does not yet include:

- polished installer theming
- file picker dialogs
- interruption inside individual Excel COM calls
- packaged installer distribution
