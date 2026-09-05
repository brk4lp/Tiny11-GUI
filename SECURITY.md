# Security Policy

Tiny11 GUI mounts and modifies offline Windows images with administrator privileges. Treat downloaded releases, source ISOs, custom unattended files, and generated images as security-sensitive artifacts.

## Supported versions

Security fixes are applied to the latest published release and the `main` branch. Older releases may not receive fixes; first reproduce an issue with the latest version when it is safe to do so.

## Reporting a vulnerability

Please do not open a public issue for a suspected vulnerability. Use GitHub's **Security → Report a vulnerability** private reporting flow for this repository. If private vulnerability reporting is unavailable, open a minimal issue asking the maintainer for a private contact channel without including exploit details, sensitive logs, or personal paths.

Include:

- the affected version or commit;
- the Windows version and source image version;
- the minimum steps needed to reproduce the issue;
- the expected and observed impact;
- sanitized logs or a proof of concept, when appropriate.

Reports will be acknowledged as maintainer availability permits. Please allow time for validation and a coordinated fix before public disclosure.

## Scope and safety

Relevant reports include unsafe command or path handling, unintended modification of resources not owned by the current build, privilege-boundary problems, tampering with generated output, and exposure of sensitive data in logs.

Do not test against systems or data you do not own or have permission to use. This project does not distribute Windows installation media; users must supply legitimately obtained media and remain responsible for Microsoft licensing and deployment requirements.
