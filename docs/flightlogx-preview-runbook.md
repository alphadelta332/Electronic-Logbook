# FlightLogX Preview Runbook

Status: pre-Preview operating plan

Last checked: 2026-08-28

This runbook defines the private, invitation-only Android-first Preview for hosted sync.
It intentionally excludes public signup, billing, public uptime promises, and public
release hardening.

## Preview Goal

Run a small eight-week Preview that proves the workbook-led move to FlightLogX without
manual packages. An existing `2.0.3` workbook is the one-time migration source. After
the hosted migration is verified, the Android app is the normal editable logbook and
Excel is used only for fresh exports. Continuing workbook/app synchronization is not
part of this Preview.

The controlled `preview` workbook channel tests `3.0.0` while ordinary `main` workbooks
stay on `2.0.3`. Its GitHub branch, protected environment, variable, secret, and normal
bridge marker use the canonical `preview` name. A pinned `pilot` branch and matching bridge
marker remain only for the coached first hop from `2.0.3`. Preview prerelease files are
publicly downloadable because the repository and
the `2.0.3` bootstrap use unauthenticated GitHub Release URLs. They are not advertised,
linked from the public update channel, or offered to a workbook unless a coach explicitly
changes that workbook to `GitHubBranch = pilot`.

## Preview Update Channel Bootstrap

### Repository setup

Before coaching any workbook, the owner must confirm all of the following:

1. The remote `preview` branch points to the exact approved `3.x` commit.
2. The GitHub `preview` environment requires owner approval.
3. That environment contains repository variable
   `ELECTRONIC_LOGBOOK_PREVIEW_SUPABASE_URL` and secret
   `ELECTRONIC_LOGBOOK_PREVIEW_SUPABASE_ANON_KEY` for the Preview Sydney project.
4. The `Publish FlightLogX Preview wizard` workflow passed for that exact commit after its environment
   approval.
5. The resulting prerelease tag is `dev-wizard-<first 12 characters of the commit>` and
   contains both `preview-wizard-channel.txt` and the compatibility alias
   `pilot-wizard-channel.txt`. The old `dev-wizard-` name is intentional: the `2.0.3`
   launcher requires it for the first hop.
6. The compatibility branch `pilot` points to the exact same commit as `preview`. Do not
   develop or publish from `pilot`; it exists only because the `2.0.3` launcher reads its
   workbook channel as a GitHub branch name.
7. `origin/main:version.txt` is still `2.0.3` and the public latest release is still
   `v2.0.3`.

Do not coach the workbook change if any one of these checks fails. Do not substitute a
development-branch build or a different Supabase project.

### Coached change in the existing 2.0.3 workbook

These steps are for the owner or a coached first canary only. They are not customer-facing
public instructions.

1. Open the existing `2.0.3` workbook and save it normally.
2. Press `Alt+F11` to open the Visual Basic window.
3. Press `Ctrl+G` to open the Immediate window at the bottom.
4. Paste this exact line, then press Enter:

   ```text
   ThisWorkbook.Names("GitHubBranch").RefersToRange.Value2 = "pilot"
   ```

5. Close the Visual Basic window and save the workbook again.
6. Use the workbook's normal update check and accept the `3.0.0` update.
7. `2.0.3` will show one warning labelled `Development Updater Warning`. This is expected
   because `2.0.3` does not yet know the word `pilot`. Confirm it only after the coach has
   matched the approved Preview commit and prerelease tag from the repository setup above.
8. Do not accept a second development warning after the workbook has reached `3.0.0`.
   The `3.0.0` launcher accepts that legacy value and canonicalises it to `preview`.

The updater writes `preview` into the upgraded workbook when the source contains either
`preview` or the legacy `pilot` value. It does not carry `dev`, `hotfix`, `main`, blank,
or an arbitrary branch value across migration. This keeps the canary on the Preview
channel without turning an accidental or hostile branch name into a durable update source.

This channel bootstrap only selects and retains the Preview build. Do not treat it as proof
that hosted migration, exact readback, Google recovery, workbook stamping, or Android
arrival is complete; those have separate acceptance gates in `TODO.md`.

## Named Cohort

Keep the participant list out of git. The Preview cohort source of truth is the local,
gitignored `artifacts/private-pilot-20260806/cohort.md` file or the project owner's
private tracker.

Use this table shape for each named participant:

| Field | Required value |
| --- | --- |
| Participant name | Real pilot name or private identifier |
| Contact email | Invited Supabase Auth email |
| Environment | Android device model, Android version, workbook use yes/no |
| Workbook path | Local/private note only; do not commit |
| Start date | Date invited |
| Week 4 status | Continue, pause, support needed, or withdrawn |
| Exit status | Pass, pass with issues, failed, or withdrawn |

Minimum cohort before starting: 2 app-only participants and 1 workbook-linked
participant. Target maximum for the first run: 5 total participants.

## Supported Environments

Supported for this Preview:

- Android first, distributed through the approved Preview installation path once
  that path passes its acceptance gate;
- Pixel 8 Pro reference device and comparable Android phones that can run the current
  WebView/PWA build;
- Australia/Sydney Supabase development or Preview project;
- Windows Excel `2.0.3` as the one-time migration source through the controlled `pilot`
  update channel and checked-in updater flow;
- local encrypted app cache and hosted encrypted operation ledger.

Out of scope:

- iPhone and iPad;
- public signup;
- billing or subscriptions;
- user-owned cloud-file sync;
- continuing workbook/app synchronization;
- public support or uptime commitments.

## Invitation Process

1. Confirm the participant has the supported Android and, if relevant, Windows/Excel
   environment.
2. Send `docs/flightlogx-preview-android-install.md` before the invitation. Explain that this
   is a Firebase-distributed APK, that Android will show outside-Play-Store warnings, and
   that the temporary **Allow from this source** permission is normally turned off after
   installation. The owner reference device may keep it enabled only for the duration of
   an explicitly active update rehearsal.
3. From the repository root, first run the owner-only enrolment as a read-only preflight:

   ```powershell
   .\tools\Add-FlightLogXParticipant.ps1 `
     -Email "participant-google-account@example.com" `
     -DisplayName "Participant name" `
     -FirebaseGroupAlias "the-approved-release-group" `
     -WhatIf
   ```

   Check that every precondition passes, then run the same command again without
   `-WhatIf`. The command creates or reuses the matching hosted invitation in
   `workbook_migration` mode, adds that same Google email to the existing
   release-bearing Firebase group, verifies the membership, and writes a private copy of
   this installation guide under
   `%LOCALAPPDATA%\ElectronicLogbook\ParticipantHandoffs`. Send that generated file only
   through the owner's trusted contact path. Public self-registration remains disabled;
   neither the owner nor participant uses database tools.
4. Complete the repository and `2.0.3` coached channel checks above.
5. Start the migration from the existing workbook, accept `3.0.0`, and use Google sign-in
   in the Windows updater.
6. Continue only after the updater reports exact hosted readback and completes the
   workbook migration lifecycle.
7. Coach the first installation using the tester-facing guide. Treat Firebase's grey
   **Download started...** state as a direction to use Android notifications or Chrome
   Downloads, not as live download progress. Stop rather than coaching an ordinary tester
   through an unverified-developer advanced flow or security delay.
8. Ask the participant to open the approved Android Preview build and sign in with the same
   Google account. The app must discover the completed migrated logbook without a workbook
   picker or manual import.
9. Keep the displayed six-digit email-code sign-in and package exchange in
   Advanced/support only.
10. Record the start date, environment, migration result, warnings encountered, and
    expected weekly check-in cadence in the private cohort tracker.

### Preview Update Rehearsal

Android `versionCode` is monotonic and separate from the displayed `version.txt` value.
FlightLogX reserves four low-order version-code digits for Preview build revisions, so a
`3.0.0` revision `1` build is newer than the initial Preview APK while a future `3.0.1`
release remains newer than every `3.0.0` Preview revision.

1. Confirm the owner-only Firebase group contains exactly the owner and no canary.
2. Build a higher disposable APK from `mobile/`:

   ```powershell
   npm.cmd run build:android:preview -- -PreviewBuildRevision 1
   ```

3. Verify the script reports the permanent package, signing certificate, displayed
   version, higher Android version code, and APK SHA-256 before uploading it.
4. Distribute only to the owner-only Firebase group. Do not put an invited email or a
   Firebase App ID in tracked scripts or evidence.
5. On the retained Pixel, use **Settings > Check for Preview update**. Record the installed
   version before the check, Firebase release identifier in redacted form, download
   outcome, and the Android installation-approval screen. Do not approve the final install
   until retained state and certificate continuity are confirmed.
6. Turn off **Allow from this source** after the rehearsal ends.

## Weekly Check-In

Collect these signals weekly for eight weeks:

- app-only entries added, edited, deleted, restored, and exported;
- workbook-linked changes in each direction;
- offline edits and later convergence;
- visible sync status at start and end of use;
- auth, pairing, reauthentication, recovery, or revoked-device events;
- user confusion, support contact, and elapsed time to resolution;
- Supabase usage and upgrade-trigger status.

Keep raw personal feedback private. Commit only redacted summaries or aggregate findings.

## Feedback And Incident Flow

Severity levels:

| Severity | Definition | Response |
| --- | --- | --- |
| S0 data loss | User cannot recover expected logbook data from app, workbook, backup, or hosted ledger | Pause Preview, preserve devices/workbooks, export diagnostics, start rollback |
| S1 sync/security | Cross-account access, plaintext hosted payload, revoked device syncs, or unrecoverable sync divergence | Disable affected account/device, stop new invites, preserve evidence |
| S2 blocked workflow | User cannot sign in, pair, sync, restore, or continue normal entry work | Provide workaround or patched build before next check-in |
| S3 usability | Confusing status, copy, timing, or recovery path without data risk | Track for Preview exit decision |

Incident record minimum:

- timestamp in Australia/Sydney;
- participant private identifier;
- environment and app/updater version;
- last known sync status;
- exact user-visible message;
- redacted diagnostic bundle path;
- recovery or rollback action taken;
- prevention decision.

## Free-Tier Monitoring

Official Supabase pages checked on 2026-08-06:

- https://supabase.com/pricing
- https://supabase.com/docs/guides/platform/billing-on-supabase

Current free-plan assumptions for Preview monitoring:

- 50,000 monthly active users;
- 500 MB database size per project;
- 5 GB egress;
- 5 GB cached egress;
- 1 GB file storage;
- 2 active projects;
- free projects may pause after inactivity.

Preview review triggers:

- database reaches 250 MB or health reports `NearLimit`;
- egress or cached egress reaches 2.5 GB in a month;
- file storage reaches 500 MB;
- active Preview accounts exceed 25;
- free-project pausing disrupts a participant;
- users begin treating hosted storage as their only practical recovery source;
- support needs require managed backups, longer log retention, or email support.

Weekly commands use `tools\Invoke-PreviewHealthCheck.ps1`, which queries the legacy
`public.get_hosted_pilot_health()` without printing the database connection string:

```powershell
$env:ELB_SUPABASE_PREVIEW_DB_URL = "<preview-db-url>"
.\tools\Invoke-PreviewHealthCheck.ps1 `
  -OutputPath artifacts\flightlogx-preview\health\week-01.json
```

Also inspect Supabase Security Advisor, Performance Advisor, Auth configuration, project
status, and usage dashboards before each new invite batch. The preflight must report the
Preview project as `ACTIVE_HEALTHY`; a paused or restoring project is not invite-ready.

For a single redacted pre-invite report that checks the local Preview files, captures
health, and can run the adversarial RLS harness:

```powershell
$env:ELB_SUPABASE_PREVIEW_DB_URL = "<preview-db-url>"
.\tools\Invoke-PreviewPreflight.ps1 -RunRlsHarness
```

## Rollback

Use rollback when an S0/S1 incident occurs or when the exit decision is `failed`.

1. Stop new invitations.
2. Disable affected devices or accounts in Supabase.
3. Ask participants not to clear app storage, uninstall the app, or overwrite the paired
   workbook.
4. Export redacted diagnostics locally.
5. For app-only users, export the encrypted local logbook backup through Advanced
   recovery/support.
6. For workbook-linked users, preserve the workbook, updater backups, and journal files.
7. Rehearse logical hosted export and restore into a disposable local or separate Sydney
   project before any destructive hosted cleanup.
8. Patch and retest with the hosted reliability/security gate before resuming.

## Exit Decision

Make the exit decision after the eight-week run or after an S0/S1 stop.

Pass requires:

- every active participant can still open and use the app;
- no unresolved S0 or S1 incidents;
- app-only and workbook-linked participants each complete at least one offline recovery
  and later convergence path;
- hosted diagnostics remain redacted;
- RLS harness passes against the final Preview schema;
- free-tier usage stays below review triggers or a paid-upgrade decision is documented;
- package exchange remains Advanced recovery/support, not normal daily use.

Pass with issues requires the same data-safety guarantees but allows S2/S3 fixes to be
queued before public-release planning.

Fail if data recovery is uncertain, security boundaries are violated, sync convergence
cannot be explained, or the support burden is not sustainable for a FlightLogX Preview.

## Pre-Invite Checklist

- [ ] Private cohort tracker exists outside git.
- [ ] Development project migration and RLS harness pass.
- [ ] Preview project is created in `ap-southeast-2`.
- [ ] Public signup is disabled and email sign-in is configured for invited users only.
- [ ] Preview project status is `ACTIVE_HEALTHY` immediately before invitations.
- [ ] Security Advisor and Performance Advisor are reviewed.
- [ ] Logical export and restore are rehearsed into a separate project or disposable
  local database.
- [ ] `tools\Invoke-PreviewHealthCheck.ps1` writes a redacted weekly health
  snapshot.
- [ ] `tools\Invoke-PreviewPreflight.ps1 -RunRlsHarness` writes a redacted
  pre-invite readiness report.
- [ ] Android install path is verified on the reference Pixel device.
- [ ] Tester receives `docs/flightlogx-preview-android-install.md` before the Firebase invitation
  and understands when to continue, when to stop, and how to remove the temporary
  unknown-app installation permission.
- [ ] Workbook pairing is verified on the Excel-capable release machine.
- [ ] Rollback contact path and diagnostic collection path are tested.
