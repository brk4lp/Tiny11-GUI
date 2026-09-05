# Contributing to Tiny11 GUI

Thanks for helping improve Tiny11 GUI. Bug reports from different Windows releases, ISO languages, editions, and trimmed-down host systems are especially valuable.

Participation in project spaces is governed by [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md). General setup questions and compatibility results belong in [GitHub Discussions](https://github.com/brk4lp/Tiny11-GUI/discussions); see [SUPPORT.md](SUPPORT.md) for the information that makes a report actionable.

## Before opening an issue

- Search existing issues first.
- Reproduce with the latest release or `main` when practical.
- Remove product keys, usernames, machine names, and personal paths from logs.
- For security problems, follow [SECURITY.md](SECURITY.md) instead of filing a public issue.

A useful build report includes the app version or commit, host Windows version, source ISO version/language/edition, selected preset and options, the failed step, relevant sanitized log lines, and whether the host is a stock or trimmed Windows installation.

## Development setup

Requirements:

- Windows 10 or 11;
- .NET 8 SDK;
- Visual Studio 2022 with the .NET desktop workload, or the `dotnet` CLI;
- Windows ADK only for end-to-end ISO creation tests.

Build and test from the repository root:

```powershell
dotnet restore tiny11-ui.sln --configfile NuGet.config
dotnet build tiny11-ui.sln --configuration Release --no-restore
dotnet test tests/Tiny11UI.Tests/Tiny11UI.Tests.csproj --configuration Release --no-build --no-restore
```

Unit tests must not mount images, require administrator privileges, or modify the host. End-to-end tests that use DISM or `oscdimg` should be clearly identified and run only on disposable test images or virtual machines.

## Pull requests

- Keep each pull request focused and explain the user-visible behavior.
- Add or update tests for script-generation and safety changes.
- Preserve targeted, ownership-based cleanup. Never terminate unrelated PowerShell processes or globally discard mounted images.
- Check every critical native command's exit code and keep failure cleanup inside the current build's scope.
- Do not include Microsoft binaries, Windows images, product keys, generated ISOs, or other copyrighted/proprietary artifacts.
- Confirm that `dotnet build` and `dotnet test` pass before submitting.

## Translations

Strings live in `Resources/Strings.<culture>.txt` as `Key=Value` pairs. English (`en-US`) is the fallback language. Keep the same keys and composite-format placeholders (for example `{0}`) as the English source, save files as UTF-8, and avoid translating product names or command-line literals.
