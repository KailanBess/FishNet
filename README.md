# FishNet

Standalone native C# / .NET 8 / WinUI 3 media player and app-link library in the Aquarium family. Reference-based charcoal layout with Home/Music/Videos navigation, an app-link library, customization, and a persistent player. There is no built-in browser and no FishNet updater; Aquarium owns update management.

## Build and run

Requires Windows 10 build 19041+ x64 and the .NET 8 SDK. WinUI and Windows build tools restore from NuGet. Use the dotnet SDK commands below; the project uses the .NET SDK WinUI compiler path. The root global.json selects .NET 8 even if newer SDKs are installed.

```powershell
dotnet build .\src\FishNet\FishNet.csproj -c Debug -p:Platform=x64
& .\src\FishNet\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\FishNet.exe
```

Or open the csproj in Visual Studio, x64. Publish a self-contained unpackaged folder:

```powershell
dotnet publish .\src\FishNet\FishNet.csproj -c Release -p:Platform=x64 -o .\publish
.\publish\FishNet.exe
```

Keep all published files together. No Aquarium package or installer is produced yet.

## Use

- **Player**: open a local audio/video file. Native controls provide playback, seeking, and volume. Codec support depends on Windows; picker extensions do not guarantee decoding. Playback errors are displayed and media resources are disposed on close.
- **Link app**: select an existing desktop `.exe`, give it a name, and choose a collection. Open externally starts that executable; FishNet does not embed or modify it.
- **Add browser**: explicitly select your installed browser `.exe`. There is no bundled browser, WebView, or automatic browser installation.
- **Add website**: save an HTTPS address and choose one of your added browsers. Open externally passes that address as one argument to the selected browser. No browser command-line customization or arbitrary shell commands are stored.
- **Collections**: create named collections, filter links, rename or move a link, and remove links without uninstalling the target app. A browser cannot be removed while website links refer to it.
- **Customize**: persist Mint / Blue / Coral accents, Ocean / Midnight / Slate backgrounds, and compact app-library spacing.

App links are launcher bookmarks, not imported app data, installed packages, synchronized Spotify libraries, or accounts. Microsoft Store app identities, arbitrary URI schemes, command-line flags, automatic app discovery, web content scraping, and persistent media-file indexing are not implemented. A browser link must point to its actual executable, not a shortcut. Missing executables produce an error. Only add software you intend to run.

## Data and architecture

`LinkedLibrary` owns app-link records and collections. `LibraryUi` owns their native controls. `Preferences` owns appearance. `MainWindow` owns local playback and its resource lifecycle. Data is stored under `%LOCALAPPDATA%\FishNet` in `linked-library.json` and `appearance.json`; it is local and never committed to the repository. Atomic temporary-file replacement commits in-memory library state only after successful saves. Unreadable or unsupported files are preserved with edits disabled. One running editor instance is assumed.

Aquarium's release checker is the only Aquarium-family update interface. Signed artifacts, installation, version comparison, launch adapters, and automatic self-update remain future work. Linked third-party apps may still have their vendor's own updater; FishNet does not disable it.

References: [Microsoft WinUI media player](https://learn.microsoft.com/windows/apps/design/controls/media-playback).

## Verification

Local source XML was checked, but the authoring machine has no discoverable .NET SDK. A Windows GitHub Actions build workflow is included; check its actual result. Playback and visual interaction still require Windows validation.

Smoke checks: play MP3/MP4, pause/seek/volume, cancel file selection, switch/close files, play a damaged file, close during playback. Add an app and browser, create a collection, save an HTTPS website with that browser, restart, filter, edit/move, open externally, and remove. Confirm browser dependency protection and missing-executable errors. Test all customization choices and restart persistence. Back up then corrupt each data file with the app closed; confirm preservation and disabled edits, then restore. Verify no embedded browser or app-specific updater appears.


## Reference layout preview

This review branch adds a persistent Recent media catalog, Windows-provided artwork thumbnails when available, music/video filters, native persistent playback, and the matching glossy outlined FishNet icon. Empty libraries remain empty; no sample albums are bundled. Browser links remain external. A real queue and folder import are follow-up work. Existing media/app-link and appearance files remain separate. The PNG master and multi-size ICO are under src/FishNet/Assets.


## Services
Services & apps includes built-in Spotify, YouTube and SoundCloud presets. Add a browser first, then add a preset to a collection or use Add custom service for your own HTTPS address. Each saved service keeps its chosen browser and can be renamed, moved or removed. These are external service launchers, not account connections or embedded streaming; provider sign-in and playback happen in your browser. Local playback remains inside FishNet.

## Local converter and MP3 playlists

Use **Converter** to pick a local file, select MP3 (192 kbps), M4A, WAV, FLAC, MP4 or WMV, and choose a destination. The built-in Windows media transcoder performs conversion locally. Audio output can extract a video's audio track. Video uses a 720p preset. Codec/container support depends on Windows and the input; this does not promise every format. Sources are preserved, and conversion uses a temporary file before replacing the selected destination. Progress and cancellation are supported. A save dialog may leave a new empty destination placeholder after cancellation or failure; temporary encoded data is removed when possible. Existing destination contents are replaced only after successful conversion. Service URLs and protected streams are not converter inputs.

Use **Playlists** to create named MP3 playlists, add multiple files, rename, reorder/remove tracks, delete the playlist, and play it in order. Missing files are marked and skipped; a decode error stops playback with an error. Opening a separate file or closing media exits playlist playback. Playlists store references under `%LOCALAPPDATA%\FishNet\playlists.json`; deleting a playlist never deletes the MP3 files. Invalid saved data is preserved with editing disabled. Shuffle, repeat, M3U import/export and crossfade are future features.

Checks:
```
dotnet run --project tests/FishNet.MediaTests -c Release
dotnet run --project tests/FishNet.ConversionTests -c Release
```
The Windows-only conversion check generates its own tone/video, exercises the real converter, decodes audio outputs to verify duration, and checks cancellation, invalid input and source overwrite prevention. No user media is used.

Windows API reference: https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/transcode-media-files

## Media tools expansion
- Converter → Batch convert files processes selected files sequentially into a chosen folder. Names are made unique, successful copies remain after cancellation, failures are listed per file, and source files are kept. The selected format applies to the batch.
- Home/Music/Videos → Import media folder recursively indexes audio/video references (up to 10,000); inaccessible folders and directory links are skipped. Path duplicates are ignored. The grid shows up to 250 matches; search narrows larger libraries.
- MP3 cards → Tags & cover art edits title/artist/album/year/track and PNG/JPEG front-cover art after preview. Each save retains an exact original backup beside the MP3. Locked files fail without replacing the original.
- Missing media cards → Relink file repairs the library reference and matching playlist references.
- Playlists includes shuffle, repeat and local M3U/M3U8 import/export. Remote URLs in imported playlists are rejected; missing local MP3 references are retained for review.
- Playback tools loads local SRT/VTT subtitles, disables captions, changes playback speed and advances the playlist. Windows caption controls also expose embedded tracks. Native codec/subtitle compatibility varies by file.

TagLibSharp 2.3.0 is dynamically linked for metadata; license and source information ship in Licenses/. See THIRD-PARTY.txt. These tools run locally and do not download or convert service streams.

## Aquarium workspace mode
Hosting-compatible builds declare workspace protocol 1 and mark only their main window when started by Aquarium with AQUARIUM_HOSTED=1. Aquarium displays that window inside its shell. Home keeps the session running; Resume restores it; Close app uses normal app shutdown. Standalone launch remains unchanged. File pickers can appear as separate owned dialogs. No built-in browser was added.

## Playback resume and release packaging

Local media opened directly resumes from its saved position. Playback positions are saved every 15 seconds and requested on media switch, Close media, and app exit; the last periodic checkpoint is the recovery point if Windows terminates the process before a final write completes. Tracks opened from a playlist start at the beginning. Positions within five seconds of the end also restart. Existing media libraries gain an optional position field; relinking preserves it.

The Windows release package workflow creates FishNet-windows-x64.zip and SHA256SUMS.txt. A manual run uploads package artifacts; a v-prefixed tag creates a draft GitHub release for review. Publish after Windows smoke checks so Aquarium can discover the release. Drafts are excluded from its catalog. Packages remain unsigned development builds.
