# Release checklist

## Before tagging

- [ ] Move the intended entries in `CHANGELOG.md` from **Unreleased** to the release version and date.
- [ ] Confirm the application version shown in the UI.
- [ ] Run the Release build and all automated tests.
- [ ] Parse the generated PowerShell and inspect changes to privileged operations.
- [ ] Test WIM and ESD sources in disposable virtual machines when the release changes build logic.
- [ ] Test at least one stock host and one trimmed host when cleanup or host compatibility changes.
- [ ] Confirm every bundled translation loads and retains required format placeholders.
- [ ] Confirm screenshots and logs contain no product keys or sensitive personal data.

## Package verification

Create the same portable package used by CI:

```powershell
./scripts/Publish-Release.ps1 -Version 1.2.0
```

- [ ] Extract the ZIP into a clean directory and launch `tiny11-ui.exe`.
- [ ] Confirm all required resource files are present.
- [ ] Verify the ZIP against its `.sha256` file.
- [ ] Scan the package with Microsoft Defender and, when appropriate, a multi-engine scanner.

## Publish

1. Merge the release commit into `main`.
2. Create and push an annotated semantic-version tag, such as `v1.2.0`.
3. The Release workflow builds, tests, packages, computes SHA-256, and creates the GitHub release.
4. Review the generated release notes and add known issues plus upgrade guidance.
5. Test the download link and checksum from a signed-out browser session.

## After publishing

- [ ] Post the prepared announcement from `docs/LAUNCH_KIT.md`.
- [ ] Pin a discussion asking for structured compatibility reports.
- [ ] Triage early reports and add confirmed combinations or known issues to the release notes.
- [ ] Thank reporters and first-time contributors visibly.
