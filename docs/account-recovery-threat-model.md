# Account and Logbook-Key Recovery Threat Model

Status: Gate 1 implementation decision, 2026-08-09.

## Decision

Use Android Credential Manager as the returning-user surface, with Sign in with Google
as the first durable identity linked to the invited Supabase account. Add passkeys when
the relying-party service is available. Use Restore Credentials as an opportunistic
automatic sign-in path, not as the only recovery mechanism, because it depends on the
user's Android backup, screen-lock, Google Play services, and device-transfer state.

Recover the logbook key through an authenticated managed envelope service. The service
holds a versioned key-encryption key (KEK) outside the database. On replacement-device
enrolment, the device creates a non-exportable Android Keystore key pair and sends only
its public key. The service authenticates the Supabase account, authorizes its active
logbook membership, unwraps the account-recovery envelope in memory, immediately
re-encrypts the logbook key to the new device public key, records a redacted audit event,
and returns only the device-wrapped envelope. The Android plugin unwraps it directly
into Keystore-backed local storage.

The raw logbook key may exist only in bounded process memory inside the authenticated
envelope operation and inside the device's Keystore-backed cryptographic operation. It
must never be written to Supabase tables, logs, diagnostics, analytics, crash reports,
support exports, or local browser storage.

## Recovery flow and trust boundaries

1. First-time invitation proves control of the invited email and creates the Supabase
   account.
2. While that session is active, the user links the durable Google identity through
   Credential Manager. Email OTP or magic link remains an explicit fallback.
3. The first device creates the logbook key, its non-exportable device key pair, the
   managed account-recovery envelope, and a separately rate-limited recovery-code
   envelope.
4. On reinstall, durable authentication occurs before device registration. The app
   queries active memberships and logbooks. If one already exists, it must not run
   app-only initialization or create another logbook.
5. The replacement device obtains a device-wrapped key envelope from the managed
   service, imports it into Android Keystore, pulls and decrypts the hosted operation
   ledger, materializes local state, and only then reports `Synced`.
6. Device activation and old-device revocation are one audited, idempotent transition.
   An interrupted attempt can resume without producing two active replacement devices.

## Fail-closed rules

- No durable credential: offer email OTP/magic link fallback, then the recovery code.
- No recoverable envelope: do not create a new logbook for an account that already has
  an active membership; report a redacted recovery error.
- More than one active logbook: require an explicit logbook choice after authentication,
  without exposing identifiers or key terminology.
- Wrong or exhausted recovery code: rate-limit, audit, and leave hosted/local state
  unchanged.
- Interrupted restore: keep the new device pending and retry idempotently; never report
  `Synced` before key import, ledger decryption, materialization, and acknowledgement.
- Managed KEK unavailable or revoked: return a generic retryable failure and never fall
  back to plaintext key storage or a newly initialized logbook.

## Rationale and rejected alternatives

- Google identity solves repeat authentication but cannot itself derive or wrap the
  logbook encryption key; an ID token is an assertion, not stable secret key material.
- A database-only envelope encrypted with a client-known secret cannot provide seamless
  recovery after both local data and Keystore material are gone.
- Copying an Android symmetric Keystore key through ordinary app backup is not a valid
  design; Keystore keys are intentionally device-bound.
- Restore Credentials improves Android-to-Android continuity, but it is not universal
  and uses the same relying-party pattern as passkeys. It complements, rather than
  replaces, the durable identity and managed envelope service.
- An administrator reset or approval by an old device is an operational escape hatch,
  not normal account recovery.

## Implementation sequence

1. Discover memberships after authentication and stop before device/logbook creation
   when hosted state already exists.
2. Add Credential Manager Sign in with Google and link it during first-time onboarding.
3. Add native device key-pair generation/import plus the authenticated envelope service
   and audited, idempotent device-enrolment RPC.
4. Create both managed and recovery-code envelopes during first initialization.
5. Restore ledger state before activation and `Synced`; then add Restore Credentials and
   the full failure matrix.

Primary platform references: [Android Credential Manager prerequisites](https://developer.android.com/identity/credential-manager/prerequisites),
[Restore Credentials overview](https://developer.android.com/identity/sign-in/restore-credentials),
[Restore Credentials implementation](https://developer.android.com/identity/sign-in/restore-credentials-implementation),
[Supabase native Google sign-in](https://supabase.com/docs/guides/auth/social-login/auth-google),
and [Supabase identity linking](https://supabase.com/docs/guides/auth/auth-identity-linking).
