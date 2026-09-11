## Release Checklist

- [ ] `versions.properties` has the intended `sheet_version`, `app_version`, and
      monotonic `android_version_code`
- [ ] Compatibility `version.txt` matches `sheet_version`
- [ ] `README.md` changelog has a matching version/date entry
- [ ] `README.pdf` was regenerated from `README.md`
- [ ] `modBoot.bas`, `modLogbook.bas`, and `ThisWorkbook.cls` were imported into the workbook
- [ ] `modUpdate.bas` was not embedded in the workbook
- [ ] `tools/Test-VbaSourceQuality.ps1` passed
- [ ] `tools/Test-WorkbookVbaParity.ps1` passed
- [ ] `updater/Test-CompatibilityMatrix.ps1` passed locally, or compatibility impact is not applicable
- [ ] Master workbook `GitHubBranch` is set correctly for this PR/release
- [ ] Master workbook `LogbookVersion` matches `versions.properties` `sheet_version`
- [ ] Master workbook `GitHubToken` is empty
- [ ] `tools/Test-WorkbookPublicReadiness.ps1` passed locally before release
- [ ] Working copy branch was switched without changing its `LogbookVersion`
- [ ] Updated copy was smoke tested manually in Excel
- [ ] Binary workbook change is expected
- [ ] Release tag will be created only after this PR is merged to `main`

## Notes
