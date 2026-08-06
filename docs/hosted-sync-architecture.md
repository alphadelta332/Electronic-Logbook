# Hosted Sync Architecture Decision

Status: accepted for private-pilot architecture planning

Date: 2026-08-06

External facts checked: 2026-08-06

## Decision

Electronic Logbook will replace routine manual package transport with a
Supabase-hosted operation ledger for the private pilot.

The pilot architecture is:

- Supabase Auth provides invited-user authentication.
- Supabase-hosted PostgreSQL stores the canonical append-only operation ledger and the
  minimum routing, membership, device, acknowledgement, pairing, and audit metadata.
- Android is the primary client and must be able to initialise and use a logbook without
  Excel.
- Excel is an optional synchronized projection of the hosted logbook, connected through
  the existing updater distribution's hidden on-demand sync mode.
- Both Android and Excel-side sync retain encrypted local caches and offline operation
  queues so users can keep working during temporary network or service outages.
- Flight-operation payloads are encrypted end to end before upload. Hosted storage may
  route, order, acknowledge, audit, and replicate ciphertext, but must not require
  recovery keys or plaintext flight records.

The hosted ledger, not a mutable workbook file or editable current-entry table, is the
source of truth. Current logbook views are projections produced from validated operation
history.

## Context

The current mobile and workbook exchange path already proves several important pieces:

- durable operation-style synchronization through portable packages;
- schema-v2 workbook projection and round-trip validation;
- local encrypted app storage;
- recovery-oriented export/import paths;
- updater-side workbook validation, backup, and handoff safeguards.

Manual package exchange is too visible and too file-centric for normal daily use. The
next release direction is a small invitation-only Android-first pilot where routine sync
is automatic, encrypted, and independent of user-owned cloud storage. Excel remains
valuable, but it should behave as a paired client rather than the canonical container.

## Consequences

The first hosted milestone should optimize for a narrow, auditable pilot:

- use Supabase Free in the Sydney region for development and the private pilot;
- keep public registration, billing, team administration, and public uptime promises out
  of scope;
- design Row Level Security, unique revision insertion, device membership, and audit
  trails as part of the service boundary instead of relying only on client behavior;
- preserve offline-first local writes and local encrypted history on every paired client;
- keep the current Package Exchange workflow as Advanced recovery and portability, not
  normal onboarding or daily sync;
- defer realtime subscriptions, premium hosted recovery, custom domains, and managed
  monitoring until pilot evidence shows they materially improve reliability or support.

This decision deliberately does not finalize the physical database schema, RLS policy
text, sync API contract, authentication UX, key-envelope format, or conflict-resolution
rules. Those remain separate milestone gates so they can be reviewed with threat-model
and acceptance evidence.

## Non-Goals

- No separate Windows companion application, installer, tray process, service, or startup
  item.
- No direct editing of a workbook stored inside OneDrive, Google Drive, Dropbox, or any
  other user-owned cloud file provider.
- No hosted plaintext flight records or hosted recovery keys.
- No mutable hosted "current entry" table as the source of truth.
- No public-product infrastructure or paid-plan dependency for the private pilot unless
  a documented trigger is reached.

## Supabase Pilot Baseline

Create separate Supabase projects for development and the private pilot. Select the
specific `ap-southeast-2` Oceania (Sydney) region for both projects; Supabase documents
that project data is stored in the chosen primary region, and lists Sydney as
`ap-southeast-2`.

Use the Free plan until a documented upgrade trigger is reached. As of the decision date,
the Supabase pricing page lists the Free plan as $0/month with 50,000 monthly active
users, 500 MB database size, 5 GB egress, 5 GB cached egress, 1 GB file storage,
community support, two active projects, and pausing after one week of inactivity. Treat
those numbers as operational assumptions to recheck before project creation and before
pilot launch.

For the invite-only pilot, disable public sign-up and use email sign-in only for existing
invited users. Supabase Auth supports disabling new sign-ups, and passwordless email OTP
or magic-link calls should set `shouldCreateUser` to `false` so an unknown address cannot
self-register from the client.

References:

- Supabase pricing: https://supabase.com/pricing
- Supabase regions: https://supabase.com/docs/guides/platform/regions
- Supabase Auth configuration:
  https://supabase.com/docs/guides/auth/general-configuration
- Supabase passwordless email:
  https://supabase.com/docs/guides/auth/auth-email-passwordless

## Minimum Hosted Schema

Use UUID primary keys and immutable timestamps throughout. Tables that identify people,
devices, membership, pairing, and acknowledgements may be plaintext metadata; flight
operation payloads and key envelopes remain ciphertext.

`accounts`:

- maps one Supabase Auth user to one Electronic Logbook account profile;
- stores display name, invited email, account status, created time, disabled time, and
  deletion-request state;
- does not duplicate access tokens or refresh tokens.

`logbooks`:

- stores logbook id, owner account id, display name, created time, current schema version,
  operation format version, retention policy marker, and deletion status;
- contains no current-entry fields.

`logbook_memberships`:

- grants account access to a logbook with role `owner`, `writer`, or `viewer`;
- records who granted access, when it was accepted, and whether the grant is revoked;
- starts with owner-only membership for the private pilot.

`devices`:

- identifies each app install or paired workbook by device id, account id, device type,
  platform label, public signing key or key fingerprint, first-seen time, last-seen time,
  status, and revocation time;
- distinguishes Android app devices from workbook/updater devices.

`operations`:

- stores immutable revisions keyed by `(logbook_id, revision)`;
- stores operation id, parent or base revision metadata, author device id, operation type,
  operation format version, ciphertext payload, payload nonce, payload authentication tag
  where applicable, payload hash, client-created time, received time, and optional
  redacted routing hints;
- enforces uniqueness on operation id and `(logbook_id, revision)`;
- rejects updates and deletes except for administrative tombstoning required by account
  deletion policy.

`operation_acks`:

- tracks the highest contiguous revision durably held by each device;
- records last upload revision, last pull revision, last successful sync time, and the
  client-reported local queue state.

`pairing_requests`:

- stores one-time workbook or replacement-device pairing attempts;
- includes requester account, logbook id, target type, short code hash, expiry, consumed
  time, approved device id, and failure count;
- never stores the end-to-end key in plaintext.

`key_envelopes`:

- stores encrypted logbook-key envelopes for trusted devices and recovery flows;
- includes logbook id, recipient device id or recovery method, wrapping algorithm,
  key-version id, ciphertext, nonce, created time, created-by device id, expiry where
  relevant, and revocation state.

`security_events`:

- records account, logbook, device, event type, severity, actor, source metadata, created
  time, and redacted details for sign-in, invitation, pairing, revocation, replay
  rejection, rollback rejection, RLS-denied access, and deletion events.

## Sync Contract

Clients synchronize by asking for missing revisions after their acknowledged cursor,
validating and decrypting each operation locally, appending local unsent operations, and
recording acknowledgements only after durable local persistence.

Pull:

- client sends logbook id, device id, known operation-format version, and last contiguous
  acknowledged revision;
- service returns a bounded page ordered by revision, plus `has_more` and the server's
  current highest revision;
- client rejects gaps, duplicate operation ids with mismatched payload hashes, invalid
  ciphertext metadata, unsupported future operation versions, and revisions below a
  rollback floor.

Upload:

- client saves local operation durably before network upload;
- service inserts operations append-only through a transaction or stored procedure that
  assigns the next revision and enforces membership, device status, payload bounds,
  operation id uniqueness, and idempotent retry behavior;
- retrying the same operation id with the same payload returns the existing revision;
- retrying the same operation id with different payload metadata is a security event and
  a rejected upload.

Acknowledgement and checkpoints:

- a device acknowledges only the highest contiguous revision it has durably stored;
- the service may record per-device lag but must not treat an ack as proof the user has
  visually reviewed the change;
- compaction is represented by a checkpoint operation that summarizes state after a
  revision while preserving original operation history for the retention period;
- clients that cannot understand a future checkpoint or operation format keep their local
  history and enter `Needs attention`.

Retries and pagination:

- all sync calls are safe to retry;
- pages are small enough for mobile network recovery and workbook open-time pulls;
- exponential backoff is client-side and bounded so normal editing is never blocked by a
  hosted outage.

Deletion and retention:

- account deletion disables access first, exports or confirms recoverability, then
  tombstones hosted metadata and ciphertext according to the documented deletion policy;
- operation history is not physically rewritten during normal conflict resolution.

## Authentication Contract

Pilot authentication is invited email sign-in. Public self-registration stays disabled.
Unknown email addresses receive a generic failure path that does not disclose whether a
pilot account exists.

Android uses Supabase Auth sessions with refresh tokens stored behind platform secure
storage. Sign-out removes hosted refresh credentials from the device but leaves encrypted
local logbook data available only if the user still has the device key or recovery
material required by the local storage design.

Workbook sign-in uses browser-based or short device-code authentication launched by the
existing updater. VBA never stores shared service secrets. Refresh credentials are stored
in Windows Credential Manager under a scoped Electronic Logbook target and are removable
from the workbook Account or Advanced recovery surface.

Revocation disables a device row and invalidates outstanding pairing requests and key
envelopes for that device. Replacement-device recovery requires account authentication
and either a trusted-device approval or recovery-code flow before a new key envelope is
issued.

## Key Ownership And Pairing

The app creates the logbook identity, device identity, and initial logbook encryption key
for app-only initialization. Android stores local key material behind Android
Keystore-backed native storage. The hosted service stores only wrapped key envelopes and
cannot decrypt flight operations.

Recovery code:

- generated during initialization or first account setup;
- shown once and confirm-tested before the app claims recovery is configured;
- derives or unlocks a recovery wrapping key without uploading the recovery secret;
- loss of every trusted device and recovery code is unrecoverable by design.

Trusted-device enrollment:

- starts from a signed-in existing device or a recovery-code flow;
- creates a new device row and key envelope scoped to that device;
- records a security event and allows later revocation.

Workbook pairing:

- starts from an explicit `Connect to Electronic Logbook` workbook action;
- authenticates through the updater, verifies the account, logbook identity, workbook
  identity, and user consent;
- transfers or unwraps the logbook key locally;
- stores workbook refresh credentials in Windows Credential Manager and any workbook
  identity metadata in existing workbook storage;
- expires unfinished pairing requests quickly and consumes successful requests exactly
  once.

## Workbook Synchronization Points

The workbook is an optional projection and must never be rewritten underneath an active
edit. VBA owns workbook mutation while the workbook is open; the hidden updater sync
process owns authentication, transport, encryption, and merge planning.

Trigger sync:

- during workbook open, before declaring the workbook current;
- after a successful save;
- after completed entry, correction, deletion, custom-field, or identity-affecting
  changes;
- during a guarded idle refresh when Excel is not editing, calculating, modal, protected
  in an unexpected state, read-only, or inside an event recursion path;
- during close, with a bounded attempt that does not trap the user in Excel.

Queue remote changes while Excel is busy, locked, read-only, duplicated, moved, or
actively edited. If the hidden process detects a required projection update, it returns a
bounded local result for VBA to apply under existing protection and save safeguards. A
closed-workbook replacement or repair may use existing updater preview, backup, journal,
handoff, and restore safeguards only when the workbook is not safely mutable in-place.

## Headless Updater Sync Contract

The existing updater release artifact gains a hidden command such as `sync` with explicit
subcommands for probe, pull-plan, apply-result handoff, upload, auth, pair, and status.
The workbook resolves, downloads, caches, and verifies the updater through the existing
release-channel path.

The headless process:

- starts only on demand and exits promptly;
- shows no console window for routine sync;
- authenticates through the user's account tokens, never through shared workbook secrets;
- exchanges request and result files in a bounded local handoff directory with strict
  ownership, path, and size validation;
- returns compact machine-readable status to VBA;
- preserves technical detail in logs and Advanced/support diagnostics;
- launches the visible updater wizard only for normal workbook updates, account
  reauthentication, recovery, or failures that require user action.

## App Synchronization Triggers

The Android app writes every local operation to encrypted local storage before any hosted
work starts. Network sync is best-effort and repeatable.

Trigger sync:

- immediately after durable local edits;
- at app launch and resume;
- when Android reports network restoration;
- through WorkManager-backed retry for pending uploads and pulls;
- from a manual status refresh in Account or Sync status.

Start with bounded polling and lifecycle-triggered pulls. Add realtime notification only
if pilot evidence shows polling/resume sync creates material latency, battery, or support
problems.

## Conflict Policy

Every accepted operation remains recoverable through the retention window. Conflict
resolution adds more operations; it does not rewrite history.

Automatic choices:

- duplicate delivery with identical operation id and payload hash is idempotent;
- independent edits to different entries or metadata fields merge in revision order;
- workbook rename or move is not a conflict when embedded workbook identity matches.

User choice required:

- concurrent changes to the same entry field where neither operation dominates;
- delete versus correction of the same entry;
- custom-field label changes that would reinterpret existing values;
- currency override changes that alter downstream totals or warnings;
- two workbook files claiming the same identity with divergent unuploaded operations;
- key loss, revoked device, or account recovery that could strand local ciphertext.

Unresolved conflicts keep both versions visible in history and put the affected client in
`Needs attention` without blocking unrelated local entry work.

## Privacy And Threat Model

Primary risks and controls:

- RLS mistakes: enforce owner/member/device predicates on every hosted table; include
  adversarial cross-account tests before pilot use.
- Token theft: store refresh tokens only in Android secure storage or Windows Credential
  Manager; support sign-out, device revocation, and account disablement.
- Key theft: never upload plaintext logbook keys or recovery codes; scope envelopes to
  devices; revoke envelopes with devices.
- Ciphertext leakage: keep payloads encrypted end to end; minimize searchable metadata;
  avoid plaintext details in diagnostics and audit rows.
- Metadata leakage: store only routing fields needed for synchronization and support;
  do not store flight dates, aircraft, routes, or remarks outside ciphertext unless a
  later explicit product decision accepts that exposure.
- Malicious operations: validate identifiers, sizes, schema versions, author device,
  membership, monotonic revision assignment, and payload hashes at insertion.
- Replay: operation ids are globally unique per logbook and idempotent only with matching
  hashes.
- Rollback: clients track acknowledged cursors and reject server responses below local
  durable history without an explicit recovery procedure.
- Account deletion: disable access first, revoke devices, confirm export/recovery state,
  then remove or tombstone hosted data according to the deletion policy.
- Service administration: administrators may manage accounts and metadata but cannot
  decrypt operation payloads.
- Support access: diagnostics are redacted and must not include recovery codes, tokens,
  decrypted payloads, or unnecessary account metadata.
- Hosted-provider compromise: retained encrypted client history and logical exports must
  allow migration to a new project or provider without plaintext exposure.

## Pilot Cost And Continuity Rules

Stay on Supabase Free while the pilot is small, invited, recoverable from client history,
and comfortably below the published Free-plan limits. Recheck published limits before
each pilot gate because hosted-provider limits can change.

Upgrade to a paid plan only when one of these triggers occurs:

- public availability starts;
- free-project pausing disrupts normal pilot use;
- monitored usage approaches half of an important quota such as database size, egress,
  storage, or monthly active users;
- pilot users begin treating the hosted copy as their only practical recovery source;
- managed daily backups, longer log retention, email support, or other paid reliability
  features become necessary for support.

Continuity requirements before pilot use:

- logical export of hosted metadata and ciphertext operation history;
- restore rehearsal into a separate project;
- retained complete encrypted operation history on paired clients;
- documented migration steps for creating a replacement Sydney project if region or
  project migration is needed;
- usage alerts where Supabase exposes them and local service-health checks in support
  diagnostics.

## Release Acceptance Matrix

Both app and workbook expose the same user-facing sync states:

`Synced`:

- all local operations are durably saved locally, uploaded, accepted by the hosted ledger,
  and acknowledged by this device;
- latest pulled remote revision is durably stored and projected locally.

`Waiting`:

- local work is saved but upload, pull, ack, workbook-safe-apply, or remote projection is
  pending;
- this includes normal backoff and Excel-busy queues.

`Offline`:

- local encrypted data is available and editable, but hosted service cannot currently be
  reached.

`Signing in`:

- the client is authenticating, refreshing a token, completing pairing, or waiting for an
  email OTP/magic-link/device-code step.

`Needs attention`:

- the client cannot safely continue automatic sync because of conflict, revoked device,
  expired authentication that needs user interaction, key loss, unsupported schema,
  duplicate workbook identity, failed recovery, or suspected rollback/replay.

Acceptance evidence must prove app-only initialization and editing, app-to-workbook sync,
workbook-to-app sync, offline edits, duplicate and out-of-order delivery, token expiry,
device revocation, workbook busy/read-only cases, service outage recovery, database
restore/migration, and diagnostics redaction.

## Migration Plan

Existing app data:

- read the current encrypted local store and operation queue;
- create a hosted account, logbook id, device id, and initial acknowledgement cursor;
- upload only missing encrypted revisions after local validation;
- preserve pending local operations and retry idempotently.

Schema-v2 workbooks:

- embed or derive stable workbook identity during pairing;
- project current workbook state into operation history using existing portable
  validation rules;
- compare with hosted revisions before first upload;
- require user choice if both sides contain divergent authoritative history.

Retained browser keys and local key material:

- migrate valid material into Android Keystore-backed storage during app upgrade;
- keep a rollback-safe local backup until the new secure-storage read path passes;
- never upload raw key material.

Recovery files and manual packages:

- continue to import/export through Advanced recovery;
- after import, validate the package, merge locally, then synchronize missing encrypted
  revisions with the hosted ledger;
- do not require package import for normal onboarding.

Manual package users:

- existing package exchange remains supported as a recovery bridge;
- once signed in, package-derived operations become ordinary local operations uploaded to
  the hosted ledger;
- no re-enrolment or data loss is acceptable for a valid existing app/workbook pair.
