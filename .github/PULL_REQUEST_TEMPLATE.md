## What changed?

Describe the user-visible behavior and why the change is needed.

## Validation

- [ ] `dotnet build tiny11-ui.sln --configuration Release --no-restore`
- [ ] `dotnet test tests/Tiny11UI.Tests/Tiny11UI.Tests.csproj --configuration Release --no-build --no-restore`
- [ ] Generated PowerShell remains ownership-scoped and fail-fast
- [ ] New or changed UI text is localized or safely falls back to English
- [ ] Logs, screenshots, and fixtures contain no product keys or personal information

## Compatibility

List the tested host Windows version and, when applicable, source ISO build, language, edition, and WIM/ESD format.
