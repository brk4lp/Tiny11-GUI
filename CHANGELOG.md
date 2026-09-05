# Changelog

Notable changes to Tiny11 GUI are documented here. The project follows semantic versioning for published releases.

## Unreleased

## 1.2.0 — 2026-09-06

### Added

- Russian, Japanese, German, French, Spanish, and Simplified Chinese UI resources with English fallback.
- Ownership-based build state and recovery for interrupted Tiny11 GUI runs.
- Automated tests for generated PowerShell safety, path escaping, ESD index handling, atomic output, native exit-code checks, and PowerShell syntax.
- GitHub Actions workflows for continuous integration and tagged, checksummed Windows release packages.
- Contribution, security, support, issue, pull-request, and release documentation.

### Changed

- Critical DISM, registry, compression, and ISO-generation operations now fail the build when their native exit code indicates an error.
- Generated scripts now use structured `try/catch/finally` cleanup limited to resources owned by the current build.
- ESD exports reset the destination WIM image index to `1` before servicing it.
- Final ISOs are built to a unique temporary path and atomically published only after validation.

### Removed

- Automatic termination of unrelated PowerShell processes.
- Global mounted-image discard and global DISM cleanup during normal build startup.

## 1.1.1

- Added custom `autounattend.xml` selection.
- Improved cleanup of stale Tiny11 GUI working directories.
- Redesigned the log panel layout.

## 1.1.0

- Added configurable deep-cleanup options and improved DISM compatibility.

## 1.0.0

- Initial public release.
