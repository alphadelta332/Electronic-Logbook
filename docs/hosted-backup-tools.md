# Encrypted hosted backup tooling

Status: local export, authenticated readback, disposable SQL restoration and shared
application replay, protected offline recovery setup and disposable-tested complete archive
orchestration implemented. Real supervised Preview restoration and USB-capsule-key
shared application replay passed on 2026-10-18. R2 production backup upload,
scheduling and end-to-end Google/managed-service recovery acceptance are not yet complete.
The measured design and owner decisions are in
[the backup plan](hosted-preview-backup-plan.md).
The [automation assessment](hosted-backup-automation-assessment-research-2026-10-18.md)
records tested drift/snapshot hazards and the remaining restricted-export proof.
The [disposable metadata contract](hosted-backup-metadata-guard-contract.md) defines
the separate PG17 guard experiment and its mutation/concurrency checks. It is not
called by these archive commands and cannot connect to hosted sources.

## Local commands

The tools use the existing Node 24/PostgreSQL 17 installation with no npm dependencies.
`Invoke-LocalDevelopmentTransfer.ps1 -Action Verify` checks the required clients.

```powershell
node tools/hosted-backup/cli.mjs export --connection <private-connection.json> --recipient <backup-public.pem> --output <new-output.elbbackup>
node tools/hosted-backup/cli.mjs bundle --connection <private-connection.json> --recipient <backup-public.pem> --source <source-checkout> --output <new-output.elbbackup>
node tools/hosted-backup/cli.mjs verify --archive <output.elbbackup> --identity <offline-backup-private.pem>
```

The explicit connection file has `host`, integer `port`, `database`, `user`,
`password`, `projectRef`, `sslRootCert`, and optionally `pgBin` for the PostgreSQL
binary directory. Hosted sources must be the Sydney session pooler on port 5432,
database `postgres`, and a matching `flightlogx_backup.<projectRef>` identity. TLS CA
and hostname verification are mandatory. Owner credentials are accepted only by the
separate provisioning and supervised local export paths. Do not put a Supabase management token, app KEK or ingress
private key in the backup workflow.

## Supervised manual safety copy

The restricted automated export remains blocked by managed-schema permissions.
The owner-authorized interim path reads Preview using credentials already on the
trusted Windows PC, and immediately encrypts the complete archive to the existing
offline recovery recipient. It does not change Preview or save owner credentials
for scheduled use. The routine CLI keeps its restricted-role audit.

```powershell
node tools/hosted-backup/supervised-export.mjs --ca <absolute-trusted-root.crt> --output <new-full-path.elbbackup>
pwsh -NoProfile -File tools/hosted-backup/Test-SupervisedBackup.ps1 -Archive <full-path.elbbackup> -RecoveryDirectory <full-path-on-recovery-USB>
```

Copy the encrypted archive and its `.receipt.json` together to trusted storage,
and compare their SHA-256 values after copying. Preserve the original receipt on
the trusted PC: encryption alone does not authenticate who produced an archive.
Existing archive, receipt and completed verification files are never overwritten.
Export uses verified TLS and checks the configured Preview project's identity,
region and health before reading. No new software or reusable secret is introduced.

Verification needs an isolated PostgreSQL 17 test instance at `127.0.0.1:54322`
with the existing disposable fixture credentials and Supabase role prerequisites;
the local Supabase stack also provides these. Enter the recovery USB password only
at the local masked PowerShell prompt. It passes to Node through stdin, never
through command arguments, chat or a saved file. The verifier checks the protected
identity and recovery capsule against the independently retained recovery receipt,
authenticates the capsule in memory, restores a new marked database, compares every
table's row count/hash/revision with the archive snapshot and removes that database.
Supervised verification also removes its marked target if restoration fails.
It writes a separate `.verification.json` only after every check and cleanup succeeds.

The real Preview archive was exported and copied byte-for-byte to the recovery USB
on 2026-10-18. Its password-prompted verification passed: all 47 tables matched,
the protected recovery capsule authenticated and the disposable database was removed.
The archive and verification report are retained on both the trusted PC and USB.
This database report does not prove Google sign-in, app-level key recovery, R2 upload
or unattended scheduling. Evidence: `artifacts/hosted-backup-manual-20261018/`.

Use `-ApplicationReplay` for the next, separate recovered-key check:

```powershell
pwsh -NoProfile -File tools/hosted-backup/Test-SupervisedBackup.ps1 -Archive G:\ElectronicLogbook\Preview-Backup-2026-10-18.elbbackup -RecoveryDirectory G:\ElectronicLogbook -ApplicationReplay -LocalPort 55432
```

This requires an already running isolated native PostgreSQL 17 fixture on loopback
port 55432 (54322 remains the default). The native fixture avoids changing the running
Docker stack, whose `postgres` role cannot set `session_replication_role`. Restore
requires that setting to suppress data-insertion triggers. Only these two loopback
ports are accepted; existing databases cannot be selected as restore targets.
`ELB_BACKUP_TEST_PORT=55432` selects the native fixture for the orchestration and
recovered-key integration tests.

The additional check builds the existing .NET replay harness, authenticates the same
USB files against the trusted PC receipt, then obtains the managed recovery KEK and
version solely from the decrypted capsule. Each populated, nondeleted logbook must
retain its owner identity, exactly one usable managed envelope, and contiguous
operation and configuration histories. Shared application ciphers decrypt every
revision, validate the reconstructed document and materialize its current entries.
Wrong keys, unavailable settings and incomplete histories fail closed. Decrypted
records and keys stay in captured process memory; only aggregate counts enter the
separate `.application-verification.json` report. The original SQL verification report
is preserved. The marked restore database is removed on success or failure. This
proves shared application decryption/replay; phone UI and Google recovery still need
their own acceptance. It does not enable automatic backups.

The real archive passed this additional check on 2026-10-18: all 47 tables matched,
both populated logbooks decrypted and validated, and their operation/configuration
histories materialized without unresolved conflicts. The separate report is retained
on USB and the trusted PC, with ignored evidence under
`artifacts/hosted-backup-manual-20261018/`. The marked database was removed and the
verified native fixture stopped; the original archive and protected recovery files
remain unchanged. No phone operation or Google sign-in was exercised.

## Restricted source setup

**Preview blocked (2026-10-17):** live `net` objects inherit PUBLIC reads/writes,
and their `supabase_admin` owner has not delegated revocation rights to `postgres`.
The permission audit stopped setup before LOGIN; the temporary role and explicit
grants were removed. No source credentials were saved and no participant rows were
exported by this batch. Do not enable this job by bypassing the audit or substituting
owner credentials. Resolve the provider boundary or choose a different export design
first. Catalog evidence: `artifacts/hosted-backup-plan-20261017/batch3c1.json`.

The authorized Cloudflare fallback was proved on development only: a real external
scheduled airport run succeeded, pg_net was removed without CASCADE, and catalogue
checks passed. Provisioning then stopped before LOGIN at `unreadable_relations=145`:
project postgres cannot delegate auth schema USAGE, all auth reads or all storage
reads. Its SELECT grant options cover 16/27 auth and 2/8 storage tables/views.
The temporary role/grants were removed; no source credentials or live row exports
were created. Preview remains unchanged. A supported managed-schema grant remedy
or a separately reviewed export design is required before continuing; broad
`pg_read_all_data` membership does not satisfy this export boundary. Evidence:
`artifacts/hosted-backup-plan-20261017/airport-fallback-result.md`.

Run `node tools/hosted-backup/initialize-source.mjs --ca <absolute-trusted-root.crt>`
once on the owner PC after that blocker is resolved. It uses the existing private
Preview project configuration only for provisioning, generates a separate random
password and writes the optional
local-app-data `ElectronicLogbook/HostedBackup/source-connection.local.json` and
`source-root.crt`. Both are explicitly classified for trusted development transfer.
Existing files/roles are never replaced. If setup stops after role creation, inspect
that role as owner; do not blindly retry or grant broader privileges. The role starts
NOLOGIN and is enabled only after the permission audit passes.

The role has no memberships, ownership, administration, replication or write grants.
It receives USAGE and SELECT only in `public`, `auth`, `storage` and
`supabase_migrations`. BYPASSRLS is required to export all participants' encrypted
rows; it does not grant table access. Every hosted export audits effective privileges,
including PUBLIC grants, and fails on missing reads, outside-schema reads, writes,
schema creation, ownership, memberships or callable non-system SECURITY DEFINER
functions. No existing application/PUBLIC/default grants are changed. New objects
require owner review and explicit grants; do not auto-grant future tables.

Read-only defaults/timeouts are defence in depth, not the permission boundary.
Tests disable read-only mode and attempt actual denied SQL within rollback-only
transactions. Normal PostgreSQL catalog visibility, temporary objects and user-owned
large objects are not a sandbox; this role cannot alter existing source application,
Auth, Storage or migration data. Keep its password secret: it can read the complete
allowed export, including Auth metadata. Future provider/grant changes require a new
permission audit. [PostgreSQL pg_dump](https://www.postgresql.org/docs/17/app-pgdump.html)
documents RLS requirements; [pg_dumpall](https://www.postgresql.org/docs/17/app-pg-dumpall.html)
documents password-free roles metadata export without access to password hashes.

Connection files are private local configuration. Do not create them in the checkout,
upload them as artifacts, or print their contents. Reusable configuration paths must
be explicitly classified in the transfer manifest when provisioning creates them.
The export/verify commands accept explicit paths and create no default secret/config directory.
The private backup identity must be separate from the application's managed-recovery
key and placed only in the protected owner recovery set. No production identity is
created by these export/verify commands. Protected/passphrase key setup is a separate owner step:

```powershell
pwsh -NoProfile -File tools/hosted-backup/Initialize-Recovery.ps1 -RecoveryDirectory <full-path-on-trusted-offline-storage>
```

Enter and confirm a password locally, with at least 20 characters; retain it separately
from the USB. No password or plaintext private key enters command-line arguments,
logs, GitHub or R2. The existing Preview recovery secrets remain inside an authenticated
encrypted capsule. The independent backup private identity is stored as encrypted
PKCS#8 using PBKDF2-SHA256 (600,000 iterations) and AES-256-CBC. Written offline files
are independently decrypted and compared before a public recipient and verification
receipt are stored on the working PC. Existing identity/capsule files are not replaced.
Only the public recipient and verification receipt may enter development transfer;
the protected offline files have deliberate exclusions in the manifest. This step
requires PowerShell 7.2+, already available through the optional PowerShell package.

`export` reads the source and writes only encrypted bytes to a new file. Existing
output is never overwritten. `verify` decrypts only in process memory and reports
status/size/file count; it does not write plaintext or restore SQL. A damaged archive,
wrong identity or invalid arguments produces a generic failure without provider
responses, credentials or decrypted records in logs.

Successful readback proves decryption and integrity, not who created the archive:
anyone holding the public recipient can encrypt new content. Restoration must also
match the encrypted archive's SHA-256 against trusted backup-job history, rather than
trusting a checksum stored only beside the archive in the same bucket.

## Private R2 storage setup

`r2-policy.json` defines Standard storage, a whole-bucket 30-day age lock, matching
30-day lifecycle expiry and one-day incomplete-multipart cleanup. Lifecycle expiry
is asynchronous; the later upload guard must bound retained objects/bytes rather than
assume immediate deletion. No Infrequent Access transition or public endpoint is
required. Read back the bucket storage class, lock, lifecycle, disabled managed public
domain and empty custom-domain list after provisioning. A configuration readback is
not an object-level permission test. Administrators can remove lock rules; keep that
authority outside automation. See [Cloudflare bucket locks](https://developers.cloudflare.com/r2/buckets/bucket-locks/)
and [R2 pricing](https://developers.cloudflare.com/r2/pricing/).

The existing maintenance OAuth login does not authorize token creation (HTTP 403).
Run `pwsh -NoProfile -File tools/hosted-backup/Initialize-R2Access.ps1` locally to
create/capture two account tokens through the dashboard: Object Read & Write for
upload, and Object Read only for restore, both scoped only to the backup bucket.
The helper uses hidden prompts and passes values through a captured process pipe,
never secret command arguments. It saves the S3 access IDs/secrets in the optional
private local-app-data `ElectronicLogbook/HostedBackup/r2-credentials.local.json` and
refuses replacement. The API token value is not needed. These are storage credentials,
separate from recovery material; the transfer manifest explicitly allowlists them.
Follow [Cloudflare R2 authentication](https://developers.cloudflare.com/r2/api/tokens/).

Saving keys validates format and distinct identities only. Before unattended use,
prove encrypted upload/readback, read-only write denial, locked replacement/deletion
denial and denied bucket-configuration access. Preserve trusted archive digests outside
the bucket. Recheck account usage, deploy early-warning/stop controls and verify paid
Actions are blocked before enabling the daily job. An empty account inventory is not
historical billing evidence or a provider-enforced spending cap.

Run the owner-only live check with `node tools/hosted-backup/check-r2-access.mjs` on
Windows, using existing curl with `--aws-sigv4` support and fresh Wrangler login.
Credentials are supplied to curl through captured stdin, not arguments. It uploads a
tiny synthetic archive, downloads/decrypts it with a fresh in-memory test identity,
checks the restore key cannot upload, and checks locked replacement/deletion return
HTTP 409 `ObjectLockedByBucketPolicy`. Both keys must receive HTTP 403 `AccessDenied`
for lifecycle configuration reads and listing a separately created empty bucket.
The temporary bucket is removed with owner credentials. The synthetic archive remains
locked until retention expires. At most three small retained probes are allowed;
the check refuses to run against participant backup objects. Capture JSON stdout as
ignored evidence. This verifies current access and lock enforcement; it does not
observe 30 days of expiry or restore a real Preview archive.

## Archive coverage and format

Version 1 uses a PostgreSQL custom-format dump of `public`, `auth`, `storage` and
`supabase_migrations` in a single consistent read-only data transaction. All 11
application tables, Auth users/identities and migration history must be present or
export fails. Cluster roles are captured separately with passwords excluded.
Additional named binary files can be included through `buildDatabaseArchive`;
replacing required dump/roles content is rejected. `export` retains its database/roles
scope. `bundle` adds the catalogue file bytes, reconstruction source and an encrypted
verification manifest. PostgreSQL Storage metadata alone does not preserve file objects.

### Complete archive orchestration (`bundle`)

This command prepares a new encrypted local archive; it does not upload, schedule or
publish backup success. Hosted use still requires resolution of the source-permission
blocker above. It uses the same restricted connection, public recipient and existing
toolchain, with no new dependency, stored credential or default configuration path.

Before reading rows it audits the hosted export identity. A read-only REPEATABLE READ
transaction exports a PostgreSQL snapshot and computes a manifest for every ordinary
or partitioned root table in the four export schemas. Each entry contains the row
count and SHA-256 over sorted per-row SHA-256 digests, including duplicates. Operations
and configuration also retain revision identities inside the encrypted manifest. UTC
and ISO date formatting make the row representation reproducible during restoration.
`pg_dump --snapshot` imports that same snapshot while its transaction remains open.
Concurrent ordinary logbook edits may continue; they cannot shift the manifest away
from the dump. See PostgreSQL's [snapshot option](https://www.postgresql.org/docs/17/app-pgdump.html)
and [snapshot synchronization](https://www.postgresql.org/docs/17/functions-admin.html#FUNCTIONS-SNAPSHOT-SYNCHRONIZATION).

Storage inventory must contain only the known public `airport-catalog` bucket and
its manifest, seed and content-addressed snapshots. Unexpected buckets/objects fail
for review. The current manifest, referenced compressed catalogue and seed bytes are
downloaded through bounded, nonredirecting HTTPS requests. Their Storage content
ETags must match the metadata in the database snapshot. The current catalogue must
also match its manifest SHA-256/count and pass the existing airport validator, with
unique codes and bounded decompression. Older published snapshots are inventoried
but their superseded public reference bytes are deliberately excluded. Missing files,
metadata or ETags stop the job; there is no fallback to an incomplete archive.

Reconstruction includes `supabase/config.toml`, all migration and Edge Function
sources, the backup/replay tools, the shared Portable project and `versions.properties`.
Git supplies tracked and nonignored untracked source names; generated directories,
local configuration, unexpected extensions and symlinks are rejected. Actual working
bytes are preserved, with the HEAD revision, dirty-state flag and per-file hashes;
uncommitted implementation is not misrepresented as committed source. Required files,
per-file and aggregate sizes are checked. Credentials and offline recovery files stay
outside these source paths and are never collected from local app-data directories.

Fresh schema-only exports bracket the operation; changed schema, migration history,
Storage inventory or reconstruction bytes fail before an archive is returned. Native
commands, response sizes and snapshot lifetime are bounded. Failure closes the held
transaction; decrypted database buffers stay in memory and are wiped after packaging.
The CLI writes only encrypted bytes with exclusive creation, and emits
size/digest status only after writing. Existing output is never replaced. Upload
readback, independent job-history authentication and freshness alerts remain later gates.

`verification.json` is inside encryption. It records the snapshot/time/server version,
table/revision manifests, Storage metadata, reconstruction hashes and coverage limits.
The disposable integration check restores the dump and compares **every table's**
computed manifest with this record. Roles remain separately sampled, and Vault/service
credentials are excluded. Supabase configuration is reconstruction source, not a
complete export of live Auth/SMTP settings or deployed secrets. Recreate service
credentials and jobs with jobs disabled on an isolated restore target; managed-service
and Google recovery acceptance remain outstanding.

Named files and SHA-256 digests are packed as JSON/base64, compressed with gzip and
encrypted with a fresh AES-256-GCM key/nonce. RSA-OAEP-SHA256 wraps that data key to
an independent 3072–8192-bit backup recipient. The header is authenticated alongside
the ciphertext. File names and account metadata are encrypted. The binary layout is:
`ELBBACK1` (8 bytes), wrapped-key length (2-byte big-endian), wrapped key, nonce
(12 bytes), authentication tag (16 bytes), and ciphertext. Algorithm changes require
a new format version. Libraries provide the cryptographic operations.

Plain contents are bounded to 64 MiB, encoded/decompressed payload to 128 MiB and
encrypted archive to 50 MiB. Traversal/absolute paths, duplicates, noncanonical base64,
digest mismatch, truncation, header/ciphertext tampering and unsupported formats are
rejected. Auth state and managed-schema definitions are retained conservatively;
restoring into another Supabase stack needs the separate matched-version restore
workflow and protection checks. Vault secrets are excluded by schema selection. No
raw logbook keys or server recovery secrets are packaged.

## Focused checks

```powershell
node --test tools/hosted-backup/archive.test.mjs
node --test tools/hosted-backup/r2-credentials.test.mjs
# Requires the existing local Supabase PostgreSQL on 127.0.0.1:54322:
node --test tools/hosted-backup/archive.integration.test.mjs
node --test tools/hosted-backup/source-access.integration.test.mjs
node --test tools/hosted-backup/orchestrate.integration.test.mjs
node --test tools/hosted-backup/replay-supervised.integration.test.mjs
.\tools\Test-LocalDevelopmentTransfer.ps1
```

The integration check builds `ReplayCheck` using the existing .NET 10 SDK and shared
portable application code. It creates a randomly named disposable source database,
exports synthetic identities, encrypted operations/settings, managed key envelope and
migration fixtures, then authenticates and restores into a second newly created local
database. It compares every row in 15 fixture tables. Production operation/settings
ciphers and materialization must recover six revisions (including parallel corrections,
conflict resolution and a deletion), one active flight, exactly 2.75 command hours,
custom-field values/definitions and currency override. The temporary synthetic KEK
stays outside the archive and travels only in captured test process pipes.

Wrong trusted digest/key and remote targets are rejected before restoring. Missing
identity/envelope/settings/history, a wrong KEK/account and corrupt archives fail
closed. Policy definitions and RLS state survive, original identity relationships
match, CLI overwrite protection holds, and cleanup verifies each owned restore marker.
The
`disposable` connection exception is limited to loopback and a generated fixture
database name; it cannot bypass hosted TLS/identity checks. This is not the real
Preview archive restoration or Google/app recovery acceptance test.

## Disposable restore safety

`restoreDisposable` requires the SHA-256 from trusted job history and fully
authenticates the archive before creating a target. It only accepts the local
Supabase endpoint (`127.0.0.1:54322`) or isolated native fixture (`127.0.0.1:55432`)
and creates a random database from `template0`;
it cannot restore into a caller-specified existing database. A random database
comment marker is verified before SQL restore, reads and deletion. Public database
access is revoked. Reviewed extension prerequisites are installed in their original
schemas before restoring tables; the dump's duplicate schema entries are skipped
through a temporary metadata-only object list. Native restore uses one transaction,
suppresses data-insertion triggers and exits on the first error. Decrypted dump and
row contents stay in memory or the disposable database, never a plaintext dump file.

Cluster roles/ownership/ACLs are deliberately not applied to the shared local server;
they require a separate empty stack/hosted-project restoration procedure. Tests check
preserved RLS/policy definitions, not a full production authorization or Google login
journey. Cron/Edge Functions/SMTP are not started in the new database. A failed target
is retained with its disposable name for investigation by the low-level helper;
the supervised verifier cleans up its identified target on success or failure.
