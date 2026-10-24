# Project documentation

Public documentation describes maintained behaviour, design decisions, interfaces
and reproducible operating or validation procedures.

| Area | Start here |
| --- | --- |
| Architecture and security | [Hosted sync](hosted-sync-architecture.md), [account recovery](account-recovery-threat-model.md) |
| Preview operation | [Operator runbook](flightlogx-preview-runbook.md), [Android installation](flightlogx-preview-android-install.md), [Supabase setup](hosted-preview-supabase.md) |
| Backup requirements and status | [Recovery contract](hosted-preview-backup-plan.md) |
| Backup operation and formats | [Backup tooling](hosted-backup-tools.md), [supervised logical export](hosted-backup-supervised-logical-export.md) |
| Logical reconstruction | [Preview compatibility contract](hosted-backup-preview-logical-contract.md), [local HTTP fixture](hosted-backup-logical-http-fixture.md) |
| Daily backup components | [Capability lifecycle](hosted-backup-daily-capability.md), [runner and controls](hosted-backup-daily-runner.md), [provider collector](hosted-backup-provider-collector.md), [internal monitoring policy](hosted-backup-monitor.md) |
| Experimental backup boundary | [Metadata guard](hosted-backup-metadata-guard-contract.md), [hybrid restore fixture](hosted-backup-hybrid-restore-fixture.md) |
| Other maintenance | [Airport catalogue](airport-catalog-updates.md), [Route map](route-map.md), [LogTen format](logten-dynamic-export.md), [UI consistency](product-ui-consistency.md), [architecture map](architecture-graph.md), [releases](release-runbook.md), [public hardening gate](public-release-hardening-gate.md) |

## What belongs in Git

Commit reusable instructions, security boundaries, accepted design rationale,
machine-readable contracts and focused test procedures alongside their code.
Label experiments and incomplete deployment/recovery acceptance explicitly.
Provider-dependent claims need a source and a review date or deployment-time check.

Keep conversation history, task handoffs, owner approvals, account-specific usage,
participant statistics and investigation diaries local. Distil a research report's
lasting decision, reason, limits and primary references into the maintained document
instead of accumulating competing reports or stale next-task instructions.

Working Markdown research belongs in ignored `docs/local/`. It is optional private
reference material in the trusted-device transfer manifest. Credentials, participant
records, generated evidence and recovery archives retain their separate protected
or excluded locations. Public docs must stand alone without links to ignored files.
The local work queue and agent instructions remain ignored.

Use generic paths and configuration names in public examples. Deployment identifiers
required by executable code stay consistent with that code; naming a private resource
does not expose its contents, but public prose need not repeat its account details.
Removing an already committed document changes the current tree, not Git history.
