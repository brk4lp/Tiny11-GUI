# Tiny11 GUI

This project, `tiny11-ui`, is a graphical user interface (GUI) wrapper for the original [tiny11builder](https://github.com/ntdevlabs/tiny11builder) PowerShell scripts created by [ntdevlabs](https://github.com/ntdevlabs). 

**Credit:** All core functionality is provided by the original tiny11builder scripts. This project only adds a modern WPF-based UI to make the tool more accessible.

The UI is built using WPF (Windows Presentation Foundation) and follows the MVVM (Model-View-ViewModel) design pattern.

## Original Project

- **Original Repository:** [https://github.com/ntdevlabs/tiny11builder](https://github.com/ntdevlabs/tiny11builder)
- **Creator:** ntdevlabs
- **License:** Check the original repository for licensing information

This GUI wrapper is designed to provide a simple and elegant user interface for the `tiny11maker.ps1` and `tiny11Coremaker.ps1` scripts, which automate the creation of a lightweight Windows 11 image.

## Project Structure

The project is organized into the following directories and files:

- **src/**: Contains the source code for the application.
  - **Models/**: Contains data models used in the application.
    - `AppSettings.cs`: Defines a class that holds configuration settings for the application, such as file paths and user preferences.
  - **Services/**: Contains service classes that handle business logic.
    - `PowerShellService.cs`: Provides methods to execute PowerShell scripts and handle their output.
  - **ViewModels/**: Contains view model classes that serve as the data context for the views.
    - `MainViewModel.cs`: Exports a class that contains properties for binding UI elements and commands for user interactions.
  - **Views/**: Contains XAML files that define the user interface layout.
    - `MainWindow.xaml`: Defines the layout for the main window of the application.
  - `App.xaml`: The application definition file that specifies resources, styles, and the startup window.
  - `App.xaml.cs`: Contains the application logic, including the OnStartup method that initializes the main window and sets the data context.

## Setup Instructions

1. Clone the repository to your local machine.
2. Open the solution in your preferred IDE.
3. Restore any necessary NuGet packages.
4. Build the solution to ensure all dependencies are resolved.

## Usage

To run the application, execute the following steps:

1. Launch the application from your IDE or by running the compiled executable.
2. Use the UI to select the Windows 11 ISO file and configure the desired settings.
3. Click the "Create Image" button to start the process.

## Features

- **User-friendly Interface**: Modern WPF-based UI for easy configuration
- **Multi-Language Support**: Fully localized interface supporting:
  - 🇬🇧 English (default)
  - 🇹🇷 Turkish (Türkçe)
  - Dynamic language switching without application restart
- **Advanced Customization Options**:
  - Preset configurations (Minimal, Balanced, Gaming, Enterprise)
  - Selective app removal (Edge, OneDrive, Cortana, Teams, Xbox, etc.)
  - System optimization toggles (Telemetry, Windows Update, Defender, etc.)
  - System requirements bypass (TPM, Secure Boot, CPU, RAM)
  - Installation process customization (MS Account, Network, Privacy)
- **Real-time Progress Tracking**: Live output from PowerShell script execution
- **Automatic ISO Management**: Mount and unmount ISO files automatically
- **Windows Edition Selection**: Choose from available Windows 11 editions in your ISO
- **Custom Output Path**: Select where to save the final tiny11 ISO file
- **Administrator Privilege Detection**: Automatic detection and guidance for admin rights

## Contributing

Contributions are welcome! Please submit a pull request or open an issue for any enhancements or bug fixes.

## License

This project is licensed under the MIT License. See the LICENSE file for more details.