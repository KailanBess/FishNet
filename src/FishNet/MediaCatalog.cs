using System.Text.Json;

namespace FishNet;

public sealed record RecentMedia(string Path, string Name, string Kind, DateTimeOffset OpenedAt);
public sealed class MediaCatalog
{
    private readonly string path;
    public MediaCatalog(string? directory = null) => path = System.IO.Path.Combine(directory ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FishNet"), "media-library.json");
    public List<RecentMedia> Items { get; private set; } = [];
    public string? Warning { get; private set; }
    public async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(path)) return;
            Items = JsonSerializer.Deserialize<List<RecentMedia>>(await File.ReadAllTextAsync(path)) ?? throw new InvalidDataException("Invalid media library.");
            if (Items.Any(i => i is null || string.IsNullOrWhiteSpace(i.Path) || string.IsNullOrWhiteSpace(i.Name))) throw new InvalidDataException("Invalid media library entry.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { Items = []; Warning = "The media library could not be read and was preserved. " + ex.Message; }
    }
    public async Task RememberAsync(string file)
    {
        if (Warning is not null) return;
        var audio = IsAudio(file);
        var next = Items.Where(i => !i.Path.Equals(file, StringComparison.OrdinalIgnoreCase)).ToList();
        next.Insert(0, new RecentMedia(file, System.IO.Path.GetFileNameWithoutExtension(file), audio ? "Music" : "Videos", DateTimeOffset.Now));
        await SaveAsync(next);
    }
    public async Task ImportAsync(IEnumerable<string> paths)
    {
        if (Warning is not null) throw new InvalidOperationException(Warning);
        var next = Items.ToList(); var known = next.Select(i => i.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in paths)
            if (known.Add(file)) next.Add(new(file, System.IO.Path.GetFileNameWithoutExtension(file), IsAudio(file) ? "Music" : "Videos", DateTimeOffset.MinValue));
        if (next.Count > 10000) throw new InvalidOperationException("The library limit is 10,000 files.");
        await SaveAsync(next);
    }
    public async Task RelinkAsync(string previous, string replacement)
    {
        if (Warning is not null) throw new InvalidOperationException(Warning);
        await SaveAsync(Items.Where(i => !i.Path.Equals(replacement, StringComparison.OrdinalIgnoreCase)).Select(i => i.Path.Equals(previous, StringComparison.OrdinalIgnoreCase) ? i with { Path = replacement, Name = System.IO.Path.GetFileNameWithoutExtension(replacement), Kind = IsAudio(replacement) ? "Music" : "Videos" } : i).ToList());
    }
    public static bool IsAudio(string file) => new[] { ".mp3", ".m4a", ".wav", ".flac", ".wma", ".aac", ".ogg" }.Contains(System.IO.Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);
    private async Task SaveAsync(List<RecentMedia> next)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(next, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true); Items = next;
    }
}

