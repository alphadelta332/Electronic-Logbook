# Read-only backup provider collection

`tools/hosted-backup/daily-provider-collector.mjs` collects supported GitHub,
Supabase and Cloudflare HTTP observations using separate dedicated monitoring
credentials. It is an importable collector, not a deployed service or a CLI.
The disabled primary scheduled composition imports it through the durable request
helper; neither export/readback job receives these credentials. Automation remains disabled.

Initial live version-2 validation on 20 October 2026 rejected: government-region
list requests returned403/400, and the default list omitted optional metadata.
Follow-up reads with the unchanged dedicated token proved default-bucket details
and an explicit active `r2.fedramp: false` account entitlement. The collector now
requires that positive disabled-feature evidence before inspecting ordinary
regions, and gets required class/jurisdiction from bucket details. It does not
interpret403/400 as empty inventories. Full revised live refresh on21October2026
passed those prerequisites but rejected operation history for a retired bucket
outside the reviewed single-bucket contract. A follow-up full live refresh accepted
the bounded retired operation review, then rejected a storage-history row for that
same retired bucket. A separate charged diagnostic identified one zero-valued row.
The optional exact zero-sample review below is implemented and locally tested;
unreviewed rows still reject. A full live refresh on21October2026 passed all fifteen
reads, both complete usage/storage windows, freshness and SQLite cache reopen.
Status remains collected-not-permitted: billing-review/permit commissioning,
deployment and scheduled backup/recovery acceptance remain separate gates.

## Configuration and credentials

The owner-reviewed configuration contains version 2, `githubUser`, `accountId`,
`supabaseOrganization`, `projectRef`, `bucket`, `deploymentRevisions` and the existing
`r2-policy.json` object as `policy`, plus `r2Period: {periodStart, periodEnd}` from
the actual owner-reviewed R2 billing baseline. There is no calendar-month fallback.
The period must contain collection time and span at most 62 days; longer/expired
periods reject before provider I/O. These are in-memory inputs; no new local
configuration file, credential path, environment variable or runtime is introduced.
Optional `retiredBucketReview: {bucket, observedAt}` admits one exact, explicitly
reviewed retired bucket name. It must differ from the live bucket, contain no
wildcard, and have a non-future observation less than31days old. Unknown fields
reject; count ceilings cannot be configured. The only admitted operations are
at most one `PutBucket` and one `DeleteBucket` per complete window, summed across
its query pieces. Groups may disappear as history ages out. Every request still
counts in that window. A second create/delete, any other operation (even with
zero requests), unreviewed storage, or live reappearance in any jurisdiction rejects.
Without this input, all other buckets still reject.

The review may additionally contain `zeroStorageAt`, taken from the exact observed
storage sample. It must be a valid timestamp no later than `observedAt` and less
than31days old. This admits at most one sample per complete window, only for that
exact retired name and timestamp, with numeric `payloadSize`, `metadataSize` and
`uploadCount` all exactly zero. Missing/unknown group, dimension or measurement
fields reject. Different times/names, extra samples, any nonzero/invalid values,
other operations or live reappearance still reject. Review timestamps are bound
in the cache fingerprint; both observation and sample expiry apply on cache reads.
Omitting `zeroStorageAt` retains the prior any-other-storage rejection.

Each window retains `zeroStorageSamples` only when this explicit review is present.
The sample is acknowledged metadata: it does not update live-bucket freshness,
storage totals or peaks, establish probe provenance, prove historical emptiness or
replace a fresh live-bucket measurement. Different buckets may legitimately have
samples at the same time; duplicate groups are checked by bucket and timestamp.
Half-open split windows count the reviewed sample once, and requests remain fully
counted regardless of the sample. Expiry cannot be renewed by changing only the
review observation time; the historical sample has its own31day limit.

The returned `r2.retiredBucketReview` retains the exact input and the two windows'
observed create/delete counts. The input is included in the existing cache
fingerprint; changing it cannot reuse a previous result or reset request counters.
The31day expiry is also enforced on cache reads. This is acceptance of bounded
observed metadata, not proof of who created the bucket, exact operation times,
historical emptiness or a new owner billing/activation review. Aggregated analytics
do not authenticate probe provenance. Review the exact evidence before supplying
this input; do not infer permission from a name prefix or from current absence.
Credentials are supplied separately as
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

Each Actions observation preserves the provider's `netAmount` alongside SKU and
gross quantity. The collector still rejects any nonzero net amount; exporting the
actual checked value lets the version-3 permit validator enforce that same rule
without inventing a live remaining allowance. The collector supplies the permit's
two R2 query windows and measured storage fields, but never issues a permit itself.

## Collected evidence and limits

Collection checks current UTC-month personal Actions usage, Free organization plan,
the exact two active deployed revisions, Standard/private backup bucket including
both public-domain mechanisms, exact lock/lifecycle, account-wide storage classes,
all-bucket operations for the reviewed R2 period and the preceding 31 days, and
timestamped storage samples. Each window is half-open `[from, until)`, ends at the
same collection time, and is split into adjacent pieces of at most 31 days. One
GraphQL request carries all pieces (at most three), without overlapping boundaries
within either window; the two window totals are separate, never added together.
An empty operation array records zero observed requests; missing/truncated arrays
reject. Empty historical storage pieces are allowed, but each complete window
must include a storage measurement less than two hours old. It rejects
HTTP failures, redirects, pagination, bodies over 1 MiB, malformed/missing data,
changed identity/period/deployment/policy, public access, paid/unknown Actions SKUs,
Infrequent Access usage, pending uploads, truncated and stale/future analytics.
The GraphQL POST reads analytics only. Before other provider reads, the account
entitlements GET must prove R2 enabled, FedRAMP disabled and an exact active R2
feature inventory (`r2.enabled`, `r2.fedramp`, `r2.infrequent_access`). Missing,
duplicate, unknown, malformed, mismatched-ID or future-deleted R2 features reject;
enabled government access stops for review. Other account features are not retained.
Unfiltered lists then cover all ordinary jurisdictions (`default`, `eu`, `us`);
only the reviewed bucket in default is accepted. Government namespaces are excluded
on the authenticated disabled entitlement, never an HTTP denial or an assumed
plan. Missing counts, continuation cursors, denied ordinary jurisdictions and
extra buckets reject.
List items must name the reviewed bucket; optional class/jurisdiction fields must
agree when present. A separate default-jurisdiction bucket-details GET must name
that same bucket and prove `jurisdiction: default` and `storage_class: Standard`.
Missing, changed or Infrequent Access details reject; no defaults are invented.
Unknown operation names, storage classes/states and unreviewed historical/deleted
buckets reject too. Account-level `ListBuckets`/`UsageSummary` may have an empty
bucket name. Known operation names follow the pricing lists, except tier
transitions, which require review. Every observed request counts toward each
window's total, including free/error requests; no Class A/B allowance is inferred.

The result has `status: collected-not-permitted`. `retrievedAt` is collection time,
not provider measurement time. R2 REST metrics expose no measurement timestamp;
that field remains null. Storage samples compute sampled per-bucket peaks, without
proving every daily billing peak. All other/deleted bucket storage stops collection;
only the explicit bounded retired operation review above is accepted. Operations are
not silently classified into billable Class A/B. Gross GitHub usage by SKU is not
the remaining included allowance: discounts and different runner types prevent
blind subtraction as Linux minutes.

R2 `windows` contains `{from, until, requests}` first for the supplied reviewed
period, then for the rolling 31 days. `reviewedPeriod` preserves only its supplied
start/end dates; it does not authenticate or refresh the owner's inspection.
`billingPeriodProved: false` and `monthlyBillingCoverageProved: false` remain:
the API neither discovers the actual billing period nor proves complete billing
usage. The controller must bind these inputs to its approved owner review.

`currentBytes` includes REST published/uploaded payload and metadata across both
storage classes. `uploadedObjects`/`uploadedBytes` and
`infrequentAccessObjects`/`infrequentAccessBytes` preserve the actual REST counts.
`pendingUploads` is the maximum observed analytics upload count for the sole
reviewed bucket, not a sum of repeated time samples. Any nonzero pending count,
uploaded bytes/objects or Infrequent Access bytes/objects rejects, even a zero-byte
object. Successful zeros therefore come from checked measurements, not defaults.

A collection makes eleven Cloudflare reads: account entitlements, three jurisdiction
lists, bucket details, account metrics, lock/lifecycle, two public-domain checks
and one combined GraphQL request.
It makes two GitHub and two Supabase reads separately. There are no retries or
pagination loops. This exceeds the earlier seven-read estimate: the service must
enforce **at most 21 collection batches per UTC day**. `21 * 11 + 8 + 12 = 251`
attempted component requests/day stays below256. No provider budget or saved
counter is increased or reset. `daily-request-controls.mjs` now enforces
these ceilings and cached evidence locally as described below; deployment remains
gated on live validation and the remaining acceptance. A future provider
schema/jurisdiction change requires review, not implicit extra calls or scopes.

## Durable request limits and cache

The importable `refreshProviderEvidence` is an internal operation, with no HTTP
route or timer. It uses the existing stable `DAILY_ATTEMPTS` object's storage.
One transaction claims a collection before I/O: at most 21 starts per UTC day and
one start per elapsed hour, including failures. Concurrent callers cannot share a
miss and create more collections. A five-minute persisted lease prevents abandoned
or late work from publishing; a restart does not refund a batch or allow an hourly
retry. Configuration/credential rotation does not reset either counter. No provider
credential is stored in the cache.

Before each outbound attempt, a transaction records its component's count. Daily
ceilings are 231 Cloudflare collector, 42 GitHub, 42 Supabase, 8 workflow R2 and
12 future monitor R2 attempts. Each collection also has exact maximum envelopes
of 11/2/2 provider calls. Unknown provider origins and extra calls deny; there are
no retries. Counts use the outbound reservation's UTC day, including midnight
crossings; backwards dates deny. Failures/lost responses consume counts. The
monitor must use `reserveComponentRequest` before every R2 call when implemented;
no monitor or public monitor-reservation endpoint exists in this batch.

`cachedProviderEvidence` only reads the saved result, never calls providers or
refreshes. Evidence keeps its original retrieval/measurement times and remains
`collected-not-permitted`. Missing, changed-input, future, two-hour-old evidence or
underlying measurements deny. The stored result is at most 16 KiB. Starting any
refresh invalidates the preceding result, so a failure cannot serve stale success.
A later permit service must validate the historical review and current evidence
without changing their dates. The production Worker still exposes only source,
readback and individual workflow-request reservations; public requests cannot
invoke collection. No real provider inputs, credentials, schedule or service were
created. Live shapes/scopes, Workers Free headroom, alerts and acceptance remain
deployment gates.

The GitHub usage report also combines included-allowance and public-run discounts.
The approved Plan-read token passed both summary and usage-report reads on
19 October 2026, but neither response identifies the discount reason or remaining
included balance. Zero net charges do not fill that gap. Keep both provider proof
flags false until supported evidence or an explicitly approved replacement control
establishes the required assurance.

Deployment must prove these live response shapes with the dedicated tokens and
real reviewed period before issuing a permit. A constant-success endpoint,
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
[Bucket inventory and jurisdictions](https://developers.cloudflare.com/api/resources/r2/subresources/buckets/methods/list/),
[Bucket details](https://developers.cloudflare.com/api/resources/r2/subresources/buckets/methods/get/),
[Account entitlements](https://developers.cloudflare.com/api/resources/accounts/subresources/entitlements/methods/list/),
[Available jurisdictions and Enterprise FedRAMP access](https://developers.cloudflare.com/r2/reference/data-location/),
[R2 operation names](https://developers.cloudflare.com/r2/pricing/),
[R2 analytics](https://developers.cloudflare.com/r2/platform/metrics-analytics/),
[Cloudflare billing cycles](https://developers.cloudflare.com/billing/understand/how-billing-works/),
[GitHub report fields](https://docs.github.com/en/billing/reference/billing-reports),
[R2 managed-domain access](https://developers.cloudflare.com/api/resources/r2/subresources/buckets/subresources/domains/subresources/managed/methods/list/).
