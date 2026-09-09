# Hosted Sync Architecture Decision

Status: accepted and implemented for the controlled Preview

Decision date: 2026-08-06

Implementation reconciled: 2026-09-09

External platform facts checked: 2026-09-09

## Decision

FlightLogX uses a Supabase-hosted append-only operation ledger as the canonical logbook.
Flight records and configuration are encrypted before upload. Supabase stores the
ciphertext plus the minimum account, membership, device, revision, acknowledgement,
migration, envelope, and redacted security-event metadata needed to operate the service.

The controlled Preview is deliberately workbook-led but not workbook-synchronized:

- an existing Electronic Logbook `2.0.3` workbook is a one-time migration source;
- the Windows updater validates and stages the workbook, signs the invited user in with
  Google, creates one hosted logbook, uploads encrypted operations and configuration,
  reads them back, and accepts completion only when the verified receipt matches;
- the updater preserves an untouched timestamped backup, installs an editable `3.0.0`
  workbook at the original filename, and stamps it `Moved to FlightLogX` with the
  migration time;
- after migration, Android is the normal editable logbook and automatically synchronizes
  with the hosted ledger;
- later Excel, CSV, and PDF files are fresh exports. They are not synchronized working
  copies; and
- Package Exchange, email-code sign-in, and recovery-code tools remain in Advanced or
  owner/support procedures. They are not part of normal migration, sign-in, recovery, or
  daily use.

The hosted ledger, not a workbook file and not a mutable hosted current-entry table, is
the source of truth. Each client materializes its current view from validated operation
history.

## Product Boundary

The Preview optimizes for one narrow, supportable journey:

1. The owner creates a `workbook_migration` invitation for one Google-account email and
   adds the same email to the approved Firebase App Distribution group.
2. The participant moves one existing workbook through the approved Windows updater.
3. The updater proves the encrypted hosted copy before it installs or stamps the new
   workbook.
4. The participant installs FlightLogX from Firebase and signs in with the same Google
   account.
5. The app discovers the completed membership, restores the logbook key through the
   managed-envelope service, replays the encrypted ledger, and reports success only after
   exact local persistence and acknowledgement.
6. The participant records all later flights in the app. Excel is used only for fresh
   exports or as a clearly local, unsynchronized copy.

This boundary removes the hardest and least useful part of the earlier design: safely
rewriting an open workbook while resolving two-way app/Excel conflicts. A workbook may
remain editable for the participant's own purposes, but its first add attempt in each
Excel session warns that those changes stay only in that spreadsheet.

## Client Responsibilities

### Windows migration client

The existing updater wizard is a temporary migration client, not a background sync agent.
It:

- creates an untouched timestamped backup before Google sign-in or hosted upload;
- validates supported workbook structure, rows, custom-field labels, currency override
  dates, cached totals, and the source fingerprint;
- uses the system browser, an approved loopback callback, and PKCE with SHA-256 for Google
  sign-in;
- requires the signed-in email to match the owner-managed invitation;
- begins or resumes the same idempotent migration for the same account and source
  fingerprint;
- creates and enrolls the managed account-recovery envelope before Android arrival;
- converts the workbook to encrypted configuration and operation revisions;
- independently reads back the hosted result and completes only with an exact verification
  receipt;
- installs the stamped `3.0.0` workbook only after hosted completion; and
- deletes temporary Windows migration credentials after successful completion.

A network interruption or retry reuses the same account, logbook, device, and migration.
It must not create a second plausible-looking logbook. If hosted migration completed but
workbook installation failed, the wizard reports that distinction and preserves the
backup rather than uploading again.

### Android app

Android is the steady-state client. It:

- stores the local logbook and pending operations encrypted;
- writes every edit durably before attempting network work;
- signs in normally through Android Credential Manager and Google;
- discovers an existing active membership before any initialization decision;
- restores the existing logbook key into Android Keystore through managed recovery on a
  clean install or replacement device;
- pulls, validates, decrypts, and materializes ordered hosted revisions locally;
- uploads pending encrypted operations idempotently;
- acknowledges only revisions durably held on that device; and
- remains usable offline, showing a truthful `Waiting`, `Offline`, or `Needs attention`
  state until safe convergence is possible.

The Preview does not use app-only initialization. That mode remains implemented for a
possible later cohort, but a `workbook_migration` invitation without a completed migration
must fail closed and tell the user to finish the Windows migration.

### Workbook after migration

The installed workbook is intentionally not a hosted client. It contains the migrated
rows and workbook features, the canonical `preview` channel, and hidden migration status
and time. It does not receive later app operations and does not upload later workbook
edits. No background process, open-time pull, save-time upload, paired workbook device,
refresh token, or continuing conflict policy is part of the accepted Preview design.

## Hosted Data Boundary

Use UUID identifiers and immutable timestamps. The implemented schema includes:

`accounts` and `logbook_memberships`:

- bind one Supabase Auth identity and invited email to an account;
- record onboarding mode and account status; and
- grant owner-first access to a logbook without storing Auth tokens.

`logbooks`:

- identify the canonical encrypted logbook and its format versions; and
- contain no plaintext current-entry fields.

`devices`:

- identify the temporary Windows migration device and each Android installation;
- bind devices to their owning account and platform label; and
- support pending, active, superseded, and revoked states.

`operations`:

- store immutable ordered revisions, operation identity and type, author device,
  ciphertext envelope, payload hash, and bounded timestamps;
- enforce unique operation ids and per-logbook revisions; and
- reject mismatched replay while making identical retry idempotent.

`configuration_revisions`:

- store encrypted custom-field and currency-override configuration separately from flight
  operation revisions; and
- restore configuration before operation replay on a new Android installation.

`operation_acks`:

- record the highest contiguous revision durably held by a device and its last upload,
  pull, sync time, and local queue state; and
- move only forward and never beyond hosted history.

`key_envelopes`:

- store only wrapped logbook keys for managed account recovery and individual devices;
- record versioned wrapping algorithms and revocation state; and
- never store a recovery secret or raw logbook key.

`workbook_migrations`:

- bind one invited account and source fingerprint to one lifecycle;
- retain pending, failed, or completed status, bounded failure information, verified
  counts, and the completion receipt; and
- expose lifecycle changes only through authenticated routines, not direct client table
  access.

`security_events`:

- record redacted invitation, sign-in, migration, recovery, activation, replay-rejection,
  revocation, and policy events; and
- exclude tokens, emails where unnecessary, identifiers in participant evidence,
  ciphertext, plaintext flights, and key material.

The hosted boundary must not add flight date, aircraft, route, remarks, crew, totals, or
other decrypted fields without a new privacy decision.

## Sync Contract

Android asks for missing revisions after its acknowledged cursor, validates and decrypts
each operation locally, appends durable local operations, and acknowledges only after
local persistence.

Pull requirements:

- bounded pages ordered by hosted revision;
- explicit highest revision and continuation state;
- rejection of gaps, rollback, malformed ciphertext metadata, unsupported formats, and
  duplicate operation identities with different hashes; and
- configuration restoration before materializing migrated operations on recovery.

Upload requirements:

- local durable save before network upload;
- authenticated writer membership and active device ownership;
- server-assigned monotonic revision inside the append transaction;
- bounded and validated encrypted envelopes;
- identical operation retry returns the existing revision; and
- identity reuse with different encrypted metadata is rejected and audited.

Normal synchronization starts after durable edits, at app launch or resume, after network
restoration, through bounded retry, and from a user-requested status refresh. Realtime
push is not required for the controlled Preview.

## Authentication And Key Recovery

Public signup is disabled. The owner provisions the hosted Auth identity and invitation;
clients cannot create an unknown account. Google is the normal identity for both the
Windows migration and Android arrival or recovery.

Google authentication proves account identity but does not derive the encryption key.
The managed-envelope service holds a versioned key-encryption key outside PostgreSQL. On
Android recovery it authenticates the account, authorizes the active membership, unwraps
the account envelope only in bounded service memory, rewraps the logbook key to the new
device's non-exportable Android Keystore public key, and returns only that device envelope.

The raw logbook key may exist only in bounded cryptographic process memory. It must never
be written to Supabase tables, browser storage, logs, diagnostics, analytics, crash
reports, or support evidence.

Email-code sign-in is an Advanced authentication fallback. The separately rate-limited
recovery-code path is an Advanced support fallback. Neither appears in the ordinary
Google recovery journey. Missing, corrupt, unavailable, or unauthorized managed recovery
must fail closed; it must never silently initialize another logbook.

See `docs/account-recovery-threat-model.md` for the detailed trust and failure model.

## Offline And Conflict Policy

Offline editing is an Android property, not a reason to retain two-way workbook sync.
Every accepted operation remains recoverable through the retention window. Conflict
resolution adds operations instead of rewriting history.

Automatic handling includes identical retry, independent edits, ordered remote replay,
and monotonic acknowledgements. Same-entry concurrent corrections, delete-versus-correct,
configuration reinterpretation, unsupported schema, rollback, or key/revocation failures
must become `Needs attention` without blocking unrelated local entry work.

Workbook/app conflict resolution is out of scope because the workbook ceases to be a
synchronized client after migration. A later request for live workbook sync would require
a new architecture decision and new acceptance evidence.

## User-Facing States

The normal app may show:

- `Synced`: local changes are durably hosted, missing revisions are durably materialized,
  and this device acknowledged the contiguous cursor;
- `Waiting`: local work is safe but upload, pull, acknowledgement, or retry is pending;
- `Offline`: the encrypted local logbook is usable but the hosted service is unreachable;
- `Signing in`: Google authentication or session renewal is in progress; and
- `Needs attention`: automatic progress stopped because continuing could hide data,
  identity, schema, recovery, revocation, replay, or rollback risk.

Normal UI must not expose databases, packages, hashes, keys, recovery codes, logbook ids,
file pickers, or migration internals. Technical detail belongs in redacted diagnostics and
Advanced/support tools.

## Security And Continuity

- Enforce Row Level Security and account, membership, and device ownership on every
  hosted operation.
- Keep flight data and configuration encrypted end to end.
- Store refresh credentials only in platform-protected storage and remove temporary
  Windows migration credentials after completion.
- Rate-limit and audit invitation, sign-in, recovery, replay, and revocation failures.
- Keep diagnostics redacted and separate from explicit encrypted data backups.
- Preserve local encrypted Android history and logical hosted exports for service
  migration or restore rehearsal.
- Rehearse restore into a disposable local or separate Sydney project; an administrator
  reset is not account recovery.
- Monitor current Supabase quotas and project pausing in the dashboard. The Preview may
  stay on Free only while usage, continuity, and support risk remain acceptable.

Supabase currently documents two active Free projects, 500 MB database size per project,
50,000 monthly active users, 5 GB egress, 5 GB cached egress, 1 GB storage, and pausing
after low activity over a seven-day period. These are operating assumptions, not product
guarantees; recheck them before each launch decision.

References:

- Supabase billing: https://supabase.com/docs/guides/platform/billing-on-supabase
- Supabase project pausing: https://supabase.com/docs/guides/platform/free-project-pausing
- Supabase Google sign-in: https://supabase.com/docs/guides/auth/social-login/auth-google
- Supabase redirect allow list: https://supabase.com/docs/guides/auth/redirect-urls
- Supabase Row Level Security:
  https://supabase.com/docs/guides/database/postgres/row-level-security

## Proven Preview State

Owner acceptance has demonstrated:

- exact one-time migration of three flights, four custom-field labels, currency dates,
  totals, and date range from a disposable `2.0.3` workbook;
- one hosted logbook, one completed migration, encrypted revisions, configuration, owner
  membership, and active managed recovery envelope;
- an untouched timestamped `2.0.3` backup and an installed `3.0.0` workbook stamped
  `Moved to FlightLogX`;
- first install and clean reinstall on the permanent Pixel recovered the same logbook via
  Google and managed recovery without a picker, package, recovery-code prompt, empty
  replacement, duplicate logbook, or duplicate entry;
- a stale mixed mobile publish failed closed before activation, then the corrected build
  resumed safely and superseded the failed pending device; and
- ordinary Android edit/delete/Undo/restore, validation, warnings, exports, offline-safe
  storage, hosted acknowledgement, and state-preserving Preview update behavior.

Durable evidence:

- `artifacts/gate4-owner-final-journey-20260908/verification.json`
- `artifacts/gate3d-disposable-android-recovery-20260907/verification.json`
- `artifacts/gate3d-disposable-cleanup-20260908/verification.json`

## Non-Goals

- Continuing workbook/app synchronization.
- A Windows tray process, service, startup task, or separate companion application.
- Direct editing of a workbook in OneDrive, Google Drive, Dropbox, or another cloud-file
  provider.
- Public signup, billing, public uptime commitments, or public release hardening.
- Hosted plaintext flight records, raw keys, recovery material, or a mutable current-entry
  source-of-truth table.
- App-only onboarding during the workbook-led controlled Preview.
- Realtime/push sync, iPhone/iPad support, or app-store distribution before an explicit
  later decision.
