# Conditional Public Release Hardening Gate

Status: intentionally not started

Last reviewed: 2026-08-06

Public release hardening must not start until there is an explicit public-release
decision and private-pilot evidence. This guard protects the Android-first private pilot
from accidentally adding public signup, billing, team administration, public uptime
promises, user-owned cloud sync, or a separate Windows companion app before the product
has earned that complexity.

## Entry Criteria

All criteria are required:

- private pilot exit decision is `pass` or `pass with issues`;
- pilot evidence covers app-only and workbook-linked users across the full run;
- no unresolved S0 or S1 data-safety, sync-security, or recovery incidents remain;
- current Supabase plan limits, region, Auth configuration, RLS harness, restore
  rehearsal, and diagnostics-redaction checks are freshly verified;
- project owner explicitly decides to pursue public release;
- public scope is written down, including supported countries, supported devices,
  workbook support level, support channels, incident policy, and paid-plan threshold.

## Hardening Work That Remains Parked

Do not start these until the entry criteria pass:

- public signup or waitlist;
- billing, subscriptions, premium hosted recovery, or paid support tiers;
- public uptime, backup, recovery-time, or recovery-point promises;
- team administration or multi-user logbook ownership;
- iPhone or iPad compatibility;
- Authenticode signing;
- remote diagnostic upload;
- realtime/push sync;
- public documentation, release-candidate packaging, or marketplace/listing work.

## Decision Record Template

When public-release hardening is explicitly started, create a dated decision artifact
with:

- pilot exit decision and evidence links;
- supported launch cohort and geography;
- current hosted-provider limits and selected plan;
- security and privacy changes since the pilot;
- release definition of done;
- rollback and support policy;
- non-goals that remain excluded.
