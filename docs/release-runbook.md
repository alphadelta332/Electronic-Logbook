# Release Runbook

This is the canonical release procedure for Electronic Logbook. Use it instead
of reconstructing release steps from `AGENTS.md`.

## Release Shape

- Version source of truth: `version.txt`
- Public workbook: `Electronic_Logbook_Master.xlsm`
- Release branch/channel in public workbooks: `main`
- Release workflow: `.github/workflows/publish-release.yml`
- Release workflow input: a full 40-character commit SHA already merged to
  `origin/main`
- Required self-hosted runner labels for Excel validation:
  `self-hosted`, `windows`, `excel`

The workflow has two protected `release` environment gates:

1. Validate release candidate
2. Publish approved release

## Normal Command

After the release candidate has been merged to `main`, run:

```powershell
.\tools\Invoke-ReleaseWorkflow.ps1 -Version 2.0.3 -ApproveEnvironmentGates
```

The script resolves `origin/main`, runs preflights, dispatches the workflow,
approves gates when the current GitHub account is allowed to approve them, and
watches the run to completion.

Use `-NoWatch` only when handing the release off to someone else after dispatch.

## Release Preparation

1. Confirm `version.txt` contains the intended semantic version.
2. Confirm `README.md` has the intended changelog entry and that the target
   version is the first release entry under `## Changelog`.
3. Prepare the workbook and artifacts:

```powershell
.\tools\ReleaseChecklist.ps1 -SkipWorkingCopy
```

For hotfixes, run the same checks from the hotfix branch with the branch guard
skipped only when the branch is deliberately not `dev`:

```powershell
.\tools\ReleaseChecklist.ps1 -SkipWorkingCopy -SkipGitChecks
```

4. If Excel rewrites package metadata after workbook preparation, sanitize the
   public workbook package and re-run readiness:

```powershell
Import-Module .\tools\ReleaseTools.psm1 -Force
Set-WorkbookCustomPropertyFileValue `
  -WorkbookPath .\Electronic_Logbook_Master.xlsm `
  -Name ElectronicLogbookVersion `
  -Value ((Get-Content .\version.txt -Raw).Trim())
.\tools\Test-WorkbookPublicReadiness.ps1
```

5. Confirm `Test-WorkbookPublicReadiness.ps1` passes. This includes verifying
   internal worksheets such as `Admin`, `Routes`, `Airports`, and `ChartData`
   are `VeryHidden`.
6. Commit the release preparation changes and merge them to `main` through a PR.

## Preflight Checklist

Before dispatching the release workflow, verify:

- `git status --short` has no unreviewed tracked changes.
- The selected commit is on `origin/main`.
- `git show <commit>:version.txt` matches the intended version.
- `README.md` at the selected commit has one changelog heading for the version.
- No remote tag `vX.Y.Z` exists.
- No GitHub release `vX.Y.Z` exists.
- The self-hosted runner with labels `self-hosted`, `windows`, `excel` is
  online.

`Invoke-ReleaseWorkflow.ps1` performs these preflights automatically.

## Workflow Gates

The release workflow intentionally requires environment approval before:

- running Excel-based validation on the self-hosted runner
- publishing the protected tag and release assets

If the script is run without `-ApproveEnvironmentGates`, approve each pending
deployment in GitHub when validation/publishing reaches the `waiting` state.

## Success Criteria

The release is complete only when all are true:

- The `Promote release` workflow has completed successfully.
- The protected tag `vX.Y.Z` exists.
- The GitHub release `vX.Y.Z` exists and is not a draft.
- The release assets include:
  - `Electronic_Logbook_Master.xlsm`
  - `README.pdf`
  - `ElectronicLogbook.Updater.Wizard.exe`
  - `ElectronicLogbook.Updater.Wizard.win-x64.zip`
  - `wizard-signature-report.json`
  - `SHA256SUMS.txt`
  - `release-manifest.json`
  - `release-manifest.json.sig`
  - `release-validation.json`

## Patching An Existing Release Asset

Prefer a new patch release for normal fixes. Use an in-place asset patch only
when the version number must remain unchanged and the existing tag/release has
not moved.

When patching a release asset, replace every integrity file that describes that
asset. For the public workbook this means:

- `Electronic_Logbook_Master.xlsm`
- `SHA256SUMS.txt`
- `release-manifest.json`
- `release-manifest.json.sig`
- `release-validation.json`

Generate the replacement integrity files from a complete artifact folder:

```powershell
.\tools\New-ReleaseArtifactIntegrity.ps1 `
  -ArtifactsPath "<folder-with-release-assets>" `
  -Version 2.0.3 `
  -Commit "<main-commit-containing-the-fixed-asset>"
```

Then upload the replacement assets with:

```powershell
gh release upload v2.0.3 `
  Electronic_Logbook_Master.xlsm `
  release-manifest.json `
  release-manifest.json.sig `
  SHA256SUMS.txt `
  release-validation.json `
  --repo alphadelta332/Electronic-Logbook `
  --clobber
```

## Runner Recovery

If the workflow waits on:

```text
Waiting for a runner to pick up this job...
```

check GitHub runner status:

```powershell
gh api repos/alphadelta332/Electronic-Logbook/actions/runners --paginate
```

For this workstation, the runner is configured under:

```text
X:\GitHub\actions-runner
```

and can be started by the scheduled task:

```powershell
schtasks.exe /Run /TN ElectronicLogbookGitHubActionsRunner
```

The workflow can continue automatically once the runner is online.
