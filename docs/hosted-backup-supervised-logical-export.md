# Supervised hosted logical export

This owner-operated route captures an encrypted archive through the fixed HTTP
capability, restores the declared 47 datasets on a compatible disposable target and
supports offline-key application replay. It preserves original identity/account
links and encrypted revision/settings history. It does not enable daily scheduling
or prove real Google sign-in/full service recovery. Overall acceptance status is
maintained in the [backup contract](hosted-preview-backup-plan.md).

## Credential and API boundary

`supervised-logical-export.mjs` is an owner controller on the trusted Windows PC.
It uses existing private project metadata, management authentication and the public
CA at `%LOCALAPPDATA%/ElectronicLogbook/HostedBackup/source-root.crt`. Those
credentials stay outside the restricted client and unattended jobs.

The external export client receives only a publishable API key and independent
random 32-byte opaque capability. The endpoint uses the existing `anon` NOLOGIN
API role; it adds no signing authority, database login, role memberships or direct
application-table access. Audit all effective exposed relation/column privileges
and function fingerprints in `preview-logical-contract.json` before installation.
Require exactly `public,graphql_public` exposure. A role name or lack of explicit
grants alone does not establish isolation; inherited and PUBLIC paths count.

The table-free activity probe, disabled/unavailable GraphQL wrapper and invoker
citext/text helpers are fingerprinted. Reject changed accessible functions or
unexpected reads/writes. PUBLIC pg_net SQL privileges remain outside this HTTP
surface; do not treat that as removing them or permitting arbitrary SQL access.

The temporary `public.elb_backup_snapshot()` is parameterless, SECURITY DEFINER,
owned by a BYPASSRLS/superuser role and executable only by anon among application
roles. It validates capability digest/expiry, request role, GET, read-only transaction
and READ COMMITTED isolation. It locks fixed tables before one aggregate SELECT,
captures typed datasets/sequence state and rejects shape, generated-expression or
migration drift and responses exceeding 32 MiB. Search path, UTC/ISO formatting
and lock wait are fixed. There is no SQL/table/filter or restore-target parameter.

Missing/malformed/wrong tokens and POST must be denied with the expected application
error. Validate successful method/read-only/isolation context and all dataset shapes.
An OpenAPI-root 401 is not evidence about the RPC's anonymous-role boundary. Never
give the export client a secret/service API key to bypass a gateway error.

## Archive and workload

The existing RSA-recipient/AES-GCM format encrypts all datasets, generated-field
definitions, migration revisions, sequence state, required actual airport bytes,
reviewed source bytes and hash manifest. PostgreSQL row JSON remains textual to
preserve numeric values. Reference ETags, catalogue hashes and validation must match.

Both active deployed Edge bundles/metadata are downloaded read-only and included
as a nested recipient-encrypted archive. Deployment revisions are rechecked before
output; the restorer authenticates both archives. Preserving deployed bytes does
not establish Google/OAuth/SMTP configuration recovery or successful redeployment.

Use a current private usage receipt for all-project billing-period egress, cached
egress and invocation headroom. The controller rechecks Free plan and at least
64 MiB remaining in each egress allowance; response/reference reads are bounded.
Usage may lag. A supervised check is neither a permanent spending cap nor approval
for a paid plan. Keep usage observations, archive names and verification receipts
private. Preserve previous native/offline/R2 safety copies.

## Commands and verification

```powershell
node tools/hosted-backup/supervised-logical-export.mjs --usage <absolute-current-usage-receipt.json> --output <new-absolute-output.elbbackup>
pwsh -NoProfile -File tools/hosted-backup/Test-SupervisedBackup.ps1 -Archive <new-absolute-output.elbbackup> -RecoveryDirectory <full-path-on-recovery-USB> -ApplicationReplay
```

The controller validates the recipient receipt and effective permissions, installs
the temporary endpoint, passes only the restricted request to `logical-client.mjs`,
checks source/deployment stability and removes only its unchanged marked endpoint
in `finally`. The memory-only token expires after 20 minutes. Do not schedule this
owner controller or reuse its credentials in the daily job.

The PowerShell verifier accepts the password only through a hidden local prompt.
`logical-restore.mjs` authenticates the trusted ciphertext digest before target
creation, builds pinned Auth/Storage schemas and reviewed application migrations,
restores writable columns and original ledgers, compares all typed-row digests
including generated values and tests sequence allocation. With the offline capsule
it invokes the shared application cipher/materializer. Only randomly marked loopback
targets are created and cleaned; existing databases and phone state are preserved.

Native-archive regression tests require the isolated PostgreSQL 17 fixture with
permission to set `session_replication_role`: set `ELB_BACKUP_TEST_PORT=55432`.
Logical service tests use the local Docker Supabase endpoint on 54322. Missing
privileges must fail; do not change the shared role or bypass restore protections.

Use the focused commands in the [compatibility contract](hosted-backup-preview-logical-contract.md)
and [backup tooling](hosted-backup-tools.md). After a live run, verify endpoint
removal, original role permissions, absence of marked fixtures and unchanged earlier
backup hashes. Record detailed results privately; passing replay is not real Google
sign-in or complete fresh-service recovery.
