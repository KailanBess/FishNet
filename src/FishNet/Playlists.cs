using System.Text.Json;

namespace FishNet;

public sealed record Playlist(string Id, string Name, List<string> Tracks);
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
            if (loaded.Any(p => p is null || string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.Name) || p.Tracks is null || p.Tracks.Any(t => !Path.IsPathFullyQualified(t) || !Path.GetExtension(t).Equals(".mp3", StringComparison.OrdinalIgnoreCase))) || loaded.Select(p => p.Id).Distinct().Count() != loaded.Count) throw new InvalidDataException("Invalid playlist data.");
            Items = loaded;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException)
        { Warning = "Playlists could not be read and were preserved. Edits are disabled. " + ex.Message; }
    }
    public async Task SaveAsync(List<Playlist> next)
    {
        if (Warning is not null) throw new InvalidOperationException(Warning);
        if (next.Any(p => string.IsNullOrWhiteSpace(p.Name) || p.Tracks.Any(t => !Path.IsPathFullyQualified(t) || !Path.GetExtension(t).Equals(".mp3", StringComparison.OrdinalIgnoreCase)))) throw new InvalidDataException("Playlists require a name and local MP3 paths.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(next, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true); Items = next;
    }
}
