import SwiftUI
import UniformTypeIdentifiers
import AVKit
import AVFoundation
import Combine

@main
@MainActor
struct FishNetApp: App {
    @StateObject private var library = MediaLibrary()
    @StateObject private var playback = Playback()
    @AppStorage("theme") private var theme = "System"

    var body: some Scene {
        WindowGroup {
            MediaShell(library: library, playback: playback)
                .preferredColorScheme(theme == "Dark" ? .dark : theme == "Light" ? .light : nil)
        }
    }
}

struct Media: Codable, Identifiable, Sendable {
    let id: UUID
    let name: String
    let filename: String
    let isVideo: Bool

    var storedName: String {
        id.uuidString + "." + (filename as NSString).pathExtension.lowercased()
    }
}

struct Playlist: Codable, Identifiable {
    let id: UUID
    let name: String
    var mediaIDs: [UUID] = []
}

struct Service: Codable, Identifiable {
    let id: UUID
    let name: String
    let url: URL
}

struct Catalog: Codable {
    var media: [Media] = []
    var playlists: [Playlist] = []
    var services: [Service] = []
}

@MainActor
final class MediaLibrary: ObservableObject {
    @Published private(set) var catalog = Catalog()
    @Published private(set) var loadError: String?
    @Published private(set) var importing = false

    private let root = URL.applicationSupportDirectory.appending(path: "FishNet", directoryHint: .isDirectory)
    private var index: URL { root.appending(path: "library.json") }
    private var storage: URL { root.appending(path: "Media", directoryHint: .isDirectory) }

    init() {
        guard FileManager.default.fileExists(atPath: index.path) else { return }
        do {
            catalog = try JSONDecoder().decode(Catalog.self, from: Data(contentsOf: index))
        } catch {
            loadError = "The saved library could not be opened. Its files remain in place."
        }
    }

    func tracks(in playlist: Playlist) -> [Media] {
        let byID = Dictionary(catalog.media.map { ($0.id, $0) }, uniquingKeysWith: { first, _ in first })
        return playlist.mediaIDs.compactMap { byID[$0] }
    }

    func url(for media: Media) -> URL { storage.appending(path: media.storedName) }

    func addPlaylist(_ name: String) throws {
        let title = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !title.isEmpty else { return }
        try update { $0.playlists.append(Playlist(id: UUID(), name: title)) }
    }

    func toggleTrack(_ media: Media, in id: UUID) throws {
        try update { catalog in
            guard let index = catalog.playlists.firstIndex(where: { $0.id == id }) else { return }
            if catalog.playlists[index].mediaIDs.contains(media.id) {
                catalog.playlists[index].mediaIDs.removeAll { $0 == media.id }
            } else {
                catalog.playlists[index].mediaIDs.append(media.id)
            }
        }
    }

    func deletePlaylist(_ id: UUID) throws {
        try update { $0.playlists.removeAll { $0.id == id } }
    }

    func deleteMedia(_ item: Media) throws {
        try update { catalog in
            catalog.media.removeAll { $0.id == item.id }
            for index in catalog.playlists.indices {
                catalog.playlists[index].mediaIDs.removeAll { $0 == item.id }
            }
        }
        let file = url(for: item)
        if FileManager.default.fileExists(atPath: file.path) { try FileManager.default.removeItem(at: file) }
    }

    func addService(_ name: String, address: String) throws {
        let label = name.trimmingCharacters(in: .whitespacesAndNewlines)
        let cleaned = address.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !label.isEmpty, let url = URL(string: cleaned),
              url.scheme?.lowercased() == "https", let host = url.host, !host.isEmpty,
              url.user == nil, url.password == nil else {
            throw LibraryError.invalidLink
        }
        try update { $0.services.append(Service(id: UUID(), name: label, url: url)) }
    }

    func deleteService(_ id: UUID) throws {
        try update { $0.services.removeAll { $0.id == id } }
    }

    func importFiles(_ urls: [URL]) async -> String? {
        guard !importing, loadError == nil else { return nil }
        importing = true
        defer { importing = false }
        let destination = storage
        var errors = [String]()
        for source in urls {
            do {
                let media = try await Task.detached(priority: .userInitiated) {
                    try await Self.copyPlayableMedia(source, to: destination)
                }.value
                do {
                    try update { $0.media.append(media) }
                } catch {
                    try? FileManager.default.removeItem(at: url(for: media))
                    throw error
                }
            } catch {
                errors.append("\(source.lastPathComponent): \(error.localizedDescription)")
            }
        }
        return errors.isEmpty ? nil : errors.joined(separator: "\n")
    }

    private func update(_ edit: (inout Catalog) -> Void) throws {
        guard loadError == nil else { throw CocoaError(.fileWriteUnknown) }
        var copy = catalog
        edit(&copy)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        try JSONEncoder().encode(copy).write(to: index, options: .atomic)
        catalog = copy
    }

    private nonisolated static func copyPlayableMedia(_ source: URL, to storage: URL) async throws -> Media {
        let access = source.startAccessingSecurityScopedResource()
        defer { if access { source.stopAccessingSecurityScopedResource() } }
        guard !source.pathExtension.isEmpty else { throw LibraryError.unsupportedMedia }
        let id = UUID()
        let filename = source.lastPathComponent
        let target = storage.appending(path: id.uuidString).appendingPathExtension(source.pathExtension.lowercased())
        do {
            try FileManager.default.createDirectory(at: storage, withIntermediateDirectories: true)
            var coordinationError: NSError?
            var copyError: Error?
            NSFileCoordinator().coordinate(readingItemAt: source, options: [], error: &coordinationError) { coordinated in
                do {
                    guard try coordinated.resourceValues(forKeys: [.isRegularFileKey]).isRegularFile == true else {
                        throw LibraryError.unsupportedMedia
                    }
                    try FileManager.default.copyItem(at: coordinated, to: target)
                } catch { copyError = error }
            }
            if let coordinationError { throw coordinationError }
            if let copyError { throw copyError }
            let asset = AVURLAsset(url: target)
            guard try await asset.load(.isPlayable), !(try await asset.load(.hasProtectedContent)) else {
                throw LibraryError.unsupportedMedia
            }
            let videoTracks = try await asset.loadTracks(withMediaType: .video)
            let isVideo = !videoTracks.isEmpty
            let name = source.deletingPathExtension().lastPathComponent
            return Media(id: id, name: name, filename: filename, isVideo: isVideo)
        } catch {
            try? FileManager.default.removeItem(at: target)
            throw error
        }
    }

    private enum LibraryError: LocalizedError {
        case invalidLink, unsupportedMedia
        var errorDescription: String? {
            switch self {
            case .invalidLink: "Enter a name and a valid HTTPS address."
            case .unsupportedMedia: "This device cannot play that file, or the file is protected."
            }
        }
    }
}

@MainActor
final class Playback: ObservableObject {
    let player = AVPlayer()
    @Published private(set) var current: Media?
    private var queue: [Media] = []
    private var position = 0
    private var localURL: ((Media) -> URL)?

    var hasNext: Bool { position + 1 < queue.count }
    var hasPrevious: Bool { position > 0 }
    var isPlaying: Bool { player.timeControlStatus != .paused }
    var elapsed: Double { player.currentTime().seconds }
    var duration: Double { player.currentItem?.duration.seconds ?? .nan }

    func start(_ queue: [Media], at index: Int = 0, url: @escaping (Media) -> URL) throws {
        guard queue.indices.contains(index) else { return }
        try AVAudioSession.sharedInstance().setCategory(.playback)
        try AVAudioSession.sharedInstance().setActive(true)
        self.queue = queue
        localURL = url
        open(index)
    }

    func toggle() {
        if isPlaying { player.pause() }
        else if player.currentItem != nil {
            if duration.isFinite, elapsed >= duration { seek(0) }
            player.play()
        }
    }

    func next() { if hasNext { open(position + 1) } }
    func previous() { if hasPrevious { open(position - 1) } }

    func seek(_ seconds: Double) {
        guard seconds.isFinite, seconds >= 0 else { return }
        player.seek(to: CMTime(seconds: seconds, preferredTimescale: 600))
    }

    func clearIfQueued(_ id: UUID) {
        guard queue.contains(where: { $0.id == id }) else { return }
        player.pause()
        player.replaceCurrentItem(with: nil)
        queue = []
        current = nil
        localURL = nil
        position = 0
        try? AVAudioSession.sharedInstance().setActive(false, options: .notifyOthersOnDeactivation)
    }

    private func open(_ index: Int) {
        guard let localURL else { return }
        let media = queue[index]
        position = index
        player.replaceCurrentItem(with: AVPlayerItem(url: localURL(media)))
        current = media
        player.play()
    }
}

@MainActor
struct MediaShell: View {
    @ObservedObject var library: MediaLibrary
    @ObservedObject var playback: Playback
    @Environment(\.horizontalSizeClass) private var sizeClass
    @Environment(\.colorScheme) private var scheme
    @AppStorage("theme") private var theme = "System"
    @AppStorage("accent") private var accent = "Mint"
    @AppStorage("showCards") private var showCards = true
    @State private var page = Page.library
    @State private var filter = MediaFilter.all
    @State private var search = ""
    @State private var importer = false
    @State private var playerOpen = false
    @State private var newPlaylist = false
    @State private var playlistName = ""
    @State private var newService = false
    @State private var serviceName = ""
    @State private var serviceURL = ""
    @State private var toRemove: Media?
    @State private var issue: String?

    private enum Page: String, CaseIterable {
        case library = "Library", playlists = "Playlists", services = "Services", settings = "Settings"
        var symbol: String {
            switch self {
            case .library: "rectangle.grid.2x2"
            case .playlists: "music.note.list"
            case .services: "link"
            case .settings: "gearshape"
            }
        }
    }

    private enum MediaFilter: String, CaseIterable {
        case all = "All", audio = "Music", video = "Videos"
    }

    private var tint: Color {
        switch accent {
        case "Ocean": .blue
        case "Coral": .orange
        default: scheme == .dark ? Color(red: 0.36, green: 0.88, blue: 0.72) : Color(red: 0.03, green: 0.44, blue: 0.34)
        }
    }

    var body: some View {
        Group {
            if sizeClass == .regular {
                NavigationSplitView {
                    List(Page.allCases, id: \.self) { item in
                        Button { page = item } label: { Label(item.rawValue, systemImage: item.symbol) }
                            .accessibilityAddTraits(page == item ? .isSelected : [])
                    }
                    .navigationTitle("FishNet")
                } detail: {
                    NavigationStack { workspace(page) }.id(page)
                }
            } else {
                TabView(selection: $page) {
                    ForEach(Page.allCases, id: \.self) { item in
                        NavigationStack { workspace(item) }
                            .tabItem { Label(item.rawValue, systemImage: item.symbol) }
                            .tag(item)
                    }
                }
            }
        }
        .tint(tint)
        .fileImporter(isPresented: $importer, allowedContentTypes: [.audio, .movie], allowsMultipleSelection: true) { result in
            switch result {
            case .success(let urls): Task { issue = await library.importFiles(urls) }
            case .failure(let error): issue = error.localizedDescription
            }
        }
        .sheet(isPresented: $playerOpen) { nowPlaying }
        .sheet(isPresented: $newService) { serviceEditor }
        .alert("New playlist", isPresented: $newPlaylist) {
            TextField("Playlist name", text: $playlistName)
            Button("Create") { attempt { try library.addPlaylist(playlistName) } }
            Button("Cancel", role: .cancel) {}
        }
        .alert("FishNet", isPresented: Binding(get: { issue != nil }, set: { if !$0 { issue = nil } })) {
            Button("OK", role: .cancel) { issue = nil }
        } message: { Text(issue ?? "") }
        .confirmationDialog("Remove FishNet’s copy?", isPresented: Binding(
            get: { toRemove != nil }, set: { if !$0 { toRemove = nil } }
        ), presenting: toRemove) { media in
            Button("Remove from Library", role: .destructive) {
                attempt {
                    playback.clearIfQueued(media.id)
                    try library.deleteMedia(media)
                }
                toRemove = nil
            }
        } message: { media in
            Text("\(media.name) will also be removed from FishNet playlists. The original file stays in Files.")
        }
        .onReceive(NotificationCenter.default.publisher(for: AVPlayerItem.didPlayToEndTimeNotification).receive(on: RunLoop.main)) { notice in
            guard let item = notice.object as? AVPlayerItem, item === playback.player.currentItem else { return }
            if playback.hasNext { playback.next() }
        }
        .onReceive(NotificationCenter.default.publisher(for: AVPlayerItem.failedToPlayToEndTimeNotification).receive(on: RunLoop.main)) { notice in
            guard let item = notice.object as? AVPlayerItem, item === playback.player.currentItem else { return }
            issue = (notice.userInfo?[AVPlayerItemFailedToPlayToEndTimeErrorKey] as? Error)?.localizedDescription
                ?? "FishNet couldn’t finish playing this file."
        }
    }

    private func workspace(_ item: Page) -> some View {
        Group {
            switch item {
            case .library: libraryPage
            case .playlists: playlistsPage
            case .services: servicesPage
            case .settings: settingsPage
            }
        }
        .navigationTitle(item.rawValue)
        .safeAreaInset(edge: .bottom, spacing: 0) {
            if let media = playback.current {
                TimelineView(.periodic(from: .now, by: 0.35)) { _ in
                    HStack {
                        Button { playerOpen = true } label: {
                            HStack(spacing: 12) {
                                Image(systemName: media.isVideo ? "film" : "waveform")
                                    .font(.title3).foregroundStyle(tint)
                                VStack(alignment: .leading) {
                                    Text(media.name).font(.subheadline.weight(.semibold)).lineLimit(1)
                                    Text("Now Playing").font(.caption).foregroundStyle(.secondary)
                                }
                                Spacer(minLength: 0)
                            }
                            .frame(minHeight: 44)
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                        Button { playback.toggle() } label: {
                            Image(systemName: playback.isPlaying ? "pause.fill" : "play.fill")
                                .frame(width: 44, height: 44)
                        }
                        .accessibilityLabel(playback.isPlaying ? "Pause" : "Play")
                    }
                    .padding(.horizontal, 16)
                    .padding(.vertical, 4)
                    .background(.regularMaterial)
                }
            }
        }
    }

    private var libraryPage: some View {
        let files = library.catalog.media.filter { media in
            (filter == .all || media.isVideo == (filter == .video))
                && (search.isEmpty || media.name.localizedCaseInsensitiveContains(search))
        }
        return ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                Picker("Media type", selection: $filter) {
                    ForEach(MediaFilter.allCases, id: \.self) { Text($0.rawValue) }
                }
                .pickerStyle(.segmented)
                if let loadError = library.loadError {
                    Label(loadError, systemImage: "exclamationmark.triangle").foregroundStyle(.secondary)
                }
                if library.importing { ProgressView("Importing media…") }
                if files.isEmpty {
                    ContentUnavailableView(
                        search.isEmpty ? "No media here yet" : "No matching media",
                        systemImage: "music.note",
                        description: Text(search.isEmpty ? "Use Add Media to import music or videos from Files." : "")
                    )
                } else {
                    LazyVGrid(columns: showCards ? [GridItem(.adaptive(minimum: 146), spacing: 12)] : [GridItem(.flexible())], spacing: 12) {
                        ForEach(files) { media in
                            Button { play(files, from: media) } label: { mediaCard(media) }
                                .buttonStyle(.plain)
                                .contextMenu {
                                    Button("Remove from Library", role: .destructive) { toRemove = media }
                                }
                                .accessibilityHint("Plays media. More actions include removing this imported copy.")
                        }
                    }
                }
            }
            .padding(16)
        }
        .background(Color(uiColor: .systemGroupedBackground))
        .searchable(text: $search, prompt: "Search media")
        .toolbar {
            ToolbarItemGroup(placement: .topBarTrailing) {
                Button { showCards.toggle() } label: {
                    Label(showCards ? "List view" : "Grid view", systemImage: showCards ? "list.bullet" : "square.grid.2x2")
                }
                Button { importer = true } label: { Label("Add Media", systemImage: "plus") }
                    .disabled(library.importing || library.loadError != nil)
            }
        }
    }

    private func mediaCard(_ media: Media) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Image(systemName: media.isVideo ? "film" : "waveform")
                .font(.title2).foregroundStyle(tint)
            Text(media.name).font(.headline).lineLimit(2)
            Text(media.isVideo ? "Video" : "Music").font(.caption).foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity, minHeight: 80, alignment: .leading)
        .padding(14)
        .background(Color(uiColor: .secondarySystemGroupedBackground), in: RoundedRectangle(cornerRadius: 12))
    }

    private var playlistsPage: some View {
        List {
            if library.catalog.playlists.isEmpty {
                ContentUnavailableView("No playlists", systemImage: "music.note.list",
                                       description: Text("Create a playlist and add media from your library."))
            }
            ForEach(library.catalog.playlists) { list in
                NavigationLink {
                    playlistDetail(list.id)
                } label: {
                    LabeledContent(list.name, value: "\(list.mediaIDs.count)")
                }
                .swipeActions {
                    Button("Delete", role: .destructive) { attempt { try library.deletePlaylist(list.id) } }
                }
            }
        }
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) {
                Button { playlistName = ""; newPlaylist = true } label: { Label("New Playlist", systemImage: "plus") }
                    .disabled(library.loadError != nil)
            }
        }
    }

    @ViewBuilder
    private func playlistDetail(_ id: UUID) -> some View {
        if let list = library.catalog.playlists.first(where: { $0.id == id }) {
            let tracks = library.tracks(in: list)
            List {
                Section("Up next") {
                    if tracks.isEmpty { Text("Add tracks below.").foregroundStyle(.secondary) }
                    ForEach(tracks) { media in
                        Button { play(tracks, from: media) } label: {
                            Label(media.name, systemImage: media.isVideo ? "film" : "music.note")
                        }
                    }
                }
                Section("Manage tracks") {
                    ForEach(library.catalog.media) { media in
                        let included = list.mediaIDs.contains(media.id)
                        Button {
                            attempt { try library.toggleTrack(media, in: id) }
                        } label: {
                            Label(media.name, systemImage: included ? "checkmark.circle.fill" : "plus.circle")
                        }
                        .accessibilityHint(included ? "Removes from playlist" : "Adds to playlist")
                    }
                }
            }
            .navigationTitle(list.name)
            .navigationBarTitleDisplayMode(.inline)
        }
    }

    private var servicesPage: some View {
        List {
            Section("Media services") {
                Link(destination: URL(string: "https://open.spotify.com")!) { Label("Spotify", systemImage: "music.note") }
                Link(destination: URL(string: "https://music.apple.com")!) { Label("Apple Music", systemImage: "music.note") }
                Link(destination: URL(string: "https://www.youtube.com")!) { Label("YouTube", systemImage: "play.rectangle") }
            }
            Section("Your links") {
                if library.catalog.services.isEmpty {
                    Text("Add an HTTPS link to a service you use.").foregroundStyle(.secondary)
                }
                ForEach(library.catalog.services) { item in
                    Link(destination: item.url) { Label(item.name, systemImage: "arrow.up.right.square") }
                        .swipeActions {
                            Button("Remove", role: .destructive) { attempt { try library.deleteService(item.id) } }
                        }
                }
            }
        }
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) {
                Button { serviceName = ""; serviceURL = ""; newService = true } label: {
                    Label("Add Link", systemImage: "plus")
                }
                .disabled(library.loadError != nil)
            }
        }
    }

    private var serviceEditor: some View {
        NavigationStack {
            Form {
                TextField("Name", text: $serviceName)
                TextField("HTTPS address", text: $serviceURL)
                    .keyboardType(.URL)
                    .textInputAutocapitalization(.never)
                    .autocorrectionDisabled()
            }
            .navigationTitle("Add service")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Cancel") { newService = false } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Save") {
                        newService = false
                        attempt { try library.addService(serviceName, address: serviceURL) }
                    }
                    .disabled(serviceName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || serviceURL.isEmpty)
                }
            }
        }
    }

    private var settingsPage: some View {
        Form {
            Section("Appearance") {
                Picker("Theme", selection: $theme) {
                    ForEach(["System", "Dark", "Light"], id: \.self) { Text($0) }
                }
                Picker("Accent", selection: $accent) {
                    ForEach(["Mint", "Ocean", "Coral"], id: \.self) { Text($0) }
                }
                Toggle("Show media cards", isOn: $showCards)
            }
            Section("Local library") {
                LabeledContent("Media files", value: "\(library.catalog.media.count)")
                LabeledContent("Playlists", value: "\(library.catalog.playlists.count)")
                Text("Imported copies and playlists stay on this device. Service links open through iOS.")
                    .foregroundStyle(.secondary)
            }
        }
    }

    private var nowPlaying: some View {
        NavigationStack {
            ScrollView {
                if let media = playback.current {
                    VStack(spacing: 20) {
                        if media.isVideo {
                            VideoPlayer(player: playback.player)
                                .aspectRatio(16.0 / 9.0, contentMode: .fit)
                                .clipShape(RoundedRectangle(cornerRadius: 12))
                        } else {
                            Image(systemName: "waveform")
                                .font(.system(size: 64))
                                .foregroundStyle(tint)
                                .frame(maxWidth: .infinity, minHeight: 160)
                                .background(tint.opacity(0.12), in: RoundedRectangle(cornerRadius: 14))
                        }
                        Text(media.name).font(.title2.weight(.semibold))
                        TimelineView(.periodic(from: .now, by: 0.35)) { _ in
                            transport
                        }
                    }
                    .padding(20)
                } else {
                    ContentUnavailableView("Nothing is playing", systemImage: "music.note")
                }
            }
            .navigationTitle("Now Playing")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .confirmationAction) { Button("Done") { playerOpen = false } } }
        }
    }

    private var transport: some View {
        VStack(spacing: 12) {
            if playback.duration.isFinite, playback.duration > 0 {
                Slider(value: Binding(get: {
                    playback.elapsed.isFinite ? max(0, min(playback.duration, playback.elapsed)) : 0
                }, set: playback.seek), in: 0...playback.duration)
                .accessibilityLabel("Playback position")
            }
            if playback.player.currentItem?.status == .failed {
                Text(playback.player.currentItem?.error?.localizedDescription ?? "This file couldn’t be played.")
                    .foregroundStyle(.secondary)
            }
            HStack(spacing: 24) {
                Button(action: playback.previous) { Image(systemName: "backward.fill").frame(width: 44, height: 44) }
                    .accessibilityLabel("Previous track").disabled(!playback.hasPrevious)
                Button(action: playback.toggle) {
                    Image(systemName: playback.isPlaying ? "pause.fill" : "play.fill").frame(width: 44, height: 44)
                }
                .accessibilityLabel(playback.isPlaying ? "Pause" : "Play")
                Button(action: playback.next) { Image(systemName: "forward.fill").frame(width: 44, height: 44) }
                    .accessibilityLabel("Next track").disabled(!playback.hasNext)
            }
            .font(.title2)
        }
    }

    private func play(_ files: [Media], from media: Media) {
        guard let index = files.firstIndex(where: { $0.id == media.id }) else { return }
        attempt {
            try playback.start(files, at: index, url: { library.url(for: $0) })
            if media.isVideo { playerOpen = true }
        }
    }

    private func attempt(_ operation: () throws -> Void) {
        do { try operation() }
        catch { issue = error.localizedDescription }
    }
}

