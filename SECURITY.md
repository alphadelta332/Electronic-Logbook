# Security Policy

## Supported Versions

Only the latest published release is supported for security fixes.

## Reporting a Vulnerability

Please do not open a public issue for suspected security vulnerabilities.

Use GitHub's private vulnerability reporting for this repository. Include:

- the affected version
- a short description of the issue
- steps to reproduce, where practical
- whether the issue affects the workbook, VBA update flow, release assets, or repository automation

## Security Notes

Electronic Logbook is a macro-enabled Excel workbook. Users should only download releases from this repository's GitHub Releases page and should not run modified workbooks from untrusted sources.

The workbook update system downloads release metadata/assets from this repository and retains a legacy VBA-update fallback for older supported workbooks. Maintainers must protect the `main` branch, protect release tags, and verify that release workbooks do not contain private tokens or personal data before publishing.

Published releases include `SHA256SUMS.txt`, `release-manifest.json`, and `release-validation.json` so downloaded assets and release validation can be checked against the release metadata.
