# FishNet

FishNet **0.5.0** is a native .NET 10 LTS / WinUI 3 Windows media player, local library and app-link collection in the Aquarium family. This release adds saved HTTPS internet radio stations and explicit stream playback, retaining moved-folder repair, audio-output selection, playback diagnostics and protection against simultaneous profile editors.

The broader development scope comes from the [shared ecosystem specification](https://chatgpt.com/share/6b08a8dd-e4a8-83e9-ab79-0f2360b34a3e), whose full text is tracked in Aquarium. This release implements one part of that roadmap; Tailscale and private networking remain excluded.

## Download

Download `FishNet-windows-x64.zip` from [Releases](../../releases), extract the complete folder and run `FishNet.exe`. Keep all runtime files together. Aquarium can install and update the approved public package. FishNet has no separate updater or built-in browser.

`BUILD-INFO.json` reports whether the package was publisher-signed. Signing remains unavailable until the maintainer supplies genuine credentials. SHA256SUMS verifies package integrity against release metadata; it is not independent publisher authentication.

## Media

- **Home, Music and Videos:** open local files or import folders; browse artwork and search the local catalog. Import supports up to 10,000 records. Supported extensions do not guarantee that Windows can decode a file.
- **Player:** native playback, seek and volume controls. Continue watching records video positions; Play queue remembers tracks, order and the selected item. Resume playback is explicit.
- **Playlists:** saved MP3 playlists, ordering, smart rules and M3U exchange.
- **Playback tools:** SRT/VTT subtitles, playback speed, sleep timer and output selection. Choose Windows default or an available device. The preferred device is remembered; an unavailable or disconnected output falls back to Windows default. Refresh the list after connecting devices.
- **Diagnostics:** playback failures report the Windows media error and HRESULT, with guidance distinguishing unavailable decoders from damaged files. FishNet does not automatically download codecs or alter system playback settings.
- **Converter:** compatible Windows audio/video formats, batch queue, cancellation and resumable job records. Available encoders depend on Windows. Completed output is separate from source media.
- **Tags:** edit MP3 tags and cover art while retaining the original backup.
- **Podcasts:** explicit user-added HTTPS RSS feeds, downloaded episodes and subscriptions. Service availability remains the provider's responsibility.
- **Internet radio:** save up to 200 named direct HTTPS audio streams, search stations, play and remove saved entries. Adding a station never connects; Play explicitly uses the Windows media engine. Pause/volume and Close media retain their normal behavior. Station lists load without network access and damaged records are preserved with edits disabled. Codecs, connectivity and regional access depend on Windows and the provider. Radio streams remain separate from local media, queues and video resume records. Public stream addresses are saved in `radio-stations.json`; do not put private tokens in them. No station directory, subscription access, recording or automatic reconnection is provided.
- **Watch media folder:** checks selected folders while FishNet is open and adds new files. Stopping a watch does not remove existing media or files.

## Repair a moved folder

Close playback and wait for imports, conversion and podcast downloads to finish. Choose **Repair moved media folder**, enter the previous absolute folder path and choose its replacement. FishNet previews missing files that exist at the same relative paths in the new folder. Existing files are kept; duplicate destinations require individual review.

After confirmation, the repair updates matching catalog, playlist and queue references, saved video positions and the selected watched-folder root. Files are never moved or renamed. Previous record contents and the reviewed path map are saved under `%LOCALAPPDATA%\FishNet\repair-backups`. Writes are staged together and already published records are rolled back if another destination fails. Keep a separate backup for power-loss recovery; review recovery records before manually replacing any files. Smart playlist folder rules outside the selected root may need their own edit.

Aquarium **Recovery → Encrypted app records** can back up and restore FishNet catalogs, playlists, queue, app links, preferences, podcast subscriptions and video positions. It does not include media files or downloaded podcast audio. Close FishNet before restoring.

## Services and apps

Add an existing desktop executable, a browser executable or an HTTPS website in a named collection. Links open externally; FishNet does not import third-party accounts or streaming libraries, embed apps, install browsers or disable vendor updaters. Browsers cannot be removed while website links reference them. Missing executables are reported. Only add software you intend to run.

Microsoft Store app identities, arbitrary URI schemes, saved command-line flags and automatic app discovery are not supported. Provider links do not grant a license, subscription or DRM access.

## Appearance and storage

Customize colors, background, border and compact library spacing. Aquarium can optionally review and apply a common Ocean/Midnight/Slate and Mint/Blue/Coral palette to closed, initialized profiles. Local records live under `%LOCALAPPDATA%\FishNet`; unreadable files are preserved with affected edits disabled. Profile files are not included in releases. A second FishNet editor cannot open the same profile concurrently; Windows releases its lease after a crash.

## Build and checks

Requires Windows 10 build 19041+ x64 and .NET 10 LTS. Use Visual Studio's Developer PowerShell or the repository's Windows build workflow.

```powershell
msbuild src/FishNet/FishNet.csproj /restore /p:Configuration=Release /p:Platform=x64
dotnet run --project tests/FishNet.MediaTests -c Release
dotnet run --project tests/FishNet.ConversionTests -c Release
```

Release packaging reruns both regression suites and checks the source/tag version. Optional signing uses GitHub secrets `WINDOWS_SIGNING_PFX_BASE64` and `WINDOWS_SIGNING_PASSWORD`, plus variable `WINDOWS_TIMESTAMP_URL` with the provider's HTTPS RFC 3161 endpoint. Credentials are available only to main/tag release packaging. Partial configuration, failed trust verification or missing timestamps fail packaging. No signing credentials are committed.

Tests cover record persistence, corrupt-file preservation, folder boundaries, metadata preservation, duplicate repair targets, interrupted repair rollback, playlists and conversion queue state. Physical audio-output removal, actual codec/playback behavior, screen readers and display scaling remain live Windows acceptance checks. See Microsoft's [MediaPlayer documentation](https://learn.microsoft.com/en-us/windows/apps/develop/media-playback/play-audio-and-video-with-mediaplayer).
