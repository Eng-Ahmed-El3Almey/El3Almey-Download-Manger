# 🚀 El3Almey Download Manager

<p align="center">
  <strong>A modern Windows desktop app for downloading and organizing video, audio, and playlists.</strong>
  <br />
  Built with ❤️ using C#, .NET 8, and WPF.
</p>

<p align="center">
  <img alt="Platform" src="https://img.shields.io/badge/Platform-Windows-0078D6?logo=windows&logoColor=white" />
  <img alt=".NET" src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white" />
  <img alt="UI" src="https://img.shields.io/badge/UI-WPF-5C2D91" />
  <img alt="Downloader" src="https://img.shields.io/badge/Engine-yt--dlp-222222" />
</p>

---

## ✨ About the Project

**El3Almey Download Manager** is a Windows application designed to make media downloads easier to manage from one clean desktop workspace.

Add a video or playlist link, review the available formats, choose your preferred download options, and keep track of your downloads from start to finish.

The project is built around a practical goal: **a straightforward downloading experience with useful controls, organized files, and a focused interface.**

## 🎯 Highlights

| Feature | What it does |
| --- | --- |
| 🎬 **Video downloads** | Download supported videos and choose from the formats and qualities provided by the source. |
| 🎵 **Audio downloads** | Extract and download available audio formats. |
| 📚 **Playlist support** | Add playlists and manage their download items. |
| ⏯️ **Download controls** | Pause, resume, and manage downloads when supported by the current task and source. |
| 📊 **Progress tracking** | Follow active download tasks from the Downloads screen. |
| 🗂️ **Download history** | Keep completed items organized in History. |
| ⚙️ **Flexible settings** | Configure available download preferences and destination options. |
| 🧩 **Media metadata** | Use available information such as formats, subtitles, and chapters when provided by the source and supported by the current version. |
| 🌐 **YouTube browser** | Browse YouTube inside the app, navigate between pages, search, and send supported video or playlist links to the download workflow. |

> ℹ️ Available formats, resolutions, subtitles, chapters, playlist behavior, and download controls depend on the source platform and the current application version.

## 🧭 App Workflow

1. **Open the YouTube Browser** or go to the add-download screen.
2. **Enter or select a supported link.**
3. **Review the available media information and options.**
4. **Start the download** and monitor it in Downloads.
5. **Find completed items** in History and open their saved location.

## 🛠️ Built With

- **C#** — application logic
- **.NET 8** — application framework
- **WPF (Windows Presentation Foundation)** — desktop interface
- **yt-dlp** — media extraction and download engine
- **WebView2** — embedded browser experience

## 💻 Requirements

- Windows 10 or Windows 11
- .NET 8 SDK (for building from source)
- The external tools and runtime dependencies configured by the project
- Internet connection for supported online sources

## 🚀 Getting Started

### 1. Clone the repository

```bash
git clone https://github.com/Eng-Ahmed-El3Almey/El3Almey-Download-Manger.git
cd El3Almey-Download-Manger
```

### 2. Find the project file

Locate **`El3AlmeyDownloadManager.csproj`**. If it is inside a subfolder, open that folder in your terminal or pass the project file's relative path to the .NET commands.

### 3. Restore dependencies

```bash
dotnet restore
```

### 4. Build the application

```bash
dotnet build
```

### 5. Run

```bash
dotnet run --project El3AlmeyDownloadManager.csproj
```

## 📦 yt-dlp

The application uses [yt-dlp](https://github.com/yt-dlp/yt-dlp) to retrieve media information and process supported downloads.

Make sure the executable and any required runtime dependencies are available at the locations expected by the current project configuration. Do not assume that installing yt-dlp alone is sufficient if the project is configured to use additional tools.

- 🔗 [yt-dlp project](https://github.com/yt-dlp/yt-dlp)
- 📖 [yt-dlp documentation](https://github.com/yt-dlp/yt-dlp#readme)

## 🧑‍💻 Development

A few notes for contributors and anyone exploring the source:

- Keep generated build folders such as `bin/` and `obj/` out of commits.
- Preserve the existing application structure and visual language when making UI changes.
- Test changes locally before merging them into `main`.
- Verify external executable paths and runtime requirements on the target Windows machine.
- Keep user-facing behavior consistent across the supported languages.

## 🗺️ Project Direction

The project is continuously evolving. Features and behavior may change as the application is improved, tested, and refined.

If you find a bug or have an idea, feel free to open an issue with clear steps to reproduce it and any relevant screenshots or logs.

## 🔗 Repository

[**Eng-Ahmed-El3Almey / El3Almey-Download-Manger**](https://github.com/Eng-Ahmed-El3Almey/El3Almey-Download-Manger)

## 📄 License

No license file is currently specified in this repository. Until a license is added, the project should not be assumed to be available for reuse, redistribution, or modification under an open-source license.

---

<p align="center">
  <strong>Made with ❤️ for a smoother downloading experience.</strong>
  <br />
  <sub>El3Almey Download Manager</sub>
</p>
