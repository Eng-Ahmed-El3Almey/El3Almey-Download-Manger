# El3Almey Download Manager

A Windows desktop application built with WPF for managing video, audio, and playlist downloads from supported online platforms.

## Overview

El3Almey Download Manager provides a desktop interface for adding media links, configuring available download options, monitoring download progress, and reviewing completed downloads.

## Features

- Download supported video and audio content.
- Select from the quality and format options available for a media item.
- Add and manage playlist downloads.
- Pause, resume, and manage downloads where supported by the source and current application state.
- View active downloads and review completed items in History.
- Configure download destinations and application preferences.
- Use temporary working files while downloads are in progress.
- Support for available subtitles and embedded chapters, depending on the media source and application settings.

> Available formats, audio languages, subtitles, chapters, and playlist behavior depend on the source platform and the current project version.

## Technology Stack

- **Language:** C#
- **Framework:** .NET 8
- **Desktop UI:** Windows Presentation Foundation (WPF)
- **Media downloading:** `yt-dlp`

## Requirements

- Windows 10 or Windows 11
- .NET 8 SDK to build the project from source
- `yt-dlp` and any runtime dependencies required by the project's current configuration

Check the project configuration for the expected locations and versions of external tools.

## Getting Started

### 1. Clone the repository

```bash
git clone https://github.com/Eng-Ahmed-El3Almey/El3Almey-Download-Manger.git
cd El3Almey-Download-Manger
```

### 2. Locate the project file

Find `El3AlmeyDownloadManager.csproj`. If it is in a subfolder, change into that folder before running the commands below, or specify the relative path to the project file.

### 3. Restore and build

```bash
dotnet restore
dotnet build
```

### 4. Run the application

From the directory containing the project file, run:

```bash
dotnet run --project El3AlmeyDownloadManager.csproj
```

## External Dependency: yt-dlp

The application uses `yt-dlp` to extract media information and perform supported downloads. Install and configure it according to the version and runtime paths expected by this project.

Official project: [yt-dlp on GitHub](https://github.com/yt-dlp/yt-dlp)

## Development Notes

- `bin/` contains build output and `obj/` contains generated intermediate files.
- These folders generally should not be committed to source control; use `.gitignore` to exclude generated build artifacts.
- Test changes against a known-working build before merging them into `main`.
- Keep external tool paths and runtime configuration consistent with the target machine.

## Repository

[Eng-Ahmed-El3Almey / El3Almey-Download-Manger](https://github.com/Eng-Ahmed-El3Almey/El3Almey-Download-Manger)

## License

No license is specified in this README. Add a `LICENSE` file if you want to define the terms under which others may use, modify, or distribute the project.
