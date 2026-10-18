# Daily export capability lifecycle

The daily job is not deployed or scheduled. This component implements
the source credential's installation, expiry, rotation and revocation. The
existing supervised export continues to use its short-lived temporary endpoint.

`logicalCapabilityRpcSql(contract, tokenHash, expiresAt, database, {daily: true})`
generates owner-executed SQL for the same fixed, parameterless GET/read-only snapshot.
Generate a random 32-byte token and pass its SHA-256 hex digest to the builder; the
raw token belongs only in the approved private job secret. Never print generated
SQL or tokens in workflow logs. Daily expiry is bounded to 31 days; arrange owner
rotation before expiry. Expiry fails closed and does not extend itself.

Installation refuses any existing endpoint. It audits the anonymous role's
attributes, memberships, all table/column privileges in the exposed schemas and
the exact reviewed function fingerprints in `preview-logical-contract.json`.
The function owner must bypass RLS. The installed function retains the coherent
snapshot, shape/generated-expression/migration checks and 32 MiB response limit.
An owner controller must separately verify the expected project, free plan, exact
PostgREST schema exposure (`public,graphql_public`) and current usage headroom before
installation. This SQL builder does not inspect provider configuration or billing.

For rotation, supply `{daily: true, expectedDefinitionHash}` with a new token digest
and expiry. Obtain the expected hash privately from the previously verified function
definition (`pg_get_functiondef`, UTF-8, SHA-256). The current marker and definition
must match before replacement; otherwise the transaction stops without changing it.
The old token stops working when rotation commits. Copy the new token to the job
through GitHub's encrypted secret facility; a brief failed run during handoff is
preferable to retaining both credentials. Rotation repeats the API permission audit.

`revokeLogicalCapabilitySql(expectedDefinitionHash, database)` removes only the
matching marked daily endpoint and reloads PostgREST's schema cache. Revocation does
not require a clean anonymous-role audit, so unrelated permission drift cannot block
removal. It refuses an absent, changed or unrelated function. Inspect drift as the
owner rather than accepting a newly observed hash without reviewing the change.
Lifecycle mutations share a transaction-scoped advisory lock. No archive, table,
participant identity, phone state, signing key or recovery material is changed.

These are owner-side SQL builders, not an unattended owner controller. A future
routine job receives only the scoped export token, publishable key, public archive
recipient, reviewed reconstruction inputs and the scoped R2 upload credential.
Do not put database, management, service, signing or recovery credentials in it.

Validation:

```powershell
node --test tools/hosted-backup/logical-capability.test.mjs
```

The test starts its own synthetic PostgreSQL 17 cluster on loopback at a free port,
uses a randomly named fixture database, then stops and removes that exact cluster.
It uses installed `initdb`/`pg_ctl`, no Docker, hosted data or existing native fixture.
It proves valid reads, token expiry, denied malformed/wrong tokens, POST/write-mode/
authenticated access, immediate old-token denial after rotation, guarded replacement
and removal, role/table/column/function permission drift, and unchanged source rows.
It also exercises the unchanged supervised call and its marker/lifetime boundary.

Deployment, live hosted lifecycle checks, a scheduled export/upload/independent
readback, trusted receipts and current provider cost stops remain deployment prerequisites.
Private Actions allowance and paid-usage blocking must be verified first. Independent
failure/staleness alerts and owner-operated managed-service/Google recovery remain
separate acceptance stages. A passing local lifecycle test does not establish a backup.

Provider references: [GitHub budgets and stop-usage controls](https://docs.github.com/en/billing/concepts/budgets-and-alerts)
and [Supabase database function security](https://supabase.com/docs/guides/database/functions).
Provider configuration and headroom must be checked at deployment time.
