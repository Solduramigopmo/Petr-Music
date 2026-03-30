# Petrichor for Windows

Russian version: [README.ru.md](README.ru.md)

This directory is the Windows branch of Petrichor.The WPF shell, the playback wiring, the SQLite data layer, the Windows-specific integrations, and the packaging scripts all live here.

If you plan to publish the Windows version as its own GitHub project, this folder is the part that matters.

## What is already here

The current Windows build is a real local music player, not just a visual prototype. At this stage it already covers the main day-to-day flows:

- local library import from folders
- metadata extraction on import
- SQLite persistence for tracks, playlists, folders, playback state, EQ state, and app settings
- queue restore between launches
- repeat and shuffle behavior
- playlists and folders as first-class sections in the UI
- playback controls with real backend wiring
- equalizer presets, custom preset save/delete, preset import/export
- DSP baseline with loudness and replay gain controls
- lyrics loading with local, embedded, and online fallback
- Last.fm authentication and scrobbling baseline
- system tray controls
- Windows SMTC integration for media keys and OS media overlay
- file association registration for supported audio formats
- portable build and installer script baseline

This means the Windows version is already beyond a migration mockup. It has enough core functionality to be run, tested, and iterated as a standalone app.

## What is still not finished

This branch is usable, but it is not fully closed out.

The main unfinished area is distribution hardening. Portable packaging and an installer script already exist, but the full installer/update flow is still treated as in progress rather than finished. There are also parity gaps compared to the original macOS version in deeper product polish: some richer metadata views, queue-centric UX, broader audio parity, release QA depth, and a few higher-end convenience features are still on the backlog.

So the honest status is this: the Windows app works, but it is still an actively developed port rather than a frozen final release.

## Project structure

Below is the real filesystem structure of the `Windows` folder in this repository. I am intentionally showing the meaningful part of the tree rather than every generated `bin` and `obj` directory.

```text
Windows/
|- Configuration/
|  |- Secrets.props
|  \- Secrets.props.template
|- dist/
|- installer/
|  \- Petrichor.iss
|- Petrichor.App/
|  |- App.xaml
|  |- App.xaml.cs
|  |- MainWindow.xaml
|  |- MainWindow.xaml.cs
|  |- LyricsService.cs
|  |- LastFmScrobbleService.cs
|  |- WindowsSmtcService.cs
|  \- Petrichor.App.csproj
|- Petrichor.Core/
|  |- Abstractions/
|  |- Domain/
|  |- Migration/
|  |- Playback/
|  \- Petrichor.Core.csproj
|- Petrichor.Data/
|  |- Persistence/
|  |- Repositories/
|  |- Services/
|  \- Petrichor.Data.csproj
|- Petrichor.Media/
|  |- Playback/
|  \- Petrichor.Media.csproj
|- Petrichor.Platform.Windows/
|  |- Shell/
|  |- Storage/
|  \- Petrichor.Platform.Windows.csproj
|- Scripts/
|  \- Build-WindowsRelease.ps1
|- windows-migration/
|  |- BACKLOG.md
|  |- MACOS-DEPENDENCIES.md
|  |- PARITY-MATRIX.md
|  |- PLAN.md
|  \- VERIFICATION-CHECKLIST.md
|- Petrichor.Windows.slnx
|- Petrichor.WindowsLegacy.slnx
\- README.md
```

If you open the `Windows` folder in VS Code or on disk, this is the structure you should actually see.

Logically, the workspace is split into a few focused projects:

- `Petrichor.App`  
  The WPF desktop application. This is the shell, the main window, section navigation, tray integration, SMTC hookup, lyrics flow, Last.fm flow, dialogs, and the overall application wiring.

- `Petrichor.Core`  
  Shared domain models and contracts. Queue state, playback state records, track references, playlist references, and other core abstractions live here.

- `Petrichor.Data`  
  SQLite persistence, schema, migrations, repositories, library import, metadata extraction, and query logic. This is where the Windows app turns audio files and library state into a stored local model.

- `Petrichor.Media`  
  Playback backends, orchestration, EQ and DSP chain, and playback service composition. This is the media engine side of the Windows build.

- `Petrichor.Platform.Windows`  
  Windows-only helpers such as app storage paths, settings storage, and shell registration logic.

- `Configuration`  
  Build-time configuration files, including the template for Last.fm secrets.

- `Scripts`  
  Release scripts, including the publish flow for portable builds and the installer handoff.

- `installer`  
  Inno Setup configuration for the Windows installer.

- `windows-migration`  
  Working documentation for backlog, parity tracking, and verification notes for the port.

For day-to-day work, the active solution entry point is `Petrichor.Windows.slnx`.

## Current architecture in plain terms

The Windows app is built as a local-first desktop player.

The UI is WPF. The data layer stores the library and user state in SQLite. Metadata is extracted from imported files and saved locally so the app can restore playlists, queue state, playback semantics, EQ state, and other user-facing behavior between launches.

Playback is wired through the Windows media layer in `Petrichor.Media`. The current branch uses a WPF/NAudio-based approach rather than a web wrapper or an embedded browser player. EQ and DSP are applied inside the Windows playback stack, not faked at the UI level.

The app also includes platform-specific behavior that a Windows user would reasonably expect from a native build: tray integration, media keys, SMTC metadata, file opening through shell associations, and local storage under the user profile.

## Technology stack

The Windows branch currently uses:

- .NET 10
- WPF for the desktop UI
- SQLite via `Microsoft.Data.Sqlite`
- `TagLibSharp` for metadata extraction
- `NAudio` for the stronger playback backend path
- Windows media/session APIs for SMTC integration
- Windows Registry integration for file associations
- Inno Setup for installer packaging

Target framework details:

- `Petrichor.App`: `net10.0-windows10.0.19041.0`
- `Petrichor.Media`: `net10.0-windows`
- `Petrichor.Data`: `net10.0`

In practice, that means this branch is meant for modern Windows and is developed as a native desktop app, not a cross-platform compromise layer.

## Supported library and shell formats

The current Windows build recognizes these audio extensions in library import and shell registration:

- `.mp3`
- `.flac`
- `.wav`
- `.m4a`
- `.aac`
- `.ogg`
- `.wma`
- `.aiff`
- `.alac`

Imported metadata currently covers the fields the app already uses heavily in the UI and library logic:

- title
- artist
- album
- genre
- year
- duration

That is enough for sorting, filtering, playlist flows, playback display, lyrics lookup, and scrobbling. Broader metadata fidelity is still a separate backlog item.

## What the user-facing Windows build can do today

From a user perspective, the current branch already supports the flows below.

### Library

- add local folders to the library
- scan supported audio files
- store tracks in SQLite
- search, sort, and filter library views
- auto-refresh through the library watcher baseline
- restore library-backed playback after restart

### Playback

- play, pause, seek, next, previous
- volume control
- repeat and shuffle
- queue restore
- open a file directly into playback
- switch playback source from library, folders, or playlists

### Playlists and folders

- create playlists
- rename and delete playlists
- add the current track to a playlist
- save the current queue as a playlist baseline
- reorder and remove playlist tracks
- import and export `.m3u` / `.m3u8`
- open folders and playlists as playback sources
- use smart playlist logic baseline

### Audio

- built-in EQ presets
- custom preset save/delete
- preset import/export
- loudness control baseline
- replay gain baseline

### Platform integration

- tray icon with playback actions
- hide-to-tray behavior
- Windows media keys via SMTC
- system media overlay metadata and timeline
- supported file types can be registered for "Open with Petrichor"

### Online services

- Last.fm login flow
- now playing updates
- scrobbling with retry behavior
- lyrics from external `.lrc`
- lyrics from external `.srt`
- lyrics from embedded tags
- lyrics fallback to LRCLIB when enabled

## Requirements

For development:

- Windows 10 version 2004 or newer is the sensible baseline because the app targets Windows 10 build `19041`
- .NET 10 SDK
- optional: Inno Setup 6 if you want to build the installer, not just a portable package

For running a packaged build:

- a Windows machine compatible with the published runtime
- no separate .NET installation is required for self-contained release builds produced by the release script

## Running the app locally

Restore and build:

```powershell
dotnet restore .\Windows\Petrichor.Windows.slnx
dotnet build .\Windows\Petrichor.Windows.slnx
```

Run the application project directly:

```powershell
dotnet run --project .\Windows\Petrichor.App\Petrichor.App.csproj
```

If you are actively working on the app, this is the simplest loop.

## Last.fm setup

The Windows version: Last.fm credentials are not typed by the end user into a random text field at runtime. They are supplied at build time, and the user later authorizes their own Last.fm account through the normal login flow.

To enable Last.fm in your own build:

1. Copy `Windows/Configuration/Secrets.props.template` to `Windows/Configuration/Secrets.props`.
2. Fill in `LastFmApiKey` and `LastFmSharedSecret`.
3. Rebuild the app.

Example:

```powershell
Copy-Item .\Windows\Configuration\Secrets.props.template .\Windows\Configuration\Secrets.props
```

If `Secrets.props` is missing or empty, the app still builds, but Last.fm connection remains unavailable.

Important detail: the credentials are build-time application credentials. They identify the app to Last.fm. The actual scrobbling still happens under the end user's own Last.fm account after the user completes authorization in the browser.

The authenticated session is then stored locally in encrypted form for the current Windows user.

## Lyrics behavior

The current lyrics chain is intentionally simple and predictable:

1. external `.lrc` file next to the track
2. external `.srt` file next to the track
3. embedded lyrics in file tags
4. online fetch from LRCLIB if online lookup is allowed

This is already good enough for a practical local-player workflow and matches the direction of the original app better than a fake placeholder panel would.

## Where Windows data is stored

User data is stored outside the installation directory, under:

```text
%LocalAppData%\Petrichor
```

This separation matters because it lets you replace or reinstall the app without automatically wiping the user's library state and preferences.

The Windows build stores its local state here:

- `petrichor.db`
- `ArtworkCache\`
- `playback-state.json`
- `settings.json`
- `equalizer-profile.json`
- `dsp-profile.json`
- `equalizer-presets.json`
- `lastfm-session.bin`

That is the practical center of the user's local state on Windows.

## Release build

The repository already includes a release script for producing a self-contained Windows package.

Portable build:

```powershell
.\Windows\Scripts\Build-WindowsRelease.ps1 -Configuration Release -Runtime win-x64 -Version 0.1.0
```

Portable build plus installer:

```powershell
.\Windows\Scripts\Build-WindowsRelease.ps1 -Configuration Release -Runtime win-x64 -Version 0.1.0 -BuildInstaller
```

What the script does:

- publishes the app as self-contained
- enables single-file publish
- enables single-file compression
- enables ReadyToRun
- writes the published app into `Windows/dist/release`
- creates a portable zip archive
- optionally invokes Inno Setup to build an installer

Installer output uses the Inno Setup definition in `Windows/installer/Petrichor.iss`.

The installer currently targets:

```text
%LocalAppData%\Programs\Petrichor
```

## File associations

The Windows build can register itself as an "Open with Petrichor" target for supported audio formats. This registration is currently done under the current user rather than as a machine-wide installer-only feature.

That gives the app a practical Windows-native entry point: users can open supported files directly in Petrichor from Explorer once registration has been applied.

## Notes for contributors

This branch already has a lot of moving parts. The biggest practical rule for working in it is simple: do not break the flows that already work.

At minimum, any non-trivial change should be checked against:

- playback: play, pause, next, previous, seek, volume, repeat, shuffle
- library: import, search, sort, filter, open track from library
- playlists: create, rename, delete, add current track, remove track, reorder, restore playback from playlist
- audio: EQ presets, custom EQ state, loudness, replay gain
- platform: tray actions, file open, SMTC, startup and shutdown behavior

## Honest summary

The Windows branch is already a serious local desktop port. It has a native shell, a real persistence layer, real playback wiring, platform integration, library management, playlist workflows, EQ and DSP baseline, lyrics, and Last.fm support.

What it still needs is not "a Windows app from scratch", but the less glamorous finishing work: installer hardening, deeper parity polish, more QA coverage, and the last stretch of product refinement that turns a good port into a very stable release.
