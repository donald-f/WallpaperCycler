# WallpaperCycler

A lightweight Windows 11 tray app that cycles wallpapers from a selected folder (recursive), keeps track of which images you've seen, allows next/previous navigation, delete to Recycle Bin, reset seen state, and supports a configurable background fill color and automatic timed cycling. Built with C# (.NET 8) WinForms and SQLite.

## Features
- Tray-based UI (NotifyIcon) with menu: Select folder, Previous, Next, Delete current, Reset seen photos, Settings, Exit
- Scans folders recursively for images (`.jpg`, `.jpeg`, `.png`, `.bmp`) and tracks them in a local SQLite DB
- `seenOrdinal` is 0-based; `-1` means unseen
- Randomized cycle through unseen photos (SQL `ORDER BY RANDOM()`), previous/next navigation based on `seenOrdinal`
- Composes a wallpaper image sized to the virtual screen (spanning monitors) and saves a temp file, then sets it as wallpaper
- Deletes files to Recycle Bin (with confirmation)
- Autostart support (creates/removes shortcut in user Startup folder)
- Timed cycling option: 10 / 20 / 30 / 60 minutes
- On restart, the app silently resumes the last shown wallpaper and continues where you left off
- Lightweight FileSystemWatcher plus occasional DB cleanup
- Simple append-only log with 1 MB cap (keeps newest content)

## Getting started
Requirements:
- Windows 11
- .NET 8 SDK (https://dotnet.microsoft.com)

Build & run:
1. Clone the repo (or copy files)
2. `dotnet build` from the project directory
3. `dotnet run --project WallpaperCycler.csproj`

To create a single-file publish (recommended for distribution):
```
dotnet publish -r win-x64 -c Release -p:PublishSingleFile=true -p:SelfContained=false -o publish
```