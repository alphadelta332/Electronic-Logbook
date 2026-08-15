# Hosted Pilot Supabase Setup

This document is the repeatable project setup note for the private hosted pilot. It is
safe to commit because it contains no Supabase URLs, anon keys, service-role keys, JWT
secrets, SMTP credentials, or pilot emails.

## Project Creation

Create two separate Supabase projects:

- Development: used for local and CI-adjacent integration work.
- Private pilot: used only for invited pilot users.

Use the Sydney region, `ap-southeast-2`, for both projects. Keep each project's anon key,
service-role key, project URL, database password, and access token in local secret
storage only. Do not add them to `release.local.json`, diagnostics bundles, screenshots,
artifacts, or workbook metadata unless a later release gate explicitly adds a redacted
secret-handling path.

The first private pilot remains on Supabase Free until a documented upgrade trigger is
reached. Recheck current Supabase Free limits, region availability, and Auth behavior
before creating the pilot project and again before inviting real users.

## Apply Migrations

Install and authenticate the Supabase CLI locally, then link each project separately.
Apply the same checked-in migrations to development first, then to the private-pilot
project after review.

```powershell
supabase login
supabase link --project-ref <development-project-ref>
supabase db push
```

For the pilot project, relink intentionally before pushing:

```powershell
supabase link --project-ref <private-pilot-project-ref>
supabase db push
```

The initial migration is:

- `supabase/migrations/20260806000000_hosted_pilot_foundation.sql`

It creates the minimum hosted ledger schema, constraints, indexes, RLS policies, and
bounded sync routines for:

- invited accounts;
- logbooks and owner-first memberships;
- Android and workbook devices;
- append-only encrypted operations;
- per-device acknowledgements;
- pairing requests;
- encrypted key envelopes;
- redacted security events.

## Auth Configuration

Configure Auth in the Supabase dashboard for each project:

- Disable public self-registration.
- Enable email sign-in only for invited users.
- Use the shared **Magic Link or OTP** email template for pilot sign-in. Supabase Free
  projects using the default email provider cannot customize this template, so the default
  email may contain only a link. Both the Android pilot client and workbook updater must
  accept either the displayed OTP (when a custom template can include `{{ .Token }}`) or an
  unused default Supabase confirmation-link address pasted into the sign-in field. The link
  must belong to the configured pilot project and is exchanged as a token hash without
  opening its redirect target. Do not hard-code the hosted OTP length in client instructions.
- Keep OAuth, phone, anonymous, and public signup providers disabled unless a later gate
  explicitly adds them.
- Client sign-in calls must pass the SDK option `shouldCreateUser: false`, which maps to
  the REST field `create_user: false`, so an unknown email address cannot create a new
  account from the app.
- Unknown email, disabled account, and revoked-device paths must use generic user-facing
  language that does not reveal whether an address belongs to the pilot.

Invitations are created administratively. The app and workbook must not contain a shared
service credential capable of creating users.

After a Supabase Auth invitation is accepted, the client should call
`public.accept_hosted_invitation(...)` with the local device type and platform label. The
function activates an invited account, registers the device, and records a redacted
security event. It rejects disabled accounts and authenticated users without a matching
invited account row, which keeps accidental public self-registration outside the hosted
ledger even if an Auth user exists.

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

Use `public.get_hosted_pilot_health()` for pre-pilot and weekly private-pilot checks. It
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
development or pilot project and import them into a separate Sydney project or disposable
local database before inviting pilot users.

## Local Secrets

Use environment variables or the platform's secret store for project-specific values.
Suggested local names:

```text
ELB_SUPABASE_DEV_URL
ELB_SUPABASE_DEV_ANON_KEY
ELB_SUPABASE_PILOT_URL
ELB_SUPABASE_PILOT_ANON_KEY
```

Service-role keys are for administrative scripts only and must never be bundled into the
Android app, workbook, updater, package exchange files, or diagnostics.

Managed recovery uses a different local secret file for each hosted project:

```text
%LOCALAPPDATA%\ElectronicLogbook\Supabase\recovery-envelope\development.env
%LOCALAPPDATA%\ElectronicLogbook\Supabase\recovery-envelope\private-pilot.env
```

Never reuse one project's ingress key pair or KEK in the other project. Create a missing
file with `tools\RecoveryEnvelopeSecretGenerator`, deploy it with `supabase secrets set
--env-file <path> --project-ref <ref>`, and retain it only through the trusted local
development transfer workflow. `Invoke-LocalDevelopmentTransfer.ps1 -Action Verify`
confirm-tests the RSA pair and KEK length without printing secret material.

For the Android owner-rehearsal build, create a local gitignored mobile runtime config:

```powershell
.\tools\New-MobileHostedSyncLocalConfig.ps1 `
  -SupabaseUrl "https://<private-pilot-project-ref>.supabase.co" `
  -AnonKey "<private-pilot-anon-key>" `
  -PlatformLabel "Pixel 8 Pro" `
  -DisplayName "Project owner"
```

This writes `mobile/src/ElectronicLogbook.Mobile/wwwroot/hosted-sync.local.json`, which
is gitignored. Use the private-pilot project URL and anon key only. Re-run
`npm.cmd run sync:android` or `npm.cmd run install:android:debug` from `mobile/` after
creating or changing that file so the Capacitor assets include the pilot transport
configuration.

### Gate 1 clean-slate connection recovery

> **Pre-cohort blocker (2026-08-09):** the retained-connection workflow below only
> repairs an installation that still has its hosted credential. A full uninstall or app
> data clear also removes the credential and Android Keystore logbook key. Although the
> encrypted operations remain in Supabase, the current client has no populated hosted
> key envelope with which to decrypt them; completing email sign-in registers a new
> device and initializes a different app-only logbook. Do not present database cleanup
> and re-enrolment as user recovery. Before cohort launch, implement and rehearse the
> recovery-code or trusted-device key-envelope design in
> `docs/hosted-sync-architecture.md`, proving that reinstall + one sign-in restores the
> existing logbook without duplicate devices, manual package import, or administrator
> intervention.

Treat the current owner connection failure as an S2 blocked workflow. Preserve the
retained credential and the single server device. The diagnostic build exposes a
read-only/disposable preflight in Settings with the exact failing stage, a stable error
code, expandable exception/HTTP details, and a **Copy redacted diagnostics** action.
Only **Recover retained connection** may create the real app-only logbook, and it is
enabled only after the same attempt has passed every remote and disposable local check.

Before installing the acceptance build, configure desktop-only values without printing
them:

```text
ELB_SUPABASE_PILOT_DB_URL
ELB_SUPABASE_PILOT_ACCESS_TOKEN
ELB_SUPABASE_PILOT_REFRESH_TOKEN       # only used when the access token is expired
ELB_SUPABASE_PILOT_SERVICE_ROLE_KEY    # desktop administrative validation only
ELB_SUPABASE_PILOT_DEVICE_ID
```

Then build/sync the isolated app so packaged config copies exist and run the preflight
once. It verifies packaged-config parity, JWT project/role/expiry, the Auth endpoint,
database access, a locally configured CLI/service credential, `/auth/v1/user`, the
active account, the existing active device, and the absence of a hosted logbook. The
report never writes the supplied secrets or full identifiers.

Do not run recovery if any check fails. Fix only that subsystem, rebuild in place, and
rerun preflight. Recovery never sends email or calls device registration; it imports the
new app-only logbook key, saves IndexedDB state, reloads it, and compares account,
device, and logbook identifiers before displaying Connected.

## Verification Before Pilot Use

Before inviting pilot users:

- run the migration against a disposable development project;
- inspect Supabase Security Advisor and Performance Advisor output;
- execute adversarial RLS tests for cross-account reads, writes, device spoofing,
  replayed operations, revoked devices, disabled accounts, and acknowledgement rollback;
- rehearse logical export and restore into a separate Sydney project;
- verify diagnostics redact URLs, keys, tokens, user emails, and ciphertext payloads
  unless the user explicitly exports a hosted data backup.

Run the checked-in RLS harness only against a disposable local database or development
project:

```powershell
supabase db reset
psql "postgresql://postgres:postgres@127.0.0.1:54322/postgres" -v ON_ERROR_STOP=1 -f supabase/tests/hosted_pilot_rls.sql
```

The harness is `supabase/tests/hosted_pilot_rls.sql`. It seeds synthetic
`example.invalid` users and rolls its transaction back.

Run the hosted managed/recovery-code rehearsal only against the development project:

```powershell
.\tools\Invoke-HostedRecoveryRehearsal.ps1
```

It obtains project credentials from the private local configuration, uses an
administrator-generated email OTP without sending mail, exercises both replacement
paths against a non-empty encrypted ledger, and removes the disposable Auth identity
and hosted rows. PostgreSQL 17 is required because the append-only operation trigger is
bypassed only inside the narrowly scoped cleanup transaction; normal API deletion
remains blocked.

## References

- Supabase database migrations: https://supabase.com/docs/guides/deployment/database-migrations
- Supabase Row Level Security: https://supabase.com/docs/guides/database/postgres/row-level-security
- Supabase Auth configuration: https://supabase.com/docs/guides/auth/general-configuration
- Supabase passwordless email: https://supabase.com/docs/guides/auth/auth-email-passwordless
