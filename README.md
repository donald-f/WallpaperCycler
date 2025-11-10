# WallpaperCycler

A lightweight Windows 11 tray app that cycles wallpapers from a selected folder (recursive), keeps track of which images you've seen, allows next/previous navigation, delete to Recycle Bin, reset seen state, and supports a configurable background fill color. Built with C# (.NET 8) WinForms and SQLite.

## Features
- Tray-based UI (NotifyIcon) with menu: Select folder, Previous, Next, Delete current, Reset seen photos, Settings, Exit
- Scans folders recursively for images (`.jpg`, `.jpeg`, `.png`, `.bmp`) and tracks them in a local SQLite DB
- `seenOrdinal` is 0-based; `-1` means unseen
- Randomized cycle through unseen photos (shuffled queue via SQL `ORDER BY RANDOM()`), previous/next navigation based on `seenOrdinal`
- Composes a wallpaper image sized to the virtual screen (spanning monitors) and saves a temp file, then sets it as wallpaper
- Deletes files to Recycle Bin (with confirmation)
- Optional `FileSystemWatcher` to keep DB sync light-weight and a rescanning policy every N advances
- Autostart with Windows (optional setting)

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
dotnet publish -r win-x64 -c Release -p:PublishSingleFile=true --self-contained false -o publish
```