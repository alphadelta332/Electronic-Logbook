# Account and Logbook-Key Recovery Threat Model

Status: implemented decision for the controlled Preview

Decision date: 2026-08-09

Implementation reconciled: 2026-09-09

## Decision

Use Android Credential Manager with Sign in with Google as the normal returning-user,
reinstall, and replacement-device identity. Use the same invited Google-account email for
the one-time Windows workbook migration and Android. Recover the existing logbook key
through the authenticated managed-envelope service; do not ask an ordinary participant
for a recovery code, package, file, key, or logbook choice.

Google proves who the participant is. It does not derive the logbook encryption key. The
managed-envelope service holds a versioned key-encryption key (KEK) outside PostgreSQL. A
new Android installation creates a non-exportable Keystore key pair and sends only its
public key. The service authenticates the Supabase account, authorizes its active logbook
membership, unwraps the account-recovery envelope in bounded memory, immediately wraps
the logbook key to the new device public key, records a redacted event, and returns only
the device envelope. The Android native plugin imports it into Keystore-backed storage.

The raw logbook key may exist only in bounded process memory during the authenticated
envelope operation and the device's Keystore-backed cryptographic operation. It must
never be written to Supabase tables, browser storage, logs, diagnostics, analytics, crash
reports, support bundles, or evidence.

Email-code authentication and the separately rate-limited recovery-code path remain
Advanced/support fallbacks. They are not shown in the normal Google recovery journey and
must never be used to disguise a broken managed-envelope path.

## Normal Workbook-Led Recovery Flow

1. The owner provisions one Auth identity and a `workbook_migration` invitation for the
   participant's Google-account email. Public self-registration remains disabled.
2. The Windows updater signs in that exact identity, begins or resumes one migration,
   creates the canonical hosted logbook, and enrolls the managed account-recovery
   envelope before Android arrival.
3. The updater uploads encrypted configuration and flight operations, verifies exact
   hosted readback, completes the migration receipt, then installs and stamps the updated
   workbook.
4. On Android, Google authentication completes before device registration. The app
   queries active memberships and logbooks before considering any initialization path.
5. Because the invitation is `workbook_migration`, the app must find exactly one completed
   migrated logbook. It creates a pending device with a non-exportable key pair and asks
   the managed service for a device-wrapped envelope.
6. The app imports the envelope into Android Keystore, restores encrypted configuration,
   pulls and decrypts the ordered operation ledger, materializes it into fresh local
   encrypted storage, verifies the migration receipt and totals, and records its
   acknowledgement.
7. Only after those checks pass may the app activate the device and show `Existing
   logbook restored and synced.`
8. A clean reinstall or replacement device repeats steps 4-7 against the same account,
   membership, logbook, and ledger. It never creates a replacement logbook.

`app_only` onboarding remains a separately explicit future mode. It is not used by the
controlled workbook-led Preview and must not be selected as a workaround for incomplete
migration or recovery.

## Trust Boundaries

Owner administration:

- may create or disable an invited identity, assign onboarding mode, revoke devices,
  inspect redacted health, and coordinate Advanced recovery;
- cannot decrypt hosted flight operations through ordinary database access; and
- must not reset hosted state and call that account recovery.

Supabase Auth and hosted database:

- authenticate the provisioned identity and enforce active account, membership, and
  device scope;
- store encrypted operations, configuration, managed and device envelopes, migration
  state, acknowledgements, and redacted events; and
- never store the raw logbook key, recovery secret, or plaintext flight data.

Managed-envelope service:

- keeps the KEK outside the database;
- unwraps the account envelope only after authenticated authorization;
- exposes the raw key only in bounded memory long enough to rewrap it; and
- returns only an envelope for the requesting device public key.

Windows updater:

- holds temporary migration key material only long enough to encrypt, verify, and enroll
  recovery;
- keeps retry material in Windows Credential Manager under a scoped target; and
- deletes that temporary credential after completed migration.

Android:

- keeps private device keys non-exportable in Android Keystore;
- persists local logbook state encrypted;
- does not report success until durable restoration, replay, verification, and
  acknowledgement complete; and
- keeps a failed replacement device pending or superseded rather than active.

## Fail-Closed Rules

- Workbook migration not completed: tell the participant to finish the Windows move;
  create no Android logbook or active device.
- Signed-in email differs from the invitation: reject the attempt without changing the
  invitation, membership, or local logbook.
- No active membership or migrated logbook: stop with a non-enumerating message; do not
  initialize an empty replacement.
- More than one active logbook: stop and route to support. Do not show raw identifiers or
  guess which logbook is canonical.
- Missing, corrupt, revoked, or incomplete managed envelope: stop before activation and
  local commit; do not fall back automatically to a new logbook or plaintext key.
- Managed KEK unavailable: return a generic retryable failure and leave hosted and local
  data unchanged.
- Receipt, operation, configuration, totals, or acknowledgement mismatch: keep the device
  non-active, retain the canonical hosted logbook, and report a stable redacted error.
- Interrupted restore: retry idempotently against the same pending attempt; supersede a
  failed pending device when a later verified attempt succeeds.
- Revoked or wrong device: reject hosted access and envelope use.
- Wrong or exhausted Advanced recovery code: rate-limit, audit, and leave hosted and local
  state unchanged.
- Missing Google credential: offer the normal Google account chooser again. Expose the
  displayed six-digit email sign-in only through Advanced/support, not as an automatic
  change of recovery model.

## Threats And Controls

Plausible empty replacement:

- discover existing membership before creation;
- prohibit Android initialization for `workbook_migration`; and
- activate only after exact restored-state verification.

Account substitution or invitation theft:

- bind onboarding mode and invited email administratively;
- compare the authenticated Google email in the Windows and Android flows; and
- use generic public failures so unknown accounts cannot be enumerated.

Database or backup disclosure:

- store only ciphertext and wrapped envelopes;
- keep the KEK outside PostgreSQL; and
- omit secrets, account identifiers, emails, and ciphertext from routine diagnostics.

Device-key theft:

- create non-exportable Android Keystore keys;
- wrap each envelope to one device public key; and
- revoke or supersede device envelopes with the device record.

Replay, rollback, or partial restore:

- use unique operation identities, matching payload hashes, ordered revisions, monotonic
  acknowledgements, migration receipts, and exact configuration/operation readback; and
- never display `Synced` before the restored cursor is durably acknowledged.

Service or build fault:

- fail closed before activation;
- retain stable redacted error codes and hosted security events;
- preserve the existing canonical ledger; and
- allow a corrected build to resume without creating another logbook or duplicate flight.

Support misuse:

- keep packages, email codes, recovery codes, keys, and ids out of normal UI;
- require a deliberate Advanced/support path and private owner coordination; and
- never describe administrator deletion or database reset as recovery.

## Rejected Alternatives

- Google ID tokens as encryption keys: tokens assert identity but are not stable key
  material.
- A database-only envelope encrypted by a client-known secret: it cannot recover after
  both local data and device key material are gone.
- Ordinary Android backup of a symmetric Keystore key: Keystore keys are intentionally
  device-bound and backup behavior is not a complete recovery contract.
- Recovery-code-first onboarding: it exposes high-risk terminology and makes normal
  recovery depend on a secret the product can recover without asking the participant.
- Old-device approval as the normal path: replacement must still work after loss of the
  old device.
- App-only initialization after any recovery uncertainty: it creates a believable but
  wrong empty logbook and is therefore prohibited.

## Accepted Evidence

The owner journey has proven first-install and clean-reinstall recovery of the same
three-flight hosted logbook on the permanent Pixel. It used Google plus managed recovery,
created no duplicate logbook or operation, displayed no picker, manual import, or
recovery-code prompt, and preserved the separate development installation. A stale mixed
mobile build failed before activation; the corrected build resumed the same canonical
state, superseded the failed device, and restored exact entries and totals.

Evidence:

- `artifacts/gate4-owner-final-journey-20260908/verification.json`
- `artifacts/gate3d-disposable-android-recovery-20260907/verification.json`
- `artifacts/gate3d-disposable-cleanup-20260908/verification.json`

## Remaining Preview Proof

Repeat the complete customer-facing journey once as the final owner dress rehearsal,
then with one external Windows Excel plus Android canary. The participant must complete
Google recovery without internal tooling or normal exposure to a database, package, key,
code, identifier, picker, or manual import. Any S0-S2 failure stops wider enrolment until
it is resolved and retested.

Primary platform references:

- Android Credential Manager prerequisites:
  https://developer.android.com/identity/credential-manager/prerequisites
- Android Restore Credentials:
  https://developer.android.com/identity/sign-in/restore-credentials
- Supabase native Google sign-in:
  https://supabase.com/docs/guides/auth/social-login/auth-google
- Supabase identity linking:
  https://supabase.com/docs/guides/auth/auth-identity-linking
