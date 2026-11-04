# FishNet

Standalone native C# / .NET 8 / WinUI 3 media player and app-link library in the Aquarium family. Aquatic dark UI with Player, App library, and Customize tabs. There is no built-in browser and no FishNet updater; Aquarium owns update management.

## Build and run

Requires Windows 10 build 19041+ x64, .NET 8 SDK, Visual Studio 2022 17.8+ WinUI C# / Windows application development tooling, and Windows SDK 10.0.26100. NuGet access is required. Run these commands from Developer PowerShell for Visual Studio so its MSBuild and Windows packaging tools are available. The root global.json selects .NET 8 even if newer SDKs are installed.

```powershell
msbuild .\src\FishNet\FishNet.csproj /restore /p:Configuration=Debug /p:Platform=x64
& .\src\FishNet\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\FishNet.exe
```

Or open the csproj in Visual Studio, x64. Publish a self-contained unpackaged folder:

```powershell
msbuild .\src\FishNet\FishNet.csproj /restore /t:Publish /p:Configuration=Release /p:Platform=x64 /p:PublishDir=..\..\publish\
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

