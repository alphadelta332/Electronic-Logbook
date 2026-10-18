# Logical HTTP backup and reconstruction proof

The recovery requirement is complete logbooks, revision/deletion history, settings,
ownership and account access. This fixture tests reconstruction through compatible
managed services rather than exact replication of provider internals.
It does not weaken the existing credential, encryption, cost or recovery gates.

Implementation: `tools/hosted-backup/logical-rpc-fixture.mjs` and
`tools/hosted-backup/logical-rpc.integration.test.mjs`. No hosted endpoint, token,
schedule or phone change is installed. Both SQL installation and invocation reject
the wrong marked database. The current function name is explicitly a fixture.

Run with the existing local Supabase/Docker stack available:

```powershell
node --test --test-concurrency=1 tools/hosted-backup/archive.test.mjs tools/hosted-backup/logical-rpc.integration.test.mjs
```

The test uses the already installed PostgreSQL 17 tools, Node, .NET replay checker
and cached Supabase PostgreSQL `17.6.1.155` / PostgREST `v14.15` images. No package,
SDK, stored credential or permanent configuration is introduced. Fixture containers
are regenerated output with a verified random label; temporary roles/databases
also have unique verified markers. Existing shared databases are never reset.

## Capability and snapshot

The simulated external caller receives only a signed, expiring role token. Its
role has NOLOGIN, no elevated attributes and no direct application-table access.
The separate temporary API authenticator has that role; its database password and
the synthetic signing secret remain in the test controller/API service, outside
the simulated export client. No real signing or recovery keys are used.

Only one fixed, parameterless RPC is exposed. It is SECURITY DEFINER with a fixed
catalog search path, PUBLIC/ordinary-user execution revoked, and no SQL/table/filter
arguments. It requires GET, a read-only transaction and READ COMMITTED isolation.
A real PostgREST GET supplies the read-only transaction even though the function
is VOLATILE. Fixed ACCESS SHARE locks precede one aggregate data SELECT, which
uses the fresh inner-query snapshot. These are measured project results using
the [PostgreSQL locking primitives](https://www.postgresql.org/docs/17/explicit-locking.html)
and [PostgREST transaction modes](https://docs.postgrest.org/en/stable/references/transactions.html),
not a claim that every hosted gateway/version has been verified.

Actual HTTP checks reject direct table reads and valid-JSON writes, POST export,
ordinary-role/expired/bad-signature tokens, unrelated RPCs, and unexposed net/Vault
schemas. Each rejection checks the expected permission/authentication/method code,
and the source row datasets remain unchanged. This is an API capability boundary;
it does not claim that PostgreSQL PUBLIC privileges vanished. The export caller
cannot use its NOLOGIN API role as an arbitrary SQL connection.

Concurrency checks prove that TRUNCATE/repopulation completed after the outer GET
began is included by the later post-lock data snapshot; a covered TRUNCATE during
capture times out, while a normal INSERT succeeds but remains outside that one
aggregate SELECT's snapshot. Persistent column drift rejects export. No arbitrary
function/role/runtime DDL exclusion or complete provider clone is claimed.

## Data, encryption and reconstruction

All ordinary tables in the local runtime's four declared schemas are covered:
23 Auth, 10 Storage, 11 application and one migration table, plus one synthetic
typed-edge table: **46 tables**. No table is excluded merely because it is empty.
**Preview has a different 47-table inventory.** This fixture does not certify that
the older local Auth runtime can reconstruct Preview; hosted contract/compatible
target mapping is covered separately by the
[Preview contract](hosted-backup-preview-logical-contract.md), with no silent table exclusions.

Ordered column types/generated/identity flags accompany rows. Row JSON is carried
as PostgreSQL-produced strings; JavaScript never parses row numeric values.
This preserves exact bigints/numerics, bytea, arrays, Unicode, NULL/empty values,
JSON, infinities and timestamps. Sorted PostgreSQL row digests cover every table,
including generated values. Unknown shapes and oversized output fail closed.
Positive/noncycling sequence values remain decimal strings and are captured as
non-MVCC state; next allocation is checked separately after restoration.

The existing RSA-recipient/AES-GCM archive encrypts the entire response and public
reference fixture bytes. A trusted ciphertext digest is checked before decryption
and target creation. The archive contains data, not a source native schema dump.
The target is rebuilt using the installed managed runtime's schema definitions
and reviewed tracked application migrations; no outbound cron job is installed.
Rows are restored in dependency order with explicit writable columns, generated
columns recomputed, and all table digests compared. Original Google provider/subject
binding, membership, devices, wrapped key envelope, settings and operation history
are preserved. Reference bytes must match exactly.

The existing production cipher/materializer replays six encrypted revisions,
correction/conflict resolution/deletion history, one active and one deleted entry,
one configuration and one custom field after restoration. Synthetic recovery
material stays in memory/pipes. Missing tables/history/settings/sequence state,
cycling state, wrong shape and corrupted ciphertext reject; created targets are
removed on success and failure.

## Limits and acceptance

This is an end-to-end **local HTTP/data/encryption/schema-reconstruction/application
replay** proof. Managed schema restoration deliberately does not reproduce cluster
ownership/ACLs from the provider. No restored Auth service or real Google login is
started and no real airport files are used. The
[Preview mapping](hosted-backup-preview-logical-contract.md) and
[supervised hosted procedure](hosted-backup-supervised-logical-export.md) cover
separate scopes. Daily scheduling, independent alerts and full owner-operated
recovery remain incomplete. This fixture alone does not establish production
readiness or replace existing safety copies.
