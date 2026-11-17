# FishNet for iPhone and iPad

FishNet is a native SwiftUI media library for iOS and iPadOS 17 and later. iPhone uses bottom tabs. iPad uses a sidebar. The Windows and iOS projects build independently.

## Install a test build using Windows

1. Sign in to GitHub and open the successful **iOS test builds** run linked from the iOS pull request. Under **Artifacts**, download **FishNet-iOS-device** and unzip it to find the IPA and its SHA-256 checksum.
2. Follow the [AltStore Classic Windows setup](https://faq.altstore.io/altstore-classic/how-to-install-altstore-windows) to install AltStore on your iPhone or iPad. A free Apple Account works for personal signing.
3. Transfer **FishNet-unsigned.ipa** to Files. In **AltStore → My Apps**, tap **+** and select the IPA while AltServer is reachable from your phone.

This package targets physical iOS and iPadOS 17+ devices. AltStore signs it for your device. With a free account, refresh the app before its seven-day signing period expires.

## Build and run

1. On a Mac, open `ios/FishNet.xcodeproj` in Xcode 16 or later.
2. Select the **FishNet** scheme and an iPhone or iPad simulator, then press **Run**.
3. On a physical device, select an Apple developer team and a bundle identifier your team can sign.

From the repo root, build an unsigned simulator app with:

```sh
xcodebuild -project ios/FishNet.xcodeproj -scheme FishNet -destination 'generic/platform=iOS Simulator' CODE_SIGNING_ALLOWED=NO build
```

## What to verify

- Import audio and video through **Library → Add Media**. FishNet copies playable files into its app sandbox and leaves the source untouched. Codec support comes from AVFoundation.
- Start an audio track, open **Now Playing**, seek, and test play/pause and previous/next. Play a video and check the native video controls.
- Create a playlist, add or remove tracks, start playback from a track, and check that playback advances in order. Restart the app and verify imported items, playlists, services, and appearance settings persist.
- Open a built-in or custom service link. Links are opened through iOS and may open in the associated service app or browser.
- Check iPhone and iPad layouts, landscape, and larger Dynamic Type.

This preview has local playback and storage. Desktop-library sync, media conversion, provider account integrations, and background playback are outside this version. The app includes no bundled browser.
