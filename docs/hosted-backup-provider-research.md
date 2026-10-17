# Hosted backup provider research

Research date: 2026-10-17 (Australia/Sydney). Scope: first-party documentation for
an unattended, encrypted off-provider backup of the FlightLogX Preview on Supabase
Free. This is a design recommendation, not evidence of an operating backup. No
credentials, participant data, accounts, infrastructure, or dependencies were changed.

## Recommended starting design

Use the existing GitHub Actions surface for a daily logical database export, encrypt
the complete archive before upload, and store dated objects in a private Cloudflare
R2 Standard bucket. Configure retention separately from the upload job. R2 is a
candidate pending account, cost, region, and restore checks below; this research did
not compare every provider.

Supabase explicitly recommends regular CLI exports and off-site copies for Free
projects. Database backups contain Storage metadata, **not Storage file contents**.
Project deletion also deletes provider-held backups. These facts support an
independent destination. [Supabase database backups](https://supabase.com/docs/guides/platform/backups)

## Export and restore boundaries

- A plain `supabase db dump` exports schema, excludes Supabase-managed schema
  definitions including `auth`, `storage`, and extension schemas, and contains no
  data or custom roles. Use separate `--role-only` and `--data-only --use-copy`
  exports. Inspect `--dry-run` with the chosen pinned CLI before implementation;
  do not infer backup coverage from a successful schema-only command.
  [Supabase CLI reference](https://supabase.com/docs/reference/cli/supabase-db-dump)
- The documented roles/schema/data procedure includes `auth.users` and related
  account data. Thus exclusion of managed schema **definitions** does not mean
  Auth account rows should be omitted. JWT/API keys, provider settings, SMTP,
  Edge Functions, Storage objects, and domain configuration require separate
  reconstruction. Custom login-role passwords are not included. Version differences
  between hosted and local Auth/Postgres can break a restore; test the intended
  versions. [Supabase platform-to-self-hosted restore](https://supabase.com/docs/guides/self-hosting/restore-from-platform)
- Use the Session pooler connection from Dashboard Connect, port 5432, by default;
  direct connections require IPv6 or the IPv4 add-on. The documented export has
  roles, schema, and data files, excluding two Storage vector metadata tables in
  its data command. Preserve `supabase_migrations` separately if required.
  Customizations to `auth`/`storage` need separate treatment from migration files or
  the documented diff procedure. Restore roles, schema, then data with
  `psql --single-transaction --variable ON_ERROR_STOP=1` and the documented trigger
  handling; re-enable required Realtime publications. Vault/column encryption has
  additional key requirements. [Supabase CLI backup and restore](https://supabase.com/docs/guides/platform/migrating-within-supabase/backup-restore)

The implementation must verify the actual pinned CLI's output includes the expected
Auth and application tables, then prove restored counts, policies, functions, and
encrypted logbook usability. A SQL restore alone cannot establish that a participant
still has a working app decryption key. That last requirement is an application
recovery check, independent of the provider documentation.

## R2 access, retention, and encryption

- R2 supports bucket-scoped Object Read & Write credentials for its S3 API; Admin
  credentials also change bucket configuration. R2 must be activated before token
  creation. Recommendation: one bucket-scoped upload credential in Actions,
  separate owner-held configuration authority, and a separate read-only restore
  credential. Do not describe the upload credential as append-only: the documented
  permission group does not establish that restriction.
  [R2 authentication](https://developers.cloudflare.com/r2/api/tokens/)
- Bucket locks prevent overwrites and deletes during retention and take precedence
  over lifecycle deletion. Authorized administrators can remove lock rules.
  Recommendation: dated unique object names, 30-day lock and 30-day lifecycle
  expiry as an initial proposal; measure costs before choosing longer retention.
  Keep lock-changing authority outside the scheduled job. This protects against
  upload-credential compromise, not account-owner compromise or provider loss.
  [R2 bucket locks](https://developers.cloudflare.com/r2/buckets/bucket-locks/)
- R2 automatically encrypts objects and metadata at rest using provider-managed
  keys and supports TLS. That alone gives no independently held recovery secret.
  [R2 data security](https://developers.cloudflare.com/r2/reference/data-security/)
- Recommendation: encrypt the archive using `age` to an owner-held public
  recipient. Only the public recipient goes into the backup job; the private
  identity stays in separate owner recovery storage, with a second protected copy.
  This uses the tool's documented recipient-encrypt/identity-decrypt model. A
  separate controlled restore rehearsal can hold the private identity temporarily;
  the routine upload workflow does not need it. [age documentation](https://github.com/FiloSottile/age)

Private means no public domain or `r2.dev` access. Archive plaintext, database
passwords, and decrypted contents must not enter Actions logs or public artifacts.
These are proposed implementation requirements, not verified configuration facts.

Oceania (`oc`) is a best-effort location hint, **not Australian residency**. The
documented enforceable jurisdictions are EU, US, and FedRAMP. If Australian storage
is required, this R2 proposal needs a different provider or a specific contractual
guarantee. [R2 data location](https://developers.cloudflare.com/r2/reference/data-location/)

## Scheduling and proof of freshness

GitHub scheduled workflows run from the default branch. Runs can be delayed or
dropped under load; use a minute away from the hour boundary. In public repositories,
60 days without repository activity disables schedules. Consequently a daily schedule
provides a target backup interval, not a guaranteed 24-hour recovery point.
[GitHub scheduled events](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#schedule)

Repository constraint: changing release-branch state requires owner authorization.
A workflow left only on `dev` cannot supply the scheduled service. Before deployment,
either obtain explicit permission for a narrowly scoped default-branch workflow or
authorize a separate private automation repository. The latter also requires checking
private-repository Actions allowances; it is not infrastructure already approved or
created by this note.

GitHub offers failure-only email/web notifications when enabled. Scheduled notices
go to the creator, later cron editor, or person who re-enabled the workflow as
documented. Confirm the intended owner receives a deliberate failed test run.
[GitHub workflow notifications](https://docs.github.com/en/actions/concepts/workflows-and-actions/notifications-for-workflow-runs)

Inference: failure notifications cannot prove a workflow ran at all. An independent
freshness check must alert when no completed backup has arrived within the agreed
window. Two workflows in the same inactive repository do not independently cover
the 60-day disable failure. Select that monitoring destination before calling the
design unattended and operational.

## Costs and unresolved checks

R2 Standard includes 10 GB-month storage, one million Class A operations, and ten
million Class B operations monthly. Above that, listed storage is USD 0.015/GB-month;
Class A/B are USD 4.50/0.36 per million. Direct egress is free. The free tier does not
apply to Infrequent Access. Retention consumes storage even when objects are locked;
estimate using actual encrypted archive size times retained copies, including all
other account usage. R2 is metered, not an unconditional zero-cost backup service.
[R2 pricing](https://developers.cloudflare.com/r2/pricing/)

Standard GitHub-hosted runner usage in public repositories is free; private repository
allowances and larger runners have different billing. Confirm the actual repository
visibility, runner, and account allowances. [GitHub Actions billing](https://docs.github.com/en/billing/concepts/product-billing/github-actions)

Owner follow-up (2026-10-17): a private automation repository, protected offline copy
and email alerts were selected. R2 acceptance is conditional on zero expected cost
and notification if payment becomes necessary. Cloudflare's budget alerts notify
after cumulative spending crosses a chosen threshold and do not cap usage; they
cannot guarantee a warning before the first charge.
[Cloudflare budget alerts](https://developers.cloudflare.com/billing/manage/budget-alerts/)

GitHub supports stopping metered Actions usage at a budget limit and alerts when
included usage reaches 90%/100%; inspect the actual account before relying on either.
[GitHub budgets and alerts](https://docs.github.com/en/billing/concepts/budgets-and-alerts)

Resend Free currently lists 3,000 emails/month and 100/day. Rare, deduplicated alerts
fit that model, subject to the existing account's plan and shared usage. Paid email
overages are available on paid subscriptions; do not enable or upgrade them for this
task. Reuse the verified domain without changing authentication email behaviour.
[Resend pricing](https://resend.com/pricing)

Before deployment, establish: the selected R2 account/region and billing setup;
private bucket configuration; measured backup size and retention budget; a safe
database credential/SSL strategy; the owner recovery identity location; required
Storage/Vault/config exports; independent missing-backup alerts; and a disposable
restore target with explicit acceptance checks. None was verified against live
infrastructure in this research. Do not claim a recoverable backup until an uploaded
archive has been downloaded, decrypted, restored, and checked.
