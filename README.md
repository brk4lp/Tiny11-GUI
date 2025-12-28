# Tiny11 GUI

A modern graphical user interface (GUI) wrapper for the original [tiny11builder](https://github.com/ntdevlabs/tiny11builder) PowerShell scripts created by [ntdevlabs](https://github.com/ntdevlabs). 

**Credit:** All core functionality is provided by the original tiny11builder scripts. This project only adds a modern WPF-based UI to make the tool more accessible.

Built with WPF (Windows Presentation Foundation) and .NET 8.0.

<img width="661" height="912" alt="resim" src="https://github.com/user-attachments/assets/6ee45226-d3c3-4266-9289-bb0bba54458a" />

## Features

- **Modern UI**: Clean WPF-based interface with fixed 680x920 window size
- **Multi-Language Support**: 
  - 🇬🇧 English (default)
  - 🇹🇷 Turkish (Türkçe)
  - Language can be changed at runtime
- **Preset Configurations**: Quick setup with predefined removal/bypass options
- **Advanced Customization**:
  - Selective app removal (Edge, OneDrive, Cortana, Chat, Teams, Xbox)
  - System optimization (Telemetry, Windows Update, Defender, Sponsored Apps, Reserved Storage, BitLocker)
  - System requirements bypass (TPM, CPU, RAM, Secure Boot)
  - Installation process bypass (MS Account, Network, Privacy questions)
- **Real-time Logging**: Live output with auto-scroll during build process
- **Automatic ISO Management**: Handles mounting/unmounting of ISO and WIM images
- **Windows Edition Selection**: Choose from all available editions in your ISO
- **Unique Build Directories**: Uses timestamp-based folders to avoid conflicts
- **Error Handling**: Filtered output to show relevant progress information

## Setup Instructions

1. Clone the repository:
   ```bash
   git clone https://github.com/yourusername/tiny11-ui.git
   ```
2. Open `tiny11-ui.sln` in Visual Studio 2022 or later
3. Restore NuGet packages
4. Build the solution (`Ctrl+Shift+B`)

## Usage

1. **Run as Administrator** - Right-click the executable and select "Run as administrator"
2. Select your Windows 11 ISO file using the "Browse" button
3. Choose the Windows edition from the dropdown
4. Set the output path for your tiny11 ISO
5. Configure options:
   - **Presets**: Choose from Minimal, Balanced, Gaming, or Enterprise configurations
   - **Apps to Remove**: Select which built-in apps to remove (Edge, OneDrive, Cortana, Teams, Xbox, etc.)
   - **System Features**: Disable telemetry, Windows Update, Defender, sponsored apps, etc.
   - **System Requirements Bypass**: Skip TPM, CPU, RAM, and Secure Boot checks
   - **Installation Process Bypass**: Skip Microsoft Account, network connection, and privacy questions
6. Click **"Start Build Tiny11"** to begin the process
7. Monitor progress in the log panel
   

## Requirements

- Windows 10/11
- .NET 8.0 Runtime
- Administrator privileges (required for WIM mounting operations)
- Windows ADK (for oscdimg.exe - ISO creation)


## Technical Details

- **Framework**: .NET 8.0 Windows (WPF)
- **Architecture**: MVVM pattern
- **PowerShell Integration**: Dynamically generates and executes PowerShell scripts
- **WIM Operations**: Uses DISM for mounting/unmounting Windows images
- **ISO Creation**: Uses oscdimg.exe from Windows ADK

## Contributing

Contributions are welcome! Please submit a pull request or open an issue for any enhancements or bug fixes.

## License

This project is licensed under the MIT License. See the LICENSE file for more details.

## Acknowledgments

- [ntdevlabs](https://github.com/ntdevlabs) for the original tiny11builder scripts
- Microsoft for WPF and .NET
