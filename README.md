# VideoPress — Windows Video Compression Studio

A native Windows desktop application built with C# WPF (.NET 8) for batch video
compression using real FFmpeg under the hood.

---

## Features

- **All 4 codecs** — H.264 (libx264), H.265/HEVC (libx265), VP9 (libvpx-vp9), AV1 (libaom-av1)
- **3 quality modes** — CRF slider, target bitrate (kbps), target file size (MB, 2-pass)
- **Resolution control** — original, preset (4K/1080p/720p/480p/360p), custom W×H
- **Audio settings** — AAC / MP3 / Opus, bitrate slider, or strip audio
- **Batch compression** — drop multiple files, process sequentially with live progress
- **Custom FFmpeg flags** — freeform extra flags appended to the command
- **Live command preview** — see the exact FFmpeg command update in real time
- **Auto FFmpeg download** — downloads and installs FFmpeg on first launch (≈80 MB)
- **Output folder picker** — defaults to source folder or choose any destination
- **Filename suffix** — optional `_compressed` suffix to avoid overwriting originals
- **FFmpeg log viewer** — toggle overlay to see raw FFmpeg output

---

## Requirements

| Requirement | Version |
|---|---|
| Windows | 10 or 11 (64-bit) |
| .NET SDK | 8.0+ |
| Internet | Required on first launch (FFmpeg download) |

### Install .NET 8 SDK

Download from: https://dotnet.microsoft.com/download/dotnet/8.0  
Choose: **.NET 8.0 SDK — Windows x64**

---

## Getting Started

### Option A — Run from source (development)

```cmd
git clone <repo>
cd VideoPress
run-dev.bat
```

### Option B — Build a standalone .exe

```cmd
build.bat
```

This produces a **single self-contained .exe** at:
```
.\publish\VideoPress\VideoPress.exe
```

No .NET installation required on the target machine — everything is bundled.

---

## First Launch

On first launch, VideoPress detects that FFmpeg is not installed and shows a banner:

> ⚠ FFmpeg is not installed. Click "Download FFmpeg automatically"

Clicking it downloads `ffmpeg-master-latest-win64-gpl.zip` from GitHub (~80 MB),
extracts `ffmpeg.exe` and `ffprobe.exe`, and stores them in:

```
%LOCALAPPDATA%\VideoPress\ffmpeg.exe
%LOCALAPPDATA%\VideoPress\ffprobe.exe
```

This only happens once. Subsequent launches detect the existing installation.

---

## Project Structure

```
VideoPress/
├── Models/
│   ├── VideoFile.cs           # Observable file model
│   └── CompressionSettings.cs # All settings + FFmpeg arg builder
├── ViewModels/
│   └── MainViewModel.cs       # MVVM logic (CommunityToolkit)
├── Views/
│   ├── MainWindow.xaml        # WPF UI layout
│   └── MainWindow.xaml.cs     # Code-behind (drag-drop, browse)
├── Services/
│   └── FFmpegService.cs       # Download, probe, run FFmpeg
├── Converters/
│   └── Converters.cs          # IValueConverter implementations
├── Themes/
│   └── Dark.xaml              # Full dark theme resource dictionary
├── App.xaml / App.xaml.cs
├── VideoPress.csproj
├── build.bat                  # Release build → single .exe
└── run-dev.bat                # Debug run
```

---

## Custom FFmpeg Flags

The flags textarea accepts any valid FFmpeg arguments, appended after the
codec/quality settings. Examples:

```
-preset fast
-tune zerolatency
-movflags +faststart
-profile:v baseline -level 3.0
-x265-params crf=28:pools=4
-vf "eq=brightness=0.05:saturation=1.2"
```

The live command preview at the bottom of the settings panel updates in real time.

---

## Building an Installer (optional)

To create an NSIS or WiX installer from the published output:

1. Build with `build.bat`
2. Point your installer tool at `.\publish\VideoPress\`
3. The only file needed is `VideoPress.exe` (all dependencies are bundled)

---

## License

MIT — free to use, modify, and distribute.

---

## Building the Installer

### Prerequisites
1. .NET 8 SDK
2. FFmpeg binaries fetched (`fetch-ffmpeg.bat`)
3. [Inno Setup 6](https://jrsoftware.org/isdl.php) installed

### One command
```cmd
make-installer.bat
```

This will:
1. Compile and publish `VideoPress.exe` (self-contained, FFmpeg embedded)
2. Run Inno Setup to package it into `dist\VideoPress-Setup-1.0.1.exe`

### What the installer does
- Installs to `C:\Program Files\VideoPress` (user-configurable)
- Creates **Start Menu** shortcut under `VideoPress`
- Offers optional **Desktop** shortcut (unchecked by default)
- Registers in **Add/Remove Programs** with icon, version, publisher
- Requires **Windows 10 or later** (checked at install time)
- Installs for current user by default (no UAC prompt unless choosing Program Files)
- Includes a clean **uninstaller** that removes all installed files
- Preserves `%APPDATA%\VideoPress\` (presets, settings) on uninstall

### Customising the installer
Edit `installer\VideoPress.iss` to change:
- `AppVersion` — version number shown in Add/Remove Programs
- `AppPublisher` / `AppURL` — your name and website
- `AppId` — generate a new GUID for your fork
- `DefaultDirName` — default install location
- `OutputBaseFilename` — output `.exe` filename

### Silent install (for enterprise / scripting)
```cmd
VideoPress-Setup-1.0.1.exe /SILENT /NORESTART
VideoPress-Setup-1.0.1.exe /VERYSILENT /NORESTART /DIR="C:\Tools\VideoPress"
```
