# GitHub repository setup

These settings are not stored in Git. Apply them after authenticating GitHub CLI with an account that can administer the repository.

## About panel

Recommended description:

> Safe, multilingual WPF GUI for building lighter Windows 11 ISOs with configurable debloating and fail-fast DISM automation.

Recommended topics:

`tiny11`, `windows-11`, `windows`, `debloat`, `windows-iso`, `wpf`, `dotnet`, `powershell`, `dism`, `windows-customization`

Apply the text metadata:

```powershell
./scripts/Configure-GitHub.ps1
```

## Social preview

Leave the current repository image unchanged until a new approved example is available. When the final asset is ready, upload it from **Settings → General → Social preview** and keep the source under `docs/`.

## Recommended repository settings

- Keep Issues and Discussions enabled.
- Enable private vulnerability reporting under **Settings → Security**.
- Protect `main`: require a pull request and the `build-and-test` status check, and block force pushes.
- Enable automatic deletion of merged branches.
- Pin the latest release announcement and one compatibility-report discussion.
- Add the release link as the repository website after the first automated release is published.
