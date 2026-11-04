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
        var audio = new[] { ".mp3", ".m4a", ".wav", ".flac", ".wma" }.Contains(System.IO.Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);
        var next = Items.Where(i => !i.Path.Equals(file, StringComparison.OrdinalIgnoreCase)).ToList();
        next.Insert(0, new RecentMedia(file, System.IO.Path.GetFileNameWithoutExtension(file), audio ? "Music" : "Videos", DateTimeOffset.Now));
        next = next.Take(100).ToList();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(next, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true); Items = next;
    }
}
