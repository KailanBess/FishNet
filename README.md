# FishNet

Initial native C# / .NET 8 / WinUI 3 local media player for the Aquarium app family. This repository was empty when inspected; this is a new standalone foundation, not an update to an existing implementation.

## Build and run

Requires Windows 10 build 19041 or newer (x64), .NET 8 SDK, and Visual Studio 2022 17.8+ Windows application development tooling for WinUI C#, including Windows SDK 10.0.26100. NuGet access is needed for restore.

```powershell
dotnet restore .\src\FishNet\FishNet.csproj
dotnet build .\src\FishNet\FishNet.csproj -c Debug -p:Platform=x64
dotnet run --project .\src\FishNet\FishNet.csproj -c Debug -p:Platform=x64
```

You can also open the project in Visual Studio and run using x64. To publish a self-contained unpackaged folder:

```powershell
dotnet publish .\src\FishNet\FishNet.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -o .\publish
.\publish\FishNet.exe
```

Keep the entire publish folder together. No MSIX installer or Aquarium package is produced yet.

## Implemented source behavior

- Open a user-selected local audio/video file through a window-owned Windows file picker.
- Native MediaPlayerElement transport controls for play/pause, seeking, and volume.
- Now-playing filename, cancel-safe file selection, playback error reporting, close-media action, and disposal on window close.
- Dark aquatic interface with keyboard-accessible native controls.

Playback depends on the container and codecs available in Windows; appearing in the picker does not guarantee decoding. No codecs or sample media are bundled. There is no remote streaming, downloading, folder scanning, persistent library, or fake playback data.

Playback follows [Microsoft's WinUI media player guidance](https://learn.microsoft.com/windows/apps/design/controls/media-playback).

## Aquarium integration

FishNet remains standalone. Aquarium's Media catalog slot can refer to FishNet, but installation and launching must stay disabled until its build is verified and an approved package manifest, integrity checks, install location, and launch adapter exist. No VPN or network-management functionality is included.

Next useful increments: verify playback on Windows, then introduce a local library service, playlists, and explicit Aquarium packaging. MainWindow currently owns the player lifecycle; extract a playback service when adding those features.

## Validation status

XML/project structure has been checked statically. The authoring machine lacks a discoverable .NET SDK, so compilation, execution, audio/video playback, and visual layout have not been verified.

On a configured Windows machine: build, open a known-good MP4 and MP3, test pause/seek/volume, cancel a second picker while playing, switch files, close media, reopen, test a damaged file, resize, navigate by keyboard, and close the window during playback. Confirm sound stops and the process exits. Codec failures should produce an error rather than an apparent success.
