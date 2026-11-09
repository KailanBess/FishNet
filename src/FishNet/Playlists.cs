using System.Text.Json;

namespace FishNet;

public sealed record Playlist(string Id, string Name, List<string> Tracks, SmartPlaylistRule? Rule = null);
public enum PlayHistoryFilter { Any, Unplayed, Played }
public sealed record SmartPlaylistRule(string NameContains, string Folder, PlayHistoryFilter History)
{
    internal bool Valid => NameContains is not null && NameContains.Length <= 100 && Folder is not null &&
        (Folder.Length == 0 || Path.IsPathFullyQualified(Folder)) && Enum.IsDefined(History);

    public IEnumerable<RecentMedia> Match(IEnumerable<RecentMedia> catalog)
    {
        if (!Valid) return [];
        string prefix;
        try { prefix = Folder.Length == 0 ? "" : Path.GetFullPath(Folder); }
        catch (ArgumentException) { return []; }
        catch (NotSupportedException) { return []; }
        if (prefix.Length > 0 && !Path.EndsInDirectorySeparator(prefix)) prefix += Path.DirectorySeparatorChar;
        return catalog.Where(item => item.Kind == "Music" &&
            item.Name.Contains(NameContains, StringComparison.OrdinalIgnoreCase) &&
            item.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            (History == PlayHistoryFilter.Any || (item.OpenedAt == DateTimeOffset.MinValue) == (History == PlayHistoryFilter.Unplayed)));
    }
}
public static class PlaylistQueue
{
    public static int NextIndex(IReadOnlyList<string> tracks, int current, bool repeat, Func<string, bool> exists)
    {
        for (var tried = 0; tried < tracks.Count; tried++)
        {
            current++;
            if (current >= tracks.Count) { if (!repeat) return -1; current = 0; }
            if (exists(tracks[current])) return current;
        }
        return -1;
    }
}
public sealed class Playlists
{
    private readonly string path;
    public Playlists(string? directory = null) => path = Path.Combine(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FishNet"), "playlists.json");
    public List<Playlist> Items { get; private set; } = [];
    public string? Warning { get; private set; }
    public async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(path)) return;
            var loaded = JsonSerializer.Deserialize<List<Playlist>>(await File.ReadAllTextAsync(path)) ?? throw new InvalidDataException("Empty playlist file.");
            if (loaded.Any(p => !Valid(p)) || loaded.Select(p => p.Id).Distinct().Count() != loaded.Count) throw new InvalidDataException("Invalid playlist data.");
            Items = loaded;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException)
        { Warning = "Playlists could not be read and were preserved. Edits are disabled. " + ex.Message; }
    }
    public async Task SaveAsync(List<Playlist> next)
    {
        if (Warning is not null) throw new InvalidOperationException(Warning);
        if (next.Any(p => !Valid(p))) throw new InvalidDataException("Playlists require a name, local MP3 paths or a valid smart rule.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(next, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true); Items = next;
    }
    private static bool Valid(Playlist? playlist) =>
        playlist is not null && !string.IsNullOrWhiteSpace(playlist.Id) && !string.IsNullOrWhiteSpace(playlist.Name) &&
        playlist.Tracks is not null && (playlist.Rule is not null
            ? playlist.Tracks.Count == 0 && playlist.Rule.Valid
            : playlist.Tracks.All(track => Path.IsPathFullyQualified(track) && Path.GetExtension(track).Equals(".mp3", StringComparison.OrdinalIgnoreCase)));
}
