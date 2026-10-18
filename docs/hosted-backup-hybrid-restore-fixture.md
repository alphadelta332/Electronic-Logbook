# Disposable PG17 hybrid restoration proof

`tools/hosted-backup/hybrid-restore.integration.test.mjs` connects the existing
metadata guard, archive encryption and application replay helpers in one synthetic
restoration fixture. It is separate from the supervised native archive workflow.
No hosted connection, unattended credential, phone operation or scheduler is added.

Run against the existing isolated PostgreSQL 17 fixture on `127.0.0.1:55432`:

```powershell
node --test --test-concurrency=1 tools/hosted-backup/metadata-guard.integration.test.mjs tools/hosted-backup/hybrid-restore.integration.test.mjs
```

Run these serially: the guard intentionally inventories shared catalogs, so a second
fixture creating databases or roles concurrently would cause legitimate drift.
The test uses the existing PostgreSQL and .NET tools and does not start a server.

## What the fixture proves

The simulated owner captures a native schema-only custom archive and the declared
metadata guard, then encrypts both to a synthetic RSA recipient. A separate simulated
routine reader has no role memberships or elevated role attributes, no base-schema
access, no password-catalog access, and no sequence allocation or view-write permission.
The reader can select only fixed views (plus one base column whose schema remains
inaccessible). This is a permission experiment, not independently isolated processes:
the test controller retains its disposable cluster administrator connection.

The reader locks all fixed views before exporting a repeatable-read snapshot. Text
COPY preserves explicit writable columns using PostgreSQL type serialization, with
fixed UTF-8, date, timezone, interval, bytea and float settings. Generated columns are
omitted from insertion and independently compared after recomputation. Row manifests
include every column, including generated values. Eight tables cover encrypted
application history, identities, configuration, a migration ledger, an empty table,
and typed edge cases: large exact numerics, NULL/empty values, escaped text and Unicode,
bytea, arrays, JSONB, enum, intervals, timestamp offsets and timestamp infinities.

Each base-table TRUNCATE CASCADE is rejected while the locks are held. A concurrent
committed insertion succeeds but stays outside the captured row manifests. Sequence
allocation also succeeds; its state is deliberately captured separately after the
rows, without converting bigint strings to JavaScript numbers. The two sequences
exercise positive nonunit increments, cached allocation ahead of copied IDs, and an
unused sequence above JavaScript's exact integer range. This is not an MVCC snapshot
of sequence state or proof against concurrent resets/exhaustion.

The restorer authenticates the baseline and data against their trusted digests and
decrypts only in memory. It creates a new marked database, restores native pre-data,
COPY rows and sequence state, then native post-data. It retains template0's `public`
schema through a temporary metadata-only restore list, as the existing native helper
does: dropping/recreating it lost its default privileges in the first experiment.
All plaintext dump and COPY bytes stay in memory or the disposable databases.

Restoration compares every typed row and generated value, native column/default/type
definitions, relation/schema owners and ACLs, column ACLs, constraints, indexes, RLS
flags/policies, trigger/function/view definitions and sequence definitions. Actual
queries also exercise restored RLS/column grants, defaults, generated computation,
trigger/check/foreign-key enforcement, and exact safe next allocation. Cluster roles
already exist; this does not demonstrate role recreation on a separate server.

Both existing application replay modes decrypt and materialize restored records.
The golden document digest must match the seed, including six operation revisions,
conflict resolution, one active and one deleted entry, 2.75 command hours, custom
field values/definitions and the currency override. The synthetic KEK stays outside
both encrypted archives and travels only through captured process pipes.

Negative checks reject missing files, missing row content, malformed typed COPY,
missing/invalid/unsafe sequence state, digest or ciphertext corruption, unavailable
settings/envelope, altered encrypted payloads, wrong recovery key, and persistent
metadata drift before or during export. Every successful or failed created restore
target is deleted through its verified random marker. Finally the source and its
random roles are removed; only the server's ordinary private logs remain.

## Limits

This uses the same PostgreSQL 17 runtime and existing cluster roles. It does not
prove Supabase Auth/Storage completeness, managed extension/runtime compatibility,
hosted view permissions or full managed-service/Google/phone recovery. The existing
metadata contract still permits A-to-B-to-A DDL between fresh endpoint observations;
an imported snapshot does not make deparsers coherent with catalog rows. Exact
hosted scope and a defensible coherence rule remain prerequisites for promoting
this candidate. No new dependency, private configuration or operational backup
format is introduced by this fixture.
