# Tiny11 GUI

A WPF front-end for [tiny11builder](https://github.com/ntdevlabs/tiny11builder), the PowerShell scripts that strip down a Windows 11 ISO. All the actual work is done by the tiny11builder scripts; this project just adds a UI on top.

<img width="661" height="912" alt="Tiny11 GUI screenshot" src="https://github.com/user-attachments/assets/6ee45226-d3c3-4266-9289-bb0bba54458a" />

## Features

- Build from a Windows 11 ISO with edition selection
- Removable apps: Edge, OneDrive, Cortana, Chat, Teams, Xbox
- Disable telemetry, Windows Update, Defender, sponsored apps, reserved storage, BitLocker
- Bypass TPM/CPU/RAM/Secure Boot checks and MS account/network/privacy setup steps
- Built-in presets: Minimal, Balanced, Gaming, Enterprise
- Live log output during the build
- English and Turkish UI

## Requirements

- Windows 10/11
- .NET 8.0 Runtime
- Administrator privileges (required for WIM mount/unmount)
- Windows ADK — needed for `oscdimg.exe` (ISO creation)

## Building from Source

```bash
git clone https://github.com/brk4lp/Tiny11-GUI.git
```

Open `tiny11-ui.sln` in Visual Studio and build (`Ctrl+Shift+B`).

## Usage

1. Run the app **as administrator**
2. Select your Windows 11 ISO and choose the edition
3. Set the output ISO path
4. Pick a preset or configure removal/bypass options manually
5. Click "Start Build Tiny11" and watch progress in the log panel

## How It Works

Based on your selections, the app generates and runs a PowerShell script. ISO/WIM mounting is handled via DISM, and the final ISO is built with `oscdimg.exe` from the Windows ADK.

## License

No license is specified. All rights reserved by default — this matches the upstream [tiny11builder](https://github.com/ntdevlabs/tiny11builder) project, which also has no license.

## Credits

- [ntdevlabs](https://github.com/ntdevlabs) — original tiny11builder scripts
