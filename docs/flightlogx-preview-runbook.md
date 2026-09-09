# FlightLogX Preview Runbook

Status: controlled-cohort operating procedure

Last checked: 2026-09-09

This runbook defines the private, invitation-only Android-first Preview. It excludes
public signup, billing, public uptime promises, app-store distribution, and public release
hardening.

## Preview Goal

Prove the workbook-led move to FlightLogX with the owner and then one external Windows
Excel plus Android canary before starting the eight-week Preview.

An existing Electronic Logbook `2.0.3` workbook is a one-time migration source. After
exact hosted verification, Android is the normal editable logbook. The upgraded workbook
remains editable and is stamped `Moved to FlightLogX`, but later changes in that workbook
stay local. Later Excel, CSV, and PDF files are fresh exports, not synchronized working
copies.

The ordinary journey uses:

- the coached `2.0.3` Preview-channel switch;
- the normal updater wizard and Google sign-in;
- the Firebase App Distribution invitation and signed permanent Android package;
- Google sign-in in FlightLogX; and
- automatic managed-envelope recovery and first sync.

It does not expose packages, recovery codes, encryption keys, logbook ids, hashes,
databases, file pickers, or manual import/export. The displayed six-digit email sign-in
and Package Exchange remain Advanced/support only.

## Controlled Channel

The canonical controlled workbook channel is `preview`. Ordinary `main` workbooks remain
on `2.0.3` while the controlled Preview tests `3.0.0`.

A pinned `pilot` branch and bridge marker remain only because the `2.0.3` launcher treats
its channel as a GitHub branch name. The first hop therefore uses `pilot`; the `3.0.0`
launcher canonicalizes it to `preview`. Do not develop or publish from `pilot`.

Preview prerelease files are publicly downloadable because the public repository and the
`2.0.3` bootstrap use unauthenticated GitHub Release URLs. They are not advertised,
linked from `main`, or offered unless a coach intentionally changes one workbook to the
legacy bridge.

Before coaching a workbook, verify:

1. Remote `preview` points to the exact approved commit.
2. The GitHub `preview` environment requires owner approval and contains the approved
   Preview Supabase URL variable and anonymous-key secret.
3. `Publish FlightLogX Preview wizard` passed for that exact commit after approval.
4. The prerelease tag is `dev-wizard-<first 12 commit characters>` and contains both
   `preview-wizard-channel.txt` and `pilot-wizard-channel.txt`.
5. Remote `pilot` points to the same commit as `preview`.
6. `origin/main:version.txt` and the public latest release remain `2.0.3` and `v2.0.3`.

Stop if any check fails. Do not substitute a development build or another Supabase
project.

### Coached `2.0.3` switch

These are private owner/coach instructions, not public documentation:

1. Open the participant's existing `2.0.3` workbook and save it normally.
2. Press `Alt+F11`.
3. Press `Ctrl+G` to open the Immediate window.
4. Paste the following exact line and press Enter:

   ```text
   ThisWorkbook.Names("GitHubBranch").RefersToRange.Value2 = "pilot"
   ```

5. Close the Visual Basic window and save the workbook.
6. Use the workbook's normal update check and accept `3.0.0`.
7. Confirm the one `Development Updater Warning` shown by `2.0.3` only after the coach
   matches the approved commit and prerelease above.
8. Do not accept a second development warning after the workbook reaches `3.0.0`. Stop
   and investigate if one appears.

The installed workbook must contain `preview`, never `pilot`, `dev`, `hotfix`, an
arbitrary branch, or blank. Remove the external `pilot` aliases only after the owner and
canary workbooks and devices use canonical Preview resources.

## Cohort And Private Records

Keep all participant names, emails, device details, workbook paths, contact details, and
raw feedback outside git. The source of truth is the gitignored
`artifacts/private-pilot-20260806/cohort.md` file or the owner's private tracker.

Launch sequence:

1. final owner dress rehearsal;
2. resolve every owner S0-S2 issue;
3. one external canary with Windows Excel and Android;
4. resolve every external-canary S0-S2 issue; and
5. begin the eight-week owner-plus-canary Preview only when both journeys are safe.

Record privately for each person:

| Field | Required value |
| --- | --- |
| Participant | Real name or private identifier |
| Google account | Exact email provisioned in Supabase and Firebase |
| Environment | Windows/Excel version, Android model and version |
| Workbook | Private source path and pre-migration backup location |
| Expected data | Flight count, hours, date range, custom fields and currency dates |
| Start | Invitation and successful arrival dates |
| Weekly status | Continue, pause, support needed, or withdrawn |
| Exit | Pass, pass with issues, failed, or withdrawn |

Supported scope is Windows Excel `2.0.3` as the migration source, the approved permanent
Android package `com.alphadelta.electroniclogbook`, and the Australia/Sydney Preview
project. iPhone/iPad, public signup, live workbook sync, user-owned cloud-file sync, and
public support are out of scope.

## Final Owner Dress Rehearsal

The dress rehearsal must use disposable workbook data and the permanent Preview app. It
must not clear or uninstall the separate development app. Destructive changes to the
permanent app require a written test plan, confirmed recoverability, and explicit owner
approval for that rehearsal.

Preparation may use private owner tools, but the observed journey itself must use only
the same visible surfaces given to a canary:

1. Record the exact approved commit, wizard tag, Firebase release, APK hash, signing
   certificate, hosted-project status, and disposable workbook expectations privately.
2. Run the redacted Preview preflight and stop on any failure.
3. Provision or reset only the disposable owner rehearsal state according to its explicit
   test plan. Confirm the development app remains untouched.
4. Perform the coached `2.0.3` switch and use the workbook's normal update action.
5. In the updater, review the visible flight count, hours, date range, and warning summary.
6. Continue with Google and select the invited owner account in the system browser.
7. Require the wizard to report exact hosted verification, retained untouched timestamped
   backup, installed workbook, and `Moved to FlightLogX` stamp.
8. Open the installed workbook and attempt to add one disposable entry. Confirm the first
   add attempt in that Excel session warns that spreadsheet changes are not sent to
   FlightLogX. Cancel the add unless that local-edit behavior is itself under test.
9. Install or update the signed app through Firebase App Distribution and Android's own
   installation approval.
10. Open FlightLogX, continue with Google using the same account, and require `Existing
    logbook restored and synced.` without a picker, package, code, key, or empty logbook.
11. Compare visible entries, totals, custom fields, and currency dates with the updater
    summary and workbook. Confirm the app shows `Synced`.
12. Capture one redacted evidence artifact covering every visible step and an independent
    hosted readback. Do not record secrets, full identifiers, or the invited email.

Any S0-S2 issue stops external enrolment. Preserve the app, workbook, backup, hosted
ledger, and redacted diagnostics until the cause is proven and the repair is retested.

## External Canary Procedure

Do not improvise this sequence. The canary receives customer-facing instructions and
plain-language coaching only. Owner preparation stays private.

### 1. Confirm readiness

- The final owner dress rehearsal passed with no open S0-S2 issue.
- The canary has the supported Windows Excel workbook and Android phone.
- The exact Google-account email is confirmed by the canary.
- The workbook's expected flight count, logged hours, date range, custom-field labels,
  and currency dates are recorded privately before migration.
- The approved Firebase group already contains a distributed permanent-package release.
- Public self-registration must remain disabled.

Send `docs/flightlogx-preview-android-install.md` through the trusted contact path before
the canary opens any Firebase invitation. Explain the outside-Play-Store warnings and the
requirement to turn off **Allow from this source** after every install or update.

### 2. Preflight and provision the same email

From the repository root, run the owner-only read-only preflight:

```powershell
.\tools\Add-FlightLogXParticipant.ps1 `
  -Email "participant-google-account@example.com" `
  -DisplayName "Participant name" `
  -FirebaseGroupAlias "the-approved-release-group" `
  -WhatIf
```

Read every result. The command must verify the exact active Sydney hosted project,
disabled signup, enabled owner-managed email plus Google Auth, the existing Firebase
group, and at least one release in that group. If it passes, rerun the identical command
without `-WhatIf`.

That command creates or safely reuses the `workbook_migration` Auth identity and hosted
invitation, adds the same email to the existing Firebase group, verifies membership, and
writes a private guide under
`%LOCALAPPDATA%\ElectronicLogbook\ParticipantHandoffs`. Send the generated guide only to
the named canary. Neither owner nor participant uses database tools for enrolment.

### 3. Migrate the workbook first

1. Complete the private controlled-channel checks and coach the exact `2.0.3` switch.
2. Ask the canary to use the workbook's normal update action and accept the approved
   `3.0.0` move.
3. Ask them to compare the updater's visible flight count, logged hours, date range, and
   warning summary with their workbook. Stop on any mismatch.
4. Ask them to continue with Google in the system browser and select the exact account
   provisioned above.
5. Wait for `Migration Complete`. Require the displayed verified flight count, retained
   untouched timestamped backup, installed original filename, and `Moved to FlightLogX`
   result. Stop on any other outcome.
6. Ask the canary to reopen the installed workbook and confirm their expected content.
7. Ask them to start one add action. Confirm the once-per-Excel-session warning says
   changes stay only in that spreadsheet and are not sent to FlightLogX. Cancel the add.

Do not install or sign into Android before migration completes. A phone attempt before
completion must fail closed, but deliberately creating that failure is not part of the
customer journey.

### 4. Install and arrive automatically

1. Ask the canary to follow the tester guide from the Firebase email through Android's
   installation approval. A grey **Download started...** button directs them to Android
   notifications or Chrome Downloads; it is not live progress.
2. Stop rather than coaching an ordinary tester through an unverified-developer advanced flow
   or a 24-hour security delay.
3. Open FlightLogX and choose the ordinary Google sign-in action with the same account.
4. Require the message `Existing logbook restored and synced.` and the expected migrated
   flights. No workbook picker, import, package, recovery code, logbook choice, or empty
   replacement should appear.
5. Compare visible flight count, entries, totals, custom fields, and currency dates with
   the recorded migration expectations.
6. Confirm the app status is `Synced`.
7. Every external tester must turn off **Allow from this source** for Chrome or Firebase
   App Tester after installation.

### 5. Observe without translating the product

Ask the canary to describe what they think happened and what they would do next. Do not
explain internal terms to help them pass. Record privately:

- every instruction they needed clarified;
- all warnings and visible messages;
- whether they attempted to find a package, key, code, picker, or database;
- elapsed time for migration, installation, sign-in, and arrival;
- final sync state and exact expected-versus-observed content; and
- any S0-S3 issue.

Do not enrol anyone else until every S0-S2 issue is resolved and the repaired path is
retested.

## Preview Updates

Android `versionCode` is monotonic and separate from `version.txt`; four low-order digits
are reserved for Preview revisions.

Before distributing an update:

1. Build a higher signed Preview APK from `mobile/`, using a deliberate revision:

   ```powershell
   npm.cmd run build:android:preview -- -PreviewBuildRevision 1
   ```

2. Verify the permanent package, signing certificate, displayed version, higher Android
   version code, clean publish output, and APK SHA-256.
3. Distribute to the owner first. From **Settings > Check for Preview update**, verify the
   Firebase prompt, download, Android scan, and installer approval while preserving app
   data.
4. After owner success, distribute the same release to the canary group.
5. Android must still show its scan and installer approval. Every external tester must
   turn off **Allow from this source** afterward.

On the retained owner development phone only, FlightLogX's own installation-source
permission may remain enabled during active Preview update work. The exception does not
apply to Chrome, Firebase App Tester, an external tester, or any device leaving the
owner's control.

## Weekly Check-In

Collect these signals for eight weeks:

- app flights added, corrected, deleted, restored, and freshly exported;
- offline edits and later automatic convergence;
- visible sync status before, during, and after outages;
- Google session renewal and managed recovery outcomes;
- Firebase installation and update outcomes;
- any attempt to resume editing the migrated workbook and whether its local-only warning
  was understood;
- user confusion, support contacts, and elapsed time to resolution; and
- Supabase usage, project status, advisors, and upgrade-trigger status.

Do not ask for two-way Excel synchronization; it does not exist.
Keep raw personal feedback private. Commit only redacted evidence or aggregate findings.

## Incidents

| Severity | Definition | Response |
| --- | --- | --- |
| S0 data loss | Expected logbook data cannot be recovered from the app, untouched migration backup, or hosted ledger | Pause Preview, preserve all state, collect redacted diagnostics, begin rollback investigation |
| S1 sync/security | Cross-account access, plaintext hosted data, revoked-device access, or unexplained divergence | Stop invitations, disable only proven affected access, preserve evidence |
| S2 blocked workflow | Participant cannot migrate, install, sign in, recover, sync, update, or continue app entry work | Fix and retest before the next participant |
| S3 usability | Confusing copy, timing, or navigation without data or security risk | Record for the Preview exit decision |

Record privately: Sydney timestamp, participant identifier, environment and versions,
last known sync status, exact visible message, redacted diagnostic path, response taken,
root cause evidence, repair, and prevention decision.

## Health And Free-Plan Monitoring

Supabase facts checked on 2026-09-09:

- https://supabase.com/docs/guides/platform/billing-on-supabase
- https://supabase.com/docs/guides/platform/free-project-pausing

Current operating assumptions are two active Free projects, 50,000 monthly active users,
500 MB database size per project, 5 GB egress, 5 GB cached egress, 1 GB storage, and
possible pausing after low activity over seven days. Recheck the live dashboard before a
launch or upgrade decision.

Review when database size reaches 250 MB, egress reaches 2.5 GB in a month, active
accounts exceed 25, project pausing disrupts use, hosted data becomes the only practical
recovery source, or support requires paid reliability features.

Weekly redacted health:

```powershell
$env:ELB_SUPABASE_PREVIEW_DB_URL = "<preview-db-url>"
.\tools\Invoke-PreviewHealthCheck.ps1 `
  -OutputPath artifacts\flightlogx-preview\health\week-01.json
```

This tool currently calls the retained compatibility function
`public.get_hosted_pilot_health()`. The legacy name does not change the canonical Preview
product model.

Pre-invite report and adversarial RLS harness:

```powershell
$env:ELB_SUPABASE_PREVIEW_DB_URL = "<preview-db-url>"
.\tools\Invoke-PreviewPreflight.ps1 -RunRlsHarness
```

Also review Security Advisor, Performance Advisor, Auth configuration, project status,
and usage dashboards. A project that is paused, restoring, unhealthy, or incorrectly
configured is not invite-ready.

## Rollback

For an S0/S1 incident or failed exit decision:

1. Stop new invitations and releases.
2. Preserve phones, app data, source and installed workbooks, timestamped migration
   backups, updater evidence, and hosted state.
3. Disable only accounts or devices proven necessary to contain the issue.
4. Collect redacted diagnostics. Do not collect keys or plaintext flight data.
5. If specifically required, use an encrypted local backup through Advanced/support.
6. Rehearse logical hosted export and restore into a disposable local or separate Sydney
   project before destructive hosted action.
7. Prove the cause, patch it, and repeat the relevant owner gate before resuming.

An administrator database reset is not account recovery.

## Exit Decision

`pass` requires both active participants to retain usable app logbooks, no unresolved
S0/S1 incident, successful offline convergence and managed recovery evidence, redacted
diagnostics, final-schema RLS success, acceptable cost/continuity risk, and Advanced-only
Package Exchange.

`pass with issues` requires the same data and security guarantees but may retain explicit
S2/S3 work before wider release.

`failed` applies when data recovery is uncertain, a security boundary is violated,
convergence cannot be explained, or support burden is not sustainable.

## Pre-Invite Checklist

- [ ] Private cohort tracker exists outside git.
- [ ] Final owner dress rehearsal passed with a redacted artifact and no open S0-S2 issue.
- [ ] Development project migration, managed recovery rehearsal, and RLS harness pass.
- [ ] Preview project is active and healthy in `ap-southeast-2`.
- [ ] Public signup is disabled; owner-managed email and Google are the only approved
  external Auth providers.
- [ ] Google loopback callback and Android OAuth package/certificate pairing are verified.
- [ ] Security Advisor, Performance Advisor, and live usage are reviewed.
- [ ] Logical export and restore are rehearsed into a separate project or disposable
  local database.
- [ ] `Invoke-PreviewHealthCheck.ps1` and `Invoke-PreviewPreflight.ps1 -RunRlsHarness`
  produce redacted passing reports.
- [ ] Exact approved wizard, permanent signed APK, Firebase group, and update path are
  verified first by the owner.
- [ ] Tester receives `docs/flightlogx-preview-android-install.md` before opening the
  Firebase invitation.
- [ ] Owner has the exact Google email and pre-migration workbook expectations.
- [ ] Rollback contact and diagnostic paths are tested.
