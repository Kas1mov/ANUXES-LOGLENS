<div align="center">

# ANUXES AFTERTRACE

### A lightweight file inspection and change tracking tool for Windows

**Inspect. Compare. Understand.**

![Version](https://img.shields.io/badge/version-0.1.1-9B75FF?style=for-the-badge)
![Platform](https://img.shields.io/badge/platform-Windows-263A63?style=for-the-badge&logo=windows)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet)
![UI](https://img.shields.io/badge/UI-WPF-17151F?style=for-the-badge)

**Publisher:** ANUXES DIVISION · **Developer:** Kas1mov

<img width="1231" height="809" alt="Screenshot_3621" src="https://github.com/user-attachments/assets/ee3ccbc7-9ae9-41a0-9f36-45f13c4ab1d2" />


---

<!-- Add your application screenshot here -->

</div>

## Overview

**ANUXES AFTERTRACE** is a Windows desktop application for inspecting files and examining their properties, integrity, and changes.

It provides file analysis tools in a dark interface with purple accents, combining file information, SHA-256 verification, digital signature inspection, and file type identification.

Built with **C#**, **WPF**, and **.NET 8**.

## Features

| Feature | Description |
|---|---|
| **File inspection** | Select a file and inspect its available properties. |
| **SHA-256 analysis** | Calculate the file's SHA-256 hash for integrity verification. |
| **Digital signature** | Inspect available digital signature information. |
| **File identification** | Examine file headers and identify file types. |
| **Metadata inspection** | View available file metadata and properties. |
| **Change Tracking** | Planned snapshot-based comparison of folder contents and file changes. |
| **Custom interface** | Frameless window controls, purple glow interaction states, and a dark navy interface. |

## Getting started

### Requirements

- Windows 10 or Windows 11.
- .NET 8 SDK.
- Visual Studio 2022 with the **.NET desktop development** workload (recommended).

### Download from Releases

1. Open the repository's **Releases** section.
2. Download the latest AFTERTRACE release archive.
3. Extract the archive.
4. Launch `ANUXES.AFTERTRACE.exe` (if included in the release).

### Run from Visual Studio

1. Clone the repository:

   ```powershell
   git clone https://github.com/Kas1mov/ANUXES-AFTERTRACE.git
   cd ANUXES-AFTERTRACE
   ```

2. Open the solution or project in Visual Studio 2022.
3. Allow NuGet restore to finish.
4. Select the desired configuration and press **F5**.

### Build a standalone release

Publish the application from PowerShell:

```powershell
dotnet publish ANUXES.AFTERTRACE.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true
```

The standalone build includes the .NET runtime and does not require the end user to install it separately.

## How it works

AFTERTRACE is designed around file inspection and integrity information.

1. Select a file for inspection.
2. Let the application analyze its available properties.
3. Review the calculated hash, file identification, signature information, and metadata.
4. Use the available information to compare or verify the file independently.

**Important:** A matching SHA-256 hash indicates identical file contents. It does not, by itself, prove that a file is safe.

## Project structure

```text
ANUXES-AFTERTRACE/
├── Assets/                 # Application icons and visual resources
├── Models/                 # Data models
├── Services/               # File inspection services
├── Views/                  # Application views
├── App.xaml                # WPF application resources
├── App.xaml.cs             # Application entry point
├── MainWindow.xaml         # Main interface
├── MainWindow.xaml.cs      # Application logic
├── ANUXES.AFTERTRACE.csproj
├── CHANGELOG.md            # Version history
└── README.md
```

*The structure above is a suggested layout and may differ from the actual repository.*

## Notes and limitations

- File inspection provides information about a file; it is not a full antivirus or malware detection system.
- SHA-256 hashes can be used to compare file contents, but a hash alone does not establish whether a file is trustworthy.
- Digital signature information depends on whether the inspected file contains a supported signature.
- File metadata may be incomplete or unavailable depending on the file format.
- Change Tracking is a planned feature and may not be available in the current release.

## Version history

See [CHANGELOG.md](CHANGELOG.md) for release notes.

## Credits

| Role | Name |
|---|---|
| Publisher | **ANUXES DIVISION** |
| Developer | **Kas1mov** |

## License

No open-source license has been granted for this repository. Unless a license is added, **all rights are reserved by the developer**. Public visibility does not grant permission to redistribute, modify, or reuse the source code. Please contact the developer for permission.

---

<div align="center">

**ANUXES AFTERTRACE** · File Inspection & Change Tracking · `v0.1.1`

*Published by ANUXES DIVISION · Developed by Kas1mov*

</div>
