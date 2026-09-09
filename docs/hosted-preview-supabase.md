# FlightLogX Preview Hosted Supabase Setup

Status: implemented controlled-Preview setup

Last reconciled: 2026-09-09

This document is the repeatable project setup note for the invitation-only Preview. It is
safe to commit because it contains no Supabase URLs, anon keys, service-role keys, JWT
secrets, SMTP credentials, or Preview emails.

## Project Creation

Create two separate Supabase projects:

- Development: used for local and CI-adjacent integration work.
- FlightLogX Preview: used only for invited Preview users.

Use the Sydney region, `ap-southeast-2`, for both projects. Service-role keys, database
passwords, and management access tokens are secrets and must remain in protected local or
CI secret storage. The project URL and anon key are public client configuration, but keep
them out of source files, logs, diagnostics, screenshots, and workbook metadata. They may
be embedded in a built client artifact for the intended environment.

The first FlightLogX Preview remains on Supabase Free until a documented upgrade trigger is
reached. Recheck current Supabase Free limits, region availability, and Auth behavior
before creating the Preview project and again before inviting real users.

## Apply Migrations

Install and authenticate the Supabase CLI locally, then link each project separately.
Apply the same checked-in migrations to development first, then to the Preview
project after review.

```powershell
supabase login
supabase link --project-ref <development-project-ref>
supabase db push
```

For the Preview project, relink intentionally before pushing:

```powershell
supabase link --project-ref <preview-project-ref>
supabase db push
```

The migration chain begins with
`supabase/migrations/20260806000000_hosted_pilot_foundation.sql`. Apply every checked-in
migration in timestamp order; do not stop after the foundation. Later migrations add and
harden managed recovery, replacement-device activation, Advanced recovery-code support,
workbook-migration invitation and lifecycle rules, encrypted configuration revisions,
and restored final-schema reporting and append protections.

Together the migrations create the hosted ledger schema, constraints, indexes, RLS
policies, and bounded routines for:

- invited accounts;
- owner-managed `app_only` and `workbook_migration` invitation modes;
- logbooks and owner-first memberships;
- Android and workbook devices;
- append-only encrypted operations;
- per-device acknowledgements;
- pairing and recovery attempts;
- managed account, recovery-code, and device-wrapped key envelopes;
- one-time workbook migration state and exact verification receipts;
- encrypted configuration revisions;
- redacted security events.

## Auth Configuration

Configure Auth in the Supabase dashboard for each project:

- Disable public self-registration.
- Enable email sign-in only for invited users.
- Send Auth email through Resend using `auth-dev.flightlogx.app` for development and
  `auth.flightlogx.app` for FlightLogX Preview, with a separate sending-only Resend credential
  for each Supabase project. Use `FlightLogX <signin@...>` as the sender.
- Change the shared **Magic Link or OTP** template to display `{{ .Token }}` and no
  clickable confirmation link. Set Email OTP length to 6, expiration to 600 seconds, email sends
  to 30 per hour, and OTP requests to 30 per hour. Advanced/support instructions request
  only the six-digit code and tell the user to check junk or spam if the message does not
  arrive. The clients retain URL parsing as an undisclosed support fallback for
  already-issued links; do not advertise either path in normal UI or participant steps.
- Keep phone, anonymous, and public signup providers disabled. Email creates the
  owner-managed invitation and remains an Advanced/support fallback. Google is the only
  enabled OAuth provider; it is the normal Windows migration sign-in and returning-user
  recovery identity documented in `docs/account-recovery-threat-model.md`.
- Add `http://127.0.0.1:*/flightlogx-auth/**` to the Auth redirect allow list. The Windows
  updater opens the system browser, binds a random loopback port and one-time callback path,
  and uses PKCE with SHA-256. Do not replace this with an embedded browser, a non-loopback
  callback, or a fixed authorization-code verifier.
- Keep the Google OAuth client ID and secret in Google/Supabase configuration. Neither value
  is required in the updater executable: it contains only the public Supabase project URL
  and publishable/anonymous key and shows the signed-in Google account email after the code
  exchange succeeds.
- In the Google Cloud project that owns that Web OAuth client, retain a separate Android
  OAuth client for every distributed Android package/signing-certificate pair. The
  permanent Preview client uses package `com.alphadelta.electroniclogbook` and the SHA-1
  of the permanent Preview signing certificate. Do not replace a development client when
  adding it. A SHA registered only in the separate Firebase App Distribution project does
  not register the app with the OAuth project selected by `googleWebClientId`.
- Client sign-in calls must pass the SDK option `shouldCreateUser: false`, which maps to
  the REST field `create_user: false`, so an unknown email address cannot create a new
  account from the app.
- Unknown email, disabled account, and revoked-device paths must use generic user-facing
  language that does not reveal whether an address belongs to the Preview.

Invitations are created administratively. The app and workbook must not contain a shared
service credential capable of creating users. Set `accounts.onboarding_mode` to
`workbook_migration` for the workbook-led Preview. The invitation RPC verifies the signed-in
identity against `accounts.invited_email`, and rejects Android registration for that mode
so the app must enter managed recovery after the Windows migration creates the hosted
membership. Existing invitations default to `app_only` for backward compatibility; do
not use that default for new workbook-led canaries.

Workbook-led invitations use the authenticated `begin_workbook_migration`,
`get_workbook_migration_status`, `fail_workbook_migration`, and
`complete_workbook_migration` RPCs. `begin` atomically creates one migration logbook,
owner membership, and workbook device; the account and source fingerprint uniquely bind
all retries to those same resources. A failed attempt retains only a bounded failure code
and resumes the same lifecycle. `complete` succeeds only when the hosted encrypted
operation count matches the updater's verified count and stores a SHA-256 verification
receipt. The underlying lifecycle table has no authenticated table access. Even after
completion, the invitation RPC does not directly activate Android: the app must use the
managed recovery path before a replacement device becomes active.

Verify the redacted remote configuration after setup and before each canary. The private
Preview preflight also fails if Google or the Windows loopback callback is missing:

```powershell
.\tools\Test-HostedEmailOtpConfiguration.ps1 -Environment development
.\tools\Test-HostedEmailOtpConfiguration.ps1 -Environment preview
```

The development wizard publishing workflow reads the project URL from the repository
variable `ELECTRONIC_LOGBOOK_DEVELOPMENT_SUPABASE_URL` and the anon key from the repository
secret `ELECTRONIC_LOGBOOK_DEVELOPMENT_SUPABASE_ANON_KEY`. It embeds those public client
settings in the wizard executable and validates the finished executable before publishing.
Do not reuse the development values for a Preview or release build.

`public.accept_hosted_invitation(...)` is the app-only initialization routine. It activates
an invited account, registers the first device, and records a redacted security event, but
the controlled workbook-led Preview must not call it to create Android state. A
`workbook_migration` participant instead uses the authenticated Windows migration
lifecycle first, then Android managed recovery against the completed membership and
logbook. Both paths reject disabled accounts and authenticated users without a matching
invitation, which keeps accidental public self-registration outside the hosted ledger
even if an Auth identity exists.

Portable client implementations should expose these local auth outcomes without relying
on live Supabase tests: invitation required, public registration blocked, expired or
invalid verification, refresh-token revocation, account disabled, device revoked, and
signed out.

## Schema Boundary

The hosted database stores synchronization metadata and ciphertext only. The
`operations.payload_ciphertext`, `payload_nonce`, `payload_tag`, and `payload_hash`
columns are the only place flight operation payload material belongs. Do not add columns
for flight date, aircraft, route, remarks, crew, totals, or other decrypted logbook
fields without a new privacy decision.

Operation writes should go through `public.append_hosted_operation(...)`. It:

- requires active writer membership and an active device owned by the authenticated
  account;
- assigns the next hosted revision inside a transaction;
- rejects missing identifiers, unsupported operation formats, non-array parent metadata,
  incomplete encrypted envelopes, oversized ciphertext, and plaintext-looking payloads;
- accepts idempotent retries with the same operation id and payload metadata;
- records and rejects replay attempts that reuse an operation id with different payload
  metadata.

Pulls should use `public.read_missing_operations(...)`, which clamps page size to 200
rows and returns ordered revisions plus the current highest revision. Acknowledgements
should use `public.record_operation_ack(...)`, which only moves the durable cursor
forward for the authenticated account's own active device and rejects checkpoints beyond
hosted history.

Portable clients should preserve the same local behavior through
`IHostedLogbookLedger`: payloads must already be encrypted, the payload hash must be the
lowercase SHA-256 hex digest used for replay detection, retries are idempotent only when
the encrypted payload metadata matches, pull pages are bounded to 200 operations, and
acknowledgements are monotonic.

## Health, Diagnostics, And Restore

Use the legacy remote function `public.get_hosted_pilot_health()` for pre-Preview and
weekly Preview checks. Its rename is deferred until the external Supabase migration. It
returns active account, device, operation, and estimated database-size counts plus
conservative upgrade-trigger labels. Treat those triggers as a review point, not as
current Supabase plan documentation; confirm the live plan limits in the Supabase
dashboard before deciding whether to continue on Free or upgrade.

Use `public.create_redacted_hosted_diagnostics(...)` for support bundles. It redacts
project URLs, keys, account IDs, logbook IDs, and omits operation ciphertext. If the user
explicitly requests a hosted data backup, create it separately rather than attaching it
to diagnostics.

Use `public.create_hosted_logical_export_manifest(...)` as the owner-only restore
rehearsal entrypoint. The manifest records counts and the highest hosted revision for a
logbook; the actual restore rehearsal should export the matching logical rows from a
development or Preview project and import them into a separate Sydney project or disposable
local database before inviting Preview users.

## Local Secrets

Use environment variables or the platform's secret store for project-specific values.
Suggested local names:

```text
ELB_SUPABASE_DEV_URL
ELB_SUPABASE_DEV_ANON_KEY
ELB_SUPABASE_PREVIEW_URL
ELB_SUPABASE_PREVIEW_ANON_KEY
```

Service-role keys are for administrative scripts only and must never be bundled into the
Android app, workbook, updater, package exchange files, or diagnostics.

Managed recovery uses a different local secret file for each hosted project:

```text
%LOCALAPPDATA%\ElectronicLogbook\Supabase\recovery-envelope\development.env
%LOCALAPPDATA%\ElectronicLogbook\Supabase\recovery-envelope\private-pilot.env
```

The Preview recovery filename above is a retained local secret alias. Do not rename it
independently; migrate it with the external project names and transfer manifest.

Never reuse one project's ingress key pair or KEK in the other project. Create a missing
file with `tools\RecoveryEnvelopeSecretGenerator`, deploy it with `supabase secrets set
--env-file <path> --project-ref <ref>`, and retain it only through the trusted local
development transfer workflow. `Invoke-LocalDevelopmentTransfer.ps1 -Action Verify`
confirm-tests the RSA pair and KEK length without printing secret material.

For an owner-only permanent-package Preview build, create the local gitignored mobile
runtime config:

```powershell
.\tools\New-MobileHostedSyncLocalConfig.ps1 `
  -SupabaseUrl "https://<preview-project-ref>.supabase.co" `
  -AnonKey "<preview-anon-key>" `
  -PlatformLabel "Pixel 8 Pro" `
  -DisplayName "Project owner"
```

This writes `mobile/src/ElectronicLogbook.Mobile/wwwroot/hosted-sync.local.json`, which
is gitignored. Use the Preview project URL and anonymous key only. Build through the
repository's clean permanent-package Preview workflow; do not install the development
package as a substitute. The build must prove the packaged config, permanent application
id, signing certificate, Android version code, first-party asset manifest, and APK hash
before Firebase distribution.

### Managed recovery implementation

Managed recovery discovers existing membership before any device or logbook creation,
restores the key into Android Keystore, restores the latest encrypted custom-field and
currency-override configuration before operation replay, verifies the completed workbook
migration receipt, and activates the replacement device only after exact local persistence
and hosted acknowledgement. A `workbook_migration` invitation without a completed hosted
logbook is blocked from Android initialization and tells the participant to finish the
Windows migration.

The normal participant action is Google sign-in. Missing, corrupt, unauthorized, or
unavailable managed recovery stops before activation and never creates an app-only empty
replacement. Email-code sign-in, recovery-code restore, Package Exchange, and diagnostic
detail remain Advanced/support tools.

The owner clean-reinstall journey has passed on the permanent Pixel. A stale mixed publish
first failed closed before activation; the clean-publish repair resumed safely, superseded
the failed pending device, restored exact entries and totals, and created no duplicate
logbook or operation. Durable evidence is in
`artifacts/gate4-owner-final-journey-20260908/verification.json`.

For private owner verification tools, configure only the desktop values required by the
specific script and never print them:

```text
ELB_SUPABASE_PREVIEW_DB_URL
ELB_SUPABASE_PREVIEW_ACCESS_TOKEN
ELB_SUPABASE_PREVIEW_REFRESH_TOKEN       # only used when the access token is expired
ELB_SUPABASE_PREVIEW_SERVICE_ROLE_KEY    # desktop administrative validation only
ELB_SUPABASE_PREVIEW_DEVICE_ID
```

The Preview tools also accept corresponding legacy `ELB_SUPABASE_PILOT_*` names during
the compatibility window. Remove those aliases only after every retained owner and canary
resource uses the canonical Preview names.

The redacted preflight verifies packaged-config parity, JWT project/role/expiry, Auth and
database access, project health and region, signup/provider restrictions, Google loopback
configuration, and the expected hosted account/logbook state. Do not continue when any
check fails. Fix the proven subsystem and rerun the check; do not clear app data, reset the
database, or initialize a replacement logbook.

## Verification Before Preview Use

Before provisioning the external canary:

- run the migration against a disposable development project;
- inspect Supabase Security Advisor and Performance Advisor output;
- execute adversarial RLS tests for cross-account reads, writes, device spoofing,
  replayed operations, revoked devices, disabled accounts, and acknowledgement rollback;
- rehearse logical export and restore into a separate Sydney project;
- verify diagnostics redact URLs, keys, tokens, user emails, and ciphertext payloads
  unless the user explicitly exports a hosted data backup;
- confirm the final owner customer-surface dress rehearsal passed with no open S0-S2
  issue; and
- run `tools\Add-FlightLogXParticipant.ps1` with `-WhatIf` before its mutating run so the
  same Google email is bound to `workbook_migration` and the approved Firebase group.

Run the checked-in RLS harness only against a disposable local database or development
project:

```powershell
supabase db reset
psql "postgresql://postgres:postgres@127.0.0.1:54322/postgres" -v ON_ERROR_STOP=1 -f supabase/tests/hosted_preview_rls.sql
```

The harness is `supabase/tests/hosted_preview_rls.sql`. It seeds synthetic
`example.invalid` users and rolls its transaction back.

Run the hosted managed/recovery-code rehearsal only against the development project:

```powershell
.\tools\Invoke-HostedRecoveryRehearsal.ps1
```

It obtains project credentials from the private local configuration, uses an
administrator-generated email OTP without sending mail, exercises both replacement
paths against a non-empty encrypted ledger, and removes the disposable Auth identity
and hosted rows. It fails before creating disposable state unless the configured development
project is `ACTIVE_HEALTHY`, is the named Sydney project, has public signup disabled, and
has email as its only enabled external Auth provider. PostgreSQL 17 is required because the
append-only operation trigger is
bypassed only inside the narrowly scoped cleanup transaction; normal API deletion
remains blocked.

Run the separate disposable workbook journey against the same development project when a
schema, migration, recovery, or client change could affect the path:

```powershell
.\tools\Invoke-HostedRecoveryRehearsal.ps1 -WorkbookMigrationJourney
```

This mode signs a synthetic invited account into the production Windows client, proves a
pending retry reuses the same hosted resources and Credential Manager material, uploads and
verifies a two-flight encrypted migration, then runs the production mobile recovery workflow
against fresh headless Android storage. It removes and corrupts only the disposable managed
envelope long enough to prove both paths fail without saving an empty logbook, rejects a
separate wrong account, restores the exact ledger into a new device, verifies the temporary
Windows credential was deleted after completion, writes redacted evidence, and removes every
disposable Auth and hosted row. It does not install, clear, or automate the retained Pixel app.

The owner has also passed the complete permanent-package workbook-to-Android journey and
clean reinstall on the physical Pixel. Keep
`artifacts/gate4-owner-final-journey-20260908/verification.json` as the durable redacted
baseline. The next live proof is the final owner dress rehearsal followed by one external
Windows Excel plus Android canary, using the exact procedure in
`docs/flightlogx-preview-runbook.md`.

## References

- Supabase database migrations: https://supabase.com/docs/guides/deployment/database-migrations
- Supabase Row Level Security: https://supabase.com/docs/guides/database/postgres/row-level-security
- Supabase Auth configuration: https://supabase.com/docs/guides/auth/general-configuration
- Supabase passwordless email: https://supabase.com/docs/guides/auth/auth-email-passwordless
