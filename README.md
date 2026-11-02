[README.md](https://github.com/user-attachments/files/34290366/README.md)

<div align="center">

# ANUXES LOGLENS

### A lightweight desktop log analysis tool for Windows

**Read. Filter. Inspect. Understand.**

![Version](https://img.shields.io/badge/version-0.2.1-9B75FF?style=for-the-badge)
![Platform](https://img.shields.io/badge/platform-Windows-263A63?style=for-the-badge&logo=windows)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet)
![UI](https://img.shields.io/badge/UI-WPF-17151F?style=for-the-badge)

**Publisher:** ANUXES DIVISION  ·  **Developer:** Kas1mov

---

<img width="1373" height="843" alt="Screenshot_3619" src="https://github.com/user-attachments/assets/833b7f09-44eb-4446-a994-bf3ac024575b" />



</div>

## Overview

**ANUXES LOGLENS** is a Windows desktop application for opening, filtering, searching, and inspecting text-based application logs. It combines a log table, severity filters, an entry inspector, error-report export, and a live file monitor in a dark interface with purple accents.

It is built with **C#**, **WPF**, and **.NET 8**.

## Features

| Feature | Description |
|---|---|
| **Log viewer** | Open `.log`, `.txt`, and `.jsonl` files. |
| **Severity filters** | Filter TRACE, DEBUG, INFO, WARN, ERROR, FATAL, and OTHER / UNPARSED entries. |
| **Severity overview** | Keep track of ERROR, WARN, and FATAL totals. |
| **Search** | Case-insensitive search, highlighted matches, and previous / next result navigation. |
| **Entry Inspector** | Inspect the timestamp, severity, message, and raw text of the selected entry. |
| **Error report export** | Export ERROR / FATAL entries and related exception or stack-trace lines to TXT. |
| **Live Monitor** | Watch an opened file for appended lines using filesystem notifications and a polling fallback. |
| **Custom interface** | Frameless window controls, purple glow interaction states, custom checkboxes, and dark navy scrollbars. |

## Getting started

### Requirements

- Windows 10 or Windows 11.
- .NET 8 SDK.
- Visual Studio 2022 with the **.NET desktop development** workload (recommended).

### Download from "Releases"

1. Tap to v0.2.1
2. Download ANUXES-LOGLENS v0.2.1.zip
3. Unzip
4. Open `ANUXES.LOGLENS.exe`

### Run from Visual Studio

1. Clone the repository:

   ```powershell
   git clone https://github.com/Kas1mov/ANUXES-LOGLENS.git
   cd ANUXES-LOGLENS
   ```

2. Open `ANUXES.LOGLENS.sln` in Visual Studio 2022.
3. Allow NuGet restore to finish.
4. Select the desired configuration and press **F5**.

### Build a standalone release

Run `Build-EXE.bat` from the project directory, or publish from PowerShell:

```powershell
dotnet publish ANUXES.LOGLENS.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true
```

The published application is placed in:

```text
bin\Release\net8.0-windows\win-x64\publish\ANUXES.LOGLENS.exe
```

The standalone build includes the .NET runtime and does not require the end user to install it separately.

## Try the sample logs

Sample files are available in the `samples/` directory:

- `clean.log.txt` — routine INFO and DEBUG messages.
- `errors.log.txt` — sample warnings, errors, a fatal event, and exception details.
- `live-test.log` — a file for testing Live Monitor.

### Test Live Monitor

1. Open `samples/live-test.log` in LOGLENS.
2. Enable **Live Mode**.
3. Open PowerShell in the repository directory and run:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File ".\samples\Append-LiveTest.ps1"
   ```

4. New completed lines should appear while the script appends them. Press `Ctrl+C` in PowerShell to stop the test.

### Test error-report export

1. Open `samples/errors.log.txt`.
2. Click **Export**.
3. Choose where to save the TXT report and inspect the exported events and related details.

## Project structure

```text
ANUXES-LOGLENS/
├── assets/                 # Interface screenshots
├── samples/                # Sample logs and Live Monitor test script
├── App.xaml                # WPF application resources
├── MainWindow.xaml         # Main interface
├── MainWindow.xaml.cs      # Application logic
├── ANUXES.LOGLENS.csproj   # Project configuration
├── ANUXES.LOGLENS.sln      # Visual Studio solution
├── Build-EXE.bat           # Release publishing script
├── CHANGELOG.md            # Version history
└── README.md
```

## Notes and known limitations

- Severity detection uses common level labels such as `TRACE`, `DEBUG`, `INFO`, `WARN`, `WARNING`, `ERROR`, and `FATAL`. It is not a framework-specific log schema parser.
- Severity overview totals are calculated for the loaded file, independently of the currently visible filters.
- Error-report export uses heuristics to associate exception and stack-trace details with ERROR / FATAL events. It checks nearby following entries; review reports before sharing them.
- Live Monitor is intended for files that are appended to. Applications that replace or rewrite an entire file on save may behave differently.
- JSONL files are currently displayed line by line; structured JSON field inspection is not included in this release.

## Version history

See [CHANGELOG.md](CHANGELOG.md) for release notes.

## Credits

| Role | Name |
|---|---|
| Publisher | **ANUXES DIVISION (me)** |
| Developer | **Kas1mov** |

## License

No open-source license has been granted for this repository. Unless a license is added, **all rights are reserved by the developer**. Public visibility does not grant permission to redistribute, modify, or reuse the source code. Please contact the developer for permission.

---

<div align="center">

**ANUXES LOGLENS** · Log Analysis System · `v0.2.1`

*Published by ANUXES DIVISION (me) · Developed by Kas1mov*

</div>
