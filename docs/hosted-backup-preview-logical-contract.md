# Preview logical recovery compatibility

Contract snapshot: 18 October 2026. The exact Preview logical datasets can
be reconstructed on the disposable target below, with explicit lossless handling
of the differences described here. Compatibility alone does not establish
daily backup automation or real Google recovery acceptance.

The machine-readable contract is
[`preview-logical-contract.json`](../tools/hosted-backup/preview-logical-contract.json).
It records **every ordered column, PostgreSQL type, generated/identity flag, all
six generated expressions, migration identifiers, Storage migration hashes and
sequence definitions**. It contains managed metadata only: no participant rows,
project identifiers, credentials, migration statements or key material.

## Supported source and target

Read-only live Preview inspection observed PostgreSQL build `17.6.1.155`, Auth
health version `v2.197.0`, 82 Auth migrations, 74 Storage migrations ending at
`73/revoke-grants-to-unused-operations`, 20 application migrations ending at
`20261017000000`, and 89 Realtime migrations ending at `20261002120000`.
Earlier inspection includes the eight extension versions and deployed Edge
revisions. Required recovery scope and drift limits are defined in the
[backup contract](hosted-preview-backup-plan.md).
Source inspection used verified TLS, read-only repeatable-read SQL and management
GET requests. Each management/SQL request is separate; this metadata inventory
is not a coherent participant-data backup.

Source API configuration exposes `public,graphql_public`, adds `public, extensions`
to the search path and limits results to 1,000 rows. Google is enabled, signup
disabled, manual linking enabled, nonce checking enabled and email required;
JWT expiry is 3,600 seconds. An unavailable provider setting is recorded as null,
not guessed. Exact hosted PostgREST/Storage/Edge binary versions remain unknown.
Storage compatibility is established against the complete ordered source migration
ID/name/hash set and column/generated-expression comparison, not an assumed binary.

The tested target uses the existing Supabase PostgreSQL `17.6.1.155` image and
PostgREST `v14.15`, plus digest-pinned Auth `v2.197.0` and Storage `v1.77.10` images
listed in `targetImages`. Fresh Auth and Storage migrators construct the managed
definitions in a randomly marked database from `template0`. A schema-only copy of
those **disposable service-generated definitions**, never hosted participant schema
or data, supplies the repeated restore fixtures. This uses existing cluster roles;
it does not prove deployment to a new hosted project with fresh role/ACL setup.

All 20 source application migrations must resolve to reviewed reconstruction
sources. Their original filenames/revisions remain in the archive manifest. The
last migration contains an outbound airport timer: the fixture applies its bucket
definition only to the synthetic source, restores that bucket as data on the
target, and never installs the outbound timer. The six-column application ledger
is reconstructed explicitly and its complete row values are restored, including
statement/rollback arrays. This is not permission to run arbitrary archived SQL.

## Complete dataset contract

Every table is required even when empty. Missing/unknown source tables or columns
fail closed; there is no feature-based exclusion or managed-table allowlist shrink.

| Schema | Required tables |
| --- | --- |
| `public` (11) | `accounts`, `configuration_revisions`, `devices`, `key_envelopes`, `logbook_memberships`, `logbooks`, `operation_acks`, `operations`, `pairing_requests`, `security_events`, `workbook_migrations` |
| `auth` (27) | `audit_log_entries`, `custom_oauth_providers`, `flow_state`, `identities`, `instances`, `mfa_amr_claims`, `mfa_challenges`, `mfa_factors`, `mfa_recovery_code_sets`, `mfa_recovery_codes`, `oauth_authorizations`, `oauth_client_states`, `oauth_clients`, `oauth_consents`, `one_time_tokens`, `refresh_tokens`, `saml_providers`, `saml_relay_states`, `schema_migrations`, `scim_tokens`, `scim_users`, `sessions`, `sso_domains`, `sso_providers`, `users`, `webauthn_challenges`, `webauthn_credentials` |
| `storage` (8) | `buckets`, `buckets_analytics`, `buckets_vectors`, `migrations`, `objects`, `s3_multipart_uploads`, `s3_multipart_uploads_parts`, `vector_indexes` |
| `supabase_migrations` (1) | `schema_migrations` |

The mapping copies every writable column without changing values or account UUIDs.
Rows remain PostgreSQL-produced JSON strings until PostgreSQL restores them; no
JavaScript floating-point conversion touches row values. Generated columns are
omitted from INSERT and their recomputed values enter every typed-row digest.
The generated fields are identity email, user confirmation time, SCIM username,
external ID and active status, and Storage path tokens. Populated synthetic cases
exercise all six, including Unicode and explicit false. Every dataset digest is
compared after restoration, including empty datasets and all managed ledger rows.

Explicit target differences:

1. Fresh Auth creates `auth.schema_migrations.version` as `varchar(14)`; widen it
   to Preview's `varchar(255)` before restoration. No truncation/cast is accepted.
2. The current Auth bootstrap folds seven 2017/2018 migrations into `00`. The test
   checks this exact known bootstrap difference, then restores **all 82 original
   source rows**, including those seven. A running restored Auth service leaves
   their digest unchanged. Do not forge a source ledger to match a newer runtime.
3. This Storage target creates `storage.iceberg_namespaces` and `storage.iceberg_tables`
   in addition to the eight Preview tables. They remain target-only and empty,
   explicitly checked after restoration. Synthetic source fixtures remove only
   these two disposable target additions to reproduce Preview's exact source shape.
   No hosted table is removed and no source data is discarded. Any other target
   addition fails compatibility until reviewed.

The one durable sequence inside these four schemas is `auth.refresh_tokens_id_seq`:
bigint, start/min/increment/cache 1, max `9223372036854775807`, noncycling. Target
definition equality and restored last/called state plus safe next allocation are
tested. Allocation state is non-MVCC and can lead committed rows; never rewind it
to MAX(id). The typed-edge fixture separately exercises values beyond JavaScript's
safe integer range. The other four source sequences belong to cron history/jobs,
pg_net queues and Realtime subscriptions; recreate fresh service infrastructure
rather than restoring transient allocation state as logbook data.

## Required reconstruction and custody sources

| Dependency | Required source and recovery handling |
| --- | --- |
| Logbooks/account access | All 47 datasets above, complete operation/configuration history, original Auth user UUID/provider subject, memberships/devices and usable wrapped envelopes. No empty replacement logbook. |
| Application/Edge implementations | Archive actual reviewed bytes and revision/hash manifest from `supabase/migrations`, `supabase/functions` (live recovery-envelope revision 5 and airport-catalog revision 2), shared Portable ciphers/materializer and backup helpers. Dirty/untracked required source is included by the existing bundle. A Git commit alone is insufficient. Check deployed-code parity before claiming complete service reconstruction. |
| Airport reference | Preserve actual `airport-catalog` manifest, seed and current snapshot bytes with metadata, validate catalogue contents and enforce capture consistency; SQL object metadata alone does not restore files. The compatibility test uses synthetic reference bytes; the supervised hosted route separately validates actual ETags and reference bytes. |
| Auth, Google and API configuration | Recorded nonsecret settings above, Google Cloud owner access and Web/Android OAuth clients, provider secret, callback/origin/redirect configuration, SMTP/domain setup and Android/client endpoint configuration. Values stay in protected owner custody; regenerate target API/JWT credentials and update clients. Presence booleans do not certify a complete credential recovery set. |
| Application recovery | Existing protected USB archive identity/capsule and separately held password; recovered KEK/version must unwrap the original envelopes. The job receives only the public archive recipient. The local test uses a synthetic in-memory KEK; it does not re-read the owner's USB keys. |
| Managed encrypted Auth fields | Preserve their opaque row values. Any configured Auth database encryption key/version or provider-specific external secret requires separate protected custody/reconfiguration; a table copy does not recover an unavailable service key. Dynamic OAuth/SSO/MFA workflows are not certified by synthetic Google identity lookup. |
| Timers/Vault/continuity | Recreate the airport timer disabled pending fresh target publisher credentials, or use the already-tested external publisher. Never run archived cron command strings or transplant Vault ciphertext as usable secrets. Vault decryption keys and Cloudflare keepalive credentials remain separate owner-managed infrastructure. No cron, Vault or HTTP queue records enter logical participant datasets. |
| Realtime | Record its 89-row source migration revision/digest; provision runtime schema/publication from reviewed target infrastructure if enabled. Hosted sync clients currently use REST/RPC, not Realtime subscriptions. This test does not clone the Realtime runtime/ledger or preserve transient messages/subscriptions. Future durable use expands this contract before export. |
| Ownership/RLS | Reapply reviewed app policies, grants and roles on fresh infrastructure, verify effective application authorization, then deploy authenticated Edge services. Local service migration and application replay do not prove a fresh hosted project's complete privilege setup. |

Supabase documents that provider settings, JWT/API keys, Edge deployments, Storage
bytes and SMTP require separate restoration. Its suggested workaround of omitting
incompatible tables does **not** meet this project's complete-data contract.
[Official platform-to-self-hosted restore guidance](https://supabase.com/docs/guides/self-hosting/restore-from-platform).
The Auth runtime's user lookup also requires the original instance/user binding;
the synthetic seed supplies a valid zero instance UUID and normal token/time fields.
[Pinned Auth user lookup implementation](https://github.com/supabase/auth/blob/v2.197.0/internal/models/user.go).

## Executable proof and limits

```powershell
node --test --test-concurrency=1 tools/hosted-backup/archive.test.mjs tools/hosted-backup/preview-compatibility.integration.test.mjs
pwsh -NoProfile -File tools/Test-LocalDevelopmentTransfer.ps1
```

The compatibility suite also runs the original local-runtime test. The Preview
fixture covers **47 required tables plus one synthetic typed-edge table**, real
restricted PostgREST GET, coherent capture, recipient encryption, independent
schema reconstruction, all typed-row digests, sequence allocation and shared
production application replay. The two additional target-only tables stay empty.
Nine prohibited HTTP cases, concurrent TRUNCATE/INSERT behavior, shape drift and
seven malformed/corrupt recovery cases retain their original checks and cleanup.
The running restored Auth service returns the preserved synthetic user and Google
provider subject through its authenticated admin API, and retains the restored
source migration ledger. This is stronger than checking a SQL identity row, but
**does not prove real Google sign-in** or phone recovery.

Some unused managed feature tables contain no synthetic rows. Their full ordered
types are checked and they remain required datasets; this is not end-to-end
acceptance of OAuth server, SAML, SCIM provisioning, WebAuthn, MFA or Storage object
service operations. This compatibility fixture does not prove hosted API behaviour,
arbitrary provider-DDL coherence or full fresh-service configuration restoration.
Its disposable container images and generated outputs are excluded from development
transfer; contract/test paths and optional image verification are declared in the
portability manifest.

## Hosted export boundary

The [supervised logical procedure](hosted-backup-supervised-logical-export.md) uses
this mapping with the existing NOLOGIN anon role and a short-lived opaque capability.
It audits all exposed relations/functions, checks hosted method/transaction behaviour,
captures all declared datasets/reference/reconstruction inputs and removes the
temporary endpoint. Owner, management, service, signing and recovery credentials
remain outside the export client. Unexposed pg_net permissions alone do not establish
an HTTP capability escape.

Scheduled operation, independent alerts and real Google/fresh-service recovery
remain distinct acceptance conditions in the [backup contract](hosted-preview-backup-plan.md).
