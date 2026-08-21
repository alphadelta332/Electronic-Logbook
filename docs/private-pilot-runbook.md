# Private Pilot Runbook

Status: pre-pilot operating plan

Last checked: 2026-08-06

This runbook defines the private, invitation-only Android-first pilot for hosted sync.
It intentionally excludes public signup, billing, public uptime promises, and public
release hardening.

## Pilot Goal

Run a small eight-week pilot that proves normal Electronic Logbook use works without
manual packages:

- app-only setup, sign-in, initialization, local entry, backup, restore, and export;
- app-to-workbook and workbook-to-app sync through the existing updater;
- offline local writes and later convergence;
- actionable `Synced`, `Waiting`, `Offline`, `Signing in`, and `Needs attention`
  states;
- recovery, rollback, incident, and exit procedures that are understandable before
  public-release planning begins.

## Named Cohort

Keep the participant list out of git. The pilot cohort source of truth is the local,
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

Supported for this pilot:

- Android first, with the debug or acceptance Android app installed through the current
  repo-supported path;
- Pixel 8 Pro reference device and comparable Android phones that can run the current
  WebView/PWA build;
- Australia/Sydney Supabase development or private-pilot project;
- Windows Excel workbook use only through the existing `Electronic_Logbook_Master.xlsm`
  plus the checked-in updater flow;
- local encrypted app cache and hosted encrypted operation ledger.

Out of scope:

- iPhone and iPad;
- public signup;
- billing or subscriptions;
- user-owned cloud-file sync;
- a separate Windows companion app;
- public support or uptime commitments.

## Invitation Process

1. Confirm the participant has the supported Android and, if relevant, Windows/Excel
   environment.
2. Create or confirm the invited Supabase Auth user administratively. Public
   self-registration must remain disabled.
3. Create the matching hosted `accounts` invitation row.
4. Ask the participant to install the Android build.
5. Have the participant sign in with the invited email and complete OTP or magic-link
   verification.
6. Confirm `public.accept_hosted_invitation(...)` activates the account and registers
   the Android device.
7. For workbook-linked pilots, pair the workbook through the updater account connection
   path. Do not use package import/export for normal pairing.
8. Record start date, environment, and expected weekly check-in cadence in the private
   cohort tracker.

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
| S0 data loss | User cannot recover expected logbook data from app, workbook, backup, or hosted ledger | Pause pilot, preserve devices/workbooks, export diagnostics, start rollback |
| S1 sync/security | Cross-account access, plaintext hosted payload, revoked device syncs, or unrecoverable sync divergence | Disable affected account/device, stop new invites, preserve evidence |
| S2 blocked workflow | User cannot sign in, pair, sync, restore, or continue normal entry work | Provide workaround or patched build before next check-in |
| S3 usability | Confusing status, copy, timing, or recovery path without data risk | Track for pilot exit decision |

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

Current free-plan assumptions for pilot monitoring:

- 50,000 monthly active users;
- 500 MB database size per project;
- 5 GB egress;
- 5 GB cached egress;
- 1 GB file storage;
- 2 active projects;
- free projects may pause after inactivity.

Pilot review triggers:

- database reaches 250 MB or health reports `NearLimit`;
- egress or cached egress reaches 2.5 GB in a month;
- file storage reaches 500 MB;
- active pilot accounts exceed 25;
- free-project pausing disrupts a participant;
- users begin treating hosted storage as their only practical recovery source;
- support needs require managed backups, longer log retention, or email support.

Weekly commands use `tools\Invoke-PrivatePilotHealthCheck.ps1`, which queries
`public.get_hosted_pilot_health()` without printing the database connection string:

```powershell
$env:ELB_SUPABASE_PILOT_DB_URL = "<pilot-db-url>"
.\tools\Invoke-PrivatePilotHealthCheck.ps1 `
  -OutputPath artifacts\private-pilot-20260806\health\week-01.json
```

Also inspect Supabase Security Advisor, Performance Advisor, Auth configuration, project
status, and usage dashboards before each new invite batch. The preflight must report the
private-pilot project as `ACTIVE_HEALTHY`; a paused or restoring project is not invite-ready.

For a single redacted pre-invite report that checks the local pilot files, captures
health, and can run the adversarial RLS harness:

```powershell
$env:ELB_SUPABASE_PILOT_DB_URL = "<pilot-db-url>"
.\tools\Invoke-PrivatePilotPreflight.ps1 -RunRlsHarness
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
- RLS harness passes against the final pilot schema;
- free-tier usage stays below review triggers or a paid-upgrade decision is documented;
- package exchange remains Advanced recovery/support, not normal daily use.

Pass with issues requires the same data-safety guarantees but allows S2/S3 fixes to be
queued before public-release planning.

Fail if data recovery is uncertain, security boundaries are violated, sync convergence
cannot be explained, or the support burden is not sustainable for a private pilot.

## Pre-Invite Checklist

- [ ] Private cohort tracker exists outside git.
- [ ] Development project migration and RLS harness pass.
- [ ] Private-pilot project is created in `ap-southeast-2`.
- [ ] Public signup is disabled and email sign-in is configured for invited users only.
- [ ] Private-pilot project status is `ACTIVE_HEALTHY` immediately before invitations.
- [ ] Security Advisor and Performance Advisor are reviewed.
- [ ] Logical export and restore are rehearsed into a separate project or disposable
  local database.
- [ ] `tools\Invoke-PrivatePilotHealthCheck.ps1` writes a redacted weekly health
  snapshot.
- [ ] `tools\Invoke-PrivatePilotPreflight.ps1 -RunRlsHarness` writes a redacted
  pre-invite readiness report.
- [ ] Android install path is verified on the reference Pixel device.
- [ ] Workbook pairing is verified on the Excel-capable release machine.
- [ ] Rollback contact path and diagnostic collection path are tested.
