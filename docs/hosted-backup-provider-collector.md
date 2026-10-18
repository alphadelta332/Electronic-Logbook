# Read-only backup provider collection

`tools/hosted-backup/daily-provider-collector.mjs` collects supported GitHub,
Supabase and Cloudflare HTTP observations using separate dedicated monitoring
credentials. It is an importable collector, not a deployed service or a CLI.
No existing worker/job imports it. Automation remains disabled.

## Configuration and credentials

The owner-reviewed configuration contains version 1, `githubUser`, `accountId`,
`supabaseOrganization`, `projectRef`, `bucket`, `deploymentRevisions` and the existing
`r2-policy.json` object as `policy`. Credentials are supplied separately as
`{github, supabaseProject, supabaseOrganization, cloudflare}`. They stay in their
provider's authorization headers, never URLs or returned evidence. There is no
fallback to broader owner/CLI credentials.

| Identity | Required scope | Local authentication file under `%LOCALAPPDATA%/ElectronicLogbook/HostedBackup/` |
| --- | --- | --- |
| GitHub | Public repositories only; Account Plan read-only | `github-plan-read-token.txt` |
| Supabase project | Preview project only; Organization Settings READ and Edge Functions READ | `supabase-metadata-read-token.txt` |
| Supabase organization | Selected organization only; Organization Settings READ; no project capabilities | `supabase-organization-read-token.txt` |
| Cloudflare | Backup account only; Account Analytics Read and Workers R2 Storage Read | `cloudflare-monitoring-read-token.txt` |

Monitoring authentication is excluded from trusted development transfer and must
be recreated on another machine. Optional `cloudflare-monitoring-credentials.local.json`
retains the monitoring identity's labelled credential metadata/S3 credentials; it
is excluded too. Keep upload and restore identities separate. R2 Storage Read also
permits object reads; do not add token-management, billing, Worker edit or R2 edit.
Token expiry is an owner choice; it is independent of the mandatory 31-day
review/source-capability limits. Enter tokens locally, never in chat or command
arguments. Test effective allowed reads and prohibited access before deployment.

Separate Supabase credentials are necessary: project-scoped metadata reads do not
establish organization-plan access, and organization-only access must not grant
project Edge-source access. `collectSupabaseEvidence` checks the organization's
documented `id`, not an assumed `slug`, and returns only plan/deployment observations.
`collectGithubEvidence` returns selected identity/Free-plan/current-UTC-month usage
observations, not account profile, email or unrelated personal fields. The full
collector calls these same exported functions.

## Collected evidence and limits

Collection checks current UTC-month personal Actions usage, Free organization plan,
the exact two active deployed revisions, Standard/private backup bucket including
both public-domain mechanisms, exact lock/lifecycle, account-wide storage classes,
all-bucket month-to-date operations and timestamped storage samples. It rejects
HTTP failures, redirects, pagination, bodies over 1 MiB, malformed/missing data,
changed identity/period/deployment/policy, public access, paid/unknown Actions SKUs,
Infrequent Access usage, pending uploads, truncated and stale/future analytics.
The GraphQL POST reads analytics only. R2 REST bucket class is `storage_class`;
missing or Infrequent Access class fails validation.

The result has `status: collected-not-permitted`. `retrievedAt` is collection time,
not provider measurement time. R2 REST metrics expose no measurement timestamp;
that field remains null. Storage samples include other/deleted buckets and sum
sampled per-bucket peaks, without proving every daily billing peak. Operations are
not silently classified into billable Class A/B. Gross GitHub usage by SKU is not
the remaining included allowance: discounts and different runner types prevent
blind subtraction as Linux minutes.

These gaps must be resolved before producing a permit. A constant-success endpoint,
freshly dated screenshot or nominal free allowance cannot replace supported current
evidence. Live read observations do not prove every token/write boundary, alert
delivery, a deployed counter or a successful backup.

## Validation

```powershell
node --test tools/hosted-backup/daily-provider-collector.test.mjs
pwsh -NoProfile -File tools/Test-LocalDevelopmentTransfer.ps1
```

Tests use controlled HTTP responses and synthetic credentials. Repeat scoped live
permission and response-shape checks for the deployment; keep account usage,
identifiers and receipts private.

Provider references: [GitHub usage and Plan read](https://docs.github.com/en/rest/billing/usage),
[Supabase organization read](https://supabase.com/docs/reference/api/v1-get-an-organization),
[Cloudflare R2 metrics](https://developers.cloudflare.com/api/resources/r2/subresources/buckets/subresources/metrics/methods/list/),
[R2 analytics](https://developers.cloudflare.com/r2/platform/metrics-analytics/),
[R2 managed-domain access](https://developers.cloudflare.com/api/resources/r2/subresources/buckets/subresources/domains/subresources/managed/methods/list/).
