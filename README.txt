El3Almey Download Manager (WPF, .NET 8)

Build / run:   dotnet run        (first build restores YoutubeExplode + CommunityToolkit.Mvvm from NuGet)

FFmpeg (needed for MP3 and for video above 720p):
  - install it (winget install Gyan.FFmpeg) or put ffmpeg.exe next to the .exe
  - or set its path in Settings > Conversion
  Without FFmpeg: video is saved as the best ready-made MP4 (up to 720p), audio as the original M4A/WebM.

User data (settings, downloads list/history): %AppData%\El3Almey

Troubleshooting Failed downloads:
- The short exception is displayed under the failed item in Downloads. Full diagnostic details are written to %APPDATA%\El3Almey\logs\download-errors.log.
- MP3 conversion and MKV output require FFmpeg. Some YouTube videos may also be blocked or unavailable to the current client.
- M3U playlist files are written to the selected playlist output folder when the queue is submitted; entries refer to the planned output filenames. Failed items may therefore need to be removed from the M3U manually.


Audio and subtitle tracks
-------------------------
The Add Video screen now loads audio-language and subtitle options from yt-dlp metadata. The app downloads yt-dlp.exe on first use to %LOCALAPPDATA%\El3AlmeyDownloadManager\Tools. Audio language selections are passed to the downloader; subtitle selections save the chosen subtitle alongside the media file. The Default audio option delegates selection to YouTube/yt-dlp's default format preference. FFmpeg is still prepared by the app as before.


Version 1.0.4 stability diagnostic update
- Added crash logging for WPF dispatcher, AppDomain, and unobserved task exceptions.
- Log path: %APPDATA%\El3Almey\crash.log
- This diagnostic build still requires a Windows .NET build and runtime verification.


Version 1.0.5 stability patch
- Fixed WPF cross-thread binding crash by keeping async download-engine continuations and progress callbacks on the UI synchronization context.
- Added bitrate-based fallback estimates when YouTube stream size metadata is missing.
- Populated per-video estimated sizes for playlist downloads. Estimates are approximate and marked with ~ in the UI.


Version 1.0.6 targeted stability update
- Marshal yt-dlp progress callbacks onto the WPF dispatcher to avoid cross-thread binding crashes.
- Freeze generated WPF brushes so they can safely be shared across threads.
- Show numbered playlist filenames in playlist detail rows when Number Playlist Items is enabled.
- Calculate playlist total estimated size from selected entries and selected video quality.
- Skip completed playlist entries already recorded in this destination when their downloaded file still exists.
- Detect same-name media files for single-video downloads and offer rename / skip / cancel.
- Keep the Windows Videos and Music known-folder paths as the default destinations.


Version 1.0.7 queue-safety patch
- Resume and retry now pass through the shared queue scheduler instead of starting directly, so they respect MaxConcurrent.
- Added a second concurrency guard inside Run to prevent direct callers from exceeding the configured cap.
- Cancellation token sources are disposed after each task has unwound.
- Download-history load/save failures are written to %APPDATA%\El3Almey\download-manager.log instead of being silently ignored.
- Original uploaded source archive was kept unchanged; this is a separate working copy.

Verification status for this package
- Static project/XAML XML parsing: checked.
- Source-level review of queue scheduling and persistence logging: checked.
- Windows WPF build and interactive download tests: NOT run in this environment because the .NET SDK / Windows WPF runtime is unavailable here.
- This patch is a targeted stability update, not a claim that every item in the supplied full specification has been implemented or functionally verified. In particular, YouTube metadata HTTP 403 behavior, Windows notifications, end-to-end playlist updates, and real YouTube download tests still require Windows runtime verification and/or further implementation.


Version 1.0.8 targeted responsiveness patch
- DownloadsViewModel.Refresh now preserves existing row containers when membership/order is unchanged, avoiding unnecessary full list rebuilds on download status changes.
- DownloadEngine routes audio and merged-video progress updates through the WPF Dispatcher helper to keep bound state thread-safe.
- Existing UI and navigation structure are preserved.

Verification note: this environment does not have the .NET SDK/Windows WPF runtime, so a Windows build and live download test are still required.


v1.0.9 Performance fixes:
- Debounced download-history persistence; JSON serialization and disk writes run off the UI thread.
- Atomic history file replacement to reduce risk of partial writes.
- FFmpeg archive extraction runs on a worker thread.
- Downloads list uses a recycling VirtualizingStackPanel while retaining the existing row design.

Validation note: source/XAML review only in this environment; Windows .NET build and live download tests must be run on a Windows machine.


Version 1.1.0 — Start Download UI freeze fix
- Fixed the blocking `StreamReader.EndOfStream` check in `YtDlpService.ReadProgressAsync`. With redirected process output, that property can block synchronously while waiting for yt-dlp output; because the async reader starts on the WPF UI thread, this can freeze the entire window. The reader now awaits `ReadLineAsync` before checking for end-of-stream and uses `ConfigureAwait(false)` for the pipe-reading continuation.
- Searched the C# source for other `EndOfStream`, `.Wait()`, `.Result`, `GetAwaiter().GetResult()`, and synchronous `Dispatcher.Invoke` patterns. No other occurrences were found in this project.
- Validation: source-level checks only. This environment does not have the .NET SDK / Windows WPF runtime, so a Windows build and live download test still must be run on the target machine; this package is not represented as runtime-tested.


Version 1.1.1 reliability update: adds yt-dlp network retry/backoff options, automatic retries for common transient failures, and playlist-level Retry Download behavior. See CHANGELOG_v1.1.1.txt.


Version 1.1.2 — reduced first-download preparation delay by warming up yt-dlp/FFmpeg in the background, preparing tools concurrently, reducing extraction work, migrating default output folders to Windows Videos/Music libraries, and placing Retry directly on failed playlist videos. Retry inside playlist details affects only the clicked video. See CHANGELOG_v1.1.2.txt.

Version 1.1.3 changes: live progress parsing, popular de-duplicated subtitle languages, video-vs-playlist choice for links containing both IDs, separate audio folder in Settings, and icon-only Retry on failed items. See CHANGELOG_v1.1.3.txt.


Version 1.2.1 changes: themed playlist selection checkbox, right-click row menu toggle, and playlist Download Again with unique filenames. See CHANGELOG_v1.2.1.txt.
