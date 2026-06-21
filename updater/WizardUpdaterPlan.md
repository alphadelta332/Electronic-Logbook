# Wizard Updater Plan

Status: approved direction, implementation-ready
Date: 2026-06-20

## Product Decisions

1. Wizard UX flow
2. Locked updater window while migration runs
3. Automatic check for updates at startup
4. User-triggered update start
5. Recovery/rollback options on failure

## Wizard Pages

## 1) Welcome

Purpose: show installed version and update-check status.

UI:
- Installed version
- Latest available version (or "Checking...")
- Last checked timestamp
- Buttons: Check for updates, Continue, Exit

Behavior:
- Startup performs non-blocking update check.
- Continue is enabled when either no update exists or an update is available.

## 2) Update Available

Purpose: explain what will happen.

UI:
- New version and release notes summary
- Source workbook path
- Planned output workbook path
- Statement that source workbook remains unchanged
- Buttons: Back, Continue, Cancel

## 3) Preflight Checks

Purpose: fail fast before long-running migration.

Checks:
- Source workbook is not open/locked by Excel
- Output folder writable
- Sufficient disk space
- Master/release asset reachable and valid

UI:
- Checklist with pass/fail state per check
- Buttons: Retry checks, Continue, Cancel

Behavior:
- Continue enabled only when all checks pass.

## 4) Ready to Update

Purpose: explicit user confirmation before mutation operations.

UI:
- Summary of source, master/release, and output paths
- Optional checkbox: save detailed diagnostic log
- Buttons: Back, Start update, Cancel

## 5) Updating (Locked)

Purpose: transparent progress and safe cancellation boundaries.

UI:
- Current phase title
- Progress bar (determinate when available, indeterminate otherwise)
- Live activity log panel
- Cancel button (checkpoint-aware)

Behavior:
- Window locked to wizard while update runs.
- Cancel is allowed only between phase boundaries.

## 6) Complete / Recovery

Success UI:
- Updated workbook path
- Validation report path
- Buttons: Open updated workbook, Open report, Finish

Failure UI:
- Friendly error summary
- Failed phase id and message
- Buttons: Retry, Open original workbook, Open diagnostics, Finish

## Engine Contract Already Implemented

The updater core now emits structured progress events through `IUpdaterProgressSink`.

Event types:
- `phase-started`
- `phase-failed`
- `update-completed`

Stable phase IDs (`UpdaterPhaseIds`):
- `start-excel`
- `open-source-workbook`
- `open-master-copy`
- `prepare-master-copy`
- `read-source-validation-data`
- `copy-logbook-data`
- `copy-keywords-data`
- `copy-routes-data`
- `copy-airport-base-flags`
- `copy-named-preferences`
- `restore-logbook-presentation`
- `calculate-output-workbook`
- `refresh-pivot-tables`
- `update-hours-over-time-chart`
- `validate-preserved-data`
- `save-output-workbook`
- `completed`
- `failed`

## Implementation Roadmap

## Milestone 1: UI Shell

- Create new WPF project `ElectronicLogbook.Updater.Wizard`
- Build wizard host window and page navigation
- Add startup update check service and page view models

## Milestone 2: Wire to Engine

- Implement WPF progress sink that maps events to UI state
- Run migration on background task with UI dispatcher updates
- Add phase log panel and checkpoint-aware cancellation

## Milestone 3: Preflight + Recovery

- Implement preflight checks and failure mapping
- Add diagnostic report links and open-folder/file helpers
- Add retry workflow from failure page

## Milestone 4: Packaging

- Publish self-contained x64 build
- Add code-signing pipeline
- Add installer/bundle packaging and release assets

## Notes on Rollback

Because source and output are separate files, rollback risk is lower than in-place replacement.
Recovery still matters for user confidence:
- Source workbook remains untouched if migration fails.
- Partial output is removed where safe, otherwise retained with diagnostics.
- Recovery page provides one-click return to original workbook.
